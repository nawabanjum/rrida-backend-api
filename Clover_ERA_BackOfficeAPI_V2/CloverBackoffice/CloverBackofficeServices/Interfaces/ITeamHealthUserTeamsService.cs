using CloverBackoffice.Infrastructure.DTO.TeamHealth;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>The teams a user can open in Team Health (Team dropdown / ERAN Coach team context).</summary>
    public interface ITeamHealthUserTeamsService
    {
        /// <summary>
        /// The teams <paramref name="userId"/> may access, by the Team Health access rule.
        /// The caller must be that user, a RRIDA Admin, or a Profile Admin of the user's profile.
        /// </summary>
        Task<TeamMilestoneResult<UserTeamsDto>> GetUserTeamsAsync(int userId, int actorUserId, CancellationToken ct = default);
    }
}
