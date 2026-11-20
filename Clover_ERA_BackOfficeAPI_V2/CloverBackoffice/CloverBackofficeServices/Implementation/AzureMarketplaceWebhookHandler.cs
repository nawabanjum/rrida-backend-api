using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloverBackoffice.Services.Implementation
{
    public class AzureMarketplaceWebhookHandler : IAzureMarketplaceWebhookHandler
    {
        private readonly IAzureMarketplaceService _api;
        private readonly IAzureMarketplaceSubscriptionRepository _repo;
        private readonly AzureMarketplaceSettings _settings;
        private readonly ILogger<AzureMarketplaceWebhookHandler> _logger;

        public AzureMarketplaceWebhookHandler(
            IAzureMarketplaceService api,
            IAzureMarketplaceSubscriptionRepository repo,
            IOptions<AzureMarketplaceSettings> settings,
            ILogger<AzureMarketplaceWebhookHandler> logger)
        {
            _api = api;
            _repo = repo;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<AzureMarketplaceResolvedSubscription> HandleLandingTokenAsync(string token, CancellationToken ct = default)
        {
            var resolved = await _api.ResolveAsync(token, ct);
            var subDetails = resolved.Subscription;
            var marketplaceSubId = subDetails?.Id ?? resolved.Id;

            var existing = await _repo.GetByMarketplaceIdAsync(marketplaceSubId);
            if (existing == null)
            {
                var entity = new AzureMarketplaceSubscription
                {
                    MarketplaceSubscriptionId = marketplaceSubId,
                    OfferId = subDetails?.OfferId ?? resolved.OfferId,
                    PlanId = subDetails?.PlanId ?? resolved.PlanId,
                    PublisherId = subDetails?.PublisherId,
                    SubscriptionName = subDetails?.Name ?? resolved.SubscriptionName,
                    Quantity = subDetails?.Quantity ?? resolved.Quantity,
                    Status = subDetails?.SaasSubscriptionStatus ?? AzureMarketplaceStatuses.PendingFulfillmentStart,
                    PurchaserEmail = subDetails?.Purchaser?.EmailId,
                    PurchaserTenantId = TryParseGuid(subDetails?.Purchaser?.TenantId),
                    PurchaserObjectId = subDetails?.Purchaser?.ObjectId,
                    BeneficiaryEmail = subDetails?.Beneficiary?.EmailId,
                    BeneficiaryTenantId = TryParseGuid(subDetails?.Beneficiary?.TenantId),
                    BeneficiaryObjectId = subDetails?.Beneficiary?.ObjectId,
                    TermUnit = subDetails?.Term?.TermUnit,
                    TermStartDate = subDetails?.Term?.StartDate,
                    TermEndDate = subDetails?.Term?.EndDate,
                    AutoRenew = subDetails?.AutoRenew ?? false,
                    IsTest = subDetails?.IsTest ?? false,
                    IsFreeTrial = subDetails?.IsFreeTrial ?? false
                };
                await _repo.CreateAsync(entity);
            }

            return resolved;
        }

        public async Task<bool> ActivateSubscriptionAsync(Guid marketplaceSubscriptionId, uint? profileId, CancellationToken ct = default)
        {
            var sub = await _repo.GetByMarketplaceIdAsync(marketplaceSubscriptionId);
            if (sub == null) return false;

            var ok = await _api.ActivateAsync(marketplaceSubscriptionId, sub.PlanId, sub.Quantity, ct);
            if (!ok) return false;

            sub.Status = AzureMarketplaceStatuses.Subscribed;
            sub.ActivatedAt = DateTime.UtcNow;
            if (profileId.HasValue) sub.ProfileId = profileId.Value;
            await _repo.UpdateAsync(sub);
            return true;
        }

        public async Task<bool> HandleAsync(AzureMarketplaceWebhookPayload payload, string rawJson, CancellationToken ct = default)
        {
            // Step 1: idempotency
            var existing = await _repo.GetWebhookEventByOperationIdAsync(payload.Id);
            if (existing != null && existing.ProcessingStatus == WebhookProcessingStatuses.Success)
            {
                _logger.LogInformation("Duplicate webhook {OpId} ignored", payload.Id);
                return true;
            }

            var evt = existing ?? await _repo.CreateWebhookEventAsync(new AzureMarketplaceWebhookEvent
            {
                OperationId = payload.Id,
                ActivityId = payload.ActivityId,
                MarketplaceSubscriptionId = payload.SubscriptionId,
                Action = payload.Action,
                IncomingStatus = payload.Status,
                PlanId = payload.PlanId,
                Quantity = payload.Quantity,
                RawPayload = rawJson,
                ProcessingStatus = WebhookProcessingStatuses.Received
            });

            // Step 2: verify with Microsoft (anti-spoof) — skipped in dev/test
            if (_settings.SkipVerificationForTesting)
            {
                _logger.LogWarning("Marketplace verification SKIPPED (test mode) for {OpId}", payload.Id);
                evt.VerifiedWithMicrosoft = false;
            }
            else
            {
                var operation = await _api.GetOperationAsync(payload.SubscriptionId, payload.Id, ct);
                if (operation == null)
                {
                    evt.ProcessingStatus = WebhookProcessingStatuses.VerificationFailed;
                    evt.ErrorMessage = "Operation not found in Marketplace API.";
                    evt.ProcessedAt = DateTime.UtcNow;
                    await _repo.UpdateWebhookEventAsync(evt);
                    return false;
                }
                evt.VerifiedWithMicrosoft = true;
            }
            evt.ProcessingStatus = WebhookProcessingStatuses.Processing;
            await _repo.UpdateWebhookEventAsync(evt);

            // Step 3: apply state change
            var ackStatus = "Success";
            try
            {
                await ApplyActionAsync(payload, ct);
                evt.ProcessingStatus = WebhookProcessingStatuses.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Webhook processing failed for {OpId}", payload.Id);
                evt.ProcessingStatus = WebhookProcessingStatuses.Failure;
                evt.ErrorMessage = ex.Message;
                ackStatus = "Failure";
            }

            // Step 4: ack Microsoft (must be < 10s of receiving) — skipped in test mode
            if (_settings.SkipVerificationForTesting)
            {
                evt.AckSentToMicrosoft = false;
            }
            else
            {
                var acked = await _api.PatchOperationAsync(payload.SubscriptionId, payload.Id, ackStatus,
                    payload.PlanId, payload.Quantity, ct);
                evt.AckSentToMicrosoft = acked;
            }
            evt.ProcessedAt = DateTime.UtcNow;
            await _repo.UpdateWebhookEventAsync(evt);

            return evt.ProcessingStatus == WebhookProcessingStatuses.Success;
        }

        private async Task ApplyActionAsync(AzureMarketplaceWebhookPayload payload, CancellationToken ct)
        {
            var sub = await _repo.GetByMarketplaceIdAsync(payload.SubscriptionId);
            if (sub == null)
            {
                // Pull from Microsoft if missing locally (rare — Subscribe before Resolve).
                // Skipped in test mode; create from payload only.
                AzureMarketplaceSubscriptionDetails? details = null;
                if (!_settings.SkipVerificationForTesting)
                {
                    try { details = await _api.GetSubscriptionAsync(payload.SubscriptionId, ct); }
                    catch (Exception ex) { _logger.LogWarning(ex, "GetSubscription failed; falling back to payload data."); }
                }

                sub = await _repo.CreateAsync(new AzureMarketplaceSubscription
                {
                    MarketplaceSubscriptionId = payload.SubscriptionId,
                    OfferId = details?.OfferId ?? payload.OfferId,
                    PlanId = details?.PlanId ?? payload.PlanId,
                    PublisherId = details?.PublisherId ?? payload.PublisherId,
                    SubscriptionName = details?.Name,
                    Quantity = details?.Quantity ?? payload.Quantity,
                    Status = details?.SaasSubscriptionStatus ?? AzureMarketplaceStatuses.PendingFulfillmentStart,
                    PurchaserEmail = details?.Purchaser?.EmailId,
                    PurchaserTenantId = TryParseGuid(details?.Purchaser?.TenantId),
                    BeneficiaryEmail = details?.Beneficiary?.EmailId,
                    BeneficiaryTenantId = TryParseGuid(details?.Beneficiary?.TenantId),
                    TermUnit = details?.Term?.TermUnit,
                    TermStartDate = details?.Term?.StartDate,
                    TermEndDate = details?.Term?.EndDate,
                    AutoRenew = details?.AutoRenew ?? false,
                    IsTest = details?.IsTest ?? false,
                    IsFreeTrial = details?.IsFreeTrial ?? false
                });
            }

            switch (payload.Action)
            {
                case AzureMarketplaceActions.Subscribe:
                    sub.Status = AzureMarketplaceStatuses.Subscribed;
                    sub.ActivatedAt ??= DateTime.UtcNow;
                    break;

                case AzureMarketplaceActions.ChangePlan:
                    sub.PlanId = payload.PlanId;
                    break;

                case AzureMarketplaceActions.ChangeQuantity:
                    sub.Quantity = payload.Quantity;
                    break;

                case AzureMarketplaceActions.Suspend:
                    sub.Status = AzureMarketplaceStatuses.Suspended;
                    break;

                case AzureMarketplaceActions.Reinstate:
                    sub.Status = AzureMarketplaceStatuses.Subscribed;
                    break;

                case AzureMarketplaceActions.Renew:
                    if (!_settings.SkipVerificationForTesting)
                    {
                        try
                        {
                            var renewed = await _api.GetSubscriptionAsync(payload.SubscriptionId, ct);
                            if (renewed?.Term != null)
                            {
                                sub.TermStartDate = renewed.Term.StartDate;
                                sub.TermEndDate = renewed.Term.EndDate;
                            }
                        }
                        catch (Exception ex) { _logger.LogWarning(ex, "Renew fetch failed."); }
                    }
                    break;

                case AzureMarketplaceActions.Unsubscribe:
                    sub.Status = AzureMarketplaceStatuses.Unsubscribed;
                    sub.UnsubscribedAt = DateTime.UtcNow;
                    break;

                default:
                    _logger.LogWarning("Unknown marketplace action {Action}", payload.Action);
                    break;
            }

            await _repo.UpdateAsync(sub);
        }

        private static Guid? TryParseGuid(string? s)
            => Guid.TryParse(s, out var g) ? g : null;
    }
}
