using System.Runtime.CompilerServices;
using System.Text.Json;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Rrida.Engine;
using Rrida.State;
using Xunit;
using static CloverBackoffice.Services.Implementation.ManagerActionEngineActivity;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>
    /// The manager's own actions on the engine's weekly and monthly messages: reported as the
    /// period's activity, added when the run is stored, never fed to the engine. A team with no
    /// manual actions keeps exactly the payloads it had.
    /// </summary>
    public class ManagerActionEngineActivityTests
    {
        private const uint Unit = 9301;
        private static readonly DateOnly Start = new(2026, 9, 1);
        private static readonly Lazy<EngineContext> Ctx = new(() => EngineContext.FromKit(Path.Combine(EngineRoot(), "kit", "data")));

        // ── The period rule ──────────────────────────────────────────────────

        private static readonly DateOnly From = new(2026, 9, 8), To = new(2026, 9, 14);

        private static Fact A(string title, string created, string status = ManagerActionStatuses.Open,
            string? completed = null, string? cancelled = null, string? due = null) =>
            new(title, DateOnly.Parse(created), status,
                completed is null ? null : DateOnly.Parse(completed),
                cancelled is null ? null : DateOnly.Parse(cancelled),
                due is null ? null : DateOnly.Parse(due));

        private static Dictionary<string, string> Progress(IReadOnlyList<Fact> facts) =>
            (ForPeriod(facts, From, To) ?? []).ToDictionary(r => (string)r["action"]!, r => (string)r["progress"]!);

        [Fact]
        public void Each_action_is_reported_by_what_happened_to_it_in_the_period()
        {
            var p = Progress([
                A("Done this week", "2026-09-01", ManagerActionStatuses.Done, completed: "2026-09-10"),
                A("Added this week", "2026-09-09"),
                A("Older, still open", "2026-09-02", ManagerActionStatuses.InProgress),
                A("Dropped this week", "2026-09-03", ManagerActionStatuses.Cancelled, cancelled: "2026-09-12"),
                A("Done later, open then", "2026-09-05", ManagerActionStatuses.Done, completed: "2026-09-20"),
                A("Done before", "2026-08-20", ManagerActionStatuses.Done, completed: "2026-09-01"),
                A("Dropped before", "2026-08-20", ManagerActionStatuses.Cancelled, cancelled: "2026-09-02"),
                A("Not yet created", "2026-09-15"),
            ]);

            Assert.Equal(Completed, p["Done this week"]);
            Assert.Equal(AddedStillOpen, p["Added this week"]);
            Assert.Equal(StillOpen, p["Older, still open"]);
            Assert.Equal(Dropped, p["Dropped this week"]);
            Assert.Equal(StillOpen, p["Done later, open then"]);
            Assert.False(p.ContainsKey("Done before"));
            Assert.False(p.ContainsKey("Dropped before"));
            Assert.False(p.ContainsKey("Not yet created"));
        }

        [Fact]
        public void Completed_first_then_open_then_dropped_with_due_and_overdue()
        {
            var rows = ForPeriod([
                A("Open late", "2026-09-02", due: "2026-09-10"),
                A("Dropped", "2026-09-02", ManagerActionStatuses.Cancelled, cancelled: "2026-09-09"),
                A("Done", "2026-09-02", ManagerActionStatuses.Done, completed: "2026-09-09", due: "2026-09-30"),
            ], From, To)!;

            Assert.Equal(new[] { "Done", "Open late", "Dropped" }, rows.Select(r => (string)r["action"]!));
            Assert.Equal("10 September", rows[1]["due"]);
            Assert.Equal(true, rows[1]["overdue"]);
            Assert.False(rows[0].ContainsKey("overdue"));
        }

        [Fact]
        public void Nothing_in_the_period_gives_no_rows()
        {
            Assert.Null(ForPeriod([], From, To));
            Assert.Null(ForPeriod([A("Later", "2026-09-20")], From, To));
        }

        // ── Stored with a real engine run ────────────────────────────────────

        [Fact]
        public async Task A_team_without_manual_actions_keeps_exactly_the_same_payloads()
        {
            var (team, sink, lastDay) = Run();

            await using var plain = NewDb();
            await Store(plain, team, sink, lastDay);

            await using var withSettings = NewDb();
            withSettings.EngineTeamSettings.Add(Settings());
            // Another team's action is not this team's progress.
            withSettings.ManagerActions.Add(Action("Another team's retro", Start.AddDays(3), unitId: Unit + 1));
            await withSettings.SaveChangesAsync();
            await Store(withSettings, team, sink, lastDay);

            var a = await plain.EngineFeedItems.OrderBy(f => f.FeedKey).Select(f => f.PayloadJson).ToListAsync();
            var b = await withSettings.EngineFeedItems.OrderBy(f => f.FeedKey).Select(f => f.PayloadJson).ToListAsync();
            Assert.Equal(a, b);
        }

        [Fact]
        public async Task Weekly_and_monthly_messages_carry_the_managers_actions_and_nothing_else_changes()
        {
            var (team, sink, lastDay) = Run();
            var weekly = team.Feed.First(f => f.Kind == "weekly" && f.Day > 14);
            var readingDate = Start.AddDays(weekly.Day - 1);

            await using var db = NewDb();
            db.EngineTeamSettings.Add(Settings());
            db.ManagerActions.Add(Action("Run a retro on the release", readingDate.AddDays(-3),
                status: ManagerActionStatuses.Done, completed: readingDate.AddDays(-1)));
            await db.SaveChangesAsync();
            await Store(db, team, sink, lastDay);

            var rows = await db.EngineFeedItems.ToListAsync();

            var stored = rows.Single(r => r.Kind == "weekly" && r.Day == weekly.Day);
            using (var doc = JsonDocument.Parse(stored.PayloadJson))
            {
                var row = doc.RootElement.GetProperty(WeeklyKey)[0];
                Assert.Equal("Run a retro on the release", row.GetProperty("action").GetString());
                Assert.Equal(Completed, row.GetProperty("progress").GetString());
                // The engine's own fields are still there, unchanged.
                Assert.Equal(JsonSerializer.Serialize(weekly.Payload["state_after"]),
                    doc.RootElement.GetProperty("state_after").GetRawText());
            }
            Assert.True(EranNarratorService.HasManagerActivity(stored.PayloadJson));

            // The weekly a fortnight later no longer mentions it: it was completed before.
            var later = rows.Where(r => r.Kind == "weekly" && r.Day >= weekly.Day + 14).ToList();
            Assert.All(later, r => Assert.False(EranNarratorService.HasManagerActivity(r.PayloadJson)));

            // Daily and every other kind are exactly what the engine produced.
            Assert.All(rows.Where(r => r.Kind is not ("weekly" or "monthly")),
                r => Assert.DoesNotContain("manager_actions_", r.PayloadJson));

            // The engine's cycles and state are exactly what a run without the action stores.
            await using var plain = NewDb();
            await Store(plain, team, sink, lastDay);
            Assert.Equal(
                await plain.EngineCycleDeliveries.OrderBy(d => d.ActionKey).ThenBy(d => d.Rung)
                    .Select(d => d.ActionKey + "|" + d.Rung + "|" + d.Outcome + "|" + d.DeliveredDay).ToListAsync(),
                await db.EngineCycleDeliveries.OrderBy(d => d.ActionKey).ThenBy(d => d.Rung)
                    .Select(d => d.ActionKey + "|" + d.Rung + "|" + d.Outcome + "|" + d.DeliveredDay).ToListAsync());
            Assert.Equal(
                await plain.EngineStateDaily.OrderBy(s => s.Day).Select(s => s.RecordJson).ToListAsync(),
                await db.EngineStateDaily.OrderBy(s => s.Day).Select(s => s.RecordJson).ToListAsync());
        }

        [Fact]
        public void Eran_may_quote_the_managers_action_on_a_weekly_message()
        {
            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["state_after"] = "drift",
                [WeeklyKey] = new[] { new Dictionary<string, object?> { ["action"] = "Ask two people about handovers", ["progress"] = Completed } }
            });
            var prose = "This week you completed Ask two people about handovers. The reading still sits in drift.";

            Assert.True(EranGuard.CheckFeedItem("weekly", null, payload, prose).Passed);
            // Without it in the payload, "two people" is a count of people and fails.
            Assert.False(EranGuard.CheckFeedItem("weekly", null, "{\"state_after\":\"drift\"}", prose).Passed);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static EngineTeamSettings Settings() =>
            new() { BusinessUnitId = Unit, StartDate = Start, IsEnabled = true, CreatedAt = DateTime.UtcNow };

        private static ManagerAction Action(string title, DateOnly created, string status = ManagerActionStatuses.Open,
            DateOnly? completed = null, uint unitId = Unit) => new()
        {
            ProfileId = 1, UnitId = unitId, Title = title,
            Source = ManagerActionSources.Manual, Status = status, CreatedBy = 7,
            CreatedAt = created.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc),
            UpdatedAt = (completed ?? created).ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc),
            CompletedAt = completed?.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc)
        };

        private static Task Store(ApplicationDbContext db, Team team, BufferingStateSink sink, int lastDay) =>
            new EngineResultStore(db, NullLogger<EngineResultStore>.Instance).SaveRunAsync(Unit, Ctx.Value, team, sink, lastDay);

        private static (Team Team, BufferingStateSink Sink, int LastDay) Run()
        {
            var file = EngineScenarioFile.LoadAsync(
                Directory.GetFiles(Path.Combine(EngineRoot(), "scenarios"), "team-*.json").First(), CancellationToken.None)
                .GetAwaiter().GetResult();
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
                .UseInMemoryDatabase($"manager-activity-{Guid.NewGuid()}")
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
