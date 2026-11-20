using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    /// <summary>
    /// The team a calendar invite is built for.
    /// </summary>
    public class DailyQuestionCalendarTeamDto
    {
        public uint UnitId { get; set; }

        /// <summary>
        /// The team's short code.
        /// </summary>
        public string TeamCode { get; set; } = string.Empty;
    }

    /// <summary>
    /// The daily check-in calendar event to publish for one team, carrying the questions the
    /// engine asks that team on the event's date.
    /// </summary>
    public class DailyQuestionCalendarEventDto
    {
        /// <summary>
        /// Team code of the business unit (its short code).
        /// </summary>
        public string TeamCode { get; set; } = string.Empty;

        /// <summary>
        /// The engine's key for the daily question; used to build a stable calendar UID.
        /// </summary>
        public int QuestionKey { get; set; }

        /// <summary>
        /// The daily question text, as the engine returns it; used as the calendar event subject.
        /// </summary>
        public string QuestionText { get; set; } = string.Empty;

        /// <summary>
        /// The team question text, as the engine returns it. Empty when the team track is not asked.
        /// </summary>
        public string TeamQuestionText { get; set; } = string.Empty;

        /// <summary>
        /// The team's name, shown in the description. Set for the Team Manager's own invite.
        /// </summary>
        public string TeamName { get; set; } = string.Empty;

        /// <summary>
        /// The mail subject when it should differ from <see cref="Title" /> (e.g. the Team
        /// Manager's mail names the team). Never used as the calendar event title.
        /// </summary>
        public string EmailSubject { get; set; } = string.Empty;

        /// <summary>
        /// The calendar event title: always the standard "RRIDA Daily Check-in". The daily
        /// question changes every day, so it is never part of the recurring event.
        /// </summary>
        public string Title => BusinessModel.DataConstants.DailyCheckInEventTitle;

        /// <summary>
        /// What the mail shows as its subject.
        /// </summary>
        public string Subject => string.IsNullOrWhiteSpace(EmailSubject) ? Title : EmailSubject;

        /// <summary>
        /// Link to the team's page on the RRIDA Feedback app.
        /// </summary>
        public string QuestionLink { get; set; } = string.Empty;

        /// <summary>
        /// Event start, 5:00 PM on the question's date. Written to the .ics as a floating
        /// local time so every recipient sees 5:00 PM in their own time zone.
        /// </summary>
        public DateTime EventStartLocal { get; set; }

        /// <summary>
        /// Event end.
        /// </summary>
        public DateTime EventEndLocal { get; set; }

        /// <summary>
        /// Reminder in minutes before the event start; 0 fires at the start time. Null uses
        /// the standard reminder (<c>DataConstants.DailyCheckInDefaultReminderMinutes</c>, 0).
        /// </summary>
        public int? ReminderMinutes { get; set; }
    }

    /// <summary>
    /// The daily question calendar invite returned as text: the event details plus the
    /// .ics content, so a caller can show the invite without parsing the file.
    /// </summary>
    public class DailyQuestionCalendarInviteDto
    {
        /// <summary>
        /// Suggested file name for the download, e.g. <c>daily-question-20260811.ics</c>.
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        public string ContentType { get; set; } = "text/calendar";

        /// <summary>
        /// The calendar event title: the standard "RRIDA Daily Check-in".
        /// </summary>
        public string Subject { get; set; } = string.Empty;

        /// <summary>
        /// The calendar event description, exactly as written to the .ics file.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// The event time for display, e.g. <c>05:00 PM</c>.
        /// </summary>
        public string EventTime { get; set; } = string.Empty;

        /// <summary>
        /// The event date for display, e.g. <c>2026-08-11</c>.
        /// </summary>
        public string EventDate { get; set; } = string.Empty;

        public DateTime EventStart { get; set; }

        public DateTime EventEnd { get; set; }

        /// <summary>
        /// Team code of the team the invite was built for.
        /// </summary>
        public string TeamCode { get; set; } = string.Empty;

        public string QuestionLink { get; set; } = string.Empty;

        /// <summary>
        /// The reminder written to the event, in minutes before the start; null when none.
        /// </summary>
        public int? ReminderMinutes { get; set; }

        /// <summary>
        /// The .ics document as plain text.
        /// </summary>
        public string CalendarContent { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request for the daily question calendar invite that is mailed out instead of being
    /// returned as a file. The event itself is built exactly as the file endpoint builds it.
    /// </summary>
    public class SendDailyQuestionCalendarInviteDto
    {
        /// <summary>
        /// The business unit (team) the invite is built for. Questions rotate per team, so
        /// this or <see cref="ProfileId" /> is required.
        /// </summary>
        public uint? UnitId { get; set; }

        /// <summary>
        /// Company filter. On its own it builds the invite for the company's first team.
        /// </summary>
        public uint? ProfileId { get; set; }

        /// <summary>
        /// Recipients. Required — at least one address must be supplied. Blank / duplicate
        /// addresses are dropped before sending.
        /// </summary>
        [Required(ErrorMessage = "At least one recipient email is required.")]
        [MinLength(1, ErrorMessage = "At least one recipient email is required.")]
        public List<string> Emails { get; set; } = new();

        /// <summary>
        /// Optional reminder, in minutes before the event (0–10080). 0 is valid and fires at
        /// the start time. Omitted = the standard reminder of 0 minutes (at the start time).
        /// </summary>
        [Range(
            BusinessModel.DataConstants.CalendarReminderMinMinutes,
            BusinessModel.DataConstants.CalendarReminderMaxMinutes,
            ErrorMessage = "reminderMinutes must be between 0 and 10080 (7 days).")]
        public int? ReminderMinutes { get; set; }
    }

    /// <summary>
    /// Outcome of the mailed daily question invite: who it went to, the subject used and the
    /// link the recipients follow to open the event.
    /// </summary>
    public class DailyQuestionCalendarInviteEmailResultDto
    {
        public bool EmailSent { get; set; }

        /// <summary>
        /// True when the mail carried the calendar invitation for the event.
        /// </summary>
        public bool CalendarInviteSent { get; set; }

        /// <summary>
        /// The addresses the mail was actually sent to, after blanks and duplicates were dropped.
        /// </summary>
        public List<string> Recipients { get; set; } = new();

        /// <summary>
        /// The mail subject: the daily question text.
        /// </summary>
        public string Subject { get; set; } = string.Empty;

        /// <summary>
        /// Link to the created event, carried in the mail body.
        /// </summary>
        public string EventLink { get; set; } = string.Empty;

        public DateTime EventStart { get; set; }

        public DateTime EventEnd { get; set; }

        public string TeamCode { get; set; } = string.Empty;

        public string QuestionLink { get; set; } = string.Empty;

        /// <summary>
        /// The reminder written to the event, in minutes before the start; null when none.
        /// </summary>
        public int? ReminderMinutes { get; set; }
    }
}
