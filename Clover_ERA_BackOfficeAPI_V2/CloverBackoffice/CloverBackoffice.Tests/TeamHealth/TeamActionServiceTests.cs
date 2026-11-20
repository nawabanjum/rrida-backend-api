using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.TeamHealth
{
    /// <summary>
    /// Unit tests for TeamActionService (Team Health → Action Management). Today is
    /// Tue 2026-09-29. Team 294's engine started 2026-09-01, so engine day N is
    /// 2026-09-(N). Its actions, each taken up ("I'll do it") on the day it was delivered:
    ///   C1 rung 1   day 10   engine closed            → CLOSED by default
    ///   C2 rung 1   day 20   still running            → IN_FLIGHT by default
    /// </summary>
    public partial class TeamActionServiceTests
    {
        private readonly string _db = Guid.NewGuid().ToString();
        private readonly long _closedAction;
        private readonly long _runningAction;

        private const string RunningText = "Ask one person what they keep meaning to think about.";

        public TeamActionServiceTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
            db.EngineTeamSettings.Add(new EngineTeamSettings
            {
                BusinessUnitId = Team, StartDate = new DateOnly(2026, 9, 1), IsEnabled = true, CreatedAt = DateTime.UtcNow
            });
            _closedAction = AddAction(db, Team, "C1", 10, "MA-C-069-21", "Tell one person what they did well.", "closed", "closed");
            _runningAction = AddAction(db, Team, "C2", 20, "MA-R-025-22", RunningText, null, null);
            Acknowledge(db, Team, "C1", new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc));
            Acknowledge(db, Team, "C2", new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc));
        }

        // "I'll do it": what puts an action in flight.
        private static void Acknowledge(ApplicationDbContext db, uint unitId, string cycleKey, DateTime at, int rung = 1, int? by = ManagerOfTeam)
        {
            db.EngineAcknowledgements.Add(new EngineAcknowledgement
            {
                BusinessUnitId = unitId, CycleKey = cycleKey, Rung = rung, AcknowledgedByUserId = by, AcknowledgedAt = at
            });
            db.SaveChanges();
        }

        private static long AddAction(ApplicationDbContext db, uint unitId, string cycleKey, int day, string actionKey,
            string text, string? deliveryOutcome, string? cycleOutcome)
        {
            var cycle = new EngineCycle
            {
                BusinessUnitId = unitId, CycleKey = cycleKey, IssueFileKey = "IF-1", SelectedDay = day,
                DeliveredDay = day, ConfirmationDueDay = day + 3, Outcome = cycleOutcome, WrittenAt = DateTime.UtcNow
            };
            db.EngineCycles.Add(cycle);
            db.SaveChanges();
            var delivery = new EngineCycleDelivery
            {
                CycleId = cycle.Id, Rung = 1, ActionKey = actionKey, DeliveredText = text,
                DeliveredWorkedIf = "They name one thing.", DeliveredDay = day, Outcome = deliveryOutcome
            };
            db.EngineCycleDeliveries.Add(delivery);
            db.SaveChanges();
            return delivery.Id;
        }

        private TeamActionService Service() => Service(CreateContext(_db));

        private static TeamActionService Service(ApplicationDbContext db) =>
            new(db, new TeamHealthAccessService(db, Roles()), new FixedClock(Today));

        private ApplicationDbContext Db() => CreateContext(_db);

        private async Task<TeamActionDto> Ok(Task<TeamMilestoneResult<TeamActionDto>> call)
        {
            var r = await call;
            Assert.True(r.IsSuccess, r.Message);
            return r.Data!;
        }

        private static async Task Fails(Task<TeamMilestoneResult<TeamActionDto>> call, TeamMilestoneError error, string message)
        {
            var r = await call;
            Assert.Equal(error, r.Error);
            Assert.Equal(message, r.Message);
        }

        private Task<TeamMilestoneResult<TeamActionDto>> SetStatus(long id, string status, int? version = null, int user = ProfileAdminA) =>
            Service().UpdateStatusAsync(id.ToString(), new UpdateTeamActionStatusDto { Status = status, Version = version }, user);

        // ── Read ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Get_RunningAction_IsInFlight_FollowingTheEngine()
        {
            var a = await Ok(Service().GetAsync(_runningAction.ToString(), ProfileAdminA));

            Assert.Equal(_runningAction, a.ActionId);
            Assert.Equal("294", a.TeamId);
            Assert.Equal("2026-09-20", a.Date);
            Assert.Equal(RunningText, a.Text);
            Assert.Equal(RunningText, a.OriginalText);
            Assert.False(a.IsEdited);
            Assert.Equal("They name one thing.", a.WorkedIf);
            Assert.Equal(TeamActionStatuses.InFlight, a.Status);
            Assert.Equal("engine", a.StatusSource);
            Assert.Equal("running", a.EngineOutcome);
            Assert.True(a.CanClose);
            Assert.False(a.CanReopen);
            Assert.Equal(0, a.Version);
            Assert.Empty(a.Comments);
        }

        [Fact]
        public async Task Get_EngineResolvedAction_IsClosed()
        {
            var a = await Ok(Service().GetAsync(_closedAction.ToString(), ProfileAdminA));

            Assert.Equal(TeamActionStatuses.Closed, a.Status);
            Assert.Equal("closed", a.EngineOutcome);
            Assert.False(a.CanClose);
            Assert.True(a.CanReopen);
            Assert.Null(a.ClosedBy);   // the engine closed it, not a manager
        }

        // ── Close / reopen ─────────────────────────────────────────────────

        [Fact]
        public async Task Close_ThenReopen_KeepsTheSameAction_AndNeverTouchesTheEngine()
        {
            var closed = await Ok(SetStatus(_runningAction, "CLOSED"));
            Assert.Equal(_runningAction, closed.ActionId);
            Assert.Equal(TeamActionStatuses.Closed, closed.Status);
            Assert.Equal("manager", closed.StatusSource);
            Assert.Equal("running", closed.EngineOutcome);
            Assert.Equal(ProfileAdminA, closed.ClosedBy!.UserId);
            Assert.NotNull(closed.ClosedAt);
            Assert.True(closed.CanReopen);
            Assert.Equal(1, closed.Version);

            var reopened = await Ok(SetStatus(_runningAction, "in_flight"));
            Assert.Equal(_runningAction, reopened.ActionId);
            Assert.Equal(TeamActionStatuses.InFlight, reopened.Status);
            Assert.Null(reopened.ClosedAt);
            Assert.Null(reopened.ClosedBy);
            Assert.Equal(2, reopened.Version);

            using var db = Db();
            Assert.Single(db.TeamActionStates);   // updated in place, never a second record
            Assert.Equal(new[] { "CLOSE", "REOPEN" },
                db.TeamActionAudits.OrderBy(x => x.AuditId).Select(x => x.OperationType));
            var engine = db.EngineCycleDeliveries.Single(d => d.Id == _runningAction);
            Assert.Null(engine.Outcome);
            Assert.Equal(RunningText, engine.DeliveredText);
        }

        [Fact]
        public async Task Reopen_EngineClosedAction_ThenCloseAgain()
        {
            var reopened = await Ok(SetStatus(_closedAction, "IN_FLIGHT"));
            Assert.Equal(TeamActionStatuses.InFlight, reopened.Status);
            Assert.Equal("closed", reopened.EngineOutcome);   // the engine's reading stays as it was

            var closed = await Ok(SetStatus(_closedAction, "CLOSED"));
            Assert.Equal(TeamActionStatuses.Closed, closed.Status);
        }

        [Fact]
        public async Task InvalidTransitions_AreBadRequest()
        {
            await Fails(SetStatus(_closedAction, "CLOSED"), TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionAlreadyClosed);
            await Fails(SetStatus(_runningAction, "IN_FLIGHT"), TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionAlreadyInFlight);
            await Fails(SetStatus(_runningAction, "DONE"), TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionStatusInvalid);
            await Fails(SetStatus(_runningAction, ""), TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionStatusInvalid);

            using var db = Db();
            Assert.Empty(db.TeamActionStates);
            Assert.Empty(db.TeamActionAudits);
        }

        [Fact]
        public async Task StaleVersion_IsConflict_AndChangesNothing()
        {
            await Ok(SetStatus(_runningAction, "CLOSED", version: 0));   // now version 1

            await Fails(SetStatus(_runningAction, "IN_FLIGHT", version: 0), TeamMilestoneError.Conflict, ResponseMessages.Errors.ActionStale);

            var a = await Ok(Service().GetAsync(_runningAction.ToString(), ProfileAdminA));
            Assert.Equal(TeamActionStatuses.Closed, a.Status);
            Assert.Equal(1, a.Version);
        }
    }
}
