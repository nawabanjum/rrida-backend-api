using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class User
    {
        [Key]
        public int UserId { get; set; }
        public string TenantId { get; set; }
        public string Username { get; set; }
        public string Name { get; set; }
        public string? CreatedDate { get; set; }

        public string? ModifiedDate { get; set; }

        public string? CreatedBy { get; set; }

        public string? ModifiedBy { get; set; }

        public bool IsDel { get; set; } = false;

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// DERIVED — the highest-ranked of the user's roles in <see cref="Roles"/>.
        /// Kept only so existing readers keep working; write roles through
        /// IUserRoleService, never by setting this. Dropped in phase 2.
        /// </summary>
        public int UserGroupId { get; set; }

        public DateTime? LastLoginDate { get; set; }

        public UserGroup UserGroup { get; set; }

        /// <summary>Every role the user holds. The source of truth for roles.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public ICollection<UserRole> Roles { get; set; } = new List<UserRole>();

        public string? Password { get; set; }
        public bool IsVerified { get; set; }
        public string Status { get; set; }
        public string Type { get; set; }
        public bool IsEntraId { get; set; } = false;
        public bool? IsFirstTimePasswordChange { get; set; } = false;
        public int? ProfileId { get; set; }
        public int FailedLoginAttempts { get; set; } = 0;
        public DateTime? LastFailedLoginAt { get; set; }
    }
}
