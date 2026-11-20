using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Troubleshooting email logger for the Clovera Personal Snapshot flow.
    /// Sends the complete request/response (plus diagnostics) of the Run and
    /// Status endpoints to a configurable recipient list so the team has full
    /// visibility into a snapshot run — for both success and failure.
    /// All methods are best-effort: a mail failure is logged, never thrown,
    /// so it can never break the API response.
    /// </summary>
    public interface ISnapshotEmailLogService
    {
        /// <summary>
        /// Emails the outcome of an /api/clovera-personal-snapshot/run call.
        /// Always sends (one email per Run call), covering success and failure.
        /// </summary>
        Task SendRunLogAsync(object? request, object? response, bool success, string? error);

        /// <summary>
        /// Emails the FINAL result of a snapshot (Status endpoint), at most once
        /// per snapshot id. Intended to be called on every Status poll; it only
        /// actually sends when the snapshot has reached a terminal state
        /// (Completed / Failed) and no final email has been sent yet for that id.
        /// Returns true when an email was sent on this call.
        /// </summary>
        Task<bool> SendFinalStatusLogOnceAsync(int snapshotId, string status, object? request, object? response, bool success, string? error);
    }
}
