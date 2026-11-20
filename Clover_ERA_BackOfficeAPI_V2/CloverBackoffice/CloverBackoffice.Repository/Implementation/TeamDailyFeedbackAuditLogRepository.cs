using CloverBackoffice.Infrastructure.Models.TeamQuestions;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    // Mirrors DailyFeedbackAuditLogRepository against the team tables.
    public class TeamDailyFeedbackAuditLogRepository : ITeamDailyFeedbackAuditLogRepository
    {
        private readonly ApplicationDbContext _context;

        public TeamDailyFeedbackAuditLogRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Dictionary<long, TeamDailyFeedbackAuditLog>> GetLatestAuditSnapshotAsync()
        {
            var latestPerRecord = await _context.TeamDailyFeedbackAuditLogs
                .GroupBy(a => a.Id)
                .Select(g => g.OrderByDescending(a => a.VersionNumber).First())
                .ToListAsync();

            return latestPerRecord.ToDictionary(a => a.Id);
        }

        public async Task<List<TeamDailyFeedback>> GetAllTeamDailyFeedbacksAsync()
        {
            return await _context.TeamDailyFeedback
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<int> GetTeamDailyFeedbackCountAsync()
        {
            return await _context.TeamDailyFeedback.CountAsync();
        }

        public async Task AddAuditLogsAsync(List<TeamDailyFeedbackAuditLog> auditLogs)
        {
            if (auditLogs.Count == 0) return;

            await _context.TeamDailyFeedbackAuditLogs.AddRangeAsync(auditLogs);
            await _context.SaveChangesAsync();
        }
    }
}
