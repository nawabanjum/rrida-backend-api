using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using Rrida.Engine;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// How many people a team has, for anything counted "against the team".
    ///
    /// The team size the manager set (BusinessUnits.TeamSize, dated in TeamSizeHistory) is the
    /// headcount. App enrolments count devices, not people: a reinstall, a new phone or a second
    /// browser enrols the same person again, so a team of 6 can show 13 enrolled. Members
    /// enrolled are used only where no team size is set.
    ///
    /// Two readings, on purpose:
    ///   * <see cref="EngineSizeOn"/>   the engine's: the dated history only, else enrolled. The
    ///     engine is replayed from day one, so a day before the size was recorded keeps the
    ///     count it was decided on. Eran's floor sentence reads the same, so it matches the engine.
    ///   * <see cref="DisplaySizeOn"/>  the screens': the dated history, else the size set now,
    ///     else enrolled. Nothing is stored from it, so it uses the team size straight away.
    /// </summary>
    public static class TeamHeadcount
    {
        public sealed record SizeRow(DateOnly EffectiveFrom, int? TeamSize);

        /// <summary>Every dated size for these teams, oldest first.</summary>
        public static async Task<Dictionary<uint, List<SizeRow>>> LoadHistoryAsync(
            ApplicationDbContext context, IReadOnlyCollection<uint> unitIds, CancellationToken ct)
        {
            if (unitIds.Count == 0) return new Dictionary<uint, List<SizeRow>>();
            var rows = await context.TeamSizeHistory.AsNoTracking()
                .Where(h => unitIds.Contains(h.BusinessUnitId))
                .Select(h => new { h.BusinessUnitId, h.EffectiveFrom, h.TeamSize })
                .ToListAsync(ct);
            return rows
                .GroupBy(r => r.BusinessUnitId)
                .ToDictionary(g => g.Key, g => g.OrderBy(r => r.EffectiveFrom)
                    .Select(r => new SizeRow(r.EffectiveFrom, r.TeamSize)).ToList());
        }

        public static async Task<List<SizeRow>> LoadHistoryAsync(ApplicationDbContext context, uint unitId, CancellationToken ct) =>
            (await LoadHistoryAsync(context, new[] { unitId }, ct)).GetValueOrDefault(unitId) ?? new List<SizeRow>();

        /// <summary>The engine's headcount on a date: the size in force, else the members enrolled.</summary>
        public static int EngineSizeOn(IReadOnlyList<SizeRow> history, DateOnly day, int enrolled) =>
            InForce(history, day) is { } row && row.TeamSize is > 0 and var size ? size : enrolled;

        /// <summary>The screens' headcount on a date: the size in force, else the size set now, else enrolled.</summary>
        public static int DisplaySizeOn(IReadOnlyList<SizeRow> history, int? currentTeamSize, DateOnly day, int enrolled)
        {
            var row = InForce(history, day);
            if (row is not null) return row.TeamSize is > 0 and var size ? size : enrolled;
            return currentTeamSize is > 0 and var current ? current : enrolled;
        }

        /// <summary>
        /// The history as the engine reads it: periods on the team's own day numbers. A size
        /// that took effect before the engine started applies from day one.
        /// </summary>
        public static List<HeadcountPeriod> ToEnginePeriods(IReadOnlyList<SizeRow> history, EngineTeamClock clock) =>
            history
                .Select(r => new HeadcountPeriod(Math.Max(1, clock.DayOf(r.EffectiveFrom)), r.TeamSize is > 0 ? r.TeamSize : null))
                .GroupBy(p => p.FromDay)
                .Select(g => g.Last())   // two sizes landing on day one: the later one is in force
                .OrderBy(p => p.FromDay)
                .ToList();

        private static SizeRow? InForce(IReadOnlyList<SizeRow> history, DateOnly day)
        {
            SizeRow? inForce = null;
            foreach (var row in history)
            {
                if (row.EffectiveFrom > day) break;
                inForce = row;
            }
            return inForce;
        }
    }
}
