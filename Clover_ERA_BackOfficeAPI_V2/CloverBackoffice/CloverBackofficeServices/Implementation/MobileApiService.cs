using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.DTO.Mobile;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// The mobile app's operations.
    ///
    /// Mostly a pass-through to <see cref="IMobileApiRepository" />. The exception is
    /// registration, which also sends the two SendGrid template emails the old API
    /// sent — the welcome message and the share-code message.
    /// </summary>
    public class MobileApiService : IMobileApiService
    {
        // SendGrid dynamic template ids, carried over from the mobile API. They are
        // properties of the SendGrid account, not of either codebase, so they are the
        // same ids the app's users have been receiving.
        private const string RegistrationSuccessTemplateId = "d-c10ee8fab94042f08a75afedc57e3372";
        private const string ShareCodeTemplateId = "d-c165d181554148df87f80a17d3d88741";

        /// <summary>
        /// Trial length for a mobile sign-up, in days. Configurable, defaulting to the
        /// value the old API shipped with.
        /// </summary>
        private const ushort DefaultTrialPeriodDays = 30;

        private readonly IConfiguration _configuration;
        private readonly ILogger<MobileApiService> _logger;
        private readonly IMobileApiRepository _repository;

        public MobileApiService(
            IMobileApiRepository repository,
            IConfiguration configuration,
            ILogger<MobileApiService> logger)
        {
            _repository = repository;
            _configuration = configuration;
            _logger = logger;
        }

        public Task<MobileQuestionResult> GetDailyQuestionAsync(uint businessUnitId) =>
            _repository.GetDailyQuestionAsync(businessUnitId);

        public Task<MobileQuestionListResult> GetNextDailyQuestionsAsync(uint businessUnitId) =>
            _repository.GetNextDailyQuestionsAsync(businessUnitId);

        public Task<MobileFeedbackResult> SubmitDailyFeedbackAsync(DailyFeedbackSubmissionRequest request) =>
            _repository.SubmitDailyFeedbackAsync(request);

        public Task<MemberEnrolmentResponse?> EnrollMemberAsync(string businessUnitShortCode) =>
            _repository.EnrollMemberAsync(businessUnitShortCode);

        public Task SubmitProductivityAsync(ProductivitySubmissionRequest request) =>
            _repository.SubmitProductivityAsync(request);

        public Task SubmitSuggestionAsync(FeedbackSubmissionRequest request) =>
            _repository.SubmitSuggestionAsync(request);

        public Task<bool> SubmitTeamFeedbackAsync(TeamFeedbackSubmissionRequest request) =>
            _repository.SubmitTeamFeedbackAsync(request);

        public async Task<MobileRegistrationResult> RegisterProfileAsync(ProfileRegistrationRequest request)
        {
            // ProfileRegistration is the current key; CompanyRegistration is still
            // honoured so an environment that set the old name keeps its value.
            var trialPeriodDays = _configuration.GetValue<ushort?>("ProfileRegistration:TrialPeriodDays")
                                  ?? _configuration.GetValue<ushort?>("CompanyRegistration:TrialPeriodDays")
                                  ?? DefaultTrialPeriodDays;

            var result = await _repository.RegisterProfileAsync(request, trialPeriodDays);

            if (!result.IsSuccess || result.Registration == null)
            {
                return result;
            }

            await SendRegistrationEmailsAsync(request, result.Registration.BusinessUnitShortCode);

            return result;
        }

        /// <summary>
        /// Sends the welcome and share-code emails.
        ///
        /// Failures are logged and swallowed on purpose: the company, team and trial
        /// are already committed by this point, and the app treats a non-200 as "sign
        /// up failed". Turning an SMTP outage into a failed registration would leave a
        /// real company in the database that the member is told does not exist.
        /// </summary>
        private async Task SendRegistrationEmailsAsync(ProfileRegistrationRequest request, string shortCode)
        {
            var registrantEmail = request.RegistrantEmailAddress ?? string.Empty;

            try
            {
                var settings = _configuration.GetSection("SendGrid").Get<SendGridSettings>();
                var apiKey = _configuration["AzureKeyVault:SendGridApiKey"] ?? settings?.ApiKey;

                if (settings == null || string.IsNullOrWhiteSpace(apiKey))
                {
                    _logger.LogWarning(
                        "Mobile registration for {Email} completed but no SendGrid configuration is present, so no email was sent.",
                        request.RegistrantEmailAddress);
                    return;
                }

                // Presented as XXXX-XXXX, which is how it is printed and how members
                // read it out to each other.
                var upper = shortCode.ToUpperInvariant();
                var formatted = upper.Length >= MobileApiConstants.ShortCodeLength
                    ? $"{upper[..4]}-{upper[4..]}"
                    : upper;

                await EmailHelper.SendDynamicTemplateEmailAsync(
                    apiKey,
                    settings.FromEmail,
                    settings.FromName,
                    registrantEmail,
                    RegistrationSuccessTemplateId,
                    new Dictionary<string, object>
                    {
                        ["RegistrantFirstName"] = request.RegistrantFirstName ?? string.Empty,
                        ["BusinessUnitShortCode"] = formatted
                    });

                await EmailHelper.SendDynamicTemplateEmailAsync(
                    apiKey,
                    settings.FromEmail,
                    settings.FromName,
                    registrantEmail,
                    ShareCodeTemplateId,
                    new Dictionary<string, object>
                    {
                        ["BusinessUnitShortCode"] = formatted
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Mobile registration for {Email} completed but the notification emails could not be sent.",
                    request.RegistrantEmailAddress);
            }
        }
    }
}
