using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CloverBackoffice.API.Controllers
{
    [Route("api/payment")]
    [ApiController]
    public class PaymentController : ControllerBase
    {
        private readonly IPaymentService _paymentService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IStripeService _stripeService;
        private readonly ITokenReaderService _tokenReader;
        private readonly StripeSettings _stripeSettings;
        private readonly ILogger<PaymentController> _logger;

        public PaymentController(
            IPaymentService paymentService,
            ISubscriptionService subscriptionService,
            IStripeService stripeService,
            ITokenReaderService tokenReader,
            IOptions<StripeSettings> stripeSettings,
            ILogger<PaymentController> logger)
        {
            _paymentService = paymentService;
            _subscriptionService = subscriptionService;
            _stripeService = stripeService;
            _tokenReader = tokenReader;
            _stripeSettings = stripeSettings.Value;
            _logger = logger;
        }

        [HttpGet("history")]
        [Authorize]
        public async Task<IActionResult> GetHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            try
            {
                var user = await _tokenReader.GetCurrentUserAsync();
                if (user == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                if (user.ProfileId == null)
                    return Ok(ApiResponseHandler.Fail<string>("No company linked to this account."));

                var profileId = (uint)user.ProfileId.Value;
                var payments = await _paymentService.GetPaymentHistoryAsync(profileId, page, pageSize);
                var total = await _paymentService.GetPaymentTotalCountAsync(profileId);

                return Ok(ApiResponseHandler.Success(new { payments, total, page, pageSize }, "Payment history retrieved."));
            }
            catch (Exception)
            {
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPost("request-invoice")]
        [ApiExplorerSettings(IgnoreApi = true)]
        [Authorize]
        public async Task<IActionResult> RequestInvoice([FromBody] RequestInvoiceDto dto)
        {
            try
            {
                var user = await _tokenReader.GetCurrentUserAsync();
                if (user == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                if (user.ProfileId == null)
                    return Ok(ApiResponseHandler.Fail<string>("No company linked to this account."));

                var result = await _paymentService.RequestInvoicePaymentAsync((uint)user.ProfileId.Value, dto.CompanySubscriptionId);
                return Ok(ApiResponseHandler.Success(result, "Invoice request submitted. Pending admin verification."));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception)
            {
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPut("{id}/verify-invoice")]
        [ApiExplorerSettings(IgnoreApi = true)]
        [Authorize]
        public async Task<IActionResult> VerifyInvoice(int id)
        {
            try
            {
                var isAdmin = await _tokenReader.IsAdminAsync();
                if (!isAdmin)
                    return StatusCode(403, ApiResponseHandler.Fail<string>("Access denied. Clover ERA Admin only."));

                var user = await _tokenReader.GetCurrentUserAsync();
                var result = await _paymentService.VerifyInvoiceAsync(id, user!.UserId);
                return Ok(ApiResponseHandler.Success(result, "Invoice verified successfully."));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception)
            {
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPost("webhook")]
        [ApiExplorerSettings(IgnoreApi = true)]
        [AllowAnonymous]
        public async Task<IActionResult> Webhook()
        {
            try
            {
                var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
                var signature = Request.Headers["Stripe-Signature"].FirstOrDefault();

                if (string.IsNullOrEmpty(signature))
                {
                    _logger.LogWarning("Webhook called without Stripe-Signature header.");
                    return BadRequest(ApiResponseHandler.Fail<string>("Missing Stripe-Signature header."));
                }

                _logger.LogInformation("Webhook received with signature, processing...");

                var stripeEvent = _stripeService.ConstructWebhookEvent(json, signature, _stripeSettings.WebhookSecret);
                await _subscriptionService.HandleWebhookEventAsync(stripeEvent);

                return Ok();
            }
            catch (Stripe.StripeException ex)
            {
                _logger.LogError(ex, "Stripe webhook signature validation failed.");
                return BadRequest(ApiResponseHandler.Fail<string>($"Webhook error: {ex.Message}"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Webhook processing failed.");
                return StatusCode(500, ApiResponseHandler.Fail<string>("Webhook processing failed."));
            }
        }

//#if DEBUG
//        // TEMP test-only endpoint for verifying card-detail capture. Compiled
//        // out of Release builds. Pass an existing test-mode pi_... to inspect a
//        // real charge, or omit it to mint a confirmed PaymentIntent with the
//        // canned pm_card_visa (expects last4=4242, brand=visa).
//        [HttpGet("debug/card-details")]
//        [AllowAnonymous]
//        public async Task<IActionResult> DebugCardDetails([FromQuery] string? paymentIntentId = null)
//        {
//            try
//            {
//                if (string.IsNullOrEmpty(paymentIntentId))
//                {
//                    var pi = await new Stripe.PaymentIntentService().CreateAsync(new Stripe.PaymentIntentCreateOptions
//                    {
//                        Amount = 500,
//                        Currency = "usd",
//                        PaymentMethod = "pm_card_visa",
//                        Confirm = true,
//                        AutomaticPaymentMethods = new Stripe.PaymentIntentAutomaticPaymentMethodsOptions
//                        {
//                            Enabled = true,
//                            AllowRedirects = "never"
//                        }
//                    });
//                    paymentIntentId = pi.Id;
//                }

//                var (last4, brand) = await _stripeService.GetCardDetailsAsync(paymentIntentId);
//                return Ok(ApiResponseHandler.Success(new { paymentIntentId, last4, brand }, "Card details fetched."));
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Debug card-details failed.");
//                return Ok(ApiResponseHandler.Fail<string>($"Debug error: {ex.Message}"));
//            }
//        }
//#endif
    }
}
