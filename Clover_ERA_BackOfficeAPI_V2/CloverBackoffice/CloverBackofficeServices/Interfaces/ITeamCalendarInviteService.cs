using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;

namespace CloverBackoffice.Services.Interfaces
{
    public interface ITeamCalendarInviteService
    {
        /// <summary>
        /// Mails the team's calendar invitation to the logged-in Team Manager's own registered
        /// address. Refused unless they manage the team. <paramref name="reminderMinutes" /> is
        /// optional (0–10080; 0 = at the start time).
        /// </summary>
        Task<TeamMilestoneResult<TeamCalendarInviteResultDto>> SendAsync(
            uint teamId, int actorUserId, int? reminderMinutes = null, CancellationToken ct = default);
    }
}
