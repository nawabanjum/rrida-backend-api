using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Add User — multiple roles and their relationships.
    //
    // api/profile/upsert-user is the one Add User endpoint. It creates a user, or
    // — when the email already belongs to a user of the same profile — assigns that
    // user an ADDITIONAL role, and in both cases settles the relationship the role
    // needs:
    //
    //   Profile Admin      nothing
    //   Team Manager       a team: existingUnitId, or isNewTeam
    //   Portfolio Manager  a Team Manager placed under them: managerId
    //
    // The request fields live on UserUpsertDto so existing clients keep sending the
    // same body; everything here is what comes back.
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>One role a user holds.</summary>
    public class UserRoleDto
    {
        /// <summary>UserGroups id: 1 Profile Admin, 2 Team Manager, 3 RRIDA Admin, 4 Portfolio Manager.</summary>
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>What the role's relationship step did. Null parts did not apply.</summary>
    public class UserRoleAssignmentResultDto
    {
        /// <summary>The role this request assigned (or found already held).</summary>
        public int UserGroupId { get; set; }

        /// <summary>False when the user already held the role and only a relationship was added.</summary>
        public bool RoleWasAdded { get; set; }

        // ── Team Manager ──
        public long? UnitId { get; set; }
        public string? TeamName { get; set; }
        public bool TeamWasCreated { get; set; }

        /// <summary>
        /// The licence auto-assigned to the user when they became a Team Manager
        /// here, else the licence covering the team this request created. Null when
        /// no licence was free or the role needs none.
        /// </summary>
        public int? LicenseId { get; set; }
        public string? LicenseKey { get; set; }

        /// <summary>The manager this user displaced on the team, when replaceExistingManager was confirmed.</summary>
        public int? ReplacedManagerUserId { get; set; }

        /// <summary>
        /// True when the user has not accepted their invitation yet, so the
        /// replacement is NOT effective: <see cref="ReplacedManagerUserId"/> still
        /// manages the team and is replaced only once the invitation is accepted.
        /// </summary>
        public bool ReplacementPendingActivation { get; set; }

        // ── Portfolio Manager ──

        /// <summary>The Team Manager placed under this Portfolio Manager.</summary>
        public int? ManagerId { get; set; }
        public string? ManagerName { get; set; }

        /// <summary>The Portfolio Manager that manager was moved away from, when replaceExistingPortfolioManager was confirmed.</summary>
        public int? ReplacedPortfolioManagerUserId { get; set; }
    }

    /// <summary>
    /// Returned as the <c>data</c> of a FAILED upsert-user response when the
    /// assignment would displace somebody. Nothing has been written. The client
    /// shows the popup and, on confirm, re-sends the identical request with the
    /// flag named by <see cref="ConfirmWith"/> set to true.
    /// </summary>
    public class AddUserConfirmationDto
    {
        public bool RequiresConfirmation { get; set; } = true;

        /// <summary>"ReplaceTeamManager" or "ReplacePortfolioManager".</summary>
        public string ConfirmationType { get; set; } = string.Empty;

        /// <summary>The request flag that confirms: "replaceExistingManager" or "replaceExistingPortfolioManager".</summary>
        public string ConfirmWith { get; set; } = string.Empty;

        public long? UnitId { get; set; }
        public string? TeamName { get; set; }

        public int? ManagerId { get; set; }
        public string? ManagerName { get; set; }

        /// <summary>The person who would be displaced.</summary>
        public int ExistingUserId { get; set; }
        public string ExistingUserName { get; set; } = string.Empty;
        public string ExistingUserEmail { get; set; } = string.Empty;
    }

    /// <summary>
    /// Result of assigning or removing the Profile Admin role on an existing user.
    /// The user record itself is not changed — only their roles.
    /// </summary>
    public class ProfileAdminRoleResultDto
    {
        public int UserId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int? ProfileId { get; set; }

        /// <summary>True when the user holds Profile Admin after the operation.</summary>
        public bool IsProfileAdmin { get; set; }

        /// <summary>The highest role the user now holds (Users.UserGroupId).</summary>
        public int PrimaryUserGroupId { get; set; }

        /// <summary>Every role the user holds after the operation, highest first.</summary>
        public List<UserRoleDto> Roles { get; set; } = new();
    }

    /// <summary>Outcome of a Profile Admin assign / remove. Data is set on success.</summary>
    public class ProfileAdminRoleOutcomeDto
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>True when the target user does not exist in a profile the caller may manage.</summary>
        public bool IsNotFound { get; set; }

        /// <summary>True when the caller may not change roles on this profile.</summary>
        public bool IsForbidden { get; set; }

        public ProfileAdminRoleResultDto? Data { get; set; }

        public static ProfileAdminRoleOutcomeDto Fail(string message) => new() { ErrorMessage = message };
    }

    /// <summary>
    /// Remove Team Manager from Manage User. Only the user is sent: their teams are
    /// worked out from the user id.
    /// </summary>
    public class RemoveTeamManagerDto
    {
        public int UserId { get; set; }
    }

    /// <summary>The user after the Team Manager role was removed.</summary>
    public class TeamManagerRemovalResultDto
    {
        public int UserId { get; set; }

        /// <summary>Names of the roles the user still holds, highest first.</summary>
        public List<string> Roles { get; set; } = new();

        public bool IsTeamManager { get; set; }

        /// <summary>"Active", "Pending" or "Inactive".</summary>
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>Outcome of removing the Team Manager role. Data is set on success.</summary>
    public class TeamManagerRemovalOutcomeDto
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>True when the target user does not exist in a profile the caller may manage.</summary>
        public bool IsNotFound { get; set; }

        /// <summary>True when the caller may not change roles on this profile.</summary>
        public bool IsForbidden { get; set; }

        public TeamManagerRemovalResultDto? Data { get; set; }

        public static TeamManagerRemovalOutcomeDto Fail(string message) => new() { ErrorMessage = message };
    }

    /// <summary>Outcome of the service call behind upsert-user.</summary>
    public class AddUserOutcomeDto
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>Set instead of an error when the caller must confirm a replacement first.</summary>
        public AddUserConfirmationDto? Confirmation { get; set; }

        /// <summary>True when a user row was created — the invitation is due.</summary>
        public bool IsNewUser { get; set; }

        /// <summary>True when an existing user of the profile was found by email and given a role.</summary>
        public bool IsExistingUserAssignment { get; set; }

        public UserUpsertDto? Data { get; set; }

        /// <summary>True when the request was refused because the profile has no available licence.</summary>
        public bool IsLicenseLimitReached { get; set; }

        public static AddUserOutcomeDto Fail(string message) => new() { ErrorMessage = message };
    }

    /// <summary>One option in the Add User team dropdown.</summary>
    public class AssignableTeamDto
    {
        public long UnitId { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public long? ParentId { get; set; }

        /// <summary>True when choosing this team replaces its manager — show the confirmation.</summary>
        public bool HasManager { get; set; }
        public int? ManagerUserId { get; set; }
        public string? ManagerName { get; set; }
        public string? ManagerEmail { get; set; }
    }

    /// <summary>One option in the Add User Portfolio Manager "select manager" dropdown.</summary>
    public class AssignableManagerDto
    {
        public int UserId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        /// <summary>False while the invitation is still pending.</summary>
        public bool IsVerified { get; set; }

        public int TeamCount { get; set; }

        /// <summary>True when choosing this manager moves them from another Portfolio Manager — show the confirmation.</summary>
        public bool HasPortfolioManager { get; set; }
        public int? PortfolioManagerUserId { get; set; }
        public string? PortfolioManagerName { get; set; }
    }

    /// <summary>A team a user manages, as reported after login.</summary>
    public class ManagedTeamDto
    {
        public long UnitId { get; set; }
        public string TeamName { get; set; } = string.Empty;

        /// <summary>
        /// True when an existing manager was given this team after their first
        /// login and has not acknowledged it yet — show the new-team popup, then
        /// call POST api/profile/managed-teams/acknowledge.
        /// </summary>
        public bool IsNewAssignment { get; set; }

        /// <summary>When the manager was put on this team (UTC).</summary>
        public DateTime AssignedDate { get; set; }
    }

    /// <summary>Body of POST api/profile/managed-teams/acknowledge. Empty/null UnitIds = all of the caller's teams.</summary>
    public class AcknowledgeManagedTeamsRequestDto
    {
        public List<long>? UnitIds { get; set; }
    }

    /// <summary>A user on the other side of a Portfolio Manager relationship, as reported after login.</summary>
    public class RelatedUserDto
    {
        public int UserId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }
}
