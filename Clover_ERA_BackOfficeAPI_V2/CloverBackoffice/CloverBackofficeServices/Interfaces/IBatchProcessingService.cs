using System;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IBatchProcessingService
    {
        Task<BatchJob> CreateBatchJobAsync(uint businessUnitId, int periodDays);
        Task ProcessCompletedJobsAsync();
        Task<BatchStatus> GetBatchStatusAsync(Guid jobId);
        Task<EngagementInsights> GetResultsAsync(Guid jobId);
    }
}


