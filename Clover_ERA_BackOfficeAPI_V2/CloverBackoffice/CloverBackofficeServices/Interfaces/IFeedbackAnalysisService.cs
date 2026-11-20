using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IFeedbackAnalysisService
    {
        Task<ChatGptSentimentResponse> AnalyzeFeedbackAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate);
        Task<FeedbackInsightsDto> AnalyzeFeedbackAsync2(long unitId, int days);
        Task<string> AnalyzeFeedbackBatches(long unitId, int days);

        Task<DashboardSummaryDto> GetDashboardSummaryAsync(long unitId, int daysBack);
        Task<bool> CheckBusinessUnitAsync(long unitId);
        Task<TopDataResponse> GetTopDataAsync(long unitId);
        Task<string> DumpUsersAsync();
        Task<IEnumerable<LLMReportDto>> GetReportsByUnitAsync(long unitId);
        Task<TopReportDataResponse> GetTopReportDataAsync(long Id, long unitId);
        Task<LLMReportRequestDto> CreateReportAsync(LLMReportRequestDto dto);
        Task<List<FeedbackDataResultDto>> FeedbackDataAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate);
        Task<List<FeedbackDataResultDto>> FeedbackDataAsync(long profileId, long unitId, DateTimeOffset fromDate, DateTimeOffset toDate);
        Task<List<WorkAllocationResultDto>> WorkAllocationDataAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate);
        Task<List<DailyReflectionLinesDto>> DailyReflectionLinesAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate);
        Task<DashboardSummaryDto> GetDashboardDateRangeAsync(long unitId, DateTime fromDate, DateTime toDate);
        Task<List<QuestionDto>> GetQuestionListAsync();
        Task ImportExcelAsync(IFormFile file);
        Task<DashboardSummaryDto> AnalyzeFeedbackRunTimeAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate);

        Task<string> AnalyzeFeedbackPromptData(string startPoint, IFormFile file, string endpoint);
        Task<BusinessUnitBatchJobsDto> GetBatchJobsWithInsightsAsync(int businessUnitId);
        Task<BatchJob?> GetLatestJobAsync(uint businessUnitId);
        Task<BatchJob> CreateJobAsync(uint businessUnitId, string requestType, string metadataJson);
        Task UpdateJobStatusAsync(Guid jobId, string status, string? errorMessage = null);
        Task<List<BusinessUnit>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<BatchJobAzureDto> AnalyzeFeedbackAzureResults([FromQuery] long unitId, [FromQuery] DateTimeOffset fromDate, [FromQuery] DateTimeOffset toDate);
        Task<string> SaveInsightsAsync(Guid batchJobId, string jsonResponse, int userId);
        Task<(bool IsSuccess, string FilePath, string Message)> GetWorksheetFileAsync(string fileName);
        Task<bool> DeleteReportAsync(long id);
        Task<int> SaveRunInsightsAsync(SaveRunInsightsRequestDto dto, int? userId = null);

        // Same save, with the release step made explicit. publish = true is the
        // workbench behaviour the overload above keeps: report + Top* rows + the
        // InsightPublishLog row that marks it published. publish = false stops
        // one step short, leaving the report Pending for the admin review queue —
        // which is how the scheduler saves, since a scheduled report must be
        // approved before its manager hears about it.
        Task<SaveRunInsightsResultDto> SaveRunInsightsAsync(
            SaveRunInsightsRequestDto dto,
            int? userId,
            bool publish,
            CancellationToken ct = default);
        Task<bool> CheckWorksheetFileExistsAsync(string fileName);
        Task<string> RunPromptfooEvaluationAsync() ;
        Task ImportFeedbackFromExcelAsync(string filePath, uint businessUnitId);


    }
}
