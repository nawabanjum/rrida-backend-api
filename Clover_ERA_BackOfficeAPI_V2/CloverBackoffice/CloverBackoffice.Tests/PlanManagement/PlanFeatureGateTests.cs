using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.PlanManagement;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using CloverBackoffice.Tests.TeamHealth;
using Moq;
using Xunit;
using static CloverBackoffice.Tests.PlanManagement.PlanManagementTestData;

namespace CloverBackoffice.Tests.PlanManagement
{
    /// <summary>
    /// Backend enforcement: Team Managers 300 (Light), 301 (Pro), 302 (Profile Admin + Team
    /// Manager on Light); RRIDA Admin 87. Today 2026-11-11.
    /// </summary>
    public class PlanFeatureGateTests
    {
        private const uint Profile = 124;
        private const int LightTm = 300, ProTm = 301, PaLightTm = 302;

        private static readonly Dictionary<int, int[]> People = new()
        {
            [LightTm] = new[] { 2 },
            [ProTm] = new[] { 2 },
            [PaLightTm] = new[] { 1, 2 }
        };

        private readonly string _db = Guid.NewGuid().ToString();

        public PlanFeatureGateTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
            var sort = 100;
            void Feature(string key, string title, string type, string? unit = null, string? period = null, bool show = true) =>
                db.PlanFeatures.Add(new PlanFeature
                {
                    Key = key, Title = title, ValueType = type, LimitUnit = unit, LimitPeriod = period, IsSystem = true,
                    IsActive = true, ShowInComparison = show, SortOrder = ++sort, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime
                });
            Feature(PlanFeatureKeys.TeamHealthTimeline, "Interactive Team Health Timeline", PlanFeatureValueTypes.Included);
            Feature(PlanFeatureKeys.DailyNudgesMonthlyReporting, "Daily quick-action nudges and monthly reporting", PlanFeatureValueTypes.Included);
            Feature(PlanFeatureKeys.EranConversations, "Eran conversations", PlanFeatureValueTypes.Limit, "conversations", PlanFeatureLimitPeriods.Month);
            Feature(PlanFeatureKeys.CloverResources, "Clover Resources", PlanFeatureValueTypes.Included, show: false);
            db.SaveChanges();

            var admin = new PlanManagementService(db, PlanManagementTestData.Roles(), new FixedClock(Now));
            var light = Light();
            light.Features!.AddRange(new PlanFeatureValueInputDto[]
            {
                new() { FeatureKey = PlanFeatureKeys.TeamHealthTimeline, IsIncluded = false },
                new() { FeatureKey = PlanFeatureKeys.DailyNudgesMonthlyReporting, IsIncluded = false },
                new() { FeatureKey = PlanFeatureKeys.EranConversations, IsIncluded = true, LimitValue = 10 },
                new() { FeatureKey = PlanFeatureKeys.CloverResources, IsIncluded = false }
            });
            var pro = Pro();
            pro.Features!.AddRange(new PlanFeatureValueInputDto[]
            {
                new() { FeatureKey = PlanFeatureKeys.TeamHealthTimeline, IsIncluded = true },
                new() { FeatureKey = PlanFeatureKeys.DailyNudgesMonthlyReporting, IsIncluded = true },
                new() { FeatureKey = PlanFeatureKeys.EranConversations, IsIncluded = true },
                new() { FeatureKey = PlanFeatureKeys.CloverResources, IsIncluded = true }
            });
            var lightId = admin.CreatePlanAsync(light, RridaAdmin).Result.Data!.Id;
            var proId = admin.CreatePlanAsync(pro, RridaAdmin).Result.Data!.Id;

            foreach (var (id, roles) in People)
                db.Users.Add(new User
                {
                    UserId = id, TenantId = "t", Username = $"u{id}@yopmail.com", Name = $"User {id}",
                    UserGroupId = roles[0], ProfileId = (int)Profile, Status = "1", Type = "User", UserGroup = null!
                });
            db.CompanyLicenses.AddRange(
                Licence(1, LightTm, lightId), Licence(2, ProTm, proId), Licence(3, PaLightTm, lightId));
            db.SaveChanges();
        }

        private static CompanyLicense Licence(int id, int holder, int plan) => new()
        {
            Id = id, ProfileId = Profile, LicenseKey = $"K{id}", LicenseType = "Paid", AssignedUserId = holder,
            ServicePlanId = plan, ExpiryDate = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        private static IUserRoleAssignmentService GateRoles()
        {
            var mock = new Mock<IUserRoleAssignmentService>();
            mock.Setup(r => r.GetRoleIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int id, CancellationToken _) =>
                    id == RridaAdmin ? new List<int> { 3 } : People.TryGetValue(id, out var r) ? r.ToList() : new List<int>());
            return mock.Object;
        }

        private PlanFeatureGate Gate(ApplicationDbContext db) =>
            new(new PlanEntitlementService(db, GateRoles(), new FixedClock(Now)), GateRoles(), new FixedClock(Now));

        private async Task<PlanGateDecision> Check(int userId, string key)
        {
            using var db = CreateContext(_db);
            return await Gate(db).CheckAsync(userId, key);
        }

        private void SendEranMessages(int userId, int count)
        {
            using var db = CreateContext(_db);
            var conv = new EranCoachConversation { UserId = userId, CreatedAt = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc) };
            db.EranCoachConversations.Add(conv);
            db.SaveChanges();
            for (var i = 0; i < count; i++)
                db.EranCoachMessages.Add(new EranCoachMessage
                {
                    ConversationId = conv.Id, Role = "user", CreatedAt = new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc)
                });
            db.SaveChanges();
        }

        // ── Features ─────────────────────────────────────────────────────────

        [Fact]
        public async Task Timeline_is_refused_on_light_and_allowed_on_pro()
        {
            var light = await Check(LightTm, PlanFeatureKeys.TeamHealthTimeline);

            Assert.False(light.Allowed);
            Assert.Equal("Interactive Team Health Timeline is not included in your Light plan. Upgrade to Pro to use it.", light.Message);
            Assert.True((await Check(ProTm, PlanFeatureKeys.TeamHealthTimeline)).Allowed);
            Assert.True((await Check(RridaAdmin, PlanFeatureKeys.TeamHealthTimeline)).Allowed);
        }

        [Fact]
        public async Task Portfolio_is_refused_for_a_light_team_manager_but_not_for_a_profile_admin()
        {
            Assert.False((await Check(LightTm, PlanFeatureKeys.PortfolioView)).Allowed);
            Assert.True((await Check(PaLightTm, PlanFeatureKeys.PortfolioView)).Allowed);
            Assert.True((await Check(ProTm, PlanFeatureKeys.PortfolioView)).Allowed);
        }

        [Fact]
        public async Task Unknown_feature_key_is_not_enforced()
        {
            Assert.True((await Check(LightTm, "NO_SUCH_FEATURE")).Allowed);
        }

        // ── Monthly limits ───────────────────────────────────────────────────

        [Fact]
        public async Task Light_eran_chats_stop_at_ten_this_month()
        {
            SendEranMessages(LightTm, 9);
            Assert.True((await Check(LightTm, PlanFeatureKeys.EranCoach)).Allowed);

            SendEranMessages(LightTm, 1);
            var r = await Check(LightTm, PlanFeatureKeys.EranCoach);

            Assert.False(r.Allowed);
            Assert.True(r.LimitReached);
            Assert.Equal("You have used all 10 chats included in your Light plan this month. More are available from 1 December 2026.", r.Message);
        }

        [Fact]
        public async Task Light_eran_conversations_stop_at_ten_and_pro_is_unlimited()
        {
            for (var i = 0; i < 10; i++) SendEranMessages(LightTm, 0);
            for (var i = 0; i < 30; i++) SendEranMessages(ProTm, 1);

            Assert.False((await Check(LightTm, PlanFeatureKeys.EranConversations)).Allowed);
            Assert.True((await Check(ProTm, PlanFeatureKeys.EranConversations)).Allowed);
            Assert.True((await Check(ProTm, PlanFeatureKeys.EranCoach)).Allowed);
        }

        // ── Content filters ──────────────────────────────────────────────────

        private static List<EngineFeedItemDto> Feed() => new()
        {
            new() { Kind = "daily" }, new() { Kind = "weekly" }, new() { Kind = "monthly" }, new() { Kind = "outcome" }
        };

        [Fact]
        public async Task Light_feed_leaves_out_daily_nudges_and_monthly_report()
        {
            using var db = CreateContext(_db);
            var gate = Gate(db);

            Assert.Equal(new[] { "weekly", "outcome" }, (await gate.FilterFeedAsync(LightTm, Feed())).Select(i => i.Kind));
            Assert.Equal(4, (await gate.FilterFeedAsync(ProTm, Feed())).Count);
            Assert.Equal(4, (await gate.FilterFeedAsync(RridaAdmin, Feed())).Count);
        }

        [Fact]
        public async Task Light_action_card_has_no_clover_resources()
        {
            using var db = CreateContext(_db);
            var gate = Gate(db);
            EngineActiveActionDto Card() => new() { WorksheetCode = "W12", Worksheet = new EngineWorksheetCard(), CanDecline = true };

            var light = (await gate.FilterActionAsync(LightTm, Card()))!;
            Assert.Null(light.Worksheet);
            Assert.Null(light.WorksheetCode);
            Assert.False(light.CanDecline);

            var pro = (await gate.FilterActionAsync(ProTm, Card()))!;
            Assert.NotNull(pro.Worksheet);
            Assert.Equal("W12", pro.WorksheetCode);
            Assert.Null(await gate.FilterActionAsync(LightTm, null));
        }

        [Fact]
        public async Task Hidden_feature_is_enforced_but_not_a_comparison_row()
        {
            using var db = CreateContext(_db);
            var table = await PlanManagementService.BuildComparisonAsync(db, includeInactive: false, CancellationToken.None);

            Assert.DoesNotContain(table.Rows, r => r.Key == PlanFeatureKeys.CloverResources);
            Assert.Contains(table.Rows, r => r.Key == PlanFeatureKeys.TeamHealthTimeline);

            var e = (await new PlanEntitlementService(db, GateRoles(), new FixedClock(Now)).GetForUserAsync(LightTm))!;
            Assert.False(e.Features.Single(f => f.Key == PlanFeatureKeys.CloverResources).IsEnabled);
        }
    }
}
