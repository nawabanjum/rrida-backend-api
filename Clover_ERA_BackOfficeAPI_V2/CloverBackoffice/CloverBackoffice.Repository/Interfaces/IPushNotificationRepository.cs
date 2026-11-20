using CloverBackoffice.Infrastructure.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IPushNotificationRepository
    {
        /// <summary>
        /// Get push notification preference by user ID
        /// </summary>
        Task<PushNotificationPreference> GetByUserIdAsync(int userId);

        /// <summary>
        /// Check if user has push notifications enabled
        /// </summary>
        Task<bool> IsEnabledForUserAsync(int userId);

        /// <summary>
        /// Get all enabled OneSignal player IDs for a list of user IDs
        /// </summary>
        Task<List<string>> GetEnabledPlayerIdsByUserIdsAsync(List<int> userIds);

        /// <summary>
        /// Save user system details (device, browser, OS info)
        /// </summary>
        Task<UserSystemDetails> SaveSystemDetailsAsync(UserSystemDetails systemDetails);
        Task<UserSystemDetails?> GetLatestUserSystemDetailsAsync(int userId);
    }
}
