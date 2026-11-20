using CloverBackoffice.Infrastructure.DTO.Mobile;
using CloverBackoffice.Repository.Interfaces;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// The mobile app's operations. Mirrors <see cref="IMobileApiRepository" />, and
    /// adds the one piece of behaviour that is not data access: sending the
    /// registration emails after a company signs up.
    /// </summary>
    public interface IMobileApiService
    {
        Task<MobileQuestionResult> GetDailyQuestionAsync(uint businessUnitId);

        Task<MobileQuestionListResult> GetNextDailyQuestionsAsync(uint businessUnitId);

        /// <summary>
        /// Stores the submission. Returns the repository result rather than the app's
        /// response shape — as <see cref="GetDailyQuestionAsync" /> already does — because
        /// the outcome can be a refusal the controller has to turn into a 400, not only a
        /// pair of micro-actions.
        /// </summary>
        Task<MobileFeedbackResult> SubmitDailyFeedbackAsync(DailyFeedbackSubmissionRequest request);

        Task<MemberEnrolmentResponse?> EnrollMemberAsync(string businessUnitShortCode);

        Task SubmitProductivityAsync(ProductivitySubmissionRequest request);

        Task SubmitSuggestionAsync(FeedbackSubmissionRequest request);

        Task<bool> SubmitTeamFeedbackAsync(TeamFeedbackSubmissionRequest request);

        /// <summary>
        /// Registers the company and, for a non-personal sign-up, sends the welcome and
        /// share-code emails. A failure to send does not fail the registration.
        /// </summary>
        Task<MobileRegistrationResult> RegisterProfileAsync(ProfileRegistrationRequest request);
    }
}
