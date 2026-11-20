using System.Security.Cryptography;
using System.Text;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rrida.Engine;
using Rrida.State;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Gives a team ninety days of scripted answers, then runs the engine over them.
    ///
    /// This is how the layer is tested end to end without waiting a quarter for real
    /// answers to accumulate. The scripts are the engine's own demo scenarios, so the arcs
    /// are known: team-platform closes five cycles and fails two, and a run that does
    /// anything else is telling us something.
    ///
    /// It writes only FACTS — members, membership, answers, chips, and the manager's
    /// confirmations. Everything else is derived by the replay, exactly as it would be for
    /// a real team, which is what makes this a test of the real path rather than of itself.
    /// </summary>
    public sealed class EngineScenarioSeeder : IEngineScenarioSeeder
    {
        private const string ScenarioFolder = "CloverEra/Data/rrida-scenarios";

        private readonly ApplicationDbContext _context;
        private readonly IRridaEngineContextFactory _engine;
        private readonly IEngineReplayRunner _runner;
        private readonly ILogger<EngineScenarioSeeder> _logger;

        public EngineScenarioSeeder(
            ApplicationDbContext context,
            IRridaEngineContextFactory engine,
            IEngineReplayRunner runner,
            ILogger<EngineScenarioSeeder> logger)
        {
            _context = context;
            _engine = engine;
            _runner = runner;
            _logger = logger;
        }

        public async Task<List<EngineScenarioDto>> ListAsync(CancellationToken ct = default)
        {
            var folder = Path.Combine(AppContext.BaseDirectory, ScenarioFolder);
            if (!Directory.Exists(folder)) return [];

            var list = new List<EngineScenarioDto>();
            foreach (var path in Directory.EnumerateFiles(folder, "*.json").OrderBy(p => p))
            {
                if (Path.GetFileName(path).Equals("index.json", StringComparison.OrdinalIgnoreCase)) continue;

                var scenario = await EngineScenarioFile.LoadAsync(path, ct);
                list.Add(new EngineScenarioDto
                {
                    Id = scenario.Id,
                    Name = scenario.Name,
                    Industry = scenario.Industry,
                    Roster = scenario.Roster.Count,
                    WorkingWeek = scenario.WorkingWeek,
                    ScriptedDays = scenario.Script.Count,
                    Horizon = scenario.Horizon,
                    Arc = scenario.Arc
                });
            }

            return list;
        }

        /// <summary>
        /// D34. The engine team's eight scenarios are scripted with the chips of the six-question
        /// demo rotation, and the reference itself can only run them on that rotation (its
        /// newSimulation pins ROTATIONS.demo). Every team here is asked on the ninety, where the
        /// engine refuses those chips, so a seeded team would replay as empty: refused rather than
        /// written. The scenarios stay vendored for the engine's own parity tests.
        /// </summary>
        public Task<EngineSeedResultDto> SeedAsync(EngineSeedRequestDto dto, int actorUserId, CancellationToken ct = default) =>
            Task.FromException<EngineSeedResultDto>(new InvalidOperationException(
                "The engine team's scenarios are written for the six-question demo rotation and cannot run on the "
                + "90-question rotation every team now uses (D34). Use POST api/rrida/engine/import-workbook or the "
                + "dummy check-in job to give a test team answers on the 90."));

        public async Task<EngineSeedResultDto> SeedScenarioAsync(
            uint businessUnitId,
            EngineScenarioFile scenario,
            DateOnly startDate,
            bool autoConfirm,
            bool reset,
            int actorUserId,
            CancellationToken ct = default)
        {
            var unit = await _context.BusinessUnits.AsNoTracking()
                .Where(b => b.Id == businessUnitId && b.IsDel != true)
                .Select(b => new { b.Id, b.ProfileId })
                .FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException($"Team {businessUnitId} was not found.");

            var existing = await _context.EngineAnswers
                .AnyAsync(a => a.BusinessUnitId == businessUnitId, ct);

            if (existing && !reset)
                throw new InvalidOperationException(
                    "This team already holds engine data. Send reset: true to clear it and seed again.");

            if (reset)
                await ClearAsync(businessUnitId, ct);

            var workingWeek = string.IsNullOrWhiteSpace(scenario.WorkingWeek) ? "mon-fri" : scenario.WorkingWeek;
            var horizonDays = scenario.Horizon > 0 ? scenario.Horizon : 124;

            var clock = new EngineTeamClock(startDate, workingWeek, _engine.Calendar);
            await UpsertSettingsAsync(businessUnitId, startDate, workingWeek, actorUserId, ct);

            var members = await SeedMembersAsync(unit.ProfileId, businessUnitId, scenario, startDate, ct);
            var (answers, chips) = await SeedAnswersAsync(businessUnitId, scenario, clock, horizonDays, members, ct);

            var through = clock.DateOfDay(horizonDays);
            var run = await _runner.RunAsync(businessUnitId, through, ct);

            var confirmations = 0;
            if (autoConfirm)
            {
                confirmations = await ConfirmAndReplayAsync(businessUnitId, scenario, clock, horizonDays, through, ct);
                if (confirmations > 0) run = await _runner.RunAsync(businessUnitId, through, ct);
            }

            var outcomes = await _context.EngineCycles.AsNoTracking()
                .Where(c => c.BusinessUnitId == businessUnitId)
                .GroupBy(c => c.Outcome ?? "running")
                .Select(g => new { Outcome = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            _logger.LogInformation(
                "[Seed] Unit {Unit} seeded from {Scenario}: {Answers} answers, {Confirmations} confirmations, "
                + "{Cycles} cycles.", businessUnitId, scenario.Id, answers, confirmations, outcomes.Sum(o => o.Count));

            return new EngineSeedResultDto
            {
                BusinessUnitId = businessUnitId,
                ScenarioId = scenario.Id,
                ScenarioName = scenario.Name,
                StartDate = startDate,
                ThroughDate = through,
                WorkingWeek = workingWeek,
                MembersSeeded = members.Count,
                AnswersSeeded = answers,
                ChipsSeeded = chips,
                ConfirmationsSeeded = confirmations,
                Run = run,
                CycleOutcomes = outcomes.ToDictionary(o => o.Outcome, o => o.Count, StringComparer.Ordinal)
            };
        }

        // ── the pieces ───────────────────────────────────────────────────────

        private async Task UpsertSettingsAsync(
            uint unitId, DateOnly startDate, string workingWeek, int actorUserId, CancellationToken ct)
        {
            var settings = await _context.EngineTeamSettings
                .FirstOrDefaultAsync(s => s.BusinessUnitId == unitId, ct);

            if (settings is null)
            {
                settings = new EngineTeamSettings
                {
                    BusinessUnitId = unitId,
                    StartDate = startDate,
                    WorkingWeek = workingWeek,
                    IsEnabled = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.EngineTeamSettings.Add(settings);
            }
            else
            {
                settings.StartDate = startDate;
                settings.WorkingWeek = workingWeek;
                settings.IsEnabled = true;
                settings.UpdatedAt = DateTime.UtcNow;
                settings.UpdatedBy = actorUserId;
            }

            await _context.SaveChangesAsync(ct);
        }

        private async Task<Dictionary<string, EngineMember>> SeedMembersAsync(
            uint profileId, uint unitId, EngineScenarioFile scenario, DateOnly startDate, CancellationToken ct)
        {
            var members = new Dictionary<string, EngineMember>(StringComparer.Ordinal);

            foreach (var rosterMember in scenario.Roster)
            {
                // Deterministic, so re-seeding the same scenario on the same team reuses the
                // same people rather than inventing a second set.
                var token = $"seed:{scenario.Id}:{unitId}:{rosterMember.Id}";
                var hash = Hash(token);

                var member = await _context.EngineMembers.FirstOrDefaultAsync(m => m.TokenHash == hash, ct);
                if (member is null)
                {
                    member = new EngineMember
                    {
                        ProfileId = profileId,
                        TokenHash = hash,
                        // Prefixed so a seeded member is never mistaken for a real one.
                        EngineMemberKey = $"seed-{unitId}-{rosterMember.Id}",
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.EngineMembers.Add(member);
                }

                members[rosterMember.Id] = member;
            }

            await _context.SaveChangesAsync(ct);

            foreach (var member in members.Values)
            {
                var open = await _context.EngineTeamMemberships
                    .AnyAsync(m => m.MemberId == member.Id && m.BusinessUnitId == unitId && m.EffectiveTo == null, ct);

                if (!open)
                {
                    _context.EngineTeamMemberships.Add(new EngineTeamMembership
                    {
                        MemberId = member.Id,
                        BusinessUnitId = unitId,
                        EffectiveFrom = startDate,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            await _context.SaveChangesAsync(ct);
            return members;
        }

        private async Task<(int Answers, int Chips)> SeedAnswersAsync(
            uint unitId,
            EngineScenarioFile scenario,
            EngineTeamClock clock,
            int horizonDays,
            Dictionary<string, EngineMember> members,
            CancellationToken ct)
        {
            var rotation = _engine.Context.Rotation;
            var answers = 0;
            var chips = 0;
            var askedIndex = 0;
            var savedAt = 0;

            for (var day = 1; day <= horizonDays; day += 1)
            {
                var date = clock.DateOfDay(day);
                if (!clock.IsAskedOn(date)) continue;

                askedIndex += 1;
                if (!scenario.Script.TryGetValue(askedIndex.ToString(), out var entries)) continue;

                foreach (var entry in entries)
                {
                    if (!members.TryGetValue(entry.Member, out var member)) continue;

                    var track = string.IsNullOrWhiteSpace(entry.Track) ? "individual" : entry.Track.Trim().ToLowerInvariant();

                    // The rotation is keyed on the asked-day index, never the date. This is
                    // the crossing the reference implementation got wrong five times.
                    var questionKey = rotation.QuestionIdForDay(askedIndex, track);
                    if (questionKey is null) continue;

                    var answer = new EngineAnswer
                    {
                        BusinessUnitId = unitId,
                        MemberId = member.Id,
                        AnswerDate = date,
                        Day = day,
                        AskedDayIndex = askedIndex,
                        Track = track,
                        QuestionKey = questionKey.Value,
                        Level = entry.Smiley.Trim().ToLowerInvariant(),
                        FreeText = entry.Chips.Count == 0 ? entry.Text ?? "(seeded, no chip fitted)" : entry.Text,
                        CreatedAt = DateTime.UtcNow
                    };

                    var position = 1;
                    foreach (var chip in entry.Chips.Distinct(StringComparer.Ordinal).Take(3))
                    {
                        answer.Chips.Add(new EngineAnswerChip { ChipKey = chip, Position = position++ });
                        chips += 1;
                    }

                    _context.EngineAnswers.Add(answer);
                    answers += 1;
                }

                // Saved in batches: ninety days of a nine-person team is a lot of rows to
                // hold in one change tracker, and a failure then names the day it reached.
                if (answers - savedAt >= 200)
                {
                    await _context.SaveChangesAsync(ct);
                    savedAt = answers;
                }
            }

            await _context.SaveChangesAsync(ct);
            return (answers, chips);
        }

        /// <summary>
        /// Plays the scenario's manager exactly as the MVP does (simulation.js
        /// applyScriptedConfirmation): the action in the slot is confirmed once its offset in
        /// asked days has passed, per-cycle rules first, then the team's standing behaviour.
        ///
        /// When the manager confirms decides what the engine does next — under D29 the slot
        /// frees at confirmation — so the days cannot be worked out from a run without them.
        /// The scenario is run once in memory with the scripted manager on, which is the
        /// MVP's own run, and the confirmations it made are stored as the manager's taps on
        /// the days it made them. The replay then reproduces that run from the stored facts.
        /// </summary>
        private async Task<int> ConfirmAndReplayAsync(
            uint unitId,
            EngineScenarioFile scenario,
            EngineTeamClock clock,
            int horizonDays,
            DateOnly through,
            CancellationToken ct)
        {
            var config = new TeamConfig
            {
                Id = unitId.ToString(),
                Name = scenario.Name,
                Roster = scenario.Roster.Select(m => new Member { Id = m.Id, Name = m.Name }).ToList(),
                Script = scenario.Script.ToDictionary(kv => int.Parse(kv.Key),
                    kv => kv.Value.Select(e => new ScriptEntry(e.Member, e.Track, e.Smiley, e.Chips, e.Text)).ToList()),
                WorkingWeek = clock.WorkingWeek,
                StartDate = clock.StartDate.ToString("yyyy-MM-dd"),
                Confirmations = scenario.Confirmations.Select(c => (c.CycleIndex, c.Rung, c.DayOffset, c.WorkedIfMet)).ToList(),
                DefaultConfirmation = scenario.DefaultConfirmation is { } d ? (d.DayOffset, d.WorkedIfMet) : null,
                AutoConfirm = true
            };

            var sim = new Simulation(_engine.Context, [config],
                new Resolver(_engine.StateConfig, new BufferingStateSink("seed")), horizon: horizonDays);
            Simulation.StepResult step;
            do step = sim.Step(); while (!step.AtHorizon);
            var team = sim.AllTeams()[0];

            var cycles = team.CycleHistory
                .Concat(team.ActiveCycle is null ? [] : new[] { team.ActiveCycle })
                .Concat(team.AwaitingRead).Concat(team.PendingRungTwo)
                .GroupBy(c => c.Id, StringComparer.Ordinal).Select(g => g.Last());

            var taps = new List<(string Cycle, int Rung, int Day, bool WorkedIfMet)>();
            foreach (var cycle in cycles)
            {
                if (cycle.RungOneRecord is { ConfirmedDay: { } rungOneDay } rungOne)
                    taps.Add((cycle.Id, 1, rungOneDay, rungOne.WorkedIfMet ?? true));
                if (cycle.ConfirmedDay is { } day)
                    taps.Add((cycle.Id, cycle.Rung, day, cycle.WorkedIfMet ?? true));
            }

            foreach (var tap in taps)
            {
                var date = clock.DateOfDay(tap.Day);
                _context.EngineConfirmations.Add(new EngineConfirmation
                {
                    BusinessUnitId = unitId,
                    CycleKey = tap.Cycle,
                    Rung = tap.Rung,
                    ConfirmedDate = date,
                    Day = tap.Day,
                    AskedDayIndex = clock.AskedDayIndexOf(date),
                    WorkedIfMet = tap.WorkedIfMet,
                    Source = "seed",
                    CreatedAt = DateTime.UtcNow
                });
            }

            if (taps.Count > 0)
            {
                await _context.SaveChangesAsync(ct);
                await _runner.RunAsync(unitId, through, ct);
            }

            return taps.Count;
        }

        /// <summary>
        /// Removes this team's engine data, facts and results alike, so a re-seed starts
        /// clean. Scoped to the one unit, and only reached when the caller sends reset.
        /// </summary>
        private async Task ClearAsync(uint unitId, CancellationToken ct)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);

            // Cycle ids restart at C1 on every run, so an "I'll do it" left from the old history
            // would mark a new move as taken up.
            await _context.EngineAcknowledgements.Where(a => a.BusinessUnitId == unitId).ExecuteDeleteAsync(ct);

            // Deliveries and answer chips go with their parents by cascade.
            await _context.EngineCycles.Where(c => c.BusinessUnitId == unitId).ExecuteDeleteAsync(ct);
            await _context.EngineAnswers.Where(a => a.BusinessUnitId == unitId).ExecuteDeleteAsync(ct);
            await _context.EngineConfirmations.Where(c => c.BusinessUnitId == unitId).ExecuteDeleteAsync(ct);
            await _context.EngineDeclines.Where(d => d.BusinessUnitId == unitId).ExecuteDeleteAsync(ct);
            await _context.EngineStateDaily.Where(s => s.BusinessUnitId == unitId).ExecuteDeleteAsync(ct);
            await _context.EngineFeedItems.Where(f => f.BusinessUnitId == unitId).ExecuteDeleteAsync(ct);
            await _context.EngineTeamNotes.Where(n => n.BusinessUnitId == unitId).ExecuteDeleteAsync(ct);
            await _context.EngineLlmCalls.Where(l => l.BusinessUnitId == unitId).ExecuteDeleteAsync(ct);
            await _context.EngineTeamMemberships.Where(m => m.BusinessUnitId == unitId).ExecuteDeleteAsync(ct);

            // Members are left: they carry no team of their own, and a real member enrolled
            // on this team must not be deleted by a seeding call.
            await transaction.CommitAsync(ct);
        }

        private static string Hash(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }
}
