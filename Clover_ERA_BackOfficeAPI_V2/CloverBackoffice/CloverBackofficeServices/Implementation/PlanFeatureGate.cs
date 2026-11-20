using System.Globalization;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Services.Interfaces;

namespace CloverBackoffice.Services.Implementation
{
    // Backend enforcement of the plan catalogue (see PlanEntitlementService for who is gated
    // and how the plan is found).
    //
    //   * A feature the user's plan does not include → refused (403) with what plan it needs.
    //   * A monthly limit used up (Eran chats / conversations) → refused with when it resets.
    //   * Content: daily nudges + monthly report messages and the Clover Resources worksheet
    //     card are left out of the engine feed / action card for plans without them.
    //
    // Not limited: anyone without the Team Manager role (RRIDA Admin, Profile Admin,
    // Portfolio Manager); and for Portfolio View, a Team Manager who also is a Profile Admin
    // or Portfolio Manager (that page is part of those roles). A feature row an admin made
    // inactive, or a key the catalogue doesn't have, is not enforced.
    //
    // Scoped: a user's entitlements are read once per request.
    public class PlanFeatureGate : IPlanFeatureGate
    {
        private static readonly HashSet<string> DailyAndMonthlyKinds = new(StringComparer.OrdinalIgnoreCase) { "daily", "monthly" };

        private readonly IPlanEntitlementService _entitlements;
        private readonly IUserRoleAssignmentService _roles;
        private readonly TimeProvider _clock;
        private readonly Dictionary<int, PlanEntitlementsDto?> _cache = new();

        public PlanFeatureGate(IPlanEntitlementService entitlements, IUserRoleAssignmentService roles, TimeProvider? clock = null)
        {
            _entitlements = entitlements;
            _roles = roles;
            _clock = clock ?? TimeProvider.System;
        }

        public async Task<PlanGateDecision> CheckAsync(int userId, string featureKey, CancellationToken ct = default)
        {
            var (e, f) = await ResolveAsync(userId, featureKey, ct);
            if (e == null || f == null) return PlanGateDecision.Allow;
            if (featureKey == PlanFeatureKeys.PortfolioView && await HasPortfolioRoleAsync(userId, ct))
                return PlanGateDecision.Allow;

            if (!f.IsEnabled)
                return new PlanGateDecision(false, string.Format(CultureInfo.InvariantCulture,
                    ResponseMessages.Errors.PlanFeatureNotInPlan, f.Title, e.PlanName ?? "current"));

            if (f.Remaining is <= 0)
            {
                var now = _clock.GetUtcNow().UtcDateTime;
                var reset = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
                return new PlanGateDecision(false, string.Format(CultureInfo.InvariantCulture,
                    ResponseMessages.Errors.PlanFeatureLimitReached,
                    f.Limit, f.LimitUnit ?? f.Title, e.PlanName ?? "current", reset.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)),
                    LimitReached: true);
            }

            return PlanGateDecision.Allow;
        }

        public async Task<bool> IsIncludedAsync(int userId, string featureKey, CancellationToken ct = default)
        {
            var (e, f) = await ResolveAsync(userId, featureKey, ct);
            return e == null || f == null || f.IsEnabled;
        }

        public async Task<List<EngineFeedItemDto>> FilterFeedAsync(int userId, List<EngineFeedItemDto> items, CancellationToken ct = default)
        {
            if (await IsIncludedAsync(userId, PlanFeatureKeys.DailyNudgesMonthlyReporting, ct)) return items;
            return items.Where(i => !DailyAndMonthlyKinds.Contains(i.Kind)).ToList();
        }

        public async Task<EngineActiveActionDto?> FilterActionAsync(int userId, EngineActiveActionDto? action, CancellationToken ct = default)
        {
            if (action == null || await IsIncludedAsync(userId, PlanFeatureKeys.CloverResources, ct)) return action;
            action.Worksheet = null;
            action.WorksheetCode = null;
            action.CanDecline = false;   // the decline button reads the worksheet's conditions
            return action;
        }

        /// <summary>True when a timeline item is a daily nudge or monthly report message.</summary>
        public static bool IsDailyOrMonthly(string? kind) => kind != null && DailyAndMonthlyKinds.Contains(kind);

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>The user's entitlements when plan-gated, and the feature (null = not enforced).</summary>
        private async Task<(PlanEntitlementsDto? E, FeatureEntitlementDto? F)> ResolveAsync(int userId, string featureKey, CancellationToken ct)
        {
            if (!_cache.TryGetValue(userId, out var e))
            {
                e = await _entitlements.GetForUserAsync(userId, ct);
                _cache[userId] = e;
            }
            if (e is not { IsPlanGated: true }) return (null, null);
            var f = e.Features.FirstOrDefault(x => string.Equals(x.Key, featureKey, StringComparison.OrdinalIgnoreCase));
            return (e, f);
        }

        private async Task<bool> HasPortfolioRoleAsync(int userId, CancellationToken ct)
        {
            var roles = await _roles.GetRoleIdsAsync(userId, ct);
            return roles.Contains((int)UserGroups.ProfileAdmin)
                   || roles.Contains((int)UserGroups.PortfolioManager)
                   || roles.Contains((int)UserGroups.RridaAdmin);
        }
    }
}
