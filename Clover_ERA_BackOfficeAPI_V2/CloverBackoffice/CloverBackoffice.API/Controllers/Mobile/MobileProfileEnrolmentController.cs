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
    /// Mobile app: enrolling with a profile name and a team code.
    ///
    /// Contract, auth and response conventions as described on
    /// <see cref="MobileEnrolledMemberFeedbackController" />.
    /// </summary>
    [AllowAnonymous]
    [PersonalAppTokenAuthorize]
    [SkipTenantGuard]
    [Route("api/v1/profile")]
    [ApiController]
    public class MobileProfileEnrolmentController : ControllerBase
    {
        private readonly ILogger<MobileProfileEnrolmentController> _logger;
        private readonly IMobileApiService _mobileApiService;

        public MobileProfileEnrolmentController(
            IMobileApiService mobileApiService,
            ILogger<MobileProfileEnrolmentController> logger)
        {
            _mobileApiService = mobileApiService;
            _logger = logger;
        }

        /// <summary>
        /// Validates the team code and, when it names a team, answers with exactly what
        /// fetch-daily-question answers for that team: today's question and reasons,
        /// plus the team question and custom question when there are any — with the
        /// team's businessUnitId and businessUnitRegistrationDate added.
        ///
        /// The team code is looked up the way enroll-member looks it up, and the
        /// question is built by the same service call fetch-daily-question makes, so the
        /// two endpoints cannot drift apart. Nothing is written.
        /// </summary>
        [HttpPost("enroll")]
        public async Task<IActionResult> Enroll([FromBody] ProfileEnrolmentRequest? request)
        {
            if (request == null)
            {
                return BadRequest(new MobileErrorResponse(MobileApiMessages.RequestBody_Invalid));
            }

            var validator = new MobileRequestValidator();

            validator.For(nameof(request.ProfileName), request.ProfileName)
                .NotEmpty(MobileApiMessages.ProfileName_Missing)
                .MaximumLength(
                    MobileApiConstants.OrganisationalUnitNameLength,
                    string.Format(
                        MobileApiMessages.ProfileName_TooLong,
                        MobileApiConstants.OrganisationalUnitNameLength));

            // Same format rule as enroll-member, so a code that works there works here.
            validator.For(nameof(request.TeamCode), request.TeamCode)
                .NotEmpty(MobileApiMessages.TeamCode_Missing)
                .ExactLength(
                    MobileApiConstants.ShortCodeLength,
                    string.Format(MobileApiMessages.TeamCode_Length, MobileApiConstants.ShortCodeLength))
                .Matches(MobileApiConstants.ShortCodeRegex, MobileApiMessages.TeamCode_Invalid);

            if (!validator.IsValid)
            {
                return BadRequest(validator.ToErrorResponse());
            }

            try
            {
                // Non-null past the validator above, which rejects null, empty and
                // whitespace before this line is reached.
                var team = await _mobileApiService.EnrollMemberAsync(request.TeamCode!);

                if (team == null)
                {
                    return BadRequest(FieldError(nameof(request.TeamCode), MobileApiMessages.TeamCode_NotFound));
                }

                var result = await _mobileApiService.GetDailyQuestionAsync(team.BusinessUnitId);

                if (!result.BusinessUnitFound)
                {
                    return BadRequest(FieldError(nameof(request.TeamCode), MobileApiMessages.TeamCode_NotFound));
                }

                // Same answer fetch-daily-question gives when the rotation has nothing
                // to serve the team.
                if (!result.QuestionAvailable || result.Question == null)
                {
                    return BadRequest(new MobileErrorResponse(MobileApiMessages.Request_Invalid));
                }

                // fetch-daily-question's body, plus the team's id and registration date.
                return Ok(ProfileEnrolmentResponse.From(result.Question, team));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mobile profile/enroll failed for team code {TeamCode}.", request.TeamCode);
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
