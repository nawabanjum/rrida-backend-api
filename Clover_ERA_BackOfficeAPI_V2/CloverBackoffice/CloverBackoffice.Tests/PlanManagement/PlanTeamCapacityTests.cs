using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using CloverBackoffice.Tests.TeamHealth;
using Moq;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;
using PlanData = CloverBackoffice.Tests.PlanManagement.PlanManagementTestData;
using ProfileEntity = CloverBackoffice.Infrastructure.Models.Profile;

namespace CloverBackoffice.Tests.PlanManagement
{
    /// <summary>
    /// Team capacity from the licence's plan: Light 1 team, Pro 3 + RRIDA Admin extensions,
    /// licences from before plans = Pro. Manager 300 runs team 294 (profile A). Extensions
    /// only on plans that allow them.
    /// </summary>
    public class PlanTeamCapacityTests
    {
        private const int Manager = ManagerOfTeam;

        private readonly string _db = Guid.NewGuid().ToString();
        private readonly int _light;
        private readonly int _pro;

        public PlanTeamCapacityTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
            PlanData.Seed(db);   // plan features + RRIDA Admin 87
            db.Profiles.Add(new ProfileEntity
            {
                Id = ProfileA, ProfileShortCode = $"P{ProfileA}", DomainName = $"p{ProfileA}.test",
                ProfileName = $"Profile {ProfileA}", RegistrationDate = DateTime.UtcNow
            });
            db.SaveChanges();

            var admin = new PlanManagementService(db, PlanData.Roles(), new FixedClock(PlanData.Now));
            _light = admin.CreatePlanAsync(PlanData.Light(), PlanData.RridaAdmin).Result.Data!.Id;
            _pro = admin.CreatePlanAsync(PlanData.Pro(), PlanData.RridaAdmin).Result.Data!.Id;
        }

        private sealed class NoLockLicenseService : CompanyLicenseService
        {
            public NoLockLicenseService(ApplicationDbContext db)
                : base(new CompanyLicenseRepository(db), db, new Mock<ILicensedPeriodService>().Object) { }

            protected override Task LockProfileLicensesAsync(uint profileId) => Task.CompletedTask;
        }

        private int AddLicense(int? plan, int? holder = null)
        {
            using var db = CreateContext(_db);
            var license = new CompanyLicense
            {
                ProfileId = ProfileA, LicenseKey = Guid.NewGuid().ToString(), LicenseType = "Paid",
                ExpiryDate = DateTime.UtcNow.AddDays(30), AssignedUserId = holder, ServicePlanId = plan
            };
            db.CompanyLicenses.Add(license);
            db.SaveChanges();
            return license.Id;
        }

        private void Extend(int additional, int? licenseId = null)
        {
            using var db = CreateContext(_db);
            db.TeamLimitExtensions.Add(new TeamLimitExtension
            {
                ProfileId = ProfileA, AdditionalTeams = additional, CompanyLicenseId = licenseId,
                Scope = licenseId == null ? TeamLimitExtension.ProfileScope : TeamLimitExtension.LicenseScope,
                CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        private async Task<int> ManagerLimit()
        {
            using var db = CreateContext(_db);
            return await db.ManagerTeamLimitAsync(Manager, limits: null);
        }

        private async Task<string?> AddTeamError()
        {
            using var db = CreateContext(_db);
            return await db.ManagerTeamLimitErrorAsync(Manager, OtherTeamA);
        }

        private Task<TeamLimitExtensionResultDto> ExtendViaApi(int? licenseId)
        {
            var db = CreateContext(_db);
            return new NoLockLicenseService(db).ExtendTeamLimitAsync(new ExtendTeamLimitRequestDto
            {
                ProfileId = ProfileA, CompanyLicenseId = licenseId, AdditionalTeams = 2, Notes = "test"
            }, PlanData.RridaAdmin);
        }

        // ── Manager limit ────────────────────────────────────────────────────

        [Fact]
        public async Task Light_manager_gets_one_team_so_a_second_is_refused()
        {
            AddLicense(_light, holder: Manager);

            Assert.Equal(1, await ManagerLimit());
            Assert.NotNull(await AddTeamError());     // already runs team 294
        }

        [Fact]
        public async Task Pro_manager_gets_three_teams_plus_extensions()
        {
            var pro = AddLicense(_pro, holder: Manager);
            Assert.Equal(3, await ManagerLimit());
            Assert.Null(await AddTeamError());

            Extend(2);                 // profile-wide
            Extend(1, pro);            // this licence
            Assert.Equal(6, await ManagerLimit());
        }

        [Fact]
        public async Task Extensions_do_not_apply_to_light()
        {
            var light = AddLicense(_light, holder: Manager);
            Extend(2);
            Extend(1, light);

            Assert.Equal(1, await ManagerLimit());
        }

        [Fact]
        public async Task Licence_from_before_plans_keeps_three_plus_extensions()
        {
            AddLicense(null, holder: Manager);
            Extend(2);

            Assert.Equal(5, await ManagerLimit());
        }

        // ── Profile capacity ─────────────────────────────────────────────────

        [Fact]
        public async Task Profile_capacity_is_the_sum_of_each_licence_plan_limit()
        {
            AddLicense(_light);
            AddLicense(_pro);
            AddLicense(null);      // legacy = Pro
            Extend(1);             // Pro and legacy only

            using var db = CreateContext(_db);
            var usage = await new CompanyLicenseRepository(db).GetLicenseUsageAsync(ProfileA);

            Assert.Equal(3, usage.TotalLicenses);
            Assert.Equal(1 + 4 + 4, usage.MaxTeams);
        }

        // ── RRIDA Admin extension API ────────────────────────────────────────

        [Fact]
        public async Task Extending_a_light_licence_is_refused()
        {
            var light = AddLicense(_light, holder: Manager);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ExtendViaApi(light));
            Assert.Contains("does not allow a team limit extension", ex.Message);
        }

        [Fact]
        public async Task Profile_extension_is_refused_when_every_licence_is_light()
        {
            AddLicense(_light, holder: Manager);

            await Assert.ThrowsAsync<InvalidOperationException>(() => ExtendViaApi(null));
        }

        [Fact]
        public async Task Extending_a_pro_licence_works_and_shows_in_entitlements()
        {
            var pro = AddLicense(_pro, holder: Manager);

            var r = await ExtendViaApi(pro);

            Assert.Equal(3, r.PreviousLimit);
            Assert.Equal(5, r.NewLimit);

            using var db = CreateContext(_db);
            var e = (await new PlanEntitlementService(db, Roles()).GetForUserAsync(Manager))!;
            Assert.Equal(3, e.TeamCapacity.DefaultCapacity);
            Assert.Equal(2, e.TeamCapacity.ExtendedCapacity);
            Assert.Equal(5, e.TeamCapacity.TotalCapacity);
            Assert.Equal(4, e.TeamCapacity.Remaining);
        }
    }
}
