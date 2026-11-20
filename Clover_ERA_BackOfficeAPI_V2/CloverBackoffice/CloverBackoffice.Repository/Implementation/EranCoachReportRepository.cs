using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Implementation
{
    /// <summary>
    /// Reads the LLMReport / TopTheme / TopRecommendation / TopAction tables for EranCoach.
    /// Query shapes mirror FeedbackRepository so the coach sees the same insights the reports UI does.
    /// </summary>
    public class EranCoachReportRepository : IEranCoachReportRepository
    {
        private readonly ApplicationDbContext _context;

        public EranCoachReportRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<LLMReport?> GetLatestReportAsync(long unitId, CancellationToken ct = default)
        {
            return await _context.LLMReports
                .Where(r => r.UnitId == unitId)
                .OrderByDescending(r => r.FromDate)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<LLMReport?> GetReportAsync(long unitId, long reportId, CancellationToken ct = default)
        {
            return await _context.LLMReports
                .FirstOrDefaultAsync(r => r.UnitId == unitId && r.Id == reportId, ct);
        }

        public async Task<IReadOnlyList<LLMReport>> GetReportsAsync(long unitId, CancellationToken ct = default)
        {
            return await _context.LLMReports
                .Where(r => r.UnitId == unitId)
                .OrderByDescending(r => r.FromDate)
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<string>> GetTopThemesAsync(long unitId, long reportId, CancellationToken ct = default)
        {
            var themes = await _context.TopThemes
                .Where(t => t.UnitId == unitId && t.ReportId == reportId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync(ct); // dedup in memory (mirrors FeedbackRepository)

            return themes
                .GroupBy(t => t.Theme)
                .Select(g => g.First())
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => t.Theme)
                .ToList();
        }

        public async Task<IReadOnlyList<string>> GetTopRecommendationsAsync(long unitId, long reportId, CancellationToken ct = default)
        {
            return await _context.TopRecommendations
                .Where(r => r.UnitId == unitId && r.ReportId == reportId)
                .OrderByDescending(r => r.CreatedAt)
                .Take(3)
                .Select(r => r.Recommendation)
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<string>> GetTopActionsAsync(long unitId, long reportId, CancellationToken ct = default)
        {
            return await _context.TopActions
                .Where(a => a.UnitId == unitId && a.ReportId == reportId)
                .OrderByDescending(a => a.CreatedAt)
                .Take(3)
                .Select(a => a.Action)
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyDictionary<long, string>> GetUnitNamesAsync(IEnumerable<long> unitIds, CancellationToken ct = default)
        {
            var ids = unitIds.Select(x => (uint)x).Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<long, string>();

            var rows = await _context.BusinessUnits
                .Where(b => ids.Contains(b.Id))
                .Select(b => new { b.Id, b.Name })
                .ToListAsync(ct);

            return rows.ToDictionary(r => (long)r.Id, r => r.Name);
        }
    }
}
