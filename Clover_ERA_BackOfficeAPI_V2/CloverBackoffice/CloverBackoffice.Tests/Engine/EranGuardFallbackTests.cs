using System.Runtime.CompilerServices;
using System.Text.Json;
using CloverBackoffice.Services.Implementation;
using Rrida.Engine;
using Rrida.State;
using Xunit;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>
    /// The fixed card for every message the eight authored scenarios produce (the brief's
    /// acceptance 4): built from the payload alone, the same every time, and passing the guard.
    /// </summary>
    public class EranGuardFallbackTests
    {
        private static readonly string Root = FindEngineRoot();
        private static readonly Lazy<EngineContext> Ctx = new(() => EngineContext.FromKit(Path.Combine(Root, "kit", "data")));
        private static readonly Lazy<StateConfig> State = new(() => StateConfig.Load(Path.Combine(Root, "kit", "data", "thresholds.json")));
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

        public static IEnumerable<object[]> Scenarios() =>
            Directory.GetFiles(Path.Combine(FindEngineRoot(), "scenarios"), "team-*.json")
                .Select(f => new object[] { Path.GetFileNameWithoutExtension(f) });

        [Theory]
        [MemberData(nameof(Scenarios))]
        public async Task Every_scenario_message_has_a_card_that_passes_the_guard(string scenarioId)
        {
            var file = await EngineScenarioFile.LoadAsync(Path.Combine(Root, "scenarios", scenarioId + ".json"), CancellationToken.None);
            var team = Run(file);
            Assert.NotEmpty(team.Feed);

            foreach (var item in team.Feed)
            {
                var payload = JsonSerializer.Serialize(item.Payload, Json);
                var card = EranGuardFallback.ForFeedItem(item.Kind, item.Situation, payload);

                Assert.Equal(card, EranGuardFallback.ForFeedItem(item.Kind, item.Situation, payload));
                var verdict = EranGuard.CheckFeedItem(item.Kind, item.Situation, payload, card);
                Assert.True(verdict.Passed, $"{scenarioId} day {item.Day} {item.Kind}/{item.Situation}: {verdict.RulesText}\n{card}");
                Assert.DoesNotContain("MA-", card, StringComparison.Ordinal);

                if (item.Kind == "daily")
                {
                    Assert.True(EranGuard.TryDailyParts(card, out var parts));
                    var hasAction = item.Payload.TryGetValue("action_text", out var a) && a is not null
                                    && item.Situation is "action_first_day" or "action_reminder" or "action_confirmed";
                    Assert.Equal(hasAction, parts["one_thing"].Length > 0);
                    Assert.Equal("", parts["say_this"]);
                }
            }
        }

        [Fact]
        public void A_substituted_worksheet_wins_over_the_action_text()
        {
            const string payload = """{"situation":"action_first_day","team_state":"Stall","action_text":"Run the career-path session.","worksheet":"OP1 Ambition Roadmap","worksheet_substitution":"The action text names Career Path Transparency.","how_you_will_know":"the team can say what it was preparing them for"}""";
            Assert.True(EranGuard.TryDailyParts(EranGuardFallback.ForFeedItem("daily", "action_first_day", payload), out var parts));
            Assert.Equal("Run OP1 Ambition Roadmap with the team.", parts["one_thing"]);
            Assert.Equal("The team can say what it was preparing them for.", parts["what_tells_you"]);
            Assert.Equal("Your team has stalled this week.", parts["headline"]);
        }

        [Fact]
        public void A_quiet_day_card_never_carries_an_action_or_the_internal_posture()
        {
            const string payload = """{"situation":"nothing_cleared","team_state":"Drift","posture":"Priority intervention. Draw from the Yellow set.","nearest_condition":"unclear priorities is the one being picked up"}""";
            Assert.True(EranGuard.TryDailyParts(EranGuardFallback.ForFeedItem("daily", "nothing_cleared", payload), out var parts));
            Assert.Equal("", parts["one_thing"] + parts["say_this"] + parts["what_tells_you"]);
            Assert.DoesNotContain("Yellow set", parts["body"]);
            Assert.Contains("unclear priorities", parts["body"]);
        }

        private static Team Run(EngineScenarioFile file)
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
                Confirmations = file.Confirmations.Select(c => (c.CycleIndex, c.Rung, c.DayOffset, c.WorkedIfMet)).ToList(),
                DefaultConfirmation = file.DefaultConfirmation is { } d ? (d.DayOffset, d.WorkedIfMet) : null,
                AutoConfirm = true
            };
            var sim = new Simulation(Ctx.Value, [config], new Resolver(State.Value, new BufferingStateSink("test")),
                horizon: file.Horizon > 0 ? file.Horizon : 124);
            Simulation.StepResult step;
            do step = sim.Step(); while (!step.AtHorizon);
            return sim.AllTeams()[0];
        }

        private static string FindEngineRoot([CallerFilePath] string source = "")
        {
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(source)!); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "external", "RRIDA-Engine");
                if (Directory.Exists(Path.Combine(candidate, "kit", "data"))) return candidate;
            }
            throw new DirectoryNotFoundException("external/RRIDA-Engine was not found above the test output.");
        }
    }
}
