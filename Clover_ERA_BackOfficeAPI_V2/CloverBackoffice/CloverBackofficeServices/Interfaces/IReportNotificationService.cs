using System;
using System.Threading;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    // Outcome of one notify attempt. Never an exception — a report that saved
    // successfully must not be rolled back because SendGrid or OneSignal was
    // unreachable, but the caller still needs to know what happened so it can be
    // written to the audit trail instead of vanishing.
    public sealed class ReportNotificationResult
    {
        public bool EmailSent { get; init; }

        // OneSignal device push. Independent of InAppCreated — a manager with no
        // registered device still gets the in-app row and the email.
        public bool PushSent { get; init; }

        // Row written to SystemNotifications (the in-app bell list).
        public bool InAppCreated { get; init; }

        // Email address the notification went to, when one was resolved.
        public string? Recipient { get; init; }

        // Human-readable reason nothing (or only part) was sent — no manager
        // assigned to the unit, manager has no email, no registered device, or
        // the provider call failed.
        public string? Reason { get; init; }

        public override string ToString() =>
            $"email={(EmailSent ? "sent" : "no")}, inApp={(InAppCreated ? "created" : "no")}, " +
            $"push={(PushSent ? "sent" : "no")}" +
            (Recipient == null ? "" : $", to={Recipient}") +
            (Reason == null ? "" : $", reason={Reason}");
    }

    // "A report is ready for this unit" — email + push to the unit's assigned
    // manager. Shared by the manual create-report endpoint and the scheduler, so
    // both paths notify identically.
    public interface IReportNotificationService
    {
        Task<ReportNotificationResult> NotifyReportReadyAsync(
            long unitId,
            long reportId,
            DateTime fromDate,
            DateTime toDate,
            CancellationToken ct = default);
    }
}
