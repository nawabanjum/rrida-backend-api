using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Tests.TeamHealth;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;
using PlanData = CloverBackoffice.Tests.PlanManagement.PlanManagementTestData;
using ProfileEntity = CloverBackoffice.Infrastructure.Models.Profile;

namespace CloverBackoffice.Tests.PlanManagement
{
    /// <summary>
    /// Plan versions: a terms change saves a new version, existing licences keep the version
    /// they were bought on until an admin migrates them. Manager 300 runs team 294 (profile A).
    /// </summary>
    public class PlanVersionTests
    {
        private const int Manager = ManagerOfTeam;

        private readonly string _db = Guid.NewGuid().ToString();
        private readonly int _light;
        private readonly int _pro;

        public PlanVersionTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
            PlanData.Seed(db);
            db.Profiles.Add(new ProfileEntity
            {
                Id = ProfileA, ProfileShortCode = $"P{ProfileA}", DomainName = $"p{ProfileA}.test",
                ProfileName = $"Profile {ProfileA}", RegistrationDate = DateTime.UtcNow
            });
            db.SaveChanges();

            var admin = Admin(db);
            _light = admin.CreatePlanAsync(PlanData.Light(), PlanData.RridaAdmin).Result.Data!.Id;
            _pro = admin.CreatePlanAsync(PlanData.Pro(), PlanData.RridaAdmin).Result.Data!.Id;
        }

        private static PlanManagementService Admin(ApplicationDbContext db) => new(db, PlanData.Roles(), new FixedClock(PlanData.Now));

        private async Task<T> AsAdmin<T>(Func<PlanManagementService, Task<T>> call)
        {
            using var db = CreateContext(_db);
            return await call(Admin(db));
        }

        private int CurrentVersionId(int planId)
        {
            using var db = CreateContext(_db);
            return db.ServicePlanVersions.Where(v => v.ServicePlanId == planId).OrderByDescending(v => v.VersionNumber).First().Id;
        }

        private int AddLicense(int? plan, int? versionId, int holder)
        {
            using var db = CreateContext(_db);
            var l = new CompanyLicense
            {
                ProfileId = ProfileA, LicenseKey = Guid.NewGuid().ToString(), LicenseType = "Paid", ServicePlanId = plan,
                ServicePlanVersionId = versionId, AssignedUserId = holder, ExpiryDate = DateTime.UtcNow.AddDays(30)
            };
            db.CompanyLicenses.Add(l);
            db.SaveChanges();
            return l.Id;
        }

        private Task<PlanManagementResult<ServicePlanDetailDto>> SetLightTeams(int teams, int version) =>
            AsAdmin(s => s.SetPlanFeaturesAsync(_light, new SetPlanFeaturesDto
            {
                Version = version,
                Features = new() { new() { FeatureKey = "MANAGERS_AND_TEAMS", IsIncluded = true, LimitValue = teams, DisplayText = teams.ToString() } }
            }, PlanData.RridaAdmin));

        private async Task<int> ManagerLimit()
        {
            using var db = CreateContext(_db);
            return await db.ManagerTeamLimitAsync(Manager, limits: null);
        }

        [Fact]
        public async Task Creating_a_plan_saves_version_one()
        {
            var detail = (await AsAdmin(s => s.GetPlanAsync(_light, PlanData.RridaAdmin))).Data!;
            Assert.Equal(1, detail.CurrentVersionNumber);

            var versions = (await AsAdmin(s => s.GetPlanVersionsAsync(_light, PlanData.RridaAdmin))).Data!;
            var v1 = Assert.Single(versions.Versions);
            Assert.True(v1.IsCurrent);
            Assert.Equal(97m, v1.Terms.PriceTiers.Single().MonthlyPrice);
            Assert.Equal(1, v1.Terms.Feature("MANAGERS_AND_TEAMS")!.LimitValue);
        }

        [Fact]
        public async Task Only_terms_changes_save_a_new_version()
        {
            await AsAdmin(s => s.UpdatePlanAsync(_light, new UpdateServicePlanDto { Version = 1, Description = "Small teams" }, PlanData.RridaAdmin));
            Assert.Equal(1, (await AsAdmin(s => s.GetPlanAsync(_light, PlanData.RridaAdmin))).Data!.CurrentVersionNumber);

            var r = await SetLightTeams(1, 2);                         // same value as before
            Assert.Equal(1, r.Data!.CurrentVersionNumber);

            r = await SetLightTeams(2, 3);                             // a real change
            Assert.Equal(2, r.Data!.CurrentVersionNumber);
        }

        [Fact]
        public async Task Existing_licence_keeps_its_terms_until_migrated()
        {
            AddLicense(_light, null, Manager);                         // bought before the change (= version 1)
            Assert.Equal(1, await ManagerLimit());

            await SetLightTeams(2, 1);                                 // admin raises Light to 2 teams (version 2)
            Assert.Equal(1, await ManagerLimit());                     // still the terms bought

            using (var db = CreateContext(_db))
            {
                var e = (await new PlanEntitlementService(db, Roles()).GetForUserAsync(Manager))!;
                Assert.Equal(1, e.PlanVersionNumber);
                Assert.Equal(1, e.TeamCapacity.TotalCapacity);
            }

            var m = (await AsAdmin(s => s.MigrateLicencesAsync(_light, new MigratePlanLicencesDto { Reason = "Agreed" }, PlanData.RridaAdmin))).Data!;
            Assert.Equal(2, m.ToVersionNumber);
            Assert.Equal(1, m.LicencesMigrated);
            Assert.Equal(2, await ManagerLimit());                     // now the new terms
        }

        [Fact]
        public async Task New_licence_on_the_new_version_gets_the_new_terms()
        {
            await SetLightTeams(2, 1);
            AddLicense(_light, CurrentVersionId(_light), Manager);

            Assert.Equal(2, await ManagerLimit());
        }

        [Fact]
        public async Task Pre_plan_licence_reads_pro_version_one_even_after_pro_changes()
        {
            AddLicense(null, null, Manager);
            await AsAdmin(s => s.SetPlanFeaturesAsync(_pro, new SetPlanFeaturesDto
            {
                Version = 1,
                Features = new() { new() { FeatureKey = "MANAGERS_AND_TEAMS", IsIncluded = true, LimitValue = 5 } }
            }, PlanData.RridaAdmin));

            Assert.Equal(3, await ManagerLimit());

            var versions = (await AsAdmin(s => s.GetPlanVersionsAsync(_pro, PlanData.RridaAdmin))).Data!;
            Assert.Equal(2, versions.CurrentVersionNumber);
            Assert.Equal(1, versions.Versions.Single(v => v.VersionNumber == 1).LicenceCount);
            Assert.Equal(0, versions.Versions.Single(v => v.VersionNumber == 2).LicenceCount);
        }

        [Fact]
        public async Task Migration_can_be_limited_to_one_version_and_one_company()
        {
            AddLicense(_light, null, Manager);
            await SetLightTeams(2, 1);

            var none = (await AsAdmin(s => s.MigrateLicencesAsync(_light, new MigratePlanLicencesDto { ProfileId = 999 }, PlanData.RridaAdmin))).Data!;
            Assert.Equal(0, none.LicencesMigrated);

            var bad = await AsAdmin(s => s.MigrateLicencesAsync(_light, new MigratePlanLicencesDto { FromVersionNumber = 9 }, PlanData.RridaAdmin));
            Assert.Equal(PlanManagementError.BadRequest, bad.Error);

            var one = (await AsAdmin(s => s.MigrateLicencesAsync(_light, new MigratePlanLicencesDto { FromVersionNumber = 1, ProfileId = ProfileA }, PlanData.RridaAdmin))).Data!;
            Assert.Equal(1, one.LicencesMigrated);
        }

        [Fact]
        public async Task Only_rrida_admin_sees_versions_or_migrates()
        {
            Assert.Equal(PlanManagementError.Forbidden, (await AsAdmin(s => s.GetPlanVersionsAsync(_light, PlanData.ProfileAdmin))).Error);
            Assert.Equal(PlanManagementError.Forbidden, (await AsAdmin(s => s.MigrateLicencesAsync(_light, null, PlanData.ProfileAdmin))).Error);
        }
    }
}
