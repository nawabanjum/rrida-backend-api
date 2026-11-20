using CloverBackoffice.Components.Helpers;
using System.Globalization;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    // Plan catalogue (ServicePlans) on the purchase path. EVERY purchase is priced by a plan:
    //
    //   * checkout (first or additional), purchase-additional-teams: the plan sent, else Pro (the
    //     plan every pre-plan licence reads as), at its NEWEST version - new purchases get the
    //     current terms; the quantity picks the tier;
    //   * renewal link and the monthly/annual change of a licence: the plan and version the
    //     subscription / licence was bought on (keepTerms), so existing customers keep their
    //     terms and price until an admin migrates them. A pre-plan subscription keeps its flat
    //     price (the first tier).
    //
    // Stripe charges the price inline; plan id, version id and unit price ride on the session
    // and the Stripe subscription ("servicePlanId", "servicePlanVersionId", "servicePlanUnitPrice").
    // The webhook stamps them on the new CompanySubscription and every licence minted or
    // converted for it. Only without any plan catalogue does the old flat price apply.
    public partial class SubscriptionService
    {
        public const string ServicePlanIdMetadataKey = "servicePlanId";
        public const string ServicePlanCodeMetadataKey = "servicePlanCode";
        public const string ServicePlanUnitPriceMetadataKey = "servicePlanUnitPrice";
        public const string ServicePlanVersionIdMetadataKey = "servicePlanVersionId";

        /// <summary>
        /// Prices a purchase by plan (see above). Null only when there is no plan catalogue
        /// (the legacy flat price applies). Throws InvalidOperationException when the plan can't
        /// be bought or has no tier for the quantity.
        /// </summary>
        /// <param name="keepTerms">Price by <paramref name="versionId"/> (null = version 1) and allow an
        /// inactive plan: renewals and cycle changes of what was already bought.</param>
        /// <param name="tierQuantity">Quantity that picks the tier, when not the quantity bought
        /// (a pre-plan subscription: 1, its flat price).</param>
        private async Task<(StripePlanPrice Price, Dictionary<string, string> Metadata)?> QuotePlanCheckoutAsync(
            int? servicePlanId, string? servicePlanCode, int quantity, BillingCycle cycle,
            int? versionId = null, bool keepTerms = false, int? tierQuantity = null)
        {
            var code = string.IsNullOrWhiteSpace(servicePlanCode) ? null : servicePlanCode.Trim().ToLowerInvariant();
            if (servicePlanId is not > 0 && code == null)
            {
                // No plan asked for: Pro, the default plan. No catalogue at all: legacy price.
                if (!await _db.ServicePlans.AsNoTracking().AnyAsync(p => p.Code == LicenseTeamLimits.LegacyPlanCode))
                    return null;
                code = LicenseTeamLimits.LegacyPlanCode;
            }
            if (quantity < 1)
                throw new InvalidOperationException(ResponseMessages.Errors.PlanQuoteQuantityInvalid);

            var plan = await _db.ServicePlans.AsNoTracking()
                .FirstOrDefaultAsync(p => (keepTerms || p.IsActive)
                                          && (servicePlanId > 0 ? p.Id == servicePlanId : p.Code == code));
            if (plan == null)
                throw new InvalidOperationException(ResponseMessages.Errors.PlanNotAvailable);

            int? stampVersionId;
            PlanTerms terms;
            if (keepTerms)
            {
                var version = await _db.ServicePlanVersions.AsNoTracking()
                    .Where(v => v.ServicePlanId == plan.Id && (versionId != null ? v.Id == versionId : v.VersionNumber == 1))
                    .FirstOrDefaultAsync();
                stampVersionId = version?.Id;
                terms = version != null ? PlanTerms.FromJson(version.Terms) : await _db.LiveTermsAsync(plan.Id) ?? new PlanTerms();
            }
            else
            {
                var current = await _db.CurrentVersionAsync(plan.Id);
                stampVersionId = current?.Id;
                terms = current != null ? PlanTerms.FromJson(current.Terms) : await _db.LiveTermsAsync(plan.Id) ?? new PlanTerms();
            }

            var tier = terms.TierFor(tierQuantity ?? quantity)
                ?? throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                    ResponseMessages.Errors.PlanNoTierForQuantity, tierQuantity ?? quantity));
            var unit = cycle == BillingCycle.Annual ? tier.AnnualPrice : tier.MonthlyPrice;

            var price = new StripePlanPrice(unit, terms.Currency, $"RRIDA {plan.Name} - per {terms.PricingUnit}");
            var metadata = new Dictionary<string, string>
            {
                [ServicePlanIdMetadataKey] = plan.Id.ToString(CultureInfo.InvariantCulture),
                [ServicePlanCodeMetadataKey] = plan.Code,
                [ServicePlanUnitPriceMetadataKey] = unit.ToString(CultureInfo.InvariantCulture)
            };
            if (stampVersionId != null)
                metadata[ServicePlanVersionIdMetadataKey] = stampVersionId.Value.ToString(CultureInfo.InvariantCulture);

            _logger.LogInformation(
                "[PlanCheckout] Plan {Code} ({Id}) version {Version} x{Qty} {Cycle}: {Unit} {Cur} each (keepTerms={Keep}).",
                plan.Code, plan.Id, stampVersionId, quantity, cycle, unit, terms.Currency, keepTerms);
            return (price, metadata);
        }

        /// <summary>The "servicePlanVersionId" a plan checkout carries, only when that version
        /// exists here and belongs to the plan; null otherwise.</summary>
        private async Task<int?> ExistingServicePlanVersionIdAsync(IDictionary<string, string>? metadata, int? servicePlanId)
        {
            if (servicePlanId == null || metadata == null
                || !metadata.TryGetValue(ServicePlanVersionIdMetadataKey, out var raw)
                || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                return null;
            return await _db.ServicePlanVersions.AsNoTracking().AnyAsync(v => v.Id == id && v.ServicePlanId == servicePlanId)
                ? id
                : null;
        }

        /// <summary>
        /// subscription/detail licence rows: the plan each licence gives and its price per
        /// manager (LicencePlanQueries). Rows without a catalogue plan are left as they are.
        /// </summary>
        private async Task StampLicencePlansAsync(IReadOnlyCollection<SubscriptionLicenseDto> rows)
        {
            var info = await _db.GetLicencePlanInfoAsync(rows.Where(r => r.LicenseId > 0).Select(r => r.LicenseId));
            foreach (var row in rows)
            {
                if (!info.TryGetValue(row.LicenseId, out var p)) continue;
                row.ServicePlanId = p.PlanId;
                row.ServicePlanCode = p.PlanCode;
                row.ServicePlanName = p.PlanName;
                row.IsLegacyPlan = p.IsLegacyPlan;
                row.PricePerManager = p.PricePerManager;
            }
        }

        /// <summary>
        /// The company pool: licence counts per plan, and what the live paid licences cost per
        /// billing cycle (each licence once; complimentary rows are coverage, not seats).
        /// </summary>
        private async Task StampPoolPlansAsync(CompanyLicensePoolDto? pool, uint profileId)
        {
            if (pool == null) return;
            pool.Plans = await _db.GetPlanLicenseCountsAsync(profileId);

            var paid = pool.Licenses
                .Where(l => !l.IsComplimentary && !l.IsExpired && l.PricePerManager != null
                            && !string.Equals(l.LicenseType, "Trial", StringComparison.OrdinalIgnoreCase))
                .GroupBy(l => l.LicenseId)
                .Select(g => g.First())
                .ToList();
            bool IsAnnual(SubscriptionLicenseDto l) => l.BillingCycle.StartsWith("Annual", StringComparison.OrdinalIgnoreCase)
                                                       || l.BillingCycle.StartsWith("Year", StringComparison.OrdinalIgnoreCase);
            pool.TotalAnnualAmount = paid.Where(IsAnnual).Sum(l => l.PricePerManager!.Value);
            pool.TotalMonthlyAmount = paid.Where(l => !IsAnnual(l)).Sum(l => l.PricePerManager!.Value);
        }

        /// <summary>The "servicePlanUnitPrice" (per manager, for the cycle) a plan checkout carries; null otherwise.</summary>
        public static decimal? ParseServicePlanUnitPriceMetadata(IDictionary<string, string>? metadata) =>
            metadata != null
            && metadata.TryGetValue(ServicePlanUnitPriceMetadataKey, out var raw)
            && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var price)
            && price >= 0
                ? price
                : null;

        /// <summary>
        /// The plan id only when that plan exists in this database: metadata from a session of
        /// another environment (or a deleted plan) must never fail the licence insert on the
        /// foreign key, so it is dropped and the purchase is provisioned as a legacy one.
        /// </summary>
        private async Task<int?> ExistingServicePlanIdAsync(int? servicePlanId)
        {
            if (servicePlanId == null) return null;
            if (await _db.ServicePlans.AsNoTracking().AnyAsync(p => p.Id == servicePlanId)) return servicePlanId;
            _logger.LogWarning("[PlanCheckout] Metadata names plan {PlanId}, which does not exist here; ignored.", servicePlanId);
            return null;
        }

        /// <summary>The "servicePlanId" a plan checkout / its Stripe subscription carries; null otherwise.</summary>
        public static int? ParseServicePlanIdMetadata(IDictionary<string, string>? metadata) =>
            metadata != null
            && metadata.TryGetValue(ServicePlanIdMetadataKey, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            && id > 0
                ? id
                : null;
    }
}
