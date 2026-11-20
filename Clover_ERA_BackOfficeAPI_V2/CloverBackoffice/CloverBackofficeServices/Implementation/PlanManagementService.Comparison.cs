using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // The comparison table ("What you get on each plan"): one column per plan, one row per
    // active feature, one cell per plan in each row, and the features' footnotes underneath.
    // The admin preview (api/admin/plans/comparison) and the customer list (api/plans) share it.
    public partial class PlanManagementService
    {
        public async Task<PlanManagementResult<PlanComparisonDto>> GetComparisonAsync(bool includeInactive, int actorUserId, CancellationToken ct = default)
        {
            if (!await IsRridaAdminAsync(actorUserId, ct)) return Forbidden<PlanComparisonDto>();
            return PlanManagementResult<PlanComparisonDto>.Ok(await BuildComparisonAsync(_context, includeInactive, ct));
        }

        /// <summary>No access check: callers decide who may see it.</summary>
        public static async Task<PlanComparisonDto> BuildComparisonAsync(ApplicationDbContext context, bool includeInactive, CancellationToken ct)
        {
            var plans = await context.ServicePlans.AsNoTracking()
                .Include(p => p.PriceTiers).Include(p => p.Features)
                .Where(p => includeInactive || p.IsActive)
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
                .ToListAsync(ct);

            // Rows of the table only: a feature shown as part of another row (Clover Resources)
            // is enforced but not listed.
            var features = await context.PlanFeatures.AsNoTracking()
                .Where(f => f.IsActive && f.ShowInComparison)
                .OrderBy(f => f.SortOrder).ThenBy(f => f.Id)
                .ToListAsync(ct);

            var result = new PlanComparisonDto
            {
                Plans = plans.Select(p => new PlanComparisonColumnDto
                {
                    Id = p.Id,
                    Code = p.Code,
                    Name = p.Name,
                    IsHighlighted = p.IsHighlighted,
                    IsActive = p.IsActive,
                    PricingUnit = p.PricingUnit,
                    Currency = p.Currency,
                    PriceSummary = PriceSummary(p),
                    PriceTiers = OrderedTiers(p).Select(t => MapTier(t, p.PricingUnit)).ToList()
                }).ToList(),
                Footnotes = features.Where(f => f.Footnote != null).Select(f => f.Footnote!).Distinct().ToList()
            };

            foreach (var f in features)
            {
                var row = new PlanComparisonRowDto
                {
                    FeatureId = f.Id,
                    Key = f.Key,
                    Title = f.Title,
                    Description = f.Description,
                    ValueType = f.ValueType
                };
                foreach (var p in plans)
                {
                    var v = MapValue(f, p.Features.FirstOrDefault(c => c.PlanFeatureId == f.Id));
                    row.Cells.Add(new PlanComparisonCellDto
                    {
                        PlanId = p.Id,
                        IsIncluded = v.IsIncluded,
                        LimitValue = v.LimitValue,
                        IsUnlimited = v.IsUnlimited,
                        Display = v.Display,
                        SubText = v.SubText
                    });
                }
                result.Rows.Add(row);
            }

            return result;
        }
    }
}
