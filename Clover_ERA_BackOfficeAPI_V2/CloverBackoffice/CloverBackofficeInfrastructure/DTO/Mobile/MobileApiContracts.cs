using System;
using System.Collections.Generic;
using System.Linq;
using CloverBackoffice.Infrastructure.Models;
using TQ = CloverBackoffice.Infrastructure.Models.TeamQuestions;

namespace CloverBackoffice.Infrastructure.DTO.Mobile
{
    // ═════════════════════════════════════════════════════════════════════════
    // The wire contract of the mobile app, reproduced exactly.
    //
    // These are the one part of the port that is NOT free to follow house style.
    // A shipped app is already sending and parsing these shapes, so property
    // names, nesting and types are fixed. Three consequences worth knowing
    // before editing anything here:
    //
    //   * Responses are returned bare. They are NOT wrapped in
    //     ApiResponseHandler's { isSuccess, message, content } envelope like the
    //     rest of this API, because the app reads the object directly.
    //   * Enums serialise as their integer values, matching the old API's
    //     serializer settings. Do not add a string enum converter.
    //   * MobileErrorFieldItem.FieldName carries the PascalCase C# property name
    //     ("BusinessUnitShortCode"), because the old API populated it from
    //     FluentValidation's PropertyName. The JSON key is camelCase; the value
    //     inside it is not.
    // ═════════════════════════════════════════════════════════════════════════

    // ── Shared error shape ──────────────────────────────────────────────────

    /// <summary>
    /// The body returned with every 400 from a mobile endpoint.
    /// Serialises as <c>{ "errors": [...], "message": "..." }</c>.
    /// </summary>
    public class MobileErrorResponse
    {
        public MobileErrorResponse(string message)
        {
            Errors = new List<MobileErrorFieldItem>();
            Message = message;
        }

        public List<MobileErrorFieldItem> Errors { get; }

        public string Message { get; }
    }

    /// <summary>One field-level validation failure.</summary>
    public class MobileErrorFieldItem
    {
        /// <summary>The PascalCase name of the offending property.</summary>
        public required string FieldName { get; init; }

        public required string Message { get; init; }
    }

    // ── Daily questions ─────────────────────────────────────────────────────

    /// <summary>Body of both fetch-daily-question and fetch-next-daily-questions.</summary>
    public class DailyQuestionFetchRequest
    {
        public uint BusinessUnitId { get; set; }
    }

    /// <summary>One question, with the company and team it was served to.</summary>
    public class DailyQuestionFetchResponse
    {
        public required string ProfileName { get; init; }
        public required string BusinessUnitName { get; init; }
        public required uint QuestionId { get; init; }
        public required string QuestionText { get; init; }
        public required DailyReasonResponse[] Reasons { get; init; }
        public CloverCategory CloverCategory { get; set; }

        /// <summary>
        /// The team question for the same day, or null when the team bank has nothing
        /// to serve. Added, never substituted: every field above keeps its meaning and
        /// its value, so a client that does not know about team questions reads this
        /// response exactly as it did before and ignores this property.
        /// </summary>
        public TeamQuestionFetchResponse? TeamQuestion { get; init; }

        /// <summary>
        /// The one-off question this team's company scheduled for today, or null when it
        /// has none. Added on the same terms as <see cref="TeamQuestion" />: every field
        /// above keeps its meaning and its value, so a client that does not know about
        /// custom questions reads this response exactly as it did before.
        /// </summary>
        public CustomQuestionFetchResponse? CustomQuestion { get; init; }
    }

    /// <summary>One selectable reason under a question.</summary>
    public class DailyReasonResponse
    {
        public required uint ReasonId { get; init; }
        public required string ReasonText { get; init; }
        public required bool RequiresFreeTextFeedback { get; init; }
        public required DailyReasonSentimentType Sentiment { get; init; }
    }

    // ── Team questions ──────────────────────────────────────────────────────
    //
    // The team bank served alongside the daily one. The shapes below mirror the
    // daily ones field for field so the app renders both screens the same way; the
    // only additions are the team categorisation, the reason's TeamState, and the
    // team's goals.
    //
    // Enums serialise as integers here too — see the note at the top of this file.

    /// <summary>One team question, with its reasons and the team's goals.</summary>
    public class TeamQuestionFetchResponse
    {
        public required uint TeamQuestionId { get; init; }
        public required string QuestionText { get; init; }
        public required TeamReasonResponse[] Reasons { get; init; }

        // ── Categorisation ──
        //
        // Each value is sent with the words to put on screen beside it. The client
        // needs the number to post an answer back and the wording to draw the header,
        // and sending both stops a shipped app hard-coding "1 means Goal Affinity" —
        // which goes wrong the day a condition is renamed.

        /// <summary>The workbook grouping the question belongs to.</summary>
        public TQ.TeamElement TeamElement { get; init; }

        /// <summary>The enum name, e.g. "DirectionAndLearning".</summary>
        public required string TeamElementName { get; init; }

        /// <summary>Display wording, e.g. "Direction and Learning".</summary>
        public required string TeamElementLabel { get; init; }

        /// <summary>Which of the eight team conditions this question measures.</summary>
        public TQ.TeamSubCategory SubCategory { get; init; }

        /// <summary>The enum name, e.g. "GoalAffinity".</summary>
        public required string SubCategoryName { get; init; }

        /// <summary>
        /// The heading the check-in screen shows above the question — "Goal Affinity".
        /// </summary>
        public required string SubCategoryLabel { get; init; }

        /// <summary>
        /// The two-letter badge shown beside that heading — "GA". Always two characters,
        /// so the badge is the same size whichever condition is being asked about.
        /// </summary>
        public required string SubCategoryShortLabel { get; init; }

        /// <summary>
        /// Generic wording, or the goal-anchored restatement. Goal-anchored is only
        /// served when the team actually has a goal to anchor it to.
        /// </summary>
        public TQ.TeamQuestionVariant Variant { get; init; }

        /// <summary>The enum name, e.g. "GoalAnchored".</summary>
        public required string VariantName { get; init; }

        /// <summary>
        /// The goal this question is anchored to, when <see cref="Variant" /> is
        /// GoalAnchored. Null for a generic question, and null for a team that has no
        /// goals — in which case a generic question was served instead. The question
        /// text reads "This is one of your team's goals…", so the goal has to travel
        /// with it.
        /// </summary>
        public TeamGoalResponse? AnchoredGoal { get; init; }

        /// <summary>
        /// Every goal the team is currently working towards, in display order. Empty
        /// array — never null — when the team has not been set up or has retired all of
        /// its goals. The team screen shows these alongside the question whether or not
        /// the question is anchored to one of them.
        /// </summary>
        public required TeamGoalResponse[] TeamGoals { get; init; }
    }

    /// <summary>One selectable reason under a team question.</summary>
    public class TeamReasonResponse
    {
        public required uint ReasonId { get; init; }
        public required string ReasonText { get; init; }
        public required bool RequiresFreeTextFeedback { get; init; }
        public required TQ.TeamDailyReasonSentimentType Sentiment { get; init; }

        /// <summary>
        /// The team state this reason points at. Null where the workbook left the cell
        /// empty, which is most of them.
        /// </summary>
        public TQ.TeamState? TeamState { get; init; }

        /// <summary>Display wording for <see cref="TeamState" />. Null when it is.</summary>
        public string? TeamStateLabel { get; init; }
    }

    /// <summary>One of the team's goals.</summary>
    public class TeamGoalResponse
    {
        public required uint TeamGoalId { get; init; }
        public required string GoalText { get; init; }
        public required short DisplayOrder { get; init; }
        public DateOnly? TargetDate { get; init; }
    }

    // ── Custom questions ────────────────────────────────────────────────────
    //
    // A one-off question a company authors for one of its own business units and
    // schedules for a single day. Unlike the daily and team banks there is no
    // rotation, no reason set and no sentiment scale behind it — the answer is free
    // text — so the shape below carries only what the screen needs to draw.
    //
    // The app owns every piece of surrounding copy (banner, placeholder, optional
    // label, character limit, disclaimer), so none of it is sent from here.

    /// <summary>Today's custom question for a team, when one is scheduled.</summary>
    public class CustomQuestionFetchResponse
    {
        /// <summary>Stable id, sent back on submit as CustomDailyQuestionId.</summary>
        public required uint CustomDailyQuestionId { get; init; }

        public required string QuestionText { get; init; }
    }

    // ── Daily feedback ──────────────────────────────────────────────────────

    /// <summary>
    /// An answer to a daily question, and optionally to the team question served with
    /// it.
    ///
    /// The five original properties are untouched. The team properties are all
    /// nullable and are acted on only when <see cref="TeamDailyQuestionId" /> is set,
    /// so a request from the shipped app behaves exactly as it did: it writes one
    /// DailyFeedback row and nothing else.
    /// </summary>
    public class DailyFeedbackSubmissionRequest
    {
        public uint BusinessUnitId { get; set; }
        public uint DailyQuestionId { get; set; }
        public DailyFeedbackSentimentType DailyFeedbackSentimentType { get; set; }

        /// <summary>
        /// Deprecated but still sent: the first item of <see cref="DailyReasonIds" />.
        /// Used on its own only when the array is omitted (older app builds).
        /// </summary>
        public uint DailyReasonId { get; set; }

        /// <summary>
        /// Every predefined reason selected for the personal question (1-N). At least
        /// one of these or a non-empty <see cref="FeedbackFreeText" /> is required.
        /// </summary>
        public uint[]? DailyReasonIds { get; set; }

        /// <summary>The member's own "Add your own reason..." text. Independent of the reasons.</summary>
        public string? FeedbackFreeText { get; set; }

        /// <summary>
        /// The team question being answered. Omitted or 0 means no team answer is being
        /// submitted, and nothing is written to TeamDailyFeedback.
        /// </summary>
        public uint? TeamDailyQuestionId { get; set; }

        /// <summary>
        /// Deprecated but still sent: the first item of <see cref="TeamDailyReasonIds" />.
        /// Used on its own only when the array is omitted (older app builds).
        /// </summary>
        public uint? TeamDailyReasonId { get; set; }

        /// <summary>
        /// Every team reason selected (1-N). A team answer needs at least one reason,
        /// from this array or the legacy <see cref="TeamDailyReasonId" />.
        /// </summary>
        public uint[]? TeamDailyReasonIds { get; set; }

        public TQ.TeamDailyFeedbackSentimentType? TeamFeedbackSentimentType { get; set; }

        /// <summary>Free text for the team answer, capped like the daily one at 896.</summary>
        public string? TeamFeedbackFreeText { get; set; }

        /// <summary>
        /// The custom question being answered. Omitted or 0 means no custom answer is
        /// being submitted, and nothing is written to CustomQuestionDailyFeedback.
        /// </summary>
        public uint? CustomDailyQuestionId { get; set; }

        /// <summary>
        /// What the member typed. Capped at 300 by the column, and optional: a custom
        /// question has no reason set, so an empty answer is a real outcome rather than
        /// a missing field.
        /// </summary>
        public string? CustomFeedbackFreeText { get; set; }

        /// <summary>
        /// True when the member chose "Skip and finish" instead of typing. The answer is
        /// still recorded — as a row with no free text — so a skip stays countable
        /// against the question rather than being indistinguishable from never having
        /// been shown it.
        /// </summary>
        public bool CustomFeedbackSkipped { get; set; }

        /// <summary>
        /// The selected personal reasons as a set: <see cref="DailyReasonIds" /> when
        /// sent, otherwise the legacy <see cref="DailyReasonId" />. Order kept, first
        /// selection first.
        /// </summary>
        public List<uint> SelectedDailyReasonIds() => ReasonSet(DailyReasonIds, DailyReasonId);

        /// <summary>The selected team reasons as a set, on the same terms.</summary>
        public List<uint> SelectedTeamDailyReasonIds() => ReasonSet(TeamDailyReasonIds, TeamDailyReasonId ?? 0);

        private static List<uint> ReasonSet(uint[]? ids, uint legacyId)
        {
            var set = (ids ?? Array.Empty<uint>()).Where(id => id > 0).Distinct().ToList();

            if (set.Count == 0 && legacyId > 0)
                set.Add(legacyId);

            return set;
        }
    }

    /// <summary>
    /// The nudge shown after answering. Empty string, never null and never an
    /// error, when the chosen reason has no micro-action behind it.
    /// </summary>
    public class DailyFeedbackSubmissionResponse
    {
        public string? SuggestedMicroAction { get; init; }

        /// <summary>
        /// The nudge for the team answer, on the same terms as the daily one. Null when
        /// no team answer was submitted; empty string when one was and its reason has no
        /// micro-action behind it.
        /// </summary>
        public string? TeamSuggestedMicroAction { get; init; }
    }

    // ── Team feedback ───────────────────────────────────────────────────────

    /// <summary>
    /// Body of submit-team-feedback: the optional "Any other feedback about your
    /// team?" text from the check-in wrap-up screen. Anonymous — only the team is
    /// sent. Nullable fields so a missing value is reported as a field error the app
    /// parses, not an ASP.NET ProblemDetails.
    /// </summary>
    public class TeamFeedbackSubmissionRequest
    {
        public uint BusinessUnitId { get; set; }

        /// <summary>Required, at most 300 characters.</summary>
        public string? FeedbackText { get; set; }

        /// <summary>Required, ISO-8601.</summary>
        public DateTime? ClientSubmittedAtUtc { get; set; }
    }

    // ── Member enrolment ────────────────────────────────────────────────────

    /// <summary>Joining a team by its printed short code.</summary>
    public class MemberEnrolmentRequest
    {
        // Nullable for the same reason as the registration fields below: a
        // non-nullable reference property is implicitly [Required] under
        // [ApiController], and that check pre-empts the action, answering a null
        // with ProblemDetails instead of the MobileErrorResponse the app parses.
        public string? BusinessUnitShortCode { get; set; }
    }

    public class MemberEnrolmentResponse
    {
        public required uint BusinessUnitId { get; init; }
        public required DateTime BusinessUnitRegistrationDate { get; init; }
    }

    /// <summary>
    /// Body of profile/enroll: the profile name and the team code the member typed.
    /// A valid team code answers with the same body fetch-daily-question returns.
    /// </summary>
    public class ProfileEnrolmentRequest
    {
        // Nullable for the implicit-required reason described on
        // MemberEnrolmentRequest: the endpoint validates both itself, so a missing
        // value comes back as the MobileErrorResponse the app parses.
        public string? ProfileName { get; set; }
        public string? TeamCode { get; set; }
    }

    /// <summary>
    /// profile/enroll's answer: every field fetch-daily-question returns, plus the
    /// team's id and registration date the team code resolved to.
    ///
    /// A subclass rather than two new properties on DailyQuestionFetchResponse, so
    /// fetch-daily-question's body stays exactly as it is.
    /// </summary>
    public class ProfileEnrolmentResponse : DailyQuestionFetchResponse
    {
        public required uint BusinessUnitId { get; init; }
        public required DateTime BusinessUnitRegistrationDate { get; init; }

        /// <summary>
        /// Copies the question as fetch-daily-question built it. A field added to
        /// DailyQuestionFetchResponse must be copied here too.
        /// </summary>
        public static ProfileEnrolmentResponse From(
            DailyQuestionFetchResponse question, MemberEnrolmentResponse team) => new()
        {
            BusinessUnitId = team.BusinessUnitId,
            BusinessUnitRegistrationDate = team.BusinessUnitRegistrationDate,
            ProfileName = question.ProfileName,
            BusinessUnitName = question.BusinessUnitName,
            QuestionId = question.QuestionId,
            QuestionText = question.QuestionText,
            Reasons = question.Reasons,
            CloverCategory = question.CloverCategory,
            TeamQuestion = question.TeamQuestion,
            CustomQuestion = question.CustomQuestion
        };
    }

    // ── Productivity ────────────────────────────────────────────────────────

    /// <summary>A day's productivity reflection.</summary>
    public class ProductivitySubmissionRequest
    {
        public uint BusinessUnitId { get; set; }
        public uint PersonalBusinessUnitId { get; set; }
        public DateOnly ReflectionLocalDate { get; set; }
        public List<TimeAllocationRequest>? TimeAllocations { get; set; }
        public string[]? AchievementsLines { get; set; }
        public string[]? NotDoneLines { get; set; }
        public string[]? BlockersLines { get; set; }
        public string? TomorrowReminderText { get; set; }
        public string? TomorrowReminderLocalTime { get; set; }
        public DateTime ClientSubmittedAtUtc { get; set; }
    }

    public class TimeAllocationRequest
    {
        public int OptionId { get; set; }
        public decimal Hours { get; set; }
    }

    // ── Suggestions ─────────────────────────────────────────────────────────

    /// <summary>Free-text product feedback.</summary>
    public class FeedbackSubmissionRequest
    {
        public uint BusinessUnitId { get; set; }

        // Nullable for the implicit-required reason described on
        // MemberEnrolmentRequest. The endpoint validates it explicitly instead,
        // which is where the old API rejected it too.
        public string? FeedbackText { get; set; }
        public DateTime ClientSubmittedAtUtc { get; set; }
    }

    // ── Profile registration ────────────────────────────────────────────────

    public class ProfileRegistrationRequest
    {
        // Nullable on purpose, and not to be tightened.
        //
        // [ApiController] treats a non-nullable reference property as implicitly
        // [Required], and that check runs in a filter ordered before the action.
        // A personal sign-up posts every one of these as null, so declaring them
        // non-nullable answers it with an ASP.NET ProblemDetails 400 the app
        // cannot read, and neither the Personal bypass below nor the validator
        // that produces MobileErrorResponse is ever reached. The old API
        // deserialised straight from the body with no model binder in front of
        // it, which is the behaviour these annotations restore.
        public string? BusinessUnitName { get; set; }

        // Still named ProfileName because the shipped app posts "profileName" and
        // reads the field back by that name. The value is stored in
        // Profiles.ProfileName. Renaming it is a breaking change for every
        // installed app, not a refactor.
        public string? ProfileName { get; set; }

        // CompanyCountryCode was removed: Profiles has no CountryCode column since
        // the Companies -> Profiles redesign, so the value had nowhere to go. An
        // older app still sending it is unaffected - the property is simply not
        // bound, and the request no longer fails for omitting it.
        public string? RegistrantEmailAddress { get; set; }
        public string? RegistrantFirstName { get; set; }
        public string? RegistrantLastName { get; set; }
    }

    public class ProfileRegistrationResponse
    {
        public required uint BusinessUnitId { get; init; }
        public required string BusinessUnitShortCode { get; init; }
        public required DateTime BusinessUnitRegistrationDate { get; init; }
    }

    // ── Authentication ──────────────────────────────────────────────────────

    public class SignInRequest
    {
        // Nullable for the same reason as the registration fields above: the
        // implicit-required check would answer a null with ProblemDetails
        // instead of the MobileErrorResponse the app parses.
        public string? SignInEmailAddress { get; set; }
    }

    // ── App update ──────────────────────────────────────────────────────────

    public class AppVersionResponse
    {
        public required string Version { get; init; }
        public required bool ForceUpdate { get; init; }
    }
}
