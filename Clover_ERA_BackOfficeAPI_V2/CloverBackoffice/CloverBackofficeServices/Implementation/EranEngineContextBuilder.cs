using System.Text.Json;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Rrida.Engine;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// The context block every Eran call about a team carries: who is being spoken to, and
    /// about what (MVP src/eran/context.js). One builder for the feed and for the chat, as in
    /// the MVP, so the two never describe the same team differently.
    /// </summary>
    public sealed class EranEngineContextBuilder
    {
        private const string DirectionsFile = "CloverEra/Data/rrida-demo-data/coaching-directions.json";
        private static readonly Lazy<Dictionary<string, string>> DirectionLabels = new(LoadDirectionLabels);

        private readonly ApplicationDbContext _context;
        private readonly IRridaEngineContextFactory _engine;

        public EranEngineContextBuilder(ApplicationDbContext context, IRridaEngineContextFactory engine)
        {
            _context = context;
            _engine = engine;
        }

        /// <summary>
        /// Who Eran is speaking to, as of today, in the MVP's keys. Keys the main project
        /// does not hold (industry, tenure with the team) are left out, as the MVP leaves out
        /// any key that has no value.
        /// </summary>
        public async Task<List<(string Key, string Value)>> BuildAsync(uint businessUnitId, CancellationToken ct = default)
        {
            var context = new List<(string Key, string Value)>();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var settings = await _context.EngineTeamSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.BusinessUnitId == businessUnitId, ct);
            var day = settings is null ? 1 : Math.Max(1, today.DayNumber - settings.StartDate.DayNumber + 1);

            var manager = await _context.UserTeams.AsNoTracking()
                .Where(ut => ut.UnitId == businessUnitId && !ut.IsUnAssigned)
                .Join(_context.Users.Where(u => !u.IsDel), ut => ut.UserId, u => u.UserId,
                    (ut, u) => new { ut.Id, ut.CreatedDate, u.Name })
                .OrderByDescending(x => x.CreatedDate).ThenByDescending(x => x.Id)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(ct);
            var teamName = await _context.BusinessUnits.AsNoTracking()
                .Where(b => b.Id == businessUnitId).Select(b => b.Name).FirstOrDefaultAsync(ct);
            var enrolled = await _context.EngineTeamMemberships.AsNoTracking()
                .CountAsync(m => m.BusinessUnitId == businessUnitId && m.EffectiveFrom <= today
                                 && (m.EffectiveTo == null || m.EffectiveTo >= today), ct);
            // The headcount the engine's floor counts against today (the team size the manager
            // set, else members enrolled), so Eran's team size and floor match the engine's.
            var teamSize = TeamHeadcount.EngineSizeOn(
                await TeamHeadcount.LoadHistoryAsync(_context, businessUnitId, ct), today, enrolled);

            var teamState = await TeamStateAsync(businessUnitId, ct) ?? "calibrating";

            // The team's active cycle, as the engine holds it: the most recently delivered one
            // still open. A confirmed cycle stays active (the "action_confirmed" day) until a new
            // selection takes its place, so it is not filtered out here.
            var active = await _context.EngineCycles.AsNoTracking()
                .Where(c => c.BusinessUnitId == businessUnitId && c.Outcome == null
                            && c.DeliveredDay != null && c.DeclinedDay == null)
                .Select(c => new
                {
                    c.IssueFileKey,
                    Delivery = _context.EngineCycleDeliveries
                        .Where(d => d.CycleId == c.Id)
                        .OrderByDescending(d => d.Rung)
                        .Select(d => new { d.Rung, d.DeliveredDay, d.ConfirmedDay })
                        .FirstOrDefault()
                })
                .Where(c => c.Delivery != null)
                .OrderByDescending(c => c.Delivery!.DeliveredDay)
                .FirstOrDefaultAsync(ct);

            Add(context, "manager_name", manager);
            Add(context, "manager_level", "Team manager");
            Add(context, "team_name", teamName);
            Add(context, "team_size", teamSize.ToString());
            Add(context, "team_state", teamState);
            Add(context, "cycle_day", active is null ? "0" : (day - active.Delivery!.DeliveredDay + 1).ToString());
            // Plain language. Anything Eran is handed in system vocabulary tends to come back
            // out in system vocabulary.
            Add(context, "cycle_position", active is null
                ? "nothing is running"
                : active.Delivery!.Rung == 2
                    ? "the second move on this theme, a change to how something works"
                    : "the first move on this theme, a conversation");
            if (active?.IssueFileKey is { } file && DirectionLabels.Value.TryGetValue(file, out var label))
                Add(context, "what_this_is_about", label);

            // Not an MVP key: the actions the manager created themselves (api/actions), so the
            // daily messages and the chat can weigh them. Absent when nothing is open.
            var ownActions = await ManagerActionEranContext.OpenAsync(_context, businessUnitId, today, ct);
            Add(context, "managers_own_actions", ManagerActionEranContext.ContextValue(ownActions, today));

            // Spec 5.1.5. On a team too small for the ask route, an answer held in reserve.
            var floor = Tally.ComputeFloor(teamSize, _engine.Context.Config);
            if (teamSize > 0 && !floor.AskRouteReachable)
            {
                Add(context, "why_no_one_to_one",
                    $"On a team of {teamSize} the floor of {floor.Required} is the smallest signal that can "
                    + $"be acted on, which is {floor.Required} of {teamSize}. That is already above half, so "
                    + "the one-to-one route cannot come up here: anything strong enough to act on has "
                    + "been said by more than half the team, and the honest move is to respond rather "
                    + "than to go and ask one person. Four is the smallest team where a one-to-one can "
                    + "be selected.");
            }

            return context;
        }

        private static void Add(List<(string, string)> context, string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) context.Add((key, value));
        }

        public static string Render(List<(string Key, string Value)> context) =>
            "Context you are given:\n" + string.Join("\n", context.Select(c => $"  {c.Key}: {c.Value}"));


        /// <summary>
        /// The engine's own state string (Cruise, Drift, Stall, or calibrating), as the newest
        /// daily message carries it. Null when the team has no messages yet.
        /// </summary>
        public async Task<string?> TeamStateAsync(uint businessUnitId, CancellationToken ct = default)
        {
            var latestDaily = await _context.EngineFeedItems.AsNoTracking()
                .Where(f => f.BusinessUnitId == businessUnitId && f.Kind == "daily")
                .OrderByDescending(f => f.Day)
                .Select(f => f.PayloadJson)
                .FirstOrDefaultAsync(ct);
            return StringField(latestDaily, "team_state");
        }

        private static string? StringField(string? json, string name)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        /// <summary>
        /// Issue file to its coaching direction's name. Where a file sits in several, the first
        /// in document order is the label (MVP directions.labelFor). A missing file means no
        /// label, which the MVP also tolerates: the key is simply not given.
        /// </summary>
        private static Dictionary<string, string> LoadDirectionLabels()
        {
            var path = Path.Combine(AppContext.BaseDirectory, DirectionsFile);
            if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.Ordinal);

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var names = doc.RootElement.GetProperty("directions").EnumerateArray()
                .ToDictionary(d => d.GetProperty("id").GetString()!, d => d.GetProperty("name").GetString()!, StringComparer.Ordinal);
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in doc.RootElement.GetProperty("byIssueFile").EnumerateObject())
            {
                var first = file.Value.EnumerateArray().Select(id => id.GetString()).FirstOrDefault(id => id is not null && names.ContainsKey(id));
                if (first is not null) labels[file.Name] = names[first];
            }
            return labels;
        }
    }
}
