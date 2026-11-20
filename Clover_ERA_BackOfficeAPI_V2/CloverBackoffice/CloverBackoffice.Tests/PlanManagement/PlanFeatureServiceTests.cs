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
    /// api/admin/plan-features (catalogue), PUT api/admin/plans/{id}/features (cells) and the
    /// comparison table.
    /// </summary>
    public class PlanFeatureServiceTests
    {
        private readonly string _db = Guid.NewGuid().ToString();

        public PlanFeatureServiceTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
        }

        private async Task<T> With<T>(Func<PlanManagementService, Task<T>> call)
        {
            using var db = CreateContext(_db);
            return await call(new PlanManagementService(db, Roles(), new FixedClock(Now)));
        }

        private async Task<ServicePlanDetailDto> CreateOk(CreateServicePlanDto dto)
        {
            var r = await With(s => s.CreatePlanAsync(dto, RridaAdmin));
            Assert.True(r.IsSuccess, r.Message);
            return r.Data!;
        }

        private int FeatureId(string key)
        {
            using var db = CreateContext(_db);
            return db.PlanFeatures.Single(f => f.Key == key).Id;
        }

        private Task<PlanManagementResult<ServicePlanDetailDto>> SetCells(int planId, int version, params PlanFeatureValueInputDto[] cells) =>
            With(s => s.SetPlanFeaturesAsync(planId, new SetPlanFeaturesDto { Version = version, Features = cells.ToList() }, RridaAdmin));

        // ── Catalogue ────────────────────────────────────────────────────────

        [Fact]
        public async Task Creates_a_limit_feature_uppercasing_key_with_default_period_and_next_sort()
        {
            var r = await With(s => s.CreateFeatureAsync(new CreatePlanFeatureDto
            {
                Key = "eran_voice", Title = "Eran voice sessions", ValueType = "limit", LimitUnit = "sessions"
            }, RridaAdmin));

            Assert.True(r.IsSuccess, r.Message);
            Assert.Equal("ERAN_VOICE", r.Data!.Key);
            Assert.Equal(PlanFeatureValueTypes.Limit, r.Data.ValueType);
            Assert.Equal(PlanFeatureLimitPeriods.None, r.Data.LimitPeriod);
            Assert.Equal(6, r.Data.SortOrder);
            Assert.False(r.Data.IsSystem);
            Assert.True(r.Data.IsActive);
        }

        [Theory]
        [InlineData("PORTFOLIO_VIEW", "INCLUDED", null, PlanManagementError.Conflict)]   // key taken
        [InlineData("1BAD", "INCLUDED", null, PlanManagementError.BadRequest)]           // key format
        [InlineData("NEW_ONE", "BOOLEAN", null, PlanManagementError.BadRequest)]         // type
        [InlineData("NEW_ONE", "INCLUDED", "MONTH", PlanManagementError.BadRequest)]     // period on non-limit
        [InlineData("NEW_ONE", "LIMIT", "WEEK", PlanManagementError.BadRequest)]         // period value
        public async Task Invalid_feature_is_rejected(string key, string type, string? period, PlanManagementError expected)
        {
            var r = await With(s => s.CreateFeatureAsync(new CreatePlanFeatureDto
            {
                Key = key, Title = "T", ValueType = type, LimitPeriod = period
            }, RridaAdmin));
            Assert.Equal(expected, r.Error);
        }

        [Fact]
        public async Task System_feature_keeps_its_type_and_cannot_be_deactivated_but_text_is_editable()
        {
            var id = FeatureId("PORTFOLIO_VIEW");

            var type = await With(s => s.UpdateFeatureAsync(id, new UpdatePlanFeatureDto { ValueType = "TEXT" }, RridaAdmin));
            var off = await With(s => s.SetFeatureStatusAsync(id, new SetPlanFeatureStatusDto { IsActive = false }, RridaAdmin));
            var text = await With(s => s.UpdateFeatureAsync(id, new UpdatePlanFeatureDto { Title = "Portfolio View", Description = "All your teams" }, RridaAdmin));

            Assert.Equal(PlanManagementError.BadRequest, type.Error);
            Assert.Equal(PlanManagementError.BadRequest, off.Error);
            Assert.True(text.IsSuccess, text.Message);
            Assert.Equal("Portfolio View", text.Data!.Title);
            Assert.Equal("PORTFOLIO_VIEW", text.Data.Key);
        }

        [Fact]
        public async Task Custom_feature_type_changes_only_while_no_plan_uses_it_and_can_be_deactivated()
        {
            var f = (await With(s => s.CreateFeatureAsync(new CreatePlanFeatureDto { Key = "API_ACCESS", Title = "API access", ValueType = "INCLUDED" }, RridaAdmin))).Data!;

            var toLimit = await With(s => s.UpdateFeatureAsync(f.Id, new UpdatePlanFeatureDto { ValueType = "LIMIT", LimitPeriod = "MONTH" }, RridaAdmin));
            Assert.True(toLimit.IsSuccess, toLimit.Message);
            Assert.Equal(PlanFeatureLimitPeriods.Month, toLimit.Data!.LimitPeriod);

            var plan = await CreateOk(Light());
            await SetCells(plan.Id, 1, new PlanFeatureValueInputDto { FeatureId = f.Id, IsIncluded = true, LimitValue = 100 });

            var back = await With(s => s.UpdateFeatureAsync(f.Id, new UpdatePlanFeatureDto { ValueType = "INCLUDED" }, RridaAdmin));
            Assert.Equal(PlanManagementError.BadRequest, back.Error);

            var off = await With(s => s.SetFeatureStatusAsync(f.Id, new SetPlanFeatureStatusDto { IsActive = false, Reason = "Dropped" }, RridaAdmin));
            Assert.True(off.IsSuccess, off.Message);
            Assert.Equal(1, off.Data!.IncludedInPlans);
        }

        [Fact]
        public async Task Reorder_features_needs_every_id()
        {
            using var db = CreateContext(_db);
            var ids = db.PlanFeatures.OrderByDescending(f => f.SortOrder).Select(f => f.Id).ToList();

            var bad = await With(s => s.ReorderFeaturesAsync(new ReorderDto { Ids = ids.Take(2).ToList() }, RridaAdmin));
            var ok = await With(s => s.ReorderFeaturesAsync(new ReorderDto { Ids = ids }, RridaAdmin));

            Assert.Equal(PlanManagementError.BadRequest, bad.Error);
            Assert.True(ok.IsSuccess, ok.Message);
            Assert.Equal(ids, ok.Data!.Features.Select(f => f.Id));
        }

        // ── Plan cells ───────────────────────────────────────────────────────

        [Fact]
        public async Task Setting_cells_upserts_only_the_ones_sent()
        {
            var p = await CreateOk(Light());

            var r = await SetCells(p.Id, 1,
                new PlanFeatureValueInputDto { FeatureKey = "portfolio_view", IsIncluded = true },
                new PlanFeatureValueInputDto { FeatureKey = "ERAN_COACH", IsIncluded = true, LimitValue = null, DisplayText = "" });

            Assert.True(r.IsSuccess, r.Message);
            Assert.Equal(2, r.Data!.Version);
            Assert.True(r.Data.Features.Single(f => f.Key == "PORTFOLIO_VIEW").IsIncluded);
            Assert.Equal("Unlimited", r.Data.Features.Single(f => f.Key == "ERAN_COACH").Display);
            Assert.Equal("1", r.Data.Features.Single(f => f.Key == "MANAGERS_AND_TEAMS").Display); // untouched
        }

        [Fact]
        public async Task Not_included_limit_cell_stores_no_limit()
        {
            var p = await CreateOk(Light());

            var r = await SetCells(p.Id, 1, new PlanFeatureValueInputDto { FeatureKey = "ERAN_COACH", IsIncluded = false, LimitValue = 5 });

            var eran = r.Data!.Features.Single(f => f.Key == "ERAN_COACH");
            Assert.False(eran.IsIncluded);
            Assert.Null(eran.LimitValue);
            Assert.False(eran.IsUnlimited);
        }

        public static IEnumerable<object[]> BadCells() => new[]
        {
            new object[] { new PlanFeatureValueInputDto { FeatureKey = "DAILY_CHECKINS", IsIncluded = true, LimitValue = 3 } },     // limit on INCLUDED
            new object[] { new PlanFeatureValueInputDto { FeatureKey = "WEEKLY_SUGGESTED_ACTION", IsIncluded = true } },             // TEXT needs text
            new object[] { new PlanFeatureValueInputDto { FeatureKey = "ERAN_COACH", IsIncluded = true, LimitValue = -1 } },         // negative
            new object[] { new PlanFeatureValueInputDto { FeatureKey = "NOPE", IsIncluded = true } },                                // unknown
            new object[] { new PlanFeatureValueInputDto { FeatureKey = "DAILY_CHECKINS" } },                                         // isIncluded missing
            new object[] { new PlanFeatureValueInputDto { IsIncluded = true } }                                                      // no ref
        };

        [Theory]
        [MemberData(nameof(BadCells))]
        public async Task Invalid_cell_is_rejected(PlanFeatureValueInputDto cell)
        {
            var p = await CreateOk(Light());
            var r = await SetCells(p.Id, 1, cell);
            Assert.Equal(PlanManagementError.BadRequest, r.Error);
        }

        [Fact]
        public async Task Same_feature_twice_or_inactive_feature_included_is_rejected()
        {
            var p = await CreateOk(Light());
            var custom = (await With(s => s.CreateFeatureAsync(new CreatePlanFeatureDto { Key = "SSO", Title = "SSO", ValueType = "INCLUDED" }, RridaAdmin))).Data!;
            await With(s => s.SetFeatureStatusAsync(custom.Id, new SetPlanFeatureStatusDto { IsActive = false }, RridaAdmin));

            var twice = await SetCells(p.Id, 1,
                new PlanFeatureValueInputDto { FeatureKey = "DAILY_CHECKINS", IsIncluded = true },
                new PlanFeatureValueInputDto { FeatureId = FeatureId("DAILY_CHECKINS"), IsIncluded = false });
            var inactive = await SetCells(p.Id, 1, new PlanFeatureValueInputDto { FeatureId = custom.Id, IsIncluded = true });
            var inactiveOff = await SetCells(p.Id, 1, new PlanFeatureValueInputDto { FeatureId = custom.Id, IsIncluded = false });

            Assert.Equal(PlanManagementError.BadRequest, twice.Error);
            Assert.Equal(PlanManagementError.BadRequest, inactive.Error);
            Assert.True(inactiveOff.IsSuccess, inactiveOff.Message);
        }

        // ── Comparison ───────────────────────────────────────────────────────

        [Fact]
        public async Task Comparison_matches_the_pricing_table_layout()
        {
            var light = await CreateOk(Light());
            var pro = await CreateOk(Pro());
            await CreateOk(new CreateServicePlanDto { Code = "draft", Name = "Draft" });   // inactive: hidden

            var r = await With(s => s.GetComparisonAsync(false, RridaAdmin));

            Assert.True(r.IsSuccess, r.Message);
            var t = r.Data!;
            Assert.Equal(new[] { light.Id, pro.Id }, t.Plans.Select(p => p.Id));
            Assert.True(t.Plans[1].IsHighlighted);
            Assert.Equal(5, t.Rows.Count);
            Assert.All(t.Rows, row => Assert.Equal(2, row.Cells.Count));

            var teams = t.Rows.Single(x => x.Key == "MANAGERS_AND_TEAMS");
            Assert.Equal(new[] { "1", "Up to 3" }, teams.Cells.Select(c => c.Display));
            var portfolio = t.Rows.Single(x => x.Key == "PORTFOLIO_VIEW");
            Assert.Equal(new[] { false, true }, portfolio.Cells.Select(c => c.IsIncluded));
            Assert.Equal("*with Clover Resources", t.Rows.Single(x => x.Key == "WEEKLY_SUGGESTED_ACTION").Cells[1].SubText);
            Assert.Equal("* Clover Framework includes over 2000 micro-actions.", Assert.Single(t.Footnotes));

            var all = await With(s => s.GetComparisonAsync(true, RridaAdmin));
            Assert.Equal(3, all.Data!.Plans.Count);
        }

        [Fact]
        public void Money_formats_whole_and_decimal_amounts()
        {
            Assert.Equal("$6,950", PlanManagementService.Money(6950m, "USD"));
            Assert.Equal("£9.50", PlanManagementService.Money(9.5m, "GBP"));
            Assert.Equal("AUD 10", PlanManagementService.Money(10m, "AUD"));
        }
    }
}
