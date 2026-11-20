using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Implementation
{
    public class AzureMarketplaceSubscriptionRepository : IAzureMarketplaceSubscriptionRepository
    {
        private readonly ApplicationDbContext _context;

        public AzureMarketplaceSubscriptionRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<AzureMarketplaceSubscription?> GetByMarketplaceIdAsync(Guid marketplaceSubscriptionId)
        {
            return await _context.AzureMarketplaceSubscriptions
                .FirstOrDefaultAsync(s => s.MarketplaceSubscriptionId == marketplaceSubscriptionId && !s.IsDel);
        }

        public async Task<AzureMarketplaceSubscription?> GetByProfileIdAsync(uint profileId)
        {
            return await _context.AzureMarketplaceSubscriptions
                .Where(s => s.ProfileId == profileId && !s.IsDel)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<AzureMarketplaceSubscription> CreateAsync(AzureMarketplaceSubscription subscription)
        {
            subscription.CreatedAt = DateTime.UtcNow;
            subscription.UpdatedAt = DateTime.UtcNow;
            await _context.AzureMarketplaceSubscriptions.AddAsync(subscription);
            await _context.SaveChangesAsync();
            return subscription;
        }

        public async Task<AzureMarketplaceSubscription> UpdateAsync(AzureMarketplaceSubscription subscription)
        {
            subscription.UpdatedAt = DateTime.UtcNow;
            _context.AzureMarketplaceSubscriptions.Update(subscription);
            await _context.SaveChangesAsync();
            return subscription;
        }

        public async Task<AzureMarketplaceWebhookEvent?> GetWebhookEventByOperationIdAsync(Guid operationId)
        {
            return await _context.AzureMarketplaceWebhookEvents
                .FirstOrDefaultAsync(e => e.OperationId == operationId);
        }

        public async Task<AzureMarketplaceWebhookEvent> CreateWebhookEventAsync(AzureMarketplaceWebhookEvent evt)
        {
            evt.ReceivedAt = DateTime.UtcNow;
            await _context.AzureMarketplaceWebhookEvents.AddAsync(evt);
            await _context.SaveChangesAsync();
            return evt;
        }

        public async Task<AzureMarketplaceWebhookEvent> UpdateWebhookEventAsync(AzureMarketplaceWebhookEvent evt)
        {
            _context.AzureMarketplaceWebhookEvents.Update(evt);
            await _context.SaveChangesAsync();
            return evt;
        }
    }
}
