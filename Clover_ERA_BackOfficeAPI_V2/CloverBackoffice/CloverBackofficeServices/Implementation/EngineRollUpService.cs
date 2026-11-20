using CloverBackoffice.Components.Enums;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// The manager-of-managers roll-up, and the only signal that is theirs to act on.
    ///
    /// Read-only by design. The senior manager sees each child team's state, whether a
    /// cycle is running, and how many have closed or failed. They do not get the child
    /// team's action, its issue file or its chips, and are not prompted to intervene in it.
    ///
    /// Computed from the stored results rather than from a live engine run, because the
    /// engine's own roll-up needs every team replayed inside one run scope and the daily
    /// job replays them one at a time.
    /// </summary>
    public sealed class EngineRollUpService : IEngineRollUpService
    {
        private readonly ApplicationDbContext _context;
        private readonly IRridaEngineContextFactory _engine;
        private readonly ILogger<EngineRollUpService> _logger;

        public EngineRollUpService(
            ApplicationDbContext context,
            IRridaEngineContextFactory engine,
            ILogger<EngineRollUpService> logger)
        {
            _context = context;
            _engine = engine;
            _logger = logger;
        }

        public async Task<EngineRollUpDto> GetRollUpAsync(User actor, CancellationToken ct = default)
        {
            var config = _engine.Context.Config;
            var childUnitIds = await ResolveChildTeamsAsync(actor, ct);

            var result = new EngineRollUpDto
            {
                ChildTeams = childUnitIds.Count,
                VisibilityFloor = config.RollUpVisibilityFloor
            };

            if (childUnitIds.Count == 0) return result;

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var units = await _context.BusinessUnits.AsNoTracking()
                .Where(b => childUnitIds.Contains(b.Id))
                .Select(b => new { b.Id, b.Name, b.TeamSize })
                .ToListAsync(ct);
            var names = units.ToDictionary(b => b.Id, b => b.Name);
            var teamSizes = units.ToDictionary(b => b.Id, b => b.TeamSize);
            var sizeHistory = await TeamHeadcount.LoadHistoryAsync(_context, childUnitIds, ct);

            // Enrolled today, per team: the floor on visibility counts people, not answers.
            var enrolled = await _context.EngineTeamMemberships.AsNoTracking()
                .Where(m => childUnitIds.Contains(m.BusinessUnitId)
                            && m.EffectiveFrom <= today
                            && (m.EffectiveTo == null || m.EffectiveTo >= today))
                .GroupBy(m => m.BusinessUnitId)
                .Select(g => new { UnitId = g.Key, Count = g.Select(x => x.MemberId).Distinct().Count() })
                .ToDictionaryAsync(x => x.UnitId, x => x.Count, ct);

            var latestStates = await LatestStatesAsync(childUnitIds, ct);

            var cycles = await _context.EngineCycles.AsNoTracking()
                .Where(c => childUnitIds.Contains(c.BusinessUnitId))
                .Select(c => new { c.BusinessUnitId, c.IssueFileKey, c.Issue, c.Outcome, c.ConfirmationDueDay, c.SignalReadDay })
                .ToListAsync(ct);

            foreach (var unitId in childUnitIds)
            {
                var teamCycles = cycles.Where(c => c.BusinessUnitId == unitId).ToList();
                var state = latestStates.GetValueOrDefault(unitId);
                // The floor protects anonymity, so it counts the fewer of the team size and the
                // members enrolled: an enrolment can be a second device of one person, and a team
                // size can be out of date. Neither can make a team visible that was hidden before.
                var enrolledToday = enrolled.GetValueOrDefault(unitId, 0);
                var size = Math.Min(enrolledToday, TeamHeadcount.DisplaySizeOn(
                    sizeHistory.GetValueOrDefault(unitId) ?? [], teamSizes.GetValueOrDefault(unitId), today, enrolledToday));
                var belowFloor = size < config.RollUpVisibilityFloor;

                var row = new EngineRollUpRowDto
                {
                    BusinessUnitId = unitId,
                    TeamName = names.GetValueOrDefault(unitId, $"unit {unitId}"),
                    CyclesClosed = teamCycles.Count(c => c.Outcome == "closed"),
                    CyclesFailed = teamCycles.Count(c => c.Outcome == "failed"),
                    BelowVisibilityFloor = belowFloor,
                    IsCalibrating = state is null,
                    CycleStatus = CycleStatusOf(teamCycles.Any(c => c.Outcome == null),
                                                teamCycles.LastOrDefault()?.Outcome)
                };

                // A child team below the floor reads as calibrating whatever its real
                // state. This is a data access rule, not a UI one, so the state never
                // leaves here.
                if (!belowFloor && state is not null)
                {
                    row.Vertical = state.Vertical;
                    row.Horizontal = state.Horizontal;
                    row.HeadwindsFlag = state.HeadwindsFlag;
                }
                else
                {
                    row.IsCalibrating = true;
                }

                result.Teams.Add(row);
            }

            // Headwinds first: those are the ones only this reader can do anything about.
            result.Teams = result.Teams
                .OrderByDescending(t => t.HeadwindsFlag)
                .ThenBy(t => t.TeamName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            result.CrossTeam = CrossTeamConditions(cycles
                .Select(c => (c.BusinessUnitId, c.IssueFileKey, c.Issue, c.Outcome))
                .ToList(), config.CrossTeamActiveTeams, config.CrossTeamFailingTeams);

            return result;
        }

        /// <summary>
        /// The senior manager's own actionable signal: the same issue file active in
        /// several child teams at once, or failing in more than one. A systemic condition
        /// rather than a team one, and the only thing that produces a senior-level prompt.
        /// </summary>
        private static List<EngineCrossTeamConditionDto> CrossTeamConditions(
            List<(uint UnitId, string IssueFileKey, string? Issue, string? Outcome)> cycles,
            int activeThreshold,
            int failingThreshold)
        {
            var conditions = new List<EngineCrossTeamConditionDto>();

            foreach (var group in cycles.GroupBy(c => c.IssueFileKey, StringComparer.Ordinal))
            {
                var activeTeams = group.Where(c => c.Outcome is null).Select(c => c.UnitId).Distinct().Count();
                var failingTeams = group.Where(c => c.Outcome == "failed").Select(c => c.UnitId).Distinct().Count();

                if (activeTeams >= activeThreshold)
                {
                    conditions.Add(new EngineCrossTeamConditionDto
                    {
                        IssueFileKey = group.Key,
                        Issue = group.First().Issue,
                        Trigger = "concentration",
                        ActiveTeams = activeTeams,
                        FailingTeams = failingTeams,
                        Threshold = activeThreshold
                    });
                }
                else if (failingTeams >= failingThreshold)
                {
                    conditions.Add(new EngineCrossTeamConditionDto
                    {
                        IssueFileKey = group.Key,
                        Issue = group.First().Issue,
                        Trigger = "repeated_failure",
                        ActiveTeams = activeTeams,
                        FailingTeams = failingTeams,
                        Threshold = failingThreshold
                    });
                }
            }

            return conditions;
        }

        private static string CycleStatusOf(bool hasActive, string? lastOutcome) =>
            hasActive ? "active" : lastOutcome switch
            {
                "failed" => "failed",
                "closed" => "closing",
                _ => "none"
            };

        private async Task<Dictionary<uint, StateRow>> LatestStatesAsync(List<uint> unitIds, CancellationToken ct)
        {
            var rows = await _context.EngineStateDaily.AsNoTracking()
                .Where(s => unitIds.Contains(s.BusinessUnitId))
                .GroupBy(s => s.BusinessUnitId)
                .Select(g => g.OrderByDescending(s => s.Day).First())
                .Select(s => new StateRow(s.BusinessUnitId, s.Vertical, s.Horizontal, s.HeadwindsFlag))
                .ToListAsync(ct);

            return rows.ToDictionary(r => r.UnitId);
        }

        /// <summary>
        /// The teams beneath a Portfolio Manager: the teams managed by the Team Managers
        /// assigned to them. A RRIDA Admin passes through for support, and anyone else is
        /// refused — this view is not a second route into another manager's teams.
        /// </summary>
        private async Task<List<uint>> ResolveChildTeamsAsync(User actor, CancellationToken ct)
        {
            var roles = await _context.UserRoles.AsNoTracking()
                .Where(r => r.UserId == actor.UserId)
                .Select(r => r.UserGroupId)
                .ToListAsync(ct);

            if (roles.Count == 0 && actor.UserGroupId > 0) roles.Add(actor.UserGroupId);

            var isPortfolioManager = roles.Contains((int)UserGroups.PortfolioManager);
            var isRridaAdmin = roles.Contains((int)UserGroups.RridaAdmin);

            if (!isPortfolioManager && !isRridaAdmin)
                throw new UnauthorizedAccessException("The roll-up is for a manager of managers.");

            var managerIds = await _context.PortfolioManagerAssignments.AsNoTracking()
                .Where(a => a.PortfolioManagerUserId == actor.UserId && !a.IsUnAssigned)
                .Select(a => a.ManagerUserId)
                .ToListAsync(ct);

            if (managerIds.Count == 0) return [];

            return await _context.UserTeams.AsNoTracking()
                .Where(t => managerIds.Contains(t.UserId) && !t.IsUnAssigned)
                .Select(t => t.UnitId)
                .Distinct()
                .ToListAsync(ct);
        }

        private sealed record StateRow(uint UnitId, string Vertical, string Horizontal, bool HeadwindsFlag);
    }
}
