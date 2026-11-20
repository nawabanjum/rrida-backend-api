using System.Runtime.CompilerServices;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Rrida.Engine;
using Rrida.State;
using Xunit;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>
    /// Seeding a scenario with the scripted manager stores what the MVP's own run decides:
    /// every cycle, on the same days, with the same outcome. The seed goes the production
    /// way — answers into the tables, the manager's confirmations stored as taps, the team
    /// replayed from them — and is compared with the engine run with the scripted manager
    /// on, which is the MVP's run (the engine reproduces the MVP scenario by scenario).
    /// </summary>
    public class EngineScenarioSeederMvpTests
    {
        private const uint Unit = 9301;
        private static readonly DateOnly MvpDayOne = new(2026, 1, 5);   // calendar.js DEFAULT_START_DATE
        private static readonly Lazy<EngineContext> Ctx = new(() => EngineContext.FromKit(Path.Combine(EngineRoot(), "kit", "data")));
        private static readonly Lazy<StateConfig> States = new(() => StateConfig.Load(Path.Combine(EngineRoot(), "kit", "data", "thresholds.json")));

        public static IEnumerable<object[]> Scenarios() =>
            Directory.GetFiles(Path.Combine(EngineRoot(), "scenarios"), "team-*.json").Select(f => new object[] { Path.GetFileName(f) });

        [Theory]
        [MemberData(nameof(Scenarios))]
        public async Task Auto_confirm_stores_the_MVP_run(string file)
        {
            var scenario = await EngineScenarioFile.LoadAsync(Path.Combine(EngineRoot(), "scenarios", file), CancellationToken.None);
            var expected = MvpRun(scenario);

            await using var db = NewDb();
            await Seeder(db).SeedScenarioAsync(Unit, scenario, MvpDayOne, autoConfirm: true, reset: false, actorUserId: 1);

            var stored = await db.EngineCycles.AsNoTracking().Include(c => c.Deliveries).ToListAsync();
            Assert.Equal(expected.Select(c => c.Id).OrderBy(k => k), stored.Select(c => c.CycleKey).OrderBy(k => k));
            foreach (var cycle in expected)
            {
                var row = stored.Single(c => c.CycleKey == cycle.Id);
                Assert.Equal((cycle.Id, cycle.IssueFile, cycle.SelectedDay, cycle.DeliveredDay, cycle.ResolvedDay, cycle.Outcome),
                    (row.CycleKey, row.IssueFileKey, row.SelectedDay, row.DeliveredDay, row.ResolvedDay, row.Outcome));
                var confirmed = cycle.RungOneRecord?.ConfirmedDay ?? (cycle.Rung == 1 ? cycle.ConfirmedDay : null);
                Assert.Equal(confirmed, row.Deliveries.Single(d => d.Rung == 1).ConfirmedDay);
            }
        }

        private static List<Cycle> MvpRun(EngineScenarioFile s)
        {
            var config = new TeamConfig
            {
                Id = s.Id, Name = s.Name,
                Roster = s.Roster.Select(m => new Member { Id = m.Id, Name = m.Name }).ToList(),
                Script = s.Script.ToDictionary(kv => int.Parse(kv.Key),
                    kv => kv.Value.Select(e => new ScriptEntry(e.Member, e.Track, e.Smiley, e.Chips, e.Text)).ToList()),
                WorkingWeek = s.WorkingWeek,
                Confirmations = s.Confirmations.Select(c => (c.CycleIndex, c.Rung, c.DayOffset, c.WorkedIfMet)).ToList(),
                DefaultConfirmation = s.DefaultConfirmation is { } d ? (d.DayOffset, d.WorkedIfMet) : null,
            };
            var sim = new Simulation(Ctx.Value, [config], new Resolver(States.Value, new BufferingStateSink("t")),
                horizon: s.Horizon > 0 ? s.Horizon : 124);
            Simulation.StepResult step;
            do step = sim.Step(); while (!step.AtHorizon);
            var team = sim.AllTeams()[0];
            return team.CycleHistory
                .Concat(team.ActiveCycle is null ? [] : new[] { team.ActiveCycle })
                .Concat(team.AwaitingRead).Concat(team.PendingRungTwo)
                .GroupBy(c => c.Id).Select(g => g.Last()).ToList();
        }

        private static EngineScenarioSeeder Seeder(ApplicationDbContext db)
        {
            db.BusinessUnits.Add(new BusinessUnit
            {
                Id = Unit, BusinessUnitCode = $"BU{Unit}", BusinessUnitShortCode = $"S{Unit}", Name = "Team",
                NameInvariant = "team", RegistrationDate = DateTime.UtcNow, ProfileId = 93,
                ActiveDailyQuestionSetGroupId = 1, ActiveDailyQuestionSetGroup = null!
            });
            db.SaveChanges();

            var engine = new Mock<IRridaEngineContextFactory>();
            engine.Setup(e => e.Context).Returns(Ctx.Value);
            engine.Setup(e => e.Calendar).Returns(Ctx.Value.Calendar);
            engine.Setup(e => e.StateConfig).Returns(States.Value);
            engine.Setup(e => e.KitDataPath).Returns(Path.Combine(EngineRoot(), "kit", "data"));

            var runner = new EngineReplayRunner(db, engine.Object,
                new EngineResultStore(db, NullLogger<EngineResultStore>.Instance),
                new EngineWeeklyFlagWriter(db, engine.Object, NullLogger<EngineWeeklyFlagWriter>.Instance),
                new Mock<IEngineNarrationQueue>().Object, NullLogger<EngineReplayRunner>.Instance);
            return new EngineScenarioSeeder(db, engine.Object, runner, NullLogger<EngineScenarioSeeder>.Instance);
        }

        private static ApplicationDbContext NewDb() =>
            new TestApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"seeder-mvp-{Guid.NewGuid()}")
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
