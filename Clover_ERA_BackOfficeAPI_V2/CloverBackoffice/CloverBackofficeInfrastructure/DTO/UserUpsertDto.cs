using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class UserUpsertDto
    {
        // Optional: the plan of the licence a new Team Manager is given ("light" / "pro").
        // Null = the only free plan; with Light and Pro both free, none (assign it later).
        public string? PlanCode { get; set; }

        public int UserId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        /// <summary>
        /// The profile the user belongs to.
        ///
        /// IGNORED when a Profile Admin calls api/profile/upsert-user — their user
        /// always lands in their own profile, whatever is sent here, so the company
        /// cannot be switched by editing the request. A foreign id is refused
        /// outright (403) by the tenant guard, which recognises this property by
        /// name; omitting it is fine and is the expected client behaviour.
        ///
        /// Only a RRIDA Admin, who administers every profile, may name one. Still
        /// settable by the internal callers that compose UpsertUserAsync directly
        /// (manager onboarding resolves the profile itself and passes it here).
        /// </summary>
        public int? ProfileId { get; set; }

        [JsonIgnore]
        public string? CompanyName { get; set; }

        [JsonIgnore]
        public int CreatedBy { get; set; }

        /// <summary>
        /// REQUEST: the role to give the user — 1 Profile Admin, 2 Team Manager,
        /// 4 Portfolio Manager. ADDITIVE: a user keeps the roles they already hold,
        /// so sending 2 for an existing Profile Admin makes them Profile Admin +
        /// Team Manager.
        ///
        /// RESPONSE: the user's highest role after the call (see <see cref="Roles"/>
        /// for all of them).
        /// </summary>
        [Required]
        public int UserGroupId { get; set; }

        // ── Team Manager (userGroupId 2) ─────────────────────────────────────

        /// <summary>
        /// True to create a new team in the user's profile and make the user its
        /// manager. When false, <see cref="ExistingUnitId"/> names the team.
        /// Required (one or the other) when the user does not already hold Team
        /// Manager; optional afterwards, to give a Team Manager another team.
        /// </summary>
        public bool IsNewTeam { get; set; }

        /// <summary>
        /// An existing team of the SAME profile. Tenant-guarded by name (ends in
        /// "UnitId"): another profile's team is refused with 403 before the action runs.
        /// </summary>
        public long? ExistingUnitId { get; set; }

        /// <summary>
        /// Name for the new team when <see cref="IsNewTeam"/> is true. Blank ("" or
        /// omitted): no team is created — the user is saved without one and sets their
        /// team up after logging in, so login returns managedTeams empty.
        /// </summary>
        public string? TeamName { get; set; }

        /// <summary>
        /// Confirms replacing the team's current manager. Without it, a team that
        /// already has a manager comes back as a failed response whose data is an
        /// <c>AddUserConfirmationDto</c>, and nothing is written.
        /// </summary>
        public bool ReplaceExistingManager { get; set; }

        // ── Portfolio Manager (userGroupId 4) ────────────────────────────────

        /// <summary>
        /// The Team Manager to place under this Portfolio Manager. Must be a live
        /// user of the SAME profile who holds Team Manager. Required when the user
        /// does not already hold Portfolio Manager; optional afterwards, to add
        /// another manager to their portfolio.
        /// </summary>
        public int? ManagerId { get; set; }

        /// <summary>
        /// Confirms moving the manager away from their current Portfolio Manager
        /// (a manager has one at a time). Same confirmation contract as
        /// <see cref="ReplaceExistingManager"/>.
        /// </summary>
        public bool ReplaceExistingPortfolioManager { get; set; }

        // ── Response only ────────────────────────────────────────────────────

        /// <summary>RESPONSE ONLY — every role the user holds after the call.</summary>
        public List<UserRoleDto>? Roles { get; set; }

        /// <summary>RESPONSE ONLY — what the relationship step did.</summary>
        public UserRoleAssignmentResultDto? Assignment { get; set; }

        /// <summary>RESPONSE ONLY — true when the email matched an existing user of the profile, who was given the role instead of a duplicate being created.</summary>
        public bool IsExistingUser { get; set; }

        /// <summary>RESPONSE ONLY — whether the invitation email went out. Always false for an existing user (none is sent).</summary>
        public bool InvitationSent { get; set; }

        /// <summary>
        /// RESPONSE ONLY — always written by the server, any value sent in the
        /// request is ignored.
        ///
        /// True when the company has exactly 2 users — the Company Admin created
        /// automatically at registration plus the first user that admin added.
        /// False once the company has more than 2. The frontend uses this to decide
        /// whether to show the "Pending Account Verification" popup.
        ///
        /// get-users returns the same flag on every UserStatusDto for the company,
        /// computed the same way, so the two endpoints always agree.
        /// </summary>
        public bool FirstTimeUserAdded { get; set; }
    }
}
