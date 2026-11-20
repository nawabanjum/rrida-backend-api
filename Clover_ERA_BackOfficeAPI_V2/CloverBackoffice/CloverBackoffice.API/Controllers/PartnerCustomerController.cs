using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.Controllers
{
    [ApiController]
    [Route("api/partner-customers")]
    [Authorize]
    public class PartnerCustomerController : ControllerBase
    {
        private readonly IPartnerCustomerService _service;
        private readonly ITokenReaderService _tokenReader;

        public PartnerCustomerController(IPartnerCustomerService service, ITokenReaderService tokenReader)
        {
            _service = service;
            _tokenReader = tokenReader;
        }

        // BE-03: GET /api/partner-customers/partners — Admin only
        [HttpGet("partners")]
        public async Task<IActionResult> GetPartners()
        {
            try
            {
                var user = await _tokenReader.GetCurrentUserAsync();
                if (user == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidToken));

                if (user.UserGroupId != (int)UserGroups.RridaAdmin)
                    return StatusCode(403, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.PartnerCustomerAccessDenied));

                var result = await _service.GetAllPartnersAsync();
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        // BE-04: GET /api/partner-customers/customers — Admin = all, Partner = own
        [HttpGet("customers")]
        public async Task<IActionResult> GetCustomers()
        {
            try
            {
                var user = await _tokenReader.GetCurrentUserAsync();
                if (user == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidToken));

                if (user.UserGroupId != (int)UserGroups.RridaAdmin &&
                    user.UserGroupId != (int)UserGroups.ProfileAdmin)
                    return StatusCode(403, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.PartnerCustomerAccessDenied));

                var result = await _service.GetCustomersAsync(user.UserId);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        // BE-05: POST /api/partner-customers/customers — Admin + Partner
        [HttpPost("customers")]
        public async Task<IActionResult> CreateCustomer([FromBody] CreateCustomerDto dto)
        {
            try
            {
                var user = await _tokenReader.GetCurrentUserAsync();
                if (user == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidToken));

                if (user.UserGroupId != (int)UserGroups.RridaAdmin &&
                    user.UserGroupId != (int)UserGroups.ProfileAdmin)
                    return StatusCode(403, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.PartnerCustomerAccessDenied));

                if (!ModelState.IsValid)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidData));

                var (success, message, customer) = await _service.CreateCustomerAsync(dto, user.UserId);

                if (!success)
                    return Ok(ApiResponseHandler.Fail<string>(message));

                return Ok(ApiResponseHandler.Success(customer, message));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        // BE-06: PATCH /api/partner-customers/customers/{id}/reassign — Admin only
        // profileId, not id: a customer IS a Profile, and CompanyTenantGuardFilter
        // only recognises tenant ids by argument name. Same URL as before.
        [HttpPatch("customers/{profileId}/reassign")]
        public async Task<IActionResult> ReassignCustomer(uint profileId, [FromBody] ReassignCustomerDto dto)
        {
            try
            {
                var user = await _tokenReader.GetCurrentUserAsync();
                if (user == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidToken));

                if (user.UserGroupId != (int)UserGroups.RridaAdmin &&
                    user.UserGroupId != (int)UserGroups.ProfileAdmin)
                    return StatusCode(403, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.PartnerCustomerAccessDenied));

                if (!ModelState.IsValid)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidData));

                var (success, message) = await _service.ReassignCustomerAsync(profileId, dto, user.UserId);

                if (!success)
                    return Ok(ApiResponseHandler.Fail<string>(message));

                return Ok(ApiResponseHandler.Success<string>(null, message));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        // BE-10: DELETE /api/partner-customers/partners/{id} — Admin only
        // profileId, not id — see ReassignCustomer above. Same URL as before.
        [HttpDelete("partners/{profileId}")]
        public async Task<IActionResult> DeletePartner(uint profileId)
        {
            try
            {
                var user = await _tokenReader.GetCurrentUserAsync();
                if (user == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidToken));

                if (user.UserGroupId != (int)UserGroups.RridaAdmin &&
                    user.UserGroupId != (int)UserGroups.ProfileAdmin)
                    return StatusCode(403, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.PartnerCustomerAccessDenied));

                var (success, message) = await _service.SoftDeletePartnerAsync(profileId, user.UserId);

                if (!success)
                    return Ok(ApiResponseHandler.Fail<string>(message));

                return Ok(ApiResponseHandler.Success<string>(null, message));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }
}
