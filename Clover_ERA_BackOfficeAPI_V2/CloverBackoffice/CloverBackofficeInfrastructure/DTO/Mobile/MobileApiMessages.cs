using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CloverBackoffice.Infrastructure.DTO.Mobile
{
    /// <summary>
    /// The exact user-facing strings the mobile API returned, and the constants its
    /// validation was written against.
    ///
    /// Reproduced verbatim from the old service's resource files, then re-worded for
    /// Profile terminology when the mobile contract moved to the Profile structure.
    ///
    /// The app surfaces several of these to the user directly, so the wording is part
    /// of the contract: change one only alongside an app release. ProfileName_TooLong
    /// used to carry a genuine shipped typo ("must most not exceed"); it was corrected
    /// in the same release that renamed these fields, since that release breaks
    /// compatibility anyway.
    /// </summary>
    public static class MobileApiMessages
    {
        public const string RequestBody_Invalid =
            "The request body was either not in the correct format or was not submitted.";

        public const string Request_Invalid = "The request contains errors.";

        public const string BusinessUnit_NotFound = "The business unit was not found.";

        public const string BusinessUnitName_Missing = "The business unit name has not been provided.";
        public const string BusinessUnitName_TooLong = "The business unit name must not exceed {0} characters.";
        public const string BusinessUnitShortCode_Length = "The business unit code must be exactly {0} characters.";
        public const string BusinessUnitShortCode_Invalid =
            "The business unit code must contain only numbers and the letters A-F.";

        public const string ProfileName_Missing = "The profile name has not been provided.";

        // Verbatim, typo included. The app has shipped against this string.
        public const string ProfileName_TooLong = "The profile name must not exceed {0} characters.";

        public const string RegistrantEmailAddress_Missing = "The registrant's email address has not been provided.";
        public const string RegistrantEmailAddress_TooLong =
            "The registrant's email address must not exceed {0} characters.";
        public const string RegistrantEmailAddress_Invalid = "The registrant's email address is invalid.";
        public const string RegistrantEmailAddress_Free = "The registrant's email address is a free email address.";
        public const string RegistrantFirstName_Missing = "The registrant's first name has not been provided.";
        public const string RegistrantFirstName_TooLong =
            "The registrant's first name must not exceed {0} characters.";
        public const string RegistrantLastName_Missing = "The registrant's last name has not been provided.";
        public const string RegistrantLastName_TooLong = "The registrant's last name must not exceed {0} characters.";

        public const string ProfileNameConflict = "The profile already exists.";
        public const string RegistrantEmailAddressConflict = "This address doesn't belong to the profile.";

        public const string FeedbackFreeText_TooLong = "The feedback free text must not exceed {0} characters.";

        // The suggestions endpoint declared its two rules with no .WithMessage, so
        // FluentValidation supplied its own wording — a different style to every
        // message above, and split PascalCase property name included. Reproduced as
        // the library emitted it, because the app has shipped against these strings.
        public const string FeedbackText_Missing = "'Feedback Text' must not be empty.";
        public const string SuggestionFeedbackText_TooLong =
            "The length of 'Feedback Text' must be {0} characters or fewer. You entered {1} characters.";
        public const string BusinessUnitId_NotPositive = "'Business Unit Id' must be greater than '0'.";

        /// <summary>
        /// New with team questions, so it is not bound by the old API's wording the way
        /// the messages around it are.
        /// </summary>
        public const string TeamDailyReason_Required = "A team reason is required when answering a team question.";

        // Daily check-in multi-select reasons and team feedback.
        public const string DailyReason_Required =
            "At least one reason or your own reason text is required.";
        public const string DailyReason_NotFound = "One or more selected reasons were not found.";
        public const string TeamDailyReason_NotFound = "One or more selected team reasons were not found.";
        public const string TeamFeedbackText_Missing = "The team feedback text has not been provided.";
        public const string TeamFeedbackText_TooLong = "The team feedback text must not exceed {0} characters.";
        public const string ClientSubmittedAtUtc_Missing = "The client submission time has not been provided.";

        /// <summary>
        /// One message for every way a custom question can fail to be answerable —
        /// unknown, deleted, belonging to another team, or past the day it was
        /// scheduled for. Deliberately not more specific: which of those it is says
        /// something about another team's content, and the app's recovery is the same
        /// in every case (re-fetch the question).
        /// </summary>
        public const string CustomDailyQuestion_NotAvailable =
            "The custom question is not available for this team today.";

        // profile/enroll. New with that endpoint, so not bound by the old API's wording.
        public const string TeamCode_Missing = "The team code has not been provided.";
        public const string TeamCode_Length = "The team code must be exactly {0} characters.";
        public const string TeamCode_Invalid = "The team code must contain only numbers and the letters a-f.";
        public const string TeamCode_NotFound = "The team code was not found.";

        public const string SignInEmailAddress_Missing = "The sign-in email address has not been provided.";
        public const string SignInEmailAddress_TooLong = "The sign-in email address must not exceed {0} characters.";
        public const string SignInEmailAddress_Invalid = "The sign-in email address is invalid.";
        public const string SignInEmailAddress_Free = "The sign-in email address is a free email address.";
    }

    /// <summary>
    /// Field lengths and rotation parameters the mobile API validated and scheduled
    /// against. Changing any of these changes behaviour the app depends on.
    /// </summary>
    public static class MobileApiConstants
    {
        public const int CodeLength = 36;
        public const int ShortCodeLength = 8;
        public const int EmailAddressLength = 254;
        public const int OrganisationalUnitNameLength = 140;
        public const int PersonalNameLength = 60;
        public const int FeedbackFreeTextLength = 896;

        /// <summary>
        /// The suggestions box caps at 2000, not at the 896 the daily free text uses.
        /// Two different limits in the old API, kept apart on purpose.
        /// </summary>
        public const int SuggestionFeedbackTextLength = 2000;

        /// <summary>The question set group given to every newly registered team.</summary>
        public const uint DefaultDailyQuestionSetGroupId = 1;

        /// <summary>
        /// The rotation reads the bank from this ordinal upwards. Everything below it
        /// is older content the rotation deliberately skips.
        /// </summary>
        public const int StartOrdinal = 121;

        /// <summary>
        /// Day zero of the rotation. The served ordinal is a pure function of how many
        /// days have elapsed since this date, so moving it re-points every team's
        /// question for every future day.
        /// </summary>
        public static readonly DateTime RotationStartDateUtc =
            new DateTime(2026, 06, 22, 0, 0, 0, DateTimeKind.Utc);

        // ── Team questions ──────────────────────────────────────────────────
        // The team bank is a second, independent rotation served alongside the daily
        // one. It shares the epoch above so both move on the same day boundary, but
        // it has its own start ordinal: the team bank is numbered from 1, where the
        // legacy bank starts at 121.

        /// <summary>The team rotation reads its bank from this ordinal upwards.</summary>
        public const int TeamStartOrdinal = 1;

        /// <summary>
        /// The category whose questions are served to every team. Team questions are
        /// not yet segmented by industry — the business unit carries no category — so
        /// the rotation reads the seeded "General" category only.
        /// </summary>
        public const uint DefaultTeamQuestionCategoryId = 0;

        /// <summary>Matches TeamDailyFeedback.FreeText, which is varchar(896) like the legacy column.</summary>
        public const int TeamFeedbackFreeTextLength = 896;

        // ── Custom questions ────────────────────────────────────────────────

        /// <summary>
        /// Matches CustomQuestionDailyFeedback.FreeText, which is varchar(300) — a much
        /// shorter column than either of the two above, and not to be aligned with them.
        /// The app enforces the same limit on the keyboard; this is the backstop.
        /// </summary>
        public const int CustomFeedbackFreeTextLength = 300;

        /// <summary>Matches TeamFeedback.FeedbackText, varchar(300).</summary>
        public const int TeamFeedbackTextLength = 300;

        public static readonly string DefaultContentCultureName = CultureInfo.GetCultureInfo("en-US").Name;

        /// <summary>
        /// Short codes are the first 8 characters of a GUID, so hex only.
        ///
        /// Deliberately unanchored and case-SENSITIVE, exactly as the mobile API had
        /// it. Anchoring it or accepting upper case would let through codes the old
        /// endpoint rejected, which is a behaviour change even though it looks like a
        /// bug fix.
        /// </summary>
        public static readonly Regex ShortCodeRegex =
            new Regex("[a-f0-9]{8}", RegexOptions.Compiled);
    }
}
