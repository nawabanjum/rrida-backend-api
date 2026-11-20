using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IAuditLogsService
    {
        Task<AuditLogs> AddAuditLogsAsync(AuditLogs model);
        Task<LLMLog> AddLLMLogAsync(LLMLog model);
        Task<LLMLog> LogHangfireJobAsync(uint unitId, string jobName, string status, string message, DateTime? fromDate = null, DateTime? toDate = null);

    }
}
