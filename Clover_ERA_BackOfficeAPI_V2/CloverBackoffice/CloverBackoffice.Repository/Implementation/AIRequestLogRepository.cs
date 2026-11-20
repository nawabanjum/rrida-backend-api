using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    public class AIRequestLogRepository : IAIRequestLogRepository
    {
        private readonly ApplicationDbContext _context;

        public AIRequestLogRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(AIRequestLog log)
        {
            await _context.AIRequestLogs.AddAsync(log);
        }

        public async Task<List<AIRequestLog>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct)
        {
            return await _context.AIRequestLogs
                .Where(x => x.CreatedAt >= from && x.CreatedAt <= to)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(ct);
        }

        public async Task<List<AIRequestLog>> GetByPromptNameAsync(string promptName, int limit, CancellationToken ct)
        {
            return await _context.AIRequestLogs
                .Where(x => x.PromptName == promptName)
                .OrderByDescending(x => x.CreatedAt)
                .Take(limit)
                .ToListAsync(ct);
        }

        public async Task<List<AIRequestLog>> GetRecentAsync(int limit, CancellationToken ct)
        {
            return await _context.AIRequestLogs
                .OrderByDescending(x => x.CreatedAt)
                .Take(limit)
                .ToListAsync(ct);
        }

        public async Task SaveChangesAsync(CancellationToken ct)
        {
            await _context.SaveChangesAsync(ct);
        }
    }
}
