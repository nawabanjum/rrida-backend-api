using CloverBackoffice.Infrastructure.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IDailyFeedbackAuditLogRepository
    {
        Task<Dictionary<long, DailyFeedbackAuditLog>> GetLatestAuditSnapshotAsync();
        Task<List<DailyFeedback>> GetAllDailyFeedbacksAsync();
        Task<int> GetDailyFeedbackCountAsync();
        Task AddAuditLogsAsync(List<DailyFeedbackAuditLog> auditLogs);
    }
}
