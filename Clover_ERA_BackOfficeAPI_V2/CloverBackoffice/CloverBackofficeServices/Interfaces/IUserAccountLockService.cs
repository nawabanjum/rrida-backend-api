using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Accounts locked by too many failed logins (LoginPolicy): who is locked, and
    /// unlocking them before the lockout window runs out.
    /// </summary>
    public interface IUserAccountLockService
    {
        /// <summary>
        /// POST api/profile/unlock-user. Clears the failed-login lock of
        /// <paramref name="userId"/>; nothing else on the account changes.
        /// Throws UnauthorizedAccessException when the caller may not unlock this
        /// user (not a Profile Admin / RRIDA Admin, or another profile), and
        /// InvalidOperationException when the user does not exist or is not locked.
        /// </summary>
        Task<UnlockUserResultDto> UnlockUserAsync(int userId, int actorUserId);

        /// <summary>Fills IsLocked / LockedUntil on get-users rows.</summary>
        void ApplyLockState(IEnumerable<UserStatusDto> users);
    }
}
