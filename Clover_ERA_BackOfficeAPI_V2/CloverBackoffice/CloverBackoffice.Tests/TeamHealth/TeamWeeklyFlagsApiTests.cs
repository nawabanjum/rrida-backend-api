using System.Net;
using System.Text.Json;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.TeamHealth
{
    // HTTP tests for GET /api/teams/{teamId}/weekly-flags, on the shared test host.
    public partial class TeamHealthMilestonesApiTests
    {
        private void SeedFlags()
        {
            using var db = CreateContext(_db);
            TeamWeeklyFlagServiceTests.AddFlag(db, Team, "2026-08-18", "Earlier", "Do this");
            TeamWeeklyFlagServiceTests.AddFlag(db, Team, "2026-08-25", "Several members reflected not feeling acknowledged for their efforts.",
                "Name the win from the last quarter out loud at your next team meeting.");
        }

        [Fact]
        public async Task WeeklyFlags_AllWeeks_Returns200_LatestFirst()
        {
            SeedFlags();

            var r = await Send(HttpMethod.Get, $"/api/teams/{Team}/weekly-flags", ManagerOfTeam);

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var json = await Json(r);
            Assert.True(json.GetProperty("isSuccess").GetBoolean());
            var c = json.GetProperty("content");
            Assert.Equal("294", c.GetProperty("teamId").GetString());
            var flags = c.GetProperty("weeklyFlags");
            Assert.Equal(2, flags.GetArrayLength());
            var first = flags[0];
            Assert.Equal("2026-08-25", first.GetProperty("weekStartDate").GetString());
            Assert.Equal("2026-08-31", first.GetProperty("weekEndDate").GetString());
            Assert.Equal("Week of 25 Aug", first.GetProperty("label").GetString());
            Assert.Equal("Several members reflected not feeling acknowledged for their efforts.", first.GetProperty("whatCameUp").GetString());
            Assert.True(first.TryGetProperty("suggestedAction", out _));
            Assert.True(first.TryGetProperty("id", out _));
        }

        [Fact]
        public async Task WeeklyFlags_SpecificWeek_ReturnsOne()
        {
            SeedFlags();

            var r = await Send(HttpMethod.Get, $"/api/teams/{Team}/weekly-flags?weekStartDate=2026-08-18", ProfileAdminA);

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var flags = (await Json(r)).GetProperty("content").GetProperty("weeklyFlags");
            Assert.Equal(1, flags.GetArrayLength());
            Assert.Equal("Earlier", flags[0].GetProperty("whatCameUp").GetString());
        }

        [Fact]
        public async Task WeeklyFlags_WeekWithoutFlag_Returns200EmptyList()
        {
            SeedFlags();

            var r = await Send(HttpMethod.Get, $"/api/teams/{Team}/weekly-flags?weekStartDate=2026-09-08", ProfileAdminA);

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var json = await Json(r);
            Assert.Equal("Nothing flagged for this team yet.", json.GetProperty("message").GetString());
            Assert.Equal(0, json.GetProperty("content").GetProperty("weeklyFlags").GetArrayLength());
        }

        [Theory]
        [InlineData("/api/teams/abc/weekly-flags")]
        [InlineData("/api/teams/0/weekly-flags")]
        [InlineData("/api/teams/294/weekly-flags?weekStartDate=nope")]
        public async Task WeeklyFlags_BadInput_Returns400(string url)
        {
            var r = await Send(HttpMethod.Get, url, ProfileAdminA);

            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.False((await Json(r)).GetProperty("isSuccess").GetBoolean());
        }

        [Fact]
        public async Task WeeklyFlags_Unauthorised_Returns401_403_404()
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, $"/api/teams/{Team}/weekly-flags", asUser: null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, $"/api/teams/{TeamB}/weekly-flags", ProfileAdminA)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, $"/api/teams/{Team}/weekly-flags", PortfolioManagerNoReports)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, "/api/teams/9999/weekly-flags", RridaAdmin)).StatusCode);
        }
    }
}
