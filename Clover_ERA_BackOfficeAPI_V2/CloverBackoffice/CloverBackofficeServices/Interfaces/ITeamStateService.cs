using CloverBackoffice.Infrastructure.DTO.TeamHealth;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>Team State card on the Team Health page. Read only.</summary>
    public interface ITeamStateService
    {
        /// <summary>
        /// The team's current state, the four segment percentages and the trend chart history
        /// for the last <paramref name="days"/> days (30, 60 or 90; anything else is 60).
        /// </summary>
        Task<TeamMilestoneResult<TeamStateDto>> GetAsync(string? teamId, int actorUserId, CancellationToken ct = default, int? days = null);
    }
}
