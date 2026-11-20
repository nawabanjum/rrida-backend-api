using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IDocumentBatchesRepository
    {
        Task<List<DailyFeedbackReportRowDto>> GetDailyFeedbackReportAsync2Embedding(long unitId, int days);
        Task<List<BatchJobDto>> GetBatchJobsWithInsightsByBusinessUnitIdAsyncEmbedding(int businessUnitId);
        Task UpdateBatchJobAsync(BatchJob batchJob);
        Task<bool> ExecuteInTransactionAsync(Func<Task> action);
        Task<bool> CheckFileExistsAsync(string fileName);
        Task<List<DailyFeedbackReportRowDto>> GetDailyFeedbackReportAzureResults(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate);
        Task<List<string>> GetAllWorksheetFilesAsync();
        Task<List<(string FileName, string Content)>> GetWorksheetFileContentsAsync();

    }
}
