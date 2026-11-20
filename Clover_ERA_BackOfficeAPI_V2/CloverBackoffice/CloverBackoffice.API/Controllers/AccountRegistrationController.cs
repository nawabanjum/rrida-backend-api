using CloverBackoffice.Components;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using static CloverBackoffice.Components.BussinessManager;
 
namespace CloverBackoffice.API.Controllers
{
    [AllowAnonymous]
    [Route("api/profile")]
    [ApiController]
    public class AccountRegistrationController : ControllerBase
    {
        private readonly IAccountRegistrationService _IAccountRegistrationService;
        private readonly IConfiguration _config;
        private readonly SendGridSettings _sendGridSettings;
        private readonly ITokenReaderService _tokenReader;
        private readonly IUserService _service;
        private readonly ApplicationDbContext _context;
        private readonly IPushNotificationService _pushNotificationService;
        private readonly ILogger<AccountRegistrationController> _logger;
        private readonly IUserRoleAssignmentService _userRoleAssignment;
        private readonly ILicenseAvailabilityService _licenseAvailability;
        private readonly IUserAccountLockService _accountLock;
        private readonly IUserManualActivationService _manualActivation;
        public AccountRegistrationController(ApplicationDbContext context, IConfiguration config, IAccountRegistrationService registrationService, SendGridSettings sendGridSettings, ITokenReaderService tokenReader, IUserService service, IPushNotificationService pushNotificationService, ILogger<AccountRegistrationController> logger, IUserRoleAssignmentService userRoleAssignment, ILicenseAvailabilityService licenseAvailability, IUserAccountLockService accountLock, IUserManualActivationService manualActivation)
        {
            _accountLock = accountLock;
            _manualActivation = manualActivation;
            _userRoleAssignment = userRoleAssignment;
            _licenseAvailability = licenseAvailability;
            _IAccountRegistrationService = registrationService;
            _config = config;
            _sendGridSettings = sendGridSettings;
            _tokenReader = tokenReader;
            _service = service;
            _context = context;
            _pushNotificationService = pushNotificationService;
            _logger = logger;
        }
        [HttpPost("login")]
        public async Task<APIResponse<LoginDto>> Login([FromForm] string email, [FromForm] string password)
        {
            APIResponse<LoginDto> response = new APIResponse<LoginDto>();
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                response.IsSuccess = false;
                response.Message = ResponseMessages.Errors.EmailPasswordRequired;
                return response;

            }
            // Get user by email to check is verified
            var userInfo = await _IAccountRegistrationService.GetUserInfoByEmailAsync(email);
            // Check if email is verified
            if (userInfo == null)
            {
                response.IsSuccess = false;
                response.Message = ResponseMessages.Errors.UserNotRegistered;
                return response;
            }
            if (!userInfo.IsVerified)
            {
                response.IsSuccess = false;
                response.Message = ResponseMessages.Warnings.EmailNotVerified;
                return response;
            }

            // Login attempt lockout check
            var maxAttempts = _config.GetValue<int>("LoginPolicy:MaxFailedAttempts", 5);
            var lockoutWindowMinutes = _config.GetValue<int>("LoginPolicy:LockoutWindowMinutes", 30);

            if (userInfo.FailedLoginAttempts >= maxAttempts && userInfo.LastFailedLoginAt.HasValue)
            {
                var timeSinceLastFail = DateTime.UtcNow - userInfo.LastFailedLoginAt.Value;
                var remainingMinutes = (int)Math.Ceiling(lockoutWindowMinutes - timeSinceLastFail.TotalMinutes);
                if (remainingMinutes > 0)
                {
                    response.IsSuccess = false;
                    response.IsLimitExceeded = true;
                    response.Message = $"Your account has been locked due to multiple failed login attempts. Please try again after {remainingMinutes} minute{(remainingMinutes != 1 ? "s" : "")}.";
                    return response;
                }
                else
                {
                    // Lockout window expired, reset attempts
                    await _IAccountRegistrationService.ResetFailedLoginAttemptsAsync(userInfo.UserId);
                    userInfo.FailedLoginAttempts = 0;
                }
            }

            // Check credentials
            var isAuthenticated = await _IAccountRegistrationService.AuthenticateAsync(email, password);
            if (!isAuthenticated)
            {
                // Increment failed login attempts
                await _IAccountRegistrationService.IncrementFailedLoginAttemptsAsync(userInfo.UserId);
                var updatedAttempts = userInfo.FailedLoginAttempts + 1;

                if (updatedAttempts >= maxAttempts)
                {
                    response.IsSuccess = false;
                    response.IsLimitExceeded = true;
                    response.Message = $"Your account has been locked due to multiple failed login attempts. Please try again after {lockoutWindowMinutes} minute{(lockoutWindowMinutes != 1 ? "s" : "")}.";
                    return response;
                }

                response.IsSuccess = false;
                response.Message = ResponseMessages.Errors.InvalidCredentials;
                return response;
            }

            // Successful login — reset failed attempts if any
            if (userInfo.FailedLoginAttempts > 0)
            {
                await _IAccountRegistrationService.ResetFailedLoginAttemptsAsync(userInfo.UserId);
            }

            // Made inactive by a Profile Admin (update-user-status): no access until
            // re-activated. Checked after the password so a wrong guess learns nothing.
            if (userInfo.Status == ((int)UserStatus.Suspend).ToString())
            {
                response.IsSuccess = false;
                response.Message = ResponseMessages.Errors.AccountInactive;
                return response;
            }

            var user = await _IAccountRegistrationService.UserInfoAsync(email);
            if (userInfo != null)
            {
                // Save system details after successful login
                if (user?.User != null && user.User.UserId > 0)
                {
                    try
                    {
                        string ipAddress = GetClientIpAddress();
                        string userAgent = Request.Headers["User-Agent"].FirstOrDefault() ?? string.Empty;
                        var securityRequest = new SecurityNotificationRequestDto
                        {
                            UserId = user.User.UserId,
                            IPAddress = ipAddress,
                            UserAgent = userAgent
                            // Custom title/message here if needed
                            // CustomTitle = "Security Alert",
                            // CustomMessage = "New login detected from different location"
                        };

                        await _pushNotificationService.SendSecurityNotificationIfMatchFoundAsync(securityRequest);
                        await _pushNotificationService.SaveSystemDetailsAsync(user.User.UserId, ipAddress, userAgent);
                    }
                    catch
                    {
                        // Don't fail login if system details saving fails
                    }
                }

                response.IsSuccess = true;
                response.Message = ResponseMessages.Success.LoginSuccess;
                response.Content = user;
                return response;
            }
            response.IsSuccess = false;
            response.Message = ResponseMessages.Success.UserFound;
            return response;
        }

        /// <summary>
        /// Gets the client IP address from the request, checking proxy headers first
        /// </summary>
        private string GetClientIpAddress()
        {
            // Check for forwarded IP address (when behind proxy/load balancer)
            var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwardedFor))
            {
                // X-Forwarded-For can contain multiple IPs, take the first one
                var ip = forwardedFor.Split(',')[0].Trim();
                if (!string.IsNullOrWhiteSpace(ip))
                {
                    return ip;
                }
            }

            // Check for real IP header
            var realIp = Request.Headers["X-Real-IP"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(realIp))
            {
                return realIp;
            }

            // Fall back to connection remote IP address
            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        }

        // POST /api/company/Create was removed. Self-service registration is now
        // POST /api/profile/Create (ProfileRegistrationController), which writes a
        // Profile instead of a Company and additionally requires the email domain to
        // be unused. CreateTrial below is unaffected and still in use.

        // POST /api/company/CreateTrial
        //
        // Clones POST /api/company/Create but provisions subscription + licenses.
        // Uses the SAME email-verification flow as /Create: the new user is
        // created unverified (Pending) and must verify via the emailed link.
        // The contact-notification email still fires (matches paid flow).
        [HttpPost("create-profile")]
        public async Task<IActionResult> CreateTrialCompany([FromBody] TrialProfileCreateDto dto)
        {
            try
            {
                if (dto == null)
                    return Ok(ApiResponseHandler.Fail<string>("Request body is required."));

                if (string.IsNullOrWhiteSpace(dto.ProfileName))
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.CompanyNameRequired));

                var domain = await _IAccountRegistrationService.checkCompany(dto.EmailAddress);
                // Trial always provisions a NEW company + default unit, even when
                // the domain already maps to an existing company. So the only
                // failures we honour here are the actually-bad-email cases (no @,
                // disallowed domain). The "existing company unverified" branch
                // from checkCompany is intentionally ignored — that signal was
                // built for the reuse-company flow we no longer want.
                var emailMalformed =
                    !domain.IsValid &&
                    !domain.CompanyNotVerified;
                if (emailMalformed)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidEmail));

                // Duplicate company names are allowed — see /Create.

                if (await _IAccountRegistrationService.ContactEmailExistsAsync(dto.EmailAddress) != null)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ContactEmailUnique));

                // Same blind spot as /Create — Contacts is not written by the
                // add-user endpoint, so a partner user's email passed this guard and
                // AddTrialProfileWithUserAsync then minted a duplicate Users row.
                if (await _IAccountRegistrationService.UserEmailExistsAsync(dto.EmailAddress) != null)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.EmailAlreadyExist));

                var resp = await _IAccountRegistrationService.CreateTrialProfileWithUserAsync(dto);

                // Same email-verification flow as /api/company/Create. The trial
                // user is created unverified (Pending); send the verification email
                // best-effort and log provider diagnostics on failure so a silent
                // mail-provider error surfaces. A send failure must NOT roll back
                // the already-committed registration.
                try
                {
                    var verificationSent = await BussinessManager.SendEmailAsync(
                        _sendGridSettings, resp.Email, resp.UserId, resp.ProfileId);

                    if (!verificationSent)
                        _logger.LogError(
                            "Verification email was NOT sent for {Email} (UserId {UserId}, CompanyId {CompanyId}). " +
                            "Provider diagnostics: UseSendGrid={UseSendGrid}, ResendApiKeyConfigured={ResendKeySet}, " +
                            "SendGridApiKeyConfigured={SendGridKeySet}, FromEmail={FromEmail}.",
                            resp.Email, resp.UserId, resp.ProfileId,
                            _sendGridSettings.UseSendGrid,
                            !string.IsNullOrWhiteSpace(_sendGridSettings.ResendApiKey),
                            !string.IsNullOrWhiteSpace(_sendGridSettings.ApiKey),
                            _sendGridSettings.FromEmail);
                    else
                        _logger.LogInformation(
                            "Verification email sent to {Email} (UserId {UserId}).", resp.Email, resp.UserId);

                    // contact-email template (internal notification) — also best-effort.
                    if (resp.IsNew)
                    {
                        var contactSent = await BussinessManager.SendEmailContactAsync(
                            _sendGridSettings, resp.Email, dto.ProfileName);
                        if (!contactSent)
                            _logger.LogWarning(
                                "Contact-notification email was not sent for new company {CompanyName} ({Email}).",
                                dto.ProfileName, resp.Email);
                    }
                }
                catch (Exception emailEx)
                {
                    _logger.LogError(emailEx,
                        "Verification/contact email threw for {Email} (UserId {UserId}, CompanyId {CompanyId}). " +
                        "Registration itself succeeded.",
                        resp.Email, resp.UserId, resp.ProfileId);
                }

                return Ok(ApiResponseHandler.Success(resp, resp.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                // Surface root-cause so DbUpdateException / FK / unique-index
                // failures aren't hidden behind the generic "An error occurred"
                // message. Safe here — endpoint is AllowAnonymous + dev-only flow.
                var root = ex.GetBaseException().Message;
                return Ok(ApiResponseHandler.Fail<string>(
                    $"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetType().Name}: {root}"));
            }
        }

        // The route token and the parameter are BOTH named profileId on purpose.
        // CompanyTenantGuardFilter identifies tenant surface by argument name, so a
        // profile id called "id" is invisible to it and the endpoint ends up
        // unguarded — which is exactly how one Profile Admin's token could read
        // another Profile Admin's record. The URL shape is unchanged: this is still
        // GET api/profile/GetProfileById/{value}.
        [HttpGet("GetProfileById/{profileId}")]
        public async Task<IActionResult> GetCompanyById(int profileId)
        {
            try
            {
                var companyDto = await _IAccountRegistrationService.GetCompanyByIdAsync(profileId);

                if (companyDto == null || companyDto.Id == 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.CompanyNotFound));
                }

                return Ok(ApiResponseHandler.Success(companyDto, ResponseMessages.Success.CompanyRecordFound));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        //[HttpPost("ValidateDomain")]
        //public async Task<IActionResult> ValidateDomain(string email)
        //{
        //    try
        //    {
        //        var result = await _IAccountRegistrationService.checkCompany(email);

        //        // Check if company exists but is not verified
        //        if (result.CompanyNotVerified)
        //        {
        //            return Ok(ApiResponseHandler.Fail(new
        //            {
        //                id = result.ProfileId,
        //                domainName = "null",
        //                name = "null",
        //                emailExist = false,
        //                domainExist = true,
        //                isVerified = result.IsVerified
        //            }, ResponseMessages.Errors.CompanyNotVerified));
        //        }

        //        if (!result.IsDomainValid)
        //        {
        //            return Ok(ApiResponseHandler.Fail(new
        //            {
        //                id = 0,
        //                domainName = "null",
        //                name = "null",
        //                emailExist = false,
        //                domainExist = false,

        //            }, "Domain is not valid."));
        //        }
        //        // company exist and email exist
        //        if (result.ProfileId > 0 && result.UserId > 1)
        //        {
        //            return Ok(ApiResponseHandler.Success(new
        //            {
        //                id = result.ProfileId,
        //                domainName = "null",
        //                name = "null",
        //                emailExist = true,
        //                domainExist = true,
        //                //isVerified = result.IsVerified
        //            }, ResponseMessages.Errors.EmailAlreadyExist));
        //        }
        //        if (result.ProfileId > 0 && result.UserId == 0)
        //        {
        //            return Ok(ApiResponseHandler.Success(new
        //            {
        //                id = result.ProfileId,
        //                domainName = "null",
        //                name = "null",
        //                emailExist = false,
        //                domainExist = true,
        //            }, "Email is valid."));
        //        }
        //        // user exist
        //        if (result.UserId > 0 && result.ProfileId == 0)
        //        {
        //            return Ok(ApiResponseHandler.Fail(new
        //            {
        //                id = 0,
        //                domainName = "null",
        //                name = "null",
        //                emailExist = true,
        //                domainExist = false,
        //            }, ResponseMessages.Warnings.EmailAlreadyTaken));
        //        }
        //        if (!result.IsEmailValid)
        //        {
        //            return Ok(ApiResponseHandler.Fail(new
        //            {
        //                id = 0,
        //                domainName = "null",
        //                name = "null",
        //                emailExist = false,
        //                domainExist = false,

        //            }, "Email is not valid."));
        //        }

        //        //no company, no user, no team and
        //        if (result.ProfileId == 0 && result.UserId == 0 && result.UnitId == 0)
        //        {
        //            return Ok(ApiResponseHandler.Success(new
        //            {
        //                id = 0,
        //                domainName = "null",
        //                name = "null",
        //                emailExist = false,
        //                domainExist = true,
        //            }, ResponseMessages.Success.DomainAvailable));
        //        }
        //        if (result.ProfileId < 0)
        //        {
        //            return Ok(ApiResponseHandler.Success(new
        //            {
        //                id = 0,
        //                domainName = "null",
        //                name = "null",
        //                emailExist = false,
        //                domainExist = false,
        //            }, ResponseMessages.Success.EmailAvailable));
        //        }

        //        return Ok(ApiResponseHandler.Fail(new
        //        {
        //            id = 0,
        //            domainName = "null",
        //            name = "null",
        //            emailExist = false,
        //            domainExist = false,

        //        }, "Email is not valid."));
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
        //    }
        //}

        // Same check as ValidateDomain, reduced to three rejection rules:
        //   malformed email, blacklisted domain, email already in use.
        //
        // The "company already exists / is unverified" branch is deliberately gone:
        // duplicate companies are allowed now, so an existing company on the same
        // domain is no longer a reason to reject. Matches /Create and /CreateTrial,
        // which already ignore CompanyNotVerified.
        //
        // "Already in use" spans Profiles, BusinessUnits and Users — see
        // EmailExistsInCompanyUnitOrUserAsync.
        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost("ValidateDomainV2")]
        public async Task<IActionResult> ValidateDomainV2(string email)
        {
            try
            {
                // checkCompany leaves IsEmailValid = true when the input has no "@",
                // so the format check is done here before it is called.
                if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email.Trim()))
                {
                    return Ok(ApiResponseHandler.Fail(new
                    {
                        id = 0,
                        domainName = "null",
                        name = "null",
                        emailExist = false,
                        domainExist = false,
                    }, "Email is not valid."));
                }

                var result = await _IAccountRegistrationService.checkCompany(email.Trim());

                // Blacklisted domain
                if (!result.IsDomainValid)
                {
                    return Ok(ApiResponseHandler.Fail(new
                    {
                        id = 0,
                        domainName = "null",
                        name = "null",
                        emailExist = false,
                        domainExist = false,
                    }, "This domain is not valid for this site"));
                }

                // Email already registered as a company contact, a business unit
                // contact, or a login. An unverified account is called out
                // separately so the caller can offer to resend the verification
                // link instead of just "already exists".
                var usage = await _IAccountRegistrationService.GetEmailUsageAsync(email.Trim());
                if (usage.Exists)
                {
                    return Ok(ApiResponseHandler.Fail(new
                    {
                        id = result.ProfileId,
                        domainName = "null",
                        name = "null",
                        emailExist = true,
                        domainExist = result.ProfileId > 0,
                        isVerified = usage.IsVerified,
                    }, usage.IsVerified
                        ? ResponseMessages.Errors.EmailAlreadyExist
                        : ResponseMessages.Errors.EmailExistsNotVerified));
                }

                return Ok(ApiResponseHandler.Success(new
                {
                    id = result.ProfileId,
                    domainName = "null",
                    name = "null",
                    emailExist = false,
                    domainExist = result.ProfileId > 0,
                }, ResponseMessages.Success.EmailAvailable));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost("add-unit")]
        public async Task<IActionResult> CreateUnit([FromBody] UnitCreateDto dto)
        {
            try
            {
                // Required-name check first: BussinessNameExistsAsync would happily
                // return null for a blank name and let an unnamed unit be created.
                if (dto == null || string.IsNullOrWhiteSpace(dto.UnitName))
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UnitNameRequired));

                var nameExits = await _IAccountRegistrationService.BussinessNameExistsAsync(dto.UnitName, dto.ProfileId);
                if (nameExits != null)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.BussinessNameUnique));

                var emailExits = await _IAccountRegistrationService.ContactEmailExistsAsync(dto.EmailAddress);
                if (emailExits != null)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ContactEmailUnique));

                // Identity-store check — see /Create. Without it an email that only
                // exists in Users (partner user) silently gets re-attached to a new
                // unit instead of being rejected.
                var unitUserExists = await _IAccountRegistrationService.UserEmailExistsAsync(dto.EmailAddress);
                if (unitUserExists != null)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.EmailAlreadyExist));

                // Step 1: Validate if the domain is allowed (e.g., from a JSON whitelist)

                var result = await _IAccountRegistrationService.checkCompany(dto.EmailAddress);

                if (!result.IsValid)
                    return Ok(ApiResponseHandler.Fail(new
                    {
                        userId = 0,
                        email = "null",
                        UnitId = 0
                    }, ResponseMessages.Errors.InvalidEmail));

                // Step 2: Proceed with company + Bussiness Unit + user creation
                var resp = await _IAccountRegistrationService.CreateBusinessWithUserAsync(dto);

                if (resp.UserId == 0 && resp.ProfileId == 0 && string.IsNullOrEmpty(resp.Email))
                {
                    return Ok(ApiResponseHandler.Fail(new
                    {
                        userId = 0,
                        email = "null",
                        UnitId = 0
                    }, resp.Message));
                }

                await BussinessManager.SendEmailAsync(_sendGridSettings, resp.Email, resp.UserId, resp.ProfileId);

                return Ok(ApiResponseHandler.Success(new
                {
                    userId = resp.UserId,
                    email = resp.Email,
                    UnitId = resp.ProfileId
                }, resp.Message));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPost("list")]
        public async Task<IActionResult> GetCompanyList()
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (!userId.HasValue)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>("Invalid token - UserId missing."));
                }

                var resp = await _IAccountRegistrationService.GetCompanyListAsync(userId.Value);

                return Ok(ApiResponseHandler.Success(resp, ResponseMessages.Success.CompanyDataFound));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([Required][FromForm] string email)
        {
            var result = await _IAccountRegistrationService.ForgotPasswordAsync(email);
            if (!result.IsSuccess)
                return Ok(ApiResponseHandler.Fail<string>(result.Message));

            return Ok(ApiResponseHandler.Success<string>(null, result.Message));
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            var result = await _IAccountRegistrationService.ResetPasswordAsync(dto);
            if (!result.IsSuccess)
                return Ok(ApiResponseHandler.Fail<string>(result.Message));

            return Ok(ApiResponseHandler.Success<string>(null, result.Message));
        }
        [HttpPost("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromQuery][Required] string param)
        {
            //var decrypted = BussinessManager.DecryptObject<VerifyUserEmailDto>(param);
            VerifyUserEmailDto decrypted;
            try
            {
                decrypted = BussinessManager.DecryptObject<VerifyUserEmailDto>(param);
            }
            catch (Exception)
            {
                // If decryption fails (e.g., invalid or tampered token)
                return Ok(ApiResponseHandler.Fail<object>(ResponseMessages.Errors.InvalidAction));
            }
            if (!ModelState.IsValid)
                return Ok(ApiResponseHandler.Fail<object>(ResponseMessages.Errors.InvalidRequest));

            var failedContent = new
            {
                UserId = 0,
                Username = "null",
                IsVerified = (bool?)null,
                Status = "null"
            };

            // Invitation links (accept-invite page load) are only checked here —
            // latest, unused, not expired. The account is verified and activated
            // when the password is set, so an old or superseded link activates nobody.
            if (decrypted.IsInvitation)
            {
                var check = await _IAccountRegistrationService.ValidateInvitationLinkAsync(param);
                if (!check.IsSuccess)
                    return Ok(ApiResponseHandler.Fail(failedContent, check.Message));

                return Ok(ApiResponseHandler.Success<object>(null, check.Message));
            }

            var success = await _IAccountRegistrationService.VerifyUserEmailAsync(decrypted);

            if (!success)
                return Ok(ApiResponseHandler.Fail(failedContent, ResponseMessages.Errors.UserNotFoundOrInvalid));

            return Ok(ApiResponseHandler.Success<object>(null, ResponseMessages.Success.EmailVerified));
        }
        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("GetUnitsByCompanyId")]
        public async Task<IActionResult> GetUnitsByProfileId([Required][FromQuery] long profileId)
        {
            try
            {
                if (profileId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidCompany));
                }

                //  Get UserId from token
                int? userId = _tokenReader.GetUserIdFromToken();
                if (!userId.HasValue)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>("Invalid token - UserId missing."));
                }

                var businessUnits = await _IAccountRegistrationService.GetUnitsByProfileIdAsync(profileId, userId.Value);

                if (businessUnits == null || !businessUnits.Any())
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UnitsNotFound));
                }

                return Ok(ApiResponseHandler.Success(businessUnits, ResponseMessages.Success.UnitsFound));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// POST api/profile/upsert-user — the Add User endpoint.
        ///
        /// A Profile Admin adds a user to THEIR OWN profile and picks that user's
        /// role: Profile Admin, Team Manager or Portfolio Manager. The user is
        /// created invited/pending (IsVerified false, Status Pending) and is
        /// emailed a single-use invitation link; following it sets their password,
        /// verifies the account and lets them sign in. Sending <c>userId &gt; 0</c>
        /// edits an existing user of the same profile instead.
        ///
        /// Three things authorise it, and all three are needed:
        ///
        ///   * a token — the route is no longer in
        ///     <c>TokenAuthentication.skipPaths</c>, so an anonymous call is
        ///     refused before MVC and the tenant guard always has a caller to
        ///     judge. The <c>[Authorize]</c> below states the intent but does not
        ///     enforce it — the controller's class-level <c>[AllowAnonymous]</c>
        ///     overrides it (build warning ASP0026), exactly as it does for the
        ///     two other actions here that carry it. The middleware and the token
        ///     check inside are what actually refuse an anonymous caller;
        ///   * the caller's ROLE — Profile Admin, or RRIDA Admin who administers
        ///     every profile. A Team Manager holding a valid token is refused;
        ///     before this, any authenticated user could create users, including
        ///     Profile Admins;
        ///   * the PROFILE — see <c>ResolveTargetProfileIdAsync</c>. A Profile
        ///     Admin's user always lands in the admin's own profile whatever the
        ///     request body says, so the company cannot be switched by editing a
        ///     parameter. <c>CompanyTenantGuardFilter</c> already answers
        ///     403 for a foreign <c>profileId</c>; this is the second half of the
        ///     same rule, covering the omitted / 0 case the guard waves through.
        ///
        /// Licences (a licence is a team slot), checked before anything is written:
        /// a new team needs a free licence and is given it in the same transaction;
        /// a new user needs a free licence unless they join an existing team that
        /// already holds one. No licence → no user, no team, no invitation.
        /// </summary>
        [Authorize]
        [HttpPost("upsert-user")]
        public async Task<IActionResult> UpsertUser([FromBody] UserUpsertDto dto)
        {
            try
            {
                if (dto == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.RequestBodyEmpty));
                }

                // The whole caller record, not just the id: the role and the
                // owning profile both come off it, and each is a decision here.
                var actor = await _tokenReader.GetCurrentUserAsync();
                if (actor == null)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));
                }

                // Read from UserRoles: a Team Manager who is ALSO a Profile Admin may add users.
                if (!await _userRoleAssignment.HasAnyRoleAsync(actor.UserId, UserGroups.ProfileAdmin, UserGroups.RridaAdmin))
                {
                    return StatusCode(StatusCodes.Status403Forbidden,
                        ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UserCreateNotPermitted));
                }

                dto.CreatedBy = actor.UserId;

                // A RRIDA Admin adding a NEW user must say which profile it is for.
                // Falling back to their own profile put the user in the RRIDA
                // platform profile, so get-users for the profile on screen never
                // listed them.
                if (actor.UserGroupId == (int)UserGroups.RridaAdmin && dto.UserId <= 0 && dto.ProfileId is not > 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.RridaAdminProfileIdRequired));
                }

                var targetProfileId = await ResolveTargetProfileIdAsync(actor, dto);
                if (targetProfileId is null or <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidProfileId));
                }

                dto.ProfileId = targetProfileId;

                // Only the three ASSIGNABLE roles may be set here. RRIDA Admin is
                // deliberately absent: it is platform staff, minted only by
                // DatabaseSeeder, and allowing it here would let any Profile Admin
                // promote themselves to super admin. Retired group ids are rejected
                // for the older reason — they are no longer rows in UserGroups, so
                // they would surface later as a foreign-key violation.
                if (dto.UserGroupId != (int)UserGroups.ProfileAdmin &&
                    dto.UserGroupId != (int)UserGroups.TeamManager &&
                    dto.UserGroupId != (int)UserGroups.PortfolioManager)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidUserGroup));
                }

                if (string.IsNullOrWhiteSpace(dto.Email))
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.EmailPasswordRequired));
                }

                // Shape of the address plus the blocked-domain list, and nothing
                // else. IsDomainValidAsync was used here and also looks a company
                // up BY DOMAIN; only .IsValid was ever read, so the lookup bought
                // nothing but coupled adding a user to domain-based identity —
                // which this product does not do (see api/profile/Create). The
                // replacement is stricter on the part that matters: it requires a
                // dot in the domain, so "someone@localhost" no longer passes.
                if (!_IAccountRegistrationService.IsRegistrationEmailAcceptable(dto.Email))
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidEmail));
                }

                // The persisted user name is $"{FirstName} {LastName}".Trim(), so it is
                // empty only when BOTH parts are blank. A name is needed to create or
                // edit a user — not to give a role to an existing user of the profile
                // found by email, whose name is left as it is.
                if (string.IsNullOrWhiteSpace($"{dto.FirstName} {dto.LastName}"))
                {
                    var existingByEmail = dto.UserId == 0
                        ? await _IAccountRegistrationService.UserEmailExistsAsync(dto.Email)
                        : null;

                    if (existingByEmail == null)
                        return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UserNameRequired));
                }

                // Create, or assign a role to an existing user of the SAME profile
                // (never a duplicate row), and settle the team / manager relationship
                // the role needs — one transaction. An email in another profile is
                // refused inside.
                var outcome = await _userRoleAssignment.AddOrAssignUserAsync(dto, actor.UserId);

                // Replacing a team's manager or a manager's Portfolio Manager needs
                // the admin's confirmation. Nothing was written; the data tells the
                // client what to show and which flag to re-send.
                if (outcome.Confirmation != null)
                {
                    return Ok(ApiResponseHandler.Fail(outcome.Confirmation, outcome.ErrorMessage ?? string.Empty));
                }

                // No available licence: nothing was written. The content carries the
                // counts, isLicenseLimitReached and the Buy Additional Licences checkout.
                if (outcome.IsLicenseLimitReached)
                {
                    var availability = await _licenseAvailability.GetAvailabilityAsync((uint)targetProfileId.Value, actor);
                    availability.IsLicenseLimitReached = true;
                    return Ok(ApiResponseHandler.Fail(availability, outcome.ErrorMessage ?? ResponseMessages.Errors.LicenseLimitReached));
                }

                if (!outcome.IsSuccess || outcome.Data == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(outcome.ErrorMessage ?? ResponseMessages.Errors.UserUpsertFail));
                }

                var result = outcome.Data;
                string message = outcome.IsExistingUserAssignment
                    ? ResponseMessages.Success.UserRoleAssigned
                    : ResponseMessages.Success.UserUpdated;

                // Invitation only for a user row created now — an existing user
                // already has an account and gets no second invitation.
                if (outcome.IsNewUser)
                {
                    // Records the link as single-use before sending it.
                    var invitationSent = await _IAccountRegistrationService.SendInvitationEmailAsync(
                        result.Email, result.UserId, result.CompanyName);

                    // The user exists either way — the invitation is sent outside
                    // the write and a mail outage must not undo a good creation —
                    // but the admin is told which of the two happened. Reporting
                    // "check your email" for an invitation that never left sends
                    // them to watch an inbox nothing is coming to.
                    if (!invitationSent)
                    {
                        _logger.LogError(
                            "Invitation email was NOT sent for new user {Email} (UserId {UserId}, ProfileId {ProfileId}).",
                            result.Email, result.UserId, dto.ProfileId);
                    }

                    result.InvitationSent = invitationSent;
                    message = invitationSent
                        ? ResponseMessages.Success.UserInvited
                        : ResponseMessages.Success.UserCreatedInviteFailed;
                }

                return Ok(ApiResponseHandler.Success(result, message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "upsert-user failed for {Email}.", dto?.Email);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// Which profile the added / edited user belongs to.
        ///
        /// A Profile Admin administers exactly one profile, so theirs is the
        /// answer and the request's <c>profileId</c> is IGNORED rather than
        /// trusted — that is what stops a crafted body from placing a user in
        /// another admin's company. A foreign id never gets this far anyway
        /// (<c>CompanyTenantGuardFilter</c> answers 403 first); this also
        /// covers the id being omitted or 0, which the guard passes through as
        /// "use my own" and which would otherwise create a user with no profile.
        ///
        /// A RRIDA Admin spans every profile, so they MAY name one. When they do
        /// not, an EDIT keeps the user where they already are — the profile is a
        /// property of that user, not of the administrator touching them, and
        /// defaulting to the caller's own would silently move somebody else's user
        /// into the platform profile. A create with no id never gets here:
        /// UpsertUser refuses it first.
        /// </summary>
        private async Task<int?> ResolveTargetProfileIdAsync(User actor, UserUpsertDto dto)
        {
            if (actor.UserGroupId != (int)UserGroups.RridaAdmin)
                return actor.ProfileId;

            if (dto.ProfileId is > 0)
                return dto.ProfileId;

            if (dto.UserId > 0)
            {
                var target = await _IAccountRegistrationService.GetUserByIdAsync(dto.UserId);
                if (target?.ProfileId is > 0)
                    return target.ProfileId;
            }

            return actor.ProfileId;
        }

        /// <summary>
        /// GET api/profile/assignable-teams — the Add User "select team" dropdown
        /// for a Team Manager: the profile's live teams, each with its current
        /// manager. <c>hasManager: true</c> means choosing it needs the
        /// replace-manager confirmation.
        ///
        /// Same authorisation and profile rule as upsert-user: Profile Admin → own
        /// profile (profileId ignored); RRIDA Admin → the profileId given.
        /// </summary>
        [Authorize]
        [HttpGet("assignable-teams")]
        public async Task<IActionResult> GetAssignableTeams([FromQuery] int? profileId)
        {
            try
            {
                var actor = await _tokenReader.GetCurrentUserAsync();
                if (actor == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                var target = await ResolveDropdownProfileIdAsync(actor, profileId);
                if (target == null)
                    return StatusCode(StatusCodes.Status403Forbidden,
                        ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UserCreateNotPermitted));
                if (target <= 0)
                    return Ok(ApiResponseHandler.Fail<string>(DropdownProfileError(actor)));

                var teams = await _userRoleAssignment.GetAssignableTeamsAsync((uint)target.Value);
                return Ok(ApiResponseHandler.Success(teams,
                    teams.Any() ? ResponseMessages.Success.RecordFound : ResponseMessages.Errors.NoRecords));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "assignable-teams failed.");
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// GET api/profile/assignable-managers — the Add User "select manager"
        /// dropdown for a Portfolio Manager: the profile's live Team Managers, each
        /// with their current Portfolio Manager. <c>hasPortfolioManager: true</c>
        /// means choosing them needs the replace confirmation. Pass <c>userId</c>
        /// (the user being edited) to leave them out of their own list.
        /// </summary>
        [Authorize]
        [HttpGet("assignable-managers")]
        public async Task<IActionResult> GetAssignableManagers([FromQuery] int? profileId, [FromQuery] int? userId)
        {
            try
            {
                var actor = await _tokenReader.GetCurrentUserAsync();
                if (actor == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                var target = await ResolveDropdownProfileIdAsync(actor, profileId);
                if (target == null)
                    return StatusCode(StatusCodes.Status403Forbidden,
                        ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UserCreateNotPermitted));
                if (target <= 0)
                    return Ok(ApiResponseHandler.Fail<string>(DropdownProfileError(actor)));

                var managers = await _userRoleAssignment.GetAssignableManagersAsync((uint)target.Value, userId is > 0 ? userId : null);
                return Ok(ApiResponseHandler.Success(managers,
                    managers.Any() ? ResponseMessages.Success.RecordFound : ResponseMessages.Errors.NoRecords));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "assignable-managers failed.");
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        // Null: caller may not add users. Otherwise the profile the dropdown lists —
        // a Profile Admin's own, whatever was asked for; a RRIDA Admin's choice.
        // A RRIDA Admin who names none gets 0 (refused by the caller), not their
        // own profile: that listed the platform profile's managers and teams.
        private async Task<int?> ResolveDropdownProfileIdAsync(User actor, int? profileId)
        {
            if (!await _userRoleAssignment.HasAnyRoleAsync(actor.UserId, UserGroups.ProfileAdmin, UserGroups.RridaAdmin))
                return null;

            if (actor.UserGroupId == (int)UserGroups.RridaAdmin)
                return profileId is > 0 ? profileId : 0;

            return actor.ProfileId ?? 0;
        }

        private static string DropdownProfileError(User actor) =>
            actor.UserGroupId == (int)UserGroups.RridaAdmin
                ? ResponseMessages.Errors.RridaAdminProfileIdRequired
                : ResponseMessages.Errors.InvalidProfileId;

        /// <summary>
        /// POST api/profile/user/{userId}/profile-admin — gives an existing user the
        /// Profile Admin role. DELETE on the same route takes it off. Only roles
        /// change; the user's details and other roles stay as they are.
        /// Caller: Profile Admin (own profile) or RRIDA Admin. Route parameter is
        /// named userId so CompanyTenantGuardFilter can see it.
        /// </summary>
        [Authorize]
        [HttpPost("user/{userId}/profile-admin")]
        public Task<IActionResult> AssignProfileAdmin(int userId)
            => ChangeProfileAdminRoleAsync(userId, assign: true);

        [Authorize]
        [HttpDelete("user/{userId}/profile-admin")]
        public Task<IActionResult> RemoveProfileAdmin(int userId)
            => ChangeProfileAdminRoleAsync(userId, assign: false);

        private async Task<IActionResult> ChangeProfileAdminRoleAsync(int userId, bool assign)
        {
            try
            {
                var callerUserId = _tokenReader.GetUserIdFromToken();
                if (callerUserId == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                if (userId <= 0)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UserNotFound));

                var outcome = assign
                    ? await _userRoleAssignment.AssignProfileAdminAsync(userId, callerUserId.Value)
                    : await _userRoleAssignment.RemoveProfileAdminAsync(userId, callerUserId.Value);

                if (outcome.IsForbidden)
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponseHandler.Fail<string>(outcome.ErrorMessage!));

                if (outcome.IsNotFound)
                    return NotFound(ApiResponseHandler.Fail<string>(outcome.ErrorMessage!));

                if (!outcome.IsSuccess || outcome.Data == null)
                    return Ok(ApiResponseHandler.Fail<string>(outcome.ErrorMessage ?? ResponseMessages.Errors.ExceptionMsg));

                return Ok(ApiResponseHandler.Success(outcome.Data, assign
                    ? ResponseMessages.Success.ProfileAdminAssigned
                    : ResponseMessages.Success.ProfileAdminRemoved));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Profile Admin role change failed for user {UserId} (assign {Assign}).", userId, assign);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// POST api/profile/remove-team-manager — Manage User. Takes the Team Manager role
        /// off an existing user, whether they hold no team, one or several: they are cleared
        /// from every team, their licence is released and their other roles stay. The teams
        /// stay too, with no manager and no replacement. Body: { "userId": 334 }, a name the
        /// tenant guard sees. Caller: Profile Admin (own profile) or RRIDA Admin. The
        /// frontend then re-reads GET api/license-assignments/availability for the counts.
        /// </summary>
        [Authorize]
        [HttpPost("remove-team-manager")]
        public async Task<IActionResult> RemoveTeamManager([FromBody] RemoveTeamManagerDto dto)
        {
            try
            {
                var callerUserId = _tokenReader.GetUserIdFromToken();
                if (callerUserId == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                if (dto.UserId <= 0)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UserNotFound));

                var outcome = await _userRoleAssignment.RemoveTeamManagerAsync(dto.UserId, callerUserId.Value);

                if (outcome.IsForbidden)
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponseHandler.Fail<string>(outcome.ErrorMessage!));

                if (outcome.IsNotFound)
                    return NotFound(ApiResponseHandler.Fail<string>(outcome.ErrorMessage!));

                if (!outcome.IsSuccess || outcome.Data == null)
                    return Ok(ApiResponseHandler.Fail<string>(outcome.ErrorMessage ?? ResponseMessages.Errors.ExceptionMsg));

                return Ok(ApiResponseHandler.Success(outcome.Data, ResponseMessages.Success.TeamManagerRemoved));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Team Manager removal failed for user {UserId}.", dto.UserId);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        // Named userId, not id, so the tenant guard can see it — see the note on
        // GetProfileById above. Without it a Profile Admin could delete a user
        // belonging to any other profile.
        [HttpDelete("user/{userId}")]
        public async Task<IActionResult> DeleteUser(int userId)
        {
            try
            {
                int? callerUserId = _tokenReader.GetUserIdFromToken();
                if (callerUserId == null)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));
                }
                // A profile must always keep a Profile Admin. Checked before the
                // delete: a Profile Admin may not remove their own account, and
                // nobody may remove the last one. Users who are not Profile Admins
                // are unaffected.
                var removalError = await _userRoleAssignment.ProfileAdminRemovalErrorAsync(
                    userId, callerUserId.Value, refuseSelf: true);

                if (removalError != null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(removalError));
                }

                var result = await _IAccountRegistrationService.DeleteUserAsync(userId);

                if (!result)
                {
                    return Ok(ApiResponseHandler.Fail<string>("User not found"));
                }

                return Ok(ApiResponseHandler.Success(true, ResponseMessages.Success.Userdelete));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// POST api/profile/update-user-status — Manage User "Make Inactive" / "Make Active".
        /// Body: { "userId": 229, "status": "Inactive" } ("Active"/"Inactive" or "1"/"2"/"3").
        /// Only Users.Status changes: roles, team assignments and answers are kept.
        /// Inactive = UserStatus.Suspend: login is refused and every open session is
        /// signed out. Caller: Profile Admin (own profile — CompanyTenantGuardFilter
        /// checks the body's userId) or RRIDA Admin.
        /// Making Active can also issue access: "reactivationMethod": "ManualCredentials"
        /// (returns resetLink to share) or "Invite" (emails an invitation).
        /// </summary>
        [HttpPost("update-user-status")]
        public async Task<IActionResult> UpdateUserStatus([FromBody] UpdateUserStatusDto dto)
        {
            try
            {
                if (dto == null || dto.UserId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UserNotFound));
                }

                var actorUserId = _tokenReader.GetUserIdFromToken();
                if (actorUserId == null)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));
                }

                var actorRoles = await _userRoleAssignment.GetRoleIdsAsync(actorUserId.Value);
                if (!actorRoles.Contains((int)UserGroups.ProfileAdmin) && !actorRoles.Contains((int)UserGroups.RridaAdmin))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.StatusChangeNotAllowed));
                }

                var user = await _IAccountRegistrationService.GetUserByIdAsync(dto.UserId);

                if (user == null || user.IsDel)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UserNotFound));
                }

                // "Active" / "Inactive" (the Manage User modal) or the numeric enum value.
                var requested = (dto.Status ?? string.Empty).Trim();
                int statusInt;
                if (requested.Equals("Active", StringComparison.OrdinalIgnoreCase))
                    statusInt = (int)UserStatus.Active;
                else if (requested.Equals("Inactive", StringComparison.OrdinalIgnoreCase)
                         || requested.Equals("Suspend", StringComparison.OrdinalIgnoreCase))
                    statusInt = (int)UserStatus.Suspend;
                else if (!int.TryParse(requested, out statusInt) || !Enum.IsDefined(typeof(UserStatus), statusInt))
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InValidStatus));

                // Optional: how the admin gives a re-activated user access again.
                string? reactivationMethod = null;
                if (!string.IsNullOrWhiteSpace(dto.ReactivationMethod))
                {
                    var method = dto.ReactivationMethod.Trim();
                    if (statusInt == (int)UserStatus.Active && method.Equals(ReactivationMethods.ManualCredentials, StringComparison.OrdinalIgnoreCase))
                        reactivationMethod = ReactivationMethods.ManualCredentials;
                    else if (statusInt == (int)UserStatus.Active && method.Equals(ReactivationMethods.Invite, StringComparison.OrdinalIgnoreCase))
                        reactivationMethod = ReactivationMethods.Invite;
                    else
                        return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidReactivationMethod));
                }

                // Suspending is the other way a profile could end up with no
                // Profile Admin, so the same minimum applies. Self-suspension is
                // allowed while other admins remain — only being the LAST one
                // stops it, which is why refuseSelf is false here and true on
                // delete.
                if (statusInt == (int)UserStatus.Suspend)
                {
                    var suspendError = await _userRoleAssignment.ProfileAdminRemovalErrorAsync(
                        dto.UserId, actorUserId.Value, refuseSelf: false);

                    if (suspendError != null)
                    {
                        return Ok(ApiResponseHandler.Fail<string>(suspendError));
                    }
                }

                // Re-activating someone who never accepted their invitation puts
                // them back to Pending, not Active: the invitation flow still has
                // to verify them (login refuses unverified users anyway).
                if (statusInt == (int)UserStatus.Active && !user.IsVerified)
                    statusInt = (int)UserStatus.Pending;

                user.Status = statusInt.ToString();
                user.ModifiedDate = DateTime.UtcNow.ToString("o");
                if (user.LastLoginDate.HasValue)
                    user.LastLoginDate = DateTime.SpecifyKind(user.LastLoginDate.Value, DateTimeKind.Utc);
                await _IAccountRegistrationService.UpdateUserAsync(user);

                // Inactive means no access now, not at the next login: drop every
                // access/refresh token the user holds.
                if (statusInt == (int)UserStatus.Suspend)
                    await _IAccountRegistrationService.InvalidateUserTokensAsync(user.UserId);

                var result = new UpdateUserStatusResultDto
                {
                    UserId = user.UserId,
                    Status = user.Status,
                    StatusName = statusInt switch
                    {
                        (int)UserStatus.Active => "Active",
                        (int)UserStatus.Pending => "Pending",
                        _ => "Inactive"
                    },
                    IsActive = statusInt == (int)UserStatus.Active,
                    Roles = await _userRoleAssignment.GetRolesAsync(user.UserId)
                };

                var message = statusInt == (int)UserStatus.Suspend
                    ? ResponseMessages.Success.UserMadeInactive
                    : statusInt == (int)UserStatus.Active
                        ? ResponseMessages.Success.UserMadeActive
                        : ResponseMessages.Success.StatusUpdate;

                // Re-activation access. Runs after the status is saved, so the link /
                // invitation is only issued for a user who can actually log in with it.
                // (An unverified user was put back to Pending above; the invitation is
                // still the right way in for them — accepting it verifies and activates.)
                if (reactivationMethod == ReactivationMethods.ManualCredentials)
                {
                    result.ReactivationMethod = reactivationMethod;
                    result.ResetLink = await _IAccountRegistrationService.GenerateManualResetPasswordLink(user);
                    message = ResponseMessages.Success.UserMadeActiveWithCredentials;
                }
                else if (reactivationMethod == ReactivationMethods.Invite)
                {
                    var companyName = user.ProfileId.HasValue
                        ? await _context.Profiles.AsNoTracking()
                            .Where(pr => pr.Id == (uint)user.ProfileId.Value)
                            .Select(pr => pr.ProfileName)
                            .FirstOrDefaultAsync()
                        : null;

                    var invitationSent = await _IAccountRegistrationService.SendInvitationEmailAsync(
                        user.Username, user.UserId, companyName);

                    if (!invitationSent)
                    {
                        _logger.LogError("Re-activation invitation was NOT sent for user {UserId}.", user.UserId);
                    }

                    result.ReactivationMethod = reactivationMethod;
                    result.InvitationSent = invitationSent;
                    message = invitationSent
                        ? ResponseMessages.Success.UserMadeActiveInviteSent
                        : ResponseMessages.Success.UserMadeActiveInviteFailed;
                }

                return Ok(ApiResponseHandler.Success(result, message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "update-user-status failed for user {UserId}.", dto?.UserId);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// POST api/profile/unlock-user — user list "Unlock".
        /// Body: { "userId": 229 }. Clears the lock set by too many failed logins
        /// (LoginPolicy), so the user can sign in again straight away. Only the
        /// failed-login counter changes: status, roles, profile and invitation are
        /// kept. Caller: Profile Admin (own profile — CompanyTenantGuardFilter also
        /// checks the body's userId) or RRIDA Admin (any profile). get-users returns
        /// isLocked / lockedUntil per row.
        /// </summary>
        [Authorize]
        [HttpPost("unlock-user")]
        public async Task<IActionResult> UnlockUser([FromBody] UnlockUserRequestDto dto)
        {
            try
            {
                if (dto == null || dto.UserId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UserNotFound));
                }

                var actorUserId = _tokenReader.GetUserIdFromToken();
                if (actorUserId == null)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));
                }

                var result = await _accountLock.UnlockUserAsync(dto.UserId, actorUserId.Value);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.UserUnlocked));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "unlock-user failed for user {UserId}.", dto?.UserId);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// POST api/profile/activate-user — activate a user manually.
        /// Body: { "userId": 229, "temporaryPassword": "..." }. For an invited
        /// (pending) or inactive user: stores the password as a BCrypt hash, marks
        /// the account verified and Active, and revokes any open invitation link, so
        /// the user can log in without the email invitation. Roles, profile and
        /// teams are kept. The password follows the RRIDA screens' rules and is never
        /// returned or logged. Caller: Profile Admin (own profile —
        /// CompanyTenantGuardFilter also checks the body's userId) or RRIDA Admin.
        /// </summary>
        [Authorize]
        [HttpPost("activate-user")]
        public async Task<IActionResult> ActivateUser([FromBody] ActivateUserRequestDto dto)
        {
            try
            {
                if (dto == null || dto.UserId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UserNotFound));
                }

                var actorUserId = _tokenReader.GetUserIdFromToken();
                if (actorUserId == null)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));
                }

                var result = await _manualActivation.ActivateUserAsync(dto.UserId, dto.TemporaryPassword, actorUserId.Value);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.UserActivatedManually));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "activate-user failed for user {UserId}.", dto?.UserId);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// POST api/profile/resend-invitation — user list "Resend Invite".
        /// Body: { "userId": 229 }. Emails the pending user a fresh invitation, the
        /// same email and single-use, 3-day link upsert-user sends. Older links stay
        /// valid until used or expired. Caller: Profile Admin (own profile) or RRIDA Admin.
        /// The invite URL is returned on success and on an email failure, so the
        /// admin can still copy it.
        /// </summary>
        [Authorize]
        [HttpPost("resend-invitation")]
        public async Task<IActionResult> ResendInvitation([FromBody] UserInvitationRequestDto dto)
        {
            try
            {
                var (user, error) = await GetPendingInvitationUserAsync(dto);
                if (error != null)
                {
                    return error;
                }

                var companyName = user!.ProfileId.HasValue
                    ? await _context.Profiles.AsNoTracking()
                        .Where(pr => pr.Id == (uint)user.ProfileId.Value)
                        .Select(pr => pr.ProfileName)
                        .FirstOrDefaultAsync()
                    : null;

                var link = await _IAccountRegistrationService.ResendInvitationEmailAsync(user, companyName);

                var result = new UserInvitationResultDto
                {
                    UserId = user.UserId,
                    Email = user.Username,
                    InviteUrl = link.InviteUrl,
                    ExpiresAt = link.ExpiresAt,
                    InvitationSent = link.EmailSent == true
                };

                if (link.EmailSent != true)
                {
                    _logger.LogError("Resend invitation email was NOT sent for user {UserId}.", user.UserId);
                    return Ok(ApiResponseHandler.Fail(result, ResponseMessages.Errors.InvitationEmailFailed));
                }

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.InvitationResent));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "resend-invitation failed for user {UserId}.", dto?.UserId);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// POST api/profile/invitation-link — user list "Copy Invite Link".
        /// Body: { "userId": 229 }. Returns the pending user's newest invitation link
        /// that still works (the one already emailed); when none is left (used or
        /// expired) a new one is created. No email is sent. Same single-use, 3-day
        /// rules as the emailed link. Caller: Profile Admin (own profile) or RRIDA Admin.
        /// </summary>
        [Authorize]
        [HttpPost("invitation-link")]
        public async Task<IActionResult> GetInvitationLink([FromBody] UserInvitationRequestDto dto)
        {
            try
            {
                var (user, error) = await GetPendingInvitationUserAsync(dto);
                if (error != null)
                {
                    return error;
                }

                var link = await _IAccountRegistrationService.GetOrCreateInvitationLinkAsync(user!);

                return Ok(ApiResponseHandler.Success(new UserInvitationResultDto
                {
                    UserId = user!.UserId,
                    Email = user.Username,
                    InviteUrl = link.InviteUrl,
                    ExpiresAt = link.ExpiresAt
                }, ResponseMessages.Success.InvitationLinkReady));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "invitation-link failed for user {UserId}.", dto?.UserId);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        // Shared checks for resend-invitation / invitation-link: caller is a Profile
        // Admin of the user's profile (CompanyTenantGuardFilter also checks the
        // body's userId) or an RRIDA Admin, and the user is live and still Pending.
        private async Task<(User? User, IActionResult? Error)> GetPendingInvitationUserAsync(UserInvitationRequestDto? dto)
        {
            if (dto == null || dto.UserId <= 0)
            {
                return (null, Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UserNotFound)));
            }

            var actor = await _tokenReader.GetCurrentUserAsync();
            if (actor == null)
            {
                return (null, Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken)));
            }

            var actorRoles = await _userRoleAssignment.GetRoleIdsAsync(actor.UserId);
            var isRridaAdmin = actorRoles.Contains((int)UserGroups.RridaAdmin);
            if (!isRridaAdmin && !actorRoles.Contains((int)UserGroups.ProfileAdmin))
            {
                return (null, StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvitationNotAllowed)));
            }

            var user = await _IAccountRegistrationService.GetUserByIdAsync(dto.UserId);
            if (user == null || user.IsDel)
            {
                return (null, Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UserNotFound)));
            }

            if (!isRridaAdmin && user.ProfileId != actor.ProfileId)
            {
                return (null, StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvitationNotAllowed)));
            }

            if (user.Status == ((int)UserStatus.Suspend).ToString())
            {
                return (null, Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvitationUserInactive)));
            }

            if (user.Status != ((int)UserStatus.Pending).ToString())
            {
                return (null, Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvitationUserNotPending)));
            }

            return (user, null);
        }

        [HttpGet("user-group-roles")]
        public async Task<IActionResult> GetUserGroups()
        {
            try
            {
                int? userId = _tokenReader.GetTokenInfo();
                if (!userId.HasValue)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>("Invalid token - UserId missing."));
                }
                var groups = await _IAccountRegistrationService.GetUserGroupsAsync(userId);

                if (groups == null || !groups.Any())
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UserGroupNotFound));

                return Ok(ApiResponseHandler.Success(groups, ResponseMessages.Success.UserGroupSuccess));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// GET api/profile/role-hierarchy
        ///
        /// The role hierarchy as the CALLER may see it: their own role first, then
        /// every role beneath it, highest first.
        ///
        ///   RRIDA Admin       → RRIDA Admin, Profile Admin, Team Manager, Portfolio Manager
        ///   Profile Admin     → Profile Admin, Team Manager, Portfolio Manager
        ///   Team Manager      → Team Manager, Portfolio Manager
        ///   Portfolio Manager → Portfolio Manager
        ///
        /// Open to every authenticated role — that is the point, each one seeing
        /// its own slice. Ordering is the response's job and it is already correct:
        /// <c>level</c> is there to be displayed, not to be sorted by, and the
        /// <c>id</c> must never be sorted on (1 Profile Admin outranks 2 Team
        /// Manager).
        ///
        /// NOT interchangeable with <c>user-group-roles</c>, which stays as it is.
        /// That endpoint answers which roles the caller may HAND OUT — a narrower
        /// question, and one with different answers: it hides RRIDA Admin from
        /// everybody, and it gives a Team Manager only Team Manager. This one
        /// answers what the caller may SEE, so a RRIDA Admin does get their own
        /// role back. It is marked <c>isAssignable: false</c>, since
        /// <c>upsert-user</c> refuses to create one — a role picker built on this
        /// list must respect that flag rather than offering every row.
        /// </summary>
        [Authorize]
        [HttpGet("role-hierarchy")]
        public async Task<IActionResult> GetRoleHierarchy()
        {
            try
            {
                // GetUserIdFromToken, not GetTokenInfo: an expired token must not
                // still answer. The route is not in TokenAuthentication.skipPaths,
                // so the middleware has already refused a missing one — this is the
                // in-action half of the same check, as everywhere else here.
                int? userId = _tokenReader.GetUserIdFromToken();
                if (!userId.HasValue)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));
                }

                var roles = await _IAccountRegistrationService.GetRoleHierarchyAsync(userId.Value);

                // Empty means the caller's own role could not be placed in the
                // hierarchy — a deleted account, or a group id left over from the
                // retired six-role model. Not an empty list dressed as success:
                // a client would render an empty role picker and blame itself.
                if (roles == null || !roles.Any())
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UserGroupNotFound));

                return Ok(ApiResponseHandler.Success(roles, ResponseMessages.Success.UserGroupSuccess));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "role-hierarchy failed.");
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// Acknowledges the new-team popup: the caller's unseen team assignments
        /// (login: HasNewTeamAssignment / ManagedTeams[].IsNewAssignment) are marked
        /// seen, so the next login no longer reports them. Body UnitIds optional —
        /// omitted/empty acknowledges all.
        /// </summary>
        [Authorize]
        [HttpPost("managed-teams/acknowledge")]
        public async Task<IActionResult> AcknowledgeManagedTeams([FromBody] AcknowledgeManagedTeamsRequestDto? request)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (!userId.HasValue)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));
                }

                var acknowledged = await _userRoleAssignment.AcknowledgeManagedTeamsAsync(userId.Value, request?.UnitIds);
                var managedTeams = await _userRoleAssignment.GetManagedTeamsAsync(userId.Value);

                return Ok(ApiResponseHandler.Success(new
                {
                    Acknowledged = acknowledged,
                    HasNewTeamAssignment = managedTeams.Any(t => t.IsNewAssignment),
                    ManagedTeams = managedTeams
                }, "Team assignments acknowledged."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "managed-teams/acknowledge failed.");
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpGet("get-users")]
        public async Task<IActionResult> GetUserList([Required][FromQuery] int profileid)
        {
            try
            {
                int? userId = _tokenReader.GetTokenInfo();
                if (!userId.HasValue)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>(
                        "Invalid token - UserId missing."));
                }

                var users = await _service.GetUserList(profileid, userId.Value);
                _accountLock.ApplyLockState(users);

                if (users.Any())
                {
                    return Ok(ApiResponseHandler.Success(
                        users, ResponseMessages.Success.UsersList));
                }

                return Ok(ApiResponseHandler.Success(
                    users, ResponseMessages.Errors.NoRecords));
            }
            catch (Exception)
            {
                return StatusCode(500,
                    ApiResponseHandler.Fail<string>(
                        ResponseMessages.Errors.ExceptionMsg));
            }
        }


        [HttpGet("get-teammanagers")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> GetTeamManagers([Required][FromQuery] int profileId)
        {
            try
            {
                int? userId = _tokenReader.GetTokenInfo();
                if (!userId.HasValue)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>(
                        "Invalid token - UserId missing."));
                }

                var teamManagers = await _service.GetTeamManagers(profileId, userId.Value);

                return Ok(ApiResponseHandler.Success(
                    teamManagers,
                    teamManagers.Any()
                        ? ResponseMessages.Success.UsersList
                        : ResponseMessages.Errors.NoRecords));
            }
            catch
            {
                return StatusCode(500,
                    ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }


        [HttpPost("check-name-exist")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> CheckNameExistence([FromBody] NameCheckDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.NameRequired));

            bool exists;

            if (dto.IsCompany)
            {
                // Company names are no longer required to be unique, so this always
                // reports "available". Business unit names are still checked per
                // company below.
                exists = false;
            }
            else
            {
                var businessUnit = await _IAccountRegistrationService.BussinessNameExistsAsync(dto.Name, dto.ProfileId);
                exists = businessUnit != null;
            }

            if (exists)
            {
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Success.NameSuccess));
            }
            else
            {
                return Ok(ApiResponseHandler.Success<string>(null, ResponseMessages.Errors.NameFail));
            }
        }

        [HttpGet("check-email-exist")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> CheckEmailExists([FromQuery] string emailAddress)
        {
            if (string.IsNullOrWhiteSpace(emailAddress))
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.EmailReq));

            bool exists = await _IAccountRegistrationService.EmailExistsAsync(emailAddress.Trim().ToLower());

            if (exists)
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Success.EmailSuccess));
            else
                return Ok(ApiResponseHandler.Success<string>(null, ResponseMessages.Errors.EmailFail));
        }

        [HttpPost("logout")]
        public async Task<IActionResult> LogoutUser()
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (!userId.HasValue)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>("Invalid token - UserId missing."));
                }
                string token = _tokenReader.GetToken();
                var resp = await _IAccountRegistrationService.LogoutUser(userId.Value, token);

                if (!resp)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.TokenExpireAlready));

                return Ok(ApiResponseHandler.Success<string>(null, ResponseMessages.Success.UserLogout));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken()
        {
            try
            {
                int? userId = _tokenReader.GetTokenInfo();
                if (!userId.HasValue)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>("Invalid token - UserId missing."));
                }
                string token = _tokenReader.GetToken();
                var resp = await _IAccountRegistrationService.RefreshToken(userId.Value, token);

                if (!resp.isVaild)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.TokenExpireAlready));

                return Ok(ApiResponseHandler.Success<RefreshTokenDto>(resp, ResponseMessages.Success.RefreshToken));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPost("reset-password-manually")]
        public async Task<IActionResult> ResetPasswordManually([Required][FromQuery] int UserId)
        {
            // Step 1 - Check user exists
            var user = await _IAccountRegistrationService.GetUserById(UserId);
            if (user == null)
            {
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UserNotFound));
            }

            try
            {
                // Generate reset link
                string link = await _IAccountRegistrationService.GenerateManualResetPasswordLink(user);

                return Ok(ApiResponseHandler.Success(new
                {
                    ResetLink = link
                },
                ResponseMessages.Success.ResetPasswordLinkGenerated));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponseHandler.Fail<string>(ex.Message));
            }
        }

        [HttpPost("update-reset-password-manually")]
        public async Task<IActionResult> UpdateResetPassword([FromBody] ResetPasswordDto dto)
        {
            var result = await _IAccountRegistrationService.UpdateResetPasswordAsync(dto);

            if (!result.IsSuccess)
                return Ok(ApiResponseHandler.Fail<string>(result.Message));

            return Ok(ApiResponseHandler.Success<string>(null, result.Message));
        }
       

        [Authorize]
        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost("assign-referral")]
        public async Task<IActionResult> AssignReferral([FromBody] AssignReferralDto dto)
        {
            try
            {
                dto.ProfileId = dto.ReferredBy.HasValue && dto.ReferredBy > 0 ? dto.ReferredBy.Value : dto.ProfileId;
                dto.PartnerId = dto.ReferredTo.HasValue && dto.ReferredTo > 0 ? dto.ReferredTo.Value : dto.PartnerId;

                if (dto.ProfileId <= 0 || dto.PartnerId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidData));
                }

                var (success, message) = await _IAccountRegistrationService.AssignPartnerAsync(dto.ProfileId, dto.PartnerId);

                if (!success)
                {
                    return Ok(ApiResponseHandler.Fail<string>(message));
                }

                return Ok(ApiResponseHandler.Success<string>(null, ResponseMessages.Success.ReferralAssignedSuccess));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [Authorize]
        [HttpPost("unassign-partner")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> UnassignPartner([FromBody] UnassignPartnerDto dto)
        {
            try
            {
                if (dto.ProfileId <= 0 || dto.PartnerId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidData));
                }

                var (success, message) = await _IAccountRegistrationService.UnassignPartnerAsync(dto.ProfileId, dto.PartnerId);

                if (!success)
                {
                    return Ok(ApiResponseHandler.Fail<string>(message));
                }

                return Ok(ApiResponseHandler.Success<string>(null, "Partner unassigned successfully."));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpGet("partners-getall")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> GetAllPartnerCompanies()
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (!userId.HasValue)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>("Invalid token - UserId missing."));
                }
                var resp = await _IAccountRegistrationService.GetAllPartnerCompaniesAsync();
                return Ok(ApiResponseHandler.Success(resp, ResponseMessages.Success.CompanyDataFound));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPost("partner-company-list")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> GetPatnerCompanyList()
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (!userId.HasValue)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>("Invalid token - UserId missing."));
                }

                var resp = await _IAccountRegistrationService.GetCompanyPatnerListAsync(userId.Value);

                return Ok(ApiResponseHandler.Success(resp, ResponseMessages.Success.CompanyDataFound));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }
}

