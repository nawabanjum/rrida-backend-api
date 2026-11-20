using CloverBackoffice.Components.Enums;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // Who may read or change a team's Team Health data (timeline, milestones):
    //   RRIDA Admin        any team
    //   Profile Admin      any team in their own profile
    //   Team Manager       a team they are assigned to (not unassigned), or one below it
    //   Portfolio Manager  a team whose Team Manager (or a manager of a team above it)
    //                      reports to them, directly or through other Portfolio Managers
    public class TeamHealthAccessService : ITeamHealthAccessService
    {
        // Longest parent chain walked when checking a manager's reach; guards
        // against a corrupt ParentId loop.
        private const int MaxHierarchyDepth = 64;

        private readonly ApplicationDbContext _context;
        private readonly IUserRoleAssignmentService _roles;

        public TeamHealthAccessService(ApplicationDbContext context, IUserRoleAssignmentService roles)
        {
            _context = context;
            _roles = roles;
        }

        public async Task<bool> CanAccessTeamAsync(uint unitId, uint unitProfileId, int userId, CancellationToken ct = default)
        {
            var roles = await _roles.GetRoleIdsAsync(userId, ct);

            if (roles.Contains((int)UserGroups.RridaAdmin))
                return true;

            var userProfileId = await _context.Users
                .AsNoTracking()
                .Where(u => u.UserId == userId)
                .Select(u => u.ProfileId)
                .FirstOrDefaultAsync(ct);
            if (userProfileId == null || userProfileId.Value <= 0 || (uint)userProfileId.Value != unitProfileId)
                return false;

            if (roles.Contains((int)UserGroups.ProfileAdmin))
                return true;

            var isTeamManager = roles.Contains((int)UserGroups.TeamManager);
            var isPortfolioManager = roles.Contains((int)UserGroups.PortfolioManager);
            if (!isTeamManager && !isPortfolioManager)
                return false;

            // The team itself and every team above it: a manager of a parent team
            // reaches everything below it, as UnitAccessService scopes it.
            var chain = await GetUnitChainAsync(unitId, ct);

            // Active managers on that chain.
            var managers = await _context.UserTeams
                .AsNoTracking()
                .Where(ut => chain.Contains(ut.UnitId) && !ut.IsUnAssigned)
                .Select(ut => ut.UserId)
                .Distinct()
                .ToListAsync(ct);

            if (isTeamManager && managers.Contains(userId))
                return true;

            if (isPortfolioManager && managers.Count > 0)
                return await ReportsToAsync(managers, userId, unitProfileId, ct);

            return false;
        }

        public async Task<bool> ManagesTeamAsync(uint unitId, uint unitProfileId, int userId, CancellationToken ct = default)
        {
            var roles = await _roles.GetRoleIdsAsync(userId, ct);
            if (!roles.Contains((int)UserGroups.TeamManager))
                return false;

            var userProfileId = await _context.Users
                .AsNoTracking()
                .Where(u => u.UserId == userId)
                .Select(u => u.ProfileId)
                .FirstOrDefaultAsync(ct);
            if (userProfileId == null || userProfileId.Value <= 0 || (uint)userProfileId.Value != unitProfileId)
                return false;

            var chain = await GetUnitChainAsync(unitId, ct);

            return await _context.UserTeams
                .AsNoTracking()
                .AnyAsync(ut => ut.UserId == userId && chain.Contains(ut.UnitId) && !ut.IsUnAssigned, ct);
        }

        // unitId followed by each of its parents, bounded against a ParentId loop.
        private async Task<List<uint>> GetUnitChainAsync(uint unitId, CancellationToken ct)
        {
            var chain = new List<uint>();
            uint? current = unitId;
            for (var depth = 0; current.HasValue && depth < MaxHierarchyDepth && !chain.Contains(current.Value); depth++)
            {
                chain.Add(current.Value);
                var parentId = await _context.BusinessUnits
                    .AsNoTracking()
                    .Where(u => u.Id == current.Value)
                    .Select(u => u.ParentId)
                    .FirstOrDefaultAsync(ct);
                current = parentId.HasValue && parentId.Value > 0 ? (uint)parentId.Value : null;
            }
            return chain;
        }

        // True when any of the managers reports to the Portfolio Manager, directly or
        // through other Portfolio Managers. Reporting lines are the live
        // PortfolioManagerAssignments rows, latest per manager, as TeamStructureService
        // reads them.
        private async Task<bool> ReportsToAsync(List<int> managerUserIds, int portfolioManagerUserId, uint profileId, CancellationToken ct)
        {
            var parentOf = (await _context.PortfolioManagerAssignments
                    .AsNoTracking()
                    .Where(a => a.ProfileId == profileId && !a.IsUnAssigned)
                    .Select(a => new { a.Id, a.ManagerUserId, a.PortfolioManagerUserId })
                    .ToListAsync(ct))
                .GroupBy(a => a.ManagerUserId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.Id).First().PortfolioManagerUserId);

            foreach (var manager in managerUserIds)
            {
                var seen = new HashSet<int> { manager };
                var current = manager;
                while (parentOf.TryGetValue(current, out var parent) && seen.Add(parent))
                {
                    if (parent == portfolioManagerUserId)
                        return true;
                    current = parent;
                }
            }
            return false;
        }
    }
}
