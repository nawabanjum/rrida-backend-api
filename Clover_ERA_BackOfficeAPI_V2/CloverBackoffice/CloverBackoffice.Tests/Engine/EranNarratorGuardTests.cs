using System.Runtime.CompilerServices;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using Rrida.Engine;
using Xunit;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>
    /// The guard on Eran's feed messages (the brief, sections 2 and 6): one retry, then the fixed
    /// card; stored wording checked again; the card after spent attempts; the nightly retry; and a
    /// log that never carries what Eran wrote.
    /// </summary>
    public class EranNarratorGuardTests
    {
        private const uint Unit = 4343;
        private const string Bad = "Two people said the handoffs keep stalling.";
        private const string Clean = "Most of the team said the handoffs keep stalling.";
        private const string WeeklyPayload = """{"state_before":"Drift","state_after":"Drift","changed":false}""";
        private static readonly Lazy<EngineContext> Ctx = new(() =>
            EngineContext.FromKit(Path.Combine(EngineRoot(), "kit", "data")));

        [Fact]
        public async Task A_breach_is_retried_once_with_the_rule_labels_and_the_clean_reply_is_kept()
        {
            await using var db = NewDb();
            Seed(db, Feed("19.w", 19, "weekly", WeeklyPayload));
            var users = new List<string>();
            var replies = new Queue<string>([Bad, Clean]);
            var log = new ListLogger();

            Assert.Equal(1, await Narrator(db, (_, u) => { users.Add(u); return replies.Dequeue(); }, log).NarrateAsync(Unit, 14));

            var item = db.EngineFeedItems.Single();
            Assert.Equal(Clean, item.Prose);
            Assert.Equal(EranGuardLog.PassedOnRetry, item.GuardOutcome);
            Assert.Equal(2, users.Count);
            Assert.Contains("Your last reply broke these rules: a count of people.", users[1]);
            Assert.DoesNotContain("Two people", users[1]);

            var calls = db.EngineLlmCalls.OrderBy(c => c.Id).ToList();
            Assert.Equal(new[] { EranGuardLog.Retried, EranGuardLog.PassedOnRetry }, calls.Select(c => c.GuardOutcome));
            Assert.Equal("a count of people", calls[0].GuardRules);
            Assert.Contains(log.Lines, l => l.Contains("outcome=retried") && l.Contains("rules=[a count of people]"));
        }

        [Fact]
        public async Task Two_breaches_deliver_the_fixed_card_and_nothing_Eran_wrote()
        {
            await using var db = NewDb();
            Seed(db, Feed("19.w", 19, "weekly", WeeklyPayload));
            var log = new ListLogger();

            await Narrator(db, (_, _) => Bad, log).NarrateAsync(Unit, 14);

            var item = db.EngineFeedItems.Single();
            Assert.Equal(EranGuardLog.Fallback, item.GuardOutcome);
            Assert.Equal(EranGuardFallback.ForFeedItem("weekly", null, WeeklyPayload), item.Prose);
            Assert.DoesNotContain("Two people", item.Prose);
            Assert.Equal(2, db.EngineLlmCalls.Count());

            // Acceptance 5: one event per catch, with no reply, payload or matched words in it.
            Assert.Contains(log.Lines, l => l.Contains("outcome=fallback"));
            Assert.DoesNotContain(log.Lines, l => l.Contains("Two people") || l.Contains("handoffs") || l.Contains(item.Prose!));
        }

        [Fact]
        public async Task A_daily_answered_in_prose_is_retried_for_the_five_parts()
        {
            await using var db = NewDb();
            const string payload = """{"situation":"calibrating","team_state":null}""";
            Seed(db, Feed("4.d", 4, "daily", payload));
            var replies = new Queue<string>(["The first week is still settling.",
                """{"headline":"Still settling.","body":"The first readings are coming in.","one_thing":"","say_this":"","what_tells_you":""}"""]);

            await Narrator(db, (_, _) => replies.Dequeue()).NarrateAsync(Unit, 14);

            var item = db.EngineFeedItems.Single();
            Assert.Equal(EranGuardLog.PassedOnRetry, item.GuardOutcome);
            Assert.Contains("Still settling.", item.Prose);
        }

        [Fact]
        public async Task Stored_wording_that_breaks_a_rule_is_cleared_and_written_again()
        {
            await using var db = NewDb();
            var stored = Feed("19.w", 19, "weekly", WeeklyPayload);
            stored.Prose = Bad;
            var fine = Feed("12.w", 12, "weekly", WeeklyPayload);
            fine.Prose = Clean;
            Seed(db, stored, fine);
            var log = new ListLogger();

            await Narrator(db, (_, _) => Clean, log).NarrateAsync(Unit, 14);

            Assert.Equal(Clean, db.EngineFeedItems.Single(f => f.FeedKey == "19.w").Prose);
            Assert.Equal(EranGuardLog.Passed, db.EngineFeedItems.Single(f => f.FeedKey == "12.w").GuardOutcome);
            Assert.Contains(log.Lines, l => l.Contains("outcome=cache_evicted"));
            Assert.Single(db.EngineLlmCalls);
        }

        [Fact]
        public async Task Spent_attempts_get_the_card_and_the_nightly_run_tries_once_more()
        {
            await using var db = NewDb();
            Seed(db, Feed("19.w", 19, "weekly", WeeklyPayload));
            for (var i = 0; i < EranNarratorService.MaxAttempts; i += 1)
                db.EngineLlmCalls.Add(new EngineLlmCall { BusinessUnitId = Unit, FeedKey = "19.w", PromptType = "weekly", Day = 19, SystemPrompt = "", UserContent = "", Error = "boom", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();

            var calls = 0;
            var narrator = Narrator(db, (_, _) => { calls += 1; return Clean; });
            await narrator.NarrateAsync(Unit, 14);

            var item = db.EngineFeedItems.Single();
            Assert.Equal(0, calls);
            Assert.Equal(EranGuardLog.Fallback, item.GuardOutcome);
            Assert.NotNull(item.Prose);

            Assert.Equal(1, await narrator.RetryFallbacksAsync(Unit));
            Assert.Equal(Clean, item.Prose);
            Assert.Equal(EranGuardLog.Passed, item.GuardOutcome);
        }

        [Fact]
        public async Task A_card_that_fails_its_nightly_retry_stays_for_good()
        {
            await using var db = NewDb();
            var card = Feed("19.w", 19, "weekly", WeeklyPayload);
            card.Prose = EranGuardFallback.ForFeedItem("weekly", null, WeeklyPayload);
            card.GuardOutcome = EranGuardLog.Fallback;
            Seed(db, card);
            var narrator = Narrator(db, (_, _) => Bad);

            Assert.Equal(0, await narrator.RetryFallbacksAsync(Unit));
            var item = db.EngineFeedItems.Single();
            Assert.Equal(EranGuardLog.FallbackFinal, item.GuardOutcome);
            Assert.DoesNotContain("Two people", item.Prose);

            // Final: the next night leaves it alone.
            Assert.Equal(0, await narrator.RetryFallbacksAsync(Unit));
            Assert.Equal(2, db.EngineLlmCalls.Count());
        }

        [Fact]
        public async Task A_stored_card_is_not_evicted_and_rewritten_every_pass()
        {
            // The headline-only alert card on an action day has no one_thing; re-checking it as
            // model output would evict it and write it again on every pass.
            await using var db = NewDb();
            const string payload = """{"situation":"action_reminder","team_state":"Drift","action_text":"Ask about handoffs."}""";
            var card = Feed("9.d", 9, "daily", payload);
            card.Situation = "action_reminder";
            card.Prose = EranGuardFallback.DailyHeadlineOnly(payload);
            card.GuardOutcome = EranGuardLog.FallbackFinal;
            Seed(db, card);

            var calls = 0;
            await Narrator(db, (_, _) => { calls += 1; return Clean; }).NarrateAsync(Unit, 14);

            Assert.Equal(0, calls);
            Assert.Equal(EranGuardLog.FallbackFinal, db.EngineFeedItems.Single().GuardOutcome);
            Assert.NotNull(EranCoachTimelineService.GuardedProse(db.EngineFeedItems.Single()));
        }

        [Fact]
        public async Task The_timeline_never_shows_stored_wording_that_breaks_a_rule()
        {
            var row = Feed("19.w", 19, "weekly", WeeklyPayload);
            row.Prose = Bad;
            Assert.Null(EranCoachTimelineService.GuardedProse(row));
            row.Prose = Clean;
            Assert.Equal(Clean, EranCoachTimelineService.GuardedProse(row));
            await Task.CompletedTask;
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private sealed class ListLogger : ILogger<EranNarratorService>
        {
            public List<string> Lines { get; } = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                Lines.Add(formatter(state, exception) + (exception is null ? "" : " " + exception.Message));
        }

        private static EranNarratorService Narrator(ApplicationDbContext db, Func<string, string, string> answer, ILogger<EranNarratorService>? log = null)
        {
            var client = new Mock<IEranCoachAnthropicClient>();
            client.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ModelConfiguration>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
                .ReturnsAsync((string s, string u, ModelConfiguration _, int _, CancellationToken _, bool _) => answer(s, u));
            var models = new Mock<IDocumentEmbeddingRepository>();
            models.Setup(m => m.GetAllModelsAsync(It.IsAny<int?>()))
                .ReturnsAsync([new ModelConfiguration { ModelName = "claude-sonnet-test", IsSelected = true }]);
            var engine = new Mock<IRridaEngineContextFactory>();
            engine.Setup(e => e.Context).Returns(Ctx.Value);
            return new EranNarratorService(db, client.Object, models.Object, new EranEngineContextBuilder(db, engine.Object),
                log ?? new ListLogger());
        }

        private static ApplicationDbContext NewDb() =>
            new TestApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"narrator-guard-{Guid.NewGuid()}")
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

        private static EngineFeedItem Feed(string key, int day, string kind, string payload) => new()
        {
            BusinessUnitId = Unit, FeedKey = key, Day = day, Kind = kind, PayloadJson = payload,
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
