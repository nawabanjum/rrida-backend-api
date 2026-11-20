using System.Net;
using System.Net.Http.Json;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;
using ProfileEntity = CloverBackoffice.Infrastructure.Models.Profile;

namespace CloverBackoffice.Tests.TeamHealth
{
    // HTTP tests for api/portfolio and the Portfolio turn of POST api/eran-coach/chat, on the
    // shared test host, so the tenant guard runs. Profile B has no managers, so its calls stay
    // off the engine.
    public partial class TeamHealthMilestonesApiTests
    {
        private const string Portfolio = "/api/portfolio";
        private const string Chat = "/api/eran-coach/chat";
        private const uint UnknownProfile = 9999;

        private static readonly string[] PortfolioRoutes = { "managers", "teams", "alerts", "eran-context" };

        private readonly Mock<IEranCoachChatService> _chat = new();
        private readonly Mock<IEranCoachConversationService> _conversations = new();

        private void AddPortfolioServices(IServiceCollection services)
        {
            services.AddScoped<IPortfolioViewService, PortfolioViewService>();
            services.AddSingleton(Mock.Of<IRridaEngineContextFactory>());
            services.AddSingleton(_chat.Object);
            services.AddSingleton(_conversations.Object);
            services.AddSingleton(Mock.Of<IEranCoachSeedService>());
            services.AddSingleton(Mock.Of<IEranCoachTimelineService>());
            services.AddSingleton(Mock.Of<IEranCoachModelConfigService>());
            services.AddSingleton<EranCoachSeedWorkQueue>();
            services.AddSingleton<EranCoachSeedRunState>();
        }

        private void SeedProfiles()
        {
            using var db = CreateContext(_db);
            foreach (var id in new[] { ProfileA, ProfileB })
                db.Profiles.Add(new ProfileEntity
                {
                    Id = id,
                    ProfileShortCode = $"P{id}",
                    DomainName = $"p{id}.test",
                    ProfileName = $"Profile {id}",
                    RegistrationDate = DateTime.UtcNow
                });
            db.SaveChanges();
        }

        private static object ChatBody(uint? profileId, int? managerId = null) => new
        {
            portfolio = true,
            profileId,
            managerId,
            messages = new[] { new { role = "user", content = "Which team needs me?" } }
        };

        private Task<HttpResponseMessage> SendChat(object body, int asUser) =>
            Send(HttpMethod.Post, Chat, asUser, JsonContent.Create(body));

        private static async IAsyncEnumerable<string> OneChunk()
        {
            await Task.CompletedTask;
            yield return "Nothing needs you this week.";
        }

        [Fact]
        public async Task Portfolio_RridaAdmin_GetsTheManagersOfTheSelectedProfile()
        {
            SeedProfiles();

            var r = await Send(HttpMethod.Get, $"{Portfolio}/managers?profileId={ProfileA}", RridaAdmin);

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var content = (await Json(r)).GetProperty("content");
            Assert.Equal(ProfileA, content.GetProperty("profileId").GetUInt32());
            var ids = content.GetProperty("managers").EnumerateArray().Select(m => m.GetProperty("managerId").GetInt32()).ToList();
            Assert.Contains(ManagerOfTeam, ids);
            Assert.Contains(PortfolioManagerOfTeam, ids);
            Assert.DoesNotContain(ProfileAdminA, ids);
        }

        [Fact]
        public async Task Portfolio_RridaAdmin_EveryRouteAnswersForTheSelectedProfile()
        {
            SeedProfiles();

            foreach (var route in PortfolioRoutes)
            {
                var r = await Send(HttpMethod.Get, $"{Portfolio}/{route}?profileId={ProfileB}", RridaAdmin);
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);
                Assert.True((await Json(r)).GetProperty("isSuccess").GetBoolean());
            }
        }

        [Fact]
        public async Task Portfolio_RridaAdmin_WithoutAProfile_Returns400()
        {
            SeedProfiles();

            foreach (var route in PortfolioRoutes)
            {
                var r = await Send(HttpMethod.Get, $"{Portfolio}/{route}", RridaAdmin);
                Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
                Assert.Equal(ResponseMessages.Errors.PortfolioProfileRequired, (await Json(r)).GetProperty("message").GetString());
            }
        }

        [Fact]
        public async Task Portfolio_RridaAdmin_UnknownProfile_Returns404()
        {
            SeedProfiles();

            foreach (var route in PortfolioRoutes)
                Assert.Equal(HttpStatusCode.NotFound,
                    (await Send(HttpMethod.Get, $"{Portfolio}/{route}?profileId={UnknownProfile}", RridaAdmin)).StatusCode);
        }

        [Fact]
        public async Task Portfolio_PortfolioManager_KeepsWorkingWithoutAProfile()
        {
            SeedProfiles();

            var r = await Send(HttpMethod.Get, $"{Portfolio}/managers", PortfolioManagerOfTeam);

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var managers = (await Json(r)).GetProperty("content").GetProperty("managers").EnumerateArray().ToList();
            Assert.Equal(ManagerOfTeam, Assert.Single(managers).GetProperty("managerId").GetInt32());
        }

        [Fact]
        public async Task Portfolio_PortfolioManager_AnotherProfile_IsRefusedByTheTenantGuard()
        {
            SeedProfiles();

            foreach (var route in PortfolioRoutes)
            {
                var r = await Send(HttpMethod.Get, $"{Portfolio}/{route}?profileId={ProfileB}", PortfolioManagerOfTeam);
                Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
                Assert.Equal(ResponseMessages.Errors.CrossCompanyAccessDenied, (await Json(r)).GetProperty("message").GetString());
            }
        }

        [Theory]
        [InlineData(ProfileAdminA)]
        [InlineData(ManagerOfTeam)]
        public async Task Portfolio_OtherRoles_Return403(int asUser)
        {
            SeedProfiles();

            foreach (var route in PortfolioRoutes)
            {
                var r = await Send(HttpMethod.Get, $"{Portfolio}/{route}?profileId={ProfileA}", asUser);
                Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
                Assert.Equal(ResponseMessages.Errors.PortfolioNotAllowed, (await Json(r)).GetProperty("message").GetString());
            }
        }

        [Fact]
        public async Task Chat_RridaAdmin_IsGroundedInTheSelectedProfile()
        {
            SeedProfiles();
            string? context = null;
            _chat.Setup(c => c.StreamReplyAsync(It.IsAny<EranCoachChatRequestDto>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Callback((EranCoachChatRequestDto _, int? _, string? block, CancellationToken _) => context = block)
                .Returns(OneChunk());
            _conversations.Setup(c => c.BeginTurnAsync(RridaAdmin, It.IsAny<int?>(), It.IsAny<int?>(), 0, null, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EranCoachTurnContextDto { ConversationId = 1, UserMessageId = 2, Created = true });

            var r = await SendChat(ChatBody(ProfileB), RridaAdmin);

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal("Nothing needs you this week.", await r.Content.ReadAsStringAsync());
            Assert.Contains("a RRIDA administrator looking at every team across the managers in this profile", context);
        }

        [Fact]
        public async Task Chat_RridaAdmin_RefusedBeforeAnythingIsStored()
        {
            SeedProfiles();

            Assert.Equal(HttpStatusCode.BadRequest, (await SendChat(ChatBody(null), RridaAdmin)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await SendChat(ChatBody(UnknownProfile), RridaAdmin)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendChat(ChatBody(ProfileB, ManagerOfTeam), RridaAdmin)).StatusCode);

            _conversations.Verify(c => c.BeginTurnAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<long?>(),
                It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Chat_OtherCallers_StayRefused()
        {
            SeedProfiles();

            Assert.Equal(HttpStatusCode.Forbidden, (await SendChat(ChatBody(ProfileB), PortfolioManagerOfTeam)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendChat(ChatBody(ProfileA), ProfileAdminA)).StatusCode);
        }
    }
}
