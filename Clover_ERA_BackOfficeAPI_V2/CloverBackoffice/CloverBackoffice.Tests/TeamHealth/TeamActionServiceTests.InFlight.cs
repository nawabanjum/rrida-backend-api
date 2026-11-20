using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using CloverBackoffice.Services.Implementation;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.TeamHealth
{
    // An action is in flight, with a dot on the timeline, only once a manager has taken it
    // up ("I'll do it"). Setup in TeamActionServiceTests.cs.
    public partial class TeamActionServiceTests
    {
        private const string SuggestedText = "Ask the team what slowed them down this week.";

        private static async Task<List<long>> DotIds(uint unitId, DateOnly from, DateOnly to, string db)
        {
            using var context = CreateContext(db);
            return (await TeamActionService.LoadTimelineActionsAsync(context, unitId, from, to, default))
                .Select(d => d.ActionId).ToList();
        }

        [Fact]
        public async Task SuggestedAction_NotTakenUp_HasNoDot_IsNotListed()
        {
            long suggested;
            using (var db = Db())
            {
                suggested = AddAction(db, Team, "C3", 25, "MA-E-002-22", SuggestedText, null, null);
                // Taken up on another team, or on another rung of this cycle: still not this one.
                Acknowledge(db, OtherTeamA, "C3", new DateTime(2026, 9, 25, 9, 0, 0, DateTimeKind.Utc));
                Acknowledge(db, Team, "C3", new DateTime(2026, 9, 25, 9, 0, 0, DateTimeKind.Utc), rung: 2);
            }

            Assert.Equal(new[] { _closedAction, _runningAction },
                await DotIds(Team, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 29), _db));
            Assert.DoesNotContain(suggested,
                (await Service().ListAsync("294", null, null, ProfileAdminA)).Data!.Actions.Select(a => a.ActionId));

            using var check = Db();
            var timeline = new TeamHealthTimelineService(check, new TeamHealthAccessService(check, Roles()), TimelineTestData.Thresholds(), new FixedClock(Today));
            Assert.DoesNotContain(suggested, (await timeline.GetTimelineAsync("294", ProfileAdminA)).Data!.Timeline.Actions.Select(a => a.ActionId));
        }

        [Fact]
        public async Task SuggestedAction_NotTakenUp_PanelAndWritesAreNotFound_AndChangeNothing()
        {
            long suggested;
            using (var db = Db())
                suggested = AddAction(db, Team, "C3", 25, "MA-E-002-22", SuggestedText, null, null);

            await Fails(Service().GetAsync(suggested.ToString(), ProfileAdminA), TeamMilestoneError.NotFound, ResponseMessages.Errors.ActionNotInFlight);
            await Fails(SetStatus(suggested, "CLOSED"), TeamMilestoneError.NotFound, ResponseMessages.Errors.ActionNotInFlight);
            await Fails(Edit(suggested, "New wording"), TeamMilestoneError.NotFound, ResponseMessages.Errors.ActionNotInFlight);
            await Fails(Comment(suggested, "A comment"), TeamMilestoneError.NotFound, ResponseMessages.Errors.ActionNotInFlight);

            // Access is still checked first: someone with no access is refused, not told it is not in flight.
            await Fails(Service().GetAsync(suggested.ToString(), ProfileAdminB), TeamMilestoneError.Forbidden, ResponseMessages.Errors.ActionAccessDenied);

            using var check = Db();
            Assert.Empty(check.TeamActionStates);
            Assert.Empty(check.TeamActionComments);
            Assert.Empty(check.TeamActionAudits);
        }

        [Fact]
        public async Task TakenUpLater_DotSitsOnTheDayItWasTakenUp_WithItsCycleRungAndManager()
        {
            long action;
            using (var db = Db())
            {
                action = AddAction(db, Team, "C3", 22, "MA-E-002-22", SuggestedText, null, null);
                Acknowledge(db, Team, "C3", new DateTime(2026, 9, 24, 16, 30, 0, DateTimeKind.Utc), by: ManagerOfParent);
            }
            long cycleId;
            using (var db = Db())
                cycleId = db.EngineCycleDeliveries.Single(d => d.Id == action).CycleId;

            using (var db = Db())
            {
                var dots = await TeamActionService.LoadTimelineActionsAsync(db, Team, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 29), default);
                Assert.Equal(new[] { _closedAction, _runningAction, action }, dots.Select(d => d.ActionId));
                var dot = dots[2];
                Assert.Equal("2026-09-24", dot.Date);   // taken up on the 24th, delivered on the 22nd
                Assert.Equal(cycleId, dot.CycleId);
                Assert.Equal(1, dot.Rung);
                Assert.Equal(TeamActionStatuses.InFlight, dot.Status);
                Assert.Equal(new DateTime(2026, 9, 24, 16, 30, 0, DateTimeKind.Utc), dot.AcknowledgedAt);
                Assert.Equal(ManagerOfParent, dot.AcknowledgedBy!.UserId);
                Assert.Equal($"User {ManagerOfParent}", dot.AcknowledgedBy.Name);
            }

            var panel = await Ok(Service().GetAsync(action.ToString(), ProfileAdminA));
            Assert.Equal("2026-09-24", panel.Date);
            Assert.Equal(cycleId, panel.CycleId);
            Assert.Equal(ManagerOfParent, panel.AcknowledgedBy!.UserId);
            Assert.NotNull(panel.AcknowledgedAt);

            // The window is on the day it was taken up, not the day it was delivered.
            Assert.Equal(new[] { action },
                (await Service().ListAsync("294", "2026-09-23", "2026-09-29", ProfileAdminA)).Data!.Actions.Select(a => a.ActionId));
            Assert.Empty((await Service().ListAsync("294", "2026-09-22", "2026-09-22", ProfileAdminA)).Data!.Actions);

            // A due date may not be set before the day it went in flight.
            await Fails(Edit(action, dueDate: "2026-09-23"), TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionDueDateBeforeAction);
            await Ok(Edit(action, dueDate: "2026-09-24"));
        }

        [Fact]
        public async Task LadderedRungTwo_IsItsOwnActionInFlight_OnlyOnceTakenUp()
        {
            long rungTwo;
            using (var db = Db())
            {
                var cycleId = db.EngineCycleDeliveries.Single(d => d.Id == _runningAction).CycleId;
                var delivery = new EngineCycleDelivery
                {
                    CycleId = cycleId, Rung = 2, ActionKey = "MA-R-025-23", DeliveredText = "Change how the stand-up runs.",
                    DeliveredWorkedIf = "They name one thing.", DeliveredDay = 26
                };
                db.EngineCycleDeliveries.Add(delivery);
                db.SaveChanges();
                rungTwo = delivery.Id;
            }
            var from = new DateOnly(2026, 9, 1);
            var to = new DateOnly(2026, 9, 29);

            Assert.Equal(new[] { _closedAction, _runningAction }, await DotIds(Team, from, to, _db));

            using (var db = Db())
                Acknowledge(db, Team, "C2", new DateTime(2026, 9, 26, 9, 0, 0, DateTimeKind.Utc), rung: 2);

            Assert.Equal(new[] { _closedAction, _runningAction, rungTwo }, await DotIds(Team, from, to, _db));
        }
    }
}
