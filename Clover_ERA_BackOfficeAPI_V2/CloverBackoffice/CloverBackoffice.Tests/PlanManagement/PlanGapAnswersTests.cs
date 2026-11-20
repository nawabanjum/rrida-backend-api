using CloverBackoffice.Components.Enums;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using CloverBackoffice.Tests.TeamHealth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;
using PlanData = CloverBackoffice.Tests.PlanManagement.PlanManagementTestData;
using ProfileEntity = CloverBackoffice.Infrastructure.Models.Profile;

namespace CloverBackoffice.Tests.PlanManagement
{
    /// <summary>
    /// The product decisions on the open gaps: no random plan on auto-assign (5), over-limit
    /// teams kept and flagged (4), replaced licence cancelled on a switch (2), Eran without
    /// worksheets on Light (6). Manager 300 runs team 294 (profile A); Profile Admin 272.
    /// </summary>
    public class PlanGapAnswersTests
    {
        private const int Manager = ManagerOfTeam;

        private readonly string _db = Guid.NewGuid().ToString();
        private readonly int _light;
        private readonly int _pro;

        public PlanGapAnswersTests()
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

        private int AddLicense(int? plan, int? holder = null, string type = "Paid", int? sub = null)
        {
            using var db = CreateContext(_db);
            var l = new CompanyLicense
            {
                ProfileId = ProfileA, LicenseKey = Guid.NewGuid().ToString(), LicenseType = type, ServicePlanId = plan,
                AssignedUserId = holder, CompanySubscriptionId = sub, ExpiryDate = DateTime.UtcNow.AddDays(30)
            };
            db.CompanyLicenses.Add(l);
            db.SaveChanges();
            return l.Id;
        }

        private sealed class NoLockLicenseService : CompanyLicenseService
        {
            public NoLockLicenseService(ApplicationDbContext db)
                : base(new CompanyLicenseRepository(db), db, new Mock<ILicensedPeriodService>().Object) { }

            protected override Task LockProfileLicensesAsync(uint profileId) => Task.CompletedTask;
        }

        // ── 5: never a random plan ───────────────────────────────────────────

        [Fact]
        public async Task Free_licence_pick_follows_admin_choice_or_the_only_plan()
        {
            var light = AddLicense(_light);
            using (var db = CreateContext(_db))
                Assert.Equal((light, LicencePlanQueries.AutoPickOutcome.Picked), await db.PickFreeLicenseAsync(ProfileA, null, false));

            var pro = AddLicense(_pro);
            using var db2 = CreateContext(_db);
            Assert.Equal((null, LicencePlanQueries.AutoPickOutcome.MixedPlans), await db2.PickFreeLicenseAsync(ProfileA, null, false));
            Assert.Equal(pro, (await db2.PickFreeLicenseAsync(ProfileA, "PRO", false)).LicenseId);
            Assert.Equal(light, (await db2.PickFreeLicenseAsync(ProfileA, "light", false)).LicenseId);
            Assert.Equal(pro, (await db2.PickFreeLicenseAsync(ProfileA, null, useDefaultPlan: true)).LicenseId);   // team coverage
        }

        [Fact]
        public async Task Admin_assign_without_a_plan_when_both_plans_are_free_asks_for_one()
        {
            AddLicense(_light);
            AddLicense(_pro);

            using var db = CreateContext(_db);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new NoLockLicenseService(db).ExecuteLifecycleAsync(
                new LicenseAssignmentRequestDto { Action = LicenseAssignmentAction.Assign, ManagerId = Manager }, ProfileAdminA));
            Assert.Contains("Send planCode", ex.Message);
        }

        // ── 4: over-limit teams are kept and said ────────────────────────────

        [Fact]
        public async Task Light_manager_with_two_teams_keeps_them_and_is_flagged()
        {
            using (var db = CreateContext(_db))
            {
                db.UserTeams.Add(new UserTeam { Id = 91, UserId = Manager, UnitId = OtherTeamA, CreatedDate = DateTime.UtcNow });
                db.SaveChanges();
            }
            AddLicense(_light, holder: Manager);

            using var db2 = CreateContext(_db);
            var e = (await new PlanEntitlementService(db2, Roles()).GetForUserAsync(Manager))!;
            Assert.Equal(2, e.TeamCapacity.TeamsUsed);
            Assert.True(e.TeamCapacity.IsOverLimit);
            Assert.Contains("Existing teams are kept", e.TeamCapacity.Message);

            var rows = await new CompanyLicenseService(new CompanyLicenseRepository(db2), db2, new Mock<ILicensedPeriodService>().Object)
                .GetLicenseDetailsAsync(ProfileA, ProfileAdminA);
            var row = rows.Single(r => r.ManagerId == Manager);
            Assert.True(row.IsOverTeamLimit);
            Assert.Equal(2, row.TeamsUsed);
        }

        // ── 2: replaced licence is cancelled (only when billed) ──────────────

        private SubscriptionService Subscriptions(ApplicationDbContext db) => new(
            new CompanySubscriptionRepository(db, new PaymentAuditLogRepository(db)),
            new Mock<IPlanRepository>().Object, new Mock<IStripeService>().Object,
            new Mock<IPromoCodeService>().Object, new Mock<IPromoCodeRepository>().Object,
            new Mock<IPaymentRepository>().Object, new CompanyLicenseRepository(db), db,
            Options.Create(new StripeSettings()), NullLogger<SubscriptionService>.Instance,
            new Mock<ILegacyTrialBackfillService>().Object);

        [Fact]
        public async Task Nothing_to_cancel_for_a_trial_or_unbilled_licence()
        {
            var trial = AddLicense(null, type: "Trial");
            int unbilledSub;
            using (var db = CreateContext(_db))
            {
                var s = new CompanySubscription
                {
                    ProfileId = ProfileA, TeamCount = 1, Status = SubscriptionStatus.Active,
                    CurrentPeriodStart = DateTime.UtcNow, CurrentPeriodEnd = DateTime.UtcNow.AddDays(30)
                };
                db.CompanySubscriptions.Add(s);
                db.SaveChanges();
                unbilledSub = s.Id;
            }
            var unbilled = AddLicense(_light, sub: unbilledSub);

            using var db2 = CreateContext(_db);
            Assert.False(await Subscriptions(db2).CancelReplacedLicenceAsync(trial, PlanData.RridaAdmin));
            Assert.False(await Subscriptions(db2).CancelReplacedLicenceAsync(unbilled, PlanData.RridaAdmin));
        }

        // ── 6: Eran gives no worksheets on Light ─────────────────────────────

        [Fact]
        public async Task Eran_worksheet_tool_refuses_when_the_plan_has_no_clover_resources()
        {
            var tools = new EranCoachTools(new Mock<IEranCoachCorpusRepository>().Object, new Mock<IEmbeddingRepository>().Object,
                new Mock<IEranCoachReportService>().Object, NullLogger<EranCoachTools>.Instance)
            { WorksheetsAllowed = false };

            var reply = await tools.RunAsync("get_worksheet", "{\"id\":\"C1\"}");
            Assert.Contains("not part of this manager's plan", reply);

            var search = await tools.RunAsync("search_clover", "{\"query\":\"trust\",\"source\":\"worksheet\"}");
            Assert.Contains("not part of this manager's plan", search);
        }
    }
}
