using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.TeamHealth
{
    /// <summary>Unit tests for TeamStateService (read only).</summary>
    public class TeamStateServiceTests
    {
        private readonly string _db = Guid.NewGuid().ToString();

        // Team 294 started 39 days ago, so today is day 40 and the window is days 11..40.
        internal static readonly DateOnly StartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-39);

        public TeamStateServiceTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
        }

        internal static void AddSettings(ApplicationDbContext db, uint unitId, DateOnly start)
        {
            db.EngineTeamSettings.Add(new EngineTeamSettings
            {
                BusinessUnitId = unitId,
                StartDate = start,
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        internal static void AddDay(ApplicationDbContext db, uint unitId, int day, string vertical, bool headwinds = false)
        {
            db.EngineStateDaily.Add(new EngineStateDaily
            {
                BusinessUnitId = unitId,
                Day = day,
                Schema = "rrida.state.daily.v1",
                ConfigId = "test",
                Vertical = vertical,
                Horizontal = headwinds ? "external" : "unlocated",
                HeadwindsFlag = headwinds,
                RecordJson = "{}",
                WrittenAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        /// <summary>The engine's weekly message on a reading day (D25), the day a report week ends.</summary>
        internal static void AddReading(ApplicationDbContext db, uint unitId, int day)
        {
            db.EngineFeedItems.Add(new EngineFeedItem
            {
                BusinessUnitId = unitId,
                FeedKey = $"{day}.w",
                Day = day,
                Kind = "weekly",
                PayloadJson = "{}",
                WrittenAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        /// <summary>The engine's first six resolved days: the calibration warm-up, never shown or counted.</summary>
        internal static void AddWarmUp(ApplicationDbContext db, uint unitId)
        {
            for (var day = 1; day <= TeamStateService.CalibrationDays; day++) AddDay(db, unitId, day, "stall");
        }

        // 30 days in the window: 18 cruise, 7 drift, 2 stall, 3 stall with the Headwinds flag.
        // Plus the warm-up days 1..6 (outside the window) and day 41 (tomorrow), which must not count.
        internal static void SeedMix(ApplicationDbContext db, uint unitId)
        {
            AddSettings(db, unitId, StartDate);
            AddWarmUp(db, unitId);
            var day = 11;
            for (var i = 0; i < 18; i++) AddDay(db, unitId, day++, "cruise");
            for (var i = 0; i < 7; i++) AddDay(db, unitId, day++, "drift");
            for (var i = 0; i < 2; i++) AddDay(db, unitId, day++, "stall");
            for (var i = 0; i < 3; i++) AddDay(db, unitId, day++, "stall", headwinds: true);  // days 38..40
            AddDay(db, unitId, 41, "stall");
        }

        private TeamStateService Service()
        {
            var db = CreateContext(_db);
            return new TeamStateService(db, new TeamHealthAccessService(db, Roles()));
        }

        [Fact]
        public async Task Mix_ReturnsLatestStateAndWindowPercentages()
        {
            using (var db = CreateContext(_db)) SeedMix(db, Team);

            var r = await Service().GetAsync("294", ManagerOfTeam);

            Assert.True(r.IsSuccess, r.Message);
            var d = r.Data!;
            Assert.Equal("294", d.TeamId);
            Assert.Equal("Team 294", d.TeamName);
            // MVP: Headwinds is a flag beside the state, not a state (D19/D21).
            Assert.Equal(TeamStates.Stall, d.State);         // day 40: stall, with the flag raised
            Assert.True(d.HeadwindsFlag);
            Assert.Equal(60, d.Cruise);                      // 18/30
            Assert.Equal(23, d.Drift);                       // 7/30 = 23.3
            Assert.Equal(17, d.Stall);                       // 5/30 = 16.7, takes the spare point
            Assert.Equal(100, d.Cruise + d.Drift + d.Stall);
            Assert.Equal(10, d.Headwinds);                   // flag on 3 of the 30 days
            Assert.Equal(30, d.DaysCounted);
            Assert.False(d.IsCalibrating);
            Assert.Equal(StartDate.AddDays(39).ToString("yyyy-MM-dd"), d.AsOfDate);
        }

        [Theory]
        [InlineData("cruise", false, TeamStates.Cruise)]
        [InlineData("drift", false, TeamStates.Drift)]
        [InlineData("stall", false, TeamStates.Stall)]
        [InlineData("cruise", true, TeamStates.Cruise)]
        [InlineData("DRIFT", false, TeamStates.Drift)]
        public async Task CurrentState_FromLatestDay(string vertical, bool headwinds, string expected)
        {
            using (var db = CreateContext(_db))
            {
                AddSettings(db, Team, StartDate);
                AddWarmUp(db, Team);
                AddDay(db, Team, 30, "cruise");
                AddDay(db, Team, 35, vertical, headwinds);
            }

            var r = await Service().GetAsync("294", ProfileAdminA);

            Assert.Equal(expected, r.Data!.State);
            Assert.Equal(headwinds, r.Data.HeadwindsFlag);
            Assert.Equal(2, r.Data.DaysCounted);
        }

        [Fact]
        public async Task Trail_IsTheReadingOnEachOfTheLastThreeWeeklyReadings_OldestFirst()
        {
            using (var db = CreateContext(_db))
            {
                SeedMix(db, Team);
                // Readings before today's day 40. Day 40 is the current reading itself, day 41
                // has not happened, and day 13 is older than the last three.
                foreach (var day in new[] { 13, 20, 27, 34, 40, 41 }) AddReading(db, Team, day);
                AddReading(db, OtherTeamA, 37);
            }

            var d = (await Service().GetAsync("294", ManagerOfTeam)).Data!;

            Assert.Collection(d.Trail,
                p =>
                {
                    Assert.Equal(StartDate.AddDays(19).ToString("yyyy-MM-dd"), p.AsOfDate);
                    Assert.Equal((100, 0, 0, 0, 10), (p.Cruise, p.Drift, p.Stall, p.Headwinds, p.DaysCounted));   // days 11..20
                },
                p =>
                {
                    Assert.Equal(StartDate.AddDays(26).ToString("yyyy-MM-dd"), p.AsOfDate);
                    Assert.Equal((100, 0, 0, 0, 17), (p.Cruise, p.Drift, p.Stall, p.Headwinds, p.DaysCounted));   // days 11..27
                },
                p =>
                {
                    Assert.Equal(StartDate.AddDays(33).ToString("yyyy-MM-dd"), p.AsOfDate);
                    Assert.Equal((75, 25, 0, 0, 24), (p.Cruise, p.Drift, p.Stall, p.Headwinds, p.DaysCounted));   // 18 cruise, 6 drift
                });
            // The current reading is unchanged by the trail.
            Assert.Equal((60, 23, 17, 10, 30), (d.Cruise, d.Drift, d.Stall, d.Headwinds, d.DaysCounted));
        }

        [Fact]
        public async Task Trail_SkipsAReadingWithNothingCounted()
        {
            using (var db = CreateContext(_db))
            {
                SeedMix(db, Team);
                AddReading(db, Team, 5);    // inside the warm-up: no position to draw
                AddReading(db, Team, 20);
            }

            var d = (await Service().GetAsync("294", ManagerOfTeam)).Data!;

            var point = Assert.Single(d.Trail);
            Assert.Equal(StartDate.AddDays(19).ToString("yyyy-MM-dd"), point.AsOfDate);
        }

        [Fact]
        public async Task Trail_EmptyWhileCalibrating()
        {
            using (var db = CreateContext(_db))
            {
                AddSettings(db, Team, StartDate);
                for (var day = 1; day <= TeamStateService.CalibrationDays; day++) AddDay(db, Team, day, "drift");
                AddReading(db, Team, 3);
            }

            var d = (await Service().GetAsync("294", ProfileAdminA)).Data!;

            Assert.True(d.IsCalibrating);
            Assert.Empty(d.Trail);
        }

        [Theory]
        [InlineData(6, true)]    // six asked days resolved: still calibrating
        [InlineData(7, false)]   // the seventh: a state (MVP: no state before day seven)
        public async Task Calibrating_UntilTheSeventhResolvedDay(int resolvedDays, bool calibrating)
        {
            using (var db = CreateContext(_db))
            {
                AddSettings(db, Team, StartDate);
                for (var day = 1; day <= resolvedDays; day++) AddDay(db, Team, day, "drift");
            }

            var d = (await Service().GetAsync("294", ProfileAdminA)).Data!;

            Assert.Equal(calibrating, d.IsCalibrating);
            Assert.Equal(calibrating ? null : StartDate.AddDays(resolvedDays - 1).ToString("yyyy-MM-dd"), d.AsOfDate);
        }

        [Fact]
        public async Task NoEngineSettings_IsCalibrating_CruiseAllZero()
        {
            var r = await Service().GetAsync("294", ProfileAdminA);

            Assert.True(r.IsSuccess);
            var d = r.Data!;
            Assert.True(d.IsCalibrating);
            Assert.Equal(TeamStates.Cruise, d.State);
            Assert.Equal(0, d.Cruise + d.Drift + d.Headwinds + d.Stall);
            Assert.False(d.HeadwindsFlag);
            Assert.Null(d.AsOfDate);
        }

        [Fact]
        public async Task OnlyOldDays_StateFromLatest_SegmentsZero()
        {
            using (var db = CreateContext(_db))
            {
                AddSettings(db, Team, StartDate);
                AddWarmUp(db, Team);
                AddDay(db, Team, 7, "drift");
            }

            var r = await Service().GetAsync("294", ProfileAdminA);

            Assert.False(r.Data!.IsCalibrating);
            Assert.Equal(TeamStates.Drift, r.Data.State);
            Assert.Equal(0, r.Data.DaysCounted);
            Assert.Equal(0, r.Data.Drift);
        }

        [Fact]
        public async Task OtherTeamsRows_AreIgnored()
        {
            using (var db = CreateContext(_db))
            {
                AddSettings(db, Team, StartDate);
                AddWarmUp(db, Team);
                AddDay(db, Team, 40, "cruise");
                AddDay(db, OtherTeamA, 40, "stall");
            }

            var r = await Service().GetAsync("294", ProfileAdminA);

            Assert.Equal(100, r.Data!.Cruise);
            Assert.Equal(0, r.Data.Stall);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("")]
        public async Task BadTeamId_IsBadRequest(string teamId)
        {
            var r = await Service().GetAsync(teamId, ProfileAdminA);
            Assert.Equal(TeamMilestoneError.BadRequest, r.Error);
        }

        [Theory]
        [InlineData("999999")]
        [InlineData("296")]   // soft-deleted
        public async Task MissingTeam_IsNotFound(string teamId)
        {
            var r = await Service().GetAsync(teamId, ProfileAdminA);
            Assert.Equal(TeamMilestoneError.NotFound, r.Error);
        }

        [Theory]
        [InlineData(ProfileAdminB)]
        [InlineData(UnassignedManager)]
        public async Task NotCallersTeam_IsForbidden(int userId)
        {
            var r = await Service().GetAsync("294", userId);
            Assert.Equal(TeamMilestoneError.Forbidden, r.Error);
        }
    }
}
