using System.Globalization;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // The Teams table's action columns: Last Action Update, No. of Actions (Last 90 Days) and
    // Action. The actions are the ones GET api/actions lists for the team, read the same way:
    //   MANUAL   ManagerActions rows (not deleted)
    //   ENGINE   engine actions taken up with "I'll do it" (TeamActionService.LoadTimelineActionsAsync,
    //            the same items as the Team Health timeline dots), with the manager's record beside them
    // A suggested engine action nobody took up is not an action of the team's yet, as in api/actions.
    public partial class PortfolioViewService
    {
        internal const int ActionCountDays = 90;

        /// <summary>One action of a team, reduced to what the columns need.</summary>
        internal sealed record ActionFact(
            string ActionId, string Title, string Source, string Status, string? DueDate,
            DateTime CreatedAt, DateTime UpdatedAt);

        private async Task<Dictionary<uint, List<ActionFact>>> LoadActionsAsync(List<uint> unitIds, CancellationToken ct)
        {
            var byTeam = unitIds.Distinct().ToDictionary(id => id, _ => new List<ActionFact>());
            if (byTeam.Count == 0) return byTeam;

            // The manager's own actions.
            var manual = await _context.ManagerActions.AsNoTracking()
                .Where(a => !a.IsDel && unitIds.Contains(a.UnitId))
                .Select(a => new { a.Id, a.UnitId, a.Title, a.Source, a.Status, a.DueDate, a.CreatedAt, a.UpdatedAt, a.CompletedAt })
                .ToListAsync(ct);
            foreach (var a in manual)
            {
                var updated = Latest(a.UpdatedAt, a.CompletedAt ?? DateTime.MinValue, a.CreatedAt);
                byTeam[a.UnitId].Add(new ActionFact(
                    a.Id.ToString(CultureInfo.InvariantCulture), a.Title, a.Source, a.Status,
                    a.DueDate is { } d ? Date(d) : null, Utc(a.CreatedAt), Utc(updated)));
            }

            // The engine actions taken up, only for the teams that took any up.
            var engineUnits = await _context.EngineAcknowledgements.AsNoTracking()
                .Where(k => unitIds.Contains(k.BusinessUnitId))
                .Select(k => k.BusinessUnitId)
                .Distinct()
                .ToListAsync(ct);
            if (engineUnits.Count == 0) return byTeam;

            // The manager's record (edit / close / reopen) moves the action's update time.
            var cycleKeys = await _context.EngineCycles.AsNoTracking()
                .Where(c => engineUnits.Contains(c.BusinessUnitId))
                .Select(c => new { c.Id, c.CycleKey })
                .ToDictionaryAsync(c => c.Id, c => c.CycleKey, ct);
            var states = await _context.TeamActionStates.AsNoTracking()
                .Where(s => engineUnits.Contains(s.UnitId))
                .Select(s => new { s.UnitId, s.CycleKey, s.Rung, s.UpdatedAt, s.ClosedAt })
                .ToListAsync(ct);

            foreach (var unitId in engineUnits)
            {
                var items = await TeamActionService.LoadTimelineActionsAsync(
                    _context, unitId, DateOnly.MinValue, DateOnly.MaxValue, ct);
                foreach (var i in items)
                {
                    var key = cycleKeys.GetValueOrDefault(i.CycleId);
                    var record = states
                        .Where(s => s.UnitId == unitId && s.CycleKey == key && s.Rung == i.Rung)
                        .Select(s => Latest(s.UpdatedAt, s.ClosedAt ?? DateTime.MinValue))
                        .DefaultIfEmpty(DateTime.MinValue)
                        .Max();
                    byTeam[unitId].Add(new ActionFact(
                        ManagerActionService.EngineIdPrefix + i.ActionId.ToString(CultureInfo.InvariantCulture),
                        i.Text, ManagerActionSources.Engine, ManagerActionService.ToActionStatus(i.Status), i.DueDate,
                        Utc(i.AcknowledgedAt), Utc(Latest(i.AcknowledgedAt, record))));
                }
            }

            return byTeam;
        }

        /// <summary>Fills the three columns (and the in-flight count and latest action) from the team's actions.</summary>
        internal static void ApplyActions(PortfolioTeamDto team, IReadOnlyList<ActionFact> actions, DateOnly today)
        {
            // The last 90 calendar days, today included, by the day the action was created / taken up.
            var since = today.AddDays(1 - ActionCountDays);
            team.ActionsCountLast90Days = actions.Count(a => DateOnly.FromDateTime(a.CreatedAt) >= since);

            team.ActionsInFlight = actions.Count(a => ManagerActionStatuses.Active.Contains(a.Status));
            team.Action = team.ActionsInFlight > 0
                ? $"{team.ActionsInFlight.ToString(CultureInfo.InvariantCulture)} in flight"
                : null;

            var latest = actions
                .OrderByDescending(a => a.UpdatedAt)
                .ThenByDescending(a => a.CreatedAt)
                .ThenBy(a => a.ActionId, StringComparer.Ordinal)
                .FirstOrDefault();
            team.LastActionUpdate = latest?.UpdatedAt;
            team.LatestAction = latest == null ? null : new PortfolioActionDto
            {
                ActionId = latest.ActionId,
                Title = latest.Title,
                Source = latest.Source,
                Status = latest.Status,
                DueDate = latest.DueDate,
                UpdatedAt = latest.UpdatedAt
            };
        }

        private static DateTime Latest(params DateTime[] values) => values.Max();

        private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}
