using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static CloverBackoffice.Components.BussinessManager;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Manual activation (POST api/profile/activate-user): a Profile Admin (own
    /// profile) or RRIDA Admin (any profile) activates an invited or inactive user
    /// and gives them a temporary password, so they can log in without the email
    /// invitation.
    ///
    /// The account ends up exactly as accepting the invitation leaves it
    /// (AccountRegistrationService.ResetPasswordAsync): password hashed, verified,
    /// Active, sessions dropped, a waiting Team Manager handover finalised. The
    /// invitation flow itself is not changed.
    /// </summary>
    public class UserManualActivationService : IUserManualActivationService
    {
        private static readonly string ActiveStatus = ((int)UserStatus.Active).ToString();
        private static readonly string SuspendedStatus = ((int)UserStatus.Suspend).ToString();

        private readonly ApplicationDbContext _db;
        private readonly IUserRoleAssignmentService _roles;
        private readonly IUserRepository _userRepository;
        private readonly ITeamManagerHandoverService _handover;
        private readonly ILogger<UserManualActivationService> _logger;

        public UserManualActivationService(
            ApplicationDbContext db,
            IUserRoleAssignmentService roles,
            IUserRepository userRepository,
            ITeamManagerHandoverService handover,
            ILogger<UserManualActivationService> logger)
        {
            _db = db;
            _roles = roles;
            _userRepository = userRepository;
            _handover = handover;
            _logger = logger;
        }

        public async Task<ActivateUserResultDto> ActivateUserAsync(int userId, string? temporaryPassword, int actorUserId)
        {
            var actorRoles = await _roles.GetRoleIdsAsync(actorUserId);
            var isRridaAdmin = actorRoles.Contains((int)UserGroups.RridaAdmin);
            if (!isRridaAdmin && !actorRoles.Contains((int)UserGroups.ProfileAdmin))
                throw new UnauthorizedAccessException(ResponseMessages.Errors.ActivateNotAllowed);

            var user = userId > 0
                ? await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && !u.IsDel)
                : null;
            if (user == null)
                throw new InvalidOperationException(ResponseMessages.Errors.UserNotFound);

            if (!isRridaAdmin)
            {
                // A Profile Admin activates people of their own profile only, and
                // never a RRIDA Admin (platform staff, not a member of the profile).
                var actorProfileId = await _db.Users.AsNoTracking()
                    .Where(u => u.UserId == actorUserId && !u.IsDel)
                    .Select(u => u.ProfileId)
                    .FirstOrDefaultAsync();

                if (actorProfileId == null || user.ProfileId != actorProfileId)
                    throw new UnauthorizedAccessException(ResponseMessages.Errors.ActivateOtherProfileNotAllowed);

                if ((await _roles.GetRoleIdsAsync(user.UserId)).Contains((int)UserGroups.RridaAdmin))
                    throw new UnauthorizedAccessException(ResponseMessages.Errors.ActivateNotAllowed);
            }

            // Invited = not verified yet / Pending; Inactive = Suspend. A user who is
            // already verified and Active changes their password through
            // forgot-password or the manual reset link, not here.
            if (user.IsVerified && user.Status == ActiveStatus)
                throw new InvalidOperationException(ResponseMessages.Errors.UserAlreadyActive);

            var passwordError = PasswordPolicy.Validate(temporaryPassword);
            if (passwordError != null)
                throw new InvalidOperationException(passwordError);

            var previousStatusName = user.Status == SuspendedStatus ? "Inactive" : "Pending";
            var activatedAt = DateTime.UtcNow;

            user.Password = PasswordUtility.Encrypt(temporaryPassword!);
            user.IsVerified = true;
            user.Status = ActiveStatus;
            user.ModifiedDate = activatedAt.ToString("o");
            // A fresh password starts with a clean failed-login count, so an old
            // lock cannot stop the first login with it.
            user.FailedLoginAttempts = 0;
            user.LastFailedLoginAt = null;

            // The invitation is no longer needed: an outstanding link must not let
            // anyone replace the password the admin just set. Invitation rows store
            // the bare param; manual reset links store the whole URL ("data=").
            var openInvitations = await _db.ResetPasswordLinks
                .Where(l => l.UserId == user.UserId && !l.IsLinkUsed && !l.IsDel
                            && l.ResetLink != null && !l.ResetLink.Contains("data="))
                .ToListAsync();
            foreach (var link in openInvitations)
                link.IsLinkUsed = true;

            // One save: password, status and the revoked invitations land together.
            await _db.SaveChangesAsync();

            // Same as accepting an invitation: sign out every existing session.
            await _userRepository.InvalidateUserTokensAsync(user.UserId);

            // Same as accepting an invitation: a Team Manager replacement that was
            // waiting for this user to become active takes effect now. Never throws.
            await _handover.FinalizePendingForUserAsync(user.UserId);

            // The password is never logged.
            _logger.LogInformation(
                "User {UserId} (profile {ProfileId}) manually activated by {ActorUserId}; was {PreviousStatus}; {RevokedInvitations} invitation link(s) revoked.",
                user.UserId, user.ProfileId, actorUserId, previousStatusName, openInvitations.Count);

            return new ActivateUserResultDto
            {
                UserId = user.UserId,
                Name = user.Name,
                Email = user.Username,
                ProfileId = user.ProfileId,
                Status = user.Status,
                StatusName = "Active",
                IsActive = true,
                IsVerified = true,
                PreviousStatusName = previousStatusName,
                ActivatedAt = activatedAt,
                ActivatedByUserId = actorUserId,
                Roles = await _roles.GetRolesAsync(user.UserId)
            };
        }
    }
}
