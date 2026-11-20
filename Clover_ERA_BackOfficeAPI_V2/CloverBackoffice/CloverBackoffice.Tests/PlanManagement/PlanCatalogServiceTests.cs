using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Infrastructure.Models.PlanManagement;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Tests.TeamHealth;
using Xunit;
using static CloverBackoffice.Tests.PlanManagement.PlanManagementTestData;

namespace CloverBackoffice.Tests.PlanManagement
{
    /// <summary>api/plans and api/plans/price: active plans and price quotes.</summary>
    public class PlanCatalogServiceTests
    {
        private readonly string _db = Guid.NewGuid().ToString();
        private readonly int _lightId;
        private readonly int _proId;

        public PlanCatalogServiceTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
            var admin = new PlanManagementService(db, Roles(), new FixedClock(Now));
            _lightId = admin.CreatePlanAsync(Light(), RridaAdmin).Result.Data!.Id;
            _proId = admin.CreatePlanAsync(Pro(), RridaAdmin).Result.Data!.Id;
            admin.CreatePlanAsync(new CreateServicePlanDto { Code = "draft", Name = "Draft", PriceTiers = LightTiers() }, RridaAdmin).Wait();
        }

        private async Task<PlanManagementResult<PlanPriceQuoteDto>> Quote(int? id, string? code, int? qty, string? cycle)
        {
            using var db = CreateContext(_db);
            return await new PlanCatalogService(db).QuoteAsync(id, code, qty, cycle);
        }

        [Fact]
        public async Task Active_plans_only_no_drafts()
        {
            using var db = CreateContext(_db);
            var t = await new PlanCatalogService(db).GetActivePlansAsync();

            Assert.Equal(new[] { "light", "pro" }, t.Plans.Select(p => p.Code));
            Assert.Equal(5, t.Rows.Count);
        }

        [Theory]
        [InlineData(1, "monthly", 695, 695)]
        [InlineData(3, "monthly", 695, 2085)]
        [InlineData(4, "monthly", 695, 2780)]
        [InlineData(5, "monthly", 595, 2975)]
        [InlineData(9, "monthly", 595, 5355)]
        [InlineData(10, "monthly", 495, 4950)]
        [InlineData(25, "monthly", 495, 12375)]
        [InlineData(1, "annual", 6950, 6950)]
        [InlineData(6, "annual", 5950, 35700)]
        public async Task Quantity_picks_one_tier_and_every_manager_pays_it(int qty, string cycle, decimal unit, decimal total)
        {
            var r = await Quote(null, "PRO", qty, cycle);

            Assert.True(r.IsSuccess, r.Message);
            Assert.Equal(unit, r.Data!.UnitPrice);
            Assert.Equal(total, r.Data.TotalPrice);
            Assert.Equal(cycle, r.Data.BillingCycle);
            Assert.Equal(_proId, r.Data.PlanId);
        }

        [Fact]
        public async Task Annual_quote_shows_savings_and_summary()
        {
            var r = await Quote(null, "pro", 6, "annual");

            Assert.Equal((595m * 12 - 5950m) * 6, r.Data!.AnnualSavings);
            Assert.Equal("5 to 9 managers", r.Data.Tier.Label);
            Assert.Equal("6 managers × $5,950 per year = $35,700 per year", r.Data.Summary);

            var light = await Quote(_lightId, null, 1, "monthly");
            Assert.Equal("1 manager × $97 per month = $97 per month", light.Data!.Summary);
            Assert.Equal(0m, light.Data.AnnualSavings);
        }

        [Theory]
        [InlineData(null, null, 1, "monthly", PlanManagementError.BadRequest)]     // no plan
        [InlineData(null, "pro", 0, "monthly", PlanManagementError.BadRequest)]    // quantity
        [InlineData(null, "pro", null, "monthly", PlanManagementError.BadRequest)] // quantity missing
        [InlineData(null, "pro", 1, "weekly", PlanManagementError.BadRequest)]     // cycle
        [InlineData(null, "draft", 1, "monthly", PlanManagementError.NotFound)]    // inactive
        [InlineData(null, "gold", 1, "monthly", PlanManagementError.NotFound)]     // unknown
        public async Task Invalid_quote_is_rejected(int? id, string? code, int? qty, string? cycle, PlanManagementError expected)
        {
            Assert.Equal(expected, (await Quote(id, code, qty, cycle)).Error);
        }

        [Fact]
        public async Task Quantity_above_a_capped_top_tier_has_no_price()
        {
            using (var db = CreateContext(_db))
            {
                var admin = new PlanManagementService(db, Roles(), new FixedClock(Now));
                await admin.CreatePlanAsync(new CreateServicePlanDto
                {
                    Code = "capped", Name = "Capped", IsActive = true,
                    PriceTiers = new() { new() { MinManagers = 1, MaxManagers = 20, MonthlyPrice = 10, AnnualPrice = 100 } }
                }, RridaAdmin);
            }

            var r = await Quote(null, "capped", 21, "monthly");
            Assert.Equal(PlanManagementError.BadRequest, r.Error);
            Assert.Contains("21", r.Message);
        }

        [Fact]
        public void Pick_tier_and_parse_cycle()
        {
            var tiers = new[]
            {
                new ServicePlanPriceTier { MinManagers = 1, MaxManagers = 4 },
                new ServicePlanPriceTier { MinManagers = 5, MaxManagers = null }
            };
            Assert.Equal(1, PlanCatalogService.PickTier(tiers, 4)!.MinManagers);
            Assert.Equal(5, PlanCatalogService.PickTier(tiers, 500)!.MinManagers);
            Assert.Null(PlanCatalogService.PickTier(tiers, 0));

            Assert.Equal("annual", PlanCatalogService.ParseCycle("Yearly"));
            Assert.Equal("monthly", PlanCatalogService.ParseCycle("1"));
            Assert.Null(PlanCatalogService.ParseCycle(""));
        }
    }
}
