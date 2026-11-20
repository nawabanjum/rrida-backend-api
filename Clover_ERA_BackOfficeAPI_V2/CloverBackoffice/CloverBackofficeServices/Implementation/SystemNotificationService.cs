using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    public class SystemNotificationService : ISystemNotificationService
    {
        private readonly ISystemNotificationRepository _repository;

        public SystemNotificationService(ISystemNotificationRepository repository)
        {
            _repository = repository;
        }

        public async Task<SystemNotification?> CreateNotificationAsync(CreateSystemNotificationDto dto)
        {
            try
            {
                var notification = new SystemNotification
                {
                    UserId = dto.UserId,
                    Title = dto.Title,
                    Message = dto.Message,
                    NotificationType = dto.NotificationType,
                    IpAddress = dto.IpAddress,
                    NotificationUrl = dto.NotificationUrl,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                };

                return await _repository.AddAsync(notification);
            }
            catch
            {
                return null;
            }
        }

        public async Task CreateNotificationForAdminsAsync(CreateSystemNotificationDto dto)
        {
            try
            {
                var adminUserIds = await _repository.GetAdminUserIdsAsync();
                if (!adminUserIds.Any()) return;

                var notifications = adminUserIds.Select(adminId => new SystemNotification
                {
                    UserId = adminId,
                    Title = dto.Title,
                    Message = dto.Message,
                    NotificationType = dto.NotificationType,
                    IpAddress = dto.IpAddress,
                    NotificationUrl = dto.NotificationUrl,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                }).ToList();

                await _repository.AddBulkAsync(notifications);
            }
            catch
            {
                // Silent - notification failure should not break the main operation
            }
        }

        public async Task CreateNotificationForCompanyAdminsAsync(int userId, CreateSystemNotificationDto dto)
        {
            try
            {
                var profileId = await _repository.GetProfileIdByUserIdAsync(userId);
                if (profileId == null || profileId <= 0) return;

                var companyAdminIds = await _repository.GetCompanyAdminUserIdsAsync(profileId.Value);
                if (!companyAdminIds.Any()) return;

                var notifications = companyAdminIds.Select(adminId => new SystemNotification
                {
                    UserId = adminId,
                    Title = dto.Title,
                    Message = dto.Message,
                    NotificationType = dto.NotificationType,
                    IpAddress = dto.IpAddress,
                    NotificationUrl = dto.NotificationUrl,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                }).ToList();

                await _repository.AddBulkAsync(notifications);
            }
            catch
            {
                // Silent - notification failure should not break the main operation
            }
        }

        public async Task<PaginatedNotificationResponseDto> GetNotificationsByUserIdAsync(int userId, int page, int pageSize)
        {
            var notifications = await _repository.GetByUserIdAsync(userId, page, pageSize);
            var totalCount = await _repository.GetTotalCountByUserIdAsync(userId);

            return new PaginatedNotificationResponseDto
            {
                Notifications = notifications.Select(n => new SystemNotificationResponseDto
                {
                    Id = n.Id,
                    UserId = n.UserId,
                    Title = n.Title,
                    Message = n.Message,
                    NotificationType = n.NotificationType,
                    NotificationUrl = n.NotificationUrl,
                    IsRead = n.IsRead,
                    CreatedAt = n.CreatedAt,
                    ReadAt = n.ReadAt
                }).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        public async Task<int> GetUnreadCountAsync(int userId)
        {
            return await _repository.GetUnreadCountByUserIdAsync(userId);
        }

        public async Task<bool> MarkAsReadAsync(int id, int userId)
        {
            return await _repository.MarkAsReadAsync(id, userId);
        }

        public async Task<int> MarkAllAsReadAsync(int userId)
        {
            return await _repository.MarkAllAsReadAsync(userId);
        }

        public async Task CreateNotificationForTeamManagersAsync(int unitId, CreateSystemNotificationDto dto, int? excludeUserId = null)
        {
            try
            {
                var managerIds = await _repository.GetTeamManagerUserIdsAsync(unitId);

                // Exclude the acting user (don't notify yourself)
                if (excludeUserId.HasValue)
                    managerIds = managerIds.Where(id => id != excludeUserId.Value).ToList();

                if (!managerIds.Any()) return;

                var notifications = managerIds.Select(managerId => new SystemNotification
                {
                    UserId = managerId,
                    Title = dto.Title,
                    Message = dto.Message,
                    NotificationType = dto.NotificationType,
                    IpAddress = dto.IpAddress,
                    NotificationUrl = dto.NotificationUrl,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                }).ToList();

                await _repository.AddBulkAsync(notifications);
            }
            catch
            {
                // Silent - notification failure should not break the main operation
            }
        }
    }
}
