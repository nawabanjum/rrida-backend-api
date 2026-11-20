using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Infrastructure.Models.PlanManagement;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    /// <summary>The terms a licence has: its plan, the version it was bought on and that version's terms.</summary>
    public sealed record LicenceTerms(int PlanId, string PlanCode, string PlanName, int? VersionId, int VersionNumber,
        PlanTerms Terms, bool IsLegacyPlan);

    // Plan versions: which terms each licence has. A licence keeps the version it was bought on
    // (ServicePlanVersionId); one with none has version 1 of its plan (licences from before
    // versions, including every pre-plan licence = Pro version 1). A plan that has no version
    // yet reads its live terms. Shared by the team limits, entitlements and licence prices.
    public static class PlanTermsQueries
    {
        public static async Task<Dictionary<int, LicenceTerms>> GetLicenceTermsAsync(
            this ApplicationDbContext context, IEnumerable<int> licenseIds, CancellationToken ct = default)
        {
            var ids = licenseIds.Distinct().ToList();
            var result = new Dictionary<int, LicenceTerms>();
            if (ids.Count == 0) return result;

            var plans = await context.ServicePlans.AsNoTracking()
                .Select(p => new { p.Id, p.Code, p.Name })
                .ToListAsync(ct);
            var legacyId = plans.FirstOrDefault(p => p.Code == LicenseTeamLimits.LegacyPlanCode)?.Id;
            if (legacyId == null) return result;

            var licences = await context.CompanyLicenses.AsNoTracking()
                .Where(l => ids.Contains(l.Id))
                .Select(l => new { l.Id, l.ServicePlanId, l.ServicePlanVersionId })
                .ToListAsync(ct);

            var planIds = licences.Select(l => l.ServicePlanId ?? legacyId.Value).Distinct().ToList();
            var versionIds = licences.Where(l => l.ServicePlanVersionId != null).Select(l => l.ServicePlanVersionId!.Value).Distinct().ToList();
            var versions = await context.ServicePlanVersions.AsNoTracking()
                .Where(v => versionIds.Contains(v.Id) || (v.VersionNumber == 1 && planIds.Contains(v.ServicePlanId)))
                .ToListAsync(ct);

            var live = new Dictionary<int, PlanTerms>();
            foreach (var l in licences)
            {
                var planId = l.ServicePlanId ?? legacyId.Value;
                var plan = plans.FirstOrDefault(p => p.Id == planId);
                if (plan == null) continue;

                var version = l.ServicePlanVersionId != null
                    ? versions.FirstOrDefault(v => v.Id == l.ServicePlanVersionId && v.ServicePlanId == planId)
                    : null;
                version ??= versions.FirstOrDefault(v => v.ServicePlanId == planId && v.VersionNumber == 1);

                PlanTerms terms;
                if (version != null) terms = PlanTerms.FromJson(version.Terms);
                else
                {
                    if (!live.TryGetValue(planId, out var t))
                        live[planId] = t = await context.LiveTermsAsync(planId, ct) ?? new PlanTerms();
                    terms = t;
                }

                result[l.Id] = new LicenceTerms(plan.Id, plan.Code, plan.Name, version?.Id, version?.VersionNumber ?? 0,
                    terms, l.ServicePlanId == null);
            }
            return result;
        }

        /// <summary>The plan's newest version; null when it has none.</summary>
        public static Task<ServicePlanVersion?> CurrentVersionAsync(this ApplicationDbContext context, int planId, CancellationToken ct = default) =>
            context.ServicePlanVersions.AsNoTracking()
                .Where(v => v.ServicePlanId == planId)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefaultAsync(ct);

        /// <summary>Terms of one version of a plan (null id = version 1; no versions = live terms).</summary>
        public static async Task<PlanTerms?> TermsOfAsync(this ApplicationDbContext context, int planId, int? versionId, CancellationToken ct = default)
        {
            var version = await context.ServicePlanVersions.AsNoTracking()
                .Where(v => v.ServicePlanId == planId && (versionId != null ? v.Id == versionId : v.VersionNumber == 1))
                .FirstOrDefaultAsync(ct);
            return version != null ? PlanTerms.FromJson(version.Terms) : await context.LiveTermsAsync(planId, ct);
        }

        /// <summary>The plan's terms as they are now (what a new version freezes).</summary>
        public static async Task<PlanTerms?> LiveTermsAsync(this ApplicationDbContext context, int planId, CancellationToken ct = default)
        {
            var plan = await context.ServicePlans.AsNoTracking()
                .Include(p => p.PriceTiers).Include(p => p.Features)
                .FirstOrDefaultAsync(p => p.Id == planId, ct);
            if (plan == null) return null;
            var keys = await context.PlanFeatures.AsNoTracking()
                .Select(f => new { f.Id, f.Key })
                .ToDictionaryAsync(f => f.Id, f => f.Key, ct);
            return TermsOf(plan, keys);
        }

        /// <summary>Freezes a (loaded) plan's terms.</summary>
        public static PlanTerms TermsOf(ServicePlan plan, IReadOnlyDictionary<int, string> featureKeys) => new()
        {
            Currency = plan.Currency,
            PricingUnit = plan.PricingUnit,
            AllowsTeamLimitExtension = plan.AllowsTeamLimitExtension,
            PriceTiers = plan.PriceTiers.OrderBy(t => t.MinManagers)
                .Select(t => new PlanTermsTier { MinManagers = t.MinManagers, MaxManagers = t.MaxManagers, MonthlyPrice = t.MonthlyPrice, AnnualPrice = t.AnnualPrice })
                .ToList(),
            Features = plan.Features.OrderBy(c => c.PlanFeatureId)
                .Select(c => new PlanTermsFeature
                {
                    FeatureId = c.PlanFeatureId,
                    Key = featureKeys.TryGetValue(c.PlanFeatureId, out var k) ? k : string.Empty,
                    IsIncluded = c.IsIncluded,
                    LimitValue = c.LimitValue,
                    DisplayText = c.DisplayText,
                    SubText = c.SubText
                })
                .ToList()
        };
    }
}
