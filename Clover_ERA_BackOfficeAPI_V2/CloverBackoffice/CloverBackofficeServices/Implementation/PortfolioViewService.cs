using System.Globalization;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // Portfolio View, per manager. The caller must hold the Portfolio Manager role; their
    // portfolio is every manager below them in the live PortfolioManagerAssignments, directly
    // or through other Portfolio Managers (latest row per manager, as TeamHealthAccessService
    // and TeamStructureService read the reporting lines), within their own profile. The
    // caller's own teams are not part of it (MVP portfolioTeams walks the children only).
    //
    // A RRIDA Admin has no reporting lines of their own, so they name a profile instead
    // (the tenant guard authorises it) and see every manager in it. No fallback to the
    // profile on their own account: that would show the wrong company's data as an answer.
    //
    // A manager's teams are the teams they hold now (active UserTeams rows). Each team's state
    // follows the Team State card exactly (TeamStateService: calibrating until more than six
    // asked days, the latest resolved day is current), with Headwinds shown as the state when
    // the flag is raised, as the MVP's Portfolio table does. A team with fewer enrolled members
    // than the roll-up visibility floor reads as calibrating whatever its state, the same data
    // access rule as GET api/rrida/manager/roll-up: the state never leaves here.
    //
    // Weeks = calendar weeks the team has been in its current state. Trend = one reading per
    // week for the last quarter, each the Team State card's 30-day shares as of that day;
    // the plotted score is the Cruise share.
    public partial class PortfolioViewService : IPortfolioViewService
    {
        private const int TrendWeeks = 13;   // a quarter

        private readonly ApplicationDbContext _context;
        private readonly IUserRoleAssignmentService _roles;
        private readonly IRridaEngineContextFactory _engine;

        public PortfolioViewService(ApplicationDbContext context, IUserRoleAssignmentService roles, IRridaEngineContextFactory engine)
        {
            _context = context;
            _roles = roles;
            _engine = engine;
        }

        public async Task<TeamMilestoneResult<PortfolioManagersDto>> GetManagersAsync(int actorUserId, uint? profileId = null, CancellationToken ct = default)
        {
            var (scope, error) = await ResolveAsync(actorUserId, null, profileId, ct);
            if (scope == null) return Fail<PortfolioManagersDto>(error!);

            var ids = scope.Managers.Select(m => m.UserId).ToList();
            var teamCounts = (await _context.UserTeams.AsNoTracking()
                    .Where(t => ids.Contains(t.UserId) && !t.IsUnAssigned)
                    .Join(_context.BusinessUnits.Where(b => b.IsDel != true && b.ProfileId == scope.ProfileId),
                        t => t.UnitId, b => b.Id, (t, b) => new { t.UserId, t.UnitId })
                    .Distinct()
                    .ToListAsync(ct))
                .GroupBy(t => t.UserId)
                .ToDictionary(g => g.Key, g => g.Count());

            return TeamMilestoneResult<PortfolioManagersDto>.Ok(new PortfolioManagersDto
            {
                PortfolioManagerId = actorUserId,
                ProfileId = scope.ProfileId,
                Managers = scope.Managers
                    .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(m => new PortfolioManagerDto
                    {
                        ManagerId = m.UserId,
                        ManagerName = m.Name,
                        ProfileId = scope.ProfileId,
                        ReportsToManagerId = m.ReportsTo,
                        TeamCount = teamCounts.GetValueOrDefault(m.UserId),
                        IsPortfolioManager = m.HasReports
                    })
                    .ToList()
            });
        }

        public async Task<TeamMilestoneResult<PortfolioTeamsDto>> GetTeamsAsync(int actorUserId, int? managerId, uint? profileId = null, CancellationToken ct = default)
        {
            var (scope, error) = await ResolveAsync(actorUserId, managerId, profileId, ct);
            if (scope == null) return Fail<PortfolioTeamsDto>(error!);

            var teams = await LoadTeamsAsync(scope, ct);
            return TeamMilestoneResult<PortfolioTeamsDto>.Ok(new PortfolioTeamsDto
            {
                ManagerId = scope.Selected?.UserId,
                ManagerName = scope.Selected?.Name,
                TeamCount = teams.Count,
                ManagerCount = teams.Select(t => t.TeamManagerId).Distinct().Count(),
                Teams = teams
            });
        }

        public async Task<TeamMilestoneResult<PortfolioAlertsDto>> GetAlertsAsync(int actorUserId, int? managerId, uint? profileId = null, CancellationToken ct = default)
        {
            var (scope, error) = await ResolveAsync(actorUserId, managerId, profileId, ct);
            if (scope == null) return Fail<PortfolioAlertsDto>(error!);

            return TeamMilestoneResult<PortfolioAlertsDto>.Ok(BuildAlerts(scope, await LoadTeamsAsync(scope, ct)));
        }

        public async Task<TeamMilestoneResult<PortfolioEranContextDto>> GetEranContextAsync(int actorUserId, int? managerId, uint? profileId = null, CancellationToken ct = default)
        {
            var (scope, error) = await ResolveAsync(actorUserId, managerId, profileId, ct);
            if (scope == null) return Fail<PortfolioEranContextDto>(error!);

            return TeamMilestoneResult<PortfolioEranContextDto>.Ok(BuildEranContext(scope, await LoadTeamsAsync(scope, ct)));
        }

        public async Task<TeamMilestoneResult<string>> BuildEranContextTextAsync(int actorUserId, int? managerId, uint? profileId = null, CancellationToken ct = default)
        {
            var (scope, error) = await ResolveAsync(actorUserId, managerId, profileId, ct);
            if (scope == null) return Fail<string>(error!);

            return TeamMilestoneResult<string>.Ok(BuildEranContextText(scope, BuildEranContext(scope, await LoadTeamsAsync(scope, ct))));
        }

        // ---- scope -------------------------------------------------------------------------

        private sealed record Manager(int UserId, string Name, int ReportsTo, bool HasReports);

        private sealed record Scope(int PortfolioManagerId, uint ProfileId, List<Manager> Managers, Manager? Selected, bool IsRridaAdmin);

        private sealed record Failure(TeamMilestoneError Error, string Message);

        private async Task<(Scope? Scope, Failure? Error)> ResolveAsync(int actorUserId, int? managerId, uint? requestedProfileId, CancellationToken ct)
        {
            if (managerId is < 0)
                return (null, new Failure(TeamMilestoneError.BadRequest, ResponseMessages.Errors.PortfolioManagerIdInvalid));

            var roles = await _roles.GetRoleIdsAsync(actorUserId, ct);
            var isRridaAdmin = roles.Contains((int)UserGroups.RridaAdmin);
            if (!isRridaAdmin && !roles.Contains((int)UserGroups.PortfolioManager))
                return (null, new Failure(TeamMilestoneError.Forbidden, ResponseMessages.Errors.PortfolioNotAllowed));

            var (profile, profileError) = isRridaAdmin
                ? await SelectedProfileAsync(requestedProfileId, ct)
                : await OwnProfileAsync(actorUserId, requestedProfileId, ct);
            if (profileError != null)
                return (null, profileError);

            // Live reporting lines in this profile: each manager's latest row.
            var parentOf = (await _context.PortfolioManagerAssignments.AsNoTracking()
                    .Where(a => a.ProfileId == profile && !a.IsUnAssigned)
                    .Select(a => new { a.Id, a.ManagerUserId, a.PortfolioManagerUserId })
                    .ToListAsync(ct))
                .GroupBy(a => a.ManagerUserId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.Id).First().PortfolioManagerUserId);
            var childrenOf = parentOf
                .GroupBy(p => p.Value)
                .ToDictionary(g => g.Key, g => g.Select(p => p.Key).ToList());

            var reportsTo = isRridaAdmin
                ? await ProfileManagersAsync(profile, parentOf, ct)
                : ManagersBelow(actorUserId, childrenOf);

            var ids = reportsTo.Keys.ToList();
            var names = await _context.Users.AsNoTracking()
                .Where(u => ids.Contains(u.UserId) && !u.IsDel && u.ProfileId == (int)profile)
                .Select(u => new { u.UserId, u.Name })
                .ToDictionaryAsync(u => u.UserId, u => u.Name, ct);

            var managers = reportsTo
                .Where(r => names.ContainsKey(r.Key))
                .Select(r => new Manager(r.Key, names[r.Key] ?? string.Empty, r.Value, childrenOf.ContainsKey(r.Key)))
                .ToList();

            Manager? selected = null;
            if (managerId is > 0)
            {
                selected = managers.FirstOrDefault(m => m.UserId == managerId.Value);
                if (selected == null)
                    return (null, new Failure(TeamMilestoneError.Forbidden, isRridaAdmin
                        ? ResponseMessages.Errors.PortfolioManagerNotInProfile
                        : ResponseMessages.Errors.PortfolioManagerNotInPortfolio));
            }

            return (new Scope(actorUserId, profile, managers, selected, isRridaAdmin), null);
        }

        // The profile a RRIDA Admin picked. CompanyTenantGuardFilter has authorised it.
        private async Task<(uint Profile, Failure? Error)> SelectedProfileAsync(uint? requestedProfileId, CancellationToken ct)
        {
            if (requestedProfileId is not > 0)
                return (0, new Failure(TeamMilestoneError.BadRequest, ResponseMessages.Errors.PortfolioProfileRequired));

            var profile = requestedProfileId.Value;
            var exists = await _context.Profiles.AsNoTracking().AnyAsync(p => p.Id == profile && !p.IsDel, ct);
            return exists
                ? (profile, null)
                : (0, new Failure(TeamMilestoneError.NotFound, ResponseMessages.Errors.PortfolioProfileNotFound));
        }

        // A Portfolio Manager's portfolio is always in their own profile. Naming another one
        // is refused here as well as by the tenant guard, which lets a partner through to its
        // customers.
        private async Task<(uint Profile, Failure? Error)> OwnProfileAsync(int actorUserId, uint? requestedProfileId, CancellationToken ct)
        {
            var own = await _context.Users.AsNoTracking()
                .Where(u => u.UserId == actorUserId && !u.IsDel)
                .Select(u => u.ProfileId)
                .FirstOrDefaultAsync(ct);
            if (own is not > 0 || (requestedProfileId is > 0 && requestedProfileId != (uint)own.Value))
                return (0, new Failure(TeamMilestoneError.Forbidden, ResponseMessages.Errors.PortfolioNotAllowed));

            return ((uint)own.Value, null);
        }

        // Everyone below the caller, breadth first, guarded against a reporting loop.
        private static Dictionary<int, int> ManagersBelow(int actorUserId, Dictionary<int, List<int>> childrenOf)
        {
            var reportsTo = new Dictionary<int, int>();
            var seen = new HashSet<int> { actorUserId };
            var queue = new Queue<int>();
            queue.Enqueue(actorUserId);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var child in childrenOf.GetValueOrDefault(current) ?? new List<int>())
                {
                    if (!seen.Add(child)) continue;
                    reportsTo[child] = current;
                    queue.Enqueue(child);
                }
            }

            return reportsTo;
        }

        // Every manager in the profile, read as TeamStructureService reads them: Team and
        // Portfolio Managers (UserRoles, legacy UserGroupId only for a user with no roles) and
        // anyone else holding a team. A manager nobody oversees reports to 0.
        private async Task<Dictionary<int, int>> ProfileManagersAsync(uint profile, Dictionary<int, int> parentOf, CancellationToken ct)
        {
            var holderIds = await _context.UserTeams.AsNoTracking()
                .Where(t => !t.IsUnAssigned)
                .Join(_context.BusinessUnits.Where(b => b.IsDel != true && b.ProfileId == profile),
                    t => t.UnitId, b => b.Id, (t, b) => t.UserId)
                .Distinct()
                .ToListAsync(ct);

            var teamManager = (int)UserGroups.TeamManager;
            var portfolioManager = (int)UserGroups.PortfolioManager;
            var managerIds = await _context.Users.AsNoTracking()
                .Where(u => u.ProfileId == (int)profile && !u.IsDel
                            && (holderIds.Contains(u.UserId)
                                || u.Roles.Any(r => r.UserGroupId == teamManager || r.UserGroupId == portfolioManager)
                                || (!u.Roles.Any() && (u.UserGroupId == teamManager || u.UserGroupId == portfolioManager))))
                .Select(u => u.UserId)
                .ToListAsync(ct);

            return managerIds.ToDictionary(id => id, id => parentOf.GetValueOrDefault(id));
        }

        private static TeamMilestoneResult<T> Fail<T>(Failure failure) =>
            TeamMilestoneResult<T>.Fail(failure.Error, failure.Message);

        private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
