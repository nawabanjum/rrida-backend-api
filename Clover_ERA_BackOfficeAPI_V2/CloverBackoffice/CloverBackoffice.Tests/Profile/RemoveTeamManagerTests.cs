using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.Profile
{
    /// <summary>
    /// POST api/profile/remove-team-manager. The real role service, licence service and
    /// licence repository on the in-memory database; only the advisory lock is skipped.
    /// </summary>
    public class RemoveTeamManagerTests
    {
        private readonly string _db = Guid.NewGuid().ToString();
        private readonly Mock<ITeamOnboardingNotificationService> _notifications = new();

        public RemoveTeamManagerTests()
        {
            using var db = CreateContext(_db);
            Seed(db);

            db.UserGroups.AddRange(
                new UserGroup { Id = (int)UserGroups.ProfileAdmin, Name = "Profile Admin" },
                new UserGroup { Id = (int)UserGroups.TeamManager, Name = "Team Manager" },
                new UserGroup { Id = (int)UserGroups.RridaAdmin, Name = "RRIDA Admin" },
                new UserGroup { Id = (int)UserGroups.PortfolioManager, Name = "Portfolio Manager" });
            db.SaveChanges();
        }

        private static void AddRole(ApplicationDbContext db, int userId, UserGroups role) =>
            db.UserRoles.Add(new UserRole { UserId = userId, UserGroupId = (int)role, CreatedDate = DateTime.UtcNow });

        private void WithDb(Action<ApplicationDbContext> change)
        {
            using var db = CreateContext(_db);
            change(db);
            db.SaveChanges();
        }

        private T Read<T>(Func<ApplicationDbContext, T> read)
        {
            using var db = CreateContext(_db);
            return read(db);
        }

        private int AddLicense(int? holder = null, uint? onTeam = null, uint? grantTo = null)
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
                db.AssignedCompanyLicenses.Add(new AssignedCompanyLicense { CompanyLicenseId = license.Id, BusinessUnitId = onTeam.Value });

            if (grantTo.HasValue)
                db.ComplimentaryLicenseGrants.Add(new ComplimentaryLicenseGrant
                {
                    CompanyLicenseId = license.Id,
                    PrimaryBusinessUnitId = onTeam!.Value,
                    ComplimentaryBusinessUnitId = grantTo.Value,
                    ManagerUserId = holder ?? ManagerOfTeam
                });

            db.SaveChanges();
            return license.Id;
        }

        private sealed class NoLockService : UserRoleAssignmentService
        {
            public NoLockService(ApplicationDbContext db, ICompanyLicenseService licenses, ITeamOnboardingNotificationService notifications)
                : base(db,
                    new Mock<IAccountRegistrationService>().Object,
                    new Mock<ICompanyTeamService>().Object,
                    licenses,
                    notifications,
                    new Mock<ITeamManagerHandoverService>().Object,
                    NullLogger<UserRoleAssignmentService>.Instance)
            {
            }

            protected override Task LockProfileLicensesAsync(uint profileId, CancellationToken ct) => Task.CompletedTask;
        }

        private async Task<Infrastructure.DTO.TeamManagerRemovalOutcomeDto> Remove(int target, int actor = ProfileAdminA)
        {
            using var db = CreateContext(_db);
            var licenses = new CompanyLicenseService(new CompanyLicenseRepository(db), db, new Mock<ILicensedPeriodService>().Object);
            return await new NoLockService(db, licenses, _notifications.Object).RemoveTeamManagerAsync(target, actor);
        }

        private Task<int?> FreeLicense() => WithRepo(r => r.FindAvailableLicenseIdAsync(ProfileA));

        private async Task<int> ConsumedLicenses() => (await WithRepo(r => r.GetLicenseUsageAsync(ProfileA))).AssignedLicenses;

        private async Task<T> WithRepo<T>(Func<CompanyLicenseRepository, Task<T>> read)
        {
            using var db = CreateContext(_db);
            return await read(new CompanyLicenseRepository(db));
        }

        private bool ManagesAnyTeam(int userId) => Read(db => db.UserTeams.Any(ut => ut.UserId == userId && !ut.IsUnAssigned));

        private List<int> RoleRows(int userId) =>
            Read(db => db.UserRoles.Where(r => r.UserId == userId).Select(r => r.UserGroupId).ToList());

        [Fact]
        public async Task One_team_clears_the_manager_and_releases_the_licence()
        {
            var license = AddLicense(ManagerOfTeam, onTeam: Team);
            Assert.Equal(1, await ConsumedLicenses());
            Assert.Null(await FreeLicense());

            var outcome = await Remove(ManagerOfTeam);

            Assert.True(outcome.IsSuccess);
            Assert.Equal(ManagerOfTeam, outcome.Data!.UserId);
            Assert.False(outcome.Data.IsTeamManager);
            Assert.Equal("Active", outcome.Data.Status);
            Assert.False(ManagesAnyTeam(ManagerOfTeam));

            Assert.Equal(0, await ConsumedLicenses());
            Assert.Equal(license, await FreeLicense());
            Assert.Null(Read(db => db.CompanyLicenses.Single(l => l.Id == license).AssignedUserId));
        }

        [Fact]
        public async Task No_teams_still_releases_the_licence_they_hold()
        {
            WithDb(db => db.UserTeams.Single(ut => ut.UserId == ManagerOfTeam).IsUnAssigned = true);
            var license = AddLicense(ManagerOfTeam);

            var outcome = await Remove(ManagerOfTeam);

            Assert.True(outcome.IsSuccess);
            Assert.Equal(license, await FreeLicense());
            _notifications.Verify(
                n => n.NotifyManagerUnassignedAsync(It.IsAny<int>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Several_teams_are_all_cleared_and_kept_with_their_coverage_closed()
        {
            WithDb(db => db.UserTeams.Add(new UserTeam { UserId = ManagerOfTeam, UnitId = OtherTeamA, CreatedDate = DateTime.UtcNow }));
            var license = AddLicense(ManagerOfTeam, onTeam: Team, grantTo: OtherTeamA);

            var outcome = await Remove(ManagerOfTeam);

            Assert.True(outcome.IsSuccess);
            Assert.False(ManagesAnyTeam(ManagerOfTeam));
            Assert.False(Read(db => db.AssignedCompanyLicenses.Any(a => a.CompanyLicenseId == license && a.IsActive)));
            Assert.False(Read(db => db.ComplimentaryLicenseGrants.Any(g => g.CompanyLicenseId == license && g.IsActive)));
            Assert.Equal(license, await FreeLicense());

            Assert.True(Read(db => db.BusinessUnits.Any(b => b.Id == Team && b.IsDel != true)));
            Assert.True(Read(db => db.BusinessUnits.Any(b => b.Id == OtherTeamA && b.IsDel != true)));

            _notifications.Verify(n => n.NotifyManagerUnassignedAsync(ManagerOfTeam, Team, It.IsAny<CancellationToken>()), Times.Once);
            _notifications.Verify(n => n.NotifyManagerUnassignedAsync(ManagerOfTeam, OtherTeamA, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Nobody_is_put_on_the_team_and_other_managers_are_left_alone()
        {
            await Remove(ManagerOfTeam);

            Assert.False(Read(db => db.UserTeams.Any(ut => ut.UnitId == Team && !ut.IsUnAssigned)));
            Assert.True(ManagesAnyTeam(ManagerOfParent));
            Assert.Contains((int)UserGroups.TeamManager, RoleRows(ManagerOfParent));
        }

        [Fact]
        public async Task A_team_with_another_manager_keeps_its_licence()
        {
            WithDb(db => db.UserTeams.Add(new UserTeam { UserId = ManagerOfParent, UnitId = Team, CreatedDate = DateTime.UtcNow }));
            var license = AddLicense(ManagerOfTeam, onTeam: Team);

            await Remove(ManagerOfTeam);

            Assert.True(Read(db => db.AssignedCompanyLicenses.Any(a => a.CompanyLicenseId == license && a.IsActive)));
            Assert.True(Read(db => db.UserTeams.Any(ut => ut.UnitId == Team && ut.UserId == ManagerOfParent && !ut.IsUnAssigned)));
            Assert.Equal(1, await ConsumedLicenses());
        }

        [Fact]
        public async Task Profile_admin_plus_team_manager_stays_profile_admin()
        {
            WithDb(db =>
            {
                AddRole(db, ManagerOfTeam, UserGroups.ProfileAdmin);
                db.Users.Single(u => u.UserId == ManagerOfTeam).UserGroupId = (int)UserGroups.ProfileAdmin;
            });

            var outcome = await Remove(ManagerOfTeam);

            Assert.Equal(new[] { "Profile Admin" }, outcome.Data!.Roles);
            Assert.Equal(new[] { (int)UserGroups.ProfileAdmin }, RoleRows(ManagerOfTeam));
            Assert.Equal((int)UserGroups.ProfileAdmin, Read(db => db.Users.Single(u => u.UserId == ManagerOfTeam).UserGroupId));
        }

        [Fact]
        public async Task A_profile_admin_may_remove_their_own_team_manager_role()
        {
            WithDb(db => AddRole(db, ProfileAdminA, UserGroups.TeamManager));

            var outcome = await Remove(ProfileAdminA, ProfileAdminA);

            Assert.True(outcome.IsSuccess);
            Assert.Equal(new[] { "Profile Admin" }, outcome.Data!.Roles);
        }

        [Fact]
        public async Task A_user_with_no_other_role_stays_on_the_profile_with_none()
        {
            var outcome = await Remove(ManagerOfTeam);

            Assert.Empty(outcome.Data!.Roles);
            var user = Read(db => db.Users.Single(u => u.UserId == ManagerOfTeam));
            Assert.Equal(RoleHierarchy.NoRoleGroupId, user.UserGroupId);
            Assert.False(user.IsDel);
            Assert.True(user.IsActive);
            Assert.Equal("1", user.Status);
        }

        [Fact]
        public async Task A_legacy_user_with_no_role_rows_stops_being_a_team_manager()
        {
            WithDb(db => db.UserRoles.RemoveRange(db.UserRoles.Where(r => r.UserId == ManagerOfTeam)));
            var license = AddLicense(ManagerOfTeam, onTeam: Team);

            var outcome = await Remove(ManagerOfTeam);

            Assert.True(outcome.IsSuccess);
            Assert.False(outcome.Data!.IsTeamManager);
            Assert.Equal(RoleHierarchy.NoRoleGroupId, Read(db => db.Users.Single(u => u.UserId == ManagerOfTeam).UserGroupId));
            Assert.Equal(license, await FreeLicense());
        }

        [Fact]
        public async Task The_reporting_line_closes_unless_they_are_still_a_portfolio_manager()
        {
            WithDb(db => AddRole(db, ManagerOfParent, UserGroups.PortfolioManager));
            WithDb(db => db.PortfolioManagerAssignments.Add(new PortfolioManagerAssignment
            {
                Id = 10, ProfileId = ProfileA, ManagerUserId = ManagerOfParent, PortfolioManagerUserId = PortfolioManagerOfTeam, CreatedDate = DateTime.UtcNow
            }));

            await Remove(ManagerOfTeam);
            await Remove(ManagerOfParent);

            Assert.True(Read(db => db.PortfolioManagerAssignments.Single(a => a.Id == 1).IsUnAssigned));
            Assert.False(Read(db => db.PortfolioManagerAssignments.Single(a => a.Id == 10).IsUnAssigned));
        }

        [Fact]
        public async Task A_replacement_waiting_on_their_invitation_is_cancelled()
        {
            WithDb(db => db.UserTeams.Add(new UserTeam
            {
                UserId = ManagerOfTeam, UnitId = OtherTeamA, IsUnAssigned = true, IsPendingActivation = true, CreatedDate = DateTime.UtcNow
            }));

            await Remove(ManagerOfTeam);

            Assert.False(Read(db => db.UserTeams.Any(ut => ut.UserId == ManagerOfTeam && ut.IsPendingActivation)));
        }

        [Fact]
        public async Task A_failed_notification_does_not_fail_the_removal()
        {
            _notifications.Setup(n => n.NotifyManagerUnassignedAsync(It.IsAny<int>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("mail provider down"));

            var outcome = await Remove(ManagerOfTeam);

            Assert.True(outcome.IsSuccess);
        }

        [Fact]
        public async Task A_user_who_is_not_a_team_manager_is_refused_and_unchanged()
        {
            var outcome = await Remove(PortfolioManagerOfTeam);

            Assert.False(outcome.IsSuccess);
            Assert.Equal(ResponseMessages.Errors.TeamManagerNotAssigned, outcome.ErrorMessage);
            Assert.Equal(new[] { (int)UserGroups.PortfolioManager }, RoleRows(PortfolioManagerOfTeam));
        }

        [Fact]
        public async Task A_second_removal_is_refused()
        {
            await Remove(ManagerOfTeam);

            var again = await Remove(ManagerOfTeam);

            Assert.Equal(ResponseMessages.Errors.TeamManagerNotAssigned, again.ErrorMessage);
        }

        [Theory]
        [InlineData(ManagerOfParent)]
        [InlineData(PortfolioManagerOfTeam)]
        public async Task Only_an_admin_may_remove(int actor)
        {
            var outcome = await Remove(ManagerOfTeam, actor);

            Assert.True(outcome.IsForbidden);
            Assert.Equal(ResponseMessages.Errors.TeamManagerRemovalNotPermitted, outcome.ErrorMessage);
            Assert.True(ManagesAnyTeam(ManagerOfTeam));
        }

        [Fact]
        public async Task A_profile_admin_cannot_reach_another_profiles_user()
        {
            var outcome = await Remove(ManagerOfTeam, ProfileAdminB);

            Assert.True(outcome.IsNotFound);
            Assert.True(ManagesAnyTeam(ManagerOfTeam));
        }

        [Fact]
        public async Task A_rrida_admin_may_remove_on_any_profile()
        {
            var outcome = await Remove(ManagerOfTeam, RridaAdmin);

            Assert.True(outcome.IsSuccess);
        }

        [Theory]
        [InlineData(9999)]
        [InlineData(0)]
        public async Task An_unknown_user_is_not_found(int target)
        {
            var outcome = await Remove(target);

            Assert.True(outcome.IsNotFound);
        }

        [Fact]
        public async Task A_deleted_user_is_not_found()
        {
            WithDb(db => db.Users.Single(u => u.UserId == ManagerOfTeam).IsDel = true);

            var outcome = await Remove(ManagerOfTeam);

            Assert.True(outcome.IsNotFound);
        }
    }
}
