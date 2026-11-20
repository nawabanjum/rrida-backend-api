using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface ISystemNotificationService
    {
        Task<SystemNotification?> CreateNotificationAsync(CreateSystemNotificationDto dto);
        Task CreateNotificationForAdminsAsync(CreateSystemNotificationDto dto);
        Task CreateNotificationForCompanyAdminsAsync(int userId, CreateSystemNotificationDto dto);
        Task<PaginatedNotificationResponseDto> GetNotificationsByUserIdAsync(int userId, int page, int pageSize);
        Task<int> GetUnreadCountAsync(int userId);
        Task<bool> MarkAsReadAsync(int id, int userId);
        Task<int> MarkAllAsReadAsync(int userId);
        Task CreateNotificationForTeamManagersAsync(int unitId, CreateSystemNotificationDto dto, int? excludeUserId = null);
    }
}
