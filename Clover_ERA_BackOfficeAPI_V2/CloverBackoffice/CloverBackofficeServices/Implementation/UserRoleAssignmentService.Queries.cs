using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Infrastructure.DTO;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // Read side: roles, the two Add User dropdowns, and what a user manages.
    public partial class UserRoleAssignmentService
    {
        public async Task<List<int>> GetRoleIdsAsync(int userId, CancellationToken ct = default)
        {
            var roleIds = await _context.UserRoles
                .AsNoTracking()
                .Where(r => r.UserId == userId)
                .Select(r => r.UserGroupId)
                .ToListAsync(ct);

            if (roleIds.Count > 0)
                return roleIds;

            // A user created before UserRoles was backfilled. The derived column is
            // the only record of their role, so answer from it rather than "none".
            var legacy = await _context.Users
                .AsNoTracking()
                .Where(u => u.UserId == userId)
                .Select(u => (int?)u.UserGroupId)
                .FirstOrDefaultAsync(ct);

            return legacy is > 0 ? new List<int> { legacy.Value } : new List<int>();
        }

        public async Task<List<UserRoleDto>> GetRolesAsync(int userId, CancellationToken ct = default)
        {
            var roleIds = await GetRoleIdsAsync(userId, ct);
            if (roleIds.Count == 0)
                return new List<UserRoleDto>();

            var names = await _context.UserGroups
                .AsNoTracking()
                .Where(g => roleIds.Contains(g.Id))
                .ToDictionaryAsync(g => g.Id, g => g.Name, ct);

            return roleIds
                .Where(names.ContainsKey)
                .OrderBy(RoleHierarchy.RankOf)
                .Select(id => new UserRoleDto { Id = id, Name = names[id] })
                .ToList();
        }

        // Users.Status as the Suspend enum member is stored — a string column.
        private static readonly string SuspendedStatus = ((int)UserStatus.Suspend).ToString();

        /// <summary>
        /// Whether a user may be taken off the profile, from the Profile Admin
        /// minimum's point of view. Null means go ahead.
        ///
        /// Two rules, and only for a target who actually holds the Profile Admin
        /// role — removing anyone else is not this method's business:
        ///   * <paramref name="refuseSelf"/> — an admin never removes themself. Used
        ///     by delete; not by suspend, where suspending yourself while other
        ///     admins remain is a legitimate thing to do.
        ///   * the last one standing — whoever is asking, the profile must keep at
        ///     least one Profile Admin who is not suspended. A RRIDA Admin is bound
        ///     by this too: the profile would be left with no administrator either way.
        ///
        /// A user who is invited but has not accepted yet still counts: they hold
        /// the role and can accept. A suspended one does not — they cannot sign in.
        /// </summary>
        public async Task<string?> ProfileAdminRemovalErrorAsync(
            int targetUserId, int actorUserId, bool refuseSelf, CancellationToken ct = default)
        {
            var target = await _context.Users
                .AsNoTracking()
                .Where(u => u.UserId == targetUserId && !u.IsDel)
                .Select(u => new { u.UserId, u.ProfileId })
                .FirstOrDefaultAsync(ct);

            // No such user: "not found" is the caller's answer to give, not ours.
            if (target == null)
                return null;

            var roles = await GetRoleIdsAsync(targetUserId, ct);
            if (!roles.Contains((int)UserGroups.ProfileAdmin))
                return null;

            if (refuseSelf && targetUserId == actorUserId)
                return Components.Helpers.ResponseMessages.Errors.ProfileAdminCannotRemoveSelf;

            var remaining = await CountProfileAdminsAsync(target.ProfileId, targetUserId, ct);
            return remaining == 0
                ? Components.Helpers.ResponseMessages.Errors.LastProfileAdminRequired
                : null;
        }

        /// <summary>
        /// Profile Admins of a profile who could administer it right now, not
        /// counting <paramref name="excludeUserId"/> — the user about to be removed.
        /// Reads UserRoles, falling back to the derived column for a user whose
        /// role rows were never backfilled, the same way GetRoleIdsAsync does.
        /// </summary>
        private Task<int> CountProfileAdminsAsync(int? profileId, int excludeUserId, CancellationToken ct)
            => _context.Users
                .AsNoTracking()
                .CountAsync(u => u.ProfileId == profileId
                                 && u.UserId != excludeUserId
                                 && !u.IsDel
                                 && (u.Status == null || u.Status != SuspendedStatus)
                                 && (u.Roles.Any(r => r.UserGroupId == (int)UserGroups.ProfileAdmin)
                                     || (!u.Roles.Any() && u.UserGroupId == (int)UserGroups.ProfileAdmin)), ct);

        public async Task<bool> HasAnyRoleAsync(int userId, params UserGroups[] roles)
        {
            var wanted = roles.Select(r => (int)r).ToList();
            var held = await GetRoleIdsAsync(userId);
            return held.Any(wanted.Contains);
        }

        public async Task<List<AssignableTeamDto>> GetAssignableTeamsAsync(uint profileId, CancellationToken ct = default)
        {
            var teams = await _context.BusinessUnits
                .AsNoTracking()
                .Where(bu => bu.ProfileId == profileId && bu.IsDel != true)
                .OrderBy(bu => bu.Name)
                .Select(bu => new AssignableTeamDto
                {
                    UnitId = bu.Id,
                    TeamName = bu.Name,
                    ParentId = bu.ParentId
                })
                .ToListAsync(ct);

            if (teams.Count == 0)
                return teams;

            var unitIds = teams.Select(t => (uint)t.UnitId).ToList();

            // One query for every team's manager. A team has one live manager; the
            // newest row wins should old data ever hold two.
            var managers = await (from ut in _context.UserTeams.AsNoTracking()
                                  join u in _context.Users.AsNoTracking() on ut.UserId equals u.UserId
                                  where unitIds.Contains(ut.UnitId) && !ut.IsUnAssigned && !u.IsDel
                                  orderby ut.CreatedDate descending, ut.Id descending
                                  select new { ut.UnitId, u.UserId, u.Name, u.Username })
                                 .ToListAsync(ct);

            var byUnit = managers
                .GroupBy(m => m.UnitId)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var team in teams)
            {
                if (!byUnit.TryGetValue((uint)team.UnitId, out var manager))
                    continue;

                team.HasManager = true;
                team.ManagerUserId = manager.UserId;
                team.ManagerName = manager.Name;
                team.ManagerEmail = manager.Username;
            }

            return teams;
        }

        public async Task<List<AssignableManagerDto>> GetAssignableManagersAsync(
            uint profileId, int? excludeUserId, CancellationToken ct = default)
        {
            var pid = (int)profileId;

            // Team Managers only — held as a role, whatever else the user also is.
            // Invited users who have not set a password yet are included: they hold
            // the role, and the dropdown flags them through IsVerified.
            var managers = await _context.Users
                .AsNoTracking()
                .Where(u => u.ProfileId == pid
                            && !u.IsDel
                            && u.Roles.Any(r => r.UserGroupId == (int)UserGroups.TeamManager)
                            && (excludeUserId == null || u.UserId != excludeUserId))
                .OrderBy(u => u.Name)
                .Select(u => new AssignableManagerDto
                {
                    UserId = u.UserId,
                    Name = u.Name,
                    Email = u.Username,
                    IsVerified = u.IsVerified
                })
                .ToListAsync(ct);

            if (managers.Count == 0)
                return managers;

            var ids = managers.Select(m => m.UserId).ToList();

            var teamCounts = await _context.UserTeams
                .AsNoTracking()
                .Where(ut => ids.Contains(ut.UserId) && !ut.IsUnAssigned)
                .GroupBy(ut => ut.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Select(x => x.UnitId).Distinct().Count() })
                .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

            var portfolioManagers = await (from a in _context.PortfolioManagerAssignments.AsNoTracking()
                                           join pm in _context.Users.AsNoTracking() on a.PortfolioManagerUserId equals pm.UserId
                                           where ids.Contains(a.ManagerUserId) && !a.IsUnAssigned
                                           select new { a.ManagerUserId, pm.UserId, pm.Name })
                                          .ToDictionaryAsync(x => x.ManagerUserId, ct);

            foreach (var manager in managers)
            {
                manager.TeamCount = teamCounts.TryGetValue(manager.UserId, out var count) ? count : 0;

                if (portfolioManagers.TryGetValue(manager.UserId, out var pm))
                {
                    manager.HasPortfolioManager = true;
                    manager.PortfolioManagerUserId = pm.UserId;
                    manager.PortfolioManagerName = pm.Name;
                }
            }

            return managers;
        }

        public async Task<List<ManagedTeamDto>> GetManagedTeamsAsync(int userId, CancellationToken ct = default)
        {
            return await (from ut in _context.UserTeams.AsNoTracking()
                          join bu in _context.BusinessUnits.AsNoTracking() on ut.UnitId equals bu.Id
                          where ut.UserId == userId && !ut.IsUnAssigned && bu.IsDel != true
                          orderby ut.CreatedDate descending, ut.Id descending
                          select new ManagedTeamDto
                          {
                              UnitId = bu.Id,
                              TeamName = bu.Name,
                              AssignedDate = ut.CreatedDate,
                              IsNewAssignment = ut.AssignmentSeenAt == null && ut.CreatedBy != ut.UserId
                          })
                         .ToListAsync(ct);
        }

        public async Task<int> AcknowledgeManagedTeamsAsync(int userId, IReadOnlyCollection<long>? unitIds, CancellationToken ct = default)
        {
            var seenAt = DateTime.UtcNow;
            var query = _context.UserTeams.Where(ut => ut.UserId == userId && ut.AssignmentSeenAt == null);

            if (unitIds is { Count: > 0 })
                query = query.Where(ut => unitIds.Contains((long)ut.UnitId));

            return await query.ExecuteUpdateAsync(s => s.SetProperty(ut => ut.AssignmentSeenAt, seenAt), ct);
        }

        public async Task<List<RelatedUserDto>> GetManagersInPortfolioAsync(int portfolioManagerUserId, CancellationToken ct = default)
        {
            return await (from a in _context.PortfolioManagerAssignments.AsNoTracking()
                          join u in _context.Users.AsNoTracking() on a.ManagerUserId equals u.UserId
                          where a.PortfolioManagerUserId == portfolioManagerUserId && !a.IsUnAssigned && !u.IsDel
                          orderby u.Name
                          select new RelatedUserDto { UserId = u.UserId, Name = u.Name, Email = u.Username })
                         .ToListAsync(ct);
        }

        public async Task<RelatedUserDto?> GetPortfolioManagerOfAsync(int managerUserId, CancellationToken ct = default)
        {
            return await (from a in _context.PortfolioManagerAssignments.AsNoTracking()
                          join u in _context.Users.AsNoTracking() on a.PortfolioManagerUserId equals u.UserId
                          where a.ManagerUserId == managerUserId && !a.IsUnAssigned && !u.IsDel
                          select new RelatedUserDto { UserId = u.UserId, Name = u.Name, Email = u.Username })
                         .FirstOrDefaultAsync(ct);
        }
    }
}
