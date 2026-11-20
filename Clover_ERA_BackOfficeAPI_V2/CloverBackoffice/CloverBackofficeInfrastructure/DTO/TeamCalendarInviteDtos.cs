using System.ComponentModel.DataAnnotations;
using CloverBackoffice.Infrastructure.BusinessModel;

namespace CloverBackoffice.Infrastructure.DTO
{
    /// <summary>
    /// "Email this to me" on the Invite Your Team modal. The recipient is never sent: it is
    /// always the logged-in user's own registered address.
    /// </summary>
    public class SendTeamCalendarInviteDto
    {
        public uint TeamId { get; set; }

        /// <summary>
        /// Optional reminder, in minutes before the event (0–10080). 0 is valid and fires at
        /// the start time. Omitted = the standard reminder of 0 minutes (at the start time).
        /// </summary>
        [Range(
            DataConstants.CalendarReminderMinMinutes,
            DataConstants.CalendarReminderMaxMinutes,
            ErrorMessage = "reminderMinutes must be between 0 and 10080 (7 days).")]
        public int? ReminderMinutes { get; set; }
    }

    public class TeamCalendarInviteResultDto
    {
        public uint TeamId { get; set; }

        public string TeamName { get; set; } = string.Empty;

        /// <summary>
        /// The registered address the invitation goes to.
        /// </summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// False when an identical request was just served, so no second mail went out.
        /// </summary>
        public bool InvitationSent { get; set; }
    }
}
