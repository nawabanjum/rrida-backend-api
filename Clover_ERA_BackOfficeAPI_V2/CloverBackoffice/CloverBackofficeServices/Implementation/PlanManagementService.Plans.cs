using System.Text.RegularExpressions;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Infrastructure.Models.PlanManagement;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // Plans: list, detail, create, update, activate / deactivate, duplicate, reorder, history.
    //
    // Code is lowercase ('pro', 'light-plus'), unique, never changes. Names are unique too
    // (case-insensitive). A new plan is an inactive draft unless isActive is sent; an active
    // plan needs at least one price tier, and the last active plan cannot be deactivated.
    public partial class PlanManagementService
    {
        private static readonly Regex PlanCodePattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

        public async Task<PlanManagementResult<ServicePlanListDto>> ListPlansAsync(ServicePlanListQueryDto? query, int actorUserId, CancellationToken ct = default)
        {
            if (!await IsRridaAdminAsync(actorUserId, ct)) return Forbidden<ServicePlanListDto>();

            var q = _context.ServicePlans.AsNoTracking().Include(p => p.PriceTiers).Include(p => p.Features).AsQueryable();
            switch (Clean(query?.Status)?.ToLowerInvariant())
            {
                case "active": q = q.Where(p => p.IsActive); break;
                case "inactive": q = q.Where(p => !p.IsActive); break;
            }

            var plans = await q.OrderBy(p => p.SortOrder).ThenBy(p => p.Id).ToListAsync(ct);
            var planIds = plans.Select(p => p.Id).ToList();
            var versions = await _context.ServicePlanVersions.AsNoTracking()
                .Where(v => planIds.Contains(v.ServicePlanId))
                .GroupBy(v => v.ServicePlanId)
                .Select(g => new { PlanId = g.Key, Number = g.Max(v => v.VersionNumber) })
                .ToDictionaryAsync(x => x.PlanId, x => x.Number, ct);
            return PlanManagementResult<ServicePlanListDto>.Ok(new ServicePlanListDto
            {
                Total = plans.Count,
                Plans = plans.Select(p =>
                {
                    var dto = MapSummary(p);
                    dto.CurrentVersionNumber = versions.GetValueOrDefault(p.Id);
                    return dto;
                }).ToList()
            });
        }

        public async Task<PlanManagementResult<ServicePlanDetailDto>> GetPlanAsync(int planId, int actorUserId, CancellationToken ct = default)
        {
            if (!await IsRridaAdminAsync(actorUserId, ct)) return Forbidden<ServicePlanDetailDto>();
            if (!await _context.ServicePlans.AnyAsync(p => p.Id == planId, ct))
                return Fail<ServicePlanDetailDto>(PlanManagementError.NotFound, ResponseMessages.Errors.PlanNotFound);

            return PlanManagementResult<ServicePlanDetailDto>.Ok(await BuildDetailAsync(planId, ct));
        }

        public async Task<PlanManagementResult<ServicePlanDetailDto>> CreatePlanAsync(CreateServicePlanDto? dto, int actorUserId, CancellationToken ct = default)
        {
            if (!await IsRridaAdminAsync(actorUserId, ct)) return Forbidden<ServicePlanDetailDto>();
            if (dto == null) return BadRequest<ServicePlanDetailDto>(ResponseMessages.Errors.PlanBodyRequired);

            var code = Clean(dto.Code)?.ToLowerInvariant();
            var codeError = ValidateCode(code);
            if (codeError != null) return BadRequest<ServicePlanDetailDto>(codeError);

            var name = Clean(dto.Name);
            var description = Clean(dto.Description);
            var notes = Clean(dto.InternalNotes);
            var unit = Clean(dto.PricingUnit) ?? DefaultPricingUnit;
            var currency = (Clean(dto.Currency) ?? DefaultCurrency).ToUpperInvariant();
            var detailsError = ValidateDetails(name, description, notes, unit, currency, dto.SortOrder);
            if (detailsError != null) return BadRequest<ServicePlanDetailDto>(detailsError);

            List<ServicePlanPriceTier>? tiers = null;
            if (dto.PriceTiers is { Count: > 0 })
            {
                var (t, tierError) = ValidateTiers(dto.PriceTiers);
                if (tierError != null) return BadRequest<ServicePlanDetailDto>(tierError);
                tiers = t;
            }

            var active = dto.IsActive == true;
            if (active && tiers == null) return BadRequest<ServicePlanDetailDto>(ResponseMessages.Errors.PlanActivateNeedsTiers);

            List<CellInput>? cells = null;
            if (dto.Features is { Count: > 0 })
            {
                var (c, cellError) = await ValidateCellsAsync(dto.Features, ct);
                if (cellError != null) return BadRequest<ServicePlanDetailDto>(cellError);
                cells = c;
            }

            var conflict = await CodeOrNameTakenAsync(code!, name!, null, ct);
            if (conflict != null) return Fail<ServicePlanDetailDto>(PlanManagementError.Conflict, conflict);

            var now = Now;
            var plan = new ServicePlan
            {
                Code = code!,
                Name = name!,
                Description = description,
                InternalNotes = notes,
                PricingUnit = unit,
                Currency = currency,
                IsHighlighted = dto.IsHighlighted == true,
                AllowsTeamLimitExtension = dto.AllowsTeamLimitExtension == true,
                SortOrder = dto.SortOrder ?? await NextPlanSortOrderAsync(ct),
                IsActive = active,
                Version = 1,
                CreatedBy = actorUserId,
                CreatedAt = now,
                UpdatedAt = now
            };
            foreach (var t in tiers ?? new List<ServicePlanPriceTier>())
                plan.PriceTiers.Add(t);
            if (cells != null) ApplyCells(plan, cells);

            await using var tx = await _context.Database.BeginTransactionAsync(ct);
            _context.ServicePlans.Add(plan);
            await _context.SaveChangesAsync(ct);
            Audit(ServicePlanAuditActions.PlanCreated, actorUserId, plan.Id, null, null, Snapshot(plan));
            await AddVersionIfChangedAsync(plan, actorUserId, "Plan created", ct);
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return PlanManagementResult<ServicePlanDetailDto>.Ok(await BuildDetailAsync(plan.Id, ct));
        }

        public async Task<PlanManagementResult<ServicePlanDetailDto>> UpdatePlanAsync(int planId, UpdateServicePlanDto? dto, int actorUserId, CancellationToken ct = default)
        {
            if (!await IsRridaAdminAsync(actorUserId, ct)) return Forbidden<ServicePlanDetailDto>();
            if (dto == null) return BadRequest<ServicePlanDetailDto>(ResponseMessages.Errors.PlanBodyRequired);
            if (dto.Version == null) return BadRequest<ServicePlanDetailDto>(ResponseMessages.Errors.PlanVersionRequired);

            var plan = await LoadPlanForWriteAsync(planId, ct);
            if (plan == null) return Fail<ServicePlanDetailDto>(PlanManagementError.NotFound, ResponseMessages.Errors.PlanNotFound);
            if (plan.Version != dto.Version)
                return Fail<ServicePlanDetailDto>(PlanManagementError.Conflict, ResponseMessages.Errors.PlanVersionConflict);

            // Only the fields sent change; "" clears description / internal notes.
            var name = dto.Name == null ? plan.Name : Clean(dto.Name);
            var description = dto.Description == null ? plan.Description : Clean(dto.Description);
            var notes = dto.InternalNotes == null ? plan.InternalNotes : Clean(dto.InternalNotes);
            var unit = dto.PricingUnit == null ? plan.PricingUnit : Clean(dto.PricingUnit);
            var currency = dto.Currency == null ? plan.Currency : Clean(dto.Currency)?.ToUpperInvariant();
            var detailsError = ValidateDetails(name, description, notes, unit, currency, dto.SortOrder);
            if (detailsError != null) return BadRequest<ServicePlanDetailDto>(detailsError);

            if (!string.Equals(name, plan.Name, StringComparison.OrdinalIgnoreCase))
            {
                var conflict = await CodeOrNameTakenAsync(null, name!, plan.Id, ct);
                if (conflict != null) return Fail<ServicePlanDetailDto>(PlanManagementError.Conflict, conflict);
            }

            var before = Snapshot(plan);
            plan.Name = name!;
            plan.Description = description;
            plan.InternalNotes = notes;
            plan.PricingUnit = unit!;
            plan.Currency = currency!;
            if (dto.IsHighlighted != null) plan.IsHighlighted = dto.IsHighlighted.Value;
            if (dto.AllowsTeamLimitExtension != null) plan.AllowsTeamLimitExtension = dto.AllowsTeamLimitExtension.Value;
            if (dto.SortOrder != null) plan.SortOrder = dto.SortOrder.Value;
            Touch(plan, actorUserId);

            Audit(ServicePlanAuditActions.PlanUpdated, actorUserId, plan.Id, null, before, Snapshot(plan));
            await AddVersionIfChangedAsync(plan, actorUserId, "Plan details changed", ct);
            if (!await TrySaveAsync(ct))
                return Fail<ServicePlanDetailDto>(PlanManagementError.Conflict, ResponseMessages.Errors.PlanVersionConflict);

            return PlanManagementResult<ServicePlanDetailDto>.Ok(await BuildDetailAsync(plan.Id, ct));
        }

        public async Task<PlanManagementResult<ServicePlanDetailDto>> SetPlanStatusAsync(int planId, SetServicePlanStatusDto? dto, int actorUserId, CancellationToken ct = default)
        {
            if (!await IsRridaAdminAsync(actorUserId, ct)) return Forbidden<ServicePlanDetailDto>();
            if (dto?.IsActive == null) return BadRequest<ServicePlanDetailDto>(ResponseMessages.Errors.PlanStatusRequired);

            var reason = Clean(dto.Reason);
            if (TooLong(reason, ServicePlanAuditLog.ReasonMaxLength))
                return BadRequest<ServicePlanDetailDto>(ResponseMessages.Errors.PlanReasonTooLong);

            var plan = await LoadPlanForWriteAsync(planId, ct);
            if (plan == null) return Fail<ServicePlanDetailDto>(PlanManagementError.NotFound, ResponseMessages.Errors.PlanNotFound);
            // Version is optional here (a plain toggle); when sent it must match.
            if (dto.Version != null && plan.Version != dto.Version)
                return Fail<ServicePlanDetailDto>(PlanManagementError.Conflict, ResponseMessages.Errors.PlanVersionConflict);

            var activate = dto.IsActive.Value;
            if (plan.IsActive == activate)
                return BadRequest<ServicePlanDetailDto>(F(ResponseMessages.Errors.PlanStatusUnchanged, activate ? "active" : "inactive"));
            if (activate && plan.PriceTiers.Count == 0)
                return BadRequest<ServicePlanDetailDto>(ResponseMessages.Errors.PlanActivateNeedsTiers);
            if (!activate && !await _context.ServicePlans.AnyAsync(p => p.IsActive && p.Id != plan.Id, ct))
                return BadRequest<ServicePlanDetailDto>(ResponseMessages.Errors.PlanLastActive);

            var before = Snapshot(plan);
            plan.IsActive = activate;
            Touch(plan, actorUserId);

            Audit(activate ? ServicePlanAuditActions.PlanActivated : ServicePlanAuditActions.PlanDeactivated,
                actorUserId, plan.Id, null, before, Snapshot(plan), reason);
            if (!await TrySaveAsync(ct))
                return Fail<ServicePlanDetailDto>(PlanManagementError.Conflict, ResponseMessages.Errors.PlanVersionConflict);

            return PlanManagementResult<ServicePlanDetailDto>.Ok(await BuildDetailAsync(plan.Id, ct));
        }

        public async Task<PlanManagementResult<ServicePlanDetailDto>> DuplicatePlanAsync(int planId, DuplicateServicePlanDto? dto, int actorUserId, CancellationToken ct = default)
        {
            if (!await IsRridaAdminAsync(actorUserId, ct)) return Forbidden<ServicePlanDetailDto>();
            if (dto == null) return BadRequest<ServicePlanDetailDto>(ResponseMessages.Errors.PlanBodyRequired);

            var code = Clean(dto.Code)?.ToLowerInvariant();
            var codeError = ValidateCode(code);
            if (codeError != null) return BadRequest<ServicePlanDetailDto>(codeError);
            var name = Clean(dto.Name);
            if (name == null) return BadRequest<ServicePlanDetailDto>(ResponseMessages.Errors.PlanNameRequired);
            if (TooLong(name, ServicePlan.NameMaxLength)) return BadRequest<ServicePlanDetailDto>(ResponseMessages.Errors.PlanNameTooLong);

            var source = await _context.ServicePlans.AsNoTracking()
                .Include(p => p.PriceTiers).Include(p => p.Features)
                .FirstOrDefaultAsync(p => p.Id == planId, ct);
            if (source == null) return Fail<ServicePlanDetailDto>(PlanManagementError.NotFound, ResponseMessages.Errors.PlanNotFound);

            var conflict = await CodeOrNameTakenAsync(code!, name, null, ct);
            if (conflict != null) return Fail<ServicePlanDetailDto>(PlanManagementError.Conflict, conflict);

            var now = Now;
            var copy = new ServicePlan
            {
                Code = code!,
                Name = name,
                Description = source.Description,
                InternalNotes = source.InternalNotes,
                PricingUnit = source.PricingUnit,
                Currency = source.Currency,
                IsHighlighted = false,
                AllowsTeamLimitExtension = source.AllowsTeamLimitExtension,
                SortOrder = await NextPlanSortOrderAsync(ct),
                IsActive = false,
                Version = 1,
                CreatedBy = actorUserId,
                CreatedAt = now,
                UpdatedAt = now
            };
            foreach (var t in source.PriceTiers)
                copy.PriceTiers.Add(new ServicePlanPriceTier
                {
                    MinManagers = t.MinManagers,
                    MaxManagers = t.MaxManagers,
                    MonthlyPrice = t.MonthlyPrice,
                    AnnualPrice = t.AnnualPrice
                });
            foreach (var c in source.Features)
                copy.Features.Add(new ServicePlanFeature
                {
                    PlanFeatureId = c.PlanFeatureId,
                    IsIncluded = c.IsIncluded,
                    LimitValue = c.LimitValue,
                    DisplayText = c.DisplayText,
                    SubText = c.SubText,
                    UpdatedAt = now
                });

            await using var tx = await _context.Database.BeginTransactionAsync(ct);
            _context.ServicePlans.Add(copy);
            await _context.SaveChangesAsync(ct);
            await AddVersionIfChangedAsync(copy, actorUserId, $"Duplicated from plan {source.Id} ({source.Code})", ct);
            Audit(ServicePlanAuditActions.PlanCreated, actorUserId, copy.Id, null, null, Snapshot(copy),
                $"Duplicated from plan {source.Id} ({source.Code}).");
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return PlanManagementResult<ServicePlanDetailDto>.Ok(await BuildDetailAsync(copy.Id, ct));
        }

        public async Task<PlanManagementResult<ServicePlanListDto>> ReorderPlansAsync(ReorderDto? dto, int actorUserId, CancellationToken ct = default)
        {
            if (!await IsRridaAdminAsync(actorUserId, ct)) return Forbidden<ServicePlanListDto>();

            var plans = await _context.ServicePlans.ToListAsync(ct);
            var ids = dto?.Ids;
            if (ids == null || ids.Count != plans.Count || ids.Distinct().Count() != ids.Count
                || !plans.All(p => ids.Contains(p.Id)))
                return BadRequest<ServicePlanListDto>(F(ResponseMessages.Errors.PlanReorderIdsRequired, "plan"));

            var before = string.Join(",", plans.OrderBy(p => p.SortOrder).ThenBy(p => p.Id).Select(p => p.Id));
            for (var i = 0; i < ids.Count; i++)
            {
                var p = plans.First(x => x.Id == ids[i]);
                if (p.SortOrder == i + 1) continue;
                p.SortOrder = i + 1;
                Touch(p, actorUserId);
            }

            Audit(ServicePlanAuditActions.PlansReordered, actorUserId, null, null,
                $"{{\"ids\":[{before}]}}", $"{{\"ids\":[{string.Join(",", ids)}]}}");
            if (!await TrySaveAsync(ct))
                return Fail<ServicePlanListDto>(PlanManagementError.Conflict, ResponseMessages.Errors.PlanVersionConflict);

            return await ListPlansAsync(null, actorUserId, ct);
        }

        public async Task<PlanManagementResult<PlanAuditListDto>> GetPlanHistoryAsync(int planId, int? take, int actorUserId, CancellationToken ct = default)
        {
            if (!await IsRridaAdminAsync(actorUserId, ct)) return Forbidden<PlanAuditListDto>();
            if (!await _context.ServicePlans.AnyAsync(p => p.Id == planId, ct))
                return Fail<PlanAuditListDto>(PlanManagementError.NotFound, ResponseMessages.Errors.PlanNotFound);

            return PlanManagementResult<PlanAuditListDto>.Ok(
                await HistoryAsync(_context.ServicePlanAuditLogs.Where(a => a.ServicePlanId == planId), take, ct));
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static string? ValidateCode(string? code)
        {
            if (code == null) return ResponseMessages.Errors.PlanCodeRequired;
            if (code.Length < 2 || code.Length > ServicePlan.CodeMaxLength || !PlanCodePattern.IsMatch(code))
                return ResponseMessages.Errors.PlanCodeInvalid;
            return null;
        }

        private static string? ValidateDetails(string? name, string? description, string? notes, string? unit, string? currency, int? sortOrder)
        {
            if (name == null) return ResponseMessages.Errors.PlanNameRequired;
            if (TooLong(name, ServicePlan.NameMaxLength)) return ResponseMessages.Errors.PlanNameTooLong;
            if (TooLong(description, ServicePlan.DescriptionMaxLength)) return ResponseMessages.Errors.PlanDescriptionTooLong;
            if (TooLong(notes, ServicePlan.InternalNotesMaxLength)) return ResponseMessages.Errors.PlanInternalNotesTooLong;
            if (unit == null || TooLong(unit, ServicePlan.PricingUnitMaxLength)) return ResponseMessages.Errors.PlanPricingUnitInvalid;
            if (currency == null || currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper))
                return ResponseMessages.Errors.PlanCurrencyInvalid;
            if (sortOrder is < 0) return ResponseMessages.Errors.PlanSortOrderInvalid;
            return null;
        }

        /// <summary>Conflict message when the code (if given) or name is used by another plan.</summary>
        private async Task<string?> CodeOrNameTakenAsync(string? code, string name, int? exceptPlanId, CancellationToken ct)
        {
            if (code != null && await _context.ServicePlans.AnyAsync(p => p.Code == code, ct))
                return ResponseMessages.Errors.PlanCodeTaken;
            var lower = name.ToLower();
            if (await _context.ServicePlans.AnyAsync(p => p.Name.ToLower() == lower && p.Id != exceptPlanId, ct))
                return ResponseMessages.Errors.PlanNameTaken;
            return null;
        }

        private async Task<int> NextPlanSortOrderAsync(CancellationToken ct) =>
            ((await _context.ServicePlans.MaxAsync(p => (int?)p.SortOrder, ct)) ?? 0) + 1;
    }
}
