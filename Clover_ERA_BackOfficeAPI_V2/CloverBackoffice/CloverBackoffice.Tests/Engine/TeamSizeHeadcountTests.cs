using System.Runtime.CompilerServices;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Rrida.Engine;
using Rrida.State;
using Xunit;
using SizeRow = CloverBackoffice.Services.Implementation.TeamHeadcount.SizeRow;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>
    /// The team size the manager set is the headcount: the engine's floor and share, the
    /// response rate, Eran's team size. App enrolments count devices, not people, so a team of
    /// 6 can show 13 enrolled after reinstalls. Days before a size was recorded, and teams with
    /// no size, count the members enrolled, exactly as before.
    /// </summary>
    public class TeamSizeHeadcountTests
    {
        private static readonly string EngineRoot = FindEngineRoot();
        private static readonly Lazy<RridaEngineContextFactory> Factory = new(() => new RridaEngineContextFactory(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Rrida:KitDataPath"] = Path.Combine(EngineRoot, "kit", "data"),
                ["Rrida:ProductionRotationPath"] = Path.Combine(EngineRoot, "rotations", "rotation-90.json")
            }).Build(),
            NullLogger<RridaEngineContextFactory>.Instance));

        private static TeamConfig Config(int enrolled, List<HeadcountPeriod>? headcount = null) => new()
        {
            Id = "t", Name = "Team",
            Roster = Enumerable.Range(1, enrolled).Select(i => new Member { Id = $"M{i}" }).ToList(),
            Headcount = headcount ?? [],
            Script = new Dictionary<int, List<ScriptEntry>>(),
            WorkingWeek = "mon-fri",
            StartDate = "2026-01-05"
        };

        // ── the engine ───────────────────────────────────────────────────────────

        [Fact]
        public void With_no_headcount_the_engine_counts_the_members_enrolled_as_upstream()
        {
            var team = new Team(Factory.Value.Context, Config(13));

            Assert.Equal(13, team.EnrolledCountOn(1));
            Assert.Equal(13, team.EnrolledCountOn(40));
        }

        [Fact]
        public void A_headcount_applies_from_its_own_day_and_a_null_size_falls_back_to_enrolled()
        {
            var team = new Team(Factory.Value.Context, Config(13, [new(10, 6), new(20, null), new(30, 8)]));

            Assert.Equal(13, team.EnrolledCountOn(9));    // before any size: enrolled
            Assert.Equal(6, team.EnrolledCountOn(10));
            Assert.Equal(6, team.EnrolledCountOn(19));
            Assert.Equal(13, team.EnrolledCountOn(20));   // size cleared: enrolled
            Assert.Equal(8, team.EnrolledCountOn(30));
            Assert.Equal(8, team.EnrolledCountOn(99));
        }

        [Fact]
        public void The_floor_counts_the_team_size_not_duplicate_enrolments()
        {
            var ctx = Factory.Value.Context;
            var duplicates = new Team(ctx, Config(13));
            var sized = new Team(ctx, Config(13, [new(1, 6)]));

            var before = Tally.Build(ctx, duplicates, 10, ctx.DeliveryTracks, null, locusFilterWasGiven: true).Floor;
            var after = Tally.Build(ctx, sized, 10, ctx.DeliveryTracks, null, locusFilterWasGiven: true).Floor;

            Assert.Equal(13, before.Enrolled);
            Assert.Equal(4, before.Required);   // max(2, floor(0.33 x 13))
            Assert.Equal(6, after.Enrolled);
            Assert.Equal(2, after.Required);    // max(2, floor(0.33 x 6))
        }

        [Fact]
        public void The_daily_message_reports_the_headcount()
        {
            var config = Config(13, [new(1, 6)]);
            var sim = new Simulation(Factory.Value.Context, [config],
                new Resolver(Factory.Value.StateConfig, new BufferingStateSink("test")), horizon: 3, config.StartDate);
            while (!sim.Step().AtHorizon) { }

            var calibrating = sim.AllTeams()[0].Feed.First(f => f.Kind == "daily");
            Assert.Equal(6, Convert.ToInt32(calibrating.Payload["enrolled"]));
        }

        // ── the backend's two readings ───────────────────────────────────────────

        [Fact]
        public void The_engine_reading_uses_the_dated_size_only()
        {
            var history = new List<SizeRow> { new(new DateOnly(2026, 11, 16), 6) };

            Assert.Equal(13, TeamHeadcount.EngineSizeOn(history, new DateOnly(2026, 11, 13), 13));   // before it: enrolled
            Assert.Equal(6, TeamHeadcount.EngineSizeOn(history, new DateOnly(2026, 11, 16), 13));
            Assert.Equal(13, TeamHeadcount.EngineSizeOn([], new DateOnly(2026, 11, 16), 13));
        }

        [Fact]
        public void The_display_reading_uses_the_dated_size_else_the_size_now_else_enrolled()
        {
            var history = new List<SizeRow> { new(new DateOnly(2026, 11, 16), 6), new(new DateOnly(2026, 11, 20), null) };

            Assert.Equal(6, TeamHeadcount.DisplaySizeOn(history, 6, new DateOnly(2026, 11, 13), 13));    // before: the size now
            Assert.Equal(6, TeamHeadcount.DisplaySizeOn(history, 6, new DateOnly(2026, 11, 17), 13));
            Assert.Equal(13, TeamHeadcount.DisplaySizeOn(history, null, new DateOnly(2026, 11, 21), 13)); // cleared: enrolled
            Assert.Equal(13, TeamHeadcount.DisplaySizeOn([], null, new DateOnly(2026, 11, 13), 13));
        }

        [Fact]
        public void Engine_periods_are_on_the_teams_day_numbers_and_a_size_before_the_start_applies_from_day_one()
        {
            var clock = new EngineTeamClock(new DateOnly(2026, 11, 2), "mon-fri", Factory.Value.Calendar);
            var periods = TeamHeadcount.ToEnginePeriods(
                [new(new DateOnly(2026, 10, 1), 5), new(new DateOnly(2026, 11, 1), 6), new(new DateOnly(2026, 11, 16), 7)], clock);

            Assert.Equal([new HeadcountPeriod(1, 6), new HeadcountPeriod(15, 7)], periods);
        }

        // ── the history, written on save ─────────────────────────────────────────

        [Fact]
        public async Task A_new_team_with_a_size_gets_a_row_from_today()
        {
            await using var db = NewContext();
            db.BusinessUnits.Add(Unit(1, 6));
            await db.SaveChangesAsync();

            var row = Assert.Single(db.TeamSizeHistory);
            Assert.Equal((1u, 6, Today), (row.BusinessUnitId, row.TeamSize, row.EffectiveFrom));
        }

        [Fact]
        public async Task A_new_team_with_no_size_records_nothing()
        {
            await using var db = NewContext();
            db.BusinessUnits.Add(Unit(1, null));
            await db.SaveChangesAsync();

            Assert.Empty(db.TeamSizeHistory);
        }

        [Fact]
        public async Task A_change_the_same_day_updates_todays_row_and_an_unchanged_size_writes_nothing()
        {
            await using var db = NewContext();
            db.BusinessUnits.Add(Unit(1, 6));
            await db.SaveChangesAsync();

            db.BusinessUnits.Single().TeamSize = 8;
            await db.SaveChangesAsync();
            db.BusinessUnits.Single().Name = "Renamed";   // TeamSize not touched
            await db.SaveChangesAsync();

            var row = Assert.Single(db.TeamSizeHistory);
            Assert.Equal(8, row.TeamSize);
        }

        [Fact]
        public void A_change_after_an_earlier_size_adds_a_row_and_clearing_the_size_is_recorded()
        {
            using var db = NewContext();
            db.BusinessUnits.Add(Unit(1, null));
            db.TeamSizeHistory.Add(new Infrastructure.Models.DeterministicLayer.TeamSizeHistory
            {
                BusinessUnitId = 1, TeamSize = 6, EffectiveFrom = Today.AddDays(-10), CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
            db.BusinessUnits.Single().TeamSize = 6;   // the size in force: nothing new
            db.SaveChanges();
            Assert.Single(db.TeamSizeHistory);

            db.BusinessUnits.Single().TeamSize = null;   // sync SaveChanges goes the same way
            db.SaveChanges();

            var rows = db.TeamSizeHistory.OrderBy(h => h.EffectiveFrom).ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal((Today, (int?)null), (rows[1].EffectiveFrom, rows[1].TeamSize));
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

        private static ApplicationDbContext NewContext() =>
            new TestApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static BusinessUnit Unit(uint id, int? teamSize) => new()
        {
            Id = id, BusinessUnitCode = $"BU{id}", BusinessUnitShortCode = $"S{id}", Name = $"Team {id}",
            NameInvariant = $"team {id}", RegistrationDate = DateTime.UtcNow, ProfileId = 1, TeamSize = teamSize,
            IsDel = false, ActiveDailyQuestionSetGroupId = 1, ActiveDailyQuestionSetGroup = null!
        };

        private static string FindEngineRoot([CallerFilePath] string source = "")
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
