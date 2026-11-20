using System.Runtime.CompilerServices;
using System.Text.Json;
using CloverBackoffice.Services.Implementation;
using Rrida.Engine;
using Rrida.State;
using Xunit;
using Xunit.Abstractions;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>
    /// The reference's own count after D35 (developer-reply-20261017.md): across the eight
    /// authored scenarios, 720 daily messages (eight teams times ninety asked days) and 1,189
    /// feed items in all. Clive's question 1 asked why our document said 1,459 against his 1,461.
    ///
    /// The answer is the senior team. The reference runs the eight together, and
    /// team-engineering-leads has team-platform and team-data as its children, so it also gets
    /// the senior-level messages a roll-up produces. Each team replayed on its own, as the
    /// backend does, cannot produce those: two fewer, before D35 and after it.
    /// </summary>
    public class EngineFeedCountTests(ITestOutputHelper output)
    {
        private static readonly string Root = FindEngineRoot();

        [Fact]
        public async Task The_eight_scenarios_produce_the_references_message_counts()
        {
            var ctx = EngineContext.FromKit(Path.Combine(Root, "kit", "data"));
            var state = StateConfig.Load(Path.Combine(Root, "kit", "data", "thresholds.json"));
            var paths = Directory.GetFiles(Path.Combine(Root, "scenarios"), "team-*.json").OrderBy(p => p).ToList();
            var configs = new List<TeamConfig>();
            foreach (var path in paths) configs.Add(await ConfigAsync(path, withChildren: true));

            // The reference's way: one simulation, the senior team over its children.
            var together = new Simulation(ctx, configs, new Resolver(state, new BufferingStateSink("count")), horizon: 124);
            while (!together.Step().AtHorizon) { }
            var all = together.AllTeams().SelectMany(t => t.Feed).ToList();

            // The backend's way: each team replayed alone.
            var alone = new List<FeedItem>();
            foreach (var path in paths)
            {
                var config = await ConfigAsync(path, withChildren: false);
                var single = new Simulation(ctx, [config], new Resolver(state, new BufferingStateSink("count")), horizon: 124);
                while (!single.Step().AtHorizon) { }
                alone.AddRange(single.AllTeams()[0].Feed);
            }

            string Kinds(IEnumerable<FeedItem> feed) =>
                string.Join(", ", feed.GroupBy(f => f.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}"));
            output.WriteLine($"together {all.Count}: {Kinds(all)}");
            output.WriteLine($"alone    {alone.Count}: {Kinds(alone)}");

            Assert.Equal(720, all.Count(f => f.Kind == "daily"));
            Assert.Equal(1189, all.Count);
            Assert.Equal(720, alone.Count(f => f.Kind == "daily"));
            Assert.Equal(1187, alone.Count);

            // The two are the senior team's cross-team pair, which only a run across teams makes.
            var extra = all.Where(f => f.Kind is "cross_team" or "cross_team_note").ToList();
            Assert.Equal(new[] { "cross_team", "cross_team_note" }, extra.Select(f => f.Kind).OrderBy(k => k));
            Assert.All(extra, f => Assert.Equal("team-engineering-leads", f.TeamId));
            Assert.DoesNotContain(alone, f => f.Kind is "cross_team" or "cross_team_note");
        }

        private static async Task<TeamConfig> ConfigAsync(string path, bool withChildren)
        {
            var file = await EngineScenarioFile.LoadAsync(path, CancellationToken.None);
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var children = withChildren && doc.RootElement.TryGetProperty("childTeamIds", out var c) && c.ValueKind == JsonValueKind.Array
                ? c.EnumerateArray().Select(x => x.GetString()!).ToList()
                : [];
            return new TeamConfig
            {
                Id = file.Id,
                Name = file.Name,
                ChildTeamIds = children,
                Roster = file.Roster.Select(m => new Member { Id = m.Id, Name = m.Name }).ToList(),
                Script = file.Script.ToDictionary(kv => int.Parse(kv.Key),
                    kv => kv.Value.Select(e => new ScriptEntry(e.Member, e.Track, e.Smiley, e.Chips, e.Text)).ToList()),
                WorkingWeek = file.WorkingWeek,
                Confirmations = file.Confirmations.Select(x => (x.CycleIndex, x.Rung, x.DayOffset, x.WorkedIfMet)).ToList(),
                DefaultConfirmation = file.DefaultConfirmation is { } d ? (d.DayOffset, d.WorkedIfMet) : null,
                AutoConfirm = true
            };
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
