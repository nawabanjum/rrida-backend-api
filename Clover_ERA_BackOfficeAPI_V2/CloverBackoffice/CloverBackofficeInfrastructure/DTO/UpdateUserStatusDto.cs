using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class UpdateUserStatusDto
    {
        public int UserId { get; set; }

        /// <summary>"Active" / "Inactive", or the numeric UserStatus: "1" Active, "2" Pending, "3" Suspend (= Inactive).</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Only when making a user Active: how the Profile Admin gives them access again.
        ///   "ManualCredentials" — returns a set-password link (resetLink) for the admin to share.
        ///   "Invite"            — emails the user an invitation to set a new password.
        /// Omitted / null — the status changes only (previous behaviour).
        /// </summary>
        public string? ReactivationMethod { get; set; }
    }

    public static class ReactivationMethods
    {
        public const string ManualCredentials = "ManualCredentials";
        public const string Invite = "Invite";
    }

    /// <summary>Returned by POST api/profile/update-user-status.</summary>
    public class UpdateUserStatusResultDto
    {
        public int UserId { get; set; }

        /// <summary>Stored UserStatus value: "1" Active, "2" Pending, "3" Suspend.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>"Active", "Pending" or "Inactive".</summary>
        public string StatusName { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        /// <summary>The user's roles — unchanged by a status change, returned so the modal can refresh.</summary>
        public List<UserRoleDto> Roles { get; set; } = new();

        /// <summary>The reactivation method that was applied, if any.</summary>
        public string? ReactivationMethod { get; set; }

        /// <summary>ManualCredentials only: set-password link for the admin to share with the user.</summary>
        public string? ResetLink { get; set; }

        /// <summary>Invite only: whether the invitation email was sent.</summary>
        public bool? InvitationSent { get; set; }
    }
}
