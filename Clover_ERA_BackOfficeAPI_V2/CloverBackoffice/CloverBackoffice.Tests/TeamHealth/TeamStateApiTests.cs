using System.Net;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.TeamHealth
{
    // HTTP tests for GET /api/teams/{teamId}/health/state, on the shared test host.
    public partial class TeamHealthMilestonesApiTests
    {
        [Fact]
        public async Task TeamState_Returns200_WithStateAndSegments()
        {
            using (var db = CreateContext(_db)) TeamStateServiceTests.SeedMix(db, Team);

            var r = await Send(HttpMethod.Get, $"/api/teams/{Team}/health/state", ManagerOfTeam);

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var json = await Json(r);
            Assert.True(json.GetProperty("isSuccess").GetBoolean());
            var c = json.GetProperty("content");
            Assert.Equal("294", c.GetProperty("teamId").GetString());
            Assert.Equal("Team 294", c.GetProperty("teamName").GetString());
            Assert.Equal("STALL", c.GetProperty("state").GetString());
            Assert.True(c.GetProperty("headwindsFlag").GetBoolean());
            Assert.Equal(60, c.GetProperty("cruise").GetInt32());
            Assert.Equal(23, c.GetProperty("drift").GetInt32());
            Assert.Equal(17, c.GetProperty("stall").GetInt32());
            Assert.Equal(10, c.GetProperty("headwinds").GetInt32());
        }

        [Fact]
        public async Task TeamState_UnknownTeam_Returns404()
        {
            var r = await Send(HttpMethod.Get, "/api/teams/999999/health/state", ProfileAdminA);
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        }

        [Fact]
        public async Task TeamState_OtherCompany_Returns403()
        {
            var r = await Send(HttpMethod.Get, $"/api/teams/{Team}/health/state", ProfileAdminB);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }

        [Fact]
        public async Task TeamState_BadTeamId_Returns400()
        {
            var r = await Send(HttpMethod.Get, "/api/teams/abc/health/state", ProfileAdminA);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }
    }
}
