using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IAzureMarketplaceService
    {
        Task<AzureMarketplaceResolvedSubscription> ResolveAsync(string marketplaceToken, CancellationToken ct = default);
        Task<bool> ActivateAsync(Guid subscriptionId, string planId, int quantity, CancellationToken ct = default);
        Task<AzureMarketplaceSubscriptionDetails?> GetSubscriptionAsync(Guid subscriptionId, CancellationToken ct = default);
        Task<AzureMarketplaceOperation?> GetOperationAsync(Guid subscriptionId, Guid operationId, CancellationToken ct = default);
        Task<bool> PatchOperationAsync(Guid subscriptionId, Guid operationId, string status, string? planId = null, int? quantity = null, CancellationToken ct = default);
    }
}
