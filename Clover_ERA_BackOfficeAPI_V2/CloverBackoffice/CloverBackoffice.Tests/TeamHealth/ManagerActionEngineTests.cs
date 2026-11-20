using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.TeamHealth
{
    /// <summary>
    /// api/actions lists engine actions taken up with "I'll do it" beside the manager's own,
    /// in the same shape, and changes them through the Team Health action service. Team 294:
    ///   C1 day 10, engine closed, taken up      → engine-…, DONE
    ///   C2 day 20, running, taken up by 300     → engine-…, IN_PROGRESS
    ///   C3 day 25, running, never taken up      → not listed
    /// </summary>
    public class ManagerActionEngineTests
    {
        private readonly string _db = Guid.NewGuid().ToString();
        private readonly long _closed;
        private readonly long _running;
        private readonly long _suggested;

        public ManagerActionEngineTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
            db.EngineTeamSettings.Add(new EngineTeamSettings
            {
                BusinessUnitId = Team, StartDate = new DateOnly(2026, 9, 1), IsEnabled = true, CreatedAt = DateTime.UtcNow
            });
            _closed = AddAction(db, "C1", 10, "Tell one person what they did well.", "closed");
            _running = AddAction(db, "C2", 20, "Ask the team what slows handovers.", null);
            _suggested = AddAction(db, "C3", 25, "Never taken up.", null);
            Acknowledge(db, "C1", new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc));
            Acknowledge(db, "C2", new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc));

            db.ManagerActions.Add(new ManagerAction
            {
                ProfileId = ProfileA, UnitId = Team, Title = "Run a retro", Source = ManagerActionSources.Manual,
                Status = ManagerActionStatuses.Open, CreatedBy = ManagerOfTeam,
                CreatedAt = new DateTime(2026, 9, 25, 9, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 9, 25, 9, 0, 0, DateTimeKind.Utc)
            });
            db.SaveChanges();
        }

        private static long AddAction(ApplicationDbContext db, string key, int day, string text, string? outcome)
        {
            var cycle = new EngineCycle
            {
                BusinessUnitId = Team, CycleKey = key, IssueFileKey = "IF-1", SelectedDay = day,
                DeliveredDay = day, ConfirmationDueDay = day + 3, Outcome = outcome, WrittenAt = DateTime.UtcNow
            };
            db.EngineCycles.Add(cycle);
            db.SaveChanges();
            var delivery = new EngineCycleDelivery
            {
                CycleId = cycle.Id, Rung = 1, ActionKey = "MA-X-" + key, DeliveredText = text,
                DeliveredWorkedIf = "They name one thing.", DeliveredDay = day, Outcome = outcome
            };
            db.EngineCycleDeliveries.Add(delivery);
            db.SaveChanges();
            return delivery.Id;
        }

        private static void Acknowledge(ApplicationDbContext db, string key, DateTime at)
        {
            db.EngineAcknowledgements.Add(new EngineAcknowledgement
            {
                BusinessUnitId = Team, CycleKey = key, Rung = 1, AcknowledgedByUserId = ManagerOfTeam, AcknowledgedAt = at
            });
            db.SaveChanges();
        }

        private ManagerActionService Service(ApplicationDbContext db) =>
            new(db, new TeamHealthAccessService(db, Roles()), Roles(), new FixedClock(Today));

        private async Task<TeamMilestoneResult<ManagerActionListDto>> List(ManagerActionQueryDto q, int actor = ManagerOfTeam)
        {
            using var db = CreateContext(_db);
            return await Service(db).ListAsync(q, actor);
        }

        private async Task<TeamMilestoneResult<ManagerActionDto>> Get(string id, int actor = ManagerOfTeam)
        {
            using var db = CreateContext(_db);
            return await Service(db).GetAsync(id, actor);
        }

        private async Task<TeamMilestoneResult<ManagerActionDto>> Patch(string id, UpdateManagerActionDto dto, int actor = ManagerOfTeam)
        {
            using var db = CreateContext(_db);
            return await Service(db).UpdateAsync(id, dto, actor);
        }

        private static string E(long id) => ManagerActionService.EngineIdPrefix + id;

        [Fact]
        public async Task Team_list_has_manual_and_taken_up_engine_actions_in_one_shape()
        {
            var list = (await List(new ManagerActionQueryDto { TeamId = Team })).Data!;

            Assert.Equal(3, list.Total);
            Assert.DoesNotContain(list.Actions, a => a.ActionId == E(_suggested));

            var running = list.Actions.Single(a => a.ActionId == E(_running));
            Assert.Equal(ManagerActionSources.Engine, running.Source);
            Assert.Equal(ManagerActionStatuses.InProgress, running.Status);
            Assert.Equal("Ask the team what slows handovers.", running.Title);
            // Taken up by 300: that manager put it on the team's action list.
            Assert.Equal(ManagerOfTeam, running.CreatedBy);
            Assert.Equal(_running, running.EngineActionId);
            Assert.Equal("running", running.EngineOutcome);
            Assert.Equal(new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc), running.CreatedAt);
            Assert.Equal(Team, running.TeamId);
            Assert.Equal(ProfileA, running.ProfileId);

            var closed = list.Actions.Single(a => a.ActionId == E(_closed));
            Assert.Equal(ManagerActionStatuses.Done, closed.Status);

            var manual = list.Actions.Single(a => a.Source == ManagerActionSources.Manual);
            Assert.Null(manual.EngineActionId);

            // Open ones first, the closed engine action last.
            Assert.Equal(E(_closed), list.Actions[^1].ActionId);
        }

        [Fact]
        public async Task Filters_apply_to_engine_actions()
        {
            Assert.All((await List(new ManagerActionQueryDto { TeamId = Team, Source = "ENGINE" })).Data!.Actions,
                a => Assert.Equal(ManagerActionSources.Engine, a.Source));
            Assert.Equal(2, (await List(new ManagerActionQueryDto { TeamId = Team, Source = "ENGINE" })).Data!.Total);
            Assert.Equal(1, (await List(new ManagerActionQueryDto { TeamId = Team, Source = "MANUAL" })).Data!.Total);

            var done = (await List(new ManagerActionQueryDto { TeamId = Team, Status = "DONE" })).Data!;
            Assert.Equal(E(_closed), Assert.Single(done.Actions).ActionId);

            // createdBy covers both kinds: the manual action 300 created and the two engine
            // actions 300 took up.
            var by300 = (await List(new ManagerActionQueryDto { TeamId = Team, CreatedBy = ManagerOfTeam })).Data!;
            Assert.Equal(3, by300.Total);
            Assert.Equal(2, by300.Actions.Count(a => a.Source == ManagerActionSources.Engine));
            Assert.Equal(0, (await List(new ManagerActionQueryDto { TeamId = Team, CreatedBy = ManagerOfParent })).Data!.Total);
        }

        [Fact]
        public async Task Profile_list_includes_engine_actions_of_reachable_teams_only()
        {
            Assert.Equal(3, (await List(new ManagerActionQueryDto(), ProfileAdminA)).Data!.Total);
            Assert.Equal(3, (await List(new ManagerActionQueryDto { ProfileId = ProfileA }, RridaAdmin)).Data!.Total);
            // 302 is no longer on team 294.
            Assert.Equal(0, (await List(new ManagerActionQueryDto(), UnassignedManager)).Data!.Total);
        }

        [Fact]
        public async Task Details_of_an_engine_action()
        {
            var a = (await Get(E(_running))).Data!;
            Assert.Equal(E(_running), a.ActionId);
            Assert.Equal(ManagerActionSources.Engine, a.Source);

            Assert.Equal(TeamMilestoneError.NotFound, (await Get(E(_suggested))).Error);  // never taken up
            Assert.Equal(TeamMilestoneError.Forbidden, (await Get(E(_running), ProfileAdminB)).Error);
            Assert.Equal(ResponseMessages.Errors.ManagerActionIdInvalid, (await Get("engine-abc")).Message);
        }

        [Fact]
        public async Task Done_closes_it_as_the_managers_record_and_reopen_works()
        {
            var done = (await Patch(E(_running), new UpdateManagerActionDto { Status = "DONE" })).Data!;
            Assert.Equal(ManagerActionStatuses.Done, done.Status);
            Assert.NotNull(done.CompletedAt);
            Assert.Equal("running", done.EngineOutcome); // the engine's own outcome is untouched

            using (var db = CreateContext(_db))
            {
                Assert.Null(db.EngineCycles.Single(c => c.CycleKey == "C2").Outcome);
                Assert.Empty(db.EngineConfirmations);   // api/actions DONE never confirms with the engine
            }

            // Same status again is not an error.
            Assert.True((await Patch(E(_running), new UpdateManagerActionDto { Status = "CANCELLED" })).IsSuccess);

            var reopened = (await Patch(E(_running), new UpdateManagerActionDto { Status = "IN_PROGRESS" })).Data!;
            Assert.Equal(ManagerActionStatuses.InProgress, reopened.Status);
        }

        [Fact]
        public async Task Title_and_due_date_are_the_managers_wording_and_date()
        {
            var a = (await Patch(E(_running), new UpdateManagerActionDto { Title = "Ask about handovers on Monday", DueDate = "2026-10-05" })).Data!;
            Assert.Equal("Ask about handovers on Monday", a.Title);
            Assert.Equal("2026-10-05", a.DueDate);

            var listed = (await List(new ManagerActionQueryDto { TeamId = Team })).Data!.Actions.Single(x => x.ActionId == E(_running));
            Assert.Equal("2026-10-05", listed.DueDate);

            using var db = CreateContext(_db);
            Assert.Equal("Ask the team what slows handovers.", db.EngineCycleDeliveries.Single(d => d.Id == _running).DeliveredText);
        }

        private async Task<TeamMilestoneResult<ManagerActionDto>> Comment(string id, string text, int actor = ManagerOfTeam)
        {
            using var db = CreateContext(_db);
            return await Service(db).AddCommentAsync(id, new AddManagerActionCommentDto { Text = text }, actor);
        }

        [Fact]
        public async Task Engine_action_comment_goes_through_team_health_comments_unchanged()
        {
            var r = await Comment(E(_running), "Asked on Monday.");
            Assert.True(r.IsSuccess, r.Message);
            Assert.Equal(E(_running), r.Data!.ActionId);
            Assert.Equal("Asked on Monday.", Assert.Single(r.Data.Comments).Text);

            var listed = (await List(new ManagerActionQueryDto { TeamId = Team })).Data!.Actions.Single(x => x.ActionId == E(_running));
            Assert.Equal(1, listed.CommentCount);

            using var db = CreateContext(_db);
            Assert.Single(db.TeamActionComments);
            Assert.Empty(db.ManagerActionComments);

            // The engine's rule stays: not taken up, not in flight.
            Assert.Equal(ResponseMessages.Errors.ActionNotInFlight, (await Comment(E(_suggested), "no")).Message);
        }

        [Fact]
        public async Task Manual_action_with_the_same_number_as_a_not_in_flight_engine_action_takes_the_comment()
        {
            // The ticket's failure: a manual action id sent to the engine comments API was read
            // as an engine action that was never taken up. On api/actions they never mix.
            using (var db = CreateContext(_db))
            {
                db.ManagerActions.Add(new ManagerAction
                {
                    Id = _suggested, ProfileId = ProfileA, UnitId = Team, Title = "Same number", Source = ManagerActionSources.Manual,
                    Status = ManagerActionStatuses.Open, CreatedBy = ManagerOfTeam, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
                });
                db.SaveChanges();
            }

            var r = await Comment(_suggested.ToString(), "Saved on the manual action.");
            Assert.True(r.IsSuccess, r.Message);
            Assert.Equal("Same number", r.Data!.Title);
            Assert.Equal(ManagerActionSources.Manual, r.Data.Source);

            using var check = CreateContext(_db);
            Assert.Equal(_suggested, Assert.Single(check.ManagerActionComments).ManagerActionId);
            Assert.Empty(check.TeamActionComments);
        }

        [Fact]
        public async Task Description_cannot_be_changed_on_an_engine_action()
        {
            var r = await Patch(E(_running), new UpdateManagerActionDto { Description = "Not on an engine action" });
            Assert.Equal(TeamMilestoneError.BadRequest, r.Error);
            Assert.Equal(ResponseMessages.Errors.ManagerActionEngineFieldNotEditable, r.Message);

            Assert.Equal(TeamMilestoneError.Forbidden,
                (await Patch(E(_running), new UpdateManagerActionDto { Status = "DONE" }, ProfileAdminB)).Error);
        }
    }
}
