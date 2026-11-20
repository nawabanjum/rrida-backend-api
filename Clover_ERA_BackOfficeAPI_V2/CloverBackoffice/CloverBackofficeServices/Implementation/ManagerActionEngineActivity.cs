using System.Globalization;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// The manager's own actions (api/actions) as part of a team's progress in the engine's
    /// weekly and monthly messages: what was added, completed, dropped or is still open in the
    /// period the message covers.
    ///
    /// Activity, not a measured result. The engine's own moves are read against the condition
    /// they target and close or fail; a manager's action has no condition, so it is reported as
    /// what the manager did with it and never as closed or failed. Nothing here reaches the
    /// engine: its selection, slot, readings and state are unchanged, and a team with no manual
    /// actions gets exactly the payload it always had (the key is left out).
    /// </summary>
    public static class ManagerActionEngineActivity
    {
        public const string WeeklyKey = "manager_actions_this_week";
        public const string MonthlyKey = "manager_actions_this_month";

        /// <summary>Most rows a message carries; the busiest first.</summary>
        public const int MaxRows = 10;

        public const string Completed = "completed";
        public const string Dropped = "dropped";
        public const string AddedStillOpen = "added, still open";
        public const string StillOpen = "still open";

        public sealed record Fact(string Title, DateOnly CreatedOn, string Status, DateOnly? CompletedOn, DateOnly? CancelledOn, DateOnly? DueDate);

        /// <summary>Every action of the team, as dates.</summary>
        public static async Task<List<Fact>> LoadAsync(ApplicationDbContext db, uint unitId, CancellationToken ct = default)
        {
            var rows = await db.ManagerActions.AsNoTracking()
                .Where(a => a.UnitId == unitId && !a.IsDel)
                .Select(a => new { a.Title, a.CreatedAt, a.Status, a.CompletedAt, a.UpdatedAt, a.DueDate })
                .ToListAsync(ct);

            return rows.Select(a => new Fact(
                    a.Title.Trim(),
                    DateOnly.FromDateTime(a.CreatedAt),
                    a.Status,
                    a.CompletedAt is { } done ? DateOnly.FromDateTime(done) : null,
                    // There is no cancelled date; the last change is when it was dropped.
                    a.Status == ManagerActionStatuses.Cancelled ? DateOnly.FromDateTime(a.UpdatedAt) : null,
                    a.DueDate))
                .ToList();
        }

        /// <summary>
        /// The rows for a message covering [from, to], or null when the manager did nothing with
        /// an action in it. An action completed or dropped before the period is not repeated.
        /// </summary>
        public static List<Dictionary<string, object?>>? ForPeriod(IReadOnlyList<Fact> actions, DateOnly from, DateOnly to)
        {
            var rows = new List<(int Order, Fact Action, string Progress, bool Overdue)>();
            foreach (var a in actions)
            {
                if (a.CreatedOn > to) continue;

                if (a.Status == ManagerActionStatuses.Done && a.CompletedOn is { } done && done <= to)
                {
                    if (done >= from) rows.Add((0, a, Completed, false));
                    continue;
                }

                if (a.Status == ManagerActionStatuses.Cancelled && a.CancelledOn is { } dropped && dropped <= to)
                {
                    if (dropped >= from) rows.Add((2, a, Dropped, false));
                    continue;
                }

                // Open at the end of the period (anything finished later was still open then).
                var overdue = a.DueDate is { } due && due < to;
                rows.Add((1, a, a.CreatedOn >= from ? AddedStillOpen : StillOpen, overdue));
            }

            if (rows.Count == 0) return null;

            return rows
                .OrderBy(r => r.Order)
                .ThenBy(r => r.Action.DueDate ?? DateOnly.MaxValue)
                .ThenBy(r => r.Action.Title, StringComparer.Ordinal)
                .Take(MaxRows)
                .Select(r =>
                {
                    // Flat keys and plain values, as the engine's own payload rows.
                    var row = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["action"] = r.Action.Title,
                        ["progress"] = r.Progress,
                    };
                    if (r.Action.DueDate is { } d)
                        row["due"] = d.ToString("d MMMM", CultureInfo.InvariantCulture);
                    if (r.Overdue)
                        row["overdue"] = true;
                    return row;
                })
                .ToList();
        }
    }
}
