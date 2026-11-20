using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
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
    /// A Portfolio Manager can be given a Team Manager licence, which makes them a Team
    /// Manager: POST api/license-assignments (Assign + managerId) and the Team Structure
    /// drop (assign-team with assignTeamManagerLicence). Real licence service and
    /// repository on the in-memory database; only the advisory lock is skipped.
    /// </summary>
    public class PortfolioManagerTeamManagerLicenceTests
    {
        private const int PortfolioManager = PortfolioManagerNoReports; // PM only, no teams

        private readonly string _db = Guid.NewGuid().ToString();
        private readonly Mock<ICompanyTeamService> _teams = new();

        public PortfolioManagerTeamManagerLicenceTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
            db.UserGroups.AddRange(
                new UserGroup { Id = (int)UserGroups.ProfileAdmin, Name = "Profile Admin" },
                new UserGroup { Id = (int)UserGroups.TeamManager, Name = "Team Manager" },
                new UserGroup { Id = (int)UserGroups.RridaAdmin, Name = "RRIDA Admin" },
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

        private sealed class NoLockLicenseService : CompanyLicenseService
        {
            public NoLockLicenseService(ApplicationDbContext db)
                : base(new CompanyLicenseRepository(db), db, new Mock<ILicensedPeriodService>().Object) { }

            protected override Task LockProfileLicensesAsync(uint profileId) => Task.CompletedTask;
        }

        private int AddLicense(int? holder = null, uint? onTeam = null)
        {
            using var db = CreateContext(_db);
            var license = new CompanyLicense
            {
                ProfileId = ProfileA,
                LicenseKey = Guid.NewGuid().ToString(),
                LicenseType = "Test",
                AssignedUserId = holder
            };
            db.CompanyLicenses.Add(license);
            db.SaveChanges();
            if (onTeam.HasValue)
            {
                db.AssignedCompanyLicenses.Add(new AssignedCompanyLicense { CompanyLicenseId = license.Id, BusinessUnitId = onTeam.Value });
                db.SaveChanges();
            }
            return license.Id;
        }

        private async Task<LicenseAssignmentResultDto> AssignByManager(int managerId, int actor = ProfileAdminA)
        {
            using var db = CreateContext(_db);
            return await new NoLockLicenseService(db).ExecuteLifecycleAsync(new LicenseAssignmentRequestDto
            {
                Action = LicenseAssignmentAction.Assign,
                ManagerId = managerId
            }, actor);
        }

        private async Task<(TeamAssignmentResultDto? Result, string? Error)> Drop(int userId, bool confirm, uint unitId = OtherTeamA)
        {
            using var db = CreateContext(_db);
            var licensedPeriod = new Mock<ILicensedPeriodService>();
            licensedPeriod.Setup(l => l.GetActivelyLicensedUnitIdsAsync(It.IsAny<IEnumerable<long>>())).ReturnsAsync(new HashSet<long>());
            _teams.Setup(t => t.GetTeamLicencesAsync(It.IsAny<int>(), It.IsAny<IEnumerable<uint>>()))
                  .ReturnsAsync(new Dictionary<uint, TeamLicenceDto>());
            var service = new TeamStructureService(db, _teams.Object, new NoLockLicenseService(db), licensedPeriod.Object);
            return await service.AssignTeamAsync(new AssignTeamManagerDto
            {
                ProfileId = (int)ProfileA,
                UnitId = unitId,
                UserId = userId,
                AssignTeamManagerLicence = confirm
            }, ProfileAdminA);
        }

        private T Read<T>(Func<ApplicationDbContext, T> read)
        {
            using var db = CreateContext(_db);
            return read(db);
        }

        private bool IsTeamManager(int userId) =>
            Read(db => db.TeamManagersOf((int)ProfileA).Any(u => u.UserId == userId));

        private int? Holder(int licenseId) =>
            Read(db => db.CompanyLicenses.Single(l => l.Id == licenseId).AssignedUserId);

        private async Task<int> AvailableLicenses()
        {
            using var db = CreateContext(_db);
            return (await new CompanyLicenseRepository(db).GetLicenseUsageAsync(ProfileA)).AvailableLicenses;
        }

        // ---------- POST api/license-assignments (Assign + managerId) ----------

        [Fact]
        public async Task Portfolio_manager_gets_the_licence_and_the_team_manager_role()
        {
            var license = AddLicense();
            Assert.False(IsTeamManager(PortfolioManager));

            var result = await AssignByManager(PortfolioManager);

            Assert.True(result.TeamManagerRoleAssigned);
            Assert.True(result.LicenseNewlyAssigned);
            Assert.Equal(license, result.CompanyLicenseId);
            Assert.Equal(PortfolioManager, Holder(license));
            Assert.True(IsTeamManager(PortfolioManager));

            // Keeps the Portfolio Manager role; the derived column is the higher one.
            var roles = Read(db => db.UserRoles.Where(r => r.UserId == PortfolioManager).Select(r => r.UserGroupId).ToList());
            Assert.Contains((int)UserGroups.TeamManager, roles);
            Assert.Contains((int)UserGroups.PortfolioManager, roles);
            Assert.Equal((int)UserGroups.TeamManager, Read(db => db.Users.Single(u => u.UserId == PortfolioManager).UserGroupId));
            Assert.Equal(0, await AvailableLicenses());
        }

        [Fact]
        public async Task Assigning_again_is_idempotent_and_takes_no_second_licence()
        {
            var first = AddLicense();
            AddLicense();

            await AssignByManager(PortfolioManager);
            var again = await AssignByManager(PortfolioManager);

            Assert.Equal(first, again.CompanyLicenseId);
            Assert.False(again.TeamManagerRoleAssigned);
            Assert.False(again.LicenseNewlyAssigned);
            Assert.Equal(1, await AvailableLicenses());
        }

        [Fact]
        public async Task No_free_licence_refuses_and_changes_nothing()
        {
            AddLicense(holder: ManagerOfTeam, onTeam: Team); // the only licence, in use

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => AssignByManager(PortfolioManager));

            Assert.Equal(ResponseMessages.Errors.TeamManagerLicenceNotAvailable, ex.Message);
            Assert.False(IsTeamManager(PortfolioManager));
        }

        [Fact]
        public async Task Inactive_portfolio_manager_is_refused()
        {
            AddLicense();
            using (var db = CreateContext(_db))
            {
                db.Users.Single(u => u.UserId == PortfolioManager).Status = ((int)UserStatus.Suspend).ToString();
                db.SaveChanges();
            }

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => AssignByManager(PortfolioManager));

            Assert.Contains("inactive", ex.Message);
            Assert.False(IsTeamManager(PortfolioManager));
            Assert.Equal(1, await AvailableLicenses());
        }

        [Fact]
        public async Task Profile_admin_takes_back_team_manager_role_for_themselves()
        {
            // Was Profile Admin + Team Manager; the Team Manager role was removed.
            var license = AddLicense();
            Assert.False(IsTeamManager(ProfileAdminA));

            var result = await AssignByManager(ProfileAdminA, actor: ProfileAdminA);

            Assert.True(result.TeamManagerRoleAssigned);
            Assert.Equal(ProfileAdminA, Holder(license));
            Assert.True(IsTeamManager(ProfileAdminA));

            // Profile Admin kept, and still the derived (highest) role.
            var roles = Read(db => db.UserRoles.Where(r => r.UserId == ProfileAdminA).Select(r => r.UserGroupId).ToList());
            Assert.Contains((int)UserGroups.ProfileAdmin, roles);
            Assert.Contains((int)UserGroups.TeamManager, roles);
            Assert.Equal((int)UserGroups.ProfileAdmin, Read(db => db.Users.Single(u => u.UserId == ProfileAdminA).UserGroupId));
            Assert.Equal(0, await AvailableLicenses());
        }

        [Fact]
        public async Task Profile_admin_with_no_free_licence_is_refused()
        {
            AddLicense(holder: ManagerOfTeam, onTeam: Team);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => AssignByManager(ProfileAdminA, actor: ProfileAdminA));

            Assert.Equal(ResponseMessages.Errors.TeamManagerLicenceNotAvailable, ex.Message);
            Assert.False(IsTeamManager(ProfileAdminA));
        }

        private void MakeNoRole(int userId)
        {
            using var db = CreateContext(_db);
            db.UserRoles.RemoveRange(db.UserRoles.Where(r => r.UserId == userId));
            db.Users.Single(u => u.UserId == userId).UserGroupId = RoleHierarchy.NoRoleGroupId;
            db.SaveChanges();
        }

        private List<int> RoleRows(int userId) =>
            Read(db => db.UserRoles.Where(r => r.UserId == userId).Select(r => r.UserGroupId).ToList());

        [Fact]
        public async Task User_with_no_role_gets_the_licence_and_the_team_manager_role()
        {
            var license = AddLicense();
            MakeNoRole(PortfolioManager);

            var result = await AssignByManager(PortfolioManager);

            Assert.True(result.TeamManagerRoleAssigned);
            Assert.True(result.LicenseNewlyAssigned);
            Assert.Equal(PortfolioManager, Holder(license));
            Assert.True(IsTeamManager(PortfolioManager));
            Assert.Equal(new[] { (int)UserGroups.TeamManager }, RoleRows(PortfolioManager));
            Assert.Equal((int)UserGroups.TeamManager, Read(db => db.Users.Single(u => u.UserId == PortfolioManager).UserGroupId));
            Assert.Equal(0, await AvailableLicenses());
        }

        [Fact]
        public async Task User_with_no_role_and_no_free_licence_is_refused()
        {
            AddLicense(holder: ManagerOfTeam, onTeam: Team);
            MakeNoRole(PortfolioManager);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => AssignByManager(PortfolioManager));

            Assert.Equal(ResponseMessages.Errors.TeamManagerLicenceNotAvailable, ex.Message);
            Assert.False(IsTeamManager(PortfolioManager));
            Assert.Empty(RoleRows(PortfolioManager));
        }

        [Fact]
        public async Task Inactive_user_with_no_role_is_refused()
        {
            AddLicense();
            MakeNoRole(PortfolioManager);
            using (var db = CreateContext(_db))
            {
                db.Users.Single(u => u.UserId == PortfolioManager).Status = ((int)UserStatus.Suspend).ToString();
                db.SaveChanges();
            }

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => AssignByManager(PortfolioManager));

            Assert.Contains("inactive", ex.Message);
            Assert.Equal(1, await AvailableLicenses());
        }

        [Fact]
        public async Task Rrida_admin_is_still_refused()
        {
            AddLicense();
            using (var db = CreateContext(_db))
            {
                db.UserRoles.RemoveRange(db.UserRoles.Where(r => r.UserId == UnassignedManager));
                db.UserRoles.Add(new UserRole { UserId = UnassignedManager, UserGroupId = (int)UserGroups.RridaAdmin, CreatedDate = DateTime.UtcNow });
                db.Users.Single(u => u.UserId == UnassignedManager).UserGroupId = (int)UserGroups.RridaAdmin;
                db.SaveChanges();
            }

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => AssignByManager(UnassignedManager));

            Assert.Equal($"User {UnassignedManager} is not a Team Manager.", ex.Message);
            Assert.Equal(1, await AvailableLicenses());
        }

        [Fact]
        public async Task Revert_takes_a_no_role_user_back_to_no_role()
        {
            var license = AddLicense();
            MakeNoRole(PortfolioManager);
            await AssignByManager(PortfolioManager);

            using (var db = CreateContext(_db))
                await new NoLockLicenseService(db).RevertTeamManagerPromotionAsync(PortfolioManager, license, releaseLicense: true, ProfileAdminA);

            Assert.Empty(RoleRows(PortfolioManager));
            Assert.Equal(RoleHierarchy.NoRoleGroupId, Read(db => db.Users.Single(u => u.UserId == PortfolioManager).UserGroupId));
            Assert.Equal(1, await AvailableLicenses());
        }

        [Fact]
        public async Task Existing_team_manager_assignment_is_unchanged()
        {
            var license = AddLicense();

            var result = await AssignByManager(ManagerOfTeam);

            Assert.False(result.TeamManagerRoleAssigned);
            Assert.Equal(license, result.CompanyLicenseId);
            Assert.Equal(ManagerOfTeam, Holder(license));
            Assert.Equal(Team, result.CurrentBusinessUnitId);
        }

        [Fact]
        public async Task Licence_details_list_portfolio_managers_as_no_licence_rows()
        {
            AddLicense();
            using var db = CreateContext(_db);
            var rows = await new NoLockLicenseService(db).GetLicenseDetailsAsync(ProfileA, ProfileAdminA);

            var pm = Assert.Single(rows, r => r.ManagerId == PortfolioManager);
            Assert.Equal("NO_LICENSE", pm.Status);
            Assert.False(pm.IsTeamManager);
            Assert.True(pm.IsPortfolioManager);

            var tm = Assert.Single(rows, r => r.ManagerId == ManagerOfTeam);
            Assert.True(tm.IsTeamManager);
        }

        [Fact]
        public async Task Licence_details_list_users_with_no_role_as_no_licence_rows()
        {
            AddLicense();
            MakeNoRole(PortfolioManager);
            using var db = CreateContext(_db);
            var rows = await new NoLockLicenseService(db).GetLicenseDetailsAsync(ProfileA, ProfileAdminA);

            var noRole = Assert.Single(rows, r => r.ManagerId == PortfolioManager);
            Assert.Equal("NO_LICENSE", noRole.Status);
            Assert.False(noRole.IsTeamManager);
            Assert.False(noRole.IsPortfolioManager);

            // Profile Admin only (no Team Manager role) is still not listed.
            Assert.DoesNotContain(rows, r => r.ManagerId == ProfileAdminA);
        }

        [Fact]
        public async Task Inactive_user_with_no_role_is_not_listed()
        {
            MakeNoRole(PortfolioManager);
            using (var db = CreateContext(_db))
            {
                db.Users.Single(u => u.UserId == PortfolioManager).Status = ((int)UserStatus.Suspend).ToString();
                db.SaveChanges();
            }

            using var read = CreateContext(_db);
            var rows = await new NoLockLicenseService(read).GetLicenseDetailsAsync(ProfileA, ProfileAdminA);

            Assert.DoesNotContain(rows, r => r.ManagerId == PortfolioManager);
        }

        [Fact]
        public async Task Revert_takes_back_the_role_and_frees_the_licence()
        {
            var license = AddLicense();
            await AssignByManager(PortfolioManager);

            using (var db = CreateContext(_db))
                await new NoLockLicenseService(db).RevertTeamManagerPromotionAsync(PortfolioManager, license, releaseLicense: true, ProfileAdminA);

            Assert.False(IsTeamManager(PortfolioManager));
            Assert.Null(Holder(license));
            Assert.Equal(1, await AvailableLicenses());
            Assert.Equal((int)UserGroups.PortfolioManager, Read(db => db.Users.Single(u => u.UserId == PortfolioManager).UserGroupId));
        }

        // ---------- POST api/teamhierarchy/team-structure/assign-team ----------

        [Fact]
        public async Task Drop_on_portfolio_manager_asks_first_and_writes_nothing()
        {
            AddLicense();

            var (result, error) = await Drop(PortfolioManager, confirm: false);

            Assert.Null(error);
            Assert.True(result!.RequiresTeamManagerLicence);
            Assert.True(result.LicenceAvailable);
            Assert.Equal("assignTeamManagerLicence", result.ConfirmWith);
            Assert.False(result.Changed);
            Assert.False(IsTeamManager(PortfolioManager));
            _teams.Verify(t => t.AssignUserToTeamAsync(It.IsAny<UserTeamDto>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task Drop_with_no_free_licence_says_so_even_when_confirmed()
        {
            AddLicense(holder: ManagerOfTeam, onTeam: Team);

            var (result, error) = await Drop(PortfolioManager, confirm: true);

            Assert.Null(error);
            Assert.True(result!.RequiresTeamManagerLicence);
            Assert.False(result.LicenceAvailable);
            Assert.Null(result.ConfirmWith);
            Assert.False(IsTeamManager(PortfolioManager));
            _teams.Verify(t => t.AssignUserToTeamAsync(It.IsAny<UserTeamDto>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task Confirmed_drop_assigns_licence_role_then_the_team()
        {
            var license = AddLicense();
            _teams.Setup(t => t.AssignUserToTeamAsync(It.IsAny<UserTeamDto>(), It.IsAny<int>()))
                  .ReturnsAsync(new UserTeam { UserId = PortfolioManager, UnitId = OtherTeamA });

            var (result, error) = await Drop(PortfolioManager, confirm: true);

            Assert.Null(error);
            Assert.True(result!.Changed);
            Assert.True(result.TeamManagerLicenceAssigned);
            Assert.False(result.RequiresTeamManagerLicence);
            Assert.Equal(license, result.CompanyLicenseId);
            Assert.True(IsTeamManager(PortfolioManager));
            Assert.Equal(PortfolioManager, Holder(license));
            _teams.Verify(t => t.AssignUserToTeamAsync(
                It.Is<UserTeamDto>(d => d.UserId == PortfolioManager && d.UnitId == OtherTeamA), ProfileAdminA), Times.Once);
        }

        [Fact]
        public async Task Failed_team_move_takes_the_licence_and_role_back()
        {
            var license = AddLicense();
            _teams.Setup(t => t.AssignUserToTeamAsync(It.IsAny<UserTeamDto>(), It.IsAny<int>()))
                  .ThrowsAsync(new InvalidOperationException("boom"));

            var (result, error) = await Drop(PortfolioManager, confirm: true);

            Assert.Null(result);
            Assert.StartsWith(ResponseMessages.Errors.TeamManagerLicenceAssignFailed, error);
            Assert.False(IsTeamManager(PortfolioManager));
            Assert.Null(Holder(license));
            Assert.Equal(1, await AvailableLicenses());
        }

        [Fact]
        public async Task Drop_on_profile_admin_asks_then_makes_them_team_manager()
        {
            var license = AddLicense();
            _teams.Setup(t => t.AssignUserToTeamAsync(It.IsAny<UserTeamDto>(), It.IsAny<int>()))
                  .ReturnsAsync(new UserTeam { UserId = ProfileAdminA, UnitId = OtherTeamA });

            var (ask, askError) = await Drop(ProfileAdminA, confirm: false);
            Assert.Null(askError);
            Assert.Equal("assignTeamManagerLicence", ask!.ConfirmWith);
            Assert.False(IsTeamManager(ProfileAdminA));

            var (result, error) = await Drop(ProfileAdminA, confirm: true);

            Assert.Null(error);
            Assert.True(result!.Changed);
            Assert.True(result.TeamManagerLicenceAssigned);
            Assert.True(IsTeamManager(ProfileAdminA));
            Assert.Equal(ProfileAdminA, Holder(license));
            Assert.Equal((int)UserGroups.ProfileAdmin, Read(db => db.Users.Single(u => u.UserId == ProfileAdminA).UserGroupId));
        }

        [Fact]
        public async Task Drop_on_user_with_no_role_asks_then_makes_them_team_manager()
        {
            var license = AddLicense();
            MakeNoRole(PortfolioManager);
            _teams.Setup(t => t.AssignUserToTeamAsync(It.IsAny<UserTeamDto>(), It.IsAny<int>()))
                  .ReturnsAsync(new UserTeam { UserId = PortfolioManager, UnitId = OtherTeamA });

            var (ask, askError) = await Drop(PortfolioManager, confirm: false);
            Assert.Null(askError);
            Assert.Equal("assignTeamManagerLicence", ask!.ConfirmWith);
            Assert.False(IsTeamManager(PortfolioManager));

            var (result, error) = await Drop(PortfolioManager, confirm: true);

            Assert.Null(error);
            Assert.True(result!.Changed);
            Assert.True(result.TeamManagerLicenceAssigned);
            Assert.True(IsTeamManager(PortfolioManager));
            Assert.Equal(PortfolioManager, Holder(license));
            Assert.Equal(new[] { (int)UserGroups.TeamManager }, RoleRows(PortfolioManager));
        }
    }
}
