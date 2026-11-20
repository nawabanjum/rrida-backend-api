using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Interfaces
{
    // The admin review gate between report generation and the manager
    // notification.
    //
    // It adds one step to the product's existing publish model rather than
    // replacing it: a report is released when it gets an InsightPublishLog row,
    // exactly as in the workbench flow. What is new is WHO writes that row for
    // scheduler-generated reports (an admin, via ApproveAndReleaseAsync) and
    // that writing it now also notifies the manager.
    public interface IReportApprovalService
    {
        bool IsEnabled { get; }

        bool IsReviewer(string? email);

        ReportApprovalConfigDto GetConfig(string? callerEmail);

        Task<ReportReviewQueueDto> GetQueueAsync(
            string? state,
            long? profileId,
            long? unitId,
            int page,
            int pageSize,
            CancellationToken ct = default);

        Task<ReportReviewDetailDto?> GetDetailAsync(long reportId, CancellationToken ct = default);

        // Corrections before release. Only a report still awaiting review can be
        // edited: once published the manager has a link to it, and the existing
        // workbench edit path (save-run-insights) is the way to revise a
        // published report.
        Task<(ReportReviewDetailDto? Detail, string? Error)> UpdateInsightsAsync(
            long reportId,
            UpdateReportInsightsDto dto,
            CancellationToken ct = default);

        // The release: writes the publish-log row and notifies the manager.
        // Safe to call twice — the second call finds the row and sends nothing.
        Task<ReportReleaseResultDto> ApproveAndReleaseAsync(
            long reportId,
            int? actorUserId,
            CancellationToken ct = default);

        Task<ReportReleaseResultDto> RejectAsync(
            long reportId,
            int? actorUserId,
            string reason,
            CancellationToken ct = default);

        // Tells the configured reviewers about reports queued since they were
        // last told. Called by the scheduler at the end of a generation run.
        //
        // runIdByReportId carries the workbench run each report came out of, for
        // the reports this run produced. The scheduler is the only place that
        // knows the pairing — a report awaiting review has no publish-log row
        // yet, which is where the workbench flow records its RunId — so it hands
        // the map over rather than the email guessing. Reports queued by an
        // earlier cycle whose email failed are still listed, with no run shown.
        Task<ReviewerNotificationResultDto> NotifyReviewersAsync(
            IReadOnlyDictionary<long, int>? runIdByReportId = null,
            CancellationToken ct = default);
    }
}
