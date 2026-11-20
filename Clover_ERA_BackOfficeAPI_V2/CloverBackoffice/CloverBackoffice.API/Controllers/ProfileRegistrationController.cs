using CloverBackoffice.Components;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    /// <summary>
    /// Self-service registration: one call creates a Profile and its administrator
    /// User.
    ///
    /// Replaces POST api/company/Create, which has been removed. This is the new
    /// Profile Admin registration flow, and it is deliberately NOT modelled on the
    /// Company/BusinessUnit path or on CreateTrial:
    ///
    ///   * it creates a Profile, a User holding Profile Admin + Team Manager, and
    ///     the admin's 30-day free trial (Trial subscription + 1 trial licence,
    ///     starting at registration) - no default BusinessUnit and no CompanyLink;
    ///   * the only uniqueness rule is the COMPLETE email address, case-insensitively;
    ///   * the email domain is never used to decide whether a Profile Admin exists,
    ///     and never associates the registration with an existing Profile. Several
    ///     Profile Admins may share a domain (manager1@abc.com, manager2@abc.com),
    ///     and multi-level domains such as abc.co.uk are treated whole rather than
    ///     reduced to an organisation key.
    /// </summary>
    [AllowAnonymous]
    [Route("api/profile")]
    [ApiController]
    public class ProfileRegistrationController : ControllerBase
    {
        private readonly IAccountRegistrationService _registrationService;
        private readonly SendGridSettings _sendGridSettings;
        private readonly ILogger<ProfileRegistrationController> _logger;

        public ProfileRegistrationController(
            IAccountRegistrationService registrationService,
            SendGridSettings sendGridSettings,
            ILogger<ProfileRegistrationController> logger)
        {
            _registrationService = registrationService;
            _sendGridSettings = sendGridSettings;
            _logger = logger;
        }

        /// <summary>
        /// POST api/profile/Create
        ///
        /// Validates first and writes nothing unless every rule passes; the profile,
        /// its default business unit and the user are then committed together, so a
        /// profile can never be left without its user or the other way round.
        /// </summary>
        [HttpPost("Create")]
        public async Task<IActionResult> CreateProfile([FromBody] ProfileCreateDto dto)
        {
            try
            {
                if (dto == null)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.RequestBodyEmpty));

                // Field-level checks first, so a blank name fails with its own message
                // rather than reaching a lookup.
                if (string.IsNullOrWhiteSpace(dto.ProfileName))
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ProfileNameRequired));

                if (string.IsNullOrWhiteSpace(dto.Password))
                    return Ok(ApiResponseHandler.Fail<string>("Password is required."));

                // Is the address well formed, and is its domain permitted at all?
                // Deliberately not checkCompany: that looks an existing company up by
                // domain, which is the domain-based identity matching this flow rules
                // out. This asks only about the address itself.
                if (!_registrationService.IsRegistrationEmailAcceptable(dto.EmailAddress))
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidEmail));

                // The one uniqueness rule: the complete email address. There is no
                // domain rule - manager1@abc.com and manager2@abc.com are both valid
                // registrations even though abc.com already has a profile.
                var existingUser = await _registrationService.UserEmailExistsAsync(dto.EmailAddress);
                if (existingUser != null)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.EmailAlreadyExist));

                // Checked again inside the repository transaction, which is what
                // actually closes the race between two concurrent sign-ups.
                var result = await _registrationService.CreateProfileWithUserAsync(dto);

                // Email is best-effort and deliberately outside the transaction: the
                // profile and user are already committed, and a mail-provider outage
                // must not undo a good registration. Failures are logged with provider
                // diagnostics, because a silently unsent verification email tells the
                // caller to check an inbox that will never receive anything.
                await SendRegistrationEmailsAsync(result);

                return Ok(ApiResponseHandler.Success(
                    result, ResponseMessages.Success.ProfileRegistrationSuccess));
            }
            catch (InvalidOperationException ex)
            {
                // A rule rejected the registration inside the transaction. Nothing was
                // written.
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Profile registration failed for {Email}.", dto?.EmailAddress);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        private async Task SendRegistrationEmailsAsync(ProfileCreateResponseDto result)
        {
            try
            {
                var verificationSent = await BussinessManager.SendEmailAsync(
                    _sendGridSettings, result.Email, result.UserId, result.ProfileId);

                if (!verificationSent)
                    _logger.LogError(
                        "Verification email was NOT sent for {Email} (UserId {UserId}, ProfileId {ProfileId}). " +
                        "Provider diagnostics: UseSendGrid={UseSendGrid}, ResendApiKeyConfigured={ResendKeySet}, " +
                        "SendGridApiKeyConfigured={SendGridKeySet}, FromEmail={FromEmail}.",
                        result.Email, result.UserId, result.ProfileId,
                        _sendGridSettings.UseSendGrid,
                        !string.IsNullOrWhiteSpace(_sendGridSettings.ResendApiKey),
                        !string.IsNullOrWhiteSpace(_sendGridSettings.ApiKey),
                        _sendGridSettings.FromEmail);
                else
                    _logger.LogInformation(
                        "Verification email sent to {Email} (UserId {UserId}).", result.Email, result.UserId);

                var contactSent = await BussinessManager.SendEmailContactAsync(
                    _sendGridSettings, result.Email, result.ProfileName);

                if (!contactSent)
                    _logger.LogWarning(
                        "Contact-notification email was not sent for new profile {ProfileName} ({Email}).",
                        result.ProfileName, result.Email);
            }
            catch (Exception emailEx)
            {
                _logger.LogError(emailEx,
                    "Verification/contact email threw for {Email} (UserId {UserId}, ProfileId {ProfileId}). " +
                    "Registration itself succeeded.",
                    result.Email, result.UserId, result.ProfileId);
            }
        }
    }
}
