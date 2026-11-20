using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    /// <summary>Body of POST api/profile/unlock-user.</summary>
    public class UnlockUserRequestDto
    {
        public int UserId { get; set; }
    }

    /// <summary>Returned by POST api/profile/unlock-user: the user's account state after unlocking.</summary>
    public class UnlockUserResultDto
    {
        public int UserId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int? ProfileId { get; set; }

        /// <summary>Stored UserStatus value, unchanged by an unlock: "1" Active, "2" Pending, "3" Suspend.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>"Active", "Pending" or "Inactive".</summary>
        public string StatusName { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        /// <summary>Always false on success: the user can sign in again.</summary>
        public bool IsLocked { get; set; }

        /// <summary>Failed login attempts after the unlock (0).</summary>
        public int FailedLoginAttempts { get; set; }

        /// <summary>When the lock would have ended on its own (UTC).</summary>
        public DateTime? WasLockedUntil { get; set; }

        public DateTime UnlockedAt { get; set; }
        public int UnlockedByUserId { get; set; }

        /// <summary>The user's roles, unchanged by an unlock.</summary>
        public List<UserRoleDto> Roles { get; set; } = new();
    }
}
