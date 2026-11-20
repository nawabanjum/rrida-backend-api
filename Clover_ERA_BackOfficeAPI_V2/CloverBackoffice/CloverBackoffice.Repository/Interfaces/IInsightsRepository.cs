using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IInsightsRepository
    {
        Task<EngagementInsights> GetLatestInsightsAsync(uint businessUnitId);
        Task<List<EngagementInsights>> GetInsightsHistoryAsync(uint businessUnitId, int days, int page = 1, int pageSize = 10);
        Task SaveInsightsAsync(EngagementInsights insights);
        Task<BatchJob> SaveBatchJobAsync(BatchJob job);
        Task<BatchJob> GetBatchJobAsync(Guid jobId);
        Task UpdateBatchJobStatusAsync(Guid jobId, string status, DateTime? completedAt = null, string errorMessage = null, string outputFileUrl = null);
        Task<List<BatchJob>> GetSubmittedJobsAsync();
    }
}


