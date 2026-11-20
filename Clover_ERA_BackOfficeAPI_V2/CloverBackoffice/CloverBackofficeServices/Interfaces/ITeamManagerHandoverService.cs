using System.Threading;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Team Manager replacements that wait for the invited manager to accept their
    /// invitation. See UserTeam.IsPendingActivation.
    /// </summary>
    public interface ITeamManagerHandoverService
    {
        /// <summary>
        /// True when the user has not accepted their invitation yet (unverified or
        /// not Active). A replacement by such a user is written as pending.
        /// </summary>
        Task<bool> IsAwaitingActivationAsync(int userId, CancellationToken ct = default);

        /// <summary>
        /// Cancels pending replacements on the unit. Called by every path that puts
        /// a manager on a team: the latest decision wins. Tracks the changes only —
        /// the caller saves and owns the transaction.
        /// </summary>
        Task CancelPendingForUnitAsync(uint unitId, CancellationToken ct = default);

        /// <summary>
        /// Called once the user's invitation has been accepted: each pending
        /// replacement becomes the team's active manager and the outgoing manager
        /// is unassigned and told. Idempotent; never throws.
        /// </summary>
        Task<int> FinalizePendingForUserAsync(int userId, CancellationToken ct = default);
    }
}
