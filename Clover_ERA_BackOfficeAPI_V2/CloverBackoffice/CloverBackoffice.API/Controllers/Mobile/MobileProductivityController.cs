using CloverBackoffice.API.Filters;
using CloverBackoffice.Infrastructure.DTO.Mobile;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace CloverBackoffice.API.Controllers.Mobile
{
    /// <summary>
    /// Mobile app: the end-of-day productivity reflection, and free-text product
    /// suggestions.
    ///
    /// Both endpoints return 200 with no body on success, which is what the app
    /// expects. Contract, auth and response conventions as described on
    /// <see cref="MobileEnrolledMemberFeedbackController" />.
    /// </summary>
    [AllowAnonymous]
    [PersonalAppTokenAuthorize]
    [SkipTenantGuard]
    [Route("api/v1")]
    [ApiController]
    public class MobileProductivityController : ControllerBase
    {
        private readonly ILogger<MobileProductivityController> _logger;
        private readonly IMobileApiService _mobileApiService;

        public MobileProductivityController(
            IMobileApiService mobileApiService,
            ILogger<MobileProductivityController> logger)
        {
            _mobileApiService = mobileApiService;
            _logger = logger;
        }

        /// <summary>
        /// Stores one day's reflection: where the hours went, what was achieved, what
        /// was not, what blocked it, and the reminder for tomorrow.
        /// </summary>
        [HttpPost("productivity/submit")]
        public async Task<IActionResult> SubmitProductivity([FromBody] ProductivitySubmissionRequest? request)
        {
            if (request == null)
            {
                return BadRequest(new MobileErrorResponse(MobileApiMessages.RequestBody_Invalid));
            }

            if (request.BusinessUnitId == 0)
            {
                var error = new MobileErrorResponse(MobileApiMessages.Request_Invalid);
                error.Errors.Add(new MobileErrorFieldItem
                {
                    FieldName = nameof(request.BusinessUnitId),
                    Message = MobileApiMessages.BusinessUnitId_NotPositive
                });

                return BadRequest(error);
            }

            try
            {
                await _mobileApiService.SubmitProductivityAsync(request);

                return Ok();
            }
            catch (Exception ex)
            {
                // The old API swallowed this and still returned 200. That is not
                // reproduced: a reflection the member typed and lost, reported as
                // success, is worse than an honest failure the app can retry.
                _logger.LogError(ex, "Mobile productivity/submit failed for unit {UnitId}.", request.BusinessUnitId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Stores free-text product feedback. Unrelated to daily feedback — this is
        /// the "tell us what you think of the app" box.
        /// </summary>
        [HttpPost("feedback/suggestions")]
        public async Task<IActionResult> SubmitSuggestion([FromBody] FeedbackSubmissionRequest? request)
        {
            if (request == null)
            {
                return BadRequest(new MobileErrorResponse(MobileApiMessages.RequestBody_Invalid));
            }

            // Both rules the old validator declared, evaluated together and reported
            // in declaration order — it used FluentValidation's default Continue
            // cascade, so a request that breaks both gets both back in one response.
            var validator = new MobileRequestValidator();

            if (request.BusinessUnitId == 0)
            {
                validator.Add(nameof(request.BusinessUnitId), MobileApiMessages.BusinessUnitId_NotPositive);
            }

            validator.For(nameof(request.FeedbackText), request.FeedbackText)
                .NotEmpty(MobileApiMessages.FeedbackText_Missing);

            // Added directly rather than through the fluent chain because the old
            // message quotes the length that was actually submitted, which the chain
            // has no way to interpolate.
            if (request.FeedbackText != null
                && request.FeedbackText.Length > MobileApiConstants.SuggestionFeedbackTextLength)
            {
                validator.Add(
                    nameof(request.FeedbackText),
                    string.Format(
                        MobileApiMessages.SuggestionFeedbackText_TooLong,
                        MobileApiConstants.SuggestionFeedbackTextLength,
                        request.FeedbackText.Length));
            }

            if (!validator.IsValid)
            {
                return BadRequest(validator.ToErrorResponse());
            }

            try
            {
                await _mobileApiService.SubmitSuggestionAsync(request);

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mobile feedback/suggestions failed for unit {UnitId}.", request.BusinessUnitId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
