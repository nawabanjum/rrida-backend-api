using CloverBackoffice.Components.Enums;
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
    /// POST api/license-assignments (Assign + managerId + planCode): giving a Team Manager a
    /// Light or Pro licence and switching between them. Manager 300 runs team 294 (profile A);
    /// Profile Admin 272. Light = 1 team, Pro = 3. Real licence service on the in-memory
    /// database; only the advisory lock is skipped.
    /// </summary>
    public class PlanLicenceAssignTests
    {
        private const int Manager = ManagerOfTeam;

        private readonly string _db = Guid.NewGuid().ToString();
        private readonly int _light;
        private readonly int _pro;

        public PlanLicenceAssignTests()
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

        private int AddLicense(int? plan, int? holder = null, uint? onTeam = null, string type = "Paid")
        {
            using var db = CreateContext(_db);
            var license = new CompanyLicense
            {
                ProfileId = ProfileA, LicenseKey = Guid.NewGuid().ToString(), LicenseType = type,
                ExpiryDate = DateTime.UtcNow.AddDays(30), AssignedUserId = holder, ServicePlanId = plan
            };
            db.CompanyLicenses.Add(license);
            db.SaveChanges();
            if (onTeam.HasValue)
            {
                db.AssignedCompanyLicenses.Add(new AssignedCompanyLicense
                {
                    CompanyLicenseId = license.Id, BusinessUnitId = onTeam.Value,
                    AssignedAt = DateTime.UtcNow.AddDays(-10), IsActive = true
                });
                db.SaveChanges();
            }
            return license.Id;
        }

        private async Task<LicenseAssignmentResultDto> Assign(string? planCode, int? licenseId = null)
        {
            using var db = CreateContext(_db);
            return await new NoLockLicenseService(db).ExecuteLifecycleAsync(new LicenseAssignmentRequestDto
            {
                Action = LicenseAssignmentAction.Assign,
                ManagerId = Manager,
                PlanCode = planCode,
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

        private async Task<string?> PlanCodeOfManager()
        {
            using var db = CreateContext(_db);
            return (await new PlanEntitlementService(db, Roles()).GetForUserAsync(Manager))!.PlanCode;
        }

        [Fact]
        public async Task Gives_a_free_licence_of_the_plan_asked_for()
        {
            AddLicense(_pro);
            var light = AddLicense(_light);

            var r = await Assign("light");

            Assert.Equal(light, r.CompanyLicenseId);
            Assert.Equal(_light, r.ServicePlanId);
            Assert.Equal("Light", r.ServicePlanName);
            Assert.False(r.PlanSwitched);
            Assert.Equal(Manager, License(light).AssignedUserId);
            Assert.Equal(Team, ActiveTeamOf(light));
            Assert.Equal("light", await PlanCodeOfManager());
        }

        [Fact]
        public async Task Light_to_pro_switch_releases_the_light_licence_and_moves_the_team()
        {
            var light = AddLicense(_light, holder: Manager, onTeam: Team);
            var pro = AddLicense(_pro);

            var r = await Assign("pro");

            Assert.Equal(pro, r.CompanyLicenseId);
            Assert.True(r.PlanSwitched);
            Assert.Equal(light, r.ReplacedLicenseId);
            Assert.Equal("Pro", r.ServicePlanName);

            Assert.Equal(Manager, License(pro).AssignedUserId);
            Assert.Equal(Team, ActiveTeamOf(pro));
            var old = License(light);
            Assert.Null(old.AssignedUserId);
            Assert.False(old.IsDel);                 // still a live, paid licence: free again
            Assert.Null(ActiveTeamOf(light));
            Assert.Equal("pro", await PlanCodeOfManager());

            using var db = CreateContext(_db);
            var usage = await new CompanyLicenseRepository(db).GetLicenseUsageAsync(ProfileA);
            Assert.Equal(2, usage.TotalLicenses);
            Assert.Equal(1, usage.AvailableLicenses);
        }

        [Fact]
        public async Task Pro_to_light_switch_works_when_the_manager_runs_one_team()
        {
            var pro = AddLicense(_pro, holder: Manager, onTeam: Team);
            var light = AddLicense(_light);

            var r = await Assign("light");

            Assert.True(r.PlanSwitched);
            Assert.Equal(pro, r.ReplacedLicenseId);
            Assert.Equal(light, r.CompanyLicenseId);
            Assert.Equal("light", await PlanCodeOfManager());
        }

        [Fact]
        public async Task Pro_to_light_is_refused_while_the_manager_runs_more_teams_than_light_allows()
        {
            using (var db = CreateContext(_db))
            {
                db.UserTeams.Add(new UserTeam { Id = 90, UserId = Manager, UnitId = OtherTeamA, CreatedDate = DateTime.UtcNow });
                db.SaveChanges();
            }
            var pro = AddLicense(_pro, holder: Manager, onTeam: Team);
            AddLicense(_light);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Assign("light"));

            Assert.Contains("runs 2 teams but the Light plan allows 1", ex.Message);
            Assert.Equal(Manager, License(pro).AssignedUserId);   // nothing changed
            Assert.Equal(Team, ActiveTeamOf(pro));
        }

        [Fact]
        public async Task No_free_licence_of_the_plan_is_refused()
        {
            AddLicense(_light);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Assign("pro"));
            Assert.Equal("No available Pro licence. Purchase a Pro licence first.", ex.Message);
        }

        [Fact]
        public async Task Same_plan_already_held_is_idempotent()
        {
            var pro = AddLicense(_pro, holder: Manager, onTeam: Team);
            AddLicense(_pro);

            var r = await Assign("pro");

            Assert.Equal(pro, r.CompanyLicenseId);
            Assert.False(r.PlanSwitched);
        }

        [Fact]
        public async Task Licence_sent_must_be_on_the_plan_sent()
        {
            var light = AddLicense(_light);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Assign("pro", light));
            Assert.Contains("is not a Pro licence", ex.Message);
        }

        [Fact]
        public async Task Licence_from_before_plans_counts_as_pro()
        {
            var legacy = AddLicense(null, holder: Manager, onTeam: Team);
            var light = AddLicense(_light);

            Assert.Equal(legacy, (await Assign("pro")).CompanyLicenseId);   // already Pro

            var r = await Assign("light");
            Assert.True(r.PlanSwitched);
            Assert.Equal(legacy, r.ReplacedLicenseId);
            Assert.Equal(light, r.CompanyLicenseId);
        }

        [Fact]
        public async Task Another_licence_of_the_same_plan_still_needs_the_old_one_removed_first()
        {
            AddLicense(_pro, holder: Manager, onTeam: Team);
            var otherPro = AddLicense(_pro);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Assign(null, otherPro));
            Assert.Contains("Remove it before assigning another one", ex.Message);
        }

        [Fact]
        public async Task Unknown_plan_code_is_refused()
        {
            AddLicense(_pro);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Assign("gold"));
            Assert.Equal("Plan not found.", ex.Message);
        }
    }
}
