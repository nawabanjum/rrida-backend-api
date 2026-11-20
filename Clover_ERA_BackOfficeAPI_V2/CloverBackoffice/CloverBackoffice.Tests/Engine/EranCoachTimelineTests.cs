using System.Runtime.CompilerServices;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
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
    /// The team's Eran thread (GET api/eran-coach/timeline) and "I'll do it"
    /// (POST api/rrida/manager/acknowledge).
    /// </summary>
    public class EranCoachTimelineTests
    {
        private const uint Unit = 7001;
        private const uint OtherUnit = 7002;
        private static readonly User Manager = new() { UserId = 11 };
        private static readonly Lazy<EngineContext> Ctx = new(() =>
            EngineContext.FromKit(Path.Combine(EngineRoot(), "kit", "data")));

        // A Monday, so day 1 is a Monday and days 6 and 7 are the weekend.
        private static readonly DateOnly Start = MondayBefore(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30));

        [Fact]
        public async Task Engine_messages_and_my_chat_form_one_thread_in_order()
        {
            await using var db = NewDb();
            SeedSettings(db);
            db.EngineFeedItems.AddRange(
                Feed("2.d", 2, "daily", "action_first_day", prose: """{"headline":"H","body":"B","outward":"U","one_thing":"O","say_this":"S","what_tells_you":"W"}"""),
                Feed("2.o.C1.1", 2, "outcome", null, prose: "The reports moved."),
                Feed("3.d", 3, "daily", "action_reminder"));
            var conversation = AddConversation(db, Manager.UserId, Unit);
            db.EranCoachMessages.Add(new EranCoachMessage { ConversationId = conversation, Role = "user", Content = "Why this?", CreatedAt = At(2, 9) });
            db.EranCoachMessages.Add(new EranCoachMessage { ConversationId = conversation, Role = "assistant", Content = "Because.", CreatedAt = At(2, 9).AddSeconds(5) });
            await db.SaveChangesAsync();

            var page = await Service(db).GetAsync(Manager, Unit, null, 50);

            Assert.Equal(conversation, page.ConversationId);
            Assert.Equal(
                new[] { "engine:outcome", "engine:daily", "chat:user", "chat:assistant", "engine:daily" },
                page.Items.Select(i => i.Type + ":" + (i.Kind ?? i.Role)));
            var daily = page.Items[1];
            Assert.Equal("ready", daily.Status);
            Assert.Equal("S", daily.Daily!.SayThis);
            Assert.Equal("U", daily.Daily.Outward);   // D36: its own field, never folded into the body
            Assert.Equal("B", daily.Daily.Body);
            Assert.Equal("signal read", page.Items[0].Label);
            Assert.Equal("pending", page.Items[^1].Status);
        }

        [Fact]
        public async Task No_daily_message_on_a_day_the_team_was_not_asked()
        {
            // D35. The engine writes none any more; these stand for rows an older run stored.
            await using var db = NewDb();
            SeedSettings(db);
            db.EngineFeedItems.AddRange(
                Feed("5.d", 5, "daily", "action_reminder"),          // Friday
                Feed("6.d", 6, "daily", "action_reminder"),          // Saturday: hidden
                Feed("6.m", 6, "monthly", null),                     // Saturday: other kinds always shown
                Feed("7.d", 7, "daily", "action_first_day"),         // Sunday: hidden too (D35)
                Feed("8.d", 8, "daily", "nothing_cleared"));         // Monday
            await db.SaveChangesAsync();

            var page = await Service(db).GetAsync(Manager, Unit, null, 50);

            Assert.Equal(new[] { "5.d", "6.m", "8.d" }, page.Items.Select(i => i.FeedKey));
        }

        [Fact]
        public async Task Only_my_own_chat_about_this_team_is_in_the_thread()
        {
            await using var db = NewDb();
            SeedSettings(db);
            var mine = AddConversation(db, Manager.UserId, Unit);
            var otherTeam = AddConversation(db, Manager.UserId, OtherUnit);
            var someoneElse = AddConversation(db, 99, Unit);
            foreach (var (c, text) in new[] { (mine, "mine"), (otherTeam, "other team"), (someoneElse, "someone else") })
                db.EranCoachMessages.Add(new EranCoachMessage { ConversationId = c, Role = "user", Content = text, CreatedAt = At(3, 10) });
            await db.SaveChangesAsync();

            var page = await Service(db).GetAsync(Manager, Unit, null, 50);

            Assert.Equal(new[] { "mine" }, page.Items.Select(i => i.Content));
        }

        [Fact]
        public async Task A_team_I_cannot_see_is_refused()
        {
            await using var db = NewDb();
            SeedSettings(db);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(db, accessible: [OtherUnit]).GetAsync(Manager, Unit, null, 50));
        }

        [Fact]
        public async Task Pages_walk_back_without_gaps_or_repeats()
        {
            await using var db = NewDb();
            SeedSettings(db);
            for (var day = 1; day <= 5; day += 1)
                db.EngineFeedItems.Add(Feed($"{day}.d", day, "daily", "calibrating", prose: """{"headline":"h","body":"b","one_thing":"","say_this":"","what_tells_you":""}"""));
            await db.SaveChangesAsync();
            var service = Service(db);

            var first = await service.GetAsync(Manager, Unit, null, 2);
            var second = await service.GetAsync(Manager, Unit, first.NextBefore, 2);
            var third = await service.GetAsync(Manager, Unit, second.NextBefore, 2);

            Assert.Equal(new[] { "4.d", "5.d" }, first.Items.Select(i => i.FeedKey));
            Assert.Equal(new[] { "2.d", "3.d" }, second.Items.Select(i => i.FeedKey));
            Assert.Equal(new[] { "1.d" }, third.Items.Select(i => i.FeedKey));
            Assert.Null(third.NextBefore);
        }

        [Fact]
        public async Task Unwritten_messages_on_the_page_are_queued_for_Eran()
        {
            await using var db = NewDb();
            SeedSettings(db);
            db.EngineFeedItems.AddRange(Feed("3.d", 3, "daily", "calibrating"), Feed("4.d", 4, "daily", "calibrating"));
            await db.SaveChangesAsync();
            var queue = new Mock<IEngineNarrationQueue>();

            await Service(db, queue: queue.Object).GetAsync(Manager, Unit, null, 50);

            queue.Verify(q => q.Enqueue(Unit, 4), Times.Once);
        }

        [Fact]
        public async Task A_message_Eran_gave_up_on_is_failed_not_pending_for_ever()
        {
            await using var db = NewDb();
            SeedSettings(db);
            db.EngineFeedItems.AddRange(Feed("3.d", 3, "daily", "calibrating"), Feed("4.d", 4, "daily", "calibrating"));
            for (var i = 0; i < EranNarratorService.MaxAttempts; i++)
                db.EngineLlmCalls.Add(new EngineLlmCall
                {
                    BusinessUnitId = Unit, FeedKey = "3.d", Day = 3, PromptType = "daily", SystemPrompt = "s", UserContent = "u",
                    Error = "overloaded", CreatedAt = DateTime.UtcNow.AddMinutes(1)
                });
            db.EngineLlmCalls.Add(new EngineLlmCall
            {
                BusinessUnitId = Unit, FeedKey = "4.d", Day = 4, PromptType = "daily", SystemPrompt = "s", UserContent = "u",
                Error = "overloaded", CreatedAt = DateTime.UtcNow.AddMinutes(1)
            });
            await db.SaveChangesAsync();
            var queue = new Mock<IEngineNarrationQueue>();

            var page = await Service(db, queue: queue.Object).GetAsync(Manager, Unit, null, 50);

            Assert.Equal("failed", page.Items.Single(i => i.FeedKey == "3.d").Status);
            Assert.Equal("pending", page.Items.Single(i => i.FeedKey == "4.d").Status);   // one failure: still being tried
            queue.Verify(q => q.Enqueue(Unit, 4), Times.Once);   // a failed message is not queued again
        }

        [Fact]
        public async Task Acknowledge_is_recorded_once_per_rung_and_only_for_the_move_in_front()
        {
            await using var db = NewDb();
            SeedSettings(db);
            var cycle = new EngineCycle { BusinessUnitId = Unit, CycleKey = "C1", CycleIndex = 1, IssueFileKey = "MA-E-002", DeliveredDay = 8, ConfirmationDueDay = 11 };
            db.EngineCycles.Add(cycle);
            await db.SaveChangesAsync();
            db.EngineCycleDeliveries.Add(new EngineCycleDelivery { CycleId = cycle.Id, Rung = 1, ActionKey = "MA-E-002-22", DeliveredDay = 8, Route = "team" });
            await db.SaveChangesAsync();
            var manager = ManagerService(db);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                manager.AcknowledgeAsync(Manager, new EngineAcknowledgeRequestDto { BusinessUnitId = Unit, CycleKey = "C9" }));

            var first = await manager.AcknowledgeAsync(Manager, new EngineAcknowledgeRequestDto { BusinessUnitId = Unit, CycleKey = "C1" });
            var again = await manager.AcknowledgeAsync(new User { UserId = 12 }, new EngineAcknowledgeRequestDto { BusinessUnitId = Unit, CycleKey = "C1" });

            Assert.True(first!.IsAcknowledged);
            Assert.Equal(first.AcknowledgedAt, again!.AcknowledgedAt);
            var row = Assert.Single(db.EngineAcknowledgements);
            Assert.Equal(Manager.UserId, row.AcknowledgedByUserId);
            Assert.Equal(1, row.Rung);
        }

        [Fact]
        public async Task A_cycle_id_returns_only_that_cycles_engine_messages()
        {
            await using var db = NewDb();
            SeedSettings(db);
            var c1 = new EngineCycle { BusinessUnitId = Unit, CycleKey = "C1", CycleIndex = 1, IssueFileKey = "MA-E-002", DeliveredDay = 2, ConfirmationDueDay = 5 };
            db.EngineCycles.AddRange(c1,
                new EngineCycle { BusinessUnitId = Unit, CycleKey = "C2", CycleIndex = 2, IssueFileKey = "MA-E-003", DeliveredDay = 9, ConfirmationDueDay = 12 });
            db.EngineFeedItems.AddRange(
                Feed("1.d", 1, "daily", "calibrating"),
                Feed("2.d", 2, "daily", "action_first_day", cycle: "C1"),
                Feed("3.d", 3, "daily", "action_reminder", cycle: "C1"),
                Feed("5.w", 5, "weekly", null),
                Feed("8.o.C1.1", 8, "outcome", null, prose: "The reports moved.", cycle: "C1"),
                Feed("9.d", 9, "daily", "action_first_day", cycle: "C2"));
            var conversation = AddConversation(db, Manager.UserId, Unit);
            db.EranCoachMessages.Add(new EranCoachMessage { ConversationId = conversation, Role = "user", Content = "Why this?", CreatedAt = At(2, 9) });
            await db.SaveChangesAsync();

            var page = await Service(db).GetAsync(Manager, Unit, null, 50, c1.Id);

            Assert.Equal(new[] { "2.d", "3.d", "8.o.C1.1" }, page.Items.Select(i => i.FeedKey));
            Assert.All(page.Items, i => Assert.Equal("engine", i.Type));
            Assert.All(page.Items, i => Assert.Equal("C1", i.CycleKey));

            var all = await Service(db).GetAsync(Manager, Unit, null, 50);
            Assert.Equal(7, all.Items.Count);
        }

        [Fact]
        public async Task An_unknown_or_other_teams_cycle_id_is_refused()
        {
            await using var db = NewDb();
            SeedSettings(db);
            var other = new EngineCycle { BusinessUnitId = OtherUnit, CycleKey = "C1", CycleIndex = 1, IssueFileKey = "MA-E-002", DeliveredDay = 2, ConfirmationDueDay = 5 };
            db.EngineCycles.Add(other);
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).GetAsync(Manager, Unit, null, 50, 999));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).GetAsync(Manager, Unit, null, 50, other.Id));
        }

        // ── helpers ──────────────────────────────────────────────────────────

        [Fact]
        public async Task Prompts_follow_the_teams_data()
        {
            await using var db = NewDb();
            SeedSettings(db);
            foreach (var (day, vertical) in new[] { (13, "drift"), (19, "stall") })
            {
                db.EngineStateDaily.Add(new EngineStateDaily
                {
                    BusinessUnitId = Unit, Day = day, Schema = "rrida.state.daily.v1", ConfigId = "test",
                    Vertical = vertical, Horizontal = "unlocated", RecordJson = "{}", WrittenAt = DateTime.UtcNow
                });
            }
            db.EngineFeedItems.AddRange(Feed("13.w", 13, "weekly", null), Feed("19.d", 19, "daily", "between_cycles"));
            db.EngineCycles.Add(new EngineCycle
            {
                BusinessUnitId = Unit, CycleKey = "C1", CycleIndex = 1, IssueFileKey = "belonging", Issue = "Belonging",
                DeliveredDay = 8, ResolvedDay = 18, Outcome = "closed"
            });
            await db.SaveChangesAsync();

            var manager = new Mock<IEngineManagerService>();
            manager.Setup(m => m.GetStateAsync(Manager, Unit, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EngineTeamStateDto { BusinessUnitId = Unit, Day = 20, Vertical = "stall" });
            var service = new EranCoachTimelineService(db, Access().Object, manager.Object, EngineFactory().Object,
                new Mock<IEngineNarrationQueue>().Object);

            var page = await service.GetAsync(Manager, Unit, null, 50);

            Assert.Equal(new[]
            {
                "What would have to change for something to become active?",
                "What moved the team from Drift to Stall?",
                "What did working on belonging show?",
                "What does Stall actually mean for me?"
            }, page.Prompts);
        }

        [Fact]
        public async Task No_prompts_for_a_team_not_on_the_engine()
        {
            await using var db = NewDb();
            var page = await Service(db).GetAsync(Manager, Unit, null, 50);
            Assert.Empty(page.Prompts);
        }

        private static ApplicationDbContext NewDb() =>
            new TestApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"timeline-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static void SeedSettings(ApplicationDbContext db)
        {
            db.EngineTeamSettings.Add(new EngineTeamSettings { BusinessUnitId = Unit, StartDate = Start, WorkingWeek = "mon-fri", IsEnabled = true });
            db.SaveChanges();
        }

        private static EngineFeedItem Feed(string key, int day, string kind, string? situation, string? prose = null, string? cycle = null) => new()
        {
            BusinessUnitId = Unit, FeedKey = key, Day = day, Kind = kind, Situation = situation, CycleKey = cycle,
            PayloadJson = "{}", Prose = prose, WrittenAt = DateTime.UtcNow
        };

        private static int AddConversation(ApplicationDbContext db, int userId, uint unit)
        {
            var c = new EranCoachConversation { UserId = userId, BusinessUnitId = unit, CreatedAt = DateTime.UtcNow };
            db.EranCoachConversations.Add(c);
            db.SaveChanges();
            return c.Id;
        }

        private static DateTime At(int day, int hour) =>
            new DateTime(Start.AddDays(day - 1), new TimeOnly(hour, 0), DateTimeKind.Utc);

        private static Mock<IRridaEngineContextFactory> EngineFactory()
        {
            var engine = new Mock<IRridaEngineContextFactory>();
            engine.Setup(e => e.Context).Returns(Ctx.Value);
            engine.Setup(e => e.Calendar).Returns(Ctx.Value.Calendar);
            return engine;
        }

        private static Mock<IUnitAccessService> Access(long[]? accessible = null)
        {
            var access = new Mock<IUnitAccessService>();
            access.Setup(a => a.GetAccessibleUnitIdsAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((accessible ?? [Unit]).ToList());
            return access;
        }

        private static EranCoachTimelineService Service(ApplicationDbContext db, long[]? accessible = null, IEngineNarrationQueue? queue = null) =>
            new(db, Access(accessible).Object, new Mock<IEngineManagerService>().Object, EngineFactory().Object,
                queue ?? new Mock<IEngineNarrationQueue>().Object);

        private static EngineManagerService ManagerService(ApplicationDbContext db) =>
            new(db, EngineFactory().Object, Access().Object, new Mock<IEngineReplayRunner>().Object,
                NullLogger<EngineManagerService>.Instance);

        private static DateOnly MondayBefore(DateOnly date)
        {
            while (date.DayOfWeek != DayOfWeek.Monday) date = date.AddDays(-1);
            return date;
        }

        private static string EngineRoot([CallerFilePath] string source = "")
        {
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(source)!); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "external", "RRIDA-Engine");
                if (Directory.Exists(candidate)) return candidate;
            }
            throw new DirectoryNotFoundException("external/RRIDA-Engine not found");
        }
    }
}
