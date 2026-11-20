using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    /// <summary>Body of POST api/profile/activate-user.</summary>
    public class ActivateUserRequestDto
    {
        public int UserId { get; set; }

        /// <summary>
        /// Password the admin gives the user. Stored as a BCrypt hash only; never
        /// returned, never logged (AuditMiddleware masks it).
        /// </summary>
        public string TemporaryPassword { get; set; } = string.Empty;

        public override string ToString() => $"ActivateUserRequestDto {{ UserId = {UserId} }}";
    }

    /// <summary>Returned by POST api/profile/activate-user. Carries no password.</summary>
    public class ActivateUserResultDto
    {
        public int UserId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int? ProfileId { get; set; }

        /// <summary>Stored UserStatus value after activation: always "1" (Active).</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>"Active".</summary>
        public string StatusName { get; set; } = string.Empty;

        public bool IsActive { get; set; }
        public bool IsVerified { get; set; }

        /// <summary>The status before activation: "Pending" (invited) or "Inactive".</summary>
        public string PreviousStatusName { get; set; } = string.Empty;

        public DateTime ActivatedAt { get; set; }
        public int ActivatedByUserId { get; set; }

        /// <summary>The user's roles, unchanged by activation.</summary>
        public List<UserRoleDto> Roles { get; set; } = new();
    }
}
