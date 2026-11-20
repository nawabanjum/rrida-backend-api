using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.TeamHealth
{
    // HTTP tests for /api/team-health/custom-questions, on the same test host.
    public partial class TeamHealthMilestonesApiTests
    {
        private const string CqRoute = "/api/team-health/custom-questions";

        private Task<HttpResponseMessage> ScheduleCq(object body, int? asUser) =>
            Send(HttpMethod.Post, CqRoute, asUser, JsonContent.Create(body));

        [Fact]
        public async Task CustomQuestions_ScheduleListRemove_RoundTrip()
        {
            var post = await ScheduleCq(new { teamId = Team, question = "Is the new handover checklist saving you time yet?", askOn = "2026-10-06" }, ManagerOfTeam);

            Assert.Equal(HttpStatusCode.OK, post.StatusCode);
            var content = (await Json(post)).GetProperty("content");
            Assert.Equal("294", content.GetProperty("teamId").GetString());
            Assert.Equal(3, content.GetProperty("maxSlots").GetInt32());
            Assert.Equal(1, content.GetProperty("slotsUsed").GetInt32());
            var item = content.GetProperty("scheduled")[0];
            Assert.Equal("2026-10-06", item.GetProperty("askOn").GetString());
            Assert.True(item.GetProperty("canRemove").GetBoolean());
            Assert.Equal(JsonValueKind.Array, content.GetProperty("previouslyAsked").ValueKind);
            var id = item.GetProperty("id").GetUInt32();

            var get = await Send(HttpMethod.Get, $"{CqRoute}?teamId={Team}", ManagerOfTeam);
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            Assert.Equal(1, (await Json(get)).GetProperty("content").GetProperty("slotsUsed").GetInt32());

            var delete = await Send(HttpMethod.Delete, $"{CqRoute}/{id}", ManagerOfTeam);
            Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
            var after = await Json(delete);
            Assert.Equal("Question removed.", after.GetProperty("message").GetString());
            Assert.Equal(0, after.GetProperty("content").GetProperty("slotsUsed").GetInt32());
        }

        [Fact]
        public async Task CustomQuestions_Weekend_Returns400WithMessage()
        {
            var r = await ScheduleCq(new { teamId = Team, question = "Q", askOn = "2026-10-03" }, ProfileAdminA);

            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            var json = await Json(r);
            Assert.False(json.GetProperty("isSuccess").GetBoolean());
            Assert.StartsWith("That is a weekend.", json.GetProperty("message").GetString());
        }

        [Fact]
        public async Task CustomQuestions_FourthSlot_Returns400()
        {
            foreach (var day in new[] { "2026-09-30", "2026-10-01", "2026-10-02" })
                Assert.Equal(HttpStatusCode.OK, (await ScheduleCq(new { teamId = Team, question = "Q " + day, askOn = day }, ProfileAdminA)).StatusCode);

            var r = await ScheduleCq(new { teamId = Team, question = "Q fourth", askOn = "2026-10-05" }, ProfileAdminA);

            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }

        [Fact]
        public async Task CustomQuestions_OtherCompany_Returns403()
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, $"{CqRoute}?teamId={TeamB}", ProfileAdminA)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ScheduleCq(new { teamId = TeamB, question = "Q", askOn = "2026-10-06" }, ProfileAdminA)).StatusCode);
        }

        [Fact]
        public async Task CustomQuestions_UnknownIds_Return404()
        {
            Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, $"{CqRoute}?teamId=9999", RridaAdmin)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Delete, $"{CqRoute}/9999", RridaAdmin)).StatusCode);
        }

        [Fact]
        public async Task CustomQuestions_NoToken_Returns401()
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, $"{CqRoute}?teamId={Team}", asUser: null)).StatusCode);
        }

        [Fact]
        public async Task CustomQuestions_MissingTeamId_Returns400()
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, CqRoute, ProfileAdminA)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await ScheduleCq(new { question = "Q", askOn = "2026-10-06" }, ProfileAdminA)).StatusCode);
        }
    }
}
