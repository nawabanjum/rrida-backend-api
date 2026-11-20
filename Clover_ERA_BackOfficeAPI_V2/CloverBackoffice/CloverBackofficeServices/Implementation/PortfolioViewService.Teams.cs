using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using Microsoft.EntityFrameworkCore;
using StateRow = CloverBackoffice.Services.Implementation.TeamStateService.StateRow;

namespace CloverBackoffice.Services.Implementation
{
    public partial class PortfolioViewService
    {
        // The selected manager's teams, or every manager's, each with its reading.
        private async Task<List<PortfolioTeamDto>> LoadTeamsAsync(Scope scope, CancellationToken ct)
        {
            var managerIds = scope.Selected != null
                ? new List<int> { scope.Selected.UserId }
                : scope.Managers.Select(m => m.UserId).ToList();
            if (managerIds.Count == 0) return new List<PortfolioTeamDto>();

            var assignments = await _context.UserTeams.AsNoTracking()
                .Where(t => managerIds.Contains(t.UserId) && !t.IsUnAssigned)
                .Select(t => new { t.Id, t.UnitId, t.UserId })
                .ToListAsync(ct);
            var unitIds = assignments.Select(a => a.UnitId).Distinct().ToList();

            var units = await _context.BusinessUnits.AsNoTracking()
                .Where(b => unitIds.Contains(b.Id) && b.IsDel != true && b.ProfileId == scope.ProfileId)
                .Select(b => new { b.Id, b.Name, b.TeamSize })
                .ToListAsync(ct);

            var names = scope.Managers.ToDictionary(m => m.UserId, m => m.Name);
            var health = await LoadHealthAsync(units.Select(u => u.Id).ToList(), ct);
            var sizeHistory = await TeamHeadcount.LoadHistoryAsync(_context, units.Select(u => u.Id).ToList(), ct);
            var actions = await LoadActionsAsync(units.Select(u => u.Id).ToList(), ct);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var teams = new List<PortfolioTeamDto>();
            foreach (var unit in units)
            {
                // A team held by two of these managers at once belongs to the latest assignment.
                var holder = assignments.Where(a => a.UnitId == unit.Id).OrderByDescending(a => a.Id).First().UserId;
                var facts = health.GetValueOrDefault(unit.Id) ?? TeamFacts.None;

                // The team size the manager set is the member count (an enrolment is a device,
                // not a person); members enrolled only where no size is set.
                var headcount = TeamHeadcount.DisplaySizeOn(
                    sizeHistory.GetValueOrDefault(unit.Id) ?? [], unit.TeamSize, today, facts.Enrolled);

                var team = new PortfolioTeamDto
                {
                    TeamId = unit.Id,
                    TeamName = unit.Name,
                    TeamManagerId = holder,
                    TeamManagerName = names.GetValueOrDefault(holder, string.Empty),
                    MemberCount = headcount
                };
                // The visibility floor protects anonymity, so it counts the fewer of the two:
                // neither a duplicate enrolment nor an out-of-date size can reveal a team's state.
                ApplyHealth(team, facts, Math.Min(facts.Enrolled, headcount),
                    _engine.Context.Config.RollUpVisibilityFloor, today);
                ApplyActions(team, actions.GetValueOrDefault(unit.Id) ?? [], today);
                teams.Add(team);
            }

            return teams
                .OrderBy(t => Rank(t.HealthState))
                .ThenBy(t => t.TeamName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.TeamId)
                .ToList();
        }

        private sealed record TeamFacts(DateOnly? StartDate, int Enrolled, List<StateRow> Rows)
        {
            public static readonly TeamFacts None = new(null, 0, new List<StateRow>());
        }

        private async Task<Dictionary<uint, TeamFacts>> LoadHealthAsync(List<uint> unitIds, CancellationToken ct)
        {
            if (unitIds.Count == 0) return new Dictionary<uint, TeamFacts>();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var starts = (await _context.EngineTeamSettings.AsNoTracking()
                    .Where(s => unitIds.Contains(s.BusinessUnitId))
                    .Select(s => new { s.BusinessUnitId, s.StartDate })
                    .ToListAsync(ct))
                .GroupBy(s => s.BusinessUnitId)
                .ToDictionary(g => g.Key, g => g.First().StartDate);

            // Enrolled today, as the roll-up counts it: the floor counts people, not answers.
            var enrolled = (await _context.EngineTeamMemberships.AsNoTracking()
                    .Where(m => unitIds.Contains(m.BusinessUnitId)
                                && m.EffectiveFrom <= today
                                && (m.EffectiveTo == null || m.EffectiveTo >= today))
                    .Select(m => new { m.BusinessUnitId, m.MemberId })
                    .Distinct()
                    .ToListAsync(ct))
                .GroupBy(m => m.BusinessUnitId)
                .ToDictionary(g => g.Key, g => g.Count());

            var rows = (await _context.EngineStateDaily.AsNoTracking()
                    .Where(s => unitIds.Contains(s.BusinessUnitId))
                    .Select(s => new { s.BusinessUnitId, s.Day, s.Vertical, s.HeadwindsFlag })
                    .ToListAsync(ct))
                .GroupBy(s => s.BusinessUnitId)
                .ToDictionary(g => g.Key, g => g.Select(s => new StateRow(s.Day, s.Vertical, s.HeadwindsFlag)).ToList());

            return unitIds.ToDictionary(id => id, id => new TeamFacts(
                starts.TryGetValue(id, out var start) ? start : null,
                enrolled.GetValueOrDefault(id),
                rows.GetValueOrDefault(id) ?? new List<StateRow>()));
        }

        // State, weeks and trend, by the Team State card's rules.
        private static void ApplyHealth(PortfolioTeamDto team, TeamFacts facts, int visibleCount, int visibilityFloor, DateOnly today)
        {
            team.IsCalibrating = true;
            if (facts.StartDate is not DateOnly start) return;

            var todayDay = today.DayNumber - start.DayNumber + 1;
            var resolved = facts.Rows.Where(r => r.Day <= todayDay).OrderBy(r => r.Day).ToList();
            if (resolved.Count <= TeamStateService.CalibrationDays) return;

            // Below the floor the state is withheld, not just hidden: it reads as calibrating.
            if (visibleCount < visibilityFloor) return;

            var latest = resolved[^1];
            var counted = resolved.Skip(TeamStateService.CalibrationDays).ToList();
            var state = HealthOf(latest);

            team.IsCalibrating = false;
            team.HealthState = state;
            team.Vertical = TeamStateService.StateOf(latest.Vertical);
            team.HeadwindsFlag = latest.HeadwindsFlag;
            team.AsOfDate = Date(start.AddDays(latest.Day - 1));

            // Weeks in the current state: back through the counted days while it holds.
            var runStart = latest.Day;
            for (var i = counted.Count - 1; i >= 0 && HealthOf(counted[i]) == state; i--)
                runStart = counted[i].Day;
            team.WeeksCount = (latest.Day - runStart) / 7 + 1;

            // One reading a week for the last quarter, ending today's reading.
            for (var w = TrendWeeks - 1; w >= 0; w--)
            {
                var day = latest.Day - 7 * w;
                var reading = TeamStateService.ReadingAsOf(counted, day);
                if (reading.DaysCounted == 0) continue;

                var onDay = counted.Last(r => r.Day <= day);
                team.Trend.Add(new PortfolioTrendPointDto
                {
                    AsOfDate = Date(start.AddDays(day - 1)),
                    State = HealthOf(onDay),
                    Score = reading.Cruise,
                    Cruise = reading.Cruise,
                    Drift = reading.Drift,
                    Stall = reading.Stall,
                    Headwinds = reading.Headwinds,
                    DaysCounted = reading.DaysCounted
                });
            }
        }

        // The Portfolio table's state: Headwinds when the flag is raised, else the vertical.
        private static string HealthOf(StateRow row) =>
            row.HeadwindsFlag ? TeamStates.Headwinds : TeamStateService.StateOf(row.Vertical);

        private static int Rank(string? state) => state switch
        {
            TeamStates.Headwinds => 0,
            TeamStates.Stall => 1,
            TeamStates.Drift => 2,
            TeamStates.Cruise => 3,
            _ => 4
        };
    }
}
