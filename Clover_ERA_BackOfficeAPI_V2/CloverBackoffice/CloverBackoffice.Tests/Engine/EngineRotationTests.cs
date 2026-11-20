using System.Runtime.CompilerServices;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Rrida.Engine;
using Rrida.State;
using Xunit;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>
    /// D34, as the client confirmed: every team is asked all ninety individual and all ninety team
    /// questions, one of each per asked day, looping (data/rotation-90/rotation.json: day d asks
    /// 120+d and 300+d). The demo's six exist only for the engine team's authored scenarios.
    /// </summary>
    public class EngineRotationTests
    {
        private static readonly string EngineRoot = FindEngineRoot();
        private static readonly Lazy<RridaEngineContextFactory> Factory = new(() => new RridaEngineContextFactory(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Rrida:KitDataPath"] = Path.Combine(EngineRoot, "kit", "data"),
                ["Rrida:ProductionRotationPath"] = Path.Combine(EngineRoot, "rotations", "rotation-90.json")
            }).Build(),
            NullLogger<RridaEngineContextFactory>.Instance));

        [Fact]
        public void Every_team_is_asked_all_ninety_one_of_each_per_asked_day_looping()
        {
            var rotation = Factory.Value.Context.Rotation;
            Assert.Equal(90, rotation.CycleLength);
            for (var d = 1; d <= 90; d += 1)
            {
                Assert.Equal(120 + d, rotation.QuestionIdForDay(d, "individual"));
                Assert.Equal(300 + d, rotation.QuestionIdForDay(d, "team"));
            }
            Assert.Equal(121, rotation.QuestionIdForDay(91, "individual"));
            Assert.Equal(301, rotation.QuestionIdForDay(91, "team"));
        }

        [Fact]
        public async Task An_authored_scenario_is_refused_on_the_ninety()
        {
            // The reference pins them to the demo's six; the engine says why on the ninety.
            var file = await EngineScenarioFile.LoadAsync(Path.Combine(EngineRoot, "scenarios", "team-platform.json"), CancellationToken.None);
            var sim = NewSimulation(Factory.Value.Context, file);

            var error = Assert.Throws<InvalidOperationException>(() => { while (!sim.Step().AtHorizon) { } });
            Assert.Contains("rotation asks", error.Message);
        }

        [Fact]
        public void A_team_writes_a_daily_only_on_the_days_it_is_asked()
        {
            var config = new TeamConfig
            {
                Id = "prod", Name = "Production team",
                Roster = Enumerable.Range(1, 5).Select(i => new Member { Id = $"M{i}", Name = $"M{i}" }).ToList(),
                Script = new Dictionary<int, List<ScriptEntry>>(),
                WorkingWeek = "mon-fri",
                StartDate = "2026-01-05"
            };
            var sim = new Simulation(Factory.Value.Context, [config],
                new Resolver(Factory.Value.StateConfig, new BufferingStateSink("test")), horizon: 28, config.StartDate);
            while (!sim.Step().AtHorizon) { }

            var team = sim.AllTeams()[0];
            var dailies = team.Feed.Where(f => f.Kind == "daily").Select(f => f.Day).ToList();
            Assert.Equal(team.AskedDaysTo(28), dailies.Count);
            Assert.All(dailies, d => Assert.True(team.IsAskedOn(d), $"daily on day {d}, which is not asked"));
        }

        [Fact]
        public async Task The_check_in_asks_question_120_plus_the_asked_day()
        {
            await using var db = NewDb();
            const uint unit = 9501;
            SeedTeam(db, unit, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10));
            var service = new EngineCheckInService(db, Factory.Value, new Mock<IEngineTeamProvisioner>().Object,
                NullLogger<EngineCheckInService>.Instance);

            await service.EnrolAsync(new EngineEnrolRequestDto { TeamCode = $"S{unit}", MemberToken = "rotation-member" });
            var today = await service.TodayAsync("rotation-member", unit);

            Assert.True(today.IsAskedToday);
            Assert.Equal(120 + today.AskedDayIndex, today.Questions.Single(q => q.Track == "individual").QuestionKey);
            Assert.Equal(300 + today.AskedDayIndex, today.Questions.Single(q => q.Track == "team").QuestionKey);
        }

        [Fact]
        public async Task Today_by_code_answers_exactly_as_today_by_id()
        {
            await using var db = NewDb();
            const uint unit = 9503;
            SeedTeam(db, unit, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10));
            var service = new EngineCheckInService(db, Factory.Value, new Mock<IEngineTeamProvisioner>().Object,
                NullLogger<EngineCheckInService>.Instance);
            await service.EnrolAsync(new EngineEnrolRequestDto { TeamCode = $"S{unit}", MemberToken = "code-member" });

            var byId = await service.TodayAsync("code-member", unit);
            var byCode = await service.TodayByCodeAsync("code-member", $" S{unit} ");

            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(byId), System.Text.Json.JsonSerializer.Serialize(byCode));
            Assert.Equal(unit, byCode.BusinessUnitId);
        }

        [Theory]
        [InlineData("code-member", "S0000", "No team was found for that code.")]
        [InlineData("code-member", " ", "A team code is required.")]
        [InlineData("never-enrolled", "S9504", "This device is not enrolled.")]
        public async Task Today_by_code_refuses_an_unknown_code_or_member(string token, string code, string message)
        {
            await using var db = NewDb();
            const uint unit = 9504;
            SeedTeam(db, unit, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10));
            var service = new EngineCheckInService(db, Factory.Value, new Mock<IEngineTeamProvisioner>().Object,
                NullLogger<EngineCheckInService>.Instance);
            await service.EnrolAsync(new EngineEnrolRequestDto { TeamCode = $"S{unit}", MemberToken = "code-member" });

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.TodayByCodeAsync(token, code));
            Assert.Equal(message, error.Message);
        }

        [Fact]
        public async Task An_answer_recorded_on_the_old_six_question_rotation_is_left_out_of_the_replay()
        {
            await using var db = NewDb();
            const uint unit = 9502;
            var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5);
            SeedTeam(db, unit, start);
            var member = new EngineMember { ProfileId = 94, TokenHash = "h", EngineMemberKey = "m-000001", CreatedAt = DateTime.UtcNow };
            db.EngineMembers.Add(member);
            db.SaveChanges();
            db.EngineTeamMemberships.Add(new EngineTeamMembership { MemberId = member.Id, BusinessUnitId = unit, EffectiveFrom = start, CreatedAt = DateTime.UtcNow });

            var data = Factory.Value.Context.Data;
            // Day 1 on the ninety asks 121; day 2 was answered on the six, where it asked 206.
            db.EngineAnswers.Add(Answer(unit, member.Id, start, 1, 121, data.QuestionOf(121)!.Chips["positive"][0]));
            db.EngineAnswers.Add(Answer(unit, member.Id, start.AddDays(1), 2, 206, data.QuestionOf(206)!.Chips["positive"][0]));
            db.SaveChanges();

            var runner = new EngineReplayRunner(db, Factory.Value,
                new EngineResultStore(db, NullLogger<EngineResultStore>.Instance),
                new EngineWeeklyFlagWriter(db, Factory.Value, NullLogger<EngineWeeklyFlagWriter>.Instance),
                new Mock<IEngineNarrationQueue>().Object, NullLogger<EngineReplayRunner>.Instance);

            // Before, the engine refused the whole replay: "chip … belongs to question 206, but the
            // individual rotation asks 122".
            var result = await runner.RunAsync(unit, start.AddDays(3));
            Assert.Equal(4, result.Days);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static EngineAnswer Answer(uint unit, long memberId, DateOnly date, int askedDay, int question, string chip)
        {
            var answer = new EngineAnswer
            {
                BusinessUnitId = unit, MemberId = memberId, AnswerDate = date, Day = askedDay, AskedDayIndex = askedDay,
                Track = "individual", QuestionKey = question, Level = "green", CreatedAt = DateTime.UtcNow
            };
            answer.Chips.Add(new EngineAnswerChip { ChipKey = chip, Position = 0 });
            return answer;
        }

        private static void SeedTeam(ApplicationDbContext db, uint unit, DateOnly start)
        {
            db.BusinessUnits.Add(new BusinessUnit
            {
                Id = unit, BusinessUnitCode = $"BU{unit}", BusinessUnitShortCode = $"S{unit}", Name = "Team",
                NameInvariant = "team", RegistrationDate = DateTime.UtcNow, ProfileId = 94,
                ActiveDailyQuestionSetGroupId = 1, ActiveDailyQuestionSetGroup = null!
            });
            db.EngineTeamSettings.Add(new EngineTeamSettings
            {
                BusinessUnitId = unit, StartDate = start, WorkingWeek = "every-day", IsEnabled = true, CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        private static ApplicationDbContext NewDb() =>
            new TestApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"rotation-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static Simulation NewSimulation(EngineContext ctx, EngineScenarioFile file) =>
            new(ctx, [new TeamConfig
            {
                Id = file.Id, Name = file.Name,
                Roster = file.Roster.Select(m => new Member { Id = m.Id, Name = m.Name }).ToList(),
                Script = file.Script.ToDictionary(kv => int.Parse(kv.Key),
                    kv => kv.Value.Select(e => new ScriptEntry(e.Member, e.Track, e.Smiley, e.Chips, e.Text)).ToList()),
                WorkingWeek = file.WorkingWeek
            }], new Resolver(Factory.Value.StateConfig, new BufferingStateSink("test")), horizon: 30);

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
