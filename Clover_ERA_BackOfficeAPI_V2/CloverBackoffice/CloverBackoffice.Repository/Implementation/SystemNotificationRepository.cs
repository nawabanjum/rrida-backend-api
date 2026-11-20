using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    public class SystemNotificationRepository : ISystemNotificationRepository
    {
        private readonly ApplicationDbContext _context;

        public SystemNotificationRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<SystemNotification> AddAsync(SystemNotification notification)
        {
            notification.CreatedAt = DateTime.UtcNow;
            notification.IsRead = false;
            await _context.SystemNotifications.AddAsync(notification);
            await _context.SaveChangesAsync();
            return notification;
        }

        public async Task AddBulkAsync(List<SystemNotification> notifications)
        {
            await _context.SystemNotifications.AddRangeAsync(notifications);
            await _context.SaveChangesAsync();
        }

        public async Task<List<int>> GetAdminUserIdsAsync()
        {
            return await _context.Users
                .Where(u => u.UserGroupId == 2 && !u.IsDel)
                .Select(u => u.UserId)
                .ToListAsync();
        }

        public async Task<int?> GetProfileIdByUserIdAsync(int userId)
        {
            return await _context.Users
                .Where(u => u.UserId == userId && !u.IsDel)
                .Select(u => u.ProfileId)
                .FirstOrDefaultAsync();
        }

        public async Task<List<int>> GetCompanyAdminUserIdsAsync(int profileId)
        {
            return await _context.Users
                .Where(u => u.ProfileId == profileId && u.UserGroupId == 1 && !u.IsDel)
                .Select(u => u.UserId)
                .ToListAsync();
        }

        public async Task<List<SystemNotification>> GetByUserIdAsync(int userId, int page, int pageSize)
        {
            return await _context.SystemNotifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<int> GetTotalCountByUserIdAsync(int userId)
        {
            return await _context.SystemNotifications
                .CountAsync(n => n.UserId == userId);
        }

        public async Task<int> GetUnreadCountByUserIdAsync(int userId)
        {
            return await _context.SystemNotifications
                .CountAsync(n => n.UserId == userId && !n.IsRead);
        }

        public async Task<SystemNotification?> GetByIdAsync(int id)
        {
            return await _context.SystemNotifications
                .FirstOrDefaultAsync(n => n.Id == id);
        }

        public async Task<bool> MarkAsReadAsync(int id, int userId)
        {
            var notification = await _context.SystemNotifications
                .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
            if (notification == null) return false;

            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            _context.Entry(notification).Property(n => n.IsRead).IsModified = true;
            _context.Entry(notification).Property(n => n.ReadAt).IsModified = true;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<int> MarkAllAsReadAsync(int userId)
        {
            var unreadNotifications = await _context.SystemNotifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ToListAsync();

            foreach (var notification in unreadNotifications)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;
                _context.Entry(notification).Property(n => n.IsRead).IsModified = true;
                _context.Entry(notification).Property(n => n.ReadAt).IsModified = true;
            }

            await _context.SaveChangesAsync();
            return unreadNotifications.Count;
        }

        public async Task<List<int>> GetTeamManagerUserIdsAsync(int unitId)
        {
            // Collect this unit + all ancestor unit IDs by walking up the parent chain
            var unitIds = new List<int>();
            var currentUnitId = (int?)unitId;

            while (currentUnitId.HasValue)
            {
                var unit = await _context.BusinessUnits
                    .Where(bu => bu.Id == currentUnitId.Value && bu.IsDel != true)
                    .Select(bu => new { bu.Id, bu.ParentId })
                    .FirstOrDefaultAsync();

                if (unit == null) break;

                unitIds.Add((int)unit.Id);
                currentUnitId = unit.ParentId.HasValue ? (int?)unit.ParentId.Value : null;
            }

            if (!unitIds.Any()) return new List<int>();

            var castUnitIds = unitIds.Select(id => (uint)id).ToList();

            // Find active team managers assigned to these units
            return await _context.UserTeams
                .Where(ut => castUnitIds.Contains(ut.UnitId) && !ut.IsUnAssigned)
                .Join(_context.Users,
                    ut => ut.UserId,
                    u => u.UserId,
                    (ut, u) => u)
                .Where(u => u.UserGroupId == 5 && !u.IsDel) // 5 = TeamManager
                .Select(u => u.UserId)
                .Distinct()
                .ToListAsync();
        }
    }
}
