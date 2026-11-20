using CloverBackoffice.Infrastructure.DTO.TeamHealth;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>The Team Health Timeline screen, with all of its drill-downs, in one read.</summary>
    public interface ITeamHealthTimelineService
    {
        /// <summary>
        /// Builds the timeline for a team. <paramref name="teamId"/> is the raw query
        /// value; anything that is not a positive whole number is a 400.
        /// </summary>
        Task<TeamMilestoneResult<TeamHealthTimelineDto>> GetTimelineAsync(string? teamId, int actorUserId, CancellationToken ct = default);
    }
}
