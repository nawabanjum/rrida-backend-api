using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Infrastructure.Models.PlanManagement;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // Each plan's value for a feature (the comparison table cells).
    //
    //   INCLUDED  isIncluded only; limitValue not allowed
    //   LIMIT     isIncluded + limitValue (>= 0, null = Unlimited)
    //   TEXT      isIncluded + displayText (required when included)
    //
    // An inactive feature can be set to not included, never to included. Cells not sent keep
    // their value; a feature with no cell is shown as not included.
    public partial class PlanManagementService
    {
        private sealed record CellInput(PlanFeature Feature, bool IsIncluded, int? LimitValue, string? DisplayText, string? SubText);

        public async Task<PlanManagementResult<ServicePlanDetailDto>> SetPlanFeaturesAsync(int planId, SetPlanFeaturesDto? dto, int actorUserId, CancellationToken ct = default)
        {
            if (!await IsRridaAdminAsync(actorUserId, ct)) return Forbidden<ServicePlanDetailDto>();
            if (dto == null) return BadRequest<ServicePlanDetailDto>(ResponseMessages.Errors.PlanBodyRequired);
            if (dto.Version == null) return BadRequest<ServicePlanDetailDto>(ResponseMessages.Errors.PlanVersionRequired);
            if (dto.Features == null || dto.Features.Count == 0)
                return BadRequest<ServicePlanDetailDto>(ResponseMessages.Errors.PlanFeaturesRequired);

            var (cells, error) = await ValidateCellsAsync(dto.Features, ct);
            if (error != null) return BadRequest<ServicePlanDetailDto>(error);

            var plan = await LoadPlanForWriteAsync(planId, ct);
            if (plan == null) return Fail<ServicePlanDetailDto>(PlanManagementError.NotFound, ResponseMessages.Errors.PlanNotFound);
            if (plan.Version != dto.Version)
                return Fail<ServicePlanDetailDto>(PlanManagementError.Conflict, ResponseMessages.Errors.PlanVersionConflict);

            var before = Snapshot(plan);
            ApplyCells(plan, cells!);
            Touch(plan, actorUserId);

            Audit(ServicePlanAuditActions.FeaturesUpdated, actorUserId, plan.Id, null, before, Snapshot(plan));
            await AddVersionIfChangedAsync(plan, actorUserId, "Feature values changed", ct);
            if (!await TrySaveAsync(ct))
                return Fail<ServicePlanDetailDto>(PlanManagementError.Conflict, ResponseMessages.Errors.PlanVersionConflict);

            return PlanManagementResult<ServicePlanDetailDto>.Ok(await BuildDetailAsync(plan.Id, ct));
        }

        /// <summary>Upserts the cells on the (tracked) plan.</summary>
        private void ApplyCells(ServicePlan plan, IEnumerable<CellInput> cells)
        {
            var now = Now;
            foreach (var c in cells)
            {
                var row = plan.Features.FirstOrDefault(x => x.PlanFeatureId == c.Feature.Id);
                if (row == null)
                {
                    row = new ServicePlanFeature { PlanFeatureId = c.Feature.Id };
                    plan.Features.Add(row);
                }
                row.IsIncluded = c.IsIncluded;
                row.LimitValue = c.LimitValue;
                row.DisplayText = c.DisplayText;
                row.SubText = c.SubText;
                row.UpdatedAt = now;
            }
        }

        /// <summary>
        /// Resolves each entry to its feature (by featureId or featureKey) and checks the value
        /// against the feature's type. Errors name the feature by key, else id, else position.
        /// </summary>
        private async Task<(List<CellInput>? Cells, string? Error)> ValidateCellsAsync(List<PlanFeatureValueInputDto> input, CancellationToken ct)
        {
            var all = await _context.PlanFeatures.AsNoTracking().ToListAsync(ct);
            var byId = all.ToDictionary(f => f.Id);
            var byKey = all.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);

            var result = new List<CellInput>();
            var seen = new HashSet<int>();
            for (var i = 0; i < input.Count; i++)
            {
                var v = input[i];
                var key = Clean(v?.FeatureKey);
                var label = key ?? (v?.FeatureId is > 0 ? v.FeatureId.Value.ToString() : $"#{i + 1}");

                if (v == null || (key == null && v.FeatureId is not > 0))
                    return (null, F(ResponseMessages.Errors.PlanFeatureRefRequired, $"#{i + 1}"));

                PlanFeature? f = null;
                if (v.FeatureId is > 0) byId.TryGetValue(v.FeatureId.Value, out f);
                if (f == null && key != null) byKey.TryGetValue(key, out f);
                if (f == null) return (null, F(ResponseMessages.Errors.PlanFeatureRefNotFound, label));
                label = f.Key;

                if (!seen.Add(f.Id)) return (null, F(ResponseMessages.Errors.PlanFeatureDuplicate, label));
                if (v.IsIncluded == null) return (null, F(ResponseMessages.Errors.PlanFeatureIncludedRequired, label));

                var included = v.IsIncluded.Value;
                if (included && !f.IsActive) return (null, F(ResponseMessages.Errors.PlanFeatureInactive, label));

                var isLimit = f.ValueType == PlanFeatureValueTypes.Limit;
                if (!isLimit && v.LimitValue != null)
                    return (null, F(ResponseMessages.Errors.PlanFeatureLimitNotAllowed, label));
                if (v.LimitValue is < 0)
                    return (null, F(ResponseMessages.Errors.PlanFeatureLimitInvalid, label));

                var display = Clean(v.DisplayText);
                var sub = Clean(v.SubText);
                if (TooLong(display, ServicePlanFeature.DisplayTextMaxLength))
                    return (null, F(ResponseMessages.Errors.PlanFeatureDisplayTextTooLong, label));
                if (TooLong(sub, ServicePlanFeature.SubTextMaxLength))
                    return (null, F(ResponseMessages.Errors.PlanFeatureSubTextTooLong, label));
                if (included && f.ValueType == PlanFeatureValueTypes.Text && display == null)
                    return (null, F(ResponseMessages.Errors.PlanFeatureTextRequired, label));

                // Not included: no limit is stored (the cell is "–" whatever the number was).
                result.Add(new CellInput(f, included, included && isLimit ? v.LimitValue : null, display, sub));
            }

            return (result, null);
        }
    }
}
