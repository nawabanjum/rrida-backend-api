using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IAggregateDashboardRepository
    {
        Task<BusinessUnit?> GetBusinessUnitAsync(long unitId);
        Task<List<long>> GetBusinessUnitHierarchyRecursiveAsync(long parentId);
        Task<bool> UnitHasFeedbackDataAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate);
        Task<List<DailyFeedbackReportRowDto>> GetDailyFeedbackReportAsync(List<long> unitIds, DateTimeOffset fromDate, DateTimeOffset toDate);
        Task<DashboardSummaryDto> GetDashboardSummaryFromAnalysisAsync(List<long> unitIds, long requestedUnitId, List<FeedbackAnalysisResult> analysisResults, List<DailyFeedbackReportRowDto> rawFeedback, DateTime requestedFromDate, DateTime requestedToDate);
        string GetStatusFromScore(double score);
        Task<DashboardSummaryDto> AnalyzeAndSaveFeedbackInternal(List<DailyFeedbackReportRowDto> feedbackRows, List<long> unitIds, long requestedUnitId, DateTime requestedFromDate, DateTime requestedToDate);


        Task<TeamsSectionDto> GetTeamsSection(List<long> allUnitIds, List<long> licensedUnitIds, List<long> complimentaryUnitIds);
        Task<ResponseRateSectionDto> GetResponseRateSection(List<long> unitIds);
        Task<EngagementSectionDto> GetEngagementSection(List<long> unitIds);
        Task<NeedsAttentionSectionDto> GetNeedsAttentionSection(List<long> unitIds);
        Task<ActionHubActivitiesDto> GetActionHubActivities(User user, List<long> accessibleUnitIds);
        Task<User> GetUserByIdAsync(int userId);
        TrendChartDto CalculateTrendChartFromFeedback(List<DailyFeedbackReportRowDto> feedbackRows, List<long> unitIds, DateTime requestedFromDate, DateTime requestedToDate);
        CategoryDetailDto GetCategoryDetail(List<DailyFeedbackReportRowDto> feedbackRows, List<long> unitIds, int cloverCategory, DateTime requestedFromDate, DateTime requestedToDate);
    }
}
