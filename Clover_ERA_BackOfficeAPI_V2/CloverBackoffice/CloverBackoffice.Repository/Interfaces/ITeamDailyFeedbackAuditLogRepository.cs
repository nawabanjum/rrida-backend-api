using CloverBackoffice.Infrastructure.Models.TeamQuestions;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface ITeamDailyFeedbackAuditLogRepository
    {
        Task<Dictionary<long, TeamDailyFeedbackAuditLog>> GetLatestAuditSnapshotAsync();
        Task<List<TeamDailyFeedback>> GetAllTeamDailyFeedbacksAsync();
        Task<int> GetTeamDailyFeedbackCountAsync();
        Task AddAuditLogsAsync(List<TeamDailyFeedbackAuditLog> auditLogs);
    }
}
