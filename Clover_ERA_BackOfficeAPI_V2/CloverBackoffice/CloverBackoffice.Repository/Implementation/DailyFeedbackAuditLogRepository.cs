using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    public class DailyFeedbackAuditLogRepository : IDailyFeedbackAuditLogRepository
    {
        private readonly ApplicationDbContext _context;

        public DailyFeedbackAuditLogRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Dictionary<long, DailyFeedbackAuditLog>> GetLatestAuditSnapshotAsync()
        {
            var latestPerRecord = await _context.DailyFeedbackAuditLogs
                .GroupBy(a => a.Id)
                .Select(g => g.OrderByDescending(a => a.VersionNumber).First())
                .ToListAsync();

            return latestPerRecord.ToDictionary(a => a.Id);
        }

        public async Task<List<DailyFeedback>> GetAllDailyFeedbacksAsync()
        {
            return await _context.DailyFeedbacks
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<int> GetDailyFeedbackCountAsync()
        {
            return await _context.DailyFeedbacks.CountAsync();
        }

        public async Task AddAuditLogsAsync(List<DailyFeedbackAuditLog> auditLogs)
        {
            if (auditLogs.Count == 0) return;

            await _context.DailyFeedbackAuditLogs.AddRangeAsync(auditLogs);
            await _context.SaveChangesAsync();
        }
    }
}
