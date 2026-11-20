using System.Runtime.CompilerServices;
using System.Text.Json;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
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
    /// The worksheet part of the MVP's action card (src/server.js cycle card, public/app.js
    /// oneThingBox) and its substitution sentence in the chat (src/engine/chat.js).
    /// </summary>
    public class EngineWorksheetCardTests
    {
        private const uint Unit = 9101;
        private static readonly Lazy<EngineContext> Ctx = new(() => EngineContext.FromKit(Path.Combine(EngineRoot(), "kit", "data")));

        [Theory]
        [InlineData(45, 45, 0, 0, "45 minutes")]
        [InlineData(30, 45, 0, 0, "30 to 45 minutes")]
        [InlineData(30, 45, 2, 0, "30 to 45 minutes, plus 2 piece(s) of work outside the session")]
        [InlineData(7.5, 10, 1, 1, "7.5 to 10 minutes, plus 1 piece(s) of work outside the session, plus 1 session phase(s) the library does not time")]
        public void Run_minutes_read_as_the_MVP_writes_them(double low, double high, int async, int untimed, string expected)
        {
            var minutes = new Worksheets.RunMinutes
            {
                Low = low, High = high, Known = untimed == 0,
                Untimed = Enumerable.Range(0, untimed).Select(i => $"p{i}").ToList(),
                Async = Enumerable.Range(0, async).Select(i => new Phase { Id = $"a{i}", Timing = "async", Worksheet = "X" }).ToList()
            };
            Assert.Equal(expected, EngineResultStore.RunMinutesText(minutes));
        }

        [Fact]
        public async Task Every_rung_one_card_is_the_engines_own_routing()
        {
            var content = 0;
            foreach (var file in Directory.GetFiles(Path.Combine(EngineRoot(), "scenarios"), "team-*.json"))
            {
                var scenario = await EngineScenarioFile.LoadAsync(file, CancellationToken.None);
                var team = Run(scenario);
                foreach (var cycle in Cycles(team).Where(c => c.Routing is not null))
                {
                    var r = cycle.Routing;
                    var card = EngineWorksheetCardReader.Read(EngineResultStore.WorksheetCardJson(r));
                    if (r.Route != "content")
                    {
                        Assert.True(card is null || (card.Worksheets.Count == 0 && card.Contraindications.Count == 0));
                        continue;
                    }

                    content += 1;
                    Assert.NotNull(card);
                    Assert.Equal(r.WorksheetsDelivered.Select(w => (w.Code, w.Title, w.Formerly)),
                        card!.Worksheets.Select(w => (w.Code, w.Title, w.Formerly)));
                    Assert.Equal(r.Contraindications.Select(c => c.Text), card.Contraindications);
                    Assert.Equal(r.RunKind, card.RunKind);
                    Assert.Equal(r.RunMinutes is null ? null : EngineResultStore.RunMinutesText(r.RunMinutes), card.RunMinutesText);
                    Assert.Equal(r.RunMinutes?.Async.Count ?? 0, card.OutsideTheSession.Count);
                    Assert.Equal(r.WorksheetChoice?.Substitution?.Reason, card.Substitution?.Reason);
                }
            }
            Assert.True(content > 0, "no scenario routed a content move");
        }

        [Fact]
        public async Task The_chat_says_which_worksheet_went_out_when_one_was_substituted()
        {
            await using var db = NewDb();
            var worksheet = Ctx.Value.Data.Worksheets.Values.First(w => !string.IsNullOrWhiteSpace(w.Title));
            await SeedMove(db, worksheet.Code, Card(contraindications: [], substitution: new EngineWorksheetSubstitution
            {
                WantedTitle = "The Deeper Exercise", Unmet = ["the first session", "the follow-up"], Reason = "r"
            }), confirmedDay: null);

            var situation = await ChatService(db).SituationAsync(Unit);

            Assert.Contains(
                $"The worksheet they were actually handed is {worksheet.Code} {worksheet.Title}. "
                + "The action text names The Deeper Exercise, which is not what went out: it assumes the first session "
                + "and the follow-up has been run and closed first and this team has not done that. Talk about the "
                + "worksheet they were given, not the one the action names, and say where the named one sits in the "
                + "sequence if it comes up.", situation);
        }

        [Theory]
        [InlineData(true, false, true)]    // stated conditions, not confirmed: "One of those applies"
        [InlineData(true, true, false)]    // confirmed: too late to decline
        [InlineData(false, false, false)]  // nothing stated to decline against
        public async Task Can_decline_follows_the_MVP(bool hasConditions, bool confirmed, bool expected)
        {
            await using var db = NewDb();
            await SeedMove(db, "W1", Card(hasConditions ? ["Someone is in a performance process."] : [], null), confirmedDay: confirmed ? 11 : null);

            var action = await ManagerService(db).GetActiveActionAsync(new User { UserId = 1 }, Unit);

            Assert.Equal(expected, action!.CanDecline);
            Assert.Equal("W1", action.Worksheet!.Worksheets.Single().Code);
        }

        // Manager taps: stored now, taken in by the engine on its next run.

        [Fact]
        public async Task The_card_keeps_Erans_first_wording_of_the_move_and_never_re_reads_it()
        {
            await using var db = NewDb();
            await SeedMove(db, "W1", Card([], null), confirmedDay: null);
            var service = ManagerService(db);

            // Not written yet: no wording, so the delivered action text is the move.
            var before = await service.GetActiveActionAsync(new User { UserId = 1 }, Unit);
            Assert.Null(before!.OneThing);
            Assert.Null(before.WordedOnDay);

            db.EngineFeedItems.AddRange(
                Daily(7, null, "{\"headline\":\"h\",\"body\":\"b\",\"one_thing\":\"\",\"say_this\":\"\",\"what_tells_you\":\"\"}"),
                Daily(8, "C1", "{\"headline\":\"h\",\"body\":\"b\",\"one_thing\":\"Ask one person.\",\"say_this\":\"How is it going?\",\"what_tells_you\":\"They name one thing.\"}"),
                Daily(9, "C1", "{\"headline\":\"h\",\"body\":\"b\",\"one_thing\":\"Ask someone, today.\",\"say_this\":\"Got a minute?\",\"what_tells_you\":\"An answer.\"}"));
            await db.SaveChangesAsync();

            var action = await service.GetActiveActionAsync(new User { UserId = 1 }, Unit);

            Assert.Equal("Ask one person.", action!.OneThing);          // day 8, not day 9's rephrasing
            Assert.Equal("How is it going?", action.SayThis);
            Assert.Equal("They name one thing.", action.WhatTellsYou);
            Assert.Equal(8, action.WordedOnDay);
            // MVP 90fe06d: shown in the team's own working days (every-day week, today is day 21).
            Assert.Equal("13 working days ago", action.WordedWhen);
        }

        [Theory]
        [InlineData(11, "10 working days ago")]
        [InlineData(null, null)]
        public async Task The_card_says_time_in_working_days_never_a_day_number(int? confirmedDay, string? confirmedWhen)
        {
            await using var db = NewDb();
            await SeedMove(db, "W1", Card([], null), confirmedDay);

            var action = await ManagerService(db).GetActiveActionAsync(new User { UserId = 1 }, Unit);

            // Today is day 21 on an every-day week; the confirmation fell due on day 11, so it is
            // due "today" as Team.WorkingDaysAhead says it once the day has come.
            Assert.Equal(confirmedWhen, action!.ConfirmedWhen);
            Assert.Equal("today", action.ConfirmationDueIn);
            Assert.Null(action.WordedWhen);   // no wording latched yet
        }

        [Fact]
        public async Task Once_the_move_has_resolved_its_wording_is_not_given_out_again()
        {
            await using var db = NewDb();
            await SeedMove(db, "W1", Card([], null), confirmedDay: null);
            db.EngineFeedItems.Add(Daily(8, "C1", "{\"headline\":\"h\",\"body\":\"b\",\"one_thing\":\"Ask one person.\",\"say_this\":\"How is it going?\",\"what_tells_you\":\"They name one thing.\"}"));
            var cycle = await db.EngineCycles.SingleAsync();
            cycle.Outcome = "closed";
            await db.SaveChangesAsync();

            var week = await ManagerService(db).GetCycleActionAsync(new User { UserId = 1 }, Unit, cycle.Id);

            // DEVELOPER-GUIDE section 2, rule 3: the latch is cleared when the cycle resolves.
            Assert.Equal("closed", week!.Outcome);
            Assert.Null(week.OneThing);
            Assert.Null(week.SayThis);
            Assert.Null(week.WhatTellsYou);
            Assert.Null(week.WordedOnDay);
        }

        private static EngineFeedItem Daily(int day, string? cycle, string prose) => new()
        {
            BusinessUnitId = Unit, FeedKey = $"{day}.d" + (cycle is null ? "" : $".{cycle}.1"), Day = day, Kind = "daily",
            CycleKey = cycle, Rung = cycle is null ? null : 1, Situation = cycle is null ? "nothing_cleared" : "action_first_day",
            PayloadJson = "{}", Prose = prose, WrittenAt = DateTime.UtcNow
        };

        [Fact]
        public async Task A_confirmation_shows_on_the_card_at_once_and_is_not_replayed_mid_day()
        {
            await using var db = NewDb();
            await SeedMove(db, "W1", Card(["Someone is in a performance process."], null), confirmedDay: null);
            var runner = new Mock<IEngineReplayRunner>();

            var action = await ManagerService(db, runner).ConfirmAsync(new User { UserId = 1 },
                new EngineConfirmRequestDto { BusinessUnitId = Unit, CycleKey = "C1", WorkedIfMet = true });

            Assert.True(action!.IsConfirmed);
            Assert.Equal(21, action.ConfirmedDay);   // today: start + 20
            Assert.False(action.ConfirmationDue);
            Assert.False(action.CanDecline);
            runner.Verify(r => r.RunAsync(It.IsAny<uint>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
            await Assert.ThrowsAsync<InvalidOperationException>(() => ManagerService(db, runner).DeclineAsync(new User { UserId = 1 },
                new EngineDeclineRequestDto { BusinessUnitId = Unit, CycleKey = "C1", Reason = "late" }));
        }

        [Fact]
        public async Task A_decline_closes_the_card_at_once_and_is_not_replayed_mid_day()
        {
            await using var db = NewDb();
            await SeedMove(db, "W1", Card(["Someone is in a performance process."], null), confirmedDay: null);
            var runner = new Mock<IEngineReplayRunner>();
            var service = ManagerService(db, runner);

            var front = await service.DeclineAsync(new User { UserId = 1 },
                new EngineDeclineRequestDto { BusinessUnitId = Unit, CycleKey = "C1", Reason = "One of us is on a plan." });

            Assert.Null(front);   // the declined action is no longer in front
            var cycleId = await db.EngineCycles.Select(c => c.Id).SingleAsync();
            var week = await service.GetCycleActionAsync(new User { UserId = 1 }, Unit, cycleId);
            Assert.Equal("failed", week!.Outcome);
            Assert.False(week.CanDecline);
            Assert.False(week.ConfirmationDue);
            runner.Verify(r => r.RunAsync(It.IsAny<uint>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfirmAsync(new User { UserId = 1 },
                new EngineConfirmRequestDto { BusinessUnitId = Unit, CycleKey = "C1", WorkedIfMet = true }));
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static Team Run(EngineScenarioFile file)
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
            var sim = new Simulation(Ctx.Value, [config],
                new Resolver(StateConfig.Load(Path.Combine(EngineRoot(), "kit", "data", "thresholds.json")), new BufferingStateSink("t")),
                horizon: file.Horizon > 0 ? file.Horizon : 124);
            Simulation.StepResult step;
            do step = sim.Step(); while (!step.AtHorizon);
            return sim.AllTeams()[0];
        }

        private static IEnumerable<Cycle> Cycles(Team team) =>
            team.CycleHistory.Concat(team.ActiveCycle is null ? [] : new[] { team.ActiveCycle })
                .Concat(team.AwaitingRead).Concat(team.PendingRungTwo).GroupBy(c => c.Id).Select(g => g.Last());

        private static string Card(List<string> contraindications, EngineWorksheetSubstitution? substitution) =>
            JsonSerializer.Serialize(new EngineWorksheetCard
            {
                Route = "content",
                Worksheets = [new EngineWorksheetRef { Code = "W1", Title = "One" }],
                RunMinutesText = "45 minutes",
                RunKind = "content",
                Contraindications = contraindications,
                Substitution = substitution
            });

        private static async Task SeedMove(ApplicationDbContext db, string worksheetCode, string cardJson, int? confirmedDay)
        {
            db.EngineTeamSettings.Add(new EngineTeamSettings
            {
                BusinessUnitId = Unit, StartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-20), WorkingWeek = "every-day", IsEnabled = true
            });
            var cycle = new EngineCycle { BusinessUnitId = Unit, CycleKey = "C1", CycleIndex = 1, IssueFileKey = "MA-E-002", Issue = "Blockers", DeliveredDay = 8, ConfirmationDueDay = 11, SignalReadDay = 15 };
            db.EngineCycles.Add(cycle);
            await db.SaveChangesAsync();
            db.EngineCycleDeliveries.Add(new EngineCycleDelivery
            {
                CycleId = cycle.Id, Rung = 1, ActionKey = Ctx.Value.Data.Actions.Keys.First(), Route = "content", DeliveredDay = 8,
                ConfirmedDay = confirmedDay, DeliveredWorksheetCode = worksheetCode, DeliveredWorksheetCardJson = cardJson
            });
            await db.SaveChangesAsync();
        }

        private static ApplicationDbContext NewDb() =>
            new TestApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"worksheet-card-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static Mock<IRridaEngineContextFactory> Engine()
        {
            var engine = new Mock<IRridaEngineContextFactory>();
            engine.Setup(e => e.Context).Returns(Ctx.Value);
            engine.Setup(e => e.Calendar).Returns(Ctx.Value.Calendar);
            return engine;
        }

        private static EngineChatContextService ChatService(ApplicationDbContext db) =>
            new(db, Engine().Object, new EranEngineContextBuilder(db, Engine().Object));

        private static EngineManagerService ManagerService(ApplicationDbContext db, Mock<IEngineReplayRunner>? runner = null)
        {
            var access = new Mock<IUnitAccessService>();
            access.Setup(a => a.GetAccessibleUnitIdsAsync(It.IsAny<User>(), It.IsAny<CancellationToken>())).ReturnsAsync([Unit]);
            return new EngineManagerService(db, Engine().Object, access.Object, (runner ?? new Mock<IEngineReplayRunner>()).Object,
                NullLogger<EngineManagerService>.Instance);
        }

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
