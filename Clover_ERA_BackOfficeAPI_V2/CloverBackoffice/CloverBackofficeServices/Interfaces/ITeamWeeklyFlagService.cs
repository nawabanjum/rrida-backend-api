using CloverBackoffice.Infrastructure.DTO.TeamHealth;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>Weekly Flag on the Team Health page. Read only.</summary>
    public interface ITeamWeeklyFlagService
    {
        /// <summary>
        /// All of a team's weekly flags, latest first; or, with <paramref name="weekStartDate"/>,
        /// only the flag whose week contains that date (empty list when there is none).
        /// </summary>
        Task<TeamMilestoneResult<TeamWeeklyFlagsDto>> GetAsync(string? teamId, string? weekStartDate, int actorUserId, CancellationToken ct = default);
    }
}
