using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Manual activation: an admin activates an invited (pending) or inactive user
    /// and sets a temporary password, without the email invitation.
    /// </summary>
    public interface IUserManualActivationService
    {
        /// <summary>
        /// POST api/profile/activate-user. Hashes and stores
        /// <paramref name="temporaryPassword"/>, marks the user verified and Active.
        /// Roles, profile and team assignments are not changed.
        /// Throws UnauthorizedAccessException when the caller may not activate this
        /// user (not a Profile Admin / RRIDA Admin, or another profile), and
        /// InvalidOperationException when the user does not exist, is already
        /// active, or the password breaks <c>PasswordPolicy</c>.
        /// </summary>
        Task<ActivateUserResultDto> ActivateUserAsync(int userId, string? temporaryPassword, int actorUserId);
    }
}
