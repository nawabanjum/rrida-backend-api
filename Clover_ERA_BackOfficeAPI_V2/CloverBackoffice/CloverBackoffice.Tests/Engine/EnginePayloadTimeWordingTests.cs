using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using CloverBackoffice.Services.Implementation;
using Rrida.Engine;
using Rrida.State;
using Xunit;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>
    /// C# ports of the reference's two checks from 90fe06d and 59de560 / 0729864
    /// (test/scenarios.test.mjs), over the eight authored scenarios run together as the reference
    /// runs them: no Eran payload carries a run-day index, the measurement is called the
    /// check-back, and nothing says when it lands.
    /// </summary>
    public class EnginePayloadTimeWordingTests
    {
        private static readonly string Root = FindEngineRoot();
        private static readonly Lazy<Task<List<Team>>> Teams = new(RunAsync);

        [Fact]
        public async Task No_run_day_index_reaches_an_Eran_payload()
        {
            var offenders = new List<string>();
            foreach (var team in await Teams.Value)
                foreach (var item in team.Feed)
                    foreach (var (key, value) in item.Payload)
                    {
                        var found = Regex.Matches(JsonSerializer.Serialize(value), @"day \d+", RegexOptions.IgnoreCase);
                        if (found.Count > 0)
                            offenders.Add($"{team.Id} {item.Kind} {key}: {string.Join(", ", found.Select(m => m.Value))}");
                    }

            Assert.True(offenders.Count == 0, "run-day index in a payload: " + string.Join(" | ", offenders));
        }

        [Fact]
        public async Task The_measurement_is_called_the_check_back_and_no_payload_says_when_it_lands()
        {
            string[] retiredKeys = ["reading_lands_in", "awaiting_a_reading", "check_back_lands_in",
                                    "signal_read_day", "days_until_read", "confirmation_due_day"];
            var retiredProse = new Regex(@"ended without a reading|next reading on it lands|\breads in \d|check-back lands",
                RegexOptions.IgnoreCase);

            var teams = await Teams.Value;
            var offenders = new List<string>();
            foreach (var team in teams)
                foreach (var item in team.Feed)
                    foreach (var (key, value) in item.Payload)
                    {
                        if (retiredKeys.Contains(key)) offenders.Add($"{team.Id} {item.Kind} field {key}");
                        var m = retiredProse.Match(JsonSerializer.Serialize(value));
                        if (m.Success) offenders.Add($"{team.Id} {item.Kind} {key}: {m.Value}");
                    }
            Assert.True(offenders.Count == 0, "retired check-back vocabulary: " + string.Join(" | ", offenders));

            // And the phrase that replaced the interval actually reaches payloads, so this cannot
            // pass by the check-back having quietly disappeared from the product.
            Assert.Contains(teams.SelectMany(t => t.Feed),
                f => JsonSerializer.Serialize(f.Payload).Contains("comes when a question on it comes round again"));
        }

        [Fact]
        public void Working_days_are_counted_on_the_teams_own_week()
        {
            // Mon 5 Jan 2026 is day 1; the team works Mon-Fri.
            var clock = new EngineWorkingDays(EngineContext.FromKit(Path.Combine(Root, "kit", "data")),
                new DateOnly(2026, 1, 5), "mon-fri");

            Assert.Equal("today", clock.Back(8, 8));
            Assert.Equal("1 working day ago", clock.Back(9, 8));           // Tue 13 Jan, from Mon 12
            Assert.Equal("1 working day ago", clock.Back(8, 5));           // Mon 12 Jan, from Fri 9: the weekend is not counted
            Assert.Equal("in 3 working days", clock.Ahead(8, 11));          // Mon 12 to Thu 15
            Assert.Null(clock.Back(8, null));
        }

        private static async Task<List<Team>> RunAsync()
        {
            var ctx = EngineContext.FromKit(Path.Combine(Root, "kit", "data"));
            var state = StateConfig.Load(Path.Combine(Root, "kit", "data", "thresholds.json"));
            var configs = new List<TeamConfig>();
            foreach (var path in Directory.GetFiles(Path.Combine(Root, "scenarios"), "team-*.json").OrderBy(p => p))
            {
                var file = await EngineScenarioFile.LoadAsync(path, CancellationToken.None);
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
                configs.Add(new TeamConfig
                {
                    Id = file.Id,
                    Name = file.Name,
                    ChildTeamIds = doc.RootElement.TryGetProperty("childTeamIds", out var c) && c.ValueKind == JsonValueKind.Array
                        ? c.EnumerateArray().Select(x => x.GetString()!).ToList()
                        : [],
                    Roster = file.Roster.Select(m => new Member { Id = m.Id, Name = m.Name }).ToList(),
                    Script = file.Script.ToDictionary(kv => int.Parse(kv.Key),
                        kv => kv.Value.Select(e => new ScriptEntry(e.Member, e.Track, e.Smiley, e.Chips, e.Text)).ToList()),
                    WorkingWeek = file.WorkingWeek,
                    Confirmations = file.Confirmations.Select(x => (x.CycleIndex, x.Rung, x.DayOffset, x.WorkedIfMet)).ToList(),
                    DefaultConfirmation = file.DefaultConfirmation is { } d ? (d.DayOffset, d.WorkedIfMet) : null,
                    AutoConfirm = true
                });
            }

            var sim = new Simulation(ctx, configs, new Resolver(state, new BufferingStateSink("wording")), horizon: 124);
            while (!sim.Step().AtHorizon) { }
            return sim.AllTeams();
        }

        private static string FindEngineRoot([CallerFilePath] string source = "")
        {
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(source)!); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "external", "RRIDA-Engine");
                if (Directory.Exists(Path.Combine(candidate, "kit", "data"))) return candidate;
            }
            throw new DirectoryNotFoundException("external/RRIDA-Engine was not found.");
        }
    }
}
