using System.Runtime.CompilerServices;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Rrida.Engine;
using Rrida.State;
using Xunit;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>
    /// Each rung's stored DeliveredDay is the day that rung went out: rung one on the day
    /// the cycle was selected, rung two on the day it laddered. The engine moves the
    /// cycle's own DeliveredDay to the rung-two day, so rung one must not copy it — the
    /// Team Health action dot and its date are read from these rows.
    /// </summary>
    public class EngineResultStoreDeliveryDayTests
    {
        private const uint Unit = 9201;
        private static readonly Lazy<EngineContext> Ctx = new(() => EngineContext.FromKit(Path.Combine(EngineRoot(), "kit", "data")));

        [Fact]
        public async Task Each_rung_keeps_its_own_delivered_day()
        {
            var laddered = 0;
            foreach (var file in Directory.GetFiles(Path.Combine(EngineRoot(), "scenarios"), "team-*.json"))
            {
                var scenario = await EngineScenarioFile.LoadAsync(file, CancellationToken.None);
                var (team, sink, lastDay) = Run(scenario);

                await using var db = NewDb();
                await new EngineResultStore(db, NullLogger<EngineResultStore>.Instance)
                    .SaveRunAsync(Unit, Ctx.Value, team, sink, lastDay);

                var cycles = team.CycleHistory
                    .Concat(team.ActiveCycle is null ? [] : new[] { team.ActiveCycle })
                    .Concat(team.AwaitingRead).Concat(team.PendingRungTwo)
                    .GroupBy(c => c.Id).Select(g => g.Last());

                foreach (var cycle in cycles)
                {
                    var rows = await db.EngineCycleDeliveries
                        .Where(d => d.Cycle!.BusinessUnitId == Unit && d.Cycle.CycleKey == cycle.Id)
                        .ToListAsync();

                    Assert.Equal(cycle.SelectedDay, rows.Single(d => d.Rung == 1).DeliveredDay);
                    if (cycle.RungOneRecord is not null)
                    {
                        laddered++;
                        Assert.Equal(cycle.DeliveredDay, rows.Single(d => d.Rung == 2).DeliveredDay);
                        Assert.True(rows.Single(d => d.Rung == 1).DeliveredDay < rows.Single(d => d.Rung == 2).DeliveredDay);
                    }
                }
            }

            // The scenarios must include a cycle that laddered, or this proves nothing.
            Assert.True(laddered > 0);
        }

        [Fact]
        public async Task The_share_is_stored_in_words_never_as_a_count()
        {
            var file = Directory.GetFiles(Path.Combine(EngineRoot(), "scenarios"), "team-*.json").First();
            var (team, sink, lastDay) = Run(await EngineScenarioFile.LoadAsync(file, CancellationToken.None));

            await using var db = NewDb();
            await new EngineResultStore(db, NullLogger<EngineResultStore>.Instance).SaveRunAsync(Unit, Ctx.Value, team, sink, lastDay);

            var cycles = await db.EngineCycles.ToListAsync();
            Assert.NotEmpty(cycles);
            Assert.All(cycles, c =>
            {
                Assert.DoesNotMatch(@"\d+ of \d+", c.ShareText ?? "");
                Assert.Contains(c.ShareText, new[] { "fewer than half the team", "more than half the team", "most of the team", "almost everyone on the team" });
            });
        }

        [Fact]
        public async Task A_cycle_a_later_run_no_longer_produces_is_removed_up_to_the_run_day()
        {
            var file = Directory.GetFiles(Path.Combine(EngineRoot(), "scenarios"), "team-*.json").First();
            var (team, sink, lastDay) = Run(await EngineScenarioFile.LoadAsync(file, CancellationToken.None));

            await using var db = NewDb();
            var store = new EngineResultStore(db, NullLogger<EngineResultStore>.Instance);
            await store.SaveRunAsync(Unit, Ctx.Value, team, sink, lastDay);

            // An earlier run's selections: one inside this run's days, one after them.
            db.EngineCycles.Add(Ghost("C98", selectedDay: 10));
            db.EngineCycles.Add(Ghost("C99", selectedDay: lastDay + 5));
            await db.SaveChangesAsync();

            await store.SaveRunAsync(Unit, Ctx.Value, team, sink, lastDay);

            var keys = await db.EngineCycles.Select(c => c.CycleKey).ToListAsync();
            Assert.DoesNotContain("C98", keys);
            Assert.Contains("C99", keys);   // beyond this run: a longer run's, left for it
            Assert.False(await db.EngineCycleDeliveries.AnyAsync(d => d.ActionKey == "GHOST"));
        }

        private static EngineCycle Ghost(string key, int selectedDay) => new()
        {
            BusinessUnitId = Unit, CycleKey = key, CycleIndex = 90, IssueFileKey = "MA-E-002", SelectedDay = selectedDay,
            DeliveredDay = selectedDay, ConfirmationDueDay = selectedDay + 3,
            Deliveries = [new EngineCycleDelivery { Rung = 1, ActionKey = selectedDay < 100 ? "GHOST" : "LATER", DeliveredDay = selectedDay }]
        };

        private static (Team Team, BufferingStateSink Sink, int LastDay) Run(EngineScenarioFile file)
        {
            var config = new TeamConfig
            {
                Id = file.Id, Name = file.Name,
                Roster = file.Roster.Select(m => new Member { Id = m.Id, Name = m.Name }).ToList(),
                Script = file.Script.ToDictionary(kv => int.Parse(kv.Key),
                    kv => kv.Value.Select(e => new ScriptEntry(e.Member, e.Track, e.Smiley, e.Chips, e.Text)).ToList()),
                WorkingWeek = file.WorkingWeek,
                Confirmations = file.Confirmations.Select(c => (c.CycleIndex, c.Rung, c.DayOffset, c.WorkedIfMet)).ToList(),
                DefaultConfirmation = file.DefaultConfirmation is { } d ? (d.DayOffset, d.WorkedIfMet) : null,
            };
            var sink = new BufferingStateSink("t");
            var sim = new Simulation(Ctx.Value, [config],
                new Resolver(StateConfig.Load(Path.Combine(EngineRoot(), "kit", "data", "thresholds.json")), sink),
                horizon: file.Horizon > 0 ? file.Horizon : 124);
            Simulation.StepResult step;
            do step = sim.Step(); while (!step.AtHorizon);
            return (sim.AllTeams()[0], sink, sim.Day);
        }

        private static ApplicationDbContext NewDb() =>
            new TestApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"delivery-day-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static string EngineRoot([CallerFilePath] string source = "")
        {
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(source)!); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "external", "RRIDA-Engine");
                if (Directory.Exists(candidate)) return candidate;
            }
            throw new DirectoryNotFoundException("external/RRIDA-Engine not found");
        }
    }
}
