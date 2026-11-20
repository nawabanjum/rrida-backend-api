using CloverBackoffice.Infrastructure.Models.TeamHealth;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Turns what the engine decided into the Weekly Flag on the Team Health page.
    ///
    /// The flag asks two questions — what came up, and what to do about it — and the engine
    /// has already answered both: the theme its signal landed on, and the action it selected
    /// and delivered. This writes that across, once per week, and nothing else.
    ///
    /// <b>It never touches a flag it did not write.</b> Rows carry a Source, and this only
    /// ever creates or updates rows marked <c>Engine</c>. A week already holding a DemoSeed
    /// row is left exactly as it is, so the existing Team Health data and its tests are
    /// unaffected. The reading side is untouched: the Team Health API still just reads.
    ///
    /// The text obeys the same rules as everything else the manager sees: the theme, never
    /// the chip; proportions, never counts; no member named or implied.
    /// </summary>
    public sealed class EngineWeeklyFlagWriter : IEngineWeeklyFlagWriter
    {
        private const string EngineSource = "Engine";

        /// <summary>How far back to keep flags in step. A run replays from day one; the page shows recent weeks.</summary>
        private const int MaxWeeks = 26;

        private readonly ApplicationDbContext _context;
        private readonly IRridaEngineContextFactory _engine;
        private readonly ILogger<EngineWeeklyFlagWriter> _logger;

        public EngineWeeklyFlagWriter(
            ApplicationDbContext context,
            IRridaEngineContextFactory engine,
            ILogger<EngineWeeklyFlagWriter> logger)
        {
            _context = context;
            _engine = engine;
            _logger = logger;
        }

        public async Task<int> WriteAsync(uint businessUnitId, CancellationToken ct = default)
        {
            var settings = await _context.EngineTeamSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.BusinessUnitId == businessUnitId, ct);
            if (settings is null) return 0;

            var unit = await _context.BusinessUnits.AsNoTracking()
                .Where(b => b.Id == businessUnitId)
                .Select(b => new { b.ProfileId })
                .FirstOrDefaultAsync(ct);
            if (unit is null) return 0;

            var clock = new EngineTeamClock(settings.StartDate, settings.WorkingWeek, _engine.Calendar);

            var cycles = await _context.EngineCycles.AsNoTracking()
                .Include(c => c.Deliveries)
                .Where(c => c.BusinessUnitId == businessUnitId && c.DeliveredDay > 0)
                .OrderBy(c => c.DeliveredDay).ThenBy(c => c.CycleIndex)
                .ToListAsync(ct);

            // The engine's weekly reading is written on each reading day (D25: the last day of
            // the team's own week, once calibration is over), so its days are the weeks the
            // MVP's flag lists, and the state that day is the one the flag reports.
            var readingDays = await _context.EngineFeedItems.AsNoTracking()
                .Where(f => f.BusinessUnitId == businessUnitId && f.Kind == "weekly")
                .Select(f => f.Day)
                .ToListAsync(ct);
            var verticals = await _context.EngineStateDaily.AsNoTracking()
                .Where(s => s.BusinessUnitId == businessUnitId && readingDays.Contains(s.Day))
                .ToDictionaryAsync(s => s.Day, s => s.Vertical, ct);
            var readings = readingDays.Distinct()
                .Select(d => new StateDay(d, verticals.GetValueOrDefault(d)))
                .ToList();

            var earliest = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7 * MaxWeeks);
            var weeks = WeeksToWrite(clock, cycles, readings, earliest).ToList();

            // Weeks this engine wrote before and does not write now did not happen on this
            // replay: an earlier run's selection, or a Headwinds week from before D21 was
            // applied here. Retired, not left standing for the manager to read.
            var keep = weeks.Select(w => w.Start).ToHashSet();
            var retired = await _context.TeamWeeklyFlags
                .Where(f => f.UnitId == businessUnitId && !f.IsDel && f.Source == EngineSource
                            && f.WeekStartDate >= earliest && !keep.Contains(f.WeekStartDate))
                .ToListAsync(ct);
            foreach (var row in retired)
            {
                row.IsDel = true;
                row.UpdatedAt = DateTime.UtcNow;
            }

            if (weeks.Count == 0 && retired.Count == 0) return 0;

            var written = 0;

            foreach (var week in weeks)
            {
                var existing = await _context.TeamWeeklyFlags
                    .FirstOrDefaultAsync(f => f.UnitId == businessUnitId
                                              && f.WeekStartDate == week.Start
                                              && !f.IsDel, ct);

                // Somebody else's flag for this week — a demo seed, or a hand-written one.
                // Leave it alone: one live row per team per week is the rule, and the engine
                // is not entitled to overwrite what it did not write.
                if (existing is not null && !string.Equals(existing.Source, EngineSource, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug(
                        "[WeeklyFlag] Unit {Unit} week {Week} already has a {Source} flag. Left as it is.",
                        businessUnitId, week.Start, existing.Source ?? "(none)");
                    continue;
                }

                if (existing is null)
                {
                    existing = new TeamWeeklyFlag
                    {
                        ProfileId = unit.ProfileId,
                        UnitId = businessUnitId,
                        WeekStartDate = week.Start,
                        WhatCameUp = week.WhatCameUp,
                        SuggestedAction = week.SuggestedAction,
                        Source = EngineSource,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.TeamWeeklyFlags.Add(existing);
                }

                existing.WeekEndDate = week.End;
                existing.WhatCameUp = Trim(week.WhatCameUp);
                existing.SuggestedAction = Trim(week.SuggestedAction);
                existing.Source = EngineSource;
                existing.CycleId = week.CycleId;
                existing.UpdatedAt = DateTime.UtcNow;
                written += 1;
            }

            if (written > 0 || retired.Count > 0) await _context.SaveChangesAsync(ct);

            _logger.LogInformation("[WeeklyFlag] Unit {Unit}: {Written} engine flags written.", businessUnitId, written);
            return written;
        }

        /// <summary>
        /// One entry per week the MVP's flag lists (server.js weeklyFlag): every week with a
        /// reading, and every week an action was delivered in. A week with an action says
        /// what came up and the action; a reading week where nothing cleared the floor says
        /// so and gives the layer's posture for the state (NO_FLOOR_POSTURE), as the MVP's
        /// card does. Headwinds is a flag, not a state (D19/D21): it filters the tally and
        /// suppresses nothing, so a Headwinds week reads like any other.
        /// </summary>
        private static IEnumerable<WeekFlag> WeeksToWrite(
            EngineTeamClock clock,
            List<Infrastructure.Models.DeterministicLayer.EngineCycle> cycles,
            IReadOnlyList<StateDay> readings,
            DateOnly earliest)
        {
            var byWeek = new Dictionary<DateOnly, WeekFlag>();

            // A delivered action is the strongest thing a week can carry, so it wins the week.
            // Cycles come in delivery order, so of two delivered in one week the later wins.
            foreach (var cycle in cycles)
            {
                var delivery = cycle.Deliveries.OrderByDescending(d => d.Rung).FirstOrDefault();
                var deliveredDay = delivery?.DeliveredDay > 0 ? delivery.DeliveredDay : cycle.DeliveredDay;
                var date = clock.DateOfDay(deliveredDay);
                if (date < earliest) continue;

                var start = MondayOf(date);
                byWeek[start] = new WeekFlag(
                    start,
                    start.AddDays(6),
                    WhatCameUpFor(cycle),
                    SuggestedActionFor(cycle, delivery),
                    cycle.Id);
            }

            // Then the reading weeks where nothing cleared the floor.
            foreach (var reading in readings)
            {
                var date = clock.DateOfDay(reading.Day);
                if (date < earliest) continue;

                var start = MondayOf(date);
                if (byWeek.ContainsKey(start)) continue;

                byWeek[start] = new WeekFlag(
                    start,
                    start.AddDays(6),
                    NothingSettled,
                    PostureFor(reading.Vertical),
                    null);
            }

            return byWeek.Values.OrderBy(w => w.Start);
        }

        /// <summary>
        /// What came up, at theme level and in proportions. Never the chip: chip-level
        /// wording identifies people on a small team.
        /// </summary>
        internal static string WhatCameUpFor(Infrastructure.Models.DeterministicLayer.EngineCycle cycle)
        {
            var theme = string.IsNullOrWhiteSpace(cycle.Issue) ? "A theme in the team's answers" : cycle.Issue;
            var share = string.IsNullOrWhiteSpace(cycle.ShareText) ? null : cycle.ShareText;

            return share is null
                ? $"{theme} came up often enough in the week's answers to become the team's one thing to work on."
                : $"{theme} came up in the week's answers: {share}.";
        }

        /// <summary>
        /// The action exactly as it was delivered, plus the condition the manager checks it
        /// against. Taken from the delivery snapshot rather than the library, so a flag read
        /// next month still names what was actually sent.
        /// </summary>
        internal static string SuggestedActionFor(
            Infrastructure.Models.DeterministicLayer.EngineCycle cycle,
            Infrastructure.Models.DeterministicLayer.EngineCycleDelivery? delivery)
        {
            var action = delivery?.DeliveredText;
            if (string.IsNullOrWhiteSpace(action))
                return "The system selected this area but has no delivered wording for it yet.";

            var parts = new List<string> { action.Trim() };

            if (!string.IsNullOrWhiteSpace(delivery?.DeliveredWorksheetCode))
                parts.Add($"Exercise to run: {delivery.DeliveredWorksheetCode}.");

            if (!string.IsNullOrWhiteSpace(delivery?.DeliveredWorkedIf))
                parts.Add($"You will know it worked if: {delivery.DeliveredWorkedIf.Trim()}");

            if (cycle.Outcome is "failed")
                parts.Add("This one was tried and did not move the signal, so it is worth asking what sits underneath it.");

            return string.Join(" ", parts);
        }

        /// <summary>The MVP's line for a reading in which nothing settled (server.js weeklyFlag).</summary>
        public const string NothingSettled = "Nothing in the reading settled into a pattern this week.";

        /// <summary>
        /// The layer's posture for a state when nothing has cleared the floor, in the engine's
        /// own words (EngineState.NoFloorPosture, the MVP's NO_FLOOR_POSTURE).
        /// </summary>
        internal static string PostureFor(string? vertical)
        {
            var state = string.IsNullOrWhiteSpace(vertical)
                ? string.Empty
                : char.ToUpperInvariant(vertical[0]) + vertical[1..].ToLowerInvariant();
            return Rrida.Engine.EngineState.NoFloorPosture.GetValueOrDefault(state)
                   ?? "Nothing has cleared the floor.";
        }

        /// <summary>ISO weeks, Monday to Sunday, matching the flags already on the page.</summary>
        private static DateOnly MondayOf(DateOnly date)
        {
            var offset = ((int)date.DayOfWeek + 6) % 7;   // Sunday = 0 in .NET, Monday = 0 here
            return date.AddDays(-offset);
        }

        private static string Trim(string text) =>
            text.Length <= TeamWeeklyFlag.TextMaxLength ? text : text[..TeamWeeklyFlag.TextMaxLength];

        private sealed record StateDay(int Day, string? Vertical);

        private sealed record WeekFlag(DateOnly Start, DateOnly End, string WhatCameUp, string SuggestedAction, long? CycleId);
    }
}
