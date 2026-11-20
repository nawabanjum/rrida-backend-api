using System.Runtime.CompilerServices;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Rrida.Engine;
using Xunit;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>
    /// What Eran Chat is told about a team on the deterministic layer, held to the MVP's chat
    /// (src/eran/prompts.js buildChatPrompt, src/engine/chat.js chatSituation).
    /// </summary>
    public class EngineChatContextServiceTests
    {
        private const uint Unit = 8101;
        private const int Me = 21;
        private static readonly Lazy<EngineContext> Ctx = new(() =>
            EngineContext.FromKit(Path.Combine(EngineRoot(), "kit", "data")));
        private static readonly DateOnly Start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-20);
        private static int Today => DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - Start.DayNumber + 1;

        [Fact]
        public async Task A_team_not_on_the_engine_gets_nothing_and_the_chat_is_unchanged()
        {
            await using var db = NewDb();
            Assert.Null(await Service(db).BuildAsync(Unit, Me, "Hi"));

            db.EngineTeamSettings.Add(new EngineTeamSettings { BusinessUnitId = Unit, StartDate = Start, WorkingWeek = "mon-fri", IsEnabled = false });
            await db.SaveChangesAsync();
            Assert.Null(await Service(db).BuildAsync(Unit, Me, "Hi"));
        }

        [Fact]
        public async Task The_block_carries_the_MVP_rules_situation_and_areas_without_codes()
        {
            await using var db = NewDb();
            SeedTeam(db, state: "Drift");

            var engine = await Service(db).BuildAsync(Unit, Me, "Why this?");

            var block = engine!.SystemBlock;
            Assert.Contains("this block wins", block);
            Assert.Contains("You are Eran, the coaching layer of RRIDA.", block);
            Assert.Contains("Context you are given:\n", block);
            Assert.Contains("two lines containing only three dashes", block);
            Assert.Contains($"Where the team is today, day {Today}:\nThe reading on this team is Drift.", block);
            Assert.Contains("Nothing is running today. Monitor and check in.", block);

            var areas = block[block.IndexOf("The areas this product covers:", StringComparison.Ordinal)..];
            Assert.DoesNotContain("MA-", areas);
            Assert.True(areas.Split('\n').Length > 3);
        }

        [Fact]
        public async Task A_running_move_is_described_as_delivered()
        {
            await using var db = NewDb();
            SeedTeam(db, state: "Stall");
            var action = Ctx.Value.Data.Actions.Values.First(a => !string.IsNullOrWhiteSpace(a.Text));
            var worksheet = Ctx.Value.Data.Worksheets.Values.First();
            var cycle = new EngineCycle { BusinessUnitId = Unit, CycleKey = "C1", CycleIndex = 1, IssueFileKey = "MA-E-002", Issue = "Blockers Not Cleared", DeliveredDay = 9, ConfirmationDueDay = 12, SignalReadDay = 15 };
            db.EngineCycles.Add(cycle);
            await db.SaveChangesAsync();
            db.EngineCycleDeliveries.Add(new EngineCycleDelivery
            {
                CycleId = cycle.Id, Rung = 1, ActionKey = action.Id, DeliveredDay = 9, ConfirmedDay = 12,
                DeliveredWorksheetCode = worksheet.Code, DeliveredWorkedIf = "Owners are named."
            });
            await db.SaveChangesAsync();

            var situation = await Service(db).SituationAsync(Unit);

            // MVP chat.js after 90fe06d / 59de560 / 0729864: the team's own working days (an
            // every-day week, today day 21), the check-back, and no interval for it.
            Assert.Equal(
                "The reading on this team is Stall. "
                + "Delivered 12 working days ago: blockers not cleared. What was asked of the manager is a conversation, the first move on this. "
                + $"The action reads: \"{action.Text}\" They confirmed it 9 working days ago. "
                + "The next check-back on it comes when a question on it comes round again. "
                + $"The worksheet they were actually handed is {worksheet.Code} {worksheet.Title}".TrimEnd() + ". "
                + "What they are asked to confirm is: Owners are named.",
                situation);
            Assert.DoesNotMatch(@"day \d+", situation);
        }

        [Fact]
        public async Task Before_the_first_reading_and_after_a_failed_move()
        {
            await using var db = NewDb();
            SeedTeam(db, state: null);
            db.EngineCycles.Add(new EngineCycle { BusinessUnitId = Unit, CycleKey = "C1", CycleIndex = 1, IssueFileKey = "MA-C-063", Issue = "Updates Arrive Late", Outcome = "failed", ResolvedDay = 14 });
            await db.SaveChangesAsync();

            var situation = await Service(db).SituationAsync(Unit);

            Assert.Equal(
                "The reading on this team is still calibrating. Nothing is running today. "
                + "Nothing is asked before the first reading on day seven. "
                + "Most recently: updates arrive late ran out of moves without shifting, 7 working days ago.",
                situation);
            Assert.DoesNotMatch(@"day \d+", situation);
        }

        [Fact]
        public async Task History_is_the_thread_with_earlier_days_stamped_and_the_question_last()
        {
            await using var db = NewDb();
            SeedTeam(db, state: "Cruise");
            var mine = Conversation(db, Me, Unit);
            var other = Conversation(db, 99, Unit);
            var yesterday = DateTime.UtcNow.AddDays(-1);
            db.EranCoachMessages.AddRange(
                new EranCoachMessage { ConversationId = mine, Role = "assistant", Content = "orphan reply", CreatedAt = yesterday.AddMinutes(-5) },
                new EranCoachMessage { ConversationId = mine, Role = "user", Content = "What changed?", CreatedAt = yesterday },
                new EranCoachMessage { ConversationId = mine, Role = "assistant", Content = "Handoffs.", CreatedAt = yesterday.AddSeconds(5) },
                new EranCoachMessage { ConversationId = other, Role = "user", Content = "not mine", CreatedAt = yesterday },
                new EranCoachMessage { ConversationId = mine, Role = "user", Content = "And now?", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();

            var engine = await Service(db).BuildAsync(Unit, Me, "And now?");

            Assert.Equal(
                new[] { $"user:[asked on day {Today - 1}] What changed?", "assistant:Handoffs." },
                engine!.History!.Select(t => $"{t.Role}:{t.Content}"));
        }

        [Theory]
        [InlineData("user", 5, 7, "[asked on day 5] q")]
        [InlineData("user", 7, 7, "q")]
        [InlineData("assistant", 5, 7, "q")]
        public void Only_earlier_questions_are_stamped(string role, int asked, int today, string expected) =>
            Assert.Equal(expected, EngineChatContextService.StampTurn(role, "q", asked, today).Content);

        // ── helpers ──────────────────────────────────────────────────────────

        private static ApplicationDbContext NewDb() =>
            new TestApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"engine-chat-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static void SeedTeam(ApplicationDbContext db, string? state)
        {
            db.EngineTeamSettings.Add(new EngineTeamSettings { BusinessUnitId = Unit, StartDate = Start, WorkingWeek = "every-day", IsEnabled = true });
            if (state is not null)
                db.EngineFeedItems.Add(new EngineFeedItem
                {
                    BusinessUnitId = Unit, FeedKey = "18.d", Day = 18, Kind = "daily",
                    PayloadJson = $$"""{"team_state":"{{state}}"}""", WrittenAt = DateTime.UtcNow
                });
            db.SaveChanges();
        }

        private static int Conversation(ApplicationDbContext db, int userId, uint unit)
        {
            var c = new EranCoachConversation { UserId = userId, BusinessUnitId = unit };
            db.EranCoachConversations.Add(c);
            db.SaveChanges();
            return c.Id;
        }

        private static EngineChatContextService Service(ApplicationDbContext db)
        {
            var engine = new Mock<IRridaEngineContextFactory>();
            engine.Setup(e => e.Context).Returns(Ctx.Value);
            engine.Setup(e => e.Calendar).Returns(Ctx.Value.Calendar);
            return new EngineChatContextService(db, engine.Object, new EranEngineContextBuilder(db, engine.Object));
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
