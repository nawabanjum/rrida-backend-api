using CloverBackoffice.Components.Enums;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Moq;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;
using ProfileEntity = CloverBackoffice.Infrastructure.Models.Profile;

namespace CloverBackoffice.Tests.Profile
{
    /// <summary>
    /// Team Structure manager drag (POST api/teamhierarchy/team-structure/move-manager):
    /// when the move leaves a Portfolio Manager with nobody reporting to them, the
    /// Portfolio Manager role (4) is removed, so get-users no longer returns it.
    ///
    /// Seed: 300 (TM, team 294) → 303 (PM only) → 305 (PM only).
    /// </summary>
    public class MoveManagerPortfolioRoleRemovalTests
    {
        private readonly string _db = Guid.NewGuid().ToString();

        public MoveManagerPortfolioRoleRemovalTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
            db.UserGroups.AddRange(
                new UserGroup { Id = RoleHierarchy.NoRoleGroupId, Name = "No Role" },
                new UserGroup { Id = (int)UserGroups.ProfileAdmin, Name = "Profile Admin" },
                new UserGroup { Id = (int)UserGroups.TeamManager, Name = "Team Manager" },
                new UserGroup { Id = (int)UserGroups.PortfolioManager, Name = "Portfolio Manager" });
            db.Profiles.Add(new ProfileEntity
            {
                Id = ProfileA,
                ProfileShortCode = $"P{ProfileA}",
                DomainName = $"p{ProfileA}.test",
                ProfileName = $"Profile {ProfileA}",
                RegistrationDate = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        private sealed class NoLockTeamStructureService : TeamStructureService
        {
            public NoLockTeamStructureService(ApplicationDbContext db, ICompanyTeamService teams, ICompanyLicenseService licenses, ILicensedPeriodService period)
                : base(db, teams, licenses, period) { }

            protected override Task LockManagerMovesAsync(int profileId) => Task.CompletedTask;
        }

        private async Task<ManagerMoveResultDto> Move(int userId, int? portfolioManagerUserId)
        {
            using var db = CreateContext(_db);
            var teams = new Mock<ICompanyTeamService>();
            teams.Setup(t => t.GetTeamLicencesAsync(It.IsAny<int>(), It.IsAny<IEnumerable<uint>>()))
                 .ReturnsAsync(new Dictionary<uint, TeamLicenceDto>());
            var period = new Mock<ILicensedPeriodService>();
            period.Setup(l => l.GetActivelyLicensedUnitIdsAsync(It.IsAny<IEnumerable<long>>())).ReturnsAsync(new HashSet<long>());

            var licenses = new Mock<ICompanyLicenseService>();
            licenses.Setup(l => l.GetLicenseUsageAsync(It.IsAny<uint>())).ReturnsAsync(new LicenseUsage());

            var service = new NoLockTeamStructureService(db, teams.Object, licenses.Object, period.Object);
            var (result, error) = await service.MoveManagerAsync(new MoveManagerDto
            {
                ProfileId = (int)ProfileA,
                UserId = userId,
                PortfolioManagerUserId = portfolioManagerUserId
            }, ProfileAdminA);

            Assert.Null(error);
            return result!;
        }

        private T Read<T>(Func<ApplicationDbContext, T> read)
        {
            using var db = CreateContext(_db);
            return read(db);
        }

        private void GiveRoles(int userId, params UserGroups[] roles)
        {
            using var db = CreateContext(_db);
            db.UserRoles.RemoveRange(db.UserRoles.Where(r => r.UserId == userId));
            foreach (var role in roles)
                db.UserRoles.Add(new UserRole { UserId = userId, UserGroupId = (int)role, CreatedDate = DateTime.UtcNow });
            db.Users.Single(u => u.UserId == userId).UserGroupId = (int)RoleHierarchy.PrimaryOf(roles.Select(r => (int)r))!;
            db.SaveChanges();
        }

        private void AddReport(int managerId, int portfolioManagerId)
        {
            using var db = CreateContext(_db);
            db.PortfolioManagerAssignments.Add(new PortfolioManagerAssignment
            {
                ProfileId = ProfileA,
                ManagerUserId = managerId,
                PortfolioManagerUserId = portfolioManagerId,
                CreatedDate = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        private List<int> RoleRows(int userId) =>
            Read(db => db.UserRoles.Where(r => r.UserId == userId).Select(r => r.UserGroupId).ToList());

        private int DerivedRole(int userId) =>
            Read(db => db.Users.Single(u => u.UserId == userId).UserGroupId);

        private bool ReportsTo(int managerId, int portfolioManagerId) =>
            Read(db => db.PortfolioManagerAssignments.Any(a =>
                a.ManagerUserId == managerId && a.PortfolioManagerUserId == portfolioManagerId && !a.IsUnAssigned));

        [Fact]
        public async Task Last_report_moved_away_removes_portfolio_manager_role()
        {
            GiveRoles(PortfolioManagerOfTeam, UserGroups.PortfolioManager, UserGroups.TeamManager);

            var result = await Move(ManagerOfTeam, null);

            Assert.True(result.Changed);
            Assert.Equal(new[] { PortfolioManagerOfTeam }, result.PortfolioManagerRoleRemovedUserIds);
            Assert.Equal(new[] { (int)UserGroups.TeamManager }, RoleRows(PortfolioManagerOfTeam));
            Assert.Equal((int)UserGroups.TeamManager, DerivedRole(PortfolioManagerOfTeam));

            // Still a Team Manager, so still in the structure under 305, who keeps the role.
            Assert.True(ReportsTo(PortfolioManagerOfTeam, SeniorPortfolioManager));
            Assert.Equal((int)UserGroups.PortfolioManager, DerivedRole(SeniorPortfolioManager));
            Assert.False(result.Structure.Managers.Single(m => m.UserId == PortfolioManagerOfTeam).IsPortfolioManager);
        }

        [Fact]
        public async Task Get_users_no_longer_returns_role_4_after_the_drag()
        {
            GiveRoles(PortfolioManagerOfTeam, UserGroups.PortfolioManager, UserGroups.TeamManager);
            AddReport(PortfolioManagerNoReports, SeniorPortfolioManager);

            List<int> RolesInGetUsers(int userId)
            {
                using var db = CreateContext(_db);
                var users = new UserRepository(db).GetUserList((int)ProfileA, ProfileAdminA).GetAwaiter().GetResult();
                return users.Single(u => u.UserId == userId).Roles.Select(r => r.Id).ToList();
            }

            Assert.Contains((int)UserGroups.PortfolioManager, RolesInGetUsers(PortfolioManagerOfTeam));

            await Move(ManagerOfTeam, null);

            Assert.Equal(new[] { (int)UserGroups.TeamManager }, RolesInGetUsers(PortfolioManagerOfTeam));
            Assert.Equal(new[] { (int)UserGroups.PortfolioManager }, RolesInGetUsers(SeniorPortfolioManager));
        }

        [Fact]
        public async Task Get_users_returns_no_roles_when_portfolio_manager_was_the_only_one()
        {
            GiveRoles(PortfolioManagerOfTeam, UserGroups.PortfolioManager);
            AddReport(PortfolioManagerNoReports, SeniorPortfolioManager);

            await Move(ManagerOfTeam, null);

            using var db = CreateContext(_db);
            var users = await new UserRepository(db).GetUserList((int)ProfileA, ProfileAdminA);
            Assert.Empty(users.Single(u => u.UserId == PortfolioManagerOfTeam).Roles);
        }

        [Fact]
        public async Task Portfolio_manager_with_another_report_keeps_the_role()
        {
            GiveRoles(PortfolioManagerOfTeam, UserGroups.PortfolioManager);
            AddReport(ManagerOfParent, PortfolioManagerOfTeam);

            var result = await Move(ManagerOfTeam, null);

            Assert.Empty(result.PortfolioManagerRoleRemovedUserIds);
            Assert.Equal(new[] { (int)UserGroups.PortfolioManager }, RoleRows(PortfolioManagerOfTeam));
            Assert.True(ReportsTo(PortfolioManagerOfTeam, SeniorPortfolioManager));
        }

        [Fact]
        public async Task Portfolio_manager_only_role_becomes_no_role_and_leaves_their_own_line()
        {
            GiveRoles(PortfolioManagerOfTeam, UserGroups.PortfolioManager);
            AddReport(PortfolioManagerNoReports, SeniorPortfolioManager); // 305 keeps a report

            var result = await Move(ManagerOfTeam, null);

            Assert.Equal(new[] { PortfolioManagerOfTeam }, result.PortfolioManagerRoleRemovedUserIds);
            Assert.Empty(RoleRows(PortfolioManagerOfTeam));
            Assert.Equal(RoleHierarchy.NoRoleGroupId, DerivedRole(PortfolioManagerOfTeam));
            Assert.False(ReportsTo(PortfolioManagerOfTeam, SeniorPortfolioManager));
            Assert.Equal((int)UserGroups.PortfolioManager, DerivedRole(SeniorPortfolioManager));
        }

        [Fact]
        public async Task Emptied_chain_removes_the_role_further_up()
        {
            // 303 and 305 hold only the Portfolio Manager role.
            var result = await Move(ManagerOfTeam, null);

            Assert.Equal(new[] { PortfolioManagerOfTeam, SeniorPortfolioManager }, result.PortfolioManagerRoleRemovedUserIds);
            Assert.Equal(RoleHierarchy.NoRoleGroupId, DerivedRole(PortfolioManagerOfTeam));
            Assert.Equal(RoleHierarchy.NoRoleGroupId, DerivedRole(SeniorPortfolioManager));
        }

        [Fact]
        public async Task Moving_to_the_old_parents_own_portfolio_manager_keeps_that_role()
        {
            // 300 goes from 303 straight to 305: 303 empties and leaves, 305 gains 300.
            var result = await Move(ManagerOfTeam, SeniorPortfolioManager);

            Assert.Equal(new[] { PortfolioManagerOfTeam }, result.PortfolioManagerRoleRemovedUserIds);
            Assert.Equal(RoleHierarchy.NoRoleGroupId, DerivedRole(PortfolioManagerOfTeam));
            Assert.Equal((int)UserGroups.PortfolioManager, DerivedRole(SeniorPortfolioManager));
            Assert.True(ReportsTo(ManagerOfTeam, SeniorPortfolioManager));
        }

        [Fact]
        public async Task Moving_a_portfolio_manager_who_still_has_reports_keeps_their_role()
        {
            // 303 (still has 300) moved to the company: 303 keeps PM; 305 empties.
            var result = await Move(PortfolioManagerOfTeam, null);

            Assert.Equal(new[] { SeniorPortfolioManager }, result.PortfolioManagerRoleRemovedUserIds);
            Assert.Equal((int)UserGroups.PortfolioManager, DerivedRole(PortfolioManagerOfTeam));
            Assert.True(ReportsTo(ManagerOfTeam, PortfolioManagerOfTeam));
        }
    }
}
