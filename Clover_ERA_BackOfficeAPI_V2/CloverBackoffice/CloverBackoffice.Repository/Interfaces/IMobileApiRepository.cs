using CloverBackoffice.Infrastructure.DTO.Mobile;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    /// <summary>
    /// Data access for the endpoints the mobile app calls.
    ///
    /// Kept as one interface because the app's surface is one cohesive flow — join a
    /// team, get today's question, answer it, log the day — and splitting it across
    /// four repositories would spread that flow over four files without giving any
    /// of them an independent reason to change.
    ///
    /// Every method returns a result object rather than throwing for the expected
    /// failures (unknown team, unknown short code, conflicting company), because each
    /// of those maps to a specific 400 body the app parses.
    /// </summary>
    public interface IMobileApiRepository
    {
        /// <summary>
        /// Today's question for a team. Null unit means the id was not found; null
        /// question means the rotation had nothing to serve.
        /// </summary>
        Task<MobileQuestionResult> GetDailyQuestionAsync(uint businessUnitId);

        /// <summary>
        /// The next five days of questions, starting tomorrow. Days the rotation
        /// cannot fill are skipped rather than returned empty.
        /// </summary>
        Task<MobileQuestionListResult> GetNextDailyQuestionsAsync(uint businessUnitId);

        /// <summary>
        /// Stores the daily answer and, when the request carries them, the team and
        /// custom answers alongside it — all under one commit, so the set is never
        /// half-recorded. Returns the micro-action for each chosen reason, or empty when
        /// that reason has none.
        ///
        /// A custom question that is not the submitting unit's, or whose day has passed,
        /// is refused before anything is written and reported on
        /// <see cref="MobileFeedbackResult.CustomQuestionError" />.
        /// </summary>
        Task<MobileFeedbackResult> SubmitDailyFeedbackAsync(DailyFeedbackSubmissionRequest request);

        /// <summary>Resolves a printed team short code. Null when it matches nothing.</summary>
        Task<MemberEnrolmentResponse?> EnrollMemberAsync(string businessUnitShortCode);

        /// <summary>Stores one day's productivity reflection and its time allocations.</summary>
        Task SubmitProductivityAsync(ProductivitySubmissionRequest request);

        /// <summary>Stores one free-text product suggestion.</summary>
        Task SubmitSuggestionAsync(FeedbackSubmissionRequest request);

        /// <summary>
        /// Stores optional wrap-up team feedback. False, with nothing written, when the
        /// business unit does not exist.
        /// </summary>
        Task<bool> SubmitTeamFeedbackAsync(TeamFeedbackSubmissionRequest request);

        /// <summary>
        /// Registers a company, or attaches a team to one that already exists.
        /// </summary>
        Task<MobileRegistrationResult> RegisterProfileAsync(ProfileRegistrationRequest request, ushort trialPeriodDays);
    }

    /// <summary>
    /// The nudges to show after a submission. The daily one is always present, empty
    /// when its reason has no micro-action; the team one is null when no team answer
    /// was submitted, so "not answered" stays distinguishable from "no nudge".
    /// </summary>
    public sealed class MobileFeedbackResult
    {
        public string SuggestedMicroAction { get; init; } = string.Empty;

        public string? TeamSuggestedMicroAction { get; init; }

        /// <summary>
        /// Why the custom question was refused, or null when one was accepted or none was
        /// submitted. Set means nothing at all was written — the submission is rejected
        /// whole rather than saved with the custom third of it dropped.
        /// </summary>
        public string? CustomQuestionError { get; init; }

        /// <summary>
        /// Why the selected reasons were refused (an id that does not exist), with the
        /// request field it belongs to. Set means nothing was written.
        /// </summary>
        public string? ReasonError { get; init; }

        public string? ReasonErrorField { get; init; }
    }

    /// <summary>Outcome of a single-question fetch.</summary>
    public sealed class MobileQuestionResult
    {
        public bool BusinessUnitFound { get; init; }
        public bool QuestionAvailable { get; init; }
        public DailyQuestionFetchResponse? Question { get; init; }
    }

    /// <summary>Outcome of a multi-day fetch.</summary>
    public sealed class MobileQuestionListResult
    {
        public bool BusinessUnitFound { get; init; }
        public bool QuestionAvailable { get; init; }
        public List<DailyQuestionFetchResponse> Questions { get; init; } = new();
    }

    /// <summary>
    /// Outcome of a registration. <see cref="Conflict" /> is the specific case where
    /// the company name is taken by a different email domain, which the old API
    /// reported as two field errors at once.
    /// </summary>
    public sealed class MobileRegistrationResult
    {
        public bool IsSuccess { get; init; }
        public bool Conflict { get; init; }
        public ProfileRegistrationResponse? Registration { get; init; }
    }
}
