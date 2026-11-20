using System;

namespace CloverBackoffice.Components.Helpers
{
    /// <summary>
    /// The login lockout rule, in one place: an account is locked while it has
    /// LoginPolicy:MaxFailedAttempts (or more) failed logins and the last one was
    /// less than LoginPolicy:LockoutWindowMinutes ago. Same rule and defaults as
    /// POST api/profile/login, which is what sets and enforces it. There is no
    /// "Locked" Users.Status value: unlocking clears FailedLoginAttempts and
    /// LastFailedLoginAt, exactly what a successful login does.
    /// </summary>
    public sealed class AccountLockout
    {
        public const int DefaultMaxFailedAttempts = 5;
        public const int DefaultLockoutWindowMinutes = 30;

        public int MaxFailedAttempts { get; }
        public int LockoutWindowMinutes { get; }

        public AccountLockout(int maxFailedAttempts, int lockoutWindowMinutes)
        {
            MaxFailedAttempts = maxFailedAttempts;
            LockoutWindowMinutes = lockoutWindowMinutes;
        }

        /// <summary>When the lock ends (UTC), or null when the account is not locked now.</summary>
        public DateTime? LockedUntil(int failedLoginAttempts, DateTime? lastFailedLoginAt, DateTime utcNow)
        {
            if (failedLoginAttempts < MaxFailedAttempts || !lastFailedLoginAt.HasValue)
                return null;

            var until = DateTime.SpecifyKind(lastFailedLoginAt.Value, DateTimeKind.Utc).AddMinutes(LockoutWindowMinutes);
            return utcNow < until ? until : null;
        }
    }
}
