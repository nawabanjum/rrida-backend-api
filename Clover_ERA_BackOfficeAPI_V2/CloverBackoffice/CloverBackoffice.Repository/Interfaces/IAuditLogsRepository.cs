using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IAuditLogsRepository
    {
        Task<AuditLogs> AddAuditLogsAsync(AuditLogs model);
        Task<LLMLog> AddLLMLogAsync(LLMLog model);
        Task<LLMLog> LogHangfireJobAsync(LLMLog model);
    }
}
