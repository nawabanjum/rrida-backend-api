using System.Net;
using System.Net.Http.Json;
using System.Text;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.TeamHealth
{
    // HTTP tests for POST /api/team-health/user-teams, on the same test host.
    public partial class TeamHealthMilestonesApiTests
    {
        private const string UserTeamsRoute = "/api/team-health/user-teams";

        [Fact]
        public async Task UserTeams_AcceptsUserIDCasing_Returns200WithTeams()
        {
            // The ticket's payload spells it "userID".
            var body = new StringContent($"{{\"userID\": {ManagerOfTeam}}}", Encoding.UTF8, "application/json");
            var response = await Send(HttpMethod.Post, UserTeamsRoute, ManagerOfTeam, body);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var content = (await Json(response)).GetProperty("content");
            Assert.Equal(ManagerOfTeam, content.GetProperty("userId").GetInt32());
            var team = content.GetProperty("teams")[0];
            Assert.Equal(Team, team.GetProperty("teamId").GetUInt32());
            Assert.Equal("Team 294", team.GetProperty("teamName").GetString());
        }

        [Fact]
        public async Task UserTeams_NoTeams_Returns200WithEmptyList()
        {
            var response = await Send(HttpMethod.Post, UserTeamsRoute, UnassignedManager,
                JsonContent.Create(new { userId = UnassignedManager }));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(0, (await Json(response)).GetProperty("content").GetProperty("teams").GetArrayLength());
        }

        [Fact]
        public async Task UserTeams_AnotherCompanysUser_Returns403()
        {
            var response = await Send(HttpMethod.Post, UserTeamsRoute, ProfileAdminB,
                JsonContent.Create(new { userId = ManagerOfTeam }));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task UserTeams_AnotherManagerInSameCompany_Returns403()
        {
            var response = await Send(HttpMethod.Post, UserTeamsRoute, ManagerOfParent,
                JsonContent.Create(new { userId = ManagerOfTeam }));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task UserTeams_UnknownUser_Returns404()
        {
            var response = await Send(HttpMethod.Post, UserTeamsRoute, RridaAdmin,
                JsonContent.Create(new { userId = 9999 }));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task UserTeams_MissingUserId_Returns400()
        {
            var response = await Send(HttpMethod.Post, UserTeamsRoute, ManagerOfTeam, JsonContent.Create(new { }));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task UserTeams_NoToken_Returns401()
        {
            var response = await Send(HttpMethod.Post, UserTeamsRoute, null, JsonContent.Create(new { userId = ManagerOfTeam }));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
