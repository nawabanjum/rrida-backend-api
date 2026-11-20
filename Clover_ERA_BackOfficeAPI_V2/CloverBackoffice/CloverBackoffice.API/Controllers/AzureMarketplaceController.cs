using System.Text;
using System.Text.Json;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CloverBackoffice.API.Controllers
{
    [Route("api/azure-marketplace")]
    [ApiController]
    public class AzureMarketplaceController : ControllerBase
    {
        private readonly IAzureMarketplaceWebhookHandler _handler;
        private readonly AzureMarketplaceSettings _settings;
        private readonly ILogger<AzureMarketplaceController> _logger;

        public AzureMarketplaceController(
            IAzureMarketplaceWebhookHandler handler,
            IOptions<AzureMarketplaceSettings> settings,
            ILogger<AzureMarketplaceController> logger)
        {
            _handler = handler;
            _settings = settings.Value;
            _logger = logger;
        }

        // Microsoft redirects buyer here after purchase: /api/azure-marketplace/landing?token=xxx
        [HttpGet("landing")]
        [AllowAnonymous]
        public async Task<IActionResult> Landing([FromQuery] string token, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(token))
                return BadRequest(ApiResponseHandler.Fail<string>("Missing marketplace token."));

            try
            {
                var resolved = await _handler.HandleLandingTokenAsync(token, ct);
                var subId = resolved.Subscription?.Id ?? resolved.Id;

                if (_settings.AutoActivateOnLanding)
                {
                    await _handler.ActivateSubscriptionAsync(subId, profileId: null, ct);
                    var html = $@"<!doctype html><html><head><meta charset=""utf-8""><title>Subscription activated</title>
<style>body{{font-family:Segoe UI,Arial,sans-serif;max-width:560px;margin:80px auto;padding:24px;text-align:center;color:#222}}
h1{{color:#0a7d2c}}.box{{background:#f6f8fa;padding:16px;border-radius:8px;margin-top:24px;text-align:left;font-size:13px}}</style></head>
<body><h1>Subscription activated</h1><p>Thank you. Your CloverEra subscription is now active.</p>
<div class=""box""><strong>Subscription ID:</strong> {subId}<br/><strong>Offer:</strong> {resolved.OfferId}<br/><strong>Plan:</strong> {resolved.PlanId}</div>
</body></html>";
                    return Content(html, "text/html");
                }

                var redirect = string.IsNullOrWhiteSpace(_settings.LandingPageRedirectUrl)
                    ? null
                    : $"{_settings.LandingPageRedirectUrl}?subId={subId}&offerId={resolved.OfferId}&planId={resolved.PlanId}";

                if (!string.IsNullOrEmpty(redirect))
                    return Redirect(redirect);

                return Ok(ApiResponseHandler.Success(resolved, "Marketplace token resolved."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Marketplace landing failed");
                return StatusCode(500, ApiResponseHandler.Fail<string>("Failed to resolve marketplace token."));
            }
        }

        // Frontend calls after customer completes setup
        [HttpPost("activate/{marketplaceSubscriptionId:guid}")]
        [Authorize]
        public async Task<IActionResult> Activate(Guid marketplaceSubscriptionId, [FromQuery] uint? profileId, CancellationToken ct)
        {
            var ok = await _handler.ActivateSubscriptionAsync(marketplaceSubscriptionId, profileId, ct);
            return ok
                ? Ok(ApiResponseHandler.Success(true, "Subscription activated."))
                : Ok(ApiResponseHandler.Fail<bool>("Activation failed."));
        }

        // Microsoft POSTs lifecycle events here. Unauthenticated; verified inside handler.
        [HttpPost("webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> Webhook(CancellationToken ct)
        {
            string raw;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
                raw = await reader.ReadToEndAsync(ct);

            if (string.IsNullOrWhiteSpace(raw))
                return BadRequest();

            AzureMarketplaceWebhookPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<AzureMarketplaceWebhookPayload>(raw,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invalid webhook JSON");
                return BadRequest();
            }
            if (payload == null || payload.Id == Guid.Empty || payload.SubscriptionId == Guid.Empty)
                return BadRequest();

            // Fire-and-return-fast: must reply within 10s. Run handler synchronously here for simplicity.
            // For heavy workloads queue the work and return 200 immediately.
            await _handler.HandleAsync(payload, raw, ct);
            return Ok();
        }
    }
}
