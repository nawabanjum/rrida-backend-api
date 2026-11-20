using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface ISystemNotificationRepository
    {
        Task<SystemNotification> AddAsync(SystemNotification notification);
        Task AddBulkAsync(List<SystemNotification> notifications);
        Task<List<int>> GetAdminUserIdsAsync();
        Task<int?> GetProfileIdByUserIdAsync(int userId);
        Task<List<int>> GetCompanyAdminUserIdsAsync(int profileId);
        Task<List<SystemNotification>> GetByUserIdAsync(int userId, int page, int pageSize);
        Task<int> GetTotalCountByUserIdAsync(int userId);
        Task<int> GetUnreadCountByUserIdAsync(int userId);
        Task<SystemNotification?> GetByIdAsync(int id);
        Task<bool> MarkAsReadAsync(int id, int userId);
        Task<int> MarkAllAsReadAsync(int userId);
        Task<List<int>> GetTeamManagerUserIdsAsync(int unitId);
    }
}
