using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IAzureMarketplaceWebhookHandler
    {
        Task<bool> HandleAsync(AzureMarketplaceWebhookPayload payload, string rawJson, CancellationToken ct = default);
        Task<AzureMarketplaceResolvedSubscription> HandleLandingTokenAsync(string token, CancellationToken ct = default);
        Task<bool> ActivateSubscriptionAsync(Guid marketplaceSubscriptionId, uint? profileId, CancellationToken ct = default);
    }
}
