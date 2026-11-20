using CloverBackoffice.Infrastructure.DTO.PlanManagement;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// What a user's plan gives them: resolved from the live licence they hold (or that covers
    /// one of their teams) → its ServicePlan → feature values, limits, usage, team capacity.
    /// Read-only; nothing enforces it yet (later phase calls HasFeatureAsync / GetFeatureAsync).
    /// </summary>
    public interface IPlanEntitlementService
    {
        Task<PlanEntitlementsDto?> GetForUserAsync(int userId, CancellationToken ct = default);

        /// <summary>
        /// api/entitlements?userId=: self, the user's Profile Admin, or a RRIDA Admin.
        /// </summary>
        Task<PlanManagementResult<PlanEntitlementsDto>> GetForUserAsync(int userId, int actorUserId, CancellationToken ct = default);

        /// <summary>True when the user may use the feature (not gated, or enabled on their plan).</summary>
        Task<bool> HasFeatureAsync(int userId, string featureKey, CancellationToken ct = default);

        /// <summary>The user's entitlement for one feature; null when the key is unknown.</summary>
        Task<FeatureEntitlementDto?> GetFeatureAsync(int userId, string featureKey, CancellationToken ct = default);
    }
}
