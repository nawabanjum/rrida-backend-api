using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Infrastructure.Models.PlanManagement;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Tests.TeamHealth;
using Xunit;
using static CloverBackoffice.Tests.PlanManagement.PlanManagementTestData;

namespace CloverBackoffice.Tests.PlanManagement
{
    /// <summary>
    /// api/admin/plans: RRIDA Admin creates and manages plans, price tiers and plan features.
    /// </summary>
    public class PlanManagementServiceTests
    {
        private readonly string _db = Guid.NewGuid().ToString();

        public PlanManagementServiceTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
        }

        private static PlanManagementService Service(ApplicationDbContext db) =>
            new(db, Roles(), new FixedClock(Now));

        private async Task<T> With<T>(Func<PlanManagementService, Task<T>> call)
        {
            using var db = CreateContext(_db);
            return await call(Service(db));
        }

        private async Task<ServicePlanDetailDto> CreateOk(CreateServicePlanDto dto)
        {
            var r = await With(s => s.CreatePlanAsync(dto, RridaAdmin));
            Assert.True(r.IsSuccess, r.Message);
            return r.Data!;
        }

        // ── Access ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Only_RRIDA_Admin_can_manage_or_read_plans()
        {
            var create = await With(s => s.CreatePlanAsync(Light(), ProfileAdmin));
            var list = await With(s => s.ListPlansAsync(null, ProfileAdmin));
            var features = await With(s => s.ListFeaturesAsync(null, ProfileAdmin));

            Assert.Equal(PlanManagementError.Forbidden, create.Error);
            Assert.Equal(PlanManagementError.Forbidden, list.Error);
            Assert.Equal(PlanManagementError.Forbidden, features.Error);
        }

        // ── Create ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Creates_Light_with_tier_and_features_like_the_pricing_table()
        {
            var p = await CreateOk(Light());

            Assert.Equal("light", p.Code);
            Assert.True(p.IsActive);
            Assert.Equal(1, p.Version);
            Assert.Equal("manager", p.PricingUnit);
            Assert.Equal("USD", p.Currency);
            Assert.Equal("Client pricing table 2026-11", p.InternalNotes);
            Assert.Equal("$97 per manager per month, or $970 a year", p.PriceSummary);
            Assert.Equal("1+ managers", Assert.Single(p.PriceTiers).Label);
            Assert.Equal(97m, p.StartingMonthlyPrice);

            var eran = p.Features.Single(f => f.Key == "ERAN_COACH");
            Assert.True(eran.IsIncluded);
            Assert.Equal(10, eran.LimitValue);
            Assert.False(eran.IsUnlimited);
            Assert.Equal("10 chats", eran.Display);
            Assert.Equal("per month", eran.SubText);
            Assert.False(p.Features.Single(f => f.Key == "PORTFOLIO_VIEW").IsIncluded);
            Assert.Equal(4, p.IncludedFeatureCount);

            using var db = CreateContext(_db);
            var audit = Assert.Single(db.ServicePlanAuditLogs.ToList());
            Assert.Equal(ServicePlanAuditActions.PlanCreated, audit.Action);
            Assert.Equal(RridaAdmin, audit.ActorUserId);
        }

        [Fact]
        public async Task Pro_tiers_get_labels_and_a_multi_tier_price_summary_and_unlimited_eran()
        {
            var p = await CreateOk(Pro());

            Assert.Equal(new[] { "1 to 4 managers", "5 to 9 managers", "10+ managers" }, p.PriceTiers.Select(t => t.Label));
            Assert.Equal("$695 per manager per month for 1 to 4 managers, or $6,950 a year; "
                + "$595 per manager per month for 5 to 9 managers, or $5,950 a year; "
                + "$495 per manager per month for 10+ managers, or $4,950 a year", p.PriceSummary);
            var eran = p.Features.Single(f => f.Key == "ERAN_COACH");
            Assert.True(eran.IsUnlimited);
            Assert.Equal("Unlimited", eran.Display);
            Assert.True(p.IsHighlighted);
            Assert.True(p.AllowsTeamLimitExtension);
        }

        [Fact]
        public async Task New_plan_is_an_inactive_draft_by_default_and_needs_tiers_to_be_active()
        {
            var draft = await CreateOk(new CreateServicePlanDto { Code = "team-plus", Name = "Team Plus" });
            Assert.False(draft.IsActive);
            Assert.Empty(draft.PriceTiers);
            Assert.Equal(5, draft.Features.Count); // every active feature row, none configured
            Assert.All(draft.Features, f => Assert.False(f.IsConfigured));

            var active = await With(s => s.CreatePlanAsync(new CreateServicePlanDto { Code = "x-plan", Name = "X", IsActive = true }, RridaAdmin));
            Assert.Equal(PlanManagementError.BadRequest, active.Error);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("P")]
        [InlineData("pro plan")]
        [InlineData("pro_plan")]
        [InlineData("-pro")]
        public async Task Invalid_code_is_rejected(string? code)
        {
            var dto = Light();
            dto.Code = code;
            var r = await With(s => s.CreatePlanAsync(dto, RridaAdmin));
            Assert.Equal(PlanManagementError.BadRequest, r.Error);
        }

        [Fact]
        public async Task Code_is_lowercased_and_code_or_name_already_used_is_409()
        {
            var dto = Light();
            dto.Code = "LIGHT";
            Assert.Equal("light", (await CreateOk(dto)).Code);

            var sameCode = Pro();
            sameCode.Code = "light";
            var sameName = Pro();
            sameName.Name = "light";

            Assert.Equal(PlanManagementError.Conflict, (await With(s => s.CreatePlanAsync(sameCode, RridaAdmin))).Error);
            Assert.Equal(PlanManagementError.Conflict, (await With(s => s.CreatePlanAsync(sameName, RridaAdmin))).Error);
        }

        [Fact]
        public async Task Bad_currency_and_too_long_name_are_rejected()
        {
            var currency = Light();
            currency.Currency = "dollars";
            var name = Light();
            name.Name = new string('a', 101);

            Assert.Equal(ResponseMessagesText.Currency, (await With(s => s.CreatePlanAsync(currency, RridaAdmin))).Message);
            Assert.Equal(PlanManagementError.BadRequest, (await With(s => s.CreatePlanAsync(name, RridaAdmin))).Error);
        }

        // ── Update ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Update_changes_only_fields_sent_bumps_version_and_empty_clears_notes()
        {
            var p = await CreateOk(Light());

            var r = await With(s => s.UpdatePlanAsync(p.Id, new UpdateServicePlanDto
            {
                Version = 1, Description = "For a single manager", InternalNotes = ""
            }, RridaAdmin));

            Assert.True(r.IsSuccess, r.Message);
            Assert.Equal(2, r.Data!.Version);
            Assert.Equal("For a single manager", r.Data.Description);
            Assert.Null(r.Data.InternalNotes);
            Assert.Equal("Light", r.Data.Name);
            Assert.Equal(RridaAdmin, r.Data.UpdatedBy);
        }

        [Fact]
        public async Task Stale_or_missing_version_is_rejected()
        {
            var p = await CreateOk(Light());

            var missing = await With(s => s.UpdatePlanAsync(p.Id, new UpdateServicePlanDto { Name = "Lite" }, RridaAdmin));
            var stale = await With(s => s.UpdatePlanAsync(p.Id, new UpdateServicePlanDto { Version = 7, Name = "Lite" }, RridaAdmin));
            var tiers = await With(s => s.ReplacePriceTiersAsync(p.Id, new ReplacePriceTiersDto { Version = 9, PriceTiers = LightTiers() }, RridaAdmin));

            Assert.Equal(PlanManagementError.BadRequest, missing.Error);
            Assert.Equal(PlanManagementError.Conflict, stale.Error);
            Assert.Equal(PlanManagementError.Conflict, tiers.Error);
        }

        [Fact]
        public async Task Unknown_plan_is_404()
        {
            Assert.Equal(PlanManagementError.NotFound, (await With(s => s.GetPlanAsync(999, RridaAdmin))).Error);
            Assert.Equal(PlanManagementError.NotFound,
                (await With(s => s.UpdatePlanAsync(999, new UpdateServicePlanDto { Version = 1 }, RridaAdmin))).Error);
        }

        // ── Price tiers ──────────────────────────────────────────────────────

        [Fact]
        public async Task Replace_tiers_swaps_the_whole_set_sorted_and_audits()
        {
            var p = await CreateOk(Light());
            var unsorted = ProTiers();
            unsorted.Reverse();

            var r = await With(s => s.ReplacePriceTiersAsync(p.Id, new ReplacePriceTiersDto { Version = 1, PriceTiers = unsorted }, RridaAdmin));

            Assert.True(r.IsSuccess, r.Message);
            Assert.Equal(new[] { 1, 5, 10 }, r.Data!.PriceTiers.Select(t => t.MinManagers));
            Assert.Equal(2, r.Data.Version);
            using var db = CreateContext(_db);
            Assert.Equal(3, db.ServicePlanPriceTiers.Count());
            Assert.Contains(db.ServicePlanAuditLogs, a => a.Action == ServicePlanAuditActions.PriceTiersReplaced);
        }

        public static IEnumerable<object[]> BadTierSets() => new[]
        {
            new object[] { new List<PriceTierInputDto>() },
            new object[] { new List<PriceTierInputDto> { new() { MinManagers = 2, MonthlyPrice = 1, AnnualPrice = 10 } } },
            new object[] { new List<PriceTierInputDto>
            {
                new() { MinManagers = 1, MaxManagers = 4, MonthlyPrice = 1, AnnualPrice = 10 },
                new() { MinManagers = 6, MonthlyPrice = 1, AnnualPrice = 10 }      // gap 5
            } },
            new object[] { new List<PriceTierInputDto>
            {
                new() { MinManagers = 1, MaxManagers = 5, MonthlyPrice = 1, AnnualPrice = 10 },
                new() { MinManagers = 5, MonthlyPrice = 1, AnnualPrice = 10 }      // overlap
            } },
            new object[] { new List<PriceTierInputDto>
            {
                new() { MinManagers = 1, MonthlyPrice = 1, AnnualPrice = 10 },     // open-ended, not last
                new() { MinManagers = 5, MonthlyPrice = 1, AnnualPrice = 10 }
            } },
            new object[] { new List<PriceTierInputDto> { new() { MinManagers = 1, MonthlyPrice = 9.999m, AnnualPrice = 10 } } },
            new object[] { new List<PriceTierInputDto> { new() { MinManagers = 1, MonthlyPrice = -1, AnnualPrice = 10 } } },
            new object[] { new List<PriceTierInputDto> { new() { MinManagers = 1, MonthlyPrice = 9 } } },
            new object[] { new List<PriceTierInputDto> { new() { MinManagers = 3, MaxManagers = 2, MonthlyPrice = 1, AnnualPrice = 1 } } }
        };

        [Theory]
        [MemberData(nameof(BadTierSets))]
        public void Invalid_tier_sets_are_rejected(List<PriceTierInputDto> tiers)
        {
            var (rows, error) = PlanManagementService.ValidateTiers(tiers);
            Assert.Null(rows);
            Assert.NotNull(error);
        }

        // ── Status ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Last_active_plan_cannot_be_deactivated_but_one_of_two_can()
        {
            var light = await CreateOk(Light());

            var last = await With(s => s.SetPlanStatusAsync(light.Id, new SetServicePlanStatusDto { IsActive = false }, RridaAdmin));
            Assert.Equal(PlanManagementError.BadRequest, last.Error);

            await CreateOk(Pro());
            var r = await With(s => s.SetPlanStatusAsync(light.Id, new SetServicePlanStatusDto { IsActive = false, Reason = "Retired" }, RridaAdmin));

            Assert.True(r.IsSuccess, r.Message);
            Assert.False(r.Data!.IsActive);
            using var db = CreateContext(_db);
            var audit = db.ServicePlanAuditLogs.Single(a => a.Action == ServicePlanAuditActions.PlanDeactivated);
            Assert.Equal("Retired", audit.Reason);
        }

        [Fact]
        public async Task Activate_needs_tiers_and_same_status_is_rejected()
        {
            var draft = await CreateOk(new CreateServicePlanDto { Code = "draft", Name = "Draft" });

            var noTiers = await With(s => s.SetPlanStatusAsync(draft.Id, new SetServicePlanStatusDto { IsActive = true }, RridaAdmin));
            var same = await With(s => s.SetPlanStatusAsync(draft.Id, new SetServicePlanStatusDto { IsActive = false }, RridaAdmin));

            Assert.Equal(PlanManagementError.BadRequest, noTiers.Error);
            Assert.Equal(PlanManagementError.BadRequest, same.Error);

            await With(s => s.ReplacePriceTiersAsync(draft.Id, new ReplacePriceTiersDto { Version = 1, PriceTiers = LightTiers() }, RridaAdmin));
            var ok = await With(s => s.SetPlanStatusAsync(draft.Id, new SetServicePlanStatusDto { IsActive = true }, RridaAdmin));
            Assert.True(ok.IsSuccess, ok.Message);
        }

        // ── Duplicate / reorder / list / history ─────────────────────────────

        [Fact]
        public async Task Duplicate_copies_tiers_and_features_as_inactive_draft()
        {
            var pro = await CreateOk(Pro());

            var r = await With(s => s.DuplicatePlanAsync(pro.Id, new DuplicateServicePlanDto { Code = "pro-2027", Name = "Pro 2027" }, RridaAdmin));

            Assert.True(r.IsSuccess, r.Message);
            Assert.False(r.Data!.IsActive);
            Assert.False(r.Data.IsHighlighted);
            Assert.Equal(3, r.Data.PriceTiers.Count);
            Assert.Equal(pro.IncludedFeatureCount, r.Data.IncludedFeatureCount);
            Assert.Equal("Up to 3", r.Data.Features.Single(f => f.Key == "MANAGERS_AND_TEAMS").Display);
        }

        [Fact]
        public async Task Reorder_needs_every_plan_once_and_list_follows_the_order()
        {
            var light = await CreateOk(Light());
            var pro = await CreateOk(Pro());

            var partial = await With(s => s.ReorderPlansAsync(new ReorderDto { Ids = new() { pro.Id } }, RridaAdmin));
            Assert.Equal(PlanManagementError.BadRequest, partial.Error);

            var r = await With(s => s.ReorderPlansAsync(new ReorderDto { Ids = new() { pro.Id, light.Id } }, RridaAdmin));
            Assert.True(r.IsSuccess, r.Message);
            Assert.Equal(new[] { "pro", "light" }, r.Data!.Plans.Select(p => p.Code));

            var active = await With(s => s.ListPlansAsync(new ServicePlanListQueryDto { Status = "inactive" }, RridaAdmin));
            Assert.Equal(0, active.Data!.Total);
        }

        [Fact]
        public async Task History_lists_changes_newest_first_with_actor_name()
        {
            var p = await CreateOk(Light());
            await With(s => s.UpdatePlanAsync(p.Id, new UpdateServicePlanDto { Version = 1, Description = "d" }, RridaAdmin));

            var r = await With(s => s.GetPlanHistoryAsync(p.Id, null, RridaAdmin));

            Assert.True(r.IsSuccess, r.Message);
            Assert.Equal(2, r.Data!.Total);
            Assert.Equal(ServicePlanAuditActions.PlanUpdated, r.Data.Entries[0].Action);
            Assert.Equal("RRIDA Admin", r.Data.Entries[0].ActorName);
            Assert.Contains("\"Description\":\"d\"", r.Data.Entries[0].After);
        }

        private static class ResponseMessagesText
        {
            public const string Currency = CloverBackoffice.Components.Helpers.ResponseMessages.Errors.PlanCurrencyInvalid;
        }
    }
}
