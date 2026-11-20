using System.Runtime.CompilerServices;
using System.Text.Json;
using CloverBackoffice.Services.Implementation;
using Rrida.Engine;
using Rrida.State;
using Xunit;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>
    /// The replay rebuilds a team from stored facts, with the manager's real confirmations
    /// fed back in as taps. These tests hold it to the MVP: a run whose manager confirmed by
    /// script and a run fed the same confirmations as taps must produce the same messages,
    /// the same cycles and the same states, day for day.
    /// </summary>
    public class EngineManagerTapReplayTests
    {
        private static readonly string Root = FindEngineRoot();
        private static readonly Lazy<EngineContext> Ctx = new(() => EngineContext.FromKit(Path.Combine(Root, "kit", "data")));
        private static readonly Lazy<StateConfig> State = new(() => StateConfig.Load(Path.Combine(Root, "kit", "data", "thresholds.json")));

        public static IEnumerable<object[]> Scenarios() =>
            Directory.GetFiles(Path.Combine(FindEngineRoot(), "scenarios"), "team-*.json")
                .Select(f => new object[] { Path.GetFileNameWithoutExtension(f) });

        [Theory]
        [MemberData(nameof(Scenarios))]
        public async Task Taps_replayed_from_facts_reproduce_the_scripted_run(string scenarioId)
        {
            var file = await EngineScenarioFile.LoadAsync(Path.Combine(Root, "scenarios", scenarioId + ".json"), CancellationToken.None);

            // 1. The MVP's own run: the scripted manager confirms.
            var scripted = NewSimulation(file, autoConfirm: true);
            var taps = new List<Simulation.ManagerConfirmation>();
            RunToHorizon(scripted, step =>
            {
                // The rung is read off the event: a reading later the same day can move the
                // cycle on to rung two, so the cycle's rung after the step is not the one confirmed.
                foreach (var e in step.Events.Where(e => e.Type == "confirmed"))
                {
                    var rung = int.Parse(e.Label![(e.Label!.LastIndexOf(' ') + 1)..]);
                    taps.Add(new Simulation.ManagerConfirmation(e.TeamId, e.CycleId!, rung, e.Day, e.WorkedIfMet ?? true));
                }
            });

            // 2. The same team rebuilt from facts: no script, the confirmations as taps.
            var replayed = NewSimulation(file, autoConfirm: false);
            replayed.ManagerConfirmations.AddRange(taps);
            RunToHorizon(replayed, _ => { });

            var a = scripted.AllTeams()[0];
            var b = replayed.AllTeams()[0];

            Assert.True(a.Feed.Count > 0, "the scripted run produced no messages");
            Assert.Equal(Describe(a.Feed), Describe(b.Feed));
            Assert.Equal(
                CyclesOf(a).Select(c => (c.Id, c.Rung, c.DeliveredDay, c.ConfirmedDay, c.Outcome)),
                CyclesOf(b).Select(c => (c.Id, c.Rung, c.DeliveredDay, c.ConfirmedDay, c.Outcome)));
            Assert.Equal(a.State, b.State);
        }

        [Fact]
        public async Task A_tap_given_on_the_delivery_day_is_not_lost()
        {
            var file = await EngineScenarioFile.LoadAsync(Path.Combine(Root, "scenarios", "team-platform.json"), CancellationToken.None);

            // Find the first delivery, then confirm it on the day it was delivered.
            var probe = NewSimulation(file, autoConfirm: false);
            Cycle? first = null;
            RunToHorizon(probe, _ => first ??= probe.AllTeams()[0].ActiveCycle);
            Assert.NotNull(first);

            var sim = NewSimulation(file, autoConfirm: false);
            sim.ManagerConfirmations.Add(new Simulation.ManagerConfirmation(
                sim.AllTeams()[0].Id, first!.Id, 1, first.DeliveredDay, true));
            RunToHorizon(sim, _ => { });

            // Unconfirmed, the cycle never leaves the slot; with the tap it is confirmed under
            // the day it was given and moves on to its reading.
            var untouched = CyclesOf(probe.AllTeams()[0]).First(c => c.Id == first.Id);
            Assert.False(untouched.Confirmed);

            var tapped = CyclesOf(sim.AllTeams()[0]).First(c => c.Id == first.Id);
            Assert.True(tapped.Confirmed || tapped.Outcome is not null);
            Assert.Equal(first.DeliveredDay, tapped.ConfirmedDay ?? first.DeliveredDay);

            // The slot freed, so the engine went on to further cycles it could not reach before.
            Assert.True(CyclesOf(sim.AllTeams()[0]).Count > CyclesOf(probe.AllTeams()[0]).Count);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static Simulation NewSimulation(EngineScenarioFile file, bool autoConfirm)
        {
            var config = new TeamConfig
            {
                Id = file.Id,
                Name = file.Name,
                Roster = file.Roster.Select(m => new Member { Id = m.Id, Name = m.Name }).ToList(),
                Script = file.Script.ToDictionary(
                    kv => int.Parse(kv.Key),
                    kv => kv.Value.Select(e => new ScriptEntry(e.Member, e.Track, e.Smiley, e.Chips, e.Text)).ToList()),
                WorkingWeek = file.WorkingWeek,
                Confirmations = autoConfirm
                    ? file.Confirmations.Select(c => (c.CycleIndex, c.Rung, c.DayOffset, c.WorkedIfMet)).ToList()
                    : [],
                DefaultConfirmation = autoConfirm && file.DefaultConfirmation is { } d ? (d.DayOffset, d.WorkedIfMet) : null,
                AutoConfirm = autoConfirm
            };
            return new Simulation(Ctx.Value, [config], new Resolver(State.Value, new BufferingStateSink("test")),
                horizon: file.Horizon > 0 ? file.Horizon : 124);
        }

        private static void RunToHorizon(Simulation sim, Action<Simulation.StepResult> onStep)
        {
            Simulation.StepResult step;
            do
            {
                step = sim.Step();
                onStep(step);
            }
            while (!step.AtHorizon);
        }

        private static List<Cycle> CyclesOf(Team team) =>
            team.CycleHistory
                .Concat(team.ActiveCycle is null ? [] : new[] { team.ActiveCycle })
                .Concat(team.AwaitingRead)
                .Concat(team.PendingRungTwo)
                .GroupBy(c => c.Id)
                .Select(g => g.Last())
                .OrderBy(c => c.Id, StringComparer.Ordinal)
                .ToList();

        private static List<string> Describe(IEnumerable<FeedItem> feed) =>
            feed.Select(f => $"{f.Day}|{f.Kind}|{f.CycleId}|{f.Rung}|{f.Situation}|{f.NoteType}|{JsonSerializer.Serialize(f.Payload)}").ToList();

        private static string FindEngineRoot([CallerFilePath] string source = "")
        {
            // From this source file rather than the test output, which may be built elsewhere.
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(source)!); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "external", "RRIDA-Engine");
                if (Directory.Exists(Path.Combine(candidate, "kit", "data"))) return candidate;
            }
            throw new DirectoryNotFoundException("external/RRIDA-Engine was not found above the test output.");
        }
    }
}
