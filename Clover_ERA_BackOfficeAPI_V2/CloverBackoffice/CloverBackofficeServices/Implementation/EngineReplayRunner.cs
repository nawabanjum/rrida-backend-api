using System.Security.Cryptography;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rrida.Engine;
using Rrida.State;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Runs one team through the engine, from day one to today.
    ///
    /// <b>Why a replay rather than a resumable job.</b> The resolver keeps its accumulators
    /// in memory and has no snapshot: both are recurrences, so a day fed twice is
    /// double-counted and a day skipped is gone. There is no way to restore it halfway.
    /// The engine is deterministic, so the honest alternative is to keep the FACTS — the
    /// answers, the confirmations, the declines — and rebuild the state from them every
    /// time. Re-running is then free of consequence, which is also what makes the daily job
    /// idempotent.
    ///
    /// The day loop is the engine's own Simulation, the same one the MVP runs, with the
    /// script coming from our tables instead of a fixture and the manager's confirmations
    /// and declines replayed as taps. That is what writes the daily and weekly messages.
    /// </summary>
    public sealed class EngineReplayRunner : IEngineReplayRunner
    {
        private readonly ApplicationDbContext _context;
        private readonly IRridaEngineContextFactory _engine;
        private readonly IEngineResultStore _store;
        private readonly IEngineWeeklyFlagWriter _weeklyFlags;
        private readonly IEngineNarrationQueue _narration;
        private readonly ILogger<EngineReplayRunner> _logger;

        public EngineReplayRunner(
            ApplicationDbContext context,
            IRridaEngineContextFactory engine,
            IEngineResultStore store,
            IEngineWeeklyFlagWriter weeklyFlags,
            IEngineNarrationQueue narration,
            ILogger<EngineReplayRunner> logger)
        {
            _context = context;
            _engine = engine;
            _store = store;
            _weeklyFlags = weeklyFlags;
            _narration = narration;
            _logger = logger;
        }

        public async Task<EngineRunResultDto> RunAsync(uint businessUnitId, DateOnly through, CancellationToken ct = default)
        {
            var started = DateTime.UtcNow;

            var settings = await _context.EngineTeamSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.BusinessUnitId == businessUnitId, ct)
                ?? throw new InvalidOperationException($"Unit {businessUnitId} has no engine settings.");

            // D34. Every team is asked on the production rotation: all ninety questions, one of each
            // track per asked day, looping.
            var ctx = _engine.Context;

            var clock = new EngineTeamClock(settings.StartDate, settings.WorkingWeek, _engine.Calendar);
            var horizon = clock.DayOf(through);
            if (horizon < 1)
                throw new InvalidOperationException("The run ends before this team's engine clock starts.");

            var config = await BuildTeamConfigAsync(businessUnitId, settings.StartDate, settings.WorkingWeek, clock, ct);
            var confirmations = await LoadConfirmationsAsync(businessUnitId, ct);
            var declines = await LoadDeclinesAsync(businessUnitId, ct);

            var sink = new BufferingStateSink(ConfigId());
            var resolver = new Resolver(_engine.StateConfig, sink);

            // The engine's own day loop, not a copy of it. The MVP is the reference for
            // what a manager sees, and its Simulation is what writes the daily, weekly,
            // outcome, note, escalation and monthly messages: a loop that only borrows its
            // order produces the state and the cycles but none of the messages. It also
            // ticks every calendar day, as the MVP does, not only the days the team works.
            var sim = new Simulation(ctx, [config], resolver, horizon, config.StartDate);
            foreach (var ((cycle, rung, day), workedIfMet) in confirmations)
                sim.ManagerConfirmations.Add(new Simulation.ManagerConfirmation(config.Id, cycle, rung, day, workedIfMet));
            foreach (var ((cycle, day), reason) in declines)
                sim.ManagerDeclines.Add(new Simulation.ManagerDecline(config.Id, cycle, day, reason));

            var deliveries = 0;
            var resolutions = 0;
            Simulation.StepResult step;
            do
            {
                step = sim.Step();
                deliveries += step.Events.Count(e => e.Type == "action_active");
                resolutions += step.Events.Count(e => e.Type == "signal_read");
            }
            while (!step.AtHorizon);

            var team = sim.TeamOf(config.Id)!;

            var written = await _store.SaveRunAsync(businessUnitId, ctx, team, sink, horizon, ct);

            // Publish what the run decided to the Team Health page. Kept out of the run's
            // own transaction on purpose: a flag failing to write must not roll back the
            // engine's results, which are the record. It writes only rows it owns, so a
            // seeded flag is never disturbed.
            try
            {
                await _weeklyFlags.WriteAsync(businessUnitId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Weekly flags could not be written for unit {Unit}. The run itself stands.",
                    businessUnitId);
            }

            // Eran phrases the new messages after the run, as the MVP does after each step.
            // Queued rather than awaited: the run is the record, and nothing should wait on a model.
            _narration.Enqueue(businessUnitId);

            var result = new EngineRunResultDto
            {
                BusinessUnitId = businessUnitId,
                StartDate = settings.StartDate,
                Through = through,
                Days = horizon,
                AskedDays = team.AskedDaysTo(horizon),
                Enrolled = team.Roster.Count,
                StateRecords = written,
                Deliveries = deliveries,
                Resolutions = resolutions,
                State = team.State,
                ActiveCycleKey = team.ActiveCycle?.Id,
                ConfigId = sink.ConfigId,
                ElapsedMs = (int)(DateTime.UtcNow - started).TotalMilliseconds
            };

            _logger.LogInformation(
                "Engine replay for unit {Unit}: {AskedDays} asked days over {Days} dates, {Deliveries} deliveries, "
                + "state {State}, {Elapsed} ms.",
                businessUnitId, result.AskedDays, horizon, deliveries, team.State ?? "calibrating", result.ElapsedMs);

            return result;
        }

        // ── building the run ─────────────────────────────────────────────────

        private async Task<TeamConfig> BuildTeamConfigAsync(
            uint businessUnitId, DateOnly startDate, string workingWeek, EngineTeamClock clock, CancellationToken ct)
        {
            var unit = await _context.BusinessUnits.AsNoTracking()
                .Where(b => b.Id == businessUnitId)
                .Select(b => new { b.Name })
                .FirstOrDefaultAsync(ct);

            // Everyone who has EVER been on the team, with the days they joined and left.
            // The floor is a percentage of those enrolled on the day, so a roster of only
            // today's members would compute last month's floor against the wrong number.
            var memberships = await _context.EngineTeamMemberships.AsNoTracking()
                .Where(m => m.BusinessUnitId == businessUnitId)
                .Select(m => new { m.MemberId, m.Member!.EngineMemberKey, m.EffectiveFrom, m.EffectiveTo })
                .ToListAsync(ct);

            var roster = memberships
                .GroupBy(m => m.EngineMemberKey, StringComparer.Ordinal)
                .Select(g =>
                {
                    var first = g.Min(x => x.EffectiveFrom);
                    var lastEnd = g.Any(x => x.EffectiveTo is null) ? (DateOnly?)null : g.Max(x => x.EffectiveTo!.Value);
                    return new Member
                    {
                        Id = g.Key,
                        JoinedDay = Math.Max(1, clock.DayOf(first)),
                        // EnrolledOn keeps a member while LeftDay is greater than the day,
                        // so the day after their last is the exclusive end.
                        LeftDay = lastEnd is { } end ? clock.DayOf(end) + 1 : null
                    };
                })
                .OrderBy(m => m.Id, StringComparer.Ordinal)
                .ToList();

            // The script is keyed by ASKED-DAY index, because the rotation is too: its nth
            // entry meets the nth question.
            var answers = await _context.EngineAnswers.AsNoTracking()
                .Where(a => a.BusinessUnitId == businessUnitId)
                .OrderBy(a => a.AskedDayIndex).ThenBy(a => a.Id)
                .Select(a => new
                {
                    a.AskedDayIndex,
                    a.Track,
                    a.QuestionKey,
                    a.Level,
                    a.FreeText,
                    MemberKey = a.Member!.EngineMemberKey,
                    Chips = a.Chips.OrderBy(c => c.Position).Select(c => c.ChipKey).ToList()
                })
                .ToListAsync(ct);

            // An answer belongs to the question its asked day put to the team. One given to a
            // different question (asked on the demo's six-day rotation before D34) is not part of
            // this team's history on the ninety, and the engine would refuse the whole replay over
            // it, so it is left out. Nothing is deleted.
            var rotation = _engine.Context.Rotation;
            var stale = answers.Count(a => rotation.QuestionIdForDay(a.AskedDayIndex, a.Track) != a.QuestionKey);
            if (stale > 0)
            {
                _logger.LogWarning(
                    "Unit {Unit}: {Stale} of {Total} stored answers were given to a question the 90-question rotation does not ask on their day (recorded before D34). Left out of the replay.",
                    businessUnitId, stale, answers.Count);
                answers = answers.Where(a => rotation.QuestionIdForDay(a.AskedDayIndex, a.Track) == a.QuestionKey).ToList();
            }

            // The team size the manager set is the headcount the floor and the share count
            // against, day by day as it was in force (enrolments count devices, not people).
            // A day before any recorded size counts the members enrolled, as it always did.
            var headcount = TeamHeadcount.ToEnginePeriods(
                await TeamHeadcount.LoadHistoryAsync(_context, businessUnitId, ct), clock);

            var script = new Dictionary<int, List<ScriptEntry>>();
            foreach (var a in answers)
            {
                if (!script.TryGetValue(a.AskedDayIndex, out var rows))
                {
                    rows = [];
                    script[a.AskedDayIndex] = rows;
                }

                rows.Add(new ScriptEntry(a.MemberKey, a.Track, a.Level, a.Chips, a.FreeText));
            }

            return new TeamConfig
            {
                Id = businessUnitId.ToString(),
                Name = unit?.Name ?? $"unit {businessUnitId}",
                Roster = roster,
                Headcount = headcount,
                Script = script,
                WorkingWeek = workingWeek,
                StartDate = startDate.ToString("yyyy-MM-dd"),

                // Confirmations are real events read from our own tables, so the engine's
                // scripted auto-confirmation must be off. With it on, every cycle would
                // confirm itself and the record would say the manager acted when they did not.
                AutoConfirm = false
            };
        }

        private async Task<Dictionary<(string Cycle, int Rung, int Day), bool>> LoadConfirmationsAsync(
            uint businessUnitId, CancellationToken ct)
        {
            var rows = await _context.EngineConfirmations.AsNoTracking()
                .Where(c => c.BusinessUnitId == businessUnitId)
                .Select(c => new { c.CycleKey, c.Rung, c.Day, c.WorkedIfMet })
                .ToListAsync(ct);

            return rows.ToDictionary(r => (r.CycleKey, r.Rung, r.Day), r => r.WorkedIfMet);
        }

        private async Task<Dictionary<(string Cycle, int Day), string?>> LoadDeclinesAsync(
            uint businessUnitId, CancellationToken ct)
        {
            var rows = await _context.EngineDeclines.AsNoTracking()
                .Where(d => d.BusinessUnitId == businessUnitId)
                .Select(d => new { d.CycleKey, d.Day, d.Reason })
                .ToListAsync(ct);

            return rows.ToDictionary(r => (r.CycleKey, r.Day), r => r.Reason);
        }

        /// <summary>
        /// Identifies the thresholds a record was produced under. Taken from the bytes of
        /// the kit's own thresholds.json rather than computed from a config object, because
        /// hashing an object means agreeing about key order and float printing, which is not
        /// worth agreeing about. A state figure without this is not interpretable, and these
        /// thresholds are provisional.
        /// </summary>
        private string ConfigId()
        {
            var path = Path.Combine(_engine.KitDataPath, "thresholds.json");
            var hash = SHA256.HashData(File.ReadAllBytes(path));
            return Convert.ToHexString(hash)[..12].ToLowerInvariant();
        }
    }
}
