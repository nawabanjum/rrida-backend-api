using System.Runtime.CompilerServices;
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
    /// MVP 0729864, item 5: an answer written to one prompt is not an answer to another. Ours keeps
    /// stored history (a manager has read it), so a prompt change rewrites only the team's last
    /// <see cref="EranNarratorService.PromptRefreshDays"/> feed days, never a team note, never the
    /// fixed card, and never a row written before the fingerprint existed.
    /// </summary>
    public class EranPromptFingerprintTests
    {
        private const uint Unit = 5151;
        private const string Old = "Most of the team said the handoffs keep stalling, as written before.";
        private const string New = "Most of the team said the handoffs keep stalling.";
        private const string OlderPrompt = "0123456789ab";
        private const string WeeklyPayload = """{"state_before":"Drift","state_after":"Drift","changed":false}""";
        private static readonly Lazy<EngineContext> Ctx = new(() =>
            EngineContext.FromKit(Path.Combine(EngineRoot(), "kit", "data")));

        [Fact]
        public void The_fingerprint_is_twelve_hex_stable_and_moves_with_any_part_of_the_prompt()
        {
            var a = EranNarratorService.FingerprintOf("preamble", "tasks");
            Assert.Matches("^[0-9a-f]{12}$", a);
            Assert.Equal(a, EranNarratorService.FingerprintOf("preamble", "tasks"));
            Assert.NotEqual(a, EranNarratorService.FingerprintOf("preamble", "tasks."));
            Assert.NotEqual(a, EranNarratorService.FingerprintOf("preamble.", "tasks"));
            Assert.Matches("^[0-9a-f]{12}$", EranNarratorService.PromptFingerprint);
        }

        [Fact]
        public async Task A_message_Eran_writes_is_stamped_with_the_current_prompt()
        {
            await using var db = NewDb();
            Seed(db, Feed("19.w", 19, "weekly"));

            await Narrator(db, () => New).NarrateAsync(Unit, 14);

            var item = db.EngineFeedItems.Single();
            Assert.Equal(New, item.Prose);
            Assert.Equal(EranNarratorService.PromptFingerprint, item.PromptFingerprint);
        }

        [Fact]
        public async Task A_prompt_change_rewrites_the_recent_days_and_leaves_older_history_as_read()
        {
            await using var db = NewDb();
            var items = new List<EngineFeedItem>();
            for (var day = 14; day <= 20; day += 1)
                items.Add(Stored(Feed($"{day}.w", day, "weekly"), OlderPrompt));
            Seed(db, items.ToArray());

            var calls = 0;
            await Narrator(db, () => { calls += 1; return New; }).NarrateAsync(Unit, 14);

            Assert.Equal(EranNarratorService.PromptRefreshDays, calls);
            foreach (var day in new[] { 16, 17, 18, 19, 20 })
            {
                var item = db.EngineFeedItems.Single(f => f.Day == day);
                Assert.Equal(New, item.Prose);
                Assert.Equal(EranNarratorService.PromptFingerprint, item.PromptFingerprint);
            }
            foreach (var day in new[] { 14, 15 })
            {
                var item = db.EngineFeedItems.Single(f => f.Day == day);
                Assert.Equal(Old, item.Prose);
                Assert.Equal(OlderPrompt, item.PromptFingerprint);
            }
        }

        [Fact]
        public async Task Rows_written_before_the_fingerprint_existed_are_never_rewritten()
        {
            await using var db = NewDb();
            Seed(db, Stored(Feed("19.w", 19, "weekly"), null), Stored(Feed("20.w", 20, "weekly"), null));

            var calls = 0;
            await Narrator(db, () => { calls += 1; return New; }).NarrateAsync(Unit, 14);

            Assert.Equal(0, calls);
            Assert.All(db.EngineFeedItems, f => Assert.Equal(Old, f.Prose));
            Assert.All(db.EngineFeedItems, f => Assert.Null(f.PromptFingerprint));
        }

        [Fact]
        public async Task A_message_already_under_the_current_prompt_is_not_written_again()
        {
            await using var db = NewDb();
            Seed(db, Stored(Feed("20.w", 20, "weekly"), EranNarratorService.PromptFingerprint));

            var calls = 0;
            await Narrator(db, () => { calls += 1; return New; }).NarrateAsync(Unit, 14);

            Assert.Equal(0, calls);
            Assert.Equal(Old, db.EngineFeedItems.Single().Prose);
        }

        [Fact]
        public async Task A_team_note_is_never_retired_because_writing_it_again_reissues_it()
        {
            await using var db = NewDb();
            var note = Stored(Feed("20.n", 20, "note"), OlderPrompt);
            note.CycleKey = "C1";
            Seed(db, note);

            var calls = 0;
            await Narrator(db, () => { calls += 1; return New; }).NarrateAsync(Unit, 14);

            Assert.Equal(0, calls);
            var item = db.EngineFeedItems.Single();
            Assert.Equal(Old, item.Prose);
            Assert.Equal(OlderPrompt, item.PromptFingerprint);
        }

        [Fact]
        public async Task The_fixed_card_carries_no_fingerprint_and_is_not_retired()
        {
            await using var db = NewDb();
            var card = Feed("20.w", 20, "weekly");
            card.Prose = EranGuardFallback.ForFeedItem("weekly", null, WeeklyPayload);
            card.GuardOutcome = EranGuardLog.Fallback;
            card.PromptFingerprint = OlderPrompt;
            Seed(db, card);

            var calls = 0;
            await Narrator(db, () => { calls += 1; return New; }).NarrateAsync(Unit, 14);

            Assert.Equal(0, calls);
            Assert.Equal(EranGuardLog.Fallback, db.EngineFeedItems.Single().GuardOutcome);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static EranNarratorService Narrator(ApplicationDbContext db, Func<string> answer)
        {
            var client = new Mock<IEranCoachAnthropicClient>();
            client.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ModelConfiguration>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
                .ReturnsAsync(() => answer());
            var models = new Mock<IDocumentEmbeddingRepository>();
            models.Setup(m => m.GetAllModelsAsync(It.IsAny<int?>()))
                .ReturnsAsync([new ModelConfiguration { ModelName = "claude-sonnet-test", IsSelected = true }]);
            var engine = new Mock<IRridaEngineContextFactory>();
            engine.Setup(e => e.Context).Returns(Ctx.Value);
            return new EranNarratorService(db, client.Object, models.Object, new EranEngineContextBuilder(db, engine.Object),
                NullLogger<EranNarratorService>.Instance);
        }

        private static ApplicationDbContext NewDb() =>
            new TestApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"prompt-fingerprint-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static void Seed(ApplicationDbContext db, params EngineFeedItem[] items)
        {
            db.EngineTeamSettings.Add(new EngineTeamSettings
            {
                BusinessUnitId = Unit, StartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30),
                WorkingWeek = "mon-fri", IsEnabled = true
            });
            db.EngineFeedItems.AddRange(items);
            db.SaveChanges();
        }

        private static EngineFeedItem Stored(EngineFeedItem item, string? fingerprint)
        {
            item.Prose = Old;
            item.GuardOutcome = EranGuardLog.Passed;
            item.PromptFingerprint = fingerprint;
            return item;
        }

        private static EngineFeedItem Feed(string key, int day, string kind) => new()
        {
            BusinessUnitId = Unit, FeedKey = key, Day = day, Kind = kind, PayloadJson = WeeklyPayload,
            WrittenAt = DateTime.UtcNow.AddMinutes(-1)
        };

        private static string EngineRoot([CallerFilePath] string source = "")
        {
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(source)!); dir is not null; dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, "external", "RRIDA-Engine")))
                    return Path.Combine(dir.FullName, "external", "RRIDA-Engine");
            throw new DirectoryNotFoundException("repository root not found");
        }
    }
}
