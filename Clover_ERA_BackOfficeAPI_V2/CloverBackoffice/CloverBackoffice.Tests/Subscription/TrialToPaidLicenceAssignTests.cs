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

namespace CloverBackoffice.Tests.Subscription
{
    /// <summary>
    /// POST api/license-assignments (Assign + managerId) on a Team Manager who is on a
    /// trial licence: the trial ends and the purchased licence takes over, in one
    /// transaction. Real licence service and repository on the in-memory database;
    /// only the advisory lock is skipped.
    /// </summary>
    public class TrialToPaidLicenceAssignTests
    {
        private const int Manager = ManagerOfTeam; // Team Manager of team 294

        private readonly string _db = Guid.NewGuid().ToString();

        public TrialToPaidLicenceAssignTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
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

        private int AddLicense(string type, int? holder = null, uint? onTeam = null)
        {
            using var db = CreateContext(_db);
            var license = new CompanyLicense
            {
                ProfileId = ProfileA,
                LicenseKey = Guid.NewGuid().ToString(),
                LicenseType = type,
                ExpiryDate = DateTime.UtcNow.AddDays(type == "Trial" ? 20 : 30),
                AssignedUserId = holder
            };
            db.CompanyLicenses.Add(license);
            db.SaveChanges();
            if (onTeam.HasValue)
            {
                db.AssignedCompanyLicenses.Add(new AssignedCompanyLicense
                {
                    CompanyLicenseId = license.Id,
                    BusinessUnitId = onTeam.Value,
                    AssignedAt = DateTime.UtcNow.AddDays(-10),
                    IsActive = true
                });
                db.SaveChanges();
            }
            return license.Id;
        }

        private async Task<LicenseAssignmentResultDto> Assign(int managerId, int? licenseId = null)
        {
            using var db = CreateContext(_db);
            return await new NoLockLicenseService(db).ExecuteLifecycleAsync(new LicenseAssignmentRequestDto
            {
                Action = LicenseAssignmentAction.Assign,
                ManagerId = managerId,
                CompanyLicenseId = licenseId
            }, ProfileAdminA);
        }

        private T Read<T>(Func<ApplicationDbContext, T> read)
        {
            using var db = CreateContext(_db);
            return read(db);
        }

        private CompanyLicense License(int id) => Read(db => db.CompanyLicenses.Single(l => l.Id == id));

        private uint? ActiveTeamOf(int licenseId) =>
            Read(db => db.AssignedCompanyLicenses
                .Where(a => a.CompanyLicenseId == licenseId && a.IsActive && !a.IsDel)
                .Select(a => (uint?)a.BusinessUnitId)
                .FirstOrDefault());

        private async Task<LicenseUsage> Usage()
        {
            using var db = CreateContext(_db);
            return await new CompanyLicenseRepository(db).GetLicenseUsageAsync(ProfileA);
        }

        [Fact]
        public async Task Trial_manager_moves_to_a_free_purchased_licence_when_no_licence_is_sent()
        {
            var trial = AddLicense("Trial", holder: Manager, onTeam: Team);
            var paid = AddLicense("Paid");
            Assert.Equal(1, (await Usage()).AvailableLicenses);

            var result = await Assign(Manager);

            Assert.Equal(paid, result.CompanyLicenseId);
            Assert.True(result.TrialConverted);
            Assert.Equal(trial, result.TerminatedTrialLicenseId);
            Assert.False(result.LicenseNewlyAssigned);
            Assert.Equal(Team, result.CurrentBusinessUnitId);

            // Paid licence: held by the manager and on their team.
            Assert.Equal(Manager, License(paid).AssignedUserId);
            Assert.Equal(Team, ActiveTeamOf(paid));

            // Trial: ended, never to be revived, no holder, no team.
            var ended = License(trial);
            Assert.True(ended.IsDel);
            Assert.NotNull(ended.TerminatedAt);
            Assert.Equal(paid, ended.ReplacedByLicenseId);
            Assert.Null(ended.AssignedUserId);
            Assert.True(ended.ExpiryDate <= DateTime.UtcNow);
            Assert.NotNull(ended.ExpiryNotifiedAt); // no "licence expired" email
            Assert.Null(ActiveTeamOf(trial));
            Assert.Equal("UPDATE", Read(db => db.CompanyLicenseAuditLogs.Single(a => a.Id == trial).OperationType));

            // Counts: the trial is gone, the paid licence is consumed.
            var usage = await Usage();
            Assert.Equal(1, usage.TotalLicenses);
            Assert.Equal(1, usage.AssignedLicenses);
            Assert.Equal(0, usage.AvailableLicenses);

            // One live licence for the manager - the paid one.
            using var db = CreateContext(_db);
            Assert.Equal(paid, await new CompanyLicenseRepository(db).FindUserLicenseIdAsync(Manager));
            Assert.Equal(1, db.CompanyLicenses.Count(l => l.AssignedUserId == Manager && !l.IsDel));
        }

        [Fact]
        public async Task Trial_manager_takes_the_purchased_licence_that_was_sent()
        {
            var trial = AddLicense("Trial", holder: Manager, onTeam: Team);
            AddLicense("Paid");
            var chosen = AddLicense("Paid");

            var result = await Assign(Manager, chosen);

            Assert.Equal(chosen, result.CompanyLicenseId);
            Assert.True(result.TrialConverted);
            Assert.Equal(Manager, License(chosen).AssignedUserId);
            Assert.Equal(Team, ActiveTeamOf(chosen));
            Assert.NotNull(License(trial).TerminatedAt);
            Assert.Equal(1, (await Usage()).AvailableLicenses);
        }

        [Fact]
        public async Task Licence_details_show_the_manager_on_the_purchased_licence()
        {
            AddLicense("Trial", holder: Manager, onTeam: Team);
            var paid = AddLicense("Paid");

            await Assign(Manager);

            using var db = CreateContext(_db);
            var rows = await new NoLockLicenseService(db).GetLicenseDetailsAsync(ProfileA, ProfileAdminA);
            var row = Assert.Single(rows, r => r.ManagerId == Manager);
            Assert.Equal(paid, row.CompanyLicenseId);
            Assert.NotEqual("Trial", row.Plan);
            Assert.Equal("ACTIVE", row.Status);
        }

        [Fact]
        public async Task Trial_manager_with_no_free_purchased_licence_is_refused_and_nothing_changes()
        {
            var trial = AddLicense("Trial", holder: Manager, onTeam: Team);
            AddLicense("Paid", holder: ManagerOfParent, onTeam: ParentUnit); // the only paid licence, in use

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Assign(Manager));

            Assert.Equal(ResponseMessages.Errors.TrialManagerNoPaidLicenceAvailable, ex.Message);
            var unchanged = License(trial);
            Assert.False(unchanged.IsDel);
            Assert.Null(unchanged.TerminatedAt);
            Assert.Equal(Manager, unchanged.AssignedUserId);
            Assert.Equal(Team, ActiveTeamOf(trial));
        }

        [Fact]
        public async Task Purchased_licence_held_by_another_manager_is_refused_and_trial_kept()
        {
            var trial = AddLicense("Trial", holder: Manager, onTeam: Team);
            var taken = AddLicense("Paid", holder: ManagerOfParent, onTeam: ParentUnit);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Assign(Manager, taken));

            Assert.Contains("already assigned to another manager", ex.Message);
            Assert.Null(License(trial).TerminatedAt);
            Assert.Equal(Manager, License(trial).AssignedUserId);
            Assert.Equal(ManagerOfParent, License(taken).AssignedUserId);
        }

        [Fact]
        public async Task Paid_manager_cannot_be_moved_to_a_trial_licence()
        {
            var paid = AddLicense("Paid", holder: Manager, onTeam: Team);
            var trial = AddLicense("Trial");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Assign(Manager, trial));

            Assert.Equal(ResponseMessages.Errors.PaidLicenceCannotBeReplacedByTrial, ex.Message);
            Assert.Equal(Manager, License(paid).AssignedUserId);
            Assert.Null(License(trial).AssignedUserId);
        }

        [Fact]
        public async Task Paid_manager_swap_to_another_paid_licence_still_needs_a_remove_first()
        {
            var paid = AddLicense("Paid", holder: Manager, onTeam: Team);
            var other = AddLicense("Paid");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Assign(Manager, other));

            Assert.Equal($"Manager {Manager} already holds license {paid}. Remove it before assigning another one.", ex.Message);
        }

        [Fact]
        public async Task Paid_manager_reassign_is_unchanged_and_idempotent()
        {
            var paid = AddLicense("Paid", holder: Manager, onTeam: Team);
            AddLicense("Paid");

            var result = await Assign(Manager);

            Assert.Equal(paid, result.CompanyLicenseId);
            Assert.False(result.TrialConverted);
            Assert.Null(result.TerminatedTrialLicenseId);
            Assert.Equal(Team, ActiveTeamOf(paid));
            Assert.Equal(1, (await Usage()).AvailableLicenses);
        }

        [Fact]
        public async Task Trial_manager_with_no_team_just_holds_the_purchased_licence()
        {
            const int noTeamManager = UnassignedManager; // Team Manager, unassigned from 294
            var trial = AddLicense("Trial", holder: noTeamManager);
            var paid = AddLicense("Paid");

            var result = await Assign(noTeamManager);

            Assert.True(result.TrialConverted);
            Assert.Equal(noTeamManager, License(paid).AssignedUserId);
            Assert.Null(ActiveTeamOf(paid));
            Assert.NotNull(License(trial).TerminatedAt);
            Assert.Equal(0, (await Usage()).AvailableLicenses);
        }

        [Fact]
        public async Task Terminated_trial_is_never_picked_as_a_free_licence()
        {
            AddLicense("Trial", holder: Manager, onTeam: Team);
            AddLicense("Paid");
            await Assign(Manager);

            using var db = CreateContext(_db);
            var repo = new CompanyLicenseRepository(db);
            Assert.Null(await repo.FindAvailableLicenseIdAsync(ProfileA));
            Assert.Null(await repo.FindAvailablePaidLicenseIdAsync(ProfileA));
        }
    }
}
