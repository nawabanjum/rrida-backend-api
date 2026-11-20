using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Failed-login lock (see <see cref="AccountLockout"/>): get-users shows who is
    /// locked, unlock-user lets a Profile Admin (own profile) or RRIDA Admin (any
    /// profile) clear it before the lockout window ends.
    /// </summary>
    public class UserAccountLockService : IUserAccountLockService
    {
        private readonly ApplicationDbContext _db;
        private readonly IUserRoleAssignmentService _roles;
        private readonly ILogger<UserAccountLockService> _logger;
        private readonly AccountLockout _lockout;

        public UserAccountLockService(
            ApplicationDbContext db,
            IUserRoleAssignmentService roles,
            IConfiguration config,
            ILogger<UserAccountLockService> logger)
        {
            _db = db;
            _roles = roles;
            _logger = logger;
            // Same keys and defaults as POST api/profile/login.
            _lockout = new AccountLockout(
                config.GetValue("LoginPolicy:MaxFailedAttempts", AccountLockout.DefaultMaxFailedAttempts),
                config.GetValue("LoginPolicy:LockoutWindowMinutes", AccountLockout.DefaultLockoutWindowMinutes));
        }

        public void ApplyLockState(IEnumerable<UserStatusDto> users)
        {
            var now = DateTime.UtcNow;
            foreach (var user in users)
            {
                user.LockedUntil = _lockout.LockedUntil(user.FailedLoginAttempts, user.LastFailedLoginAt, now);
                user.IsLocked = user.LockedUntil.HasValue;
            }
        }

        public async Task<UnlockUserResultDto> UnlockUserAsync(int userId, int actorUserId)
        {
            var actorRoles = await _roles.GetRoleIdsAsync(actorUserId);
            var isRridaAdmin = actorRoles.Contains((int)UserGroups.RridaAdmin);
            if (!isRridaAdmin && !actorRoles.Contains((int)UserGroups.ProfileAdmin))
                throw new UnauthorizedAccessException(ResponseMessages.Errors.UnlockNotAllowed);

            var user = userId > 0
                ? await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && !u.IsDel)
                : null;
            if (user == null)
                throw new InvalidOperationException(ResponseMessages.Errors.UserNotFound);

            if (!isRridaAdmin)
            {
                // A Profile Admin unlocks people of their own profile only, and
                // never a RRIDA Admin (platform staff, not a member of the profile).
                var actorProfileId = await _db.Users.AsNoTracking()
                    .Where(u => u.UserId == actorUserId && !u.IsDel)
                    .Select(u => u.ProfileId)
                    .FirstOrDefaultAsync();

                if (actorProfileId == null || user.ProfileId != actorProfileId)
                    throw new UnauthorizedAccessException(ResponseMessages.Errors.UnlockOtherProfileNotAllowed);

                if ((await _roles.GetRoleIdsAsync(user.UserId)).Contains((int)UserGroups.RridaAdmin))
                    throw new UnauthorizedAccessException(ResponseMessages.Errors.UnlockNotAllowed);
            }

            var lockedUntil = _lockout.LockedUntil(user.FailedLoginAttempts, user.LastFailedLoginAt, DateTime.UtcNow);
            if (!lockedUntil.HasValue)
                throw new InvalidOperationException(ResponseMessages.Errors.UserNotLocked);

            // Only the lock is cleared, as a successful login does: status, roles,
            // profile, invitation and verification state are left as they are.
            user.FailedLoginAttempts = 0;
            user.LastFailedLoginAt = null;
            await _db.SaveChangesAsync();

            var unlockedAt = DateTime.UtcNow;
            _logger.LogInformation(
                "User {UserId} (profile {ProfileId}) unlocked by {ActorUserId}; lock would have ended at {LockedUntil:o}.",
                user.UserId, user.ProfileId, actorUserId, lockedUntil.Value);

            return new UnlockUserResultDto
            {
                UserId = user.UserId,
                Name = user.Name,
                Email = user.Username,
                ProfileId = user.ProfileId,
                Status = user.Status,
                StatusName = user.Status switch
                {
                    "1" => "Active",
                    "2" => "Pending",
                    _ => "Inactive"
                },
                IsActive = user.Status == ((int)UserStatus.Active).ToString(),
                IsLocked = false,
                FailedLoginAttempts = 0,
                WasLockedUntil = lockedUntil,
                UnlockedAt = unlockedAt,
                UnlockedByUserId = actorUserId,
                Roles = await _roles.GetRolesAsync(user.UserId)
            };
        }
    }
}
