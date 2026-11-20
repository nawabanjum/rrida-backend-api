using System.Text.RegularExpressions;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Infrastructure.Models.PlanManagement;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // The feature catalogue: the rows of the comparison table, shared by every plan.
    //
    // Key is UPPER_SNAKE_CASE, unique and never changes (code checks it). A system feature
    // (seeded, used by the product) keeps its value type and cannot be deactivated; any other
    // feature's type can change only while no plan has a value for it. Never deleted.
    public partial class PlanManagementService
    {
        private static readonly Regex FeatureKeyPattern = new("^[A-Z][A-Z0-9_]{1,59}$", RegexOptions.Compiled);

        public async Task<PlanManagementResult<PlanFeatureListDto>> ListFeaturesAsync(string? status, int actorUserId, CancellationToken ct = default)
        {
            if (!await IsRridaAdminAsync(actorUserId, ct)) return Forbidden<PlanFeatureListDto>();

            var q = _context.PlanFeatures.AsNoTracking();
            switch (Clean(status)?.ToLowerInvariant())
            {
                case "active": q = q.Where(f => f.IsActive); break;
                case "inactive": q = q.Where(f => !f.IsActive); break;
            }

            var rows = await q.OrderBy(f => f.SortOrder).ThenBy(f => f.Id).ToListAsync(ct);
            var counts = await IncludedCountsAsync(rows.Select(f => f.Id).ToList(), ct);
            return PlanManagementResult<PlanFeatureListDto>.Ok(new PlanFeatureListDto
            {
                Total = rows.Count,
                Features = rows.Select(f => MapFeature(f, counts.GetValueOrDefault(f.Id))).ToList()
            });
        }

        public async Task<PlanManagementResult<PlanFeatureDto>> GetFeatureAsync(int featureId, int actorUserId, CancellationToken ct = default)
        {
            if (!await IsRridaAdminAsync(actorUserId, ct)) return Forbidden<PlanFeatureDto>();

            var f = await _context.PlanFeatures.AsNoTracking().FirstOrDefaultAsync(x => x.Id == featureId, ct);
            if (f == null) return Fail<PlanFeatureDto>(PlanManagementError.NotFound, ResponseMessages.Errors.PlanFeatureNotFound);
            return PlanManagementResult<PlanFeatureDto>.Ok(await MapFeatureAsync(f, ct));
        }

        public async Task<PlanManagementResult<PlanFeatureDto>> CreateFeatureAsync(CreatePlanFeatureDto? dto, int actorUserId, CancellationToken ct = default)
        {
            if (!await IsRridaAdminAsync(actorUserId, ct)) return Forbidden<PlanFeatureDto>();
            if (dto == null) return BadRequest<PlanFeatureDto>(ResponseMessages.Errors.PlanBodyRequired);

            var key = Clean(dto.Key)?.ToUpperInvariant();
            if (key == null) return BadRequest<PlanFeatureDto>(ResponseMessages.Errors.PlanFeatureKeyRequired);
            if (!FeatureKeyPattern.IsMatch(key)) return BadRequest<PlanFeatureDto>(ResponseMessages.Errors.PlanFeatureKeyInvalid);

            var title = Clean(dto.Title);
            var description = Clean(dto.Description);
            var footnote = Clean(dto.Footnote);
            var textError = ValidateFeatureText(title, description, footnote);
            if (textError != null) return BadRequest<PlanFeatureDto>(textError);

            var valueType = Clean(dto.ValueType)?.ToUpperInvariant();
            if (valueType == null || !PlanFeatureValueTypes.All.Contains(valueType))
                return BadRequest<PlanFeatureDto>(ResponseMessages.Errors.PlanFeatureValueTypeInvalid);

            var (unit, period, limitError) = ValidateLimitFields(valueType, Clean(dto.LimitUnit), Clean(dto.LimitPeriod));
            if (limitError != null) return BadRequest<PlanFeatureDto>(limitError);

            if (dto.SortOrder is < 0) return BadRequest<PlanFeatureDto>(ResponseMessages.Errors.PlanSortOrderInvalid);

            if (await _context.PlanFeatures.AnyAsync(f => f.Key == key, ct))
                return Fail<PlanFeatureDto>(PlanManagementError.Conflict, ResponseMessages.Errors.PlanFeatureKeyTaken);

            var sort = dto.SortOrder ?? ((await _context.PlanFeatures.MaxAsync(f => (int?)f.SortOrder, ct)) ?? 0) + 1;
            var now = Now;
            var feature = new PlanFeature
            {
                Key = key,
                Title = title!,
                Description = description,
                ValueType = valueType,
                LimitUnit = unit,
                LimitPeriod = period,
                Footnote = footnote,
                IsSystem = false,
                ShowInComparison = dto.ShowInComparison ?? true,
                SortOrder = sort,
                IsActive = true,
                CreatedBy = actorUserId,
                CreatedAt = now,
                UpdatedAt = now
            };
            _context.PlanFeatures.Add(feature);
            await _context.SaveChangesAsync(ct);

            Audit(ServicePlanAuditActions.FeatureCreated, actorUserId, null, feature.Id, null, Snapshot(feature));
            await _context.SaveChangesAsync(ct);

            return PlanManagementResult<PlanFeatureDto>.Ok(await MapFeatureAsync(feature, ct));
        }

        public async Task<PlanManagementResult<PlanFeatureDto>> UpdateFeatureAsync(int featureId, UpdatePlanFeatureDto? dto, int actorUserId, CancellationToken ct = default)
        {
            if (!await IsRridaAdminAsync(actorUserId, ct)) return Forbidden<PlanFeatureDto>();
            if (dto == null) return BadRequest<PlanFeatureDto>(ResponseMessages.Errors.PlanBodyRequired);

            var f = await _context.PlanFeatures.FirstOrDefaultAsync(x => x.Id == featureId, ct);
            if (f == null) return Fail<PlanFeatureDto>(PlanManagementError.NotFound, ResponseMessages.Errors.PlanFeatureNotFound);

            // Only the fields sent change; "" clears the optional texts. Title can't be cleared.
            var title = dto.Title == null ? f.Title : Clean(dto.Title);
            var description = dto.Description == null ? f.Description : Clean(dto.Description);
            var footnote = dto.Footnote == null ? f.Footnote : Clean(dto.Footnote);
            var textError = ValidateFeatureText(title, description, footnote);
            if (textError != null) return BadRequest<PlanFeatureDto>(textError);

            var valueType = f.ValueType;
            if (dto.ValueType != null)
            {
                valueType = Clean(dto.ValueType)?.ToUpperInvariant() ?? string.Empty;
                if (!PlanFeatureValueTypes.All.Contains(valueType))
                    return BadRequest<PlanFeatureDto>(ResponseMessages.Errors.PlanFeatureValueTypeInvalid);
                if (valueType != f.ValueType)
                {
                    if (f.IsSystem) return BadRequest<PlanFeatureDto>(ResponseMessages.Errors.PlanFeatureSystemTypeLocked);
                    if (await _context.ServicePlanFeatures.AnyAsync(c => c.PlanFeatureId == f.Id, ct))
                        return BadRequest<PlanFeatureDto>(ResponseMessages.Errors.PlanFeatureTypeChangeInUse);
                }
            }

            var typeChanged = valueType != f.ValueType;
            var unitIn = dto.LimitUnit == null ? (typeChanged ? null : f.LimitUnit) : Clean(dto.LimitUnit);
            var periodIn = dto.LimitPeriod == null ? (typeChanged ? null : f.LimitPeriod) : Clean(dto.LimitPeriod);
            var (unit, period, limitError) = ValidateLimitFields(valueType, unitIn, periodIn);
            if (limitError != null) return BadRequest<PlanFeatureDto>(limitError);

            if (dto.SortOrder is < 0) return BadRequest<PlanFeatureDto>(ResponseMessages.Errors.PlanSortOrderInvalid);

            var before = Snapshot(f);
            f.Title = title!;
            f.Description = description;
            f.Footnote = footnote;
            f.ValueType = valueType;
            f.LimitUnit = unit;
            f.LimitPeriod = period;
            if (dto.SortOrder != null) f.SortOrder = dto.SortOrder.Value;
            if (dto.ShowInComparison != null) f.ShowInComparison = dto.ShowInComparison.Value;
            f.UpdatedBy = actorUserId;
            f.UpdatedAt = Now;

            Audit(ServicePlanAuditActions.FeatureUpdated, actorUserId, null, f.Id, before, Snapshot(f));
            await _context.SaveChangesAsync(ct);

            return PlanManagementResult<PlanFeatureDto>.Ok(await MapFeatureAsync(f, ct));
        }

        public async Task<PlanManagementResult<PlanFeatureDto>> SetFeatureStatusAsync(int featureId, SetPlanFeatureStatusDto? dto, int actorUserId, CancellationToken ct = default)
        {
            if (!await IsRridaAdminAsync(actorUserId, ct)) return Forbidden<PlanFeatureDto>();
            if (dto?.IsActive == null) return BadRequest<PlanFeatureDto>(ResponseMessages.Errors.PlanStatusRequired);

            var reason = Clean(dto.Reason);
            if (TooLong(reason, ServicePlanAuditLog.ReasonMaxLength))
                return BadRequest<PlanFeatureDto>(ResponseMessages.Errors.PlanReasonTooLong);

            var f = await _context.PlanFeatures.FirstOrDefaultAsync(x => x.Id == featureId, ct);
            if (f == null) return Fail<PlanFeatureDto>(PlanManagementError.NotFound, ResponseMessages.Errors.PlanFeatureNotFound);

            var activate = dto.IsActive.Value;
            if (f.IsActive == activate)
                return BadRequest<PlanFeatureDto>(F(ResponseMessages.Errors.PlanFeatureStatusUnchanged, activate ? "active" : "inactive"));
            if (!activate && f.IsSystem)
                return BadRequest<PlanFeatureDto>(ResponseMessages.Errors.PlanFeatureSystemCannotDeactivate);

            var before = Snapshot(f);
            f.IsActive = activate;
            f.UpdatedBy = actorUserId;
            f.UpdatedAt = Now;

            Audit(activate ? ServicePlanAuditActions.FeatureActivated : ServicePlanAuditActions.FeatureDeactivated,
                actorUserId, null, f.Id, before, Snapshot(f), reason);
            await _context.SaveChangesAsync(ct);

            return PlanManagementResult<PlanFeatureDto>.Ok(await MapFeatureAsync(f, ct));
        }

        public async Task<PlanManagementResult<PlanFeatureListDto>> ReorderFeaturesAsync(ReorderDto? dto, int actorUserId, CancellationToken ct = default)
        {
            if (!await IsRridaAdminAsync(actorUserId, ct)) return Forbidden<PlanFeatureListDto>();

            var features = await _context.PlanFeatures.ToListAsync(ct);
            var ids = dto?.Ids;
            if (ids == null || ids.Count != features.Count || ids.Distinct().Count() != ids.Count
                || !features.All(f => ids.Contains(f.Id)))
                return BadRequest<PlanFeatureListDto>(F(ResponseMessages.Errors.PlanReorderIdsRequired, "feature"));

            var before = string.Join(",", features.OrderBy(f => f.SortOrder).ThenBy(f => f.Id).Select(f => f.Id));
            var now = Now;
            for (var i = 0; i < ids.Count; i++)
            {
                var f = features.First(x => x.Id == ids[i]);
                if (f.SortOrder == i + 1) continue;
                f.SortOrder = i + 1;
                f.UpdatedBy = actorUserId;
                f.UpdatedAt = now;
            }

            Audit(ServicePlanAuditActions.FeaturesReordered, actorUserId, null, null,
                $"{{\"ids\":[{before}]}}", $"{{\"ids\":[{string.Join(",", ids)}]}}");
            await _context.SaveChangesAsync(ct);

            return await ListFeaturesAsync("all", actorUserId, ct);
        }

        public async Task<PlanManagementResult<PlanAuditListDto>> GetFeatureHistoryAsync(int featureId, int? take, int actorUserId, CancellationToken ct = default)
        {
            if (!await IsRridaAdminAsync(actorUserId, ct)) return Forbidden<PlanAuditListDto>();
            if (!await _context.PlanFeatures.AnyAsync(f => f.Id == featureId, ct))
                return Fail<PlanAuditListDto>(PlanManagementError.NotFound, ResponseMessages.Errors.PlanFeatureNotFound);

            return PlanManagementResult<PlanAuditListDto>.Ok(
                await HistoryAsync(_context.ServicePlanAuditLogs.Where(a => a.PlanFeatureId == featureId), take, ct));
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static string? ValidateFeatureText(string? title, string? description, string? footnote)
        {
            if (title == null) return ResponseMessages.Errors.PlanFeatureTitleRequired;
            if (TooLong(title, PlanFeature.TitleMaxLength)) return ResponseMessages.Errors.PlanFeatureTitleTooLong;
            if (TooLong(description, PlanFeature.DescriptionMaxLength)) return ResponseMessages.Errors.PlanFeatureDescriptionTooLong;
            if (TooLong(footnote, PlanFeature.FootnoteMaxLength)) return ResponseMessages.Errors.PlanFeatureFootnoteTooLong;
            return null;
        }

        /// <summary>LIMIT: unit optional, period NONE (default) or MONTH. Other types: neither.</summary>
        private static (string? Unit, string? Period, string? Error) ValidateLimitFields(string valueType, string? unit, string? period)
        {
            if (valueType != PlanFeatureValueTypes.Limit)
            {
                if (period != null) return (null, null, ResponseMessages.Errors.PlanFeatureLimitPeriodInvalid);
                return (null, null, null);
            }

            if (TooLong(unit, PlanFeature.LimitUnitMaxLength))
                return (null, null, ResponseMessages.Errors.PlanFeatureLimitUnitTooLong);

            var p = period?.ToUpperInvariant() ?? PlanFeatureLimitPeriods.None;
            if (!PlanFeatureLimitPeriods.All.Contains(p))
                return (null, null, ResponseMessages.Errors.PlanFeatureLimitPeriodInvalid);
            return (unit, p, null);
        }

        private async Task<Dictionary<int, int>> IncludedCountsAsync(List<int> featureIds, CancellationToken ct) =>
            await _context.ServicePlanFeatures.AsNoTracking()
                .Where(c => featureIds.Contains(c.PlanFeatureId) && c.IsIncluded)
                .GroupBy(c => c.PlanFeatureId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        private async Task<PlanFeatureDto> MapFeatureAsync(PlanFeature f, CancellationToken ct)
        {
            var counts = await IncludedCountsAsync(new List<int> { f.Id }, ct);
            return MapFeature(f, counts.GetValueOrDefault(f.Id));
        }

        private static PlanFeatureDto MapFeature(PlanFeature f, int includedInPlans) => new()
        {
            Id = f.Id,
            Key = f.Key,
            Title = f.Title,
            Description = f.Description,
            ValueType = f.ValueType,
            LimitUnit = f.LimitUnit,
            LimitPeriod = f.LimitPeriod,
            Footnote = f.Footnote,
            IsSystem = f.IsSystem,
            IsActive = f.IsActive,
            ShowInComparison = f.ShowInComparison,
            SortOrder = f.SortOrder,
            IncludedInPlans = includedInPlans,
            UpdatedAt = f.UpdatedAt
        };
    }
}
