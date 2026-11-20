using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IAzureMarketplaceSubscriptionRepository
    {
        Task<AzureMarketplaceSubscription?> GetByMarketplaceIdAsync(Guid marketplaceSubscriptionId);
        Task<AzureMarketplaceSubscription?> GetByProfileIdAsync(uint profileId);
        Task<AzureMarketplaceSubscription> CreateAsync(AzureMarketplaceSubscription subscription);
        Task<AzureMarketplaceSubscription> UpdateAsync(AzureMarketplaceSubscription subscription);

        Task<AzureMarketplaceWebhookEvent?> GetWebhookEventByOperationIdAsync(Guid operationId);
        Task<AzureMarketplaceWebhookEvent> CreateWebhookEventAsync(AzureMarketplaceWebhookEvent evt);
        Task<AzureMarketplaceWebhookEvent> UpdateWebhookEventAsync(AzureMarketplaceWebhookEvent evt);
    }
}
