using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// The teams a user can open in Team Health. Uses the same rule every Team Health
    /// endpoint checks (<see cref="ITeamHealthAccessService"/>), so each team listed is one
    /// Team Health will open for that user, and no other team is ever listed.
    /// </summary>
    public class TeamHealthUserTeamsService : ITeamHealthUserTeamsService
    {
        private readonly ApplicationDbContext _context;
        private readonly ITeamHealthAccessService _access;
        private readonly IUserRoleAssignmentService _roles;

        public TeamHealthUserTeamsService(ApplicationDbContext context, ITeamHealthAccessService access, IUserRoleAssignmentService roles)
        {
            _context = context;
            _access = access;
            _roles = roles;
        }

        public async Task<TeamMilestoneResult<UserTeamsDto>> GetUserTeamsAsync(int userId, int actorUserId, CancellationToken ct = default)
        {
            if (userId <= 0)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.UserIdRequired);

            var user = await _context.Users.AsNoTracking()
                .Where(u => u.UserId == userId && !u.IsDel)
                .Select(u => new { u.UserId, u.Name, u.ProfileId })
                .FirstOrDefaultAsync(ct);
            if (user == null)
                return Fail(TeamMilestoneError.NotFound, ResponseMessages.Errors.UserNotFound);

            var userRoles = await _roles.GetRoleIdsAsync(userId, ct);
            if (!await CanViewAsync(user.UserId, user.ProfileId, actorUserId, ct))
                return Fail(TeamMilestoneError.Forbidden, ResponseMessages.Errors.UserTeamsForbidden);

            var result = new UserTeamsDto
            {
                UserId = user.UserId,
                UserName = user.Name,
                ProfileId = user.ProfileId > 0 ? (uint)user.ProfileId.Value : null,
                Roles = userRoles.OrderBy(r => r).ToList()
            };

            // A RRIDA Admin reaches every team; anyone else only teams of their own profile.
            var isRridaAdmin = userRoles.Contains((int)UserGroups.RridaAdmin);
            if (!isRridaAdmin && result.ProfileId is null)
                return TeamMilestoneResult<UserTeamsDto>.Ok(result);

            var candidates = await _context.BusinessUnits.AsNoTracking()
                .Where(b => b.IsDel != true && (isRridaAdmin || b.ProfileId == result.ProfileId))
                .OrderBy(b => b.Name).ThenBy(b => b.Id)
                .Select(b => new UserTeamDto
                {
                    TeamId = b.Id,
                    TeamName = b.Name,
                    TeamCode = b.BusinessUnitShortCode,
                    ProfileId = b.ProfileId,
                    ParentTeamId = b.ParentId,
                    TeamSize = b.TeamSize
                })
                .ToListAsync(ct);

            // Admins see every candidate; managers are filtered team by team with the
            // Team Health rule itself (hierarchy, portfolio reporting lines).
            var isProfileAdmin = userRoles.Contains((int)UserGroups.ProfileAdmin);
            foreach (var team in candidates)
            {
                if (isRridaAdmin || isProfileAdmin
                    || await _access.CanAccessTeamAsync(team.TeamId, team.ProfileId, userId, ct))
                    result.Teams.Add(team);
            }

            await AddManagersAsync(result.Teams, ct);
            result.TotalTeams = result.Teams.Count;
            return TeamMilestoneResult<UserTeamsDto>.Ok(result);
        }

        // You may always list your own teams. Otherwise a RRIDA Admin may list anyone's,
        // and a Profile Admin those of a user in their own profile.
        private async Task<bool> CanViewAsync(int userId, int? userProfileId, int actorUserId, CancellationToken ct)
        {
            if (actorUserId == userId) return true;

            var actorRoles = await _roles.GetRoleIdsAsync(actorUserId, ct);
            if (actorRoles.Contains((int)UserGroups.RridaAdmin)) return true;
            if (!actorRoles.Contains((int)UserGroups.ProfileAdmin)) return false;

            var actorProfileId = await _context.Users.AsNoTracking()
                .Where(u => u.UserId == actorUserId && !u.IsDel)
                .Select(u => u.ProfileId)
                .FirstOrDefaultAsync(ct);
            return actorProfileId > 0 && actorProfileId == userProfileId;
        }

        // Each team's current Team Managers: active assignments of users who hold the role.
        private async Task AddManagersAsync(List<UserTeamDto> teams, CancellationToken ct)
        {
            if (teams.Count == 0) return;

            var teamIds = teams.Select(t => t.TeamId).ToList();
            var assignments = await _context.UserTeams.AsNoTracking()
                .Where(ut => teamIds.Contains(ut.UnitId) && !ut.IsUnAssigned)
                .Join(_context.Users.Where(u => !u.IsDel), ut => ut.UserId, u => u.UserId,
                      (ut, u) => new { ut.UnitId, u.UserId, u.Name, Email = u.Username })
                .Distinct()
                .ToListAsync(ct);

            var managerIds = new HashSet<int>();
            foreach (var id in assignments.Select(a => a.UserId).Distinct())
            {
                if ((await _roles.GetRoleIdsAsync(id, ct)).Contains((int)UserGroups.TeamManager))
                    managerIds.Add(id);
            }

            var byTeam = assignments
                .Where(a => managerIds.Contains(a.UserId))
                .GroupBy(a => a.UnitId)
                .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Name)
                    .Select(a => new UserTeamManagerDto { UserId = a.UserId, Name = a.Name, Email = a.Email })
                    .ToList());

            foreach (var team in teams)
                team.Managers = byTeam.TryGetValue(team.TeamId, out var managers) ? managers : new();
        }

        private static TeamMilestoneResult<UserTeamsDto> Fail(TeamMilestoneError error, string message) =>
            TeamMilestoneResult<UserTeamsDto>.Fail(error, message);
    }
}
