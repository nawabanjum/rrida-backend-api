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
    /// Mobile app: company sign-up, and the app-update and sign-in endpoints that sit
    /// alongside it.
    ///
    /// Contract, auth and response conventions as described on
    /// <see cref="MobileEnrolledMemberFeedbackController" />.
    ///
    /// This is a separate path from the back office's own AccountRegistrationController.
    /// The two create overlapping data but answer to different contracts and different
    /// callers, and merging them would break one of the two.
    /// </summary>
    [AllowAnonymous]
    [PersonalAppTokenAuthorize]
    [SkipTenantGuard]
    [Route("api/v1")]
    [ApiController]
    public class MobileProfileRegistrationController : ControllerBase
    {
        private readonly ILogger<MobileProfileRegistrationController> _logger;
        private readonly IMobileApiService _mobileApiService;

        public MobileProfileRegistrationController(
            IMobileApiService mobileApiService,
            ILogger<MobileProfileRegistrationController> logger)
        {
            _mobileApiService = mobileApiService;
            _logger = logger;
        }

        /// <summary>
        /// Registers a profile and its first team, or attaches a team to a profile
        /// that already owns the registrant's email domain.
        ///
        /// Every request is validated. The personal sign-up route, which skipped
        /// validation and filed the team under one designated profile, has been
        /// removed along with the rest of the Clovera Personal App behaviour.
        /// </summary>
        [HttpPost("profile-registration/register-profile")]
        public async Task<IActionResult> RegisterCompany([FromBody] ProfileRegistrationRequest? request)
        {
            if (request == null)
            {
                return BadRequest(new MobileErrorResponse(MobileApiMessages.RequestBody_Invalid));
            }

            var validator = BuildValidator(request);

            if (!validator.IsValid)
            {
                return BadRequest(validator.ToErrorResponse());
            }

            try
            {
                var result = await _mobileApiService.RegisterProfileAsync(request);

                // The company name is taken by a different email domain. Reported
                // against both fields, because either one could be the mistake.
                if (result.Conflict)
                {
                    var conflict = new MobileErrorResponse(MobileApiMessages.Request_Invalid);
                    conflict.Errors.Add(new MobileErrorFieldItem
                    {
                        FieldName = nameof(request.ProfileName),
                        Message = MobileApiMessages.ProfileNameConflict
                    });
                    conflict.Errors.Add(new MobileErrorFieldItem
                    {
                        FieldName = nameof(request.RegistrantEmailAddress),
                        Message = MobileApiMessages.RegistrantEmailAddressConflict
                    });

                    return BadRequest(conflict);
                }

                if (!result.IsSuccess || result.Registration == null)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError);
                }

                return Ok(result.Registration);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mobile register-company failed for {Email}.", request.RegistrantEmailAddress);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// The minimum app version per platform, and whether the update is mandatory.
        ///
        /// Values come from configuration (<c>MobileAppUpdate:AndroidVersion</c> /
        /// <c>IosVersion</c> / <c>ForceUpdate</c>), falling back to what the old API
        /// had hardcoded. Configurable because shipping a new build of the whole API
        /// to bump a version string is not a reasonable release process.
        /// </summary>
        [HttpGet("app-update/get-version")]
        public IActionResult GetVersion([FromQuery] string? platform, [FromServices] IConfiguration configuration)
        {
            if (string.IsNullOrWhiteSpace(platform))
            {
                return BadRequest(new MobileErrorResponse(
                    "Query parameter 'platform' is required (android | ios)"));
            }

            var version = platform.Trim().ToLowerInvariant() switch
            {
                "android" => configuration["MobileAppUpdate:AndroidVersion"] ?? "1.0.34",
                "ios" => configuration["MobileAppUpdate:IosVersion"] ?? "1.0.43",
                _ => null
            };

            if (version == null)
            {
                return BadRequest(new MobileErrorResponse(
                    "Invalid platform. Allowed values: android, ios"));
            }

            return Ok(new AppVersionResponse
            {
                Version = version,
                ForceUpdate = configuration.GetValue<bool?>("MobileAppUpdate:ForceUpdate") ?? true
            });
        }

        /// <summary>
        /// Back office sign-in as the mobile API exposed it.
        ///
        /// The old implementation validated the address and then returned 500
        /// unconditionally — it was never finished. That behaviour is reproduced
        /// rather than invented, because any real response here would be a new
        /// contract the app has never seen. The back office's own login lives at
        /// api/company/login and is unaffected.
        /// </summary>
        [HttpPost("authentication/sign-in")]
        public IActionResult SignIn([FromBody] SignInRequest? request)
        {
            if (request == null)
            {
                return BadRequest(new MobileErrorResponse(MobileApiMessages.RequestBody_Invalid));
            }

            var validator = new MobileRequestValidator();
            validator.For(nameof(request.SignInEmailAddress), request.SignInEmailAddress)
                .NotEmpty(MobileApiMessages.SignInEmailAddress_Missing)
                .MaximumLength(
                    MobileApiConstants.EmailAddressLength,
                    string.Format(
                        MobileApiMessages.SignInEmailAddress_TooLong,
                        MobileApiConstants.EmailAddressLength))
                .EmailAddress(MobileApiMessages.SignInEmailAddress_Invalid)
                .NotFreeEmailAddress(MobileApiMessages.SignInEmailAddress_Free);

            if (!validator.IsValid)
            {
                return BadRequest(validator.ToErrorResponse());
            }

            _logger.LogWarning(
                "Mobile sign-in was called for {Email}, but this endpoint was never implemented upstream.",
                request.SignInEmailAddress);

            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        /// <summary>
        /// The registration rules, in the order the old API declared them. Order
        /// matters: the app renders the returned error list as it arrives.
        /// </summary>
        private static MobileRequestValidator BuildValidator(ProfileRegistrationRequest request)
        {
            var validator = new MobileRequestValidator();

            validator.For(nameof(request.BusinessUnitName), request.BusinessUnitName)
                .NotEmpty(MobileApiMessages.BusinessUnitName_Missing)
                .MaximumLength(
                    MobileApiConstants.OrganisationalUnitNameLength,
                    string.Format(
                        MobileApiMessages.BusinessUnitName_TooLong,
                        MobileApiConstants.OrganisationalUnitNameLength));

            // CompanyCountryCode is no longer validated: Profiles has no CountryCode
            // column to store it in. Dropping the rule only ever turns a 400 into a
            // success, so an app that still sends the field is unaffected.

            validator.For(nameof(request.ProfileName), request.ProfileName)
                .NotEmpty(MobileApiMessages.ProfileName_Missing)
                .MaximumLength(
                    MobileApiConstants.OrganisationalUnitNameLength,
                    string.Format(
                        MobileApiMessages.ProfileName_TooLong,
                        MobileApiConstants.OrganisationalUnitNameLength));

            validator.For(nameof(request.RegistrantEmailAddress), request.RegistrantEmailAddress)
                .NotEmpty(MobileApiMessages.RegistrantEmailAddress_Missing)
                .MaximumLength(
                    MobileApiConstants.EmailAddressLength,
                    string.Format(
                        MobileApiMessages.RegistrantEmailAddress_TooLong,
                        MobileApiConstants.EmailAddressLength))
                .EmailAddress(MobileApiMessages.RegistrantEmailAddress_Invalid)
                .NotFreeEmailAddress(MobileApiMessages.RegistrantEmailAddress_Free);

            validator.For(nameof(request.RegistrantFirstName), request.RegistrantFirstName)
                .NotEmpty(MobileApiMessages.RegistrantFirstName_Missing)
                .MaximumLength(
                    MobileApiConstants.PersonalNameLength,
                    string.Format(
                        MobileApiMessages.RegistrantFirstName_TooLong,
                        MobileApiConstants.PersonalNameLength));

            validator.For(nameof(request.RegistrantLastName), request.RegistrantLastName)
                .NotEmpty(MobileApiMessages.RegistrantLastName_Missing)
                .MaximumLength(
                    MobileApiConstants.PersonalNameLength,
                    string.Format(
                        MobileApiMessages.RegistrantLastName_TooLong,
                        MobileApiConstants.PersonalNameLength));

            return validator;
        }
    }
}
