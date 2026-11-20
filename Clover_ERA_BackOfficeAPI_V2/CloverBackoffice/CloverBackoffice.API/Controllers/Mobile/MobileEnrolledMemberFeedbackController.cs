using CloverBackoffice.API.Filters;
using CloverBackoffice.Infrastructure.DTO.Mobile;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloverBackoffice.API.Controllers.Mobile
{
    // ═════════════════════════════════════════════════════════════════════════
    // Mobile app: daily questions and the answers to them.
    //
    // These endpoints replace the Azure Functions API the app calls today, and the
    // app is not being rebuilt, so the contract is fixed. Three house conventions
    // are deliberately NOT followed here:
    //
    //   * Responses are returned bare, not through ApiResponseHandler. The app
    //     reads the object directly and would not find its fields inside a
    //     { isSuccess, message, content } envelope.
    //   * Failures return real status codes — 400 with the app's own error shape,
    //     500 on an unexpected fault — rather than 200-with-IsSuccess-false.
    //   * The route keeps the old API's v1 path so the mobile developer only has
    //     to change the host name.
    //
    // Auth is the static mobile token this project already uses for its other
    // app-facing endpoints (CloveraPersonalApp:ApiToken), replacing the Azure
    // Functions key. See MobileApiRouteNotes for what that means for the app.
    // ═════════════════════════════════════════════════════════════════════════

    [AllowAnonymous]
    [PersonalAppTokenAuthorize]
    [SkipTenantGuard]
    [Route("api/v1/enrolled-member-feedback")]
    [ApiController]
    public class MobileEnrolledMemberFeedbackController : ControllerBase
    {
        private readonly ILogger<MobileEnrolledMemberFeedbackController> _logger;
        private readonly IMobileApiService _mobileApiService;

        public MobileEnrolledMemberFeedbackController(
            IMobileApiService mobileApiService,
            ILogger<MobileEnrolledMemberFeedbackController> logger)
        {
            _mobileApiService = mobileApiService;
            _logger = logger;
        }

        /// <summary>
        /// Today's question for a team, with its selectable reasons.
        ///
        /// The question is chosen by the calendar alone — see
        /// MobileApiRepository.ResolveOrdinal. Every team on the same question set
        /// group sees the same question on the same day.
        ///
        /// The team question and the company's own custom question for today ride along
        /// on the response when there are any. Neither can fail the call: a team bank
        /// with nothing to serve, or a team with no custom question scheduled, comes
        /// back as a null property beside an otherwise unchanged response.
        /// </summary>
        [HttpPost("fetch-daily-question")]
        public async Task<IActionResult> FetchDailyQuestion([FromBody] DailyQuestionFetchRequest? request)
        {
            if (request == null)
            {
                return BadRequest(new MobileErrorResponse(MobileApiMessages.RequestBody_Invalid));
            }

            try
            {
                var result = await _mobileApiService.GetDailyQuestionAsync(request.BusinessUnitId);

                if (!result.BusinessUnitFound)
                {
                    return BadRequest(FieldError(
                        nameof(request.BusinessUnitId),
                        MobileApiMessages.BusinessUnit_NotFound));
                }

                // The unit is real but the rotation has nothing to serve it. The old
                // API raised a validation error with no field attached here, so the
                // body carries the generic message and an empty error list.
                if (!result.QuestionAvailable || result.Question == null)
                {
                    return BadRequest(new MobileErrorResponse(MobileApiMessages.Request_Invalid));
                }

                return Ok(result.Question);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mobile fetch-daily-question failed for unit {UnitId}.", request.BusinessUnitId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// The next five days of questions, starting tomorrow. Used by the app to
        /// pre-load, so it stays usable offline.
        /// </summary>
        [HttpPost("fetch-next-daily-questions")]
        public async Task<IActionResult> FetchNextDailyQuestions([FromBody] DailyQuestionFetchRequest? request)
        {
            if (request == null)
            {
                return BadRequest(new MobileErrorResponse(MobileApiMessages.RequestBody_Invalid));
            }

            try
            {
                var result = await _mobileApiService.GetNextDailyQuestionsAsync(request.BusinessUnitId);

                if (!result.BusinessUnitFound)
                {
                    return BadRequest(FieldError(
                        nameof(request.BusinessUnitId),
                        MobileApiMessages.BusinessUnit_NotFound));
                }

                if (!result.QuestionAvailable)
                {
                    return BadRequest(new MobileErrorResponse(MobileApiMessages.Request_Invalid));
                }

                // A bare array, which is what the app parses. An empty rotation yields
                // [] rather than an error, matching the old behaviour.
                return Ok(result.Questions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mobile fetch-next-daily-questions failed for unit {UnitId}.", request.BusinessUnitId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Records one answer and returns the micro-action to show afterwards.
        ///
        /// A reason with no micro-action behind it returns an empty string, not an
        /// error: the answer is saved either way, and the nudge is a bonus.
        /// </summary>
        [HttpPost("submit-daily-feedback")]
        public async Task<IActionResult> SubmitDailyFeedback([FromBody] DailyFeedbackSubmissionRequest? request)
        {
            if (request == null)
            {
                return BadRequest(new MobileErrorResponse(MobileApiMessages.RequestBody_Invalid));
            }

            // The only rule the old API applied here. Length is capped rather than
            // truncated, so an over-long note is reported instead of silently cut.
            var validator = new MobileRequestValidator();
            validator.For(nameof(request.FeedbackFreeText), request.FeedbackFreeText)
                .MaximumLength(
                    MobileApiConstants.FeedbackFreeTextLength,
                    string.Format(
                        MobileApiMessages.FeedbackFreeText_TooLong,
                        MobileApiConstants.FeedbackFreeTextLength));

            // The team half gets the same cap as the daily half, and only when a team
            // answer is actually being submitted. A request from the shipped app leaves
            // these null and picks up no new rules.
            validator.For(nameof(request.TeamFeedbackFreeText), request.TeamFeedbackFreeText)
                .MaximumLength(
                    MobileApiConstants.TeamFeedbackFreeTextLength,
                    string.Format(
                        MobileApiMessages.FeedbackFreeText_TooLong,
                        MobileApiConstants.TeamFeedbackFreeTextLength));

            // The custom answer's own cap, which is 300 rather than 896: it is a
            // different column with a much shorter limit. Not applied to a skip: a
            // skipped answer's text is ignored, and a skip must never fail the check-in.
            if (!request.CustomFeedbackSkipped)
            {
                validator.For(nameof(request.CustomFeedbackFreeText), request.CustomFeedbackFreeText)
                    .MaximumLength(
                        MobileApiConstants.CustomFeedbackFreeTextLength,
                        string.Format(
                            MobileApiMessages.FeedbackFreeText_TooLong,
                            MobileApiConstants.CustomFeedbackFreeTextLength));
            }

            // The personal answer needs at least one selected reason (dailyReasonIds,
            // or the legacy dailyReasonId) or the member's own reason text.
            if (request.DailyQuestionId > 0
                && request.SelectedDailyReasonIds().Count == 0
                && string.IsNullOrWhiteSpace(request.FeedbackFreeText))
            {
                return BadRequest(FieldError(
                    nameof(request.DailyReasonIds),
                    MobileApiMessages.DailyReason_Required));
            }

            // A team question with no reason cannot be stored — TeamDailyReasonId is a
            // non-null foreign key. Reported as a field error rather than left to fail
            // as a 500 on the insert. Satisfied by teamDailyReasonIds or the legacy id.
            if (request.TeamDailyQuestionId is > 0 && request.SelectedTeamDailyReasonIds().Count == 0)
            {
                return BadRequest(FieldError(
                    nameof(request.TeamDailyReasonIds),
                    MobileApiMessages.TeamDailyReason_Required));
            }

            if (!validator.IsValid)
            {
                return BadRequest(validator.ToErrorResponse());
            }

            try
            {
                var result = await _mobileApiService.SubmitDailyFeedbackAsync(request);

                // Nothing was written when this is set, so the member is told to re-fetch
                // rather than left believing a check-in was recorded that was not.
                if (result.CustomQuestionError != null)
                {
                    return BadRequest(FieldError(
                        nameof(request.CustomDailyQuestionId),
                        result.CustomQuestionError));
                }

                if (result.ReasonError != null)
                {
                    return BadRequest(FieldError(
                        result.ReasonErrorField ?? nameof(request.DailyReasonIds),
                        result.ReasonError));
                }

                return Ok(new DailyFeedbackSubmissionResponse
                {
                    SuggestedMicroAction = result.SuggestedMicroAction,
                    TeamSuggestedMicroAction = result.TeamSuggestedMicroAction
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mobile submit-daily-feedback failed for unit {UnitId}.", request.BusinessUnitId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Optional "Any other feedback about your team?" from the check-in wrap-up
        /// screen. Anonymous, like daily feedback, and independent of the daily
        /// submit: it may be sent any number of times a day.
        /// </summary>
        [HttpPost("submit-team-feedback")]
        public async Task<IActionResult> SubmitTeamFeedback([FromBody] TeamFeedbackSubmissionRequest? request)
        {
            if (request == null)
            {
                return BadRequest(new MobileErrorResponse(MobileApiMessages.RequestBody_Invalid));
            }

            var validator = new MobileRequestValidator();

            if (request.BusinessUnitId == 0)
            {
                validator.Add(nameof(request.BusinessUnitId), MobileApiMessages.BusinessUnitId_NotPositive);
            }

            if (string.IsNullOrWhiteSpace(request.FeedbackText))
            {
                validator.Add(nameof(request.FeedbackText), MobileApiMessages.TeamFeedbackText_Missing);
            }
            else if (request.FeedbackText.Trim().Length > MobileApiConstants.TeamFeedbackTextLength)
            {
                validator.Add(
                    nameof(request.FeedbackText),
                    string.Format(
                        MobileApiMessages.TeamFeedbackText_TooLong,
                        MobileApiConstants.TeamFeedbackTextLength));
            }

            if (request.ClientSubmittedAtUtc == null || request.ClientSubmittedAtUtc == default(DateTime))
            {
                validator.Add(nameof(request.ClientSubmittedAtUtc), MobileApiMessages.ClientSubmittedAtUtc_Missing);
            }

            if (!validator.IsValid)
            {
                return BadRequest(validator.ToErrorResponse());
            }

            try
            {
                var saved = await _mobileApiService.SubmitTeamFeedbackAsync(request);

                if (!saved)
                {
                    return BadRequest(FieldError(
                        nameof(request.BusinessUnitId),
                        MobileApiMessages.BusinessUnit_NotFound));
                }

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mobile submit-team-feedback failed for unit {UnitId}.", request.BusinessUnitId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        private static MobileErrorResponse FieldError(string fieldName, string message)
        {
            var response = new MobileErrorResponse(MobileApiMessages.Request_Invalid);
            response.Errors.Add(new MobileErrorFieldItem { FieldName = fieldName, Message = message });

            return response;
        }
    }
}
