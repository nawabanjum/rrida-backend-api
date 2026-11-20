using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.TeamHealth
{
    /// <summary>
    /// The Team dropdown: every team the user can open in Team Health, and no other.
    /// Fixture: profile A has 290 (parent) > 294, plus 295; 296 is deleted; 500 is profile B.
    /// </summary>
    public class TeamHealthUserTeamsServiceTests
    {
        private readonly string _db = Guid.NewGuid().ToString();

        public TeamHealthUserTeamsServiceTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
        }

        private Task<TeamMilestoneResult<UserTeamsDto>> Get(int userId, int actor)
        {
            var db = CreateContext(_db);
            var service = new TeamHealthUserTeamsService(db, new TeamHealthAccessService(db, Roles()), Roles());
            return service.GetUserTeamsAsync(userId, actor);
        }

        private static uint[] Ids(TeamMilestoneResult<UserTeamsDto> r) => r.Data!.Teams.Select(t => t.TeamId).ToArray();

        [Fact]
        public async Task Team_manager_gets_their_team_with_its_manager()
        {
            var r = await Get(ManagerOfTeam, ManagerOfTeam);

            Assert.True(r.IsSuccess);
            Assert.Equal(new[] { Team }, Ids(r));
            var team = r.Data!.Teams[0];
            Assert.Equal("Team 294", team.TeamName);
            Assert.Equal("S294", team.TeamCode);
            Assert.Equal((int)ParentUnit, team.ParentTeamId);
            Assert.Equal("Active", team.Status);
            // The unassigned manager (302) is not listed.
            Assert.Equal(new[] { ManagerOfTeam }, team.Managers.Select(m => m.UserId));
            Assert.Equal(1, r.Data.TotalTeams);
        }

        [Fact]
        public async Task Manager_of_a_parent_team_gets_the_teams_below_it()
        {
            var r = await Get(ManagerOfParent, ManagerOfParent);
            Assert.Equal(new[] { ParentUnit, Team }, Ids(r));
        }

        [Fact]
        public async Task Portfolio_managers_get_teams_of_managers_reporting_to_them()
        {
            Assert.Equal(new[] { Team }, Ids(await Get(PortfolioManagerOfTeam, PortfolioManagerOfTeam)));
            Assert.Equal(new[] { Team }, Ids(await Get(SeniorPortfolioManager, SeniorPortfolioManager)));
        }

        [Fact]
        public async Task Profile_admin_gets_every_live_team_of_their_profile_only()
        {
            var r = await Get(ProfileAdminA, ProfileAdminA);
            Assert.Equal(new[] { ParentUnit, Team, OtherTeamA }, Ids(r));
        }

        [Fact]
        public async Task Rrida_admin_gets_every_live_team()
        {
            var r = await Get(RridaAdmin, RridaAdmin);
            Assert.Equal(new[] { ParentUnit, Team, OtherTeamA, TeamB }, Ids(r));
        }

        [Theory]
        [InlineData(UnassignedManager)]
        [InlineData(PortfolioManagerNoReports)]
        public async Task User_with_no_team_gets_an_empty_list(int userId)
        {
            var r = await Get(userId, userId);
            Assert.True(r.IsSuccess);
            Assert.Empty(r.Data!.Teams);
            Assert.Equal(0, r.Data.TotalTeams);
        }

        [Fact]
        public async Task Profile_admin_may_view_a_user_of_their_profile()
        {
            var r = await Get(ManagerOfTeam, ProfileAdminA);
            Assert.True(r.IsSuccess);
            Assert.Equal(new[] { Team }, Ids(r));
        }

        [Theory]
        [InlineData(ManagerOfParent, ManagerOfTeam)]   // a manager looking at another manager
        [InlineData(ProfileAdminB, ManagerOfTeam)]     // another profile's admin
        public async Task Anyone_else_is_forbidden(int actor, int userId)
        {
            var r = await Get(userId, actor);
            Assert.Equal(TeamMilestoneError.Forbidden, r.Error);
        }

        [Fact]
        public async Task Unknown_user_is_not_found()
        {
            var r = await Get(9999, RridaAdmin);
            Assert.Equal(TeamMilestoneError.NotFound, r.Error);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public async Task Missing_user_id_is_a_bad_request(int userId)
        {
            var r = await Get(userId, RridaAdmin);
            Assert.Equal(TeamMilestoneError.BadRequest, r.Error);
        }
    }
}
