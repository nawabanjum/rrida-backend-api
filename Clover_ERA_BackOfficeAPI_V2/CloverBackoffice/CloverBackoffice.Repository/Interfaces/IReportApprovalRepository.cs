using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Repository.Interfaces
{
    // Data access for the admin review queue.
    //
    // There is no review-status column anywhere: a report's state is derived
    // from its InsightPublishLog rows, the same marker the workbench flow has
    // always used to answer "has this been published?". Pending means the
    // report has no publish-log row at all — which is precisely the state the
    // scheduler now leaves its reports in.
    public interface IReportApprovalRepository
    {
        // state: ReportReviewStates.Pending / Released / Rejected, or null for all.
        Task<ReportReviewQueueDto> GetQueueAsync(
            string? state,
            long? profileId,
            long? unitId,
            int page,
            int pageSize,
            CancellationToken ct = default);

        Task<ReportReviewDetailDto?> GetDetailAsync(long reportId, CancellationToken ct = default);

        Task<LLMReport?> GetReportAsync(long reportId, CancellationToken ct = default);

        // Pending / Released / Rejected for one report.
        Task<string> GetStateAsync(long reportId, CancellationToken ct = default);

        // Replaces the supplied sections of a report's body, reusing the same
        // delete-then-insert the workbench publish path uses. Null sections are
        // left alone. Editing does NOT publish.
        Task UpdateInsightsAsync(long reportId, UpdateReportInsightsDto dto, CancellationToken ct = default);

        // Writes the publish-log row that releases the report, from its current
        // Top* rows. Returns false when a row already exists — that single
        // check is the duplicate-release guard, and it is why no
        // "ManagerNotifiedAt" column is needed.
        Task<bool> TryPublishAsync(long reportId, int? actorUserId, CancellationToken ct = default);

        // Writes a publish-log row with OperationType = "Rejected", keeping the
        // report out of both the pending queue and the manager's report list.
        Task<bool> TryRejectAsync(long reportId, int? actorUserId, string reason, CancellationToken ct = default);

        // Pending reports created since the reviewers were last told, so a
        // second run of the job announces nothing new.
        Task<List<ReportReviewListItemDto>> GetPendingSinceAsync(DateTime? since, CancellationToken ct = default);

        Task<int> GetPendingCountAsync(CancellationToken ct = default);

        // Timestamp of the last "reviewers were emailed" audit row the job
        // writes (LLMLogs.Status = 'ReviewersNotified'). Used instead of a
        // per-report notified flag.
        Task<DateTime?> GetLastReviewerNotificationAtAsync(CancellationToken ct = default);
    }
}
