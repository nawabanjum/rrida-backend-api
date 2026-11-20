using System.Runtime.CompilerServices;
using System.Text.Json;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Rrida.Engine;
using Xunit;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>
    /// Eran on the engine's feed, held to the MVP's prompt shape (src/eran/prompts.js): the
    /// preamble and a context block as the system prompt, the payload as INPUT followed by
    /// the task for its kind, the daily message in five parts, newest first, three attempts.
    /// </summary>
    public class EranNarratorServiceTests
    {
        private const uint Unit = 4242;
        private static readonly Lazy<EngineContext> Ctx = new(() =>
            EngineContext.FromKit(Path.Combine(EngineRoot(), "kit", "data")));

        [Fact]
        public async Task Daily_message_is_built_like_the_MVP_and_stored_in_parts()
        {
            await using var db = NewDb();
            Seed(db, Feed("12.d", 12, "daily", """{"situation":"action_first_day","team_state":"Drift","action_text":"Ask about handoffs","route":"team"}"""));
            string? system = null, user = null;
            var narrator = Narrator(db, (s, u) => { system = s; user = u; return """{"headline":"Handoffs are stalling.","body":"It kept coming up.","outward":"Approvals wait on another team.","one_thing":"Respond to it.","say_this":"I heard you.","what_tells_you":"Fewer waits."}"""; });

            var written = await narrator.NarrateAsync(Unit, EranNarratorService.DefaultBatch);

            Assert.Equal(1, written);
            Assert.StartsWith("You are Eran, the coaching layer of RRIDA.", system);
            Assert.Contains("Context you are given:\n", system);
            Assert.Contains("  team_state: Drift", system);
            Assert.Contains("  cycle_position: nothing is running", system);

            Assert.StartsWith("INPUT\n  situation: action_first_day\n  team_state: Drift", user);
            Assert.Contains("TASK\n  Write today's message to this manager.", user);
            // D36 (MVP c36935d): six fields, outward after body.
            Assert.Contains("\"headline\", \"body\", \"outward\", \"one_thing\", \"say_this\", \"what_tells_you\"", user);

            var prose = JsonDocument.Parse(db.EngineFeedItems.Single().Prose!).RootElement;
            Assert.Equal("Handoffs are stalling.", prose.GetProperty("headline").GetString());
            Assert.Equal("Approvals wait on another team.", prose.GetProperty("outward").GetString());
            Assert.Equal("I heard you.", prose.GetProperty("say_this").GetString());
        }

        [Fact]
        public async Task Other_kinds_use_their_own_MVP_task_and_stay_prose()
        {
            await using var db = NewDb();
            Seed(db, Feed("19.w", 19, "weekly", """{"state_before":"Cruise","state_after":"Drift","changed":true}"""));
            string? user = null;
            var narrator = Narrator(db, (_, u) => { user = u; return "The team moved into Drift this week."; });

            await narrator.NarrateAsync(Unit, EranNarratorService.DefaultBatch);

            Assert.Contains("TASK\n  Three to five sentences.", user);
            Assert.DoesNotContain("what_tells_you", user);
            Assert.Equal("The team moved into Drift this week.", db.EngineFeedItems.Single().Prose);
        }

        [Fact]
        public async Task Newest_first_in_batches_and_three_attempts_at_most()
        {
            await using var db = NewDb();
            var old = Feed("3.d", 3, "daily", """{"situation":"calibrating"}""");
            var failing = Feed("9.d", 9, "daily", """{"situation":"calibrating"}""");
            Seed(db, old, Feed("8.d", 8, "daily", """{"situation":"calibrating"}"""), failing);
            for (var i = 0; i < 3; i += 1)
                db.EngineLlmCalls.Add(new EngineLlmCall { BusinessUnitId = Unit, FeedKey = "9.d", PromptType = "daily", Day = 9, SystemPrompt = "", UserContent = "", Error = "boom", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();

            var narrator = Narrator(db, (_, _) => """{"headline":"h","body":"b","one_thing":"","say_this":"","what_tells_you":""}""");

            var written = await narrator.NarrateAsync(Unit, take: 1);

            // Day 9 has used its three attempts, so it gets Eran's fixed card instead of a model
            // call, and the newest the batch of one phrases is day 8.
            Assert.Equal(2, written);
            Assert.NotNull(db.EngineFeedItems.Single(f => f.FeedKey == "8.d").Prose);
            Assert.Null(db.EngineFeedItems.Single(f => f.FeedKey == "3.d").Prose);
            var card = db.EngineFeedItems.Single(f => f.FeedKey == "9.d");
            Assert.Equal(EranGuardLog.Fallback, card.GuardOutcome);
            Assert.Equal(EranGuardFallback.ForFeedItem("daily", null, card.PayloadJson), card.Prose);
        }

        [Fact]
        public async Task An_empty_answer_is_a_failure_and_the_item_waits()
        {
            await using var db = NewDb();
            Seed(db, Feed("5.d", 5, "daily", """{"situation":"calibrating"}"""));
            var narrator = Narrator(db, (_, _) => "   ");

            Assert.Equal(0, await narrator.NarrateAsync(Unit, EranNarratorService.DefaultBatch));
            Assert.Null(db.EngineFeedItems.Single().Prose);
            Assert.NotNull(db.EngineLlmCalls.Single().Error);
        }

        [Theory]
        [InlineData("```json\n{\"headline\":\"H\",\"body\":\"B\",\"one_thing\":\"\",\"say_this\":\"\",\"what_tells_you\":\"\"}\n```", "H", "B")]
        [InlineData("Just a paragraph of prose.", "", "Just a paragraph of prose.")]
        public void Daily_answer_is_parsed_with_the_MVP_fallback(string response, string headline, string body)
        {
            var parts = JsonDocument.Parse(EranNarratorService.NormaliseDaily(response)).RootElement;
            Assert.Equal(headline, parts.GetProperty("headline").GetString());
            Assert.Equal(body, parts.GetProperty("body").GetString());
            Assert.Equal("", parts.GetProperty("outward").GetString());   // absent or prose: empty, never missing
            Assert.Equal("", parts.GetProperty("one_thing").GetString());
        }

        [Fact]
        public void Input_is_rendered_as_the_MVP_renders_it()
        {
            var input = EranNarratorService.RenderInput("""{"a":"x","b":null,"c":["p","q"],"d":{"e":1},"f":true}""");
            Assert.Equal("INPUT\n  a: x\n  c: p; q\n  d: e=1\n  f: true", input);
        }

        [Fact]
        public void Every_MVP_task_kind_is_present()
        {
            var tasks = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(Path.Combine(ApiRoot(), "CloverEra", "Prompts", "rrida-eran-tasks.json")))!;
            Assert.Equal(
                new[] { "cross_team", "cross_team_note", "daily", "escalation", "monthly", "note", "outcome", "weekly" },
                tasks.Keys.OrderBy(k => k, StringComparer.Ordinal));
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static ApplicationDbContext NewDb() =>
            new TestApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"narrator-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static void Seed(ApplicationDbContext db, params EngineFeedItem[] items)
        {
            db.EngineTeamSettings.Add(new EngineTeamSettings
            {
                BusinessUnitId = Unit, StartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-20),
                WorkingWeek = "mon-fri", IsEnabled = true
            });
            db.EngineFeedItems.AddRange(items);
            db.SaveChanges();
        }

        private static EngineFeedItem Feed(string key, int day, string kind, string payload) => new()
        {
            BusinessUnitId = Unit, FeedKey = key, Day = day, Kind = kind, PayloadJson = payload,
            WrittenAt = DateTime.UtcNow.AddMinutes(-1)
        };

        [Fact]
        public async Task Calls_use_the_MVP_budget_and_a_cut_off_answer_is_a_failure()
        {
            await using var db = NewDb();
            Seed(db, Feed("5.w", 5, "weekly", "{}"));
            var client = new Mock<IEranCoachAnthropicClient>();
            client.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ModelConfiguration>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
                .ReturnsAsync("The week was steady.");

            await Narrator(db, client).NarrateAsync(Unit, 14);

            // src/eran/client.js callModel: max_tokens 8000, and a response that hit it (or a
            // refusal) is thrown rather than kept.
            client.Verify(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ModelConfiguration>(), 8000,
                It.IsAny<CancellationToken>(), true), Times.Once);
        }

        private static EranNarratorService Narrator(ApplicationDbContext db, Func<string, string, string> answer)
        {
            var client = new Mock<IEranCoachAnthropicClient>();
            client.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ModelConfiguration>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
                .ReturnsAsync((string s, string u, ModelConfiguration _, int _, CancellationToken _, bool _) => answer(s, u));
            return Narrator(db, client);
        }

        private static EranNarratorService Narrator(ApplicationDbContext db, Mock<IEranCoachAnthropicClient> client)
        {
            var models = new Mock<IDocumentEmbeddingRepository>();
            models.Setup(m => m.GetAllModelsAsync(It.IsAny<int?>()))
                .ReturnsAsync([new ModelConfiguration { ModelName = "claude-sonnet-test", IsSelected = true }]);
            var engine = new Mock<IRridaEngineContextFactory>();
            engine.Setup(e => e.Context).Returns(Ctx.Value);
            return new EranNarratorService(db, client.Object, models.Object, new EranEngineContextBuilder(db, engine.Object), NullLogger<EranNarratorService>.Instance);
        }

        private static string EngineRoot([CallerFilePath] string source = "") =>
            Path.Combine(RepoRoot(source), "external", "RRIDA-Engine");

        private static string ApiRoot([CallerFilePath] string source = "") =>
            Path.Combine(RepoRoot(source), "CloverBackoffice.API");

        private static string RepoRoot(string source)
        {
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(source)!); dir is not null; dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, "external", "RRIDA-Engine"))) return dir.FullName;
            throw new DirectoryNotFoundException("repository root not found");
        }
    }
}
