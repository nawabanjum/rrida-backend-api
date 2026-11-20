using System.Runtime.CompilerServices;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using EngineContext = Rrida.Engine.EngineContext;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;
using User = CloverBackoffice.Infrastructure.Models.User;

namespace CloverBackoffice.Tests.TeamHealth
{
    // The timeline's "I did this" (PATCH team-health/actions/{id}/status CLOSED) is the same tap
    // as This Week's Move "I did this": on the cycle's current, open, unconfirmed rung it also
    // records the engine confirmation, so the move card stops offering "I did this".
    // Setup in TeamActionServiceTests.cs (today 2026-09-29, engine start 2026-09-01).
    public partial class TeamActionServiceTests
    {
        private static readonly Lazy<EngineContext> Kit = new(() => EngineContext.FromKit(Path.Combine(EngineRoot(), "kit", "data")));

        private static Mock<IRridaEngineContextFactory> EngineFactory()
        {
            var engine = new Mock<IRridaEngineContextFactory>();
            engine.Setup(e => e.Context).Returns(Kit.Value);
            engine.Setup(e => e.Calendar).Returns(Kit.Value.Calendar);
            return engine;
        }

        // As the controller calls it: the timeline's "I did this".
        private Task<TeamMilestoneResult<TeamActionDto>> DidThis(long id, int user = ProfileAdminA)
        {
            var db = CreateContext(_db);
            var service = new TeamActionService(db, new TeamHealthAccessService(db, Roles()), new FixedClock(Today), EngineFactory().Object);
            return service.UpdateStatusAsync(id.ToString(), new UpdateTeamActionStatusDto { Status = "CLOSED" }, user,
                confirmEngineOnClose: true);
        }

        private static EngineManagerService MoveCard(ApplicationDbContext db)
        {
            var access = new Mock<IUnitAccessService>();
            access.Setup(a => a.GetAccessibleUnitIdsAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<long> { Team });
            return new EngineManagerService(db, EngineFactory().Object, access.Object, new Mock<IEngineReplayRunner>().Object,
                NullLogger<EngineManagerService>.Instance);
        }

        [Fact]
        public async Task DidThis_OnTheRunningMove_ClosesIt_AndConfirmsWithTheEngine()
        {
            var closed = await Ok(DidThis(_runningAction));
            Assert.Equal(TeamActionStatuses.Closed, closed.Status);
            Assert.Equal("manager", closed.StatusSource);
            Assert.Equal("running", closed.EngineOutcome);   // the engine's reading is still its own

            using var db = Db();
            var c = Assert.Single(db.EngineConfirmations);
            Assert.Equal(Team, c.BusinessUnitId);
            Assert.Equal("C2", c.CycleKey);
            Assert.Equal(1, c.Rung);
            Assert.True(c.WorkedIfMet);
            Assert.Equal(ProfileAdminA, c.ConfirmedByUserId);
            Assert.Equal("manager", c.Source);
            Assert.Equal(new DateOnly(2026, 9, 29), c.ConfirmedDate);
            Assert.Equal(29, c.Day);
            Assert.Equal(21, c.AskedDayIndex);   // mon-fri from Tue 1 Sep to Tue 29 Sep
            Assert.Null(db.EngineCycles.Single(x => x.CycleKey == "C2").Outcome);   // nothing resolved by the tap
            Assert.Contains("confirmed with the engine", db.TeamActionAudits.Single().ChangeSummary);

            // This Week's Move: confirmed, so no "I did this" any more.
            var card = await MoveCard(db).GetActiveActionAsync(new User { UserId = ProfileAdminA }, Team);
            Assert.Equal("C2", card!.CycleKey);
            Assert.True(card.IsConfirmed);
            Assert.False(card.ConfirmationDue);
        }

        [Fact]
        public async Task Close_WithoutTheFlag_StaysTheManagersRecordOnly()
        {
            // api/actions PATCH and any other caller: unchanged.
            await Ok(SetStatus(_runningAction, "CLOSED"));

            using var db = Db();
            Assert.Empty(db.EngineConfirmations);
        }

        [Fact]
        public async Task DidThis_AfterTheMoveCardConfirmed_ClosesWithoutASecondConfirmation()
        {
            using (var db = Db())
            {
                db.EngineConfirmations.Add(new EngineConfirmation
                {
                    BusinessUnitId = Team, CycleKey = "C2", Rung = 1, ConfirmedDate = new DateOnly(2026, 9, 28),
                    Day = 28, AskedDayIndex = 20, WorkedIfMet = true, ConfirmedByUserId = ManagerOfTeam, CreatedAt = DateTime.UtcNow
                });
                db.SaveChanges();
            }

            var closed = await Ok(DidThis(_runningAction));
            Assert.Equal(TeamActionStatuses.Closed, closed.Status);

            using var check = Db();
            var c = Assert.Single(check.EngineConfirmations);
            Assert.Equal(ManagerOfTeam, c.ConfirmedByUserId);   // the first tap stands
        }

        [Fact]
        public async Task DidThis_OnADeclinedMove_ClosesOnly()
        {
            using (var db = Db())
            {
                db.EngineDeclines.Add(new EngineDecline
                {
                    BusinessUnitId = Team, CycleKey = "C2", Reason = "Someone is on leave.",
                    DeclinedDate = new DateOnly(2026, 9, 28), Day = 28, AskedDayIndex = 20, CreatedAt = DateTime.UtcNow
                });
                db.SaveChanges();
            }

            await Ok(DidThis(_runningAction));

            using var check = Db();
            Assert.Empty(check.EngineConfirmations);
        }

        [Fact]
        public async Task DidThis_OnAnEngineResolvedAction_ClosesOnly()
        {
            await Ok(SetStatus(_closedAction, "IN_FLIGHT"));
            await Ok(DidThis(_closedAction));

            using var db = Db();
            Assert.Empty(db.EngineConfirmations);
        }

        [Fact]
        public async Task DidThis_OnAnOlderRung_ClosesOnly()
        {
            // C2 laddered: rung 2 is the move now, so rung 1's "I did this" is history.
            using (var db = Db())
            {
                var cycleId = db.EngineCycleDeliveries.Single(d => d.Id == _runningAction).CycleId;
                db.EngineCycleDeliveries.Add(new EngineCycleDelivery
                {
                    CycleId = cycleId, Rung = 2, ActionKey = "MA-R-025-23", DeliveredText = "Rung two.",
                    DeliveredWorkedIf = "They name one thing.", DeliveredDay = 25
                });
                db.SaveChanges();
            }

            await Ok(DidThis(_runningAction));

            using var check = Db();
            Assert.Empty(check.EngineConfirmations);
        }

        [Fact]
        public async Task Reopen_AfterDidThis_KeepsTheConfirmation()
        {
            await Ok(DidThis(_runningAction));
            var reopened = await Ok(SetStatus(_runningAction, "IN_FLIGHT"));
            Assert.Equal(TeamActionStatuses.InFlight, reopened.Status);

            // A confirmation is a fact the engine may already have read: reopening the
            // manager's record does not take it back.
            using var db = Db();
            Assert.Single(db.EngineConfirmations);
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
