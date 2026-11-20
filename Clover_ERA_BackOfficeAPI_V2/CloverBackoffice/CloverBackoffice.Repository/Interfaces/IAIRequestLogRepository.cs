using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IAIRequestLogRepository
    {
        Task AddAsync(AIRequestLog log);
        Task<List<AIRequestLog>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default);
        Task<List<AIRequestLog>> GetByPromptNameAsync(string promptName, int limit = 50, CancellationToken ct = default);
        Task<List<AIRequestLog>> GetRecentAsync(int limit = 50, CancellationToken ct = default);
        Task SaveChangesAsync(CancellationToken ct = default);
    }
}
