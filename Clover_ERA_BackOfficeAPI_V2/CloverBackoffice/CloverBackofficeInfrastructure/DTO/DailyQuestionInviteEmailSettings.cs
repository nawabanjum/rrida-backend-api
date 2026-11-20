namespace CloverBackoffice.Infrastructure.DTO
{
    /// <summary>
    /// App settings for the mailed daily question calendar invite
    /// (appsettings section <c>DailyQuestion:InviteEmail</c>). The body is html held in
    /// configuration; the event link is appended after it.
    /// </summary>
    public class DailyQuestionInviteEmailSettings
    {
        /// <summary>
        /// The email body, as html. <c>{{TeamCode}}</c> and <c>{{FeedbackLink}}</c> are
        /// replaced with the team's code and its RRIDA Feedback app link.
        /// </summary>
        public string Body { get; set; } = string.Empty;

        /// <summary>
        /// Text of the anchor that carries the event link.
        /// </summary>
        public string EventLinkLabel { get; set; } = "Open the calendar event";

        /// <summary>
        /// true (default): the mail carries the check-in as an editable event, a .ics
        /// attachment with METHOD:PUBLISH and no organizer / attendees. The recipient owns it,
        /// so date and time can be changed in Outlook as well as Google.
        /// false: the earlier meeting request (inline METHOD:REQUEST with organizer
        /// noreply@, accept / decline). Outlook locks date and time on that one.
        /// </summary>
        public bool SendAsEditableEvent { get; set; } = true;
    }

    /// <summary>
    /// A calendar invite carried on an outgoing email. The iCalendar document travels as a
    /// text/calendar REQUEST part, not as a downloadable file, so the mail client renders the
    /// event with its accept / decline controls.
    /// </summary>
    public class EmailCalendarInvite
    {
        /// <summary>
        /// The iCalendar (VCALENDAR) document.
        /// </summary>
        public string Calendar { get; set; } = string.Empty;

        /// <summary>
        /// The mime type of the calendar part. The method must match the METHOD written into
        /// <see cref="Calendar" /> or clients drop the invite.
        /// </summary>
        public string ContentType { get; set; } = "text/calendar; method=REQUEST; charset=utf-8";

        /// <summary>
        /// Part name. Only used by providers that cannot express an alternative content part
        /// and need one; recipients see the invite, not a file, either way.
        /// </summary>
        public string FileName { get; set; } = "invite.ics";

        /// <summary>
        /// Also carry a real .ics file the recipient can download and import.
        /// </summary>
        public bool AttachFile { get; set; }

        /// <summary>
        /// The document carried as the downloadable .ics file when <see cref="AttachFile" /> is
        /// set: a plain event (METHOD:PUBLISH) the recipient owns and can edit, kept apart from
        /// the invitation in <see cref="Calendar" />. Falls back to <see cref="Calendar" />
        /// when empty.
        /// </summary>
        public string? AttachmentCalendar { get; set; }

        /// <summary>
        /// File name of the downloadable .ics attachment.
        /// </summary>
        public string AttachmentFileName { get; set; } = "event.ics";
    }
}
