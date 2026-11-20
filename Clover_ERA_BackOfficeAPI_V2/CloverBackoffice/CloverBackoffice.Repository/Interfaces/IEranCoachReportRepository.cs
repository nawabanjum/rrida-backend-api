using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Repository.Interfaces
{
    /// <summary>
    /// Read-only access to the stored feedback reports (LLMReport) and their insights
    /// (TopTheme / TopRecommendation / TopAction) that EranCoach surfaces through its report
    /// tools. Role-based scoping is enforced in the service; this layer just reads a unit's data.
    /// </summary>
    public interface IEranCoachReportRepository
    {
        /// <summary>Most recent report for a unit (by report date), or null when none exist.</summary>
        Task<LLMReport?> GetLatestReportAsync(long unitId, CancellationToken ct = default);

        /// <summary>A specific report for a unit, or null when it does not belong to that unit.</summary>
        Task<LLMReport?> GetReportAsync(long unitId, long reportId, CancellationToken ct = default);

        /// <summary>All reports for a unit, most recent first.</summary>
        Task<IReadOnlyList<LLMReport>> GetReportsAsync(long unitId, CancellationToken ct = default);

        /// <summary>Distinct Top Themes for a unit's report, most recent first.</summary>
        Task<IReadOnlyList<string>> GetTopThemesAsync(long unitId, long reportId, CancellationToken ct = default);

        /// <summary>Top Recommendations (max 3) for a unit's report, most recent first.</summary>
        Task<IReadOnlyList<string>> GetTopRecommendationsAsync(long unitId, long reportId, CancellationToken ct = default);

        /// <summary>Top Actions (max 3) for a unit's report, most recent first.</summary>
        Task<IReadOnlyList<string>> GetTopActionsAsync(long unitId, long reportId, CancellationToken ct = default);

        /// <summary>Names for the given unit ids (id -> name), for building team pick-lists.</summary>
        Task<IReadOnlyDictionary<long, string>> GetUnitNamesAsync(IEnumerable<long> unitIds, CancellationToken ct = default);
    }
}
