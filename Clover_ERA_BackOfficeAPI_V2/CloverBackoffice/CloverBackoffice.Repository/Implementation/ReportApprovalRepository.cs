using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Implementation
{
    public class ReportApprovalRepository : IReportApprovalRepository
    {
        private readonly ApplicationDbContext _context;

        public ReportApprovalRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        // The single definition of "published", shared by every query here.
        // Insert/Update are the two OperationType values SaveInsightPublishLogAsync
        // writes; "Delete" rows are tombstones of removed reports and "Rejected"
        // rows are refusals — neither publishes anything.
        private IQueryable<InsightPublishLog> PublishRows =>
            _context.InsightPublishLogs.Where(l =>
                l.OperationType == ReportReviewStates.OperationInsert ||
                l.OperationType == ReportReviewStates.OperationUpdate);

        private IQueryable<InsightPublishLog> RejectRows =>
            _context.InsightPublishLogs.Where(l => l.OperationType == ReportReviewStates.OperationRejected);

        public async Task<ReportReviewQueueDto> GetQueueAsync(
            string? state,
            long? profileId,
            long? unitId,
            int page,
            int pageSize,
            CancellationToken ct = default)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize < 1 ? 25 : (pageSize > 200 ? 200 : pageSize);

            var query = _context.LLMReports.AsNoTracking().AsQueryable();

            if (unitId.HasValue && unitId.Value > 0)
                query = query.Where(r => r.UnitId == unitId.Value);

            if (profileId.HasValue && profileId.Value > 0)
            {
                // LLMReports carries no ProfileId, so the company filter goes
                // through the unit.
                var companyUnitIds = _context.BusinessUnits
                    .Where(bu => bu.Profile.Id == profileId.Value)
                    .Select(bu => (long)bu.Id);

                query = query.Where(r => companyUnitIds.Contains(r.UnitId));
            }

            query = ApplyStateFilter(query, state);

            var totalCount = await query.CountAsync(ct);

            var reports = await query
                .OrderByDescending(r => r.CreatedAt)
                .ThenByDescending(r => r.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            var items = await ProjectAsync(reports, ct);

            // Whole-queue totals so the UI can badge every tab from one call.
            var allReports = _context.LLMReports.AsNoTracking();

            return new ReportReviewQueueDto
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                PendingCount = await ApplyStateFilter(allReports, ReportReviewStates.Pending).CountAsync(ct),
                ReleasedCount = await ApplyStateFilter(allReports, ReportReviewStates.Released).CountAsync(ct),
                RejectedCount = await ApplyStateFilter(allReports, ReportReviewStates.Rejected).CountAsync(ct)
            };
        }

        private IQueryable<LLMReport> ApplyStateFilter(IQueryable<LLMReport> query, string? state)
        {
            if (string.IsNullOrWhiteSpace(state))
                return query;

            if (state.Equals(ReportReviewStates.Pending, StringComparison.OrdinalIgnoreCase))
                return query.Where(r => !_context.InsightPublishLogs.Any(l => l.ReportId == r.Id));

            if (state.Equals(ReportReviewStates.Released, StringComparison.OrdinalIgnoreCase))
                return query.Where(r => PublishRows.Any(l => l.ReportId == r.Id));

            if (state.Equals(ReportReviewStates.Rejected, StringComparison.OrdinalIgnoreCase))
                return query.Where(r => RejectRows.Any(l => l.ReportId == r.Id)
                                     && !PublishRows.Any(l => l.ReportId == r.Id));

            return query;
        }

        public async Task<string> GetStateAsync(long reportId, CancellationToken ct = default)
        {
            if (await PublishRows.AnyAsync(l => l.ReportId == reportId, ct))
                return ReportReviewStates.Released;

            if (await RejectRows.AnyAsync(l => l.ReportId == reportId, ct))
                return ReportReviewStates.Rejected;

            return ReportReviewStates.Pending;
        }

        public async Task<ReportReviewDetailDto?> GetDetailAsync(long reportId, CancellationToken ct = default)
        {
            var report = await _context.LLMReports
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == reportId, ct);

            if (report == null)
                return null;

            var header = (await ProjectAsync(new List<LLMReport> { report }, ct)).First();

            var themes = await _context.TopThemes.AsNoTracking()
                .Where(t => t.ReportId == reportId).OrderBy(t => t.Id).Select(t => t.Theme).ToListAsync(ct);

            var recommendations = await _context.TopRecommendations.AsNoTracking()
                .Where(r => r.ReportId == reportId).OrderBy(r => r.Id).Select(r => r.Recommendation).ToListAsync(ct);

            var actions = await _context.TopActions.AsNoTracking()
                .Where(a => a.ReportId == reportId).OrderBy(a => a.Id).Select(a => a.Action).ToListAsync(ct);

            return new ReportReviewDetailDto
            {
                ReportId = header.ReportId,
                UnitId = header.UnitId,
                UnitName = header.UnitName,
                ProfileId = header.ProfileId,
                CompanyName = header.CompanyName,
                FromDate = header.FromDate,
                ToDate = header.ToDate,
                CreatedAt = header.CreatedAt,
                ReviewState = header.ReviewState,
                ReviewedByUserId = header.ReviewedByUserId,
                ReviewedByName = header.ReviewedByName,
                ReviewedAt = header.ReviewedAt,
                ManagerUserId = header.ManagerUserId,
                ManagerName = header.ManagerName,
                ManagerEmail = header.ManagerEmail,
                ThemeCount = themes.Count,
                RecommendationCount = recommendations.Count,
                ActionCount = actions.Count,
                WorksheetFile = report.WorksheetFile,
                TopThemes = themes,
                TopRecommendations = recommendations,
                TopActions = actions
            };
        }

        public Task<LLMReport?> GetReportAsync(long reportId, CancellationToken ct = default)
            => _context.LLMReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reportId, ct);

        // Same delete-then-insert the workbench publish path performs; no
        // publish-log row is written, because editing is not publishing.
        public async Task UpdateInsightsAsync(long reportId, UpdateReportInsightsDto dto, CancellationToken ct = default)
        {
            var report = await _context.LLMReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reportId, ct);
            if (report == null)
                return;

            var unitId = (uint)report.UnitId;
            var now = DateTime.UtcNow;

            if (dto.TopThemes != null)
            {
                _context.TopThemes.RemoveRange(_context.TopThemes.Where(t => t.ReportId == reportId));
                _context.TopThemes.AddRange(Clean(dto.TopThemes).Select(t => new TopTheme
                {
                    UnitId = unitId, ReportId = reportId, Theme = t, CreatedAt = now
                }));
            }

            if (dto.TopRecommendations != null)
            {
                _context.TopRecommendations.RemoveRange(_context.TopRecommendations.Where(r => r.ReportId == reportId));
                _context.TopRecommendations.AddRange(Clean(dto.TopRecommendations).Select(r => new TopRecommendation
                {
                    UnitId = unitId, ReportId = reportId, Recommendation = r, CreatedAt = now
                }));
            }

            if (dto.TopActions != null)
            {
                _context.TopActions.RemoveRange(_context.TopActions.Where(a => a.ReportId == reportId));
                _context.TopActions.AddRange(Clean(dto.TopActions).Select(a => new TopAction
                {
                    UnitId = unitId, ReportId = reportId, Action = a, CreatedAt = now
                }));
            }

            await _context.SaveChangesAsync(ct);
        }

        private static List<string> Clean(List<string> lines) =>
            lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();

        // Release. Writes the publish-log row exactly as the workbench publish
        // path does — same table, same OperationType, same snapshot columns —
        // with UserId set to the approving admin.
        //
        // The pre-check is the duplicate guard: a report that already has a
        // publish row is already released, so a repeated approve (double click,
        // client retry, second job) writes nothing and notifies nobody.
        public async Task<bool> TryPublishAsync(long reportId, int? actorUserId, CancellationToken ct = default)
        {
            var report = await _context.LLMReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reportId, ct);
            if (report == null)
                return false;

            if (await _context.InsightPublishLogs.AnyAsync(l => l.ReportId == reportId, ct))
                return false;

            var themes = await _context.TopThemes.AsNoTracking()
                .Where(t => t.ReportId == reportId).OrderBy(t => t.Id).Select(t => t.Theme).ToListAsync(ct);
            var recommendations = await _context.TopRecommendations.AsNoTracking()
                .Where(r => r.ReportId == reportId).OrderBy(r => r.Id).Select(r => r.Recommendation).ToListAsync(ct);
            var actions = await _context.TopActions.AsNoTracking()
                .Where(a => a.ReportId == reportId).OrderBy(a => a.Id).Select(a => a.Action).ToListAsync(ct);

            _context.InsightPublishLogs.Add(new InsightPublishLog
            {
                ReportId = reportId,
                RunId = 0,
                UserId = actorUserId,
                DateFrom = report.FromDate,
                DateTo = report.ToDate,
                OperationType = ReportReviewStates.OperationInsert,
                TopThemes = JsonSerializer.Serialize(themes),
                TopActions = JsonSerializer.Serialize(actions),
                TopRecommendations = JsonSerializer.Serialize(recommendations),
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync(ct);
            return true;
        }

        // Refusal. Recorded on the same audit table so the decision, its author
        // and its reason survive, and the report drops out of both the pending
        // queue and the manager's report list.
        public async Task<bool> TryRejectAsync(long reportId, int? actorUserId, string reason, CancellationToken ct = default)
        {
            var report = await _context.LLMReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reportId, ct);
            if (report == null)
                return false;

            if (await _context.InsightPublishLogs.AnyAsync(l => l.ReportId == reportId, ct))
                return false;

            _context.InsightPublishLogs.Add(new InsightPublishLog
            {
                ReportId = reportId,
                RunId = 0,
                UserId = actorUserId,
                DateFrom = report.FromDate,
                DateTo = report.ToDate,
                OperationType = ReportReviewStates.OperationRejected,
                // The three snapshot columns are text; the reason goes in
                // TopThemes' slot as a single-element array so the row stays
                // parseable by anything already reading these logs.
                TopThemes = JsonSerializer.Serialize(new[] { $"REJECTED: {reason.Trim()}" }),
                TopActions = "[]",
                TopRecommendations = "[]",
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync(ct);
            return true;
        }

        public async Task<List<ReportReviewListItemDto>> GetPendingSinceAsync(DateTime? since, CancellationToken ct = default)
        {
            var query = _context.LLMReports
                .AsNoTracking()
                .Where(r => !_context.InsightPublishLogs.Any(l => l.ReportId == r.Id));

            if (since.HasValue)
            {
                var cutoff = DateTime.SpecifyKind(since.Value, DateTimeKind.Utc);
                query = query.Where(r => r.CreatedAt > cutoff);
            }

            var reports = await query.OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
            return await ProjectAsync(reports, ct);
        }

        public Task<int> GetPendingCountAsync(CancellationToken ct = default)
            => _context.LLMReports.CountAsync(r => !_context.InsightPublishLogs.Any(l => l.ReportId == r.Id), ct);

        // Derived from the audit row the scheduler already writes after a
        // reviewer notification, so no per-report "announced" flag is needed.
        public Task<DateTime?> GetLastReviewerNotificationAtAsync(CancellationToken ct = default)
            => _context.LLMLogs
                .AsNoTracking()
                .Where(l => l.Status == "ReviewersNotified")
                .OrderByDescending(l => l.Timestamp)
                .Select(l => (DateTime?)l.Timestamp)
                .FirstOrDefaultAsync(ct);

        // Shared projection for the queue and the detail header.
        private async Task<List<ReportReviewListItemDto>> ProjectAsync(
            List<LLMReport> reports,
            CancellationToken ct)
        {
            if (reports.Count == 0)
                return new List<ReportReviewListItemDto>();

            var reportIds = reports.Select(r => r.Id).ToList();
            var unitIds = reports.Select(r => (uint)r.UnitId).Distinct().ToList();

            var units = await _context.BusinessUnits
                .AsNoTracking()
                .Where(bu => unitIds.Contains(bu.Id))
                .Select(bu => new { bu.Id, bu.Name, ProfileId = (long)bu.Profile.Id, CompanyName = bu.Profile.ProfileName })
                .ToListAsync(ct);

            // Current manager per unit: newest active assignment whose user is
            // not soft-deleted — same rule the team hierarchy uses, so the
            // reviewer sees exactly who a release would reach.
            var managers = await _context.UserTeams
                .AsNoTracking()
                .Where(ut => unitIds.Contains(ut.UnitId) && !ut.IsUnAssigned)
                .Join(_context.Users.Where(u => !u.IsDel),
                      ut => ut.UserId,
                      u => u.UserId,
                      (ut, u) => new { ut.UnitId, ut.CreatedDate, ut.Id, u.UserId, u.Name, u.Username })
                .ToListAsync(ct);

            var managerByUnit = managers
                .GroupBy(m => m.UnitId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(m => m.CreatedDate).ThenByDescending(m => m.Id).First());

            // The decisive publish-log row per report — whichever came first,
            // since only one decision is allowed.
            var decisions = await _context.InsightPublishLogs
                .AsNoTracking()
                .Where(l => reportIds.Contains(l.ReportId))
                .OrderBy(l => l.Id)
                .Select(l => new { l.ReportId, l.OperationType, l.UserId, l.CreatedAt })
                .ToListAsync(ct);

            var decisionByReport = decisions
                .GroupBy(d => d.ReportId)
                .ToDictionary(g => g.Key, g => g.First());

            var themeCounts = await CountByReportAsync(_context.TopThemes.AsNoTracking()
                .Where(t => reportIds.Contains(t.ReportId)).Select(t => t.ReportId), ct);
            var recCounts = await CountByReportAsync(_context.TopRecommendations.AsNoTracking()
                .Where(r => reportIds.Contains(r.ReportId)).Select(r => r.ReportId), ct);
            var actionCounts = await CountByReportAsync(_context.TopActions.AsNoTracking()
                .Where(a => reportIds.Contains(a.ReportId)).Select(a => a.ReportId), ct);

            var reviewerIds = decisionByReport.Values
                .Where(d => d.UserId.HasValue)
                .Select(d => d.UserId!.Value)
                .Distinct()
                .ToList();

            var reviewerNames = reviewerIds.Count == 0
                ? new Dictionary<int, string>()
                : await _context.Users.AsNoTracking()
                    .Where(u => reviewerIds.Contains(u.UserId))
                    .ToDictionaryAsync(u => u.UserId, u => u.Name, ct);

            return reports.Select(r =>
            {
                var unit = units.FirstOrDefault(u => u.Id == (uint)r.UnitId);
                managerByUnit.TryGetValue((uint)r.UnitId, out var manager);
                decisionByReport.TryGetValue(r.Id, out var decision);

                var state = decision == null
                    ? ReportReviewStates.Pending
                    : decision.OperationType == ReportReviewStates.OperationRejected
                        ? ReportReviewStates.Rejected
                        : ReportReviewStates.Released;

                return new ReportReviewListItemDto
                {
                    ReportId = r.Id,
                    UnitId = r.UnitId,
                    UnitName = unit?.Name,
                    ProfileId = unit?.ProfileId ?? 0,
                    CompanyName = unit?.CompanyName,
                    FromDate = r.FromDate,
                    ToDate = r.ToDate,
                    CreatedAt = r.CreatedAt,
                    ReviewState = state,
                    ReviewedByUserId = decision?.UserId,
                    ReviewedByName = decision?.UserId != null && reviewerNames.TryGetValue(decision.UserId.Value, out var name)
                        ? name
                        : null,
                    ReviewedAt = decision?.CreatedAt,
                    ManagerUserId = manager?.UserId,
                    ManagerName = manager?.Name,
                    ManagerEmail = manager?.Username,
                    ThemeCount = themeCounts.TryGetValue(r.Id, out var tc) ? tc : 0,
                    RecommendationCount = recCounts.TryGetValue(r.Id, out var rc) ? rc : 0,
                    ActionCount = actionCounts.TryGetValue(r.Id, out var ac) ? ac : 0
                };
            }).ToList();
        }

        private static async Task<Dictionary<long, int>> CountByReportAsync(IQueryable<long> reportIds, CancellationToken ct)
            => await reportIds
                .GroupBy(id => id)
                .Select(g => new { ReportId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ReportId, x => x.Count, ct);
    }
}
