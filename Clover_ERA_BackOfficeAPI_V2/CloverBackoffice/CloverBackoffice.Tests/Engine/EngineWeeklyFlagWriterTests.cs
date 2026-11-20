using System.Runtime.CompilerServices;
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
    /// <summary>Engine → TeamWeeklyFlags: each week's flag carries the id of the cycle its action came from.</summary>
    public class EngineWeeklyFlagWriterTests
    {
        private const uint Unit = 7101;
        private const uint Profile = 71;
        private static readonly Lazy<EngineContext> Ctx = new(() =>
            EngineContext.FromKit(Path.Combine(EngineRoot(), "kit", "data")));

        // A Monday, so days 1-5 are week one and day 8 starts week two.
        private static readonly DateOnly Start = MondayBefore(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30));

        [Fact]
        public async Task Each_week_carries_the_cycle_its_action_came_from()
        {
            await using var db = NewDb();
            Seed(db);
            AddCycle(db, "C1", 1, deliveredDay: 2);
            var c2 = AddCycle(db, "C2", 2, deliveredDay: 3);   // same week as C1, delivered later: wins the week
            AddReading(db, day: 12, vertical: "drift", headwinds: true);   // Friday of week two; nothing delivered
            await db.SaveChangesAsync();

            var written = await Writer(db).WriteAsync(Unit);

            Assert.Equal(2, written);
            var flags = await db.TeamWeeklyFlags.OrderBy(f => f.WeekStartDate).ToListAsync();
            Assert.Equal(new DateOnly?[] { Start, Start.AddDays(7) }, flags.Select(f => (DateOnly?)f.WeekStartDate));
            Assert.Equal(c2, flags[0].CycleId);

            // MVP weeklyFlag: a reading week where nothing cleared the floor says so and gives
            // the posture for the state. Headwinds is a flag, so it changes none of that.
            Assert.Null(flags[1].CycleId);
            Assert.Equal(EngineWeeklyFlagWriter.NothingSettled, flags[1].WhatCameUp);
            Assert.Equal(EngineState.NoFloorPosture["Drift"], flags[1].SuggestedAction);
        }

        [Fact]
        public async Task What_came_up_is_said_in_proportions_never_counts()
        {
            await using var db = NewDb();
            Seed(db);
            AddCycle(db, "C1", 1, deliveredDay: 2, shareText: "most of the team");
            await db.SaveChangesAsync();

            await Writer(db).WriteAsync(Unit);

            var flag = await db.TeamWeeklyFlags.SingleAsync();
            Assert.Equal("Blockers came up in the week's answers: most of the team.", flag.WhatCameUp);
        }

        [Fact]
        public async Task A_week_this_engine_no_longer_writes_is_retired_and_others_flags_are_left()
        {
            await using var db = NewDb();
            Seed(db);
            AddCycle(db, "C1", 1, deliveredDay: 2);
            db.TeamWeeklyFlags.Add(Flag(Start.AddDays(14), "Engine"));     // an earlier run's week
            db.TeamWeeklyFlags.Add(Flag(Start.AddDays(21), "DemoSeed"));   // not the engine's to touch
            await db.SaveChangesAsync();

            await Writer(db).WriteAsync(Unit);

            var rows = await db.TeamWeeklyFlags.OrderBy(f => f.WeekStartDate).ToListAsync();
            Assert.False(rows.Single(f => f.WeekStartDate == Start).IsDel);
            Assert.True(rows.Single(f => f.WeekStartDate == Start.AddDays(14)).IsDel);
            Assert.False(rows.Single(f => f.WeekStartDate == Start.AddDays(21)).IsDel);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static ApplicationDbContext NewDb() =>
            new TestApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"weekly-flags-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static void Seed(ApplicationDbContext db)
        {
            db.BusinessUnits.Add(new BusinessUnit
            {
                Id = Unit, BusinessUnitCode = $"BU{Unit}", BusinessUnitShortCode = $"S{Unit}",
                Name = "Team", NameInvariant = "team", RegistrationDate = DateTime.UtcNow, ProfileId = Profile,
                ActiveDailyQuestionSetGroupId = 1, ActiveDailyQuestionSetGroup = null!
            });
            db.EngineTeamSettings.Add(new EngineTeamSettings { BusinessUnitId = Unit, StartDate = Start, WorkingWeek = "mon-fri", IsEnabled = true });
            db.SaveChanges();
        }

        private static long AddCycle(ApplicationDbContext db, string key, int index, int deliveredDay, string? shareText = null)
        {
            var cycle = new EngineCycle
            {
                BusinessUnitId = Unit, CycleKey = key, CycleIndex = index, IssueFileKey = "MA-E-002",
                Issue = "Blockers", DeliveredDay = deliveredDay, ConfirmationDueDay = deliveredDay + 3,
                ShareText = shareText
            };
            db.EngineCycles.Add(cycle);
            db.SaveChanges();
            db.EngineCycleDeliveries.Add(new EngineCycleDelivery
            {
                CycleId = cycle.Id, Rung = 1, ActionKey = "MA-E-002-22", DeliveredDay = deliveredDay,
                Route = "team", DeliveredText = $"Action for {key}."
            });
            db.SaveChanges();
            return cycle.Id;
        }

        /// <summary>The engine's weekly reading on a reading day, and that day's state.</summary>
        private static void AddReading(ApplicationDbContext db, int day, string vertical, bool headwinds)
        {
            db.EngineStateDaily.Add(new EngineStateDaily
            {
                BusinessUnitId = Unit, Day = day, Schema = "rrida.state.daily.v1", ConfigId = "test",
                Vertical = vertical, Horizontal = headwinds ? "external" : "unlocated",
                HeadwindsFlag = headwinds, RecordJson = "{}", WrittenAt = DateTime.UtcNow
            });
            db.EngineFeedItems.Add(new EngineFeedItem
            {
                BusinessUnitId = Unit, FeedKey = $"{day}.w", Day = day, Kind = "weekly",
                PayloadJson = "{}", WrittenAt = DateTime.UtcNow
            });
        }

        private static Infrastructure.Models.TeamHealth.TeamWeeklyFlag Flag(DateOnly start, string source) => new()
        {
            ProfileId = Profile, UnitId = Unit, WeekStartDate = start, WeekEndDate = start.AddDays(6),
            WhatCameUp = "old", SuggestedAction = "old", Source = source,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };

        private static EngineWeeklyFlagWriter Writer(ApplicationDbContext db)
        {
            var engine = new Mock<IRridaEngineContextFactory>();
            engine.Setup(e => e.Context).Returns(Ctx.Value);
            engine.Setup(e => e.Calendar).Returns(Ctx.Value.Calendar);
            return new EngineWeeklyFlagWriter(db, engine.Object, NullLogger<EngineWeeklyFlagWriter>.Instance);
        }

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
