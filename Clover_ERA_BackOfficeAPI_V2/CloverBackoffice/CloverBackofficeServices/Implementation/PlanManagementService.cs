using System.Globalization;
using System.Text.Json;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Infrastructure.Models.PlanManagement;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // The plan catalogue (api/admin/plans, api/admin/plan-features). See ServicePlan.cs.
    //
    //   PlanManagementService.cs           guard, loading, mapping, audit
    //   PlanManagementService.Plans.cs         plan list / detail / create / update / status /
    //                                          duplicate / reorder / history
    //   PlanManagementService.Pricing.cs       price tier validation and replace
    //   PlanManagementService.PlanFeatures.cs  each plan's value for a feature
    //   PlanManagementService.Features.cs      feature catalogue
    //   PlanManagementService.Comparison.cs    the comparison table
    //
    // RRIDA Admin only, for reads too: the catalogue holds internal notes and drafts. Every
    // write bumps the plan's Version (409 on a stale one) and appends a ServicePlanAuditLogs row
    // with the state before and after. Nothing here reads or writes licences yet.
    public partial class PlanManagementService : IPlanManagementService
    {
        public const int DefaultHistoryTake = 100;
        public const int MaxHistoryTake = 500;
        public const int MaxPriceTiers = 20;
        public const string DefaultPricingUnit = "manager";
        public const string DefaultCurrency = "USD";

        private static readonly JsonSerializerOptions AuditJson = new() { WriteIndented = false };

        private readonly ApplicationDbContext _context;
        private readonly IUserRoleAssignmentService _roles;
        private readonly TimeProvider _clock;

        public PlanManagementService(ApplicationDbContext context, IUserRoleAssignmentService roles, TimeProvider? clock = null)
        {
            _context = context;
            _roles = roles;
            _clock = clock ?? TimeProvider.System;
        }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;

        // ── Guard ────────────────────────────────────────────────────────────

        private async Task<bool> IsRridaAdminAsync(int userId, CancellationToken ct)
        {
            var roles = await _roles.GetRoleIdsAsync(userId, ct);
            return roles.Contains((int)UserGroups.RridaAdmin);
        }

        private static PlanManagementResult<T> Fail<T>(PlanManagementError error, string message) =>
            PlanManagementResult<T>.Fail(error, message);

        private static PlanManagementResult<T> Forbidden<T>() =>
            Fail<T>(PlanManagementError.Forbidden, ResponseMessages.Errors.PlanAdminOnly);

        private static PlanManagementResult<T> BadRequest<T>(string message) =>
            Fail<T>(PlanManagementError.BadRequest, message);

        private static string F(string template, params object[] args) =>
            string.Format(CultureInfo.InvariantCulture, template, args);

        // ── Loading ──────────────────────────────────────────────────────────

        /// <summary>Plan with tiers and feature values (tracked, for writes).</summary>
        private Task<ServicePlan?> LoadPlanForWriteAsync(int planId, CancellationToken ct) =>
            _context.ServicePlans
                .Include(p => p.PriceTiers)
                .Include(p => p.Features)
                .FirstOrDefaultAsync(p => p.Id == planId, ct);

        /// <summary>
        /// Reloads the plan untracked and maps it with every active feature row (plus inactive
        /// ones the plan still has a value for).
        /// </summary>
        private async Task<ServicePlanDetailDto> BuildDetailAsync(int planId, CancellationToken ct)
        {
            var plan = await _context.ServicePlans.AsNoTracking()
                .Include(p => p.PriceTiers)
                .Include(p => p.Features)
                .FirstAsync(p => p.Id == planId, ct);

            var cellFeatureIds = plan.Features.Select(f => f.PlanFeatureId).ToList();
            var features = await _context.PlanFeatures.AsNoTracking()
                .Where(f => f.IsActive || cellFeatureIds.Contains(f.Id))
                .OrderBy(f => f.SortOrder).ThenBy(f => f.Id)
                .ToListAsync(ct);

            var detail = MapDetail(plan, features);
            detail.CurrentVersionNumber = (await _context.CurrentVersionAsync(planId, ct))?.VersionNumber ?? 0;
            return detail;
        }

        // ── Mapping ──────────────────────────────────────────────────────────

        private static List<ServicePlanPriceTier> OrderedTiers(ServicePlan plan) =>
            plan.PriceTiers.OrderBy(t => t.MinManagers).ToList();

        private static void FillSummary(ServicePlanSummaryDto dto, ServicePlan plan)
        {
            var tiers = OrderedTiers(plan);
            dto.Id = plan.Id;
            dto.Code = plan.Code;
            dto.Name = plan.Name;
            dto.Description = plan.Description;
            dto.PricingUnit = plan.PricingUnit;
            dto.Currency = plan.Currency;
            dto.IsHighlighted = plan.IsHighlighted;
            dto.AllowsTeamLimitExtension = plan.AllowsTeamLimitExtension;
            dto.IsActive = plan.IsActive;
            dto.SortOrder = plan.SortOrder;
            dto.StartingMonthlyPrice = tiers.FirstOrDefault()?.MonthlyPrice;
            dto.StartingAnnualPrice = tiers.FirstOrDefault()?.AnnualPrice;
            dto.PriceTierCount = tiers.Count;
            dto.IncludedFeatureCount = plan.Features.Count(f => f.IsIncluded);
            dto.Version = plan.Version;
            dto.UpdatedAt = plan.UpdatedAt;
        }

        private static ServicePlanSummaryDto MapSummary(ServicePlan plan)
        {
            var dto = new ServicePlanSummaryDto();
            FillSummary(dto, plan);
            return dto;
        }

        private static ServicePlanDetailDto MapDetail(ServicePlan plan, IReadOnlyList<PlanFeature> features)
        {
            var dto = new ServicePlanDetailDto();
            FillSummary(dto, plan);
            dto.InternalNotes = plan.InternalNotes;
            dto.CreatedBy = plan.CreatedBy;
            dto.UpdatedBy = plan.UpdatedBy;
            dto.CreatedAt = plan.CreatedAt;
            dto.PriceTiers = OrderedTiers(plan).Select(t => MapTier(t, plan.PricingUnit)).ToList();
            dto.PriceSummary = PriceSummary(plan);

            var cells = plan.Features.ToDictionary(c => c.PlanFeatureId);
            dto.Features = features.Select(f => MapValue(f, cells.GetValueOrDefault(f.Id))).ToList();
            return dto;
        }

        private static PriceTierDto MapTier(ServicePlanPriceTier t, string unit) => new()
        {
            Id = t.Id,
            MinManagers = t.MinManagers,
            MaxManagers = t.MaxManagers,
            MonthlyPrice = t.MonthlyPrice,
            AnnualPrice = t.AnnualPrice,
            Label = TierLabel(t.MinManagers, t.MaxManagers, unit)
        };

        private static PlanFeatureValueDto MapValue(PlanFeature f, ServicePlanFeature? cell)
        {
            var included = cell?.IsIncluded == true;
            var isLimit = f.ValueType == PlanFeatureValueTypes.Limit;
            return new PlanFeatureValueDto
            {
                FeatureId = f.Id,
                Key = f.Key,
                Title = f.Title,
                Description = f.Description,
                ValueType = f.ValueType,
                LimitUnit = f.LimitUnit,
                LimitPeriod = f.LimitPeriod,
                FeatureIsActive = f.IsActive,
                IsConfigured = cell != null,
                IsIncluded = included,
                LimitValue = included && isLimit ? cell!.LimitValue : null,
                IsUnlimited = included && isLimit && cell!.LimitValue == null,
                DisplayText = cell?.DisplayText,
                SubText = cell?.SubText,
                Display = CellDisplay(f, cell)
            };
        }

        /// <summary>
        /// What the comparison table prints for a cell: the admin's display text, else "10" or
        /// "Unlimited" for an included limit, else null (the UI shows ✓ / – from isIncluded).
        /// </summary>
        public static string? CellDisplay(PlanFeature f, ServicePlanFeature? cell)
        {
            if (cell == null) return null;
            if (!string.IsNullOrWhiteSpace(cell.DisplayText)) return cell.DisplayText;
            if (!cell.IsIncluded) return null;
            if (f.ValueType == PlanFeatureValueTypes.Limit)
                return cell.LimitValue?.ToString(CultureInfo.InvariantCulture) ?? "Unlimited";
            return null;
        }

        public static string TierLabel(int min, int? max, string unit)
        {
            var plural = Plural(unit);
            if (max == null) return $"{min}+ {plural}";
            if (max == min) return $"{min} {(min == 1 ? unit : plural)}";
            return $"{min} to {max} {plural}";
        }

        /// <summary>
        /// "$97 per manager per month, or $970 a year" for one tier; for several, one clause per
        /// tier: "$695 per manager per month for 1 to 4 managers, or $6,950 a year; $595 …".
        /// </summary>
        public static string? PriceSummary(ServicePlan plan)
        {
            var tiers = OrderedTiers(plan);
            if (tiers.Count == 0) return null;
            var multi = tiers.Count > 1;
            return string.Join("; ", tiers.Select(t =>
                $"{Money(t.MonthlyPrice, plan.Currency)} per {plan.PricingUnit} per month"
                + (multi ? $" for {TierLabel(t.MinManagers, t.MaxManagers, plan.PricingUnit)}" : string.Empty)
                + $", or {Money(t.AnnualPrice, plan.Currency)} a year"));
        }

        public static string Money(decimal amount, string currency)
        {
            var number = amount == decimal.Truncate(amount)
                ? amount.ToString("#,0", CultureInfo.InvariantCulture)
                : amount.ToString("#,0.00", CultureInfo.InvariantCulture);
            return currency.ToUpperInvariant() switch
            {
                "USD" => "$" + number,
                "GBP" => "£" + number,
                "EUR" => "€" + number,
                _ => $"{currency.ToUpperInvariant()} {number}"
            };
        }

        private static string Plural(string unit) =>
            unit.EndsWith('s') ? unit : unit + "s";

        // ── Audit ────────────────────────────────────────────────────────────

        private sealed record TierSnapshot(int MinManagers, int? MaxManagers, decimal MonthlyPrice, decimal AnnualPrice);

        private sealed record CellSnapshot(int FeatureId, bool IsIncluded, int? LimitValue, string? DisplayText, string? SubText);

        private sealed record PlanSnapshot(
            string Code, string Name, string? Description, string? InternalNotes, string PricingUnit,
            string Currency, bool IsHighlighted, bool AllowsTeamLimitExtension, int SortOrder,
            bool IsActive, int Version, List<TierSnapshot> PriceTiers, List<CellSnapshot> Features);

        private static string Snapshot(ServicePlan p) => JsonSerializer.Serialize(new PlanSnapshot(
            p.Code, p.Name, p.Description, p.InternalNotes, p.PricingUnit, p.Currency, p.IsHighlighted,
            p.AllowsTeamLimitExtension, p.SortOrder, p.IsActive, p.Version,
            OrderedTiers(p).Select(t => new TierSnapshot(t.MinManagers, t.MaxManagers, t.MonthlyPrice, t.AnnualPrice)).ToList(),
            p.Features.OrderBy(c => c.PlanFeatureId)
                .Select(c => new CellSnapshot(c.PlanFeatureId, c.IsIncluded, c.LimitValue, c.DisplayText, c.SubText)).ToList()),
            AuditJson);

        private static string Snapshot(PlanFeature f) => JsonSerializer.Serialize(new
        {
            f.Key, f.Title, f.Description, f.ValueType, f.LimitUnit, f.LimitPeriod, f.Footnote,
            f.IsSystem, f.ShowInComparison, f.SortOrder, f.IsActive
        }, AuditJson);

        private void Audit(string action, int actorUserId, int? planId, int? featureId,
            string? before, string? after, string? reason = null)
        {
            _context.ServicePlanAuditLogs.Add(new ServicePlanAuditLog
            {
                Action = action,
                ServicePlanId = planId,
                PlanFeatureId = featureId,
                ActorUserId = actorUserId,
                Reason = reason,
                Before = before,
                After = after,
                CreatedAt = Now
            });
        }

        private async Task<PlanAuditListDto> HistoryAsync(IQueryable<ServicePlanAuditLog> source, int? take, CancellationToken ct)
        {
            var n = Math.Clamp(take ?? DefaultHistoryTake, 1, MaxHistoryTake);
            var total = await source.CountAsync(ct);
            var rows = await source.AsNoTracking()
                .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
                .Take(n)
                .ToListAsync(ct);

            var actorIds = rows.Select(r => r.ActorUserId).Distinct().ToList();
            var names = await _context.Users.AsNoTracking()
                .Where(u => actorIds.Contains(u.UserId))
                .Select(u => new { u.UserId, u.Name })
                .ToDictionaryAsync(u => u.UserId, u => u.Name, ct);

            return new PlanAuditListDto
            {
                Total = total,
                Entries = rows.Select(r => new PlanAuditEntryDto
                {
                    Id = r.Id,
                    PlanId = r.ServicePlanId,
                    FeatureId = r.PlanFeatureId,
                    Action = r.Action,
                    ActorUserId = r.ActorUserId,
                    ActorName = names.GetValueOrDefault(r.ActorUserId),
                    Reason = r.Reason,
                    Before = r.Before,
                    After = r.After,
                    CreatedAt = r.CreatedAt
                }).ToList()
            };
        }

        // ── Shared validation ────────────────────────────────────────────────

        /// <summary>Trimmed text, null when empty.</summary>
        private static string? Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static bool TooLong(string? value, int max) => value != null && value.Length > max;

        /// <summary>
        /// Saves, turning a concurrent write (another admin saved the same plan in between) into
        /// a 409 instead of an exception.
        /// </summary>
        private async Task<bool> TrySaveAsync(CancellationToken ct)
        {
            try
            {
                await _context.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;
            }
        }

        /// <summary>Marks the plan changed: new version, who and when.</summary>
        private void Touch(ServicePlan plan, int actorUserId)
        {
            plan.Version++;
            plan.UpdatedBy = actorUserId;
            plan.UpdatedAt = Now;
        }
    }
}
