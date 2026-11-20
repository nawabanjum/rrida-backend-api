using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IRagService
    {
        Task<string> AnswerAsync(string query, int topK = 5, CancellationToken ct = default);
        Task<string> AnswerBatchAsync(string prompt, long unitId, int days, CancellationToken ct = default);
        Task<string> PollBatchCompletionAsync(Guid batchJobId, CancellationToken ct = default);
        Task<List<BatchJob>> GetPendingBatchJobsAsync();
        Task<BatchJob> GetBatchJobsAsync(string BatchJobId);
        Task CheckBatchJobAzureResults(long unitId);
        Task<string> AnswerBatchAzureResults(string prompt, long unitId, int days, DateTimeOffset fromDate, DateTimeOffset toDate, CancellationToken ct = default);
    }
}
