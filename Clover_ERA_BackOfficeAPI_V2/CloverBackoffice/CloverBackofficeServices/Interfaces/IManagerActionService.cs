using CloverBackoffice.Infrastructure.DTO.TeamHealth;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Team actions (api/actions): the ones managers create themselves, listed beside the
    /// engine's actions a manager took up. Engine actions (api/team-health/actions) and the
    /// report Action Hub (api/actionhub) keep their own APIs, unchanged.
    /// </summary>
    public interface IManagerActionService
    {
        Task<TeamMilestoneResult<ManagerActionDto>> CreateAsync(CreateManagerActionDto? dto, int actorUserId, CancellationToken ct = default);

        Task<TeamMilestoneResult<ManagerActionListDto>> ListAsync(ManagerActionQueryDto query, int actorUserId, CancellationToken ct = default);

        Task<TeamMilestoneResult<ManagerActionDto>> GetAsync(string? actionId, int actorUserId, CancellationToken ct = default);

        /// <summary>
        /// The Create Action dropdown: the team's latest ERAN suggestions (engine deliveries up
        /// to today, newest first, taken up or not) plus None / None of these.
        /// </summary>
        Task<TeamMilestoneResult<EranSuggestionListDto>> ListEranSuggestionsAsync(uint? teamId, int actorUserId, CancellationToken ct = default);

        Task<TeamMilestoneResult<ManagerActionDto>> UpdateAsync(string? actionId, UpdateManagerActionDto? dto, int actorUserId, CancellationToken ct = default);

        /// <summary>
        /// Adds a comment. A manual action takes one at any status (no "I'll do it" step); an
        /// engine action ("engine-57") goes through the Team Health action comments.
        /// </summary>
        Task<TeamMilestoneResult<ManagerActionDto>> AddCommentAsync(string? actionId, AddManagerActionCommentDto? dto, int actorUserId, CancellationToken ct = default);

        /// <summary>
        /// The chat block for Eran on a team that is not on the engine: the team's open actions.
        /// Null when there are none.
        /// </summary>
        Task<string?> EranChatBlockAsync(uint unitId, CancellationToken ct = default);
    }
}
