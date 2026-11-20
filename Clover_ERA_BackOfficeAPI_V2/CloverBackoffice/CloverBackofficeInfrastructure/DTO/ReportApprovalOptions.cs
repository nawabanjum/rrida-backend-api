using System;
using System.Collections.Generic;
using System.Linq;

namespace CloverBackoffice.Infrastructure.DTO
{
    // Admin review-and-approve gate between report generation and the manager
    // notification. Bound to "ReportApproval" in appsettings.json.
    //
    // The reviewer list is configuration, never code: the three people who sign
    // reports off change without a deploy, and in Production they are supplied
    // per-environment through Azure App Settings
    // (ReportApproval__ReviewerEmails__0, __1, __2) rather than living in the
    // repo.
    public class ReportApprovalOptions
    {
        // false = pre-approval behaviour: the scheduler releases each report to
        // its manager the moment it is generated. Kept as a switch so the gate
        // can be turned off in an environment without redeploying, and so the
        // old flow stays reachable if the review step has to be rolled back.
        public bool Enabled { get; set; } = true;

        // Who gets the "reports are waiting for review" email. Also the
        // allow-list for the review APIs: a caller must be a RridaAdmin AND
        // appear here. An empty list therefore locks the queue rather than
        // opening it — deliberate, since the alternative (empty = everyone) is
        // the wrong way for a permission check to fail.
        public List<string> ReviewerEmails { get; set; } = new();

        // Path appended to SendGridSettings.FrontendBaseUrl to build the "log in
        // and review" link in the reviewer email. Points at the workbench
        // history screen, where reports are reviewed and published.
        public string ReviewUrlPath { get; set; } = "/history";

        // Reviewers already looking at a full queue don't need another mail for
        // every tick. 0 = notify on every run that produces new pending reports.
        public int NotifyCooldownHours { get; set; } = 0;

        // Whether publishing a report from the workbench (save-run-insights)
        // notifies the unit's manager, exactly as approving one through the
        // review API does.
        //
        // On, because the admin reviews and releases scheduled reports on the
        // workbench history screen — the link in the reviewer email — and the
        // manager has to hear about a report that has just been released.
        // Only ever fires on a report's FIRST release, so editing and re-saving
        // a published report mails nobody a second time.
        //
        // Set false to go back to publishing silently from the workbench, with
        // the manager notified only via POST /api/report-approvals/{id}/approve.
        public bool NotifyManagerOnWorkbenchPublish { get; set; } = true;

        // Case-insensitive membership test used by both the notifier and the
        // API gate, with the trimming callers would otherwise each redo.
        public bool IsReviewer(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;

            return NormalisedReviewerEmails()
                .Contains(email.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        public List<string> NormalisedReviewerEmails() =>
            (ReviewerEmails ?? new List<string>())
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }
}
