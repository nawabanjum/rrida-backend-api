using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    [Route("api/license")]
    [ApiController]
    [Authorize]
    public class LicenseController : ControllerBase
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly ITokenReaderService _tokenReader;

        public LicenseController(ISubscriptionService subscriptionService, ITokenReaderService tokenReader)
        {
            _subscriptionService = subscriptionService;
            _tokenReader = tokenReader;
        }

        // Changes a Team Manager's licence billing cycle through Stripe Checkout.
        // Nothing changes until the checkout is completed; the webhook then moves
        // the licence to the new cycle and credits the unused time of the old one.
        //
        //   POST /api/license/change
        //   Body: { "userId": 10, "billingCycle": "annual" }   // "monthly" | "annual"
        [HttpPost("change")]
        public async Task<IActionResult> ChangePlan([FromBody] LicensePlanChangeRequestDto dto)
        {
            try
            {
                if (dto?.UserId is null or <= 0)
                    return BadRequest(ApiResponseHandler.Fail<string>("UserId is required."));

                var cycle = ParseBillingCycle(dto.BillingCycle);
                if (cycle == null)
                    return BadRequest(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidLicenseBillingCycle));

                var actor = await _tokenReader.GetCurrentUserAsync();
                if (actor == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                var result = await _subscriptionService.StartLicensePlanChangeAsync(dto.UserId.Value, cycle.Value, actor);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.LicensePlanChangeCheckout));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }

        private static BillingCycle? ParseBillingCycle(string? value) => value?.Trim().ToLowerInvariant() switch
        {
            "monthly" => BillingCycle.Monthly,
            "annual" => BillingCycle.Annual,
            _ => null
        };
    }
}
