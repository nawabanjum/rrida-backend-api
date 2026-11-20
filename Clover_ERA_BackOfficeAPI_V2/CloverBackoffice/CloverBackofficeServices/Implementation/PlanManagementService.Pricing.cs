using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Infrastructure.Models.PlanManagement;

namespace CloverBackoffice.Services.Implementation
{
    // Price tiers: the per-manager price by how many managers are bought.
    //
    // Rules: 1-20 tiers; each has minManagers >= 1, maxManagers >= minManagers or null, prices
    // 0..99,999,999.99 with at most 2 decimals. Sorted by minManagers they start at 1 and follow
    // on with no gap or overlap; only the last may be open-ended (maxManagers null).
    public partial class PlanManagementService
    {
        private const decimal MaxPrice = 99_999_999.99m;

        public async Task<PlanManagementResult<ServicePlanDetailDto>> ReplacePriceTiersAsync(int planId, ReplacePriceTiersDto? dto, int actorUserId, CancellationToken ct = default)
        {
            if (!await IsRridaAdminAsync(actorUserId, ct)) return Forbidden<ServicePlanDetailDto>();
            if (dto == null) return BadRequest<ServicePlanDetailDto>(ResponseMessages.Errors.PlanBodyRequired);
            if (dto.Version == null) return BadRequest<ServicePlanDetailDto>(ResponseMessages.Errors.PlanVersionRequired);

            var (tiers, error) = ValidateTiers(dto.PriceTiers);
            if (error != null) return BadRequest<ServicePlanDetailDto>(error);

            var plan = await LoadPlanForWriteAsync(planId, ct);
            if (plan == null) return Fail<ServicePlanDetailDto>(PlanManagementError.NotFound, ResponseMessages.Errors.PlanNotFound);
            if (plan.Version != dto.Version)
                return Fail<ServicePlanDetailDto>(PlanManagementError.Conflict, ResponseMessages.Errors.PlanVersionConflict);

            var before = Snapshot(plan);

            // Old tiers are deleted and saved before the new ones are inserted, so the unique
            // (plan, minManagers) index never sees both "1 to ..." rows at once. One transaction:
            // a conflict on the second save rolls the delete back.
            await using var tx = await _context.Database.BeginTransactionAsync(ct);
            _context.ServicePlanPriceTiers.RemoveRange(plan.PriceTiers);
            plan.PriceTiers.Clear();
            Touch(plan, actorUserId);
            if (!await TrySaveAsync(ct))
                return Fail<ServicePlanDetailDto>(PlanManagementError.Conflict, ResponseMessages.Errors.PlanVersionConflict);

            foreach (var t in tiers!)
                plan.PriceTiers.Add(t);
            Audit(ServicePlanAuditActions.PriceTiersReplaced, actorUserId, plan.Id, null, before, Snapshot(plan));
            await AddVersionIfChangedAsync(plan, actorUserId, "Price tiers changed", ct);
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return PlanManagementResult<ServicePlanDetailDto>.Ok(await BuildDetailAsync(plan.Id, ct));
        }

        /// <summary>
        /// Checks a full tier set and returns new (unsaved) rows sorted by minManagers.
        /// Field errors name the tier by its position in the request (1-based).
        /// </summary>
        public static (List<ServicePlanPriceTier>? Tiers, string? Error) ValidateTiers(List<PriceTierInputDto>? input)
        {
            if (input == null || input.Count == 0) return (null, ResponseMessages.Errors.PlanTiersRequired);
            if (input.Count > MaxPriceTiers) return (null, ResponseMessages.Errors.PlanTiersTooMany);

            var rows = new List<ServicePlanPriceTier>();
            for (var i = 0; i < input.Count; i++)
            {
                var t = input[i];
                var n = i + 1;
                if (t == null || t.MinManagers == null || t.MonthlyPrice == null || t.AnnualPrice == null)
                    return (null, F(ResponseMessages.Errors.PlanTierFieldsRequired, n));
                if (t.MinManagers < 1 || (t.MaxManagers != null && t.MaxManagers < t.MinManagers))
                    return (null, F(ResponseMessages.Errors.PlanTierRangeInvalid, n));
                if (!ValidPrice(t.MonthlyPrice.Value) || !ValidPrice(t.AnnualPrice.Value))
                    return (null, F(ResponseMessages.Errors.PlanTierPriceInvalid, n));

                rows.Add(new ServicePlanPriceTier
                {
                    MinManagers = t.MinManagers.Value,
                    MaxManagers = t.MaxManagers,
                    MonthlyPrice = t.MonthlyPrice.Value,
                    AnnualPrice = t.AnnualPrice.Value
                });
            }

            rows = rows.OrderBy(r => r.MinManagers).ToList();
            if (rows[0].MinManagers != 1) return (null, ResponseMessages.Errors.PlanTierMustStartAtOne);

            for (var i = 0; i < rows.Count - 1; i++)
            {
                if (rows[i].MaxManagers == null) return (null, ResponseMessages.Errors.PlanTierOpenEndedNotLast);
                var expected = rows[i].MaxManagers!.Value + 1;
                if (rows[i + 1].MinManagers != expected)
                    return (null, F(ResponseMessages.Errors.PlanTierGapOrOverlap, i + 2, expected));
            }

            return (rows, null);
        }

        private static bool ValidPrice(decimal p) =>
            p >= 0 && p <= MaxPrice && decimal.Round(p, 2) == p;
    }
}
