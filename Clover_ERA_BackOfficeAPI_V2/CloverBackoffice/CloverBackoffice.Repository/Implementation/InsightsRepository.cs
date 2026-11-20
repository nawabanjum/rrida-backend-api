using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Implementation
{
    public class InsightsRepository : IInsightsRepository
    {
        private readonly ApplicationDbContext _context;

        public InsightsRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<EngagementInsights> GetLatestInsightsAsync(uint businessUnitId)
        {
            return await _context.EngagementInsights
                .Where(i => i.BusinessUnitId == businessUnitId)
                .OrderByDescending(i => i.GeneratedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<List<EngagementInsights>> GetInsightsHistoryAsync(uint businessUnitId, int days, int page = 1, int pageSize = 10)
        {
            var fromDate = DateTime.UtcNow.AddDays(-days);
            return await _context.EngagementInsights
                .Where(i => i.BusinessUnitId == businessUnitId && i.GeneratedAt >= fromDate)
                .OrderByDescending(i => i.GeneratedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task SaveInsightsAsync(EngagementInsights insights)
        {
            if (insights.Id == 0)
            {
                _context.EngagementInsights.Add(insights);
            }
            else
            {
                _context.EngagementInsights.Update(insights);
            }
            await _context.SaveChangesAsync();
        }

        public async Task<BatchJob> SaveBatchJobAsync(BatchJob job)
        {
            if (job.Id == Guid.Empty)
            {
                _context.BatchJobs.Add(job);
            }
            else
            {
                _context.BatchJobs.Update(job);
            }
            await _context.SaveChangesAsync();
            return job;
        }

        public async Task<BatchJob> GetBatchJobAsync(Guid jobId)
        {
            try
            {
                return await _context.BatchJobs.FindAsync(jobId);
            }
            catch (Exception ex)
            {
                throw;
            }

        }

        public async Task UpdateBatchJobStatusAsync(Guid jobId, string status, DateTime? completedAt = null, string errorMessage = null, string outputFileUrl = null)
        {
            var job = await _context.BatchJobs.FirstOrDefaultAsync(j => j.Id == jobId);
            if (job == null) return;
            job.Status = status;
            job.CompletedAt = completedAt;
            job.ErrorMessage = errorMessage;
            job.OutputFileUrl = outputFileUrl;
            await _context.SaveChangesAsync();
        }

        public async Task<List<BatchJob>> GetSubmittedJobsAsync()
        {
            return await _context.BatchJobs
                .Where(j => j.Status == "submitted")
                .ToListAsync();
        }
    }
}


