using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    /// <summary>A licence's plan catalogue plan and the price per manager it was bought at.</summary>
    public sealed record LicencePlanInfo(int PlanId, string PlanCode, string PlanName, bool IsLegacyPlan, decimal? PricePerManager, string Currency)
    {
        /// <summary>The plan version (terms) the licence was bought on.</summary>
        public int VersionNumber { get; init; }
    }

    // Plan catalogue reads for the licence and subscription APIs: which plan each licence gives
    // (its ServicePlanId, else the legacy plan Pro) and its price, and licence counts per plan.
    // Used by subscription/detail, license-assignments and availability so they agree.
    public static class LicencePlanQueries
    {
        private const string TrialLicenseType = "Trial";

        /// <summary>
        /// Plan and price per licence id. Price: what the plan checkout charged (the
        /// subscription's ServicePlanUnitPrice), else today's tier price for the subscription's
        /// quantity and cycle (a pre-plan subscription: the first tier, its flat price); null for
        /// a trial licence. Empty without a plan catalogue.
        /// </summary>
        public static async Task<Dictionary<int, LicencePlanInfo>> GetLicencePlanInfoAsync(
            this ApplicationDbContext context, IEnumerable<int> licenseIds, CancellationToken ct = default)
        {
            var ids = licenseIds.Distinct().ToList();
            var result = new Dictionary<int, LicencePlanInfo>();
            if (ids.Count == 0) return result;

            // The terms each licence was bought on (its plan version); empty without a catalogue.
            var terms = await context.GetLicenceTermsAsync(ids, ct);
            if (terms.Count == 0) return result;

            var rows = await context.CompanyLicenses.AsNoTracking()
                .Where(l => ids.Contains(l.Id))
                .Select(l => new
                {
                    l.Id,
                    l.LicenseType,
                    UnitPrice = l.CompanySubscription != null ? l.CompanySubscription.ServicePlanUnitPrice : null,
                    SubPlanId = l.CompanySubscription != null ? l.CompanySubscription.ServicePlanId : null,
                    Quantity = l.CompanySubscription != null ? (int?)l.CompanySubscription.TeamCount : null,
                    Cycle = l.CompanySubscription != null ? (BillingCycle?)l.CompanySubscription.BillingCycle : null
                })
                .ToListAsync(ct);

            foreach (var r in rows)
            {
                if (!terms.TryGetValue(r.Id, out var t)) continue;

                decimal? price = null;
                if (r.LicenseType != TrialLicenseType)
                {
                    price = r.UnitPrice;
                    if (price == null && r.Cycle != null)
                    {
                        // A plan purchase is priced by its quantity's tier. A subscription from
                        // before plans was charged one flat price whatever the quantity (the
                        // first tier: Pro $695 / $6,950), so it reads as quantity 1. Tiers come
                        // from the licence's own version, so later price edits don't show here.
                        var qty = r.SubPlanId != null ? Math.Max(1, r.Quantity ?? 1) : 1;
                        var tier = t.Terms.TierFor(qty);
                        price = tier == null ? null : r.Cycle == BillingCycle.Annual ? tier.AnnualPrice : tier.MonthlyPrice;
                    }
                }

                result[r.Id] = new LicencePlanInfo(t.PlanId, t.PlanCode, t.PlanName, t.IsLegacyPlan, price, t.Terms.Currency)
                {
                    VersionNumber = t.VersionNumber
                };
            }
            return result;
        }

        public enum AutoPickOutcome { Picked, NoneFree, MixedPlans }

        /// <summary>
        /// Which free licence to give without asking (never at random between plans):
        ///   * planCode sent (admin's choice) → the first free licence of that plan;
        ///   * else the free licences are all one plan → the first of them;
        ///   * else (Light and Pro both free) → the company default plan (Pro) when
        ///     useDefaultPlan, otherwise none (MixedPlans: the Profile Admin chooses).
        /// "Free" is FindAvailableLicenseIdAsync's rule. Without a plan catalogue: first free.
        /// </summary>
        public static async Task<(int? LicenseId, AutoPickOutcome Outcome)> PickFreeLicenseAsync(
            this ApplicationDbContext context, uint profileId, string? planCode, bool useDefaultPlan, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var teamManagers = context.TeamManagersOf((int)profileId);
            var free = await context.CompanyLicenses.AsNoTracking()
                .Where(l => l.ProfileId == profileId
                            && !l.IsDel
                            && (l.ExpiryDate == null || l.ExpiryDate > now)
                            && !(l.AssignedUserId != null && teamManagers.Any(u => u.UserId == l.AssignedUserId))
                            && !context.AssignedCompanyLicenses.Any(a =>
                                a.CompanyLicenseId == l.Id && a.IsActive && !a.IsDel
                                && a.BusinessUnit != null && a.BusinessUnit.IsDel != true))
                .OrderBy(l => l.Id)
                .Select(l => new { l.Id, l.ServicePlanId })
                .ToListAsync(ct);
            if (free.Count == 0) return (null, AutoPickOutcome.NoneFree);

            var plans = await context.ServicePlans.AsNoTracking().Select(p => new { p.Id, p.Code }).ToListAsync(ct);
            var legacyId = plans.FirstOrDefault(p => p.Code == LicenseTeamLimits.LegacyPlanCode)?.Id;
            if (legacyId == null) return (free[0].Id, AutoPickOutcome.Picked);

            int PlanOf(int? servicePlanId) => servicePlanId ?? legacyId.Value;

            var code = string.IsNullOrWhiteSpace(planCode) ? null : planCode.Trim().ToLowerInvariant();
            if (code != null)
            {
                var wanted = plans.FirstOrDefault(p => p.Code == code)?.Id;
                var hit = free.FirstOrDefault(l => wanted != null && PlanOf(l.ServicePlanId) == wanted);
                return hit == null ? (null, AutoPickOutcome.NoneFree) : (hit.Id, AutoPickOutcome.Picked);
            }

            if (free.Select(l => PlanOf(l.ServicePlanId)).Distinct().Count() == 1)
                return (free[0].Id, AutoPickOutcome.Picked);

            if (!useDefaultPlan) return (null, AutoPickOutcome.MixedPlans);
            var pro = free.FirstOrDefault(l => PlanOf(l.ServicePlanId) == legacyId);
            return (pro?.Id ?? free[0].Id, AutoPickOutcome.Picked);
        }

        /// <summary>
        /// Live licences per plan: purchased, consumed (held by a live Team Manager or on a live
        /// team - the GetLicenseUsageAsync rule), available. Plans with none are left out.
        /// </summary>
        public static async Task<List<PlanLicenseCountDto>> GetPlanLicenseCountsAsync(
            this ApplicationDbContext context, uint profileId, CancellationToken ct = default)
        {
            var plans = await context.ServicePlans.AsNoTracking()
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
                .Select(p => new { p.Id, p.Code, p.Name })
                .ToListAsync(ct);
            var legacyId = plans.FirstOrDefault(p => p.Code == LicenseTeamLimits.LegacyPlanCode)?.Id;
            if (legacyId == null) return new List<PlanLicenseCountDto>();

            var now = DateTime.UtcNow;
            var teamManagers = context.TeamManagersOf((int)profileId);
            var rows = await context.CompanyLicenses.AsNoTracking()
                .Where(l => l.ProfileId == profileId && !l.IsDel && (l.ExpiryDate == null || l.ExpiryDate > now))
                .Select(l => new
                {
                    l.ServicePlanId,
                    Consumed = (l.AssignedUserId != null && teamManagers.Any(u => u.UserId == l.AssignedUserId))
                               || context.AssignedCompanyLicenses.Any(a =>
                                   a.CompanyLicenseId == l.Id && a.IsActive && !a.IsDel
                                   && a.BusinessUnit != null && a.BusinessUnit.IsDel != true)
                })
                .ToListAsync(ct);

            return plans
                .Select(p =>
                {
                    var mine = rows.Where(r => (r.ServicePlanId ?? legacyId) == p.Id).ToList();
                    var consumed = mine.Count(r => r.Consumed);
                    return new PlanLicenseCountDto
                    {
                        ServicePlanId = p.Id,
                        PlanCode = p.Code,
                        PlanName = p.Name,
                        TotalLicenses = mine.Count,
                        ConsumedLicenses = consumed,
                        AvailableLicenses = Math.Max(0, mine.Count - consumed)
                    };
                })
                .Where(c => c.TotalLicenses > 0)
                .ToList();
        }
    }
}
