using CloverBackoffice.Infrastructure.DTO.TeamHealth;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// The action details panel behind an action dot on the Team Health timeline: read,
    /// edit, close / reopen and comment on an engine action. The manager's record only —
    /// the engine's own rows and decisions are never changed.
    /// </summary>
    public interface ITeamActionService
    {
        /// <summary>
        /// A team's actions delivered in [from, to] (both optional, inclusive), oldest first —
        /// the same items as timeline.actions[], for any range.
        /// </summary>
        Task<TeamMilestoneResult<TeamActionListDto>> ListAsync(string? teamId, string? from, string? to, int actorUserId, CancellationToken ct = default);

        /// <summary>The action's latest state, with its comments.</summary>
        Task<TeamMilestoneResult<TeamActionDto>> GetAsync(string? actionId, int actorUserId, CancellationToken ct = default);

        /// <summary>Changes the manager's wording and/or due date; returns the updated action.</summary>
        Task<TeamMilestoneResult<TeamActionDto>> UpdateAsync(string? actionId, UpdateTeamActionDto? dto, int actorUserId, CancellationToken ct = default);

        /// <summary>
        /// CLOSED marks it closed, IN_FLIGHT reopens it; returns the updated action.
        /// <paramref name="confirmEngineOnClose"/>: the close is the timeline's "I did this", so on
        /// the cycle's current, open, unconfirmed rung it also records the engine confirmation,
        /// as the This Week's Move "I did this" does (api/rrida/manager/confirm).
        /// </summary>
        Task<TeamMilestoneResult<TeamActionDto>> UpdateStatusAsync(string? actionId, UpdateTeamActionStatusDto? dto, int actorUserId, CancellationToken ct = default, bool confirmEngineOnClose = false);

        /// <summary>Adds a comment; returns the action with all its comments.</summary>
        Task<TeamMilestoneResult<TeamActionDto>> AddCommentAsync(string? actionId, AddTeamActionCommentDto? dto, int actorUserId, CancellationToken ct = default);
    }
}
