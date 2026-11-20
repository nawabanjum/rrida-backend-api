using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using CloverBackoffice.Services.Implementation;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.TeamHealth
{
    // Edit, comments, access and the timeline dots. Setup in TeamActionServiceTests.cs.
    public partial class TeamActionServiceTests
    {
        private Task<TeamMilestoneResult<TeamActionDto>> Edit(long id, string? text = null, string? dueDate = null, int? version = null) =>
            Service().UpdateAsync(id.ToString(), new UpdateTeamActionDto { Text = text, DueDate = dueDate, Version = version }, ProfileAdminA);

        private Task<TeamMilestoneResult<TeamActionDto>> Comment(long id, string? text, int user = ProfileAdminA) =>
            Service().AddCommentAsync(id.ToString(), new AddTeamActionCommentDto { Text = text }, user);

        // ── Edit ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Edit_TextAndDueDate_KeepsTheDeliveredText()
        {
            var a = await Ok(Edit(_runningAction, "  Ask two people, not one.  ", "2026-10-02"));

            Assert.Equal("Ask two people, not one.", a.Text);
            Assert.Equal(RunningText, a.OriginalText);
            Assert.True(a.IsEdited);
            Assert.Equal("2026-10-02", a.DueDate);
            Assert.Equal(TeamActionStatuses.InFlight, a.Status);   // editing never changes the status
            Assert.Equal("engine", a.StatusSource);
            Assert.Equal(1, a.Version);

            // Only what is sent changes; "" clears the due date.
            a = await Ok(Edit(_runningAction, dueDate: "", version: 1));
            Assert.Equal("Ask two people, not one.", a.Text);
            Assert.Null(a.DueDate);

            // Back to the delivered wording reads as not edited.
            a = await Ok(Edit(_runningAction, RunningText));
            Assert.False(a.IsEdited);
            Assert.Equal(3, a.Version);

            using var db = Db();
            Assert.Equal(new[] { "EDIT", "EDIT", "EDIT" }, db.TeamActionAudits.Select(x => x.OperationType));
            Assert.Equal(RunningText, db.EngineCycleDeliveries.Single(d => d.Id == _runningAction).DeliveredText);
        }

        [Fact]
        public async Task Edit_Validation()
        {
            await Fails(Edit(_runningAction), TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionNothingToUpdate);
            await Fails(Service().UpdateAsync(_runningAction.ToString(), null, ProfileAdminA),
                TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionNothingToUpdate);
            await Fails(Edit(_runningAction, "   "), TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionTextRequired);
            await Fails(Edit(_runningAction, new string('x', 1001)), TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionTextTooLong);
            await Fails(Edit(_runningAction, dueDate: "31-02-2026"), TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionDueDateInvalid);
            await Fails(Edit(_runningAction, dueDate: "2026-09-19"), TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionDueDateBeforeAction);
            await Fails(Edit(_runningAction, "New", version: 5), TeamMilestoneError.Conflict, ResponseMessages.Errors.ActionStale);
        }

        // ── Comments ───────────────────────────────────────────────────────

        [Fact]
        public async Task Comments_OldestFirst_AndKeptThroughCloseAndReopen()
        {
            await Ok(Comment(_runningAction, "Did this in Monday's stand-up."));
            await Ok(SetStatus(_runningAction, "CLOSED"));
            await Ok(Comment(_runningAction, "Second one.", ManagerOfTeam));
            var a = await Ok(SetStatus(_runningAction, "IN_FLIGHT"));

            Assert.Equal(new[] { "Did this in Monday's stand-up.", "Second one." }, a.Comments.Select(c => c.Text));
            Assert.Equal(ProfileAdminA, a.Comments[0].CreatedBy!.UserId);
            Assert.Equal(ManagerOfTeam, a.Comments[1].CreatedBy!.UserId);

            // Comments belong to their own action only.
            var other = await Ok(Service().GetAsync(_closedAction.ToString(), ProfileAdminA));
            Assert.Empty(other.Comments);
        }

        [Fact]
        public async Task Comment_Validation()
        {
            await Fails(Comment(_runningAction, "  "), TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionCommentRequired);
            await Fails(Comment(_runningAction, null), TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionCommentRequired);
            await Fails(Comment(_runningAction, new string('x', 501)), TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionCommentTooLong);
        }

        // ── Errors and access ──────────────────────────────────────────────

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("0")]
        [InlineData("-3")]
        public async Task InvalidActionId_IsBadRequest(string? id) =>
            await Fails(Service().GetAsync(id, ProfileAdminA), TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionIdInvalid);

        [Fact]
        public async Task UnknownAction_IsNotFound()
        {
            await Fails(Service().GetAsync("999999", ProfileAdminA), TeamMilestoneError.NotFound, ResponseMessages.Errors.ActionNotFound);
            await Fails(SetStatus(999999, "CLOSED"), TeamMilestoneError.NotFound, ResponseMessages.Errors.ActionNotFound);
        }

        [Theory]
        [InlineData(ProfileAdminB)]          // another company
        [InlineData(UnassignedManager)]      // no longer on the team
        [InlineData(PortfolioManagerNoReports)]
        public async Task NotPermitted_IsForbidden_ForReadAndEveryWrite(int userId)
        {
            await Fails(Service().GetAsync(_runningAction.ToString(), userId), TeamMilestoneError.Forbidden, ResponseMessages.Errors.ActionAccessDenied);
            await Fails(SetStatus(_runningAction, "CLOSED", user: userId), TeamMilestoneError.Forbidden, ResponseMessages.Errors.ActionAccessDenied);
            await Fails(Comment(_runningAction, "x", userId), TeamMilestoneError.Forbidden, ResponseMessages.Errors.ActionAccessDenied);

            using var db = Db();
            Assert.Empty(db.TeamActionStates);
            Assert.Empty(db.TeamActionComments);
        }

        [Theory]
        [InlineData(RridaAdmin)]
        [InlineData(ManagerOfTeam)]
        [InlineData(ManagerOfParent)]
        [InlineData(PortfolioManagerOfTeam)]
        public async Task PermittedRoles_CanClose(int userId) =>
            Assert.Equal(TeamActionStatuses.Closed, (await Ok(SetStatus(_runningAction, "CLOSED", user: userId))).Status);

        // ── Engine data reset ──────────────────────────────────────────────

        [Fact]
        public async Task RecordLeftFromADifferentAction_IsIgnored()
        {
            using (var db = Db())
            {
                // Same team, cycle and rung, but made against another action before a reset.
                db.TeamActionStates.Add(new TeamActionState
                {
                    ProfileId = ProfileA, UnitId = Team, CycleKey = "C2", Rung = 1, ActionKey = "MA-OLD-01",
                    Text = "Old wording", Status = TeamActionStatuses.Closed, Version = 4
                });
                db.SaveChanges();
            }

            var a = await Ok(Service().GetAsync(_runningAction.ToString(), ProfileAdminA));
            Assert.Equal(RunningText, a.Text);
            Assert.Equal(TeamActionStatuses.InFlight, a.Status);
            Assert.Equal(0, a.Version);

            a = await Ok(SetStatus(_runningAction, "CLOSED", version: 0));
            Assert.Equal(TeamActionStatuses.Closed, a.Status);
            Assert.Equal(RunningText, a.Text);
            Assert.Equal(1, a.Version);
        }

        // ── Timeline dots ──────────────────────────────────────────────────

        [Fact]
        public async Task TimelineActions_InsideWindow_ShowTheManagersStatusAndCommentCount()
        {
            await Ok(SetStatus(_runningAction, "CLOSED"));
            await Ok(Edit(_runningAction, "Edited wording"));
            await Ok(Comment(_runningAction, "One"));
            await Ok(Comment(_runningAction, "Two"));

            using var db = Db();
            var dots = await TeamActionService.LoadTimelineActionsAsync(db, Team, new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 29), default);

            Assert.Equal(new[] { _closedAction, _runningAction }, dots.Select(d => d.ActionId));
            Assert.Equal(new[] { "2026-09-10", "2026-09-20" }, dots.Select(d => d.Date));
            Assert.Equal(TeamActionStatuses.Closed, dots[1].Status);
            Assert.Equal("running", dots[1].EngineOutcome);
            Assert.Equal("Edited wording", dots[1].Text);
            Assert.Equal(2, dots[1].CommentCount);
            Assert.Equal(0, dots[0].CommentCount);
            Assert.Equal(new[] { "One", "Two" }, dots[1].Comments.Select(c => c.Text));
            Assert.Equal(ProfileAdminA, dots[1].Comments[0].CreatedBy!.UserId);
            Assert.Empty(dots[0].Comments);

            // Outside the window, or a team with no engine, has no dots.
            Assert.Single(await TeamActionService.LoadTimelineActionsAsync(db, Team, new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 29), default));
            Assert.Empty(await TeamActionService.LoadTimelineActionsAsync(db, OtherTeamA, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 29), default));
        }

        // ── List by team and range ─────────────────────────────────────────

        [Fact]
        public async Task List_TeamOnly_ReturnsEveryAction_RangeFilters()
        {
            await Ok(SetStatus(_runningAction, "CLOSED"));

            var all = (await Service().ListAsync("294", null, null, ProfileAdminA)).Data!;
            Assert.Equal("294", all.TeamId);
            Assert.Null(all.From);
            Assert.Null(all.To);
            Assert.Equal(new[] { _closedAction, _runningAction }, all.Actions.Select(a => a.ActionId));
            Assert.Equal(TeamActionStatuses.Closed, all.Actions[1].Status);

            // Inclusive on both ends, either format.
            var range = (await Service().ListAsync("294", "2026-09-10", "19-09-2026", ProfileAdminA)).Data!;
            Assert.Equal("2026-09-10", range.From);
            Assert.Equal("2026-09-19", range.To);
            Assert.Equal(new[] { _closedAction }, range.Actions.Select(a => a.ActionId));

            Assert.Equal(new[] { _runningAction },
                (await Service().ListAsync("294", "2026-09-11", null, ProfileAdminA)).Data!.Actions.Select(a => a.ActionId));
            Assert.Equal(new[] { _closedAction },
                (await Service().ListAsync("294", null, "2026-09-10", ProfileAdminA)).Data!.Actions.Select(a => a.ActionId));
            Assert.Empty((await Service().ListAsync("295", null, null, RridaAdmin)).Data!.Actions);
        }

        [Fact]
        public async Task List_Errors()
        {
            async Task Is(string? team, string? from, string? to, int user, TeamMilestoneError error, string message)
            {
                var r = await Service().ListAsync(team, from, to, user);
                Assert.Equal(error, r.Error);
                Assert.Equal(message, r.Message);
            }

            await Is(null, null, null, ProfileAdminA, TeamMilestoneError.BadRequest, ResponseMessages.Errors.TimelineTeamIdInvalid);
            await Is("abc", null, null, ProfileAdminA, TeamMilestoneError.BadRequest, ResponseMessages.Errors.TimelineTeamIdInvalid);
            await Is("294", "31-02-2026", null, ProfileAdminA, TeamMilestoneError.BadRequest, ResponseMessages.Errors.MilestoneDateInvalid);
            await Is("294", "2026-09-20", "2026-09-10", ProfileAdminA, TeamMilestoneError.BadRequest, ResponseMessages.Errors.MilestoneDateRangeInvalid);
            await Is("999", null, null, ProfileAdminA, TeamMilestoneError.NotFound, ResponseMessages.Errors.MilestoneTeamNotFound);
            await Is("294", null, null, ProfileAdminB, TeamMilestoneError.Forbidden, ResponseMessages.Errors.ActionAccessDenied);
        }

        [Fact]
        public async Task TimelineApi_ReturnsTheActions()
        {
            using var db = Db();
            var timeline = new TeamHealthTimelineService(db, new TeamHealthAccessService(db, Roles()), TimelineTestData.Thresholds(), new FixedClock(Today));

            var t = (await timeline.GetTimelineAsync("294", ProfileAdminA)).Data!;

            // Period 2026-08-31 .. 2026-09-29: both actions are inside it.
            Assert.Equal(new[] { _closedAction, _runningAction }, t.Timeline.Actions.Select(a => a.ActionId));
            Assert.Equal(new[] { TeamActionStatuses.Closed, TeamActionStatuses.InFlight }, t.Timeline.Actions.Select(a => a.Status));
        }
    }
}
