using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using System.Globalization;
using System.Text;

namespace CloverBackoffice.Components.Helpers
{
    /// <summary>
    /// Builds the iCalendar (.ics) payload for the daily question calendar events.
    /// </summary>
    public static class DailyQuestionCalendarBuilder
    {
        private const string ProductId = "-//RRIDA//Daily Question//EN";
        private const string LineBreak = "\r\n";
        private const string UidDomain = "dailyquestion.rrida.com";
        private const string EventLinkBaseUrl = "https://calendar.google.com/calendar/render";

        // The check-in repeats every working day (Monday to Friday) with no end date.
        private const string RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR";

        // UID suffixes. The emailed invitation and the downloadable file are different events:
        // the invitation belongs to the organizer, so a calendar locks its date and time for the
        // attendee. If the file shared that UID, importing it would merge into the locked
        // invitation instead of adding an event the user owns.
        private const string InviteUidSuffix = "-weekdays@";
        private const string DownloadUidSuffix = "-weekdays-download@";

        /// <summary>
        /// Renders the supplied event as a plain calendar event (METHOD:PUBLISH, no organizer or
        /// attendees) for download / import. The importing user owns it, so its date, time and
        /// reminder are editable. Its UID is stable per team, so a later download updates the
        /// same event, and differs from the emailed invitation's, so the two never merge.
        /// </summary>
        /// <param name="calendarEvent">The daily question event to publish.</param>
        /// <param name="stampUtc">The timestamp written to DTSTAMP.</param>
        public static string Build(DailyQuestionCalendarEventDto calendarEvent, DateTime stampUtc)
        {
            return BuildCalendar(calendarEvent, stampUtc, "PUBLISH", organizerEmail: null, organizerName: null, attendeeEmails: null);
        }

        /// <summary>
        /// True when <paramref name="minutes" /> is an allowed reminder: none (null), or
        /// 0–10080 minutes before the event. 0 is valid.
        /// </summary>
        public static bool IsValidReminder(int? minutes)
        {
            return minutes is null
                or (>= DataConstants.CalendarReminderMinMinutes and <= DataConstants.CalendarReminderMaxMinutes);
        }

        /// <summary>
        /// The reminder the event carries: the requested minutes, or the standard 0 minutes (at
        /// the start time) when none was requested.
        /// </summary>
        public static int ResolveReminder(int? minutes)
        {
            return minutes ?? DataConstants.DailyCheckInDefaultReminderMinutes;
        }

        /// <summary>
        /// Renders the same event as an invitation (METHOD:REQUEST) addressed to the supplied
        /// attendees. A REQUEST carries an organizer and an attendee list, which is what makes a
        /// mail client show the event with its accept / decline controls rather than treating it
        /// as a published copy. The event itself is identical to <see cref="Build" />'s.
        /// </summary>
        /// <param name="organizerEmail">Address the invitation is sent from.</param>
        /// <param name="organizerName">Display name of the organizer; optional.</param>
        /// <param name="attendeeEmails">Everyone the invitation is addressed to.</param>
        public static string BuildInvite(
            DailyQuestionCalendarEventDto calendarEvent,
            DateTime stampUtc,
            string organizerEmail,
            string? organizerName,
            IEnumerable<string> attendeeEmails)
        {
            return BuildCalendar(calendarEvent, stampUtc, "REQUEST", organizerEmail, organizerName, attendeeEmails);
        }

        private static string BuildCalendar(
            DailyQuestionCalendarEventDto calendarEvent,
            DateTime stampUtc,
            string method,
            string? organizerEmail,
            string? organizerName,
            IEnumerable<string>? attendeeEmails)
        {
            ArgumentNullException.ThrowIfNull(calendarEvent);

            var builder = new StringBuilder();

            AppendLine(builder, "BEGIN:VCALENDAR");
            AppendLine(builder, "VERSION:2.0");
            AppendLine(builder, $"PRODID:{ProductId}");
            AppendLine(builder, "CALSCALE:GREGORIAN");
            AppendLine(builder, $"METHOD:{method}");

            AppendEvent(builder, calendarEvent, stampUtc, organizerEmail, organizerName, attendeeEmails);

            AppendLine(builder, "END:VCALENDAR");

            return builder.ToString();
        }

        private static void AppendEvent(
            StringBuilder builder,
            DailyQuestionCalendarEventDto calendarEvent,
            DateTime stampUtc,
            string? organizerEmail = null,
            string? organizerName = null,
            IEnumerable<string>? attendeeEmails = null)
        {
            // The UID is one per team: the event is a recurring series, so re-issuing the invite
            // (or downloading the file again) on another day updates the series already in the
            // calendar instead of adding a second one. The invitation and the downloadable file
            // carry different UIDs so an import never merges into the organizer-owned invite.
            var scope = string.IsNullOrWhiteSpace(calendarEvent.TeamCode) ? "generic" : calendarEvent.TeamCode;

            var isInvitation = !string.IsNullOrWhiteSpace(organizerEmail);

            var uid = string.Concat(scope, isInvitation ? InviteUidSuffix : DownloadUidSuffix, UidDomain);

            var (seriesStart, seriesEnd) = GetSeriesStart(calendarEvent);

            AppendLine(builder, "BEGIN:VEVENT");
            AppendLine(builder, $"UID:{uid}");
            AppendLine(builder, $"DTSTAMP:{FormatUtc(stampUtc)}");

            // Floating local times: every recipient sees the event at 5:00 PM their own time.
            AppendLine(builder, $"DTSTART:{FormatFloating(seriesStart)}");
            AppendLine(builder, $"DTEND:{FormatFloating(seriesEnd)}");
            AppendLine(builder, RecurrenceRule);
            AppendLine(builder, $"SUMMARY:{Escape(calendarEvent.Title)}");
            AppendLine(builder, $"DESCRIPTION:{Escape(BuildDescription(calendarEvent))}");
            AppendLine(builder, $"LOCATION:{Escape(calendarEvent.QuestionLink)}");
            AppendLine(builder, $"URL:{Escape(calendarEvent.QuestionLink)}");

            // Only written for an invitation. Without them a client has nobody to reply to and
            // falls back to treating the event as a plain published copy.
            if (isInvitation)
            {
                AppendLine(builder, $"ORGANIZER{FormatCommonName(organizerName)}:mailto:{organizerEmail!.Trim()}");

                foreach (var attendee in (attendeeEmails ?? Enumerable.Empty<string>())
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Select(a => a.Trim()))
                {
                    AppendLine(
                        builder,
                        "ATTENDEE;CUTYPE=INDIVIDUAL;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;"
                            + $"RSVP=TRUE:mailto:{attendee}");
                }

                // A re-issued invite for the same UID must not look older than the one already
                // in the calendar; the series never changes its time or rule, so zero is correct.
                AppendLine(builder, "SEQUENCE:0");
            }

            AppendLine(builder, "STATUS:CONFIRMED");

            // Availability: Free, not Busy. TRANSP is the standard property; Outlook / Exchange
            // read their own busy-status properties, which a meeting request otherwise defaults
            // to Busy.
            AppendLine(builder, "TRANSP:TRANSPARENT");
            AppendLine(builder, "X-MICROSOFT-CDO-BUSYSTATUS:FREE");
            AppendLine(builder, "X-MICROSOFT-CDO-INTENDEDSTATUS:FREE");

            // One VEVENT with an RRULE and no per-occurrence overrides, so the title,
            // description, reminder and availability apply to every occurrence alike.
            AppendAlarm(builder, calendarEvent);
            AppendLine(builder, "END:VEVENT");
        }

        /// <summary>
        /// Writes the event's reminder as a VALARM. Always written: when no reminder was asked
        /// for, the standard 0 minutes (at the start time) is used, so the calendar app's own
        /// default (e.g. 10 minutes before) is not what the event carries.
        /// </summary>
        private static void AppendAlarm(StringBuilder builder, DailyQuestionCalendarEventDto calendarEvent)
        {
            var minutes = ResolveReminder(calendarEvent.ReminderMinutes);

            if (!IsValidReminder(minutes))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(calendarEvent),
                    minutes,
                    $"ReminderMinutes must be between {DataConstants.CalendarReminderMinMinutes} and {DataConstants.CalendarReminderMaxMinutes}.");
            }

            AppendLine(builder, "BEGIN:VALARM");
            AppendLine(builder, "ACTION:DISPLAY");
            AppendLine(builder, $"DESCRIPTION:{Escape(calendarEvent.Title)}");

            // RFC 5545 duration relative to DTSTART: "-PT15M" is 15 minutes before, "PT0M" is
            // at the start itself.
            AppendLine(builder, minutes == 0 ? "TRIGGER:PT0M" : $"TRIGGER:-PT{minutes.ToString(CultureInfo.InvariantCulture)}M");
            AppendLine(builder, "END:VALARM");
        }

        /// <summary>
        /// Renders the optional CN parameter of an ORGANIZER / ATTENDEE line. The value is
        /// quoted, so a name holding a comma or colon cannot break the parameter list.
        /// </summary>
        private static string FormatCommonName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            return $";CN=\"{name.Trim().Replace("\"", string.Empty)}\"";
        }

        /// <summary>
        /// Builds the event body: the team, the team code and the RRIDA Feedback app link
        /// members follow to answer. Every part is static for the team, so the recurring event
        /// never needs updating; the daily and team questions change daily and are left out.
        /// Public so the API can return the same description text alongside the .ics file.
        /// </summary>
        public static string BuildDescription(DailyQuestionCalendarEventDto calendarEvent)
        {
            var description = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(calendarEvent.TeamName))
            {
                description.Append("Team: ").Append(calendarEvent.TeamName).Append('\n');
            }

            description.Append("Team Code: ").Append(calendarEvent.TeamCode).Append('\n');
            description.Append("Answer here: ").Append(calendarEvent.QuestionLink);

            return description.ToString();
        }

        /// <summary>
        /// Builds a link that opens the same event the .ics describes, so a mail recipient can
        /// add it straight from the message instead of opening the attachment. Built from the
        /// event the caller already has — the event itself is still created by <see cref="Build" />.
        /// </summary>
        public static string BuildEventLink(DailyQuestionCalendarEventDto calendarEvent)
        {
            ArgumentNullException.ThrowIfNull(calendarEvent);

            // The times are sent without a trailing Z, matching the floating local times written
            // to the .ics: every recipient sees the event at 5:00 PM in their own time zone.
            var (seriesStart, seriesEnd) = GetSeriesStart(calendarEvent);

            var dates = string.Concat(
                FormatFloating(seriesStart),
                "/",
                FormatFloating(seriesEnd));

            var link = new StringBuilder(EventLinkBaseUrl);

            link.Append("?action=TEMPLATE");
            link.Append("&text=").Append(Uri.EscapeDataString(calendarEvent.Title ?? string.Empty));
            link.Append("&dates=").Append(dates);
            link.Append("&details=").Append(Uri.EscapeDataString(BuildDescription(calendarEvent)));
            link.Append("&location=").Append(Uri.EscapeDataString(calendarEvent.QuestionLink ?? string.Empty));
            link.Append("&recur=").Append(Uri.EscapeDataString(RecurrenceRule));

            // Show as Free, matching TRANSP:TRANSPARENT in the .ics.
            link.Append("&crm=AVAILABLE");

            return link.ToString();
        }

        /// <summary>
        /// First occurrence of the weekday series. A series must start on a day its rule
        /// matches, otherwise some clients show an extra Saturday / Sunday entry and others
        /// drop it, so an event day falling on a weekend moves to the following Monday. Time
        /// and duration are kept.
        /// </summary>
        private static (DateTime Start, DateTime End) GetSeriesStart(DailyQuestionCalendarEventDto calendarEvent)
        {
            var shiftDays = calendarEvent.EventStartLocal.DayOfWeek switch
            {
                DayOfWeek.Saturday => 2,
                DayOfWeek.Sunday => 1,
                _ => 0
            };

            return (
                calendarEvent.EventStartLocal.AddDays(shiftDays),
                calendarEvent.EventEndLocal.AddDays(shiftDays));
        }

        private static string FormatUtc(DateTime value)
        {
            return value.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        }

        private static string FormatFloating(DateTime value)
        {
            return value.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Escapes the characters that carry meaning in an iCalendar text value (RFC 5545).
        /// </summary>
        private static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace(";", "\\;")
                .Replace(",", "\\,")
                .Replace("\r\n", "\\n")
                .Replace("\n", "\\n")
                .Replace("\r", "\\n");
        }

        /// <summary>
        /// Appends a content line, folded at 75 octets as RFC 5545 requires.
        /// </summary>
        private static void AppendLine(StringBuilder builder, string contentLine)
        {
            const int maxOctets = 75;

            var bytes = Encoding.UTF8.GetBytes(contentLine);

            if (bytes.Length <= maxOctets)
            {
                builder.Append(contentLine).Append(LineBreak);
                return;
            }

            var offset = 0;
            var isFirstSegment = true;

            while (offset < bytes.Length)
            {
                // Continuation lines carry a leading space, so they hold one octet less.
                var budget = isFirstSegment ? maxOctets : maxOctets - 1;
                var take = Math.Min(budget, bytes.Length - offset);

                // Never split a multi-byte character across two folded lines.
                while (take > 0 && offset + take < bytes.Length && IsUtf8Continuation(bytes[offset + take]))
                {
                    take--;
                }

                if (take <= 0)
                {
                    break;
                }

                if (!isFirstSegment)
                {
                    builder.Append(' ');
                }

                builder.Append(Encoding.UTF8.GetString(bytes, offset, take)).Append(LineBreak);

                offset += take;
                isFirstSegment = false;
            }
        }

        private static bool IsUtf8Continuation(byte value)
        {
            return (value & 0xC0) == 0x80;
        }
    }
}
