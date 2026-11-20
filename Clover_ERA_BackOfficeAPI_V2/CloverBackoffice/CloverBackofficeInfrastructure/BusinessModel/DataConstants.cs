using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.BusinessModel
{
    public static partial class DataConstants
    {
        public const ushort CodeLength = 36;
        public const ushort CountryCodeLength = 2;
        public const uint DefaultDailyQuestionSetGroupId = 1;
        public const ushort DomainNameLength = 253;
        public const ushort EmailAddressLength = 254;
        public const ushort FeedbackFreeTextLength = 896;
        public const ushort OrganisationalUnitNameLength = 140;
        public const ushort PersonalNameLength = 60;
        public const ushort QuestionSetSize = 30;
        public const ushort ShortCodeLength = 8;

        /// <summary>
        /// First ordinal of the live daily-question rotation window. Mirrors the
        /// Clover ERA mobile API constant of the same name.
        /// </summary>
        public const ushort StartOrdinal = 121;

        /// <summary>
        /// Date the rotation window starts from. The served ordinal is derived from the
        /// number of whole UTC days elapsed since this date. Mirrors the Clover ERA
        /// mobile API constant of the same name.
        /// </summary>
        public static readonly DateTime RotationStartDateUtc = new DateTime(2026, 06, 22, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Local start time of the daily question calendar event (17:00 / 5:00 PM).
        /// </summary>
        public static readonly TimeSpan DailyQuestionEventLocalStartTime = new TimeSpan(17, 0, 0);

        /// <summary>
        /// Length of the daily question calendar event.
        /// </summary>
        public static readonly TimeSpan DailyQuestionEventDuration = TimeSpan.FromMinutes(15);

        /// <summary>
        /// Smallest calendar invite reminder, in minutes before the event. 0 is valid and
        /// means the reminder fires at the event's start time.
        /// </summary>
        public const int CalendarReminderMinMinutes = 0;

        /// <summary>
        /// Largest calendar invite reminder, in minutes before the event (7 days).
        /// </summary>
        public const int CalendarReminderMaxMinutes = 10080;

        /// <summary>
        /// Title of the recurring daily check-in calendar event. Fixed: the daily and team
        /// questions change every day, so they are never part of the event.
        /// </summary>
        public const string DailyCheckInEventTitle = "RRIDA Daily Check-in";

        /// <summary>
        /// Reminder used when none is requested: 0 minutes, i.e. at the event start time.
        /// </summary>
        public const int DailyCheckInDefaultReminderMinutes = 0;

        public static readonly string DefaultContentCultureName = CultureInfo.GetCultureInfo("en-US").Name;
        public static readonly Regex ShortCodeRegex = _shortCodeRegex();

        [GeneratedRegex(@"[a-f0-9]{8}", RegexOptions.Compiled)]
        private static partial Regex _shortCodeRegex();
    }
}
