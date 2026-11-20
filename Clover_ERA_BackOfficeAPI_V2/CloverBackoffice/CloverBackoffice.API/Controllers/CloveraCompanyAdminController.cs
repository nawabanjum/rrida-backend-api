using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Components;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    [Route("api/cloveracompanyadmin")]
    [ApiController]
    [Authorize]
    public class CloveraCompanyAdminController : ControllerBase
    {
        private readonly ICloveraCompanyAdminService _ICloveraCompanyAdminService;
        private readonly IAccountRegistrationService _IAccountRegistrationService;
        private readonly SendGridSettings _sendGridSettings;
        private readonly ITokenReaderService _tokenReader;
        public CloveraCompanyAdminController(ICloveraCompanyAdminService cloveracompanyadminService, IAccountRegistrationService registrationService, SendGridSettings sendGridSettings, ITokenReaderService tokenReader)
        {
            _ICloveraCompanyAdminService = cloveracompanyadminService;
            _IAccountRegistrationService = registrationService;
            _sendGridSettings = sendGridSettings;
            _tokenReader = tokenReader;
        }

        [HttpPost("create-company-admin")]
        public async Task<IActionResult> CreateCompanyAdmin([FromBody] CloveraCompanyCreateDto dto)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (userId == null)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));
                }

                // Step 1: Validate if the domain is allowed (e.g., from a JSON whitelist)

                var result = await _IAccountRegistrationService.checkCompany(dto.EmailAddress);

                // Mirrors /api/company/Create and /CreateTrial: this endpoint always
                // provisions a NEW company, so it must not care that the email's
                // domain already maps to an existing (and possibly unverified) one.
                //
                // checkCompany returns IsValid=false for BOTH "the email/domain is
                // bad" and "some other company on this domain has an unverified
                // admin". Only the first is a real problem here. Previously the raw
                // !IsValid check rejected the second case too, so e.g.
                // advento@yopmail.com failed with "Email is not valid." purely
                // because an unrelated company on yopmail.com had an unverified
                // admin — and which company got picked was itself non-deterministic
                // (GetCompanyByDomainAsync uses FirstOrDefault with no OrderBy).
                var emailMalformed =
                    !result.IsValid &&
                    !result.CompanyNotVerified;
                if (emailMalformed)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidEmail));

                // Duplicate company names are allowed — see /api/company/Create.

                var emailExits = await _IAccountRegistrationService.ContactEmailExistsAsync(dto.EmailAddress);
                if (emailExits != null)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ContactEmailUnique));

                // Identity-store check — Contacts misses accounts created through the
                // add-user endpoint, which write Users only. See /api/company/Create.
                var userExists = await _IAccountRegistrationService.UserEmailExistsAsync(dto.EmailAddress);
                if (userExists != null)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.EmailAlreadyExist));

                // Step 2: Proceed with company + user creation
                var resp = await _ICloveraCompanyAdminService.CreateCompanyWithUserAsync(dto);

                // Records the link as single-use before sending it.
                await _IAccountRegistrationService.SendInvitationEmailAsync(
                    resp.Email, resp.UserId, dto.CompanyName);
                return Ok(ApiResponseHandler.Success(new
                {
                    userId = resp.UserId,
                    email = resp.Email,
                    profileId = resp.ProfileId
                }, resp.Message));
            }
            catch (Exception ex)
            {
                // Used to report "Company name must be unique." for ANY failure,
                // which now reads as a validation that was never removed.
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPost("update-company-partner-status")]
        public async Task<IActionResult> UpdateCompanyPartnerStatus([FromBody] UpdateCompanyPartnerStatusDto dto)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (userId == null)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));
                }

                if (dto.ProfileId <= 0)
                    return Ok(ApiResponseHandler.Fail<string>("Invalid company id."));


                var result = await _ICloveraCompanyAdminService.UpdatePartnerStatusAsync(dto.ProfileId, dto.IsPartner);

                if (!result)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.OperationFail));

                return Ok(ApiResponseHandler.Success(new
                {
                    profileId = dto.ProfileId,
                    isPartner = dto.IsPartner
                }, ResponseMessages.Success.PartnerStatusSuccess));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }
}
