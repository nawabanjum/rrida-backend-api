using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CloverBackoffice.Services.Implementation;
using Rrida.Engine;
using Rrida.State;
using Xunit;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>
    /// Upstream's golden fixture for the between_cycles day (kit/fixtures/engine/situations/
    /// between-cycles.json at 0729864, copied to Fixtures/): the one situation the eight scenarios
    /// no longer reach. Replayed through the vendored engine, every day's Eran payload must equal
    /// the reference's, and the fallback card for that day must say what became of the last move.
    /// </summary>
    public class EngineSituationFixtureTests
    {
        private static readonly string Root = FindRepoRoot();
        private static readonly string EngineRoot = Path.Combine(Root, "external", "RRIDA-Engine");

        [Fact]
        public async Task The_between_cycles_day_matches_the_reference_payload_for_payload()
        {
            var fixture = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(Root, "CloverBackoffice.Tests", "Engine", "Fixtures", "between-cycles.json")))!;
            var scenarioPath = Path.Combine(Path.GetTempPath(), $"between-cycles-{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(scenarioPath, fixture["scenario"]!.ToJsonString());
            EngineScenarioFile file;
            try { file = await EngineScenarioFile.LoadAsync(scenarioPath, CancellationToken.None); }
            finally { File.Delete(scenarioPath); }

            var team = Run(file);
            var expectedOn = fixture["expectedOn"]!.GetValue<int>();

            var expected = fixture["days"]!.AsArray()
                .SelectMany(d => d!["eranPayloads"]!.AsArray())
                .Select(p => (Day: p!["day"]!.GetValue<int>(), Kind: p["kind"]!.GetValue<string>(), Payload: p["payload"]!))
                .ToList();
            var actual = team.Feed
                .Where(f => f.Day <= fixture["days"]!.AsArray().Count)
                .Select(f => (f.Day, f.Kind, Payload: JsonNode.Parse(JsonSerializer.Serialize(f.Payload))!))
                .ToList();

            Assert.Equal(expected.Count, actual.Count);
            for (var i = 0; i < expected.Count; i += 1)
            {
                Assert.Equal((expected[i].Day, expected[i].Kind), (actual[i].Day, actual[i].Kind));
                Assert.True(JsonNode.DeepEquals(expected[i].Payload, actual[i].Payload),
                    $"day {expected[i].Day} {expected[i].Kind}\nexpected {expected[i].Payload.ToJsonString()}\nactual   {actual[i].Payload.ToJsonString()}");
            }

            var between = team.Feed.Single(f => f.Day == expectedOn && f.Kind == "daily");
            Assert.Equal("between_cycles", between.Situation);
            Assert.False(between.Payload.ContainsKey("action_text"));
            Assert.True(between.Payload.ContainsKey("what_happened_last"));
            // MVP 90fe06d (upstream SituationFixtureTests): in the team's own working days, never
            // the run-day index.
            var what = between.Payload["what_happened_last"]!.ToString()!;
            Assert.Matches(@"\d+ working days ago|1 working day ago|today", what);
            Assert.DoesNotMatch(@"day \d+", what);

            var payload = JsonSerializer.Serialize(between.Payload);
            var card = EranGuardFallback.ForFeedItem("daily", between.Situation, payload);
            Assert.True(EranGuard.CheckFeedItem("daily", between.Situation, payload, card).Passed);
            Assert.True(EranGuard.TryDailyParts(card, out var parts));
            Assert.Equal("", parts["one_thing"]);
            Assert.Contains(between.Payload["what_happened_last"]!.ToString()!.Split(',')[0], parts["body"], StringComparison.OrdinalIgnoreCase);
        }

        private static Team Run(EngineScenarioFile file)
        {
            var ctx = EngineContext.FromKit(Path.Combine(EngineRoot, "kit", "data"));
            var state = StateConfig.Load(Path.Combine(EngineRoot, "kit", "data", "thresholds.json"));
            var config = new TeamConfig
            {
                Id = file.Id,
                Name = file.Name,
                Roster = file.Roster.Select(m => new Member { Id = m.Id, Name = m.Name }).ToList(),
                Script = file.Script.ToDictionary(
                    kv => int.Parse(kv.Key),
                    kv => kv.Value.Select(e => new ScriptEntry(e.Member, e.Track, e.Smiley, e.Chips, e.Text)).ToList()),
                WorkingWeek = file.WorkingWeek,
                Confirmations = file.Confirmations.Select(c => (c.CycleIndex, c.Rung, c.DayOffset, c.WorkedIfMet)).ToList(),
                DefaultConfirmation = file.DefaultConfirmation is { } d ? (d.DayOffset, d.WorkedIfMet) : null,
                AutoConfirm = true
            };
            var sim = new Simulation(ctx, [config], new Resolver(state, new BufferingStateSink("test")),
                horizon: file.Horizon > 0 ? file.Horizon : 30);
            Simulation.StepResult step;
            do step = sim.Step(); while (!step.AtHorizon);
            return sim.AllTeams()[0];
        }

        private static string FindRepoRoot([CallerFilePath] string source = "")
        {
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(source)!); dir is not null; dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, "external", "RRIDA-Engine", "kit", "data"))) return dir.FullName;
            throw new DirectoryNotFoundException("repository root not found");
        }
    }
}
