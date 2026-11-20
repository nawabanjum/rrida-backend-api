using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Infrastructure.Models;
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
    /// api/entitlements. Profile 124: Profile Admin 272; Team Managers 300 (holds a Light
    /// licence, team 500), 301 (holds a pre-plan licence → Pro), 302 (no licence of their own,
    /// team 501 covered by a Pro licence), 303 (no licence), 304 (expired Pro licence).
    /// Profile 200: Profile Admin 400. RRIDA Admin 87. Profile 124 has a +2 team extension.
    /// Today 2026-11-11.
    /// </summary>
    public class PlanEntitlementServiceTests
    {
        private const uint Profile = 124;
        private const uint OtherProfile = 200;
        private const int Pa = 272, LightTm = 300, LegacyTm = 301, TeamTm = 302, NoLicenceTm = 303, ExpiredTm = 304, OtherPa = 400;

        private static readonly Dictionary<int, (uint profile, int[] roles)> People = new()
        {
            [Pa] = (Profile, new[] { 1 }),
            [LightTm] = (Profile, new[] { 2 }),
            [LegacyTm] = (Profile, new[] { 2 }),
            [TeamTm] = (Profile, new[] { 2 }),
            [NoLicenceTm] = (Profile, new[] { 2 }),
            [ExpiredTm] = (Profile, new[] { 2 }),
            [OtherPa] = (OtherProfile, new[] { 1 })
        };

        private readonly string _db = Guid.NewGuid().ToString();

        public PlanEntitlementServiceTests()
        {
            using var db = CreateContext(_db);
            Seed(db);   // features + RRIDA Admin 87
            var admin = new PlanManagementService(db, PlanManagementTestData.Roles(), new FixedClock(Now));
            var light = admin.CreatePlanAsync(Light(), RridaAdmin).Result.Data!.Id;
            var pro = admin.CreatePlanAsync(Pro(), RridaAdmin).Result.Data!.Id;

            foreach (var (id, (profile, roles)) in People)
                db.Users.Add(new User
                {
                    UserId = id, TenantId = "t", Username = $"u{id}@yopmail.com", Name = $"User {id}",
                    UserGroupId = roles[0], ProfileId = (int)profile, Status = "1", Type = "User", UserGroup = null!
                });

            db.BusinessUnits.AddRange(Unit(500), Unit(501));
            db.UserTeams.AddRange(
                new UserTeam { Id = 1, UserId = LightTm, UnitId = 500, CreatedDate = Now.UtcDateTime },
                new UserTeam { Id = 2, UserId = TeamTm, UnitId = 501, CreatedDate = Now.UtcDateTime });

            db.CompanyLicenses.AddRange(
                Licence(1, LightTm, light),
                Licence(2, LegacyTm, null),
                Licence(3, null, pro),
                Licence(4, ExpiredTm, pro, expired: true));
            db.AssignedCompanyLicenses.Add(new AssignedCompanyLicense { Id = 1, CompanyLicenseId = 3, BusinessUnitId = 501, IsActive = true });
            db.TeamLimitExtensions.Add(new TeamLimitExtension
            {
                ProfileId = Profile, Scope = TeamLimitExtension.ProfileScope, AdditionalTeams = 2, CreatedAt = Now.UtcDateTime
            });

            // Eran: 2 conversations + 3 sent messages this month, older ones not counted.
            var thisMonth = new DateTime(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);
            var lastMonth = new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);
            db.EranCoachConversations.AddRange(
                new EranCoachConversation { Id = 1, UserId = LightTm, CreatedAt = thisMonth },
                new EranCoachConversation { Id = 2, UserId = LightTm, CreatedAt = thisMonth },
                new EranCoachConversation { Id = 3, UserId = LightTm, CreatedAt = lastMonth });
            db.EranCoachMessages.AddRange(
                new EranCoachMessage { Id = 1, ConversationId = 1, Role = "user", CreatedAt = thisMonth },
                new EranCoachMessage { Id = 2, ConversationId = 1, Role = "assistant", CreatedAt = thisMonth },
                new EranCoachMessage { Id = 3, ConversationId = 2, Role = "user", CreatedAt = thisMonth },
                new EranCoachMessage { Id = 4, ConversationId = 2, Role = "user", CreatedAt = thisMonth },
                new EranCoachMessage { Id = 5, ConversationId = 3, Role = "user", CreatedAt = lastMonth });
            db.SaveChanges();
        }

        private static BusinessUnit Unit(uint id) => new()
        {
            Id = id, BusinessUnitCode = $"BU{id}", BusinessUnitShortCode = $"S{id}", Name = $"Team {id}",
            NameInvariant = $"team {id}", RegistrationDate = DateTime.UtcNow, ProfileId = Profile,
            ActiveDailyQuestionSetGroupId = 1, ActiveDailyQuestionSetGroup = null!
        };

        private static CompanyLicense Licence(int id, int? holder, int? plan, bool expired = false) => new()
        {
            Id = id, ProfileId = Profile, LicenseKey = $"CLD-PRO{id:000}-124", LicenseType = "Paid",
            AssignedUserId = holder, ServicePlanId = plan,
            ExpiryDate = expired ? new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) : new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        private static IUserRoleAssignmentService Roles()
        {
            var mock = new Mock<IUserRoleAssignmentService>();
            mock.Setup(r => r.GetRoleIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int id, CancellationToken _) =>
                    id == RridaAdmin ? new List<int> { 3 } : People.TryGetValue(id, out var p) ? p.roles.ToList() : new List<int>());
            return mock.Object;
        }

        private PlanEntitlementService Service(ApplicationDbContext db) => new(db, Roles(), new FixedClock(Now));

        private async Task<PlanEntitlementsDto> For(int userId)
        {
            using var db = CreateContext(_db);
            return (await Service(db).GetForUserAsync(userId))!;
        }

        private static FeatureEntitlementDto F(PlanEntitlementsDto e, string key) => e.Features.Single(f => f.Key == key);

        // ── Plan resolution ──────────────────────────────────────────────────

        [Fact]
        public async Task Light_holder_gets_light_features_limits_usage_and_one_team()
        {
            var e = await For(LightTm);

            Assert.True(e.IsPlanGated);
            Assert.Equal("light", e.PlanCode);
            Assert.Equal(1, e.LicenseId);
            Assert.Equal(PlanEntitlementService.HeldSource, e.LicenseSource);
            Assert.False(e.IsLegacyPlanDefault);

            var eran = F(e, "ERAN_COACH");
            Assert.True(eran.IsEnabled);
            Assert.Equal(10, eran.Limit);
            Assert.Equal(3, eran.Used);          // user messages this month only
            Assert.Equal(7, eran.Remaining);
            Assert.False(F(e, "PORTFOLIO_VIEW").IsEnabled);
            Assert.Equal("Clover actions", F(e, "WEEKLY_SUGGESTED_ACTION").Display);

            Assert.Equal(1, e.TeamCapacity.DefaultCapacity);
            Assert.Equal(0, e.TeamCapacity.ExtendedCapacity);   // Light: extensions don't apply
            Assert.Equal(1, e.TeamCapacity.TeamsUsed);
            Assert.Equal(0, e.TeamCapacity.Remaining);
            Assert.False(e.TeamCapacity.CanBeExtended);
            Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), e.UsagePeriodStart);
        }

        [Fact]
        public async Task Licence_from_before_plans_reads_as_pro_with_extension()
        {
            var e = await For(LegacyTm);

            Assert.Equal("pro", e.PlanCode);
            Assert.True(e.IsLegacyPlanDefault);
            Assert.True(F(e, "ERAN_COACH").IsUnlimited);
            Assert.Null(F(e, "ERAN_COACH").Remaining);
            Assert.True(F(e, "PORTFOLIO_VIEW").IsEnabled);
            Assert.Equal(3, e.TeamCapacity.DefaultCapacity);
            Assert.Equal(2, e.TeamCapacity.ExtendedCapacity);
            Assert.Equal(5, e.TeamCapacity.TotalCapacity);
            Assert.True(e.TeamCapacity.CanBeExtended);
        }

        [Fact]
        public async Task Manager_without_own_licence_uses_the_one_on_their_team()
        {
            var e = await For(TeamTm);

            Assert.Equal(3, e.LicenseId);
            Assert.Equal(PlanEntitlementService.TeamSource, e.LicenseSource);
            Assert.Equal("pro", e.PlanCode);
        }

        [Theory]
        [InlineData(NoLicenceTm)]
        [InlineData(ExpiredTm)]
        public async Task Manager_with_no_live_licence_is_not_limited_by_a_plan(int userId)
        {
            var e = await For(userId);

            // As before plans: an expired / missing licence does not switch features off.
            Assert.False(e.IsPlanGated);
            Assert.Null(e.LicenseId);
            Assert.Null(e.PlanId);
            Assert.All(e.Features, f => Assert.True(f.IsEnabled));
            Assert.True(F(e, "ERAN_COACH").IsUnlimited);
            Assert.Equal(5, e.TeamCapacity.TotalCapacity);       // as enforced: profile teams per licence (3 + 2)
        }

        [Theory]
        [InlineData(RridaAdmin)]
        [InlineData(Pa)]
        public async Task Users_without_team_manager_role_are_not_gated(int userId)
        {
            var e = await For(userId);

            Assert.False(e.IsPlanGated);
            Assert.All(e.Features, f => Assert.True(f.IsEnabled));
            Assert.True(F(e, "ERAN_COACH").IsUnlimited);
        }

        // ── Checks ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Has_feature_follows_plan_and_remaining_usage()
        {
            using var db = CreateContext(_db);
            var s = Service(db);

            Assert.False(await s.HasFeatureAsync(LightTm, "PORTFOLIO_VIEW"));
            Assert.True(await s.HasFeatureAsync(LightTm, "ERAN_COACH"));
            Assert.False(await s.HasFeatureAsync(LightTm, "MANAGERS_AND_TEAMS"));   // 1 of 1 teams used
            Assert.True(await s.HasFeatureAsync(LegacyTm, "PORTFOLIO_VIEW"));
            Assert.True(await s.HasFeatureAsync(Pa, "PORTFOLIO_VIEW"));
            Assert.False(await s.HasFeatureAsync(LightTm, "NO_SUCH_KEY"));
            Assert.Equal(7, (await s.GetFeatureAsync(LightTm, "eran_coach"))!.Remaining);
        }

        // ── Access ───────────────────────────────────────────────────────────

        [Theory]
        [InlineData(LightTm, LightTm, PlanManagementError.None)]        // self
        [InlineData(LightTm, Pa, PlanManagementError.None)]             // own Profile Admin
        [InlineData(LightTm, RridaAdmin, PlanManagementError.None)]     // RRIDA Admin
        [InlineData(LightTm, OtherPa, PlanManagementError.Forbidden)]   // other profile's PA
        [InlineData(LightTm, LegacyTm, PlanManagementError.Forbidden)]  // another manager
        [InlineData(999, RridaAdmin, PlanManagementError.NotFound)]
        public async Task Who_can_read_whose_entitlements(int target, int actor, PlanManagementError expected)
        {
            using var db = CreateContext(_db);
            var r = await Service(db).GetForUserAsync(target, actor);
            Assert.Equal(expected, r.Error);
        }
    }
}
