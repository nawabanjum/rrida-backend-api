using System.Net;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Services.Implementation;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;
using static CloverBackoffice.Tests.TeamHealth.TeamStateServiceTests;

namespace CloverBackoffice.Tests.TeamHealth
{
    /// <summary>
    /// GET api/teams/{teamId}/health/state?days= — content.history, the trend chart: one row per
    /// calculated day in the last 30, 60 or 90 days, ending on the NOW date, oldest first.
    /// </summary>
    public class TeamStateHistoryTests
    {
        private readonly string _db = Guid.NewGuid().ToString();

        // A team 100 days in: today is day 100.
        private static readonly DateOnly LongStart = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-99);

        public TeamStateHistoryTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
        }

        private async Task<TeamStateDto> Get(int? days = null)
        {
            using var db = CreateContext(_db);
            var r = await new TeamStateService(db, new TeamHealthAccessService(db, Roles())).GetAsync("294", ProfileAdminA, default, days);
            Assert.True(r.IsSuccess, r.Message);
            return r.Data!;
        }

        private static string D(DateOnly start, int day) => start.AddDays(day - 1).ToString("yyyy-MM-dd");

        private void SeedLong()
        {
            using var db = CreateContext(_db);
            AddSettings(db, Team, LongStart);
            AddWarmUp(db, Team);
            for (var day = 7; day <= 100; day++) AddDay(db, Team, day, day % 3 == 0 ? "drift" : "cruise");
        }

        [Theory]
        [InlineData(30, 71)]
        [InlineData(60, 41)]
        [InlineData(90, 11)]
        public async Task Window_IsTheRequestedDays_EndingOnNow_Inclusive(int days, int firstDay)
        {
            SeedLong();

            var h = (await Get(days)).History;

            Assert.Equal(days, h.PeriodDays);
            Assert.Equal(D(LongStart, firstDay), h.FromDate);
            Assert.Equal(D(LongStart, 100), h.ToDate);
            Assert.Equal(days, h.DataPoints.Count);   // every day calculated here, one row each
            Assert.Equal(D(LongStart, firstDay), h.DataPoints[0].AsOfDate);
            Assert.Equal(D(LongStart, 100), h.DataPoints[^1].AsOfDate);
            Assert.Equal(h.DataPoints.Select(p => p.AsOfDate).OrderBy(x => x), h.DataPoints.Select(p => p.AsOfDate));
            Assert.Equal(h.DataPoints.Count, h.DataPoints.Select(p => p.AsOfDate).Distinct().Count());
        }

        [Theory]
        [InlineData(null)]
        [InlineData(45)]
        [InlineData(0)]
        [InlineData(-30)]
        [InlineData(365)]
        public async Task MissingOrInvalidDays_Is60(int? days)
        {
            SeedLong();

            var h = (await Get(days)).History;

            Assert.Equal(60, h.PeriodDays);
            Assert.Equal(D(LongStart, 41), h.FromDate);
            Assert.Equal(60, h.DataPoints.Count);
        }

        [Fact]
        public async Task EveryRow_HasAState_AndSharesSumTo100()
        {
            SeedLong();

            var h = (await Get(90)).History;

            Assert.All(h.DataPoints, p =>
            {
                Assert.Contains(p.State, new[] { TeamStates.Cruise, TeamStates.Drift, TeamStates.Stall });
                Assert.Equal(100, p.Cruise + p.Drift + p.Stall);
                Assert.InRange(p.Headwinds, 0, 100);
            });
        }

        [Fact]
        public async Task State_IsTheDaysOwn_NotTheLargestShare()
        {
            using (var db = CreateContext(_db))
            {
                AddSettings(db, Team, StartDate);   // today is day 40
                AddWarmUp(db, Team);
                for (var day = 30; day <= 38; day++) AddDay(db, Team, day, "drift");
                AddDay(db, Team, 39, "cruise");
            }

            var d = await Get();
            var row = d.History.DataPoints[^1];

            Assert.Equal(TeamStates.Cruise, row.State);   // drift has 90% of the reading
            Assert.Equal((10, 90, 0), (row.Cruise, row.Drift, row.Stall));
            Assert.Equal(10, row.DaysCounted);
        }

        [Fact]
        public async Task Now_IsTheLastRow_AndMatchesTheSummary()
        {
            using (var db = CreateContext(_db)) SeedMix(db, Team);   // latest day 40 (day 41 is tomorrow)

            var d = await Get();

            Assert.False(d.IsCalibrating);
            var now = Assert.Single(d.History.DataPoints, p => p.AsOfDate == d.AsOfDate);
            Assert.Same(now, d.History.DataPoints[^1]);
            Assert.Equal(d.State, now.State);
            Assert.Equal(d.AsOfDate, d.History.ToDate);
            Assert.DoesNotContain(d.History.DataPoints, p => p.AsOfDate == D(StartDate, 41));   // a future day
            Assert.DoesNotContain(d.History.DataPoints, p => p.AsOfDate == D(StartDate, 6));    // the warm-up
        }

        [Fact]
        public async Task Now_IsInTheWindow_EvenWhenTheLastRunWasLongAgo()
        {
            using (var db = CreateContext(_db))
            {
                AddSettings(db, Team, LongStart);   // today is day 100
                AddWarmUp(db, Team);
                AddDay(db, Team, 7, "drift");
                AddDay(db, Team, 8, "stall");      // nothing since
            }

            var d = await Get(30);

            Assert.Equal(D(LongStart, 8), d.AsOfDate);
            Assert.Equal(D(LongStart, 8), d.History.ToDate);
            Assert.Equal(new[] { D(LongStart, 7), D(LongStart, 8) }, d.History.DataPoints.Select(p => p.AsOfDate));
            Assert.Equal(TeamStates.Stall, d.History.DataPoints[^1].State);
        }

        [Fact]
        public async Task MissingDays_HaveNoRow()
        {
            using (var db = CreateContext(_db))
            {
                AddSettings(db, Team, StartDate);
                AddWarmUp(db, Team);
                AddDay(db, Team, 20, "cruise");
                AddDay(db, Team, 23, "drift");     // 21, 22: weekend, not calculated
                AddDay(db, Team, 24, "drift");
            }

            var h = (await Get()).History;

            Assert.Equal(new[] { D(StartDate, 20), D(StartDate, 23), D(StartDate, 24) }, h.DataPoints.Select(p => p.AsOfDate));
        }

        [Fact]
        public async Task Headwinds_OneEventPerFlaggedDate_EachOnARow()
        {
            using (var db = CreateContext(_db)) SeedMix(db, Team);   // flag on days 38..40

            var h = (await Get(30)).History;

            var flagged = h.DataPoints.Where(p => p.HeadwindsFlag).Select(p => p.AsOfDate).ToList();
            Assert.Equal(new[] { D(StartDate, 38), D(StartDate, 39), D(StartDate, 40) }, flagged);
            Assert.Equal(flagged, h.HeadwindsEvents.Select(e => e.Date));
            Assert.Equal(h.HeadwindsEvents.Count, h.HeadwindsEvents.Select(e => e.Date).Distinct().Count());
            Assert.All(h.HeadwindsEvents, e => Assert.Contains(h.DataPoints, p => p.AsOfDate == e.Date));
        }

        [Fact]
        public async Task Calibrating_ReturnsAnEmptyWindow()
        {
            using (var db = CreateContext(_db))
            {
                AddSettings(db, Team, StartDate);
                for (var day = 1; day <= TeamStateService.CalibrationDays; day++) AddDay(db, Team, day, "drift", headwinds: true);
            }

            var d = await Get(90);

            Assert.True(d.IsCalibrating);
            Assert.Empty(d.History.DataPoints);
            Assert.Empty(d.History.HeadwindsEvents);
            Assert.Equal(90, d.History.PeriodDays);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            Assert.Equal(today.ToString("yyyy-MM-dd"), d.History.ToDate);
            Assert.Equal(today.AddDays(-89).ToString("yyyy-MM-dd"), d.History.FromDate);
        }

        [Fact]
        public async Task NoEngineSettings_ReturnsAnEmptyWindow()
        {
            var d = await Get(30);

            Assert.True(d.IsCalibrating);
            Assert.Empty(d.History.DataPoints);
            Assert.Empty(d.History.HeadwindsEvents);
            Assert.Equal(30, d.History.PeriodDays);
        }

        [Fact]
        public async Task TopLevelFieldsAndTrail_AreUnchanged()
        {
            using (var db = CreateContext(_db))
            {
                SeedMix(db, Team);
                foreach (var day in new[] { 20, 27, 34 }) AddReading(db, Team, day);
            }

            var d = await Get(30);

            Assert.Equal((TeamStates.Stall, true, 60, 23, 17, 10, 30), (d.State, d.HeadwindsFlag, d.Cruise, d.Drift, d.Stall, d.Headwinds, d.DaysCounted));
            Assert.Equal(3, d.Trail.Count);
        }
    }

    // HTTP: ?days= on the existing route.
    public partial class TeamHealthMilestonesApiTests
    {
        [Theory]
        [InlineData("?days=30", 30)]
        [InlineData("?days=90", 90)]
        [InlineData("", 60)]
        [InlineData("?days=abc", 60)]
        [InlineData("?days=45", 60)]
        public async Task TeamState_History_UsesDays(string query, int expected)
        {
            using (var db = CreateContext(_db)) SeedMix(db, Team);

            var r = await Send(HttpMethod.Get, $"/api/teams/{Team}/health/state{query}", ManagerOfTeam);

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var c = (await Json(r)).GetProperty("content");
            Assert.True(c.TryGetProperty("trail", out _));   // kept for other clients
            var h = c.GetProperty("history");
            Assert.Equal(expected, h.GetProperty("periodDays").GetInt32());
            Assert.Equal(c.GetProperty("asOfDate").GetString(), h.GetProperty("toDate").GetString());
            var rows = h.GetProperty("dataPoints");
            Assert.Equal(30, rows.GetArrayLength());   // days 11..40 are the counted days
            var now = rows[rows.GetArrayLength() - 1];
            Assert.Equal(c.GetProperty("state").GetString(), now.GetProperty("state").GetString());
            Assert.Equal(3, h.GetProperty("headwindsEvents").GetArrayLength());
            Assert.False(h.GetProperty("headwindsEvents")[0].TryGetProperty("severity", out _));
        }
    }
}
