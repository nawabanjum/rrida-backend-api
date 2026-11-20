using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CloverBackoffice.API.Controllers;
using CloverBackoffice.API.Filters;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.TeamHealth
{
    /// <summary>
    /// Integration tests over HTTP: the real TeamHealthController, the global
    /// CompanyTenantGuardFilter, TenantAccessService and TeamMilestoneService, on an
    /// in-memory database. Only authentication and the token reader are test doubles
    /// (the caller is named by the X-Test-User header).
    /// </summary>
    public partial class TeamHealthMilestonesApiTests : IAsyncLifetime
    {
        private const string Route = "/api/team-health/milestones";
        private readonly string _db = Guid.NewGuid().ToString();
        private IHost _host = null!;
        private HttpClient _client = null!;

        public async Task InitializeAsync()
        {
            using (var db = CreateContext(_db))
            {
                Seed(db);
                TimelineTestData.SeedBanks(db);
                CustomQuestionTestData.SeedCulture(db);
            }

            var dbName = _db;
            _host = await new HostBuilder()
                .ConfigureWebHost(web =>
                {
                    web.UseTestServer();
                    web.ConfigureServices(services =>
                    {
                        services.AddScoped<ApplicationDbContext>(_ => CreateContext(dbName));
                        services.AddHttpContextAccessor();
                        services.AddSingleton<TimeProvider>(new FixedClock(Today));
                        services.AddSingleton(Roles());
                        services.AddSingleton(TimelineTestData.Thresholds());
                        services.AddScoped<ITokenReaderService, HeaderTokenReader>();
                        services.AddScoped<ITenantAccessService, TenantAccessService>();
                        services.AddScoped<ITeamHealthAccessService, TeamHealthAccessService>();
                        services.AddScoped<ITeamMilestoneService, TeamMilestoneService>();
                        services.AddScoped<ITeamHealthTimelineService, TeamHealthTimelineService>();
                        services.AddScoped<ITeamCustomQuestionService, TeamCustomQuestionService>();
                        services.AddScoped<ITeamActionService, TeamActionService>();
                        services.AddScoped<ITeamWeeklyFlagService, TeamWeeklyFlagService>();
                        services.AddMemoryCache();
                        services.AddSingleton(_questions.Object);
                        services.AddScoped<ITeamCalendarInviteService, TeamCalendarInviteService>();
                        services.AddScoped<ITeamStateService, TeamStateService>();
                        services.AddScoped<ITeamHealthUserTeamsService, TeamHealthUserTeamsService>();
                        // Plan enforcement: no plan catalogue in this database, so nothing is limited.
                        services.AddScoped<IPlanEntitlementService, PlanEntitlementService>();
                        services.AddScoped<IPlanFeatureGate, PlanFeatureGate>();
                        AddPortfolioServices(services);

                        services.AddAuthentication(TestAuth.Scheme)
                            .AddScheme<AuthenticationSchemeOptions, TestAuth>(TestAuth.Scheme, _ => { });
                        services.AddAuthorization();

                        services.AddControllers(o => o.Filters.Add<CompanyTenantGuardFilter>())
                            .AddApplicationPart(typeof(TeamHealthController).Assembly);
                    });
                    web.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(e => e.MapControllers());
                    });
                })
                .StartAsync();

            _client = _host.GetTestClient();
        }

        public async Task DisposeAsync()
        {
            _client.Dispose();
            await _host.StopAsync();
            _host.Dispose();
        }

        private static object ValidBody(uint unitId) => new
        {
            unitId,
            title = "New leader joined",
            description = "A new team leader joined the Support Desk team.",
            date = "2026-09-29"
        };

        private Task<HttpResponseMessage> Post(object body, int? asUser) =>
            Send(HttpMethod.Post, Route, asUser, JsonContent.Create(body));

        private Task<HttpResponseMessage> Send(HttpMethod method, string url, int? asUser, HttpContent? content = null)
        {
            var request = new HttpRequestMessage(method, url) { Content = content };
            if (asUser.HasValue)
                request.Headers.Add(TestAuth.Header, asUser.Value.ToString());
            return _client.SendAsync(request);
        }

        private static async Task<JsonElement> Json(HttpResponseMessage response) =>
            JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        [Fact]
        public async Task Post_Valid_Returns200WithMilestone_AndPersists()
        {
            var response = await Post(ValidBody(Team), ProfileAdminA);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await Json(response);
            Assert.True(json.GetProperty("isSuccess").GetBoolean());
            var content = json.GetProperty("content");
            Assert.True(content.GetProperty("id").GetUInt32() > 0);
            Assert.Equal("New leader joined", content.GetProperty("title").GetString());
            Assert.Equal("A new team leader joined the Support Desk team.", content.GetProperty("description").GetString());
            Assert.Equal("2026-09-29", content.GetProperty("date").GetString());

            using var db = CreateContext(_db);
            var row = await db.TeamMilestones.SingleAsync();
            Assert.Equal(Team, row.UnitId);
            Assert.Equal(ProfileA, row.ProfileId);
        }

        [Fact]
        public async Task Post_DdMmYyyyDate_IsStoredAsIso()
        {
            var response = await Post(new { unitId = Team, title = "Team member left", description = "Team went 7 to 6.", date = "30-06-2026" }, ManagerOfTeam);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("2026-06-30", (await Json(response)).GetProperty("content").GetProperty("date").GetString());
        }

        [Fact]
        public async Task Post_ThenGet_MilestoneIsOnTheTimeline()
        {
            await Post(new { unitId = Team, title = "Two leavers", description = "Team went 9 to 7.", date = "2026-07-07" }, ManagerOfTeam);

            var response = await Send(HttpMethod.Get, $"{Route}?unitId={Team}&from=2026-07-01&to=2026-09-29", ManagerOfTeam);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var items = (await Json(response)).GetProperty("content").EnumerateArray().ToList();
            var item = Assert.Single(items);
            Assert.Equal("Two leavers", item.GetProperty("title").GetString());
            Assert.Equal("2026-07-07", item.GetProperty("date").GetString());
        }

        [Fact]
        public async Task Post_PortfolioManagerOverTeamManager_Returns200()
        {
            var response = await Post(ValidBody(Team), PortfolioManagerOfTeam);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Post_PortfolioManagerWithNoReports_Returns403()
        {
            var response = await Post(ValidBody(Team), PortfolioManagerNoReports);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Post_WithId_EditsMilestone_Returns200Updated()
        {
            var created = await Json(await Post(ValidBody(Team), ProfileAdminA));
            var id = created.GetProperty("content").GetProperty("id").GetUInt32();

            var response = await Post(new { id, unitId = Team, title = "Leader changed", description = "Updated text.", date = "01-09-2026" }, ManagerOfTeam);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await Json(response);
            Assert.Equal("Milestone updated.", json.GetProperty("message").GetString());
            var content = json.GetProperty("content");
            Assert.Equal(id, content.GetProperty("id").GetUInt32());
            Assert.Equal("Leader changed", content.GetProperty("title").GetString());
            Assert.Equal("2026-09-01", content.GetProperty("date").GetString());

            using var db = CreateContext(_db);
            var row = await db.TeamMilestones.SingleAsync();
            Assert.Equal("Updated text.", row.Description);
        }

        [Fact]
        public async Task Post_WithUnknownId_Returns404()
        {
            var response = await Post(new { id = 9999, unitId = Team, title = "t", description = "d", date = "2026-09-01" }, ProfileAdminA);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Post_EditOtherCompanysMilestone_Returns403()
        {
            var response = await Post(new { id = 1, unitId = TeamB, title = "t", description = "d", date = "2026-09-01" }, ProfileAdminA);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Post_NoToken_Returns401()
        {
            var response = await Post(ValidBody(Team), asUser: null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Post_OtherCompanysTeam_Returns403FromTenantGuard()
        {
            var response = await Post(ValidBody(TeamB), ProfileAdminA);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using var db = CreateContext(_db);
            Assert.Empty(await db.TeamMilestones.ToListAsync());
        }

        [Fact]
        public async Task Post_TeamManagerNotOnTeam_Returns403()
        {
            var response = await Post(ValidBody(OtherTeamA), ManagerOfTeam);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Post_UnknownTeam_Returns404()
        {
            var response = await Post(ValidBody(9999), RridaAdmin);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        public static IEnumerable<object[]> BadBodies() => new[]
        {
            new object[] { new { unitId = Team, description = "d", date = "2026-09-29" } },
            new object[] { new { unitId = Team, title = new string('t', 41), description = "d", date = "2026-09-29" } },
            new object[] { new { unitId = Team, title = "t", date = "2026-09-29" } },
            new object[] { new { unitId = Team, title = "t", description = "d" } },
            new object[] { new { unitId = Team, title = "t", description = "d", date = "2026-02-30" } },
            new object[] { new { unitId = Team, title = "t", description = "d", date = "2026-12-01" } },
        };

        [Theory]
        [MemberData(nameof(BadBodies))]
        public async Task Post_InvalidBody_Returns400(object body)
        {
            var response = await Post(body, ProfileAdminA);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False((await Json(response)).GetProperty("isSuccess").GetBoolean());
        }

        [Fact]
        public async Task Post_MissingUnitId_Returns400()
        {
            var response = await Post(new { title = "t", description = "d", date = "2026-09-29" }, ProfileAdminA);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Post_MalformedJson_Returns400()
        {
            var response = await Send(HttpMethod.Post, Route, ProfileAdminA,
                new StringContent("{ \"title\": ", Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Get_OtherCompanysTeam_Returns403()
        {
            var response = await Send(HttpMethod.Get, $"{Route}?unitId={TeamB}", ProfileAdminA);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // ── GET /api/team-health/timeline ──────────────────────────────────

        private const string TimelineRoute = "/api/team-health/timeline";

        [Fact]
        public async Task Timeline_Returns200_WithFullContract()
        {
            using (var db = CreateContext(_db))
            {
                TimelineTestData.IndividualAnswer(db, Team, "2026-09-24", CloverBackoffice.Infrastructure.Models.CloverCategory.Communication, TimelineTestData.Happy, count: 4);
                TimelineTestData.TeamAnswer(db, Team, "2026-09-24", CloverBackoffice.Infrastructure.Models.TeamQuestions.TeamSubCategory.ImprovementLoop, TimelineTestData.Indifferent);
            }

            var response = await Send(HttpMethod.Get, $"{TimelineRoute}?teamId={Team}", ManagerOfTeam);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await Json(response);
            Assert.True(json.GetProperty("isSuccess").GetBoolean());
            var c = json.GetProperty("content");

            Assert.Equal("294", c.GetProperty("teamId").GetString());

            var timeline = c.GetProperty("timeline");
            Assert.Equal("2026-08-31", timeline.GetProperty("period").GetProperty("startDate").GetString());
            Assert.Equal("2026-09-29", timeline.GetProperty("period").GetProperty("endDate").GetString());
            Assert.Equal(100, timeline.GetProperty("individualEngagement").GetProperty("current").GetInt32());
            Assert.Equal(JsonValueKind.Null, timeline.GetProperty("individualEngagement").GetProperty("change").ValueKind);
            Assert.Equal("2026-09-24", timeline.GetProperty("individualEngagement").GetProperty("data")[0].GetProperty("date").GetString());
            Assert.Equal(67, timeline.GetProperty("teamConditions").GetProperty("current").GetInt32());
            Assert.Equal("last_7_working_days", timeline.GetProperty("responseRate").GetProperty("period").GetString());
            Assert.Equal(JsonValueKind.Array, timeline.GetProperty("milestones").ValueKind);

            var comm = c.GetProperty("individualEngagementDetails").GetProperty("communication");
            Assert.Equal(100, comm.GetProperty("score").GetInt32());
            Assert.Equal("EXCELLENT", comm.GetProperty("status").GetString());
            var learning = c.GetProperty("individualEngagementDetails").GetProperty("learning");
            Assert.Equal(JsonValueKind.Null, learning.GetProperty("score").ValueKind);
            Assert.Equal(JsonValueKind.Null, learning.GetProperty("status").ValueKind);
            foreach (var name in new[] { "opportunities", "vulnerability", "enablement", "reflection" })
                Assert.True(c.GetProperty("individualEngagementDetails").TryGetProperty(name, out _), name);

            var dl = c.GetProperty("teamConditionsDetails").GetProperty("directionAndLearning");
            Assert.Equal("GOOD", dl.GetProperty("status").GetString());
            Assert.Equal("Improvement Loop", dl.GetProperty("lowest").GetProperty("name").GetString());
            Assert.Equal(67, dl.GetProperty("lowest").GetProperty("score").GetInt32());
            Assert.Equal(JsonValueKind.Null, c.GetProperty("teamConditionsDetails").GetProperty("whatTeamBelieves").GetProperty("lowest").ValueKind);
            Assert.True(c.GetProperty("teamConditionsDetails").TryGetProperty("designAndMechanics", out _));

            var rr = c.GetProperty("responseRateDetails");
            Assert.Equal(7, rr.GetProperty("data").GetArrayLength());
            Assert.Equal(5, rr.GetProperty("summary").GetProperty("answers").GetInt32());
            Assert.Equal(56, rr.GetProperty("summary").GetProperty("possible").GetInt32());
            Assert.Equal(9, rr.GetProperty("current").GetInt32()); // 5 / 56
        }

        [Theory]
        [InlineData("")]
        [InlineData("?teamId=")]
        [InlineData("?teamId=abc")]
        [InlineData("?teamId=0")]
        public async Task Timeline_InvalidTeamId_Returns400(string query)
        {
            var response = await Send(HttpMethod.Get, TimelineRoute + query, ProfileAdminA);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False((await Json(response)).GetProperty("isSuccess").GetBoolean());
        }

        [Fact]
        public async Task Timeline_NoToken_Returns401()
        {
            var response = await Send(HttpMethod.Get, $"{TimelineRoute}?teamId={Team}", asUser: null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Timeline_OtherCompanysTeam_Returns403()
        {
            var response = await Send(HttpMethod.Get, $"{TimelineRoute}?teamId={TeamB}", ProfileAdminA);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Timeline_UnknownTeam_Returns404()
        {
            var response = await Send(HttpMethod.Get, $"{TimelineRoute}?teamId=9999", RridaAdmin);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // ── Test doubles ───────────────────────────────────────────────────

        private sealed class TestAuth : AuthenticationHandler<AuthenticationSchemeOptions>
        {
            public const string Scheme = "Test";
            public const string Header = "X-Test-User";

            public TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
                : base(options, logger, encoder) { }

            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                if (!Request.Headers.TryGetValue(Header, out var id))
                    return Task.FromResult(AuthenticateResult.NoResult());

                var identity = new ClaimsIdentity(new[] { new Claim("UserId", id.ToString()) }, Scheme);
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
            }
        }

        private sealed class HeaderTokenReader : ITokenReaderService
        {
            private readonly IHttpContextAccessor _http;
            private readonly ApplicationDbContext _db;

            public HeaderTokenReader(IHttpContextAccessor http, ApplicationDbContext db)
            {
                _http = http;
                _db = db;
            }

            public int? GetUserIdFromToken() =>
                int.TryParse(_http.HttpContext?.User.FindFirst("UserId")?.Value, out var id) ? id : null;

            public async Task<User?> GetCurrentUserAsync()
            {
                var id = GetUserIdFromToken();
                return id == null ? null : await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == id);
            }

            public string GetToken() => string.Empty;
            public int? GetTokenInfo() => GetUserIdFromToken();
            public Task<bool> IsAdminAsync() => Task.FromResult(false);
        }
    }
}
