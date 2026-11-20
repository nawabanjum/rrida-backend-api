using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IPushNotificationService
    {
       
        /// <summary>
        /// Send report notification to team manager
        /// </summary>
        Task<(bool IsSuccess, string Message)> SendReportNotificationAsync(int teamManagerId, long reportId, long unitId, long profileId, string reportTitle);

        /// <summary>
        /// Send notification to multiple users
        /// </summary>
        Task<(bool IsSuccess, string Message)> SendNotificationToUsersAsync(List<int> userIds, string title, string message, object data = null);

        /// <summary>
        /// Save user system details (device, browser, OS info)
        /// </summary>
        Task<(bool IsSuccess, string Message, UserSystemDetailsDto Data)> SaveSystemDetailsAsync(int userId, string ipAddress, string userAgent, string? oneSignalPlayerId = null);
    /// -----------------
        Task<MatchResultDto> CheckForMatchingSystemDetailsAsync(SystemDetailsMatchRequestDto matchRequest);
        Task<bool> SendNotificationIfSystemDetailsMatchAsync(SystemDetailsMatchRequestDto matchRequest);

        // New method to update latest record with OneSignalPlayerId
        Task<(bool IsSuccess, string Message, UserSystemDetailsDto Data)> UpdateLatestRecordWithPlayerIdAsync(
            int userId,
            string oneSignalPlayerId);

        // New method for security notifications with custom titles/messages
        Task<bool> SendSecurityNotificationIfMatchFoundAsync(
            SecurityNotificationRequestDto request,
            string? deviceModel = null,
            string? machineName = null,
            string? oneSignalPlayerId = null,string? link = null);

        // Generic notification method (extends existing SendNotificationToUsersAsync)
        Task<(bool IsSuccess, string Message)> SendNotificationAsync(
            GenericNotificationRequestDto request);
    }
}
