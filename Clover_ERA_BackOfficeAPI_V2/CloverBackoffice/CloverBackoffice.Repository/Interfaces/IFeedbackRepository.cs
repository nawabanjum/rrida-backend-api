using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IFeedbackRepository
    {
        Task<DashboardSummaryDto> GetDailyFeedbackReportAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate);
        Task<List<DailyFeedbackReportRowDto>> GetDailyFeedbackReportAsync2(long unitId, int days);
        Task SaveTopThemesAsync(IEnumerable<TopTheme> themes);
        Task SaveTopRecommendationsAsync(IEnumerable<TopRecommendation> recommendations);
        Task SaveTopActionsAsync(IEnumerable<TopAction> actions);
        Task<List<DailyFeedbackReportRowDto>> GetDailyFeedbackReportAsyncRuntime(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate);
        Task<DashboardSummaryDto> GetDashboardSummaryAsync(long unitId, int daysBack);
        Task<bool> BusinessUnitExistsAsync(long unitId);
        Task<List<TopRecommendation>> GetTopRecommendationsByUnitIdAsync(long unitId);
        Task<List<TopTheme>> GetTopThemesByUnitIdAsync(long unitId);
        Task<List<TopAction>> GetTopActionsByUnitIdAsync(long unitId);
        Task<List<CompanyUnitDto>> GetAllCompanyUnitPairsAsync();
        Task<bool> CompanyLinkExistsAsync(int profileId, int businessUnitId);
        Task<uint> CreateBussinessUnitAsync(BusinessUnit unit);
        Task<Profile> GetCompanyByIdAsync(int profileId);
        Task<int> CreateUserAsync(User user);
        Task CreateCompanyLinkAsync(CompanyLink link);
        Task<User> GetUserByEmailAsync(string email);
        Task<IEnumerable<LLMReport>> GetReportsByUnitAsync(long unitId);
        Task<LLMReport> CreateReportAsync(LLMReport report);
        Task<List<TopRecommendation>> GetTopRecommendationsByUnitIdAndReportIdAsync(long ReportId, long unitId);
        Task<List<TopTheme>> GetTopThemesByUnitIdAndReportAsync(long ReportId, long unitId);
        Task<List<TopAction>> GetTopActionsByUnitIdAndReportAsync(long ReportId, long unitId);
        Task<LLMReport> GetReportsByUnitAndReportIdAsync(long id, long unitId);
        Task<DashboardSummaryDto> GetDashboardSummaryByDateRangeAsync(long unitId, DateTime fromDate, DateTime toDate);
        Task<LLMReport> AddReportAsync(LLMReport report);
        Task<LLMReport> GetReportsByDateRange(DateTime FromDate, DateTime ToDate);

        Task DeleteRecommendationsAsync(IEnumerable<TopRecommendation> recommendations);
        Task DeleteThemesAsync(IEnumerable<TopTheme> themes);
        Task DeleteActionsAsync(IEnumerable<TopAction> actions);
        Task DeleteReportAsync(LLMReport report);
        Task<List<FeedbackDataResultDto>> GetFeedbackDataAsyncRuntime(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate);
        Task<List<FeedbackDataResultDto>> GetFeedbackDataAsyncRuntime(long profileId, long unitId, DateTimeOffset fromDate, DateTimeOffset toDate);
        Task<List<WorkAllocationResultDto>> GetWorkAllocationDataAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate);
        Task<List<DailyReflectionLinesDto>> GetDailyReflectionLinesAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate);
        Task<long?> GetBusinessUnitIdByNameAsync(string name);
        Task<long?> GetQuestionIdByTextAsync(string questionText);
        Task<long?> GetReasonIdByTextAsync(string reasonText);
        Task AddFeedbackAsync(DailyFeedback feedback);
        Task SaveChangesAsync();
        Task<DashboardSummaryDto> GetDashboardDateRangeAsync(long unitId, DateTime fromDate, DateTime toDate);
        Task<List<QuestionDto>> GetQuestionListAsync();

        Task<List<DailyFeedbackReportRowDto>> CreateDailyFeedbackReportAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate);
        Task<List<BatchJobDto>> GetBatchJobsWithInsightsByBusinessUnitIdAsync(int businessUnitId);
        Task<BatchJob?> GetLatestByBusinessUnitAsync(uint businessUnitId);
        Task AddAsync(BatchJob job);
        Task UpdateAsync(BatchJob job);
        Task<List<BusinessUnit>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<List<DailyFeedbackReportRowDto>> GetDailyFeedbackReportAzureResults(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate);
        Task<BatchJob?> GetBatchJobByIdAsync(Guid id);
        Task<EngagementInsights?> GetEngagementInsightByBatchJobIdAsync(Guid batchJobId);
        Task UpdateEngagementInsightAsync(EngagementInsights entity);
        ///..
        Task UpdateBatchJobAsync(BatchJob batchJob);
        Task<LLMReport?> GetReportByUnitAndDatesAsync(uint unitId, DateTime fromDate, DateTime toDate);
        Task DeleteReportAsync(int reportId);
        Task DeleteTopThemesByUnitAndReportAsync(uint unitId, int reportId);
        Task DeleteTopRecommendationsByUnitAndReportAsync(uint unitId, int reportId);
        Task DeleteTopActionsByUnitAndReportAsync(uint unitId, int reportId);
        Task<bool> ExecuteInTransactionAsync(Func<Task> action);
        Task<LLMReport?> GetReportByIdAsync(long id);
        Task<WorkbenchRunStatus?> GetWorkbenchRunStatusByIdAsync(int runId);
        Task SaveInsightPublishLogAsync(InsightPublishLog log);
        Task<InsightPublishLog?> GetInsightPublishLogByRunIdAsync(int runId);
        Task DeleteReportManuallyAsync(LLMReport report);
        Task DeleteLinkedReportItemsAsync(long reportId);
        Task<bool> CheckFileExistsAsync(string fileName);
        Task<DateTime?> GetLatestSubmissionDateTimeAsync(uint businessUnitId);
        Task BulkInsertFeedbackAsync(List<DailyFeedback> feedbackList);

    }
}
