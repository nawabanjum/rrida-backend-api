using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CloverBackoffice.Infrastructure.Models
{
    /// <summary>
    /// One role held by one user. A user may hold several — Profile Admin + Team
    /// Manager, Team Manager + Portfolio Manager, and so on — which the single
    /// <c>Users.UserGroupId</c> column could not express.
    ///
    /// THE source of truth for what a user is. <c>Users.UserGroupId</c> is kept
    /// during the transition as a DERIVED column: it always holds the highest-ranked
    /// role in this table (see <c>RoleHierarchy.PrimaryOf</c>), so the code that
    /// still reads it keeps answering "highest privilege wins" correctly. It is
    /// dropped in phase 2 once those readers have moved here.
    ///
    /// (UserId, UserGroupId) is unique — a role is held or it is not.
    /// </summary>
    public class UserRole
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }

        /// <summary>The <c>UserGroups</c> primary key — see the UserGroups enum.</summary>
        public int UserGroupId { get; set; }

        /// <summary>The user who granted the role. Null for backfilled and system-created rows.</summary>
        public int? CreatedBy { get; set; }

        public DateTime CreatedDate { get; set; }

        [JsonIgnore]
        public User User { get; set; }

        [JsonIgnore]
        public UserGroup UserGroup { get; set; }
    }
}
