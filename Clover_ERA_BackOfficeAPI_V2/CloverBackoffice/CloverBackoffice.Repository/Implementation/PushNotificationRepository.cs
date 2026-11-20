using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    public class PushNotificationRepository : IPushNotificationRepository
    {
        private readonly ApplicationDbContext _context;

        public PushNotificationRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<PushNotificationPreference> GetByUserIdAsync(int userId)
        {
            return await _context.PushNotificationPreferences
                .FirstOrDefaultAsync(p => p.UserId == userId);
        }

        public async Task<bool> IsEnabledForUserAsync(int userId)
        {
            return await _context.PushNotificationPreferences
                .AnyAsync(p => p.UserId == userId && p.IsEnabled);
        }

        public async Task<List<string>> GetEnabledPlayerIdsByUserIdsAsync(List<int> userIds)
        {
            return await _context.PushNotificationPreferences
                .Where(p => userIds.Contains(p.UserId) && p.IsEnabled && !string.IsNullOrWhiteSpace(p.OneSignalPlayerId))
                .Select(p => p.OneSignalPlayerId)
                .Distinct()
                .ToListAsync();
        }

        public async Task<UserSystemDetails> SaveSystemDetailsAsync(UserSystemDetails systemDetails)
        {
            systemDetails.CreatedAt = DateTime.UtcNow;
            systemDetails.LoginTime = DateTime.UtcNow;

            await _context.UserSystemDetails.AddAsync(systemDetails);
            await _context.SaveChangesAsync();

            return systemDetails;
        }
        public async Task<UserSystemDetails?> GetLatestUserSystemDetailsAsync(int userId)
        {
            return await _context.UserSystemDetails
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();
        }

    }
}
