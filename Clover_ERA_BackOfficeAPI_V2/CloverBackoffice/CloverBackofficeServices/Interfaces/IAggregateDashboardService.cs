using CloverBackoffice.Infrastructure.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IAggregateDashboardService
    {
        Task<List<long>> GetBusinessUnitHierarchyAsync(long unitId);
        Task<List<long>> GetBusinessUnitIdsForAnalysisAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate);
        Task<DashboardSummaryDto> AnalyzeFeedbackRunTimeAsync(List<long> unitIds, DateTimeOffset fromDate, DateTimeOffset toDate, bool ignoreLicenseFilter = false);
        Task<List<LicensePeriodSegmentDto>> GetLicensePeriodsAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate);
        Task<DashboardResponseDto> GetDashboardData(int userId, int? profileId = null);
        Task<CategoryDetailDto> GetCategoryDetailAsync(List<long> unitIds, DateTimeOffset fromDate, DateTimeOffset toDate, int cloverCategory);
    }
}
