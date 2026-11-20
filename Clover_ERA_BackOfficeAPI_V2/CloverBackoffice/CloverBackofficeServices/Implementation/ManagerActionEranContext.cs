using System.Globalization;
using System.Text;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// What Eran is told about the actions a manager created themselves (api/actions): the open
    /// ones (OPEN, IN_PROGRESS) of the team, most pressing first. One loader for the daily
    /// messages, the chat and the suggested questions, so they never describe them differently.
    ///
    /// They are the manager's own decisions, never a move the product selected: Eran may weigh
    /// them and follow up on them, and is told not to make one "the one thing" of a daily message.
    /// </summary>
    public static class ManagerActionEranContext
    {
        /// <summary>The most Eran is given at once.</summary>
        public const int MaxForEran = 5;

        /// <param name="FollowsSuggestion">ERAN's wording of the suggestion the manager linked it to, if any.</param>
        public sealed record Item(long Id, string Title, string? Description, DateOnly? DueDate, string Status,
            string? FollowsSuggestion = null)
        {
            public bool IsOverdue(DateOnly today) => DueDate is { } due && due < today;
        }

        /// <summary>The team's open actions: overdue first, then by due date, then newest.</summary>
        public static async Task<List<Item>> OpenAsync(
            ApplicationDbContext db, uint unitId, DateOnly today, CancellationToken ct = default)
        {
            var rows = await OpenQuery(db, unitId).ToListAsync(ct);

            return rows
                .OrderBy(r => r.DueDate is { } d && d < today ? 0 : 1)
                .ThenBy(r => r.DueDate ?? DateOnly.MaxValue)
                .ThenByDescending(r => r.CreatedAt)
                .Take(MaxForEran)
                .Select(r => new Item(r.Id, r.Title, r.Description, r.DueDate, r.Status, r.FollowsSuggestion))
                .ToList();
        }

        public sealed record Row(long Id, string Title, string? Description, DateOnly? DueDate, string Status, DateTime CreatedAt,
            string? FollowsSuggestion);

        /// <summary>The database query behind <see cref="OpenAsync"/>, public so its SQL can be checked.</summary>
        public static IQueryable<Row> OpenQuery(ApplicationDbContext db, uint unitId)
        {
            var active = ManagerActionStatuses.Active;
            return db.ManagerActions.AsNoTracking()
                .Where(a => !a.IsDel && a.UnitId == unitId && active.Contains(a.Status))
                .Select(a => new Row(a.Id, a.Title, a.Description, a.DueDate, a.Status, a.CreatedAt,
                    a.SuggestionChoice == ManagerActionSuggestionChoices.Suggestion ? a.EngineSuggestionText : null));
        }

        /// <summary>
        /// The value of the "managers_own_actions" key in the context block of a daily message
        /// and of the chat on an engine team. Null when there is nothing open.
        /// </summary>
        public static string? ContextValue(IReadOnlyList<Item> items, DateOnly today)
        {
            if (items.Count == 0) return null;
            return "actions the manager set themselves, not moves this product selected: "
                + string.Join("; ", items.Select(i => Describe(i, today)))
                + ". Mention one only where it bears on what you are saying, in the manager's own words. "
                + "Never make one the one thing to do, never put one in say_this or what_tells_you, and never "
                + "present one as the move.";
        }

        /// <summary>The chat block for a team that is not on the engine. Null when there is nothing open.</summary>
        public static string? ChatBlock(IReadOnlyList<Item> items, DateOnly today)
        {
            if (items.Count == 0) return null;

            var sb = new StringBuilder()
                .Append("## THE MANAGER'S OWN ACTIONS\n\n")
                .Append("Actions this manager decided on and recorded themselves, still open. They are the manager's ")
                .Append("own commitments, not recommendations from a report. Take them into account: connect advice to ")
                .Append("them where it fits, ask how one is going when it is relevant, and suggest a sensible next step or ")
                .Append("follow-up, especially for one that is overdue. Do not invent actions that are not listed, and do ")
                .Append("not recite the list back unprompted.\n\n");
            foreach (var i in items)
            {
                sb.Append("- ").Append(Describe(i, today));
                if (!string.IsNullOrWhiteSpace(i.Description))
                    sb.Append(". Details: ").Append(i.Description.Trim());
                sb.Append('\n');
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>The wording Eran's chat guard may echo: the titles and descriptions as written.</summary>
        public static string GuardText(IReadOnlyList<Item> items) =>
            string.Join("\n", items.SelectMany(i => new[] { i.Title, i.Description ?? "", i.FollowsSuggestion ?? "" }));

        /// <summary>The one to suggest a follow-up question about: overdue first, then the nearest due.</summary>
        public static Item? MostPressing(IReadOnlyList<Item> items) => items.Count == 0 ? null : items[0];

        private static string Describe(Item i, DateOnly today)
        {
            var parts = new List<string>();
            if (i.DueDate is { } due)
                parts.Add(i.IsOverdue(today)
                    ? $"was due {due.ToString("d MMMM", CultureInfo.InvariantCulture)}, overdue"
                    : $"due {due.ToString("d MMMM", CultureInfo.InvariantCulture)}");
            parts.Add(i.Status == ManagerActionStatuses.InProgress ? "in progress" : "not started");
            // Still the manager's own action, never the move, even when it follows a suggestion.
            if (!string.IsNullOrWhiteSpace(i.FollowsSuggestion))
                parts.Add($"follows ERAN's earlier suggestion \"{i.FollowsSuggestion.Trim()}\"");
            return $"\"{i.Title.Trim()}\" ({string.Join(", ", parts)})";
        }
    }
}
