using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using CloverBackoffice.Tests.TeamHealth;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;
using PlanData = CloverBackoffice.Tests.PlanManagement.PlanManagementTestData;
using ProfileEntity = CloverBackoffice.Infrastructure.Models.Profile;

namespace CloverBackoffice.Tests.PlanManagement
{
    /// <summary>
    /// Plan, price, counts, capacity and entitlements on the licence APIs (subscription/detail,
    /// license-assignments, availability). Manager 300 runs team 294 (profile A); Profile Admin 272.
    /// </summary>
    public class PlanLicenceResponseTests
    {
        private const int Manager = ManagerOfTeam;

        private readonly string _db = Guid.NewGuid().ToString();
        private readonly int _light;
        private readonly int _pro;

        public PlanLicenceResponseTests()
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

            var admin = new PlanManagementService(db, PlanData.Roles(), new FixedClock(PlanData.Now));
            _light = admin.CreatePlanAsync(PlanData.Light(), PlanData.RridaAdmin).Result.Data!.Id;
            _pro = admin.CreatePlanAsync(PlanData.Pro(), PlanData.RridaAdmin).Result.Data!.Id;
        }

        private int AddSub(int? plan, decimal? unitPrice, int qty, BillingCycle cycle)
        {
            using var db = CreateContext(_db);
            var sub = new CompanySubscription
            {
                ProfileId = ProfileA, ServicePlanId = plan, ServicePlanUnitPrice = unitPrice, TeamCount = qty,
                BillingCycle = cycle, Status = SubscriptionStatus.Active,
                CurrentPeriodStart = DateTime.UtcNow, CurrentPeriodEnd = DateTime.UtcNow.AddDays(30)
            };
            db.CompanySubscriptions.Add(sub);
            db.SaveChanges();
            return sub.Id;
        }

        private int AddLicense(int? plan, int? sub = null, int? holder = null, string type = "Paid")
        {
            using var db = CreateContext(_db);
            var l = new CompanyLicense
            {
                ProfileId = ProfileA, LicenseKey = Guid.NewGuid().ToString(), LicenseType = type, ServicePlanId = plan,
                CompanySubscriptionId = sub, AssignedUserId = holder, ExpiryDate = DateTime.UtcNow.AddDays(30)
            };
            db.CompanyLicenses.Add(l);
            db.SaveChanges();
            return l.Id;
        }

        // ── Price and plan per licence ───────────────────────────────────────

        [Fact]
        public async Task Licence_price_is_what_was_paid_else_todays_tier_and_none_for_trial()
        {
            var paid = AddLicense(_pro, AddSub(_pro, 595m, 6, BillingCycle.Monthly));
            var catalogue = AddLicense(_pro, AddSub(_pro, null, 6, BillingCycle.Annual));
            var legacy = AddLicense(null, AddSub(null, null, 6, BillingCycle.Monthly));   // pre-plan: flat price
            var trial = AddLicense(null, null, type: "Trial");

            using var db = CreateContext(_db);
            var info = await db.GetLicencePlanInfoAsync(new[] { paid, catalogue, legacy, trial });

            Assert.Equal(595m, info[paid].PricePerManager);
            Assert.Equal(5950m, info[catalogue].PricePerManager);          // Pro 5-9 annual
            Assert.Equal("Pro", info[legacy].PlanName);
            Assert.True(info[legacy].IsLegacyPlan);
            Assert.Equal(695m, info[legacy].PricePerManager);
            Assert.Null(info[trial].PricePerManager);
        }

        // ── Counts per plan ──────────────────────────────────────────────────

        [Fact]
        public async Task Licences_are_counted_per_plan()
        {
            AddLicense(_light);                       // free
            AddLicense(_pro, holder: Manager);        // consumed
            AddLicense(null);                         // legacy Pro, free

            using var db = CreateContext(_db);
            var counts = await db.GetPlanLicenseCountsAsync(ProfileA);

            var light = counts.Single(c => c.PlanCode == "light");
            Assert.Equal((1, 0, 1), (light.TotalLicenses, light.ConsumedLicenses, light.AvailableLicenses));
            var pro = counts.Single(c => c.PlanCode == "pro");
            Assert.Equal((2, 1, 1), (pro.TotalLicenses, pro.ConsumedLicenses, pro.AvailableLicenses));
        }

        [Fact]
        public async Task Availability_returns_counts_per_plan()
        {
            AddLicense(_light);
            AddLicense(_pro, holder: Manager);

            using var db = CreateContext(_db);
            var service = new LicenseAvailabilityService(new CompanyLicenseRepository(db),
                new Mock<ISubscriptionService>().Object, NullLogger<LicenseAvailabilityService>.Instance);
            var dto = await service.GetAvailabilityAsync(ProfileA, new User { UserId = PlanData.RridaAdmin, UserGroupId = 3 });

            Assert.Equal(2, dto.TotalLicenses);
            Assert.Equal(new[] { "light", "pro" }, dto.Plans.Select(p => p.PlanCode));
            Assert.Equal(1, dto.Plans.Single(p => p.PlanCode == "light").AvailableLicenses);
        }

        // ── License-assignments rows ─────────────────────────────────────────

        [Fact]
        public async Task Manager_row_shows_plan_price_capacity_and_entitlements()
        {
            var sub = AddSub(_light, 97m, 1, BillingCycle.Monthly);
            AddLicense(_light, sub, holder: Manager);

            using var db = CreateContext(_db);
            var provider = new Mock<IServiceProvider>();
            provider.Setup(p => p.GetService(typeof(IPlanEntitlementService)))
                .Returns(new PlanEntitlementService(db, Roles()));
            var service = new CompanyLicenseService(new CompanyLicenseRepository(db), db,
                new Mock<ILicensedPeriodService>().Object, provider.Object);
            var rows = await service.GetLicenseDetailsAsync(ProfileA, ProfileAdminA);

            var row = rows.Single(r => r.ManagerId == Manager);
            Assert.Equal("Light", row.ServicePlanName);
            Assert.Equal(_light, row.ServicePlanId);
            Assert.Equal(97m, row.PricePerManager);
            Assert.Equal(1, row.TeamsLimit);
            Assert.Equal(1, row.DefaultTeamCapacity);
            Assert.Equal(0, row.ExtendedTeamCapacity);
            Assert.NotNull(row.Entitlements);
            Assert.Contains("DAILY_CHECKINS", row.Entitlements!.EnabledFeatures);
            Assert.DoesNotContain("PORTFOLIO_VIEW", row.Entitlements.EnabledFeatures);
            Assert.Equal(10, row.Entitlements.EranChats.Limit);
            Assert.Equal(10, row.Entitlements.EranChats.Remaining);
        }

        [Fact]
        public void Unit_price_metadata_parsing()
        {
            Assert.Equal(595m, SubscriptionService.ParseServicePlanUnitPriceMetadata(new Dictionary<string, string> { ["servicePlanUnitPrice"] = "595" }));
            Assert.Equal(97.5m, SubscriptionService.ParseServicePlanUnitPriceMetadata(new Dictionary<string, string> { ["servicePlanUnitPrice"] = "97.5" }));
            Assert.Null(SubscriptionService.ParseServicePlanUnitPriceMetadata(new Dictionary<string, string> { ["servicePlanUnitPrice"] = "-1" }));
            Assert.Null(SubscriptionService.ParseServicePlanUnitPriceMetadata(null));
        }
    }
}
