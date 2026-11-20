using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    // Review state of a report. NOT a stored column — derived from the
    // InsightPublishLog rows the product already writes, which is how the
    // workbench flow has always answered "has this been published?"
    // (PromptManagementController's InsightsPublished flag).
    //
    //   Pending  → an LLMReport with no publish-log row at all. The scheduler
    //              now leaves its reports in exactly this state.
    //   Released → has an Insert/Update row: published, and its manager told.
    //   Rejected → has a "Rejected" row and no Insert/Update row. Reuses the
    //              existing OperationType column (Insert/Update/Delete) rather
    //              than adding a status of our own.
    public static class ReportReviewStates
    {
        public const string Pending = "Pending";
        public const string Released = "Released";
        public const string Rejected = "Rejected";

        // Values written to InsightPublishLog.OperationType (varchar(10)).
        public const string OperationRejected = "Rejected";
        public const string OperationInsert = "Insert";
        public const string OperationUpdate = "Update";
    }

    // One row of the admin review queue.
    public class ReportReviewListItemDto
    {
        public long ReportId { get; set; }
        public long UnitId { get; set; }
        public string? UnitName { get; set; }
        public long ProfileId { get; set; }
        public string? CompanyName { get; set; }

        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public DateTime CreatedAt { get; set; }

        public string ReviewState { get; set; } = ReportReviewStates.Pending;

        // From the publish-log row that released (or rejected) the report.
        public int? ReviewedByUserId { get; set; }
        public string? ReviewedByName { get; set; }
        public DateTime? ReviewedAt { get; set; }

        // The manager this report is destined for — resolved live from the
        // unit's current assignment, so a team that lost its manager is visible
        // BEFORE an admin tries to release it.
        public int? ManagerUserId { get; set; }
        public string? ManagerName { get; set; }
        public string? ManagerEmail { get; set; }

        public int ThemeCount { get; set; }
        public int RecommendationCount { get; set; }
        public int ActionCount { get; set; }
    }

    public class ReportReviewQueueDto
    {
        public List<ReportReviewListItemDto> Items { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }

        public int PendingCount { get; set; }
        public int ReleasedCount { get; set; }
        public int RejectedCount { get; set; }
    }

    // Full report as the reviewer sees it. The body is returned in the same
    // string-list shape the workbench publish endpoint (save-run-insights)
    // takes, so the frontend has one editor model for both flows.
    public class ReportReviewDetailDto : ReportReviewListItemDto
    {
        public string? WorksheetFile { get; set; }

        public List<string> TopThemes { get; set; } = new();
        public List<string> TopRecommendations { get; set; } = new();
        public List<string> TopActions { get; set; } = new();
    }

    // Edit payload — deliberately identical in shape to
    // SaveRunInsightsRequestDto, the existing workbench publish payload. Each
    // list fully replaces that section; null leaves it untouched.
    public class UpdateReportInsightsDto
    {
        public List<string>? TopThemes { get; set; }
        public List<string>? TopRecommendations { get; set; }
        public List<string>? TopActions { get; set; }
    }

    // What an approve/reject attempt actually did.
    public class ReportReleaseResultDto
    {
        public long ReportId { get; set; }
        public string ReviewState { get; set; } = string.Empty;
        public DateTime? ReleasedAt { get; set; }

        public bool Success { get; set; }
        public bool ManagerNotified { get; set; }
        public string? ManagerEmail { get; set; }
        public string? Message { get; set; }
    }

    public class RejectReportDto
    {
        // Required — a rejection with no reason isn't actionable by whoever
        // picks the report up next. Stored on the publish-log row.
        public string Reason { get; set; } = string.Empty;
    }

    public class ReviewerNotificationResultDto
    {
        public int PendingReportCount { get; set; }
        public int NewReportCount { get; set; }
        public int RecipientCount { get; set; }
        public bool EmailSent { get; set; }
        public int InAppCreated { get; set; }
        public string? Reason { get; set; }

        public override string ToString() =>
            $"new={NewReportCount}, pending={PendingReportCount}, recipients={RecipientCount}, " +
            $"email={(EmailSent ? "sent" : "no")}, inApp={InAppCreated}" +
            (Reason == null ? "" : $", reason={Reason}");
    }

    // Config echo for the UI: whether the gate is on and whether the caller may
    // act on the queue, so the frontend can hide it instead of showing a 403.
    public class ReportApprovalConfigDto
    {
        public bool Enabled { get; set; }
        public bool CallerIsReviewer { get; set; }
        public List<string> ReviewerEmails { get; set; } = new();
    }
}
