using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.TeamHealth
{
    /// <summary>
    /// api/actions: managers create team actions of their own, independent of reports and the
    /// engine. Team 294 (profile 124) is managed by 300; 301 manages its parent; 303 is 300's
    /// Portfolio Manager; 272 is the Profile Admin; 400 is profile B's Profile Admin. Today is
    /// 2026-09-29.
    /// </summary>
    public class ManagerActionServiceTests
    {
        private readonly string _db = Guid.NewGuid().ToString();

        public ManagerActionServiceTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
        }

        private ManagerActionService Service(ApplicationDbContext db) =>
            new(db, new TeamHealthAccessService(db, Roles()), Roles(), new FixedClock(Today));

        private async Task<TeamMilestoneResult<ManagerActionDto>> Create(CreateManagerActionDto dto, int actor = ManagerOfTeam)
        {
            using var db = CreateContext(_db);
            return await Service(db).CreateAsync(dto, actor);
        }

        private async Task<ManagerActionDto> CreateOk(CreateManagerActionDto dto, int actor = ManagerOfTeam)
        {
            var r = await Create(dto, actor);
            Assert.True(r.IsSuccess, r.Message);
            return r.Data!;
        }

        private async Task<TeamMilestoneResult<ManagerActionListDto>> List(ManagerActionQueryDto q, int actor)
        {
            using var db = CreateContext(_db);
            return await Service(db).ListAsync(q, actor);
        }

        private async Task<TeamMilestoneResult<ManagerActionDto>> Get(string id, int actor)
        {
            using var db = CreateContext(_db);
            return await Service(db).GetAsync(id, actor);
        }

        private async Task<TeamMilestoneResult<ManagerActionDto>> Update(string id, UpdateManagerActionDto dto, int actor = ManagerOfTeam)
        {
            using var db = CreateContext(_db);
            return await Service(db).UpdateAsync(id, dto, actor);
        }

        // startDate is required on create: by default the due day (so a past due date stays
        // valid), else today.
        private static CreateManagerActionDto TeamAction(string title = "Run a retro on the release", string? due = "2026-10-05", uint team = Team) =>
            new() { TeamId = team, Title = title, Description = "30 minutes, whole team.", DueDate = due,
                    StartDate = due ?? "2026-09-29" };

        // ── Create ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Manager_creates_a_team_action_recorded_as_its_creator()
        {
            var a = await CreateOk(TeamAction());

            Assert.True(long.Parse(a.ActionId) > 0);
            Assert.Equal(Team, a.TeamId);
            Assert.Equal(ProfileA, a.ProfileId);
            Assert.Equal(ManagerOfTeam, a.CreatedBy);
            Assert.Equal("User 300", a.CreatedByName);
            Assert.Equal("2026-10-05", a.DueDate);
            Assert.Equal(ManagerActionSources.Manual, a.Source);
            Assert.Equal(ManagerActionStatuses.Open, a.Status);
            Assert.False(a.IsOverdue);

            using var db = CreateContext(_db);
            var row = Assert.Single(db.ManagerActions);
            Assert.Equal(ManagerOfTeam, row.CreatedBy);
            Assert.Equal(Team, row.UnitId);
        }

        [Fact]
        public async Task The_creator_is_whoever_is_logged_in()
        {
            var a = await CreateOk(TeamAction(), ProfileAdminA);
            Assert.Equal(ProfileAdminA, a.CreatedBy);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(0u)]
        public async Task TeamId_is_required(uint? teamId)
        {
            var r = await Create(new CreateManagerActionDto { TeamId = teamId, Title = "Something" });
            Assert.Equal(TeamMilestoneError.BadRequest, r.Error);
            Assert.Equal(ResponseMessages.Errors.ManagerActionTeamRequired, r.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("  ")]
        public async Task Title_is_required(string? title)
        {
            var r = await Create(new CreateManagerActionDto { TeamId = Team, Title = title });
            Assert.Equal(TeamMilestoneError.BadRequest, r.Error);
            Assert.Equal(ResponseMessages.Errors.ManagerActionTitleRequired, r.Message);
        }

        [Fact]
        public async Task Title_over_200_and_description_over_2000_are_refused()
        {
            var r = await Create(new CreateManagerActionDto { TeamId = Team, Title = new string('a', 201) });
            Assert.Equal(ResponseMessages.Errors.ManagerActionTitleTooLong, r.Message);
            r = await Create(new CreateManagerActionDto { TeamId = Team, Title = "Ok", Description = new string('a', 2001) });
            Assert.Equal(ResponseMessages.Errors.ManagerActionDescriptionTooLong, r.Message);
        }

        [Fact]
        public async Task Invalid_due_date_is_refused()
        {
            var r = await Create(TeamAction(due: "not a date"));
            Assert.Equal(TeamMilestoneError.BadRequest, r.Error);
            Assert.Equal(ResponseMessages.Errors.ManagerActionDueDateInvalid, r.Message);
        }

        [Theory]
        [InlineData("2026-09-28", "2026-09-28")] // yesterday
        [InlineData("15-01-2025", "2025-01-15")] // long ago, DD-MM-YYYY
        public async Task Past_due_date_is_accepted_and_saved_on_create(string due, string expected)
        {
            var a = await CreateOk(TeamAction(due: due));
            Assert.Equal(expected, a.DueDate);

            using var db = CreateContext(_db);
            var row = await db.ManagerActions.FindAsync(long.Parse(a.ActionId));
            Assert.Equal(DateOnly.Parse(expected), row!.DueDate);
        }

        [Fact]
        public async Task Due_today_future_and_no_due_date_are_fine()
        {
            Assert.Equal("2026-09-29", (await CreateOk(TeamAction(due: "29-09-2026"))).DueDate);
            Assert.Equal("2026-10-05", (await CreateOk(TeamAction(due: "2026-10-05"))).DueDate);
            Assert.Null((await CreateOk(TeamAction(due: null))).DueDate);
        }

        [Theory]
        [InlineData("2026-09-25", "2026-09-25")] // past
        [InlineData("29-09-2026", "2026-09-29")] // today, DD-MM-YYYY
        [InlineData("2026-10-02", "2026-10-02")] // future
        public async Task Start_date_is_saved_on_create_and_returned(string start, string expected)
        {
            var dto = TeamAction(due: "2026-10-10");
            dto.StartDate = start;
            var a = await CreateOk(dto);
            Assert.Equal(expected, a.StartDate);

            using var db = CreateContext(_db);
            var row = await db.ManagerActions.FindAsync(long.Parse(a.ActionId));
            Assert.Equal(DateOnly.Parse(expected), row!.StartDate);

            // GET returns it too.
            Assert.Equal(expected, (await Get(a.ActionId, ManagerOfTeam)).Data!.StartDate);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Missing_start_date_is_asked_for(string? start)
        {
            var dto = TeamAction(due: "2026-10-10");
            dto.StartDate = start;
            var r = await Create(dto);
            Assert.Equal(TeamMilestoneError.BadRequest, r.Error);
            Assert.Equal(ResponseMessages.Errors.ManagerActionStartDateRequired, r.Message);

            using var db = CreateContext(_db);
            Assert.Empty(db.ManagerActions);
        }

        [Fact]
        public async Task Invalid_start_date_is_refused()
        {
            var dto = TeamAction(due: null);
            dto.StartDate = "not a date";
            var r = await Create(dto);
            Assert.Equal(TeamMilestoneError.BadRequest, r.Error);
            Assert.Equal(ResponseMessages.Errors.ManagerActionStartDateInvalid, r.Message);
        }

        [Fact]
        public async Task Due_before_start_is_refused_and_same_day_is_fine()
        {
            var bad = TeamAction(due: "2026-10-01");
            bad.StartDate = "2026-10-02";
            var r = await Create(bad);
            Assert.Equal(TeamMilestoneError.BadRequest, r.Error);
            Assert.Equal(ResponseMessages.Errors.ManagerActionDueBeforeStart, r.Message);

            var same = TeamAction(due: "2026-10-02");
            same.StartDate = "2026-10-02";
            Assert.Equal("2026-10-02", (await CreateOk(same)).StartDate);

            var noDue = TeamAction(due: null);
            noDue.StartDate = "2026-10-02";
            Assert.Equal("2026-10-02", (await CreateOk(noDue)).StartDate);
        }

        [Theory]
        [InlineData(ManagerOfParent)]        // manages the team above
        [InlineData(PortfolioManagerOfTeam)] // 300 reports to them
        [InlineData(ProfileAdminA)]
        [InlineData(RridaAdmin)]
        public async Task Everyone_who_manages_the_team_can_create(int actor) =>
            await CreateOk(TeamAction(), actor);

        [Theory]
        [InlineData(UnassignedManager)]          // no longer on the team
        [InlineData(PortfolioManagerNoReports)]  // nobody on the team reports to them
        [InlineData(ProfileAdminB)]              // another company
        public async Task Others_cannot_create_on_the_team(int actor)
        {
            var r = await Create(TeamAction(), actor);
            Assert.Equal(TeamMilestoneError.Forbidden, r.Error);
        }

        [Fact]
        public async Task Unknown_or_deleted_team_is_not_found()
        {
            var r = await Create(new CreateManagerActionDto { TeamId = DeletedTeam, Title = "x", StartDate = "2026-09-29" });
            Assert.Equal(TeamMilestoneError.NotFound, r.Error);
        }

        // ── Read ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Team_list_and_details_follow_team_access()
        {
            var a = await CreateOk(TeamAction());

            var list = await List(new ManagerActionQueryDto { TeamId = Team }, PortfolioManagerOfTeam);
            Assert.True(list.IsSuccess);
            Assert.Equal(a.ActionId, Assert.Single(list.Data!.Actions).ActionId);

            Assert.True((await Get(a.ActionId, ProfileAdminA)).IsSuccess);
            Assert.Equal(TeamMilestoneError.Forbidden, (await Get(a.ActionId, ProfileAdminB)).Error);
            Assert.Equal(TeamMilestoneError.Forbidden, (await Get(a.ActionId, UnassignedManager)).Error);
            Assert.Equal(TeamMilestoneError.Forbidden, (await List(new ManagerActionQueryDto { TeamId = Team }, ProfileAdminB)).Error);
            Assert.Equal(TeamMilestoneError.NotFound, (await Get("999", ProfileAdminA)).Error);
            Assert.Equal(TeamMilestoneError.BadRequest, (await Get("0", ProfileAdminA)).Error);
        }

        [Fact]
        public async Task Profile_list_shows_only_teams_the_caller_can_open()
        {
            await CreateOk(TeamAction("Team 294 action"));
            await CreateOk(TeamAction("Team 295 action", team: OtherTeamA), ProfileAdminA);

            var admin = (await List(new ManagerActionQueryDto(), ProfileAdminA)).Data!;
            Assert.Equal(2, admin.Total);

            // 300 manages 294 only.
            var manager = (await List(new ManagerActionQueryDto(), ManagerOfTeam)).Data!;
            Assert.Equal("Team 294 action", Assert.Single(manager.Actions).Title);
        }

        [Fact]
        public async Task Rrida_admin_needs_a_profile_or_team_and_filters_apply()
        {
            await CreateOk(TeamAction("A"));
            await CreateOk(TeamAction("B"), ProfileAdminA);

            Assert.Equal(ResponseMessages.Errors.ManagerActionProfileRequired,
                (await List(new ManagerActionQueryDto(), RridaAdmin)).Message);

            var byCreator = (await List(new ManagerActionQueryDto { ProfileId = ProfileA, CreatedBy = ProfileAdminA }, RridaAdmin)).Data!;
            Assert.Equal("B", Assert.Single(byCreator.Actions).Title);

            Assert.Equal(ResponseMessages.Errors.ManagerActionStatusInvalid,
                (await List(new ManagerActionQueryDto { ProfileId = ProfileA, Status = "nope" }, RridaAdmin)).Message);
            Assert.Equal(ResponseMessages.Errors.ManagerActionSourceInvalid,
                (await List(new ManagerActionQueryDto { ProfileId = ProfileA, Source = "nope" }, RridaAdmin)).Message);
            Assert.Equal(2, (await List(new ManagerActionQueryDto { ProfileId = ProfileA, Source = "manual" }, RridaAdmin)).Data!.Total);
        }

        [Fact]
        public async Task Another_profiles_id_is_forbidden_for_a_non_admin()
        {
            var r = await List(new ManagerActionQueryDto { ProfileId = ProfileB }, ProfileAdminA);
            Assert.Equal(TeamMilestoneError.Forbidden, r.Error);
        }

        // ── Update ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Status_done_stamps_completion_and_reopen_clears_it()
        {
            var a = await CreateOk(TeamAction());

            var done = (await Update(a.ActionId, new UpdateManagerActionDto { Status = "done" })).Data!;
            Assert.Equal(ManagerActionStatuses.Done, done.Status);
            Assert.NotNull(done.CompletedAt);
            Assert.Equal(ManagerOfTeam, done.CreatedBy); // the creator never changes

            var reopened = (await Update(a.ActionId, new UpdateManagerActionDto { Status = "IN_PROGRESS" })).Data!;
            Assert.Equal(ManagerActionStatuses.InProgress, reopened.Status);
            Assert.Null(reopened.CompletedAt);

            Assert.Equal(ResponseMessages.Errors.ManagerActionStatusInvalid,
                (await Update(a.ActionId, new UpdateManagerActionDto { Status = "FINISHED" })).Message);
        }

        [Fact]
        public async Task Only_sent_fields_change_and_empty_clears()
        {
            var a = await CreateOk(TeamAction());

            var u = (await Update(a.ActionId, new UpdateManagerActionDto { Title = "Run the retro", DueDate = "", Description = "" })).Data!;
            Assert.Equal("Run the retro", u.Title);
            Assert.Null(u.DueDate);
            Assert.Null(u.Description);
            Assert.Equal(ManagerActionStatuses.Open, u.Status);

            Assert.Equal(ResponseMessages.Errors.ManagerActionNoChange,
                (await Update(a.ActionId, new UpdateManagerActionDto())).Message);
            Assert.Equal(TeamMilestoneError.Forbidden,
                (await Update(a.ActionId, new UpdateManagerActionDto { Status = "DONE" }, ProfileAdminB)).Error);
        }

        // ── Comments ─────────────────────────────────────────────────────────

        private async Task<TeamMilestoneResult<ManagerActionDto>> Comment(string id, string? text, int actor = ManagerOfTeam)
        {
            using var db = CreateContext(_db);
            return await Service(db).AddCommentAsync(id, new AddManagerActionCommentDto { Text = text }, actor);
        }

        [Fact]
        public async Task Open_manual_action_takes_a_comment_without_going_in_flight()
        {
            var a = await CreateOk(TeamAction());
            Assert.Equal(ManagerActionStatuses.Open, a.Status);
            Assert.Empty(a.Comments);

            var r = await Comment(a.ActionId, "  Booked the room for Thursday.  ");
            Assert.True(r.IsSuccess, r.Message);
            var c = Assert.Single(r.Data!.Comments);
            Assert.Equal("Booked the room for Thursday.", c.Text);
            Assert.Equal(ManagerOfTeam, c.CreatedBy!.UserId);
            Assert.Equal("User 300", c.CreatedBy.Name);
            Assert.Equal(1, r.Data.CommentCount);
            Assert.Equal(ManagerActionStatuses.Open, r.Data.Status); // commenting never changes the status

            using var db = CreateContext(_db);
            var row = Assert.Single(db.ManagerActionComments);
            Assert.Equal(long.Parse(a.ActionId), row.ManagerActionId);
            Assert.Empty(db.TeamActionComments); // never stored as an engine action comment
        }

        [Fact]
        public async Task Comment_then_mark_done_keeps_the_comment_and_get_and_list_return_it()
        {
            var a = await CreateOk(TeamAction());
            await Comment(a.ActionId, "First");
            await Comment(a.ActionId, "Second", ProfileAdminA);

            var done = (await Update(a.ActionId, new UpdateManagerActionDto { Status = "DONE" })).Data!;
            Assert.Equal(ManagerActionStatuses.Done, done.Status);
            Assert.Equal(new[] { "First", "Second" }, done.Comments.Select(c => c.Text));

            var got = (await Get(a.ActionId, ManagerOfTeam)).Data!;
            Assert.Equal(new[] { "First", "Second" }, got.Comments.Select(c => c.Text));
            Assert.Equal(ProfileAdminA, got.Comments[1].CreatedBy!.UserId);

            var listed = Assert.Single((await List(new ManagerActionQueryDto { TeamId = Team }, ManagerOfTeam)).Data!.Actions);
            Assert.Equal(2, listed.CommentCount);

            // A done action still takes a comment.
            Assert.Equal(3, (await Comment(a.ActionId, "Went well")).Data!.CommentCount);
        }

        [Fact]
        public async Task Comments_stay_with_their_own_action()
        {
            var a = await CreateOk(TeamAction("A"));
            var b = await CreateOk(TeamAction("B"));
            await Comment(a.ActionId, "On A");

            Assert.Empty((await Get(b.ActionId, ManagerOfTeam)).Data!.Comments);
            Assert.Equal("On A", Assert.Single((await Get(a.ActionId, ManagerOfTeam)).Data!.Comments).Text);
        }

        [Theory]
        [InlineData(null, ResponseMessages.Errors.ActionCommentRequired)]
        [InlineData("   ", ResponseMessages.Errors.ActionCommentRequired)]
        public async Task Empty_comment_is_refused(string? text, string message)
        {
            var a = await CreateOk(TeamAction());
            var r = await Comment(a.ActionId, text);
            Assert.Equal(TeamMilestoneError.BadRequest, r.Error);
            Assert.Equal(message, r.Message);
        }

        [Fact]
        public async Task Comment_over_500_is_refused_and_500_is_fine()
        {
            var a = await CreateOk(TeamAction());
            Assert.Equal(ResponseMessages.Errors.ActionCommentTooLong, (await Comment(a.ActionId, new string('a', 501))).Message);
            Assert.True((await Comment(a.ActionId, new string('a', 500))).IsSuccess);
        }

        [Fact]
        public async Task Comment_follows_team_access_and_ids()
        {
            var a = await CreateOk(TeamAction());

            Assert.True((await Comment(a.ActionId, "ok", PortfolioManagerOfTeam)).IsSuccess);
            Assert.Equal(TeamMilestoneError.Forbidden, (await Comment(a.ActionId, "no", ProfileAdminB)).Error);
            Assert.Equal(TeamMilestoneError.Forbidden, (await Comment(a.ActionId, "no", UnassignedManager)).Error);
            Assert.Equal(TeamMilestoneError.NotFound, (await Comment("999", "no")).Error);
            Assert.Equal(ResponseMessages.Errors.ManagerActionIdInvalid, (await Comment("abc", "no")).Message);

            using var db = CreateContext(_db);
            Assert.Single(db.ManagerActionComments);
        }

        // ── Eran ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Eran_gets_only_open_team_actions_overdue_first()
        {
            var later = await CreateOk(TeamAction("Later one", "2026-10-20"));
            var soon = await CreateOk(TeamAction("Soon one", "2026-09-30"));
            var closed = await CreateOk(TeamAction("Finished one"));
            await Update(closed.ActionId, new UpdateManagerActionDto { Status = "DONE" });

            using (var db = CreateContext(_db))
            {
                // Make "Later one" overdue as of 1 October.
                db.ManagerActions.Single(x => x.Id == long.Parse(later.ActionId)).DueDate = new DateOnly(2026, 9, 30);
                db.ManagerActions.Single(x => x.Id == long.Parse(soon.ActionId)).DueDate = new DateOnly(2026, 10, 3);
                db.SaveChanges();
            }

            var today = new DateOnly(2026, 10, 1);
            using var read = CreateContext(_db);
            var items = await ManagerActionEranContext.OpenAsync(read, Team, today);

            Assert.Equal(new[] { "Later one", "Soon one" }, items.Select(i => i.Title));
            Assert.True(items[0].IsOverdue(today));

            var value = ManagerActionEranContext.ContextValue(items, today)!;
            Assert.Contains("\"Later one\"", value);
            Assert.Contains("overdue", value);
            Assert.Contains("Never make one the one thing", value);
            Assert.DoesNotContain("Finished one", value);

            var block = ManagerActionEranContext.ChatBlock(items, today)!;
            Assert.StartsWith("## THE MANAGER'S OWN ACTIONS", block);
            Assert.Contains("30 minutes, whole team.", block);

            Assert.Null(ManagerActionEranContext.ContextValue([], today));
            Assert.Null(ManagerActionEranContext.ChatBlock([], today));
        }

        [Fact]
        public async Task Eran_chat_block_is_the_teams_open_actions()
        {
            await CreateOk(TeamAction("Run a retro"));

            using var db = CreateContext(_db);
            Assert.Contains("Run a retro", await Service(db).EranChatBlockAsync(Team));
            Assert.Null(await Service(db).EranChatBlockAsync(OtherTeamA));
        }
    }
}
