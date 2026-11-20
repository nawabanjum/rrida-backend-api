using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Components;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloverBackoffice.Services.Implementation
{
    public class ReportApprovalService : IReportApprovalService
    {
        private readonly IReportApprovalRepository _repository;
        private readonly IReportNotificationService _reportNotification;
        private readonly ISystemNotificationService _systemNotification;
        private readonly ApplicationDbContext _context;
        private readonly SendGridSettings _sendGridSettings;
        private readonly ReportApprovalOptions _options;
        private readonly ILogger<ReportApprovalService> _logger;

        public ReportApprovalService(
            IReportApprovalRepository repository,
            IReportNotificationService reportNotification,
            ISystemNotificationService systemNotification,
            ApplicationDbContext context,
            SendGridSettings sendGridSettings,
            IOptions<ReportApprovalOptions> options,
            ILogger<ReportApprovalService> logger)
        {
            _repository = repository;
            _reportNotification = reportNotification;
            _systemNotification = systemNotification;
            _context = context;
            _sendGridSettings = sendGridSettings;
            _options = options?.Value ?? new ReportApprovalOptions();
            _logger = logger;
        }

        public bool IsEnabled => _options.Enabled;

        public bool IsReviewer(string? email) => _options.IsReviewer(email);

        public ReportApprovalConfigDto GetConfig(string? callerEmail) => new()
        {
            Enabled = _options.Enabled,
            CallerIsReviewer = _options.IsReviewer(callerEmail),
            ReviewerEmails = _options.NormalisedReviewerEmails()
        };

        public Task<ReportReviewQueueDto> GetQueueAsync(
            string? state,
            long? profileId,
            long? unitId,
            int page,
            int pageSize,
            CancellationToken ct = default)
            => _repository.GetQueueAsync(state, profileId, unitId, page, pageSize, ct);

        public Task<ReportReviewDetailDto?> GetDetailAsync(long reportId, CancellationToken ct = default)
            => _repository.GetDetailAsync(reportId, ct);

        public async Task<(ReportReviewDetailDto? Detail, string? Error)> UpdateInsightsAsync(
            long reportId,
            UpdateReportInsightsDto dto,
            CancellationToken ct = default)
        {
            var report = await _repository.GetReportAsync(reportId, ct);
            if (report == null)
                return (null, $"Report {reportId} not found.");

            var state = await _repository.GetStateAsync(reportId, ct);
            if (state != ReportReviewStates.Pending)
                return (null, $"Report {reportId} is {state} and can no longer be edited here.");

            if (dto == null || (dto.TopThemes == null && dto.TopRecommendations == null && dto.TopActions == null))
                return (null, "Nothing to update: supply topThemes, topRecommendations or topActions.");

            await _repository.UpdateInsightsAsync(reportId, dto, ct);

            var detail = await _repository.GetDetailAsync(reportId, ct);
            return (detail, null);
        }

        // The only path from a scheduler-generated report to its manager.
        //
        // TryPublishAsync writes the publish-log row and refuses if one already
        // exists. That single row is both the release marker and the
        // duplicate-send guard: a second approve (double click, client retry,
        // a job that ran twice) finds it, writes nothing and notifies nobody.
        public async Task<ReportReleaseResultDto> ApproveAndReleaseAsync(
            long reportId,
            int? actorUserId,
            CancellationToken ct = default)
        {
            var report = await _repository.GetReportAsync(reportId, ct);
            if (report == null)
            {
                return new ReportReleaseResultDto
                {
                    ReportId = reportId,
                    Success = false,
                    Message = $"Report {reportId} not found."
                };
            }

            var published = await _repository.TryPublishAsync(reportId, actorUserId, ct);

            if (!published)
            {
                var state = await _repository.GetStateAsync(reportId, ct);

                return new ReportReleaseResultDto
                {
                    ReportId = reportId,
                    ReviewState = state,
                    Success = state == ReportReviewStates.Released,
                    ManagerNotified = state == ReportReviewStates.Released,
                    Message = state == ReportReviewStates.Released
                        ? "Report was already released to the manager. No second notification sent."
                        : "Report was rejected and cannot be released. The next cycle generates a fresh report."
                };
            }

            var notify = await _reportNotification.NotifyReportReadyAsync(
                report.UnitId, reportId, report.FromDate, report.ToDate, ct);

            var delivered = notify.EmailSent || notify.InAppCreated || notify.PushSent;

            if (!delivered)
            {
                // The report IS released — it is published and now visible to
                // the manager — it just wasn't announced. Left as-is rather than
                // unpublished: the notification is a courtesy on top of the
                // release, and undoing a publish would be the bigger surprise.
                _logger.LogWarning(
                    "[ReportApproval] Release of ReportId={ReportId} (UnitId={UnitId}) notified nobody: {Reason}",
                    reportId, report.UnitId, notify.Reason);
            }

            return new ReportReleaseResultDto
            {
                ReportId = reportId,
                ReviewState = ReportReviewStates.Released,
                ReleasedAt = DateTime.UtcNow,
                Success = true,
                ManagerNotified = delivered,
                ManagerEmail = notify.Recipient,
                Message = delivered
                    ? $"Report released. Manager notified ({notify})."
                    : $"Report released but the manager was NOT notified: {notify.Reason ?? "unknown reason"}."
            };
        }

        public async Task<ReportReleaseResultDto> RejectAsync(
            long reportId,
            int? actorUserId,
            string reason,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return new ReportReleaseResultDto
                {
                    ReportId = reportId,
                    Success = false,
                    Message = "A rejection reason is required."
                };
            }

            var report = await _repository.GetReportAsync(reportId, ct);
            if (report == null)
            {
                return new ReportReleaseResultDto
                {
                    ReportId = reportId,
                    Success = false,
                    Message = $"Report {reportId} not found."
                };
            }

            var rejected = await _repository.TryRejectAsync(reportId, actorUserId, reason, ct);
            var state = await _repository.GetStateAsync(reportId, ct);

            return new ReportReleaseResultDto
            {
                ReportId = reportId,
                ReviewState = state,
                Success = rejected,
                ManagerNotified = false,
                Message = rejected
                    ? "Report rejected. The manager was not notified and will not see it."
                    : $"Report {reportId} is already {state}."
            };
        }

        public async Task<ReviewerNotificationResultDto> NotifyReviewersAsync(
            IReadOnlyDictionary<long, int>? runIdByReportId = null,
            CancellationToken ct = default)
        {
            var result = new ReviewerNotificationResultDto();

            if (!_options.Enabled)
            {
                result.Reason = "ReportApproval.Enabled = false; review step is switched off.";
                return result;
            }

            // "New since the reviewers were last emailed" — read from the audit
            // row the scheduler writes after each successful notification, which
            // is why no per-report announced flag exists.
            var lastNotifiedAt = await _repository.GetLastReviewerNotificationAtAsync(ct);

            if (_options.NotifyCooldownHours > 0 && lastNotifiedAt.HasValue &&
                (DateTime.UtcNow - DateTime.SpecifyKind(lastNotifiedAt.Value, DateTimeKind.Utc)).TotalHours
                    < _options.NotifyCooldownHours)
            {
                result.Reason =
                    $"Within the {_options.NotifyCooldownHours}h reviewer-notification cooldown (last sent {lastNotifiedAt:O}).";
                return result;
            }

            var pendingNew = await _repository.GetPendingSinceAsync(lastNotifiedAt, ct);
            result.NewReportCount = pendingNew.Count;
            result.PendingReportCount = await _repository.GetPendingCountAsync(ct);

            if (pendingNew.Count == 0)
            {
                result.Reason = "No reports queued for review since the last notification.";
                return result;
            }

            var recipients = _options.NormalisedReviewerEmails();
            result.RecipientCount = recipients.Count;

            if (recipients.Count == 0)
            {
                // Loud on purpose: reports are piling up in a queue nobody has
                // been told about. Nothing is stamped, so they are announced as
                // soon as the list is configured.
                _logger.LogError(
                    "[ReportApproval] {Count} report(s) are awaiting review but ReportApproval:ReviewerEmails is empty — nobody was notified.",
                    pendingNew.Count);

                result.Reason = "ReportApproval:ReviewerEmails is empty; no reviewer could be notified.";
                return result;
            }

            var reviewLink = BuildReviewLink();

            try
            {
                result.EmailSent = await BussinessManager.SendReportReviewRequestEmailAsync(
                    _sendGridSettings,
                    recipients,
                    pendingNew.Count,
                    result.PendingReportCount,
                    reviewLink,
                    pendingNew.Select(p => (
                        ReportId: p.ReportId,
                        RunId: runIdByReportId != null && runIdByReportId.TryGetValue(p.ReportId, out var runId)
                                   ? (int?)runId
                                   : null,
                        CompanyName: p.CompanyName ?? "N/A",
                        UnitName: p.UnitName ?? "N/A",
                        FromDate: p.FromDate,
                        ToDate: p.ToDate)));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ReportApproval] Reviewer email failed");
                result.Reason = $"email failed: {ex.GetType().Name}: {ex.Message}";
            }

            // In-app bell row for every reviewer who is an actual user, so the
            // queue is visible after login even if the mail is filtered. Matched
            // case-insensitively: the configured list is typed by hand and
            // Users.Username keeps whatever case the account was created with.
            var recipientKeys = recipients.Select(r => r.ToLower()).ToList();

            var reviewerUsers = await _context.Users
                .AsNoTracking()
                .Where(u => !u.IsDel && u.IsActive)
                .Where(u => recipientKeys.Contains(u.Username.ToLower()))
                .Select(u => new { u.UserId })
                .ToListAsync(ct);

            foreach (var reviewer in reviewerUsers)
            {
                try
                {
                    var created = await _systemNotification.CreateNotificationAsync(new CreateSystemNotificationDto
                    {
                        UserId = reviewer.UserId,
                        Title = "Reports Awaiting Review",
                        Message = $"{pendingNew.Count} new report(s) are waiting for review. " +
                                  $"{result.PendingReportCount} report(s) in the queue in total.",
                        NotificationType = "ReportReview",
                        NotificationUrl = reviewLink
                    });

                    if (created != null) result.InAppCreated++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[ReportApproval] In-app reviewer notification failed for UserId={UserId}", reviewer.UserId);
                }
            }

            if (!result.EmailSent && result.InAppCreated == 0 && result.Reason == null)
                result.Reason = "no reviewer notification could be delivered (email and in-app both failed)";

            return result;
        }

        // FrontendBaseUrl + the configured review path, tolerating a trailing
        // slash on one and a leading slash on the other.
        private string BuildReviewLink()
        {
            var baseUrl = (_sendGridSettings?.FrontendBaseUrl ?? string.Empty).TrimEnd('/');
            var path = (_options.ReviewUrlPath ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(path))
                return baseUrl;

            return $"{baseUrl}/{path.TrimStart('/')}";
        }
    }
}
