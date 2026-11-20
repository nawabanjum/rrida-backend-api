using CloverBackoffice.Infrastructure.DTO.TeamHealth;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>Milestones on the Team Health timeline.</summary>
    public interface ITeamMilestoneService
    {
        /// <summary>
        /// Adds a milestone to a team (<c>dto.Id</c> 0) or edits one of its milestones
        /// (<c>dto.Id</c> &gt; 0). Annotation only — no Team Health reading is
        /// recalculated or touched.
        /// </summary>
        Task<TeamMilestoneResult<TeamMilestoneDto>> UpsertAsync(UpsertTeamMilestoneDto? dto, int actorUserId, CancellationToken ct = default);

        /// <summary>A team's milestones for the timeline, oldest first, optionally within [from, to].</summary>
        Task<TeamMilestoneResult<List<TeamMilestoneDto>>> GetForTeamAsync(uint unitId, string? from, string? to, int actorUserId, CancellationToken ct = default);

        /// <summary>
        /// Parses "YYYY-MM-DD", "DD-MM-YYYY" (and the same with '/') into a date.
        /// Anything else, or an impossible date such as 31-02-2026, is null.
        /// </summary>
        DateOnly? ParseDate(string? value);
    }
}
