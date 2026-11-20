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
    /// Mobile app: joining a team with the short code printed on the invite.
    ///
    /// Contract, auth and response conventions as described on
    /// <see cref="MobileEnrolledMemberFeedbackController" />.
    /// </summary>
    [AllowAnonymous]
    [PersonalAppTokenAuthorize]
    [SkipTenantGuard]
    [Route("api/v1/member-enrolment")]
    [ApiController]
    public class MobileMemberEnrolmentController : ControllerBase
    {
        private readonly ILogger<MobileMemberEnrolmentController> _logger;
        private readonly IMobileApiService _mobileApiService;

        public MobileMemberEnrolmentController(
            IMobileApiService mobileApiService,
            ILogger<MobileMemberEnrolmentController> logger)
        {
            _mobileApiService = mobileApiService;
            _logger = logger;
        }

        /// <summary>
        /// Exchanges a team short code for the team id the app then uses on every
        /// other call. This is the app's whole notion of "signing in" — there is no
        /// user account behind it.
        /// </summary>
        [HttpPost("enroll-member")]
        public async Task<IActionResult> EnrollMember([FromBody] MemberEnrolmentRequest? request)
        {
            if (request == null)
            {
                return BadRequest(new MobileErrorResponse(MobileApiMessages.RequestBody_Invalid));
            }

            var validator = new MobileRequestValidator();
            validator.For(nameof(request.BusinessUnitShortCode), request.BusinessUnitShortCode)
                .NotEmpty(MobileApiMessages.BusinessUnitName_Missing)
                .ExactLength(
                    MobileApiConstants.ShortCodeLength,
                    string.Format(
                        MobileApiMessages.BusinessUnitShortCode_Length,
                        MobileApiConstants.ShortCodeLength))
                .Matches(MobileApiConstants.ShortCodeRegex, MobileApiMessages.BusinessUnitShortCode_Invalid);

            if (!validator.IsValid)
            {
                return BadRequest(validator.ToErrorResponse());
            }

            try
            {
                // Non-null past the validator above, which rejects null, empty and
                // whitespace before this line is reached.
                var response = await _mobileApiService.EnrollMemberAsync(request.BusinessUnitShortCode!);

                if (response == null)
                {
                    var error = new MobileErrorResponse(MobileApiMessages.Request_Invalid);
                    error.Errors.Add(new MobileErrorFieldItem
                    {
                        FieldName = nameof(request.BusinessUnitShortCode),
                        Message = MobileApiMessages.BusinessUnit_NotFound
                    });

                    return BadRequest(error);
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mobile enroll-member failed for short code {ShortCode}.",
                    request.BusinessUnitShortCode);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
