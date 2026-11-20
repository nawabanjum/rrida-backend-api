using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Users with more than one role, and the relationships those roles need.
    /// UserRoles is read and written here; Users.UserGroupId is only ever kept in
    /// step with it (highest role) — see RoleHierarchy.
    /// </summary>
    public interface IUserRoleAssignmentService
    {
        /// <summary>Every UserGroups id the user holds.</summary>
        Task<List<int>> GetRoleIdsAsync(int userId, CancellationToken ct = default);

        /// <summary>Every role the user holds, highest first, with names.</summary>
        Task<List<UserRoleDto>> GetRolesAsync(int userId, CancellationToken ct = default);

        Task<bool> HasAnyRoleAsync(int userId, params UserGroups[] roles);

        /// <summary>
        /// The reason a user may NOT be removed from their profile under the
        /// Profile Admin minimum, or null when the removal may go ahead. Applies
        /// only to a target holding the Profile Admin role: an admin cannot remove
        /// themself (when <paramref name="refuseSelf"/>), and nobody — RRIDA Admin
        /// included — may remove the last one, so a profile always keeps at least
        /// one Profile Admin who is not suspended.
        /// </summary>
        Task<string?> ProfileAdminRemovalErrorAsync(
            int targetUserId, int actorUserId, bool refuseSelf, CancellationToken ct = default);

        /// <summary>
        /// The body of api/profile/upsert-user once the caller is authorised and the
        /// profile, role and email have been validated: creates the user or finds the
        /// existing one in the same profile, adds the role, and settles the team /
        /// manager relationship — all in one transaction. Sends no invitation; the
        /// controller does that for <see cref="AddUserOutcomeDto.IsNewUser"/>.
        /// </summary>
        Task<AddUserOutcomeDto> AddOrAssignUserAsync(UserUpsertDto dto, int actorUserId, CancellationToken ct = default);

        /// <summary>
        /// Gives an EXISTING user the Profile Admin role. The user record and their
        /// other roles are unchanged; no user is created. Caller: Profile Admin of
        /// the same profile, or RRIDA Admin.
        /// </summary>
        Task<ProfileAdminRoleOutcomeDto> AssignProfileAdminAsync(int targetUserId, int actorUserId, CancellationToken ct = default);

        /// <summary>
        /// Takes the Profile Admin role off an existing user, keeping their other
        /// roles. Refused for the caller themself, for the last active Profile
        /// Admin, and when Profile Admin is the user's only role.
        /// </summary>
        Task<ProfileAdminRoleOutcomeDto> RemoveProfileAdminAsync(int targetUserId, int actorUserId, CancellationToken ct = default);

        /// <summary>
        /// Takes the Team Manager role off an existing user, however many teams they hold.
        /// Clears them from every team, releases their licence and keeps their other roles;
        /// the teams stay, with no replacement. All of it or none of it. Caller: Profile
        /// Admin (own profile) or RRIDA Admin.
        /// </summary>
        Task<TeamManagerRemovalOutcomeDto> RemoveTeamManagerAsync(int targetUserId, int actorUserId, CancellationToken ct = default);

        /// <summary>Live teams of a profile, with their current manager — the Add User team dropdown.</summary>
        Task<List<AssignableTeamDto>> GetAssignableTeamsAsync(uint profileId, CancellationToken ct = default);

        /// <summary>Live Team Managers of a profile, with their current Portfolio Manager — the Add User manager dropdown.</summary>
        Task<List<AssignableManagerDto>> GetAssignableManagersAsync(uint profileId, int? excludeUserId, CancellationToken ct = default);

        /// <summary>Teams the user currently manages.</summary>
        Task<List<ManagedTeamDto>> GetManagedTeamsAsync(int userId, CancellationToken ct = default);

        /// <summary>
        /// Marks the user's unseen team assignments as seen (the new-team popup was shown).
        /// Null/empty unitIds = all of them. Returns the number of rows marked.
        /// </summary>
        Task<int> AcknowledgeManagedTeamsAsync(int userId, IReadOnlyCollection<long>? unitIds, CancellationToken ct = default);

        /// <summary>Team Managers currently under this Portfolio Manager.</summary>
        Task<List<RelatedUserDto>> GetManagersInPortfolioAsync(int portfolioManagerUserId, CancellationToken ct = default);

        /// <summary>The Portfolio Manager this manager currently reports to, if any.</summary>
        Task<RelatedUserDto?> GetPortfolioManagerOfAsync(int managerUserId, CancellationToken ct = default);
    }
}
