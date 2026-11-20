using System.Globalization;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Infrastructure.Models.PlanManagement;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // The customer side of the plan catalogue (api/plans): the active plans as the comparison
    // table, and the price of N managers on a plan. Any logged-in user; no internal notes, no
    // drafts. Checkout will price through PickTier / QuoteAsync so the price shown is the price
    // charged.
    //
    // Pricing: the quantity being bought picks ONE tier and every manager is charged that tier's
    // price (3 Pro managers = 3 × $695; 6 = 6 × $595).
    public class PlanCatalogService : IPlanCatalogService
    {
        public const int MaxQuantity = 10_000;

        private readonly ApplicationDbContext _context;

        public PlanCatalogService(ApplicationDbContext context)
        {
            _context = context;
        }

        public Task<PlanComparisonDto> GetActivePlansAsync(CancellationToken ct = default) =>
            PlanManagementService.BuildComparisonAsync(_context, includeInactive: false, ct);

        public async Task<PlanManagementResult<PlanPriceQuoteDto>> QuoteAsync(int? planId, string? planCode, int? quantity, string? billingCycle, CancellationToken ct = default)
        {
            var code = string.IsNullOrWhiteSpace(planCode) ? null : planCode.Trim().ToLowerInvariant();
            if (planId is not > 0 && code == null)
                return Fail(PlanManagementError.BadRequest, ResponseMessages.Errors.PlanQuotePlanRequired);
            if (quantity is not (>= 1 and <= MaxQuantity))
                return Fail(PlanManagementError.BadRequest, ResponseMessages.Errors.PlanQuoteQuantityInvalid);

            var cycle = ParseCycle(billingCycle);
            if (cycle == null)
                return Fail(PlanManagementError.BadRequest, ResponseMessages.Errors.PlanQuoteCycleInvalid);

            var plan = await _context.ServicePlans.AsNoTracking()
                .Include(p => p.PriceTiers)
                .FirstOrDefaultAsync(p => p.IsActive && (planId > 0 ? p.Id == planId : p.Code == code), ct);
            if (plan == null)
                return Fail(PlanManagementError.NotFound, ResponseMessages.Errors.PlanNotAvailable);

            var tier = PickTier(plan.PriceTiers, quantity.Value);
            if (tier == null)
                return Fail(PlanManagementError.BadRequest,
                    string.Format(CultureInfo.InvariantCulture, ResponseMessages.Errors.PlanNoTierForQuantity, quantity.Value));

            var annual = cycle == BillingCycles.Annual;
            var unit = annual ? tier.AnnualPrice : tier.MonthlyPrice;
            var total = unit * quantity.Value;
            var savings = annual ? Math.Max(0, (tier.MonthlyPrice * 12 - tier.AnnualPrice) * quantity.Value) : 0m;
            var per = annual ? "year" : "month";
            var units = quantity.Value == 1 ? plan.PricingUnit : plan.PricingUnit + "s";

            return PlanManagementResult<PlanPriceQuoteDto>.Ok(new PlanPriceQuoteDto
            {
                PlanId = plan.Id,
                PlanCode = plan.Code,
                PlanName = plan.Name,
                Currency = plan.Currency,
                PricingUnit = plan.PricingUnit,
                BillingCycle = cycle,
                Quantity = quantity.Value,
                Tier = new PriceTierDto
                {
                    Id = tier.Id,
                    MinManagers = tier.MinManagers,
                    MaxManagers = tier.MaxManagers,
                    MonthlyPrice = tier.MonthlyPrice,
                    AnnualPrice = tier.AnnualPrice,
                    Label = PlanManagementService.TierLabel(tier.MinManagers, tier.MaxManagers, plan.PricingUnit)
                },
                UnitPrice = unit,
                TotalPrice = total,
                AnnualSavings = savings,
                Summary = $"{quantity.Value} {units} × {PlanManagementService.Money(unit, plan.Currency)} per {per} = "
                          + $"{PlanManagementService.Money(total, plan.Currency)} per {per}"
            });
        }

        /// <summary>The tier whose range holds the quantity; null when none does.</summary>
        public static ServicePlanPriceTier? PickTier(IEnumerable<ServicePlanPriceTier> tiers, int quantity) =>
            tiers.Where(t => t.MinManagers <= quantity && (t.MaxManagers == null || t.MaxManagers >= quantity))
                 .OrderBy(t => t.MinManagers)
                 .FirstOrDefault();

        /// <summary>"monthly" / "annual" (also "1" / "2", "month" / "year"); null otherwise.</summary>
        public static string? ParseCycle(string? value) =>
            value?.Trim().ToLowerInvariant() switch
            {
                "monthly" or "month" or "1" => BillingCycles.Monthly,
                "annual" or "annually" or "year" or "yearly" or "2" => BillingCycles.Annual,
                _ => null
            };

        private static PlanManagementResult<PlanPriceQuoteDto> Fail(PlanManagementError e, string m) =>
            PlanManagementResult<PlanPriceQuoteDto>.Fail(e, m);
    }

    public static class BillingCycles
    {
        public const string Monthly = "monthly";
        public const string Annual = "annual";
    }
}
