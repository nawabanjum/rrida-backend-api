using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class UserStatusDto
    {
        /// <summary>
        /// True when the company this user belongs to has exactly 2 users — the
        /// Company Admin created automatically at registration plus the first user
        /// that admin added. False once the company has more than 2.
        ///
        /// This is a per-COMPANY value, so every row in a get-users response carries
        /// the same one; read it from any element. It matches the flag returned by
        /// upsert-user for the same company.
        /// </summary>
        public bool FirstTimeUserAdded { get; set; }

        public int UserId { get; set; }
        public string TenantId { get; set; }
        public string Username { get; set; }
        public string Name { get; set; }
        public string? CreatedDate { get; set; }
        public string? ModifiedDate { get; set; }
        [JsonIgnore]
        public string? CreatedBy { get; set; }
        [JsonIgnore]
        public string? ModifiedBy { get; set; }
        [JsonIgnore]
        public bool IsDel { get; set; }
        public bool IsActive { get; set; }
        /// <summary>The user's highest role. See <see cref="Roles"/> for all of them.</summary>
        public int UserGroupId { get; set; }
        public string RoleName { get; set; }

        /// <summary>Every role the user holds, highest first.</summary>
        public List<UserRoleDto> Roles { get; set; } = new();
        public DateTime? LastLoginDate { get; set; }
        [JsonIgnore]
        public string? Password { get; set; }
        public bool IsVerified { get; set; }
        public string Status { get; set; }
        public string StatusName { get; set; }
        public string Type { get; set; }
        [JsonIgnore]
        public bool IsEntraId { get; set; }
        public bool? IsFirstTimePasswordChange { get; set; }
        public int? ProfileId { get; set; }

        /// <summary>
        /// Whether the Remove action may be offered for this row to the caller who
        /// asked for the list. False for the caller's own Profile Admin account and
        /// for the profile's last remaining Profile Admin; true for everyone else.
        /// The same rule is enforced by DELETE api/profile/user/{userId} — this is
        /// only so the UI can hide an action that the backend would refuse.
        /// </summary>
        public bool CanRemove { get; set; } = true;

        /// <summary>Why <see cref="CanRemove"/> is false; null when it is true.</summary>
        public string? RemoveBlockedReason { get; set; }

        /// <summary>
        /// True while the account is locked by too many failed logins (get-users
        /// only); the Unlock action, POST api/profile/unlock-user, applies.
        /// </summary>
        public bool IsLocked { get; set; }

        /// <summary>When the lock ends on its own (UTC); null when not locked.</summary>
        public DateTime? LockedUntil { get; set; }

        [JsonIgnore]
        public int FailedLoginAttempts { get; set; }
        [JsonIgnore]
        public DateTime? LastFailedLoginAt { get; set; }

    }
}
