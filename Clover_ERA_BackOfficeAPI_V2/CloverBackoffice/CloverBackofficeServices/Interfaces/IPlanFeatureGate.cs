using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Backend enforcement of the plan catalogue: may this user use this feature now? Built
    /// on <see cref="IPlanEntitlementService"/>; only Team Managers are plan-gated, RRIDA Admin
    /// is never limited.
    /// </summary>
    public interface IPlanFeatureGate
    {
        /// <summary>
        /// Allowed, or the message to return (403): not in the user's plan, or the monthly
        /// limit is used up.
        /// </summary>
        Task<PlanGateDecision> CheckAsync(int userId, string featureKey, CancellationToken ct = default);

        /// <summary>Whether the plan includes the feature at all (limits ignored). For filtering content.</summary>
        Task<bool> IsIncludedAsync(int userId, string featureKey, CancellationToken ct = default);

        /// <summary>Hides what the user's plan does not include from engine feed items and the action card.</summary>
        Task<List<EngineFeedItemDto>> FilterFeedAsync(int userId, List<EngineFeedItemDto> items, CancellationToken ct = default);

        Task<EngineActiveActionDto?> FilterActionAsync(int userId, EngineActiveActionDto? action, CancellationToken ct = default);
    }

    public sealed record PlanGateDecision(bool Allowed, string? Message = null, bool LimitReached = false)
    {
        public static readonly PlanGateDecision Allow = new(true);
    }
}
