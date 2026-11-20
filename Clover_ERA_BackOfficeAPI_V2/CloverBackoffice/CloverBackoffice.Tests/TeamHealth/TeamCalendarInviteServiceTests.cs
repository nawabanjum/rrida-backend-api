using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.TeamHealth
{
    /// <summary>Unit tests for POST api/teams/send-calendar-invite.</summary>
    public class TeamCalendarInviteServiceTests
    {
        private readonly string _db = Guid.NewGuid().ToString();
        private readonly Mock<IQuestionService> _questions = new();
        private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

        public TeamCalendarInviteServiceTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
            Delivers(true);
        }

        private void Delivers(bool emailSent) =>
            _questions.Setup(q => q.SendTeamCalendarInviteEmailAsync(It.IsAny<uint>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()))
                .ReturnsAsync(new DailyQuestionCalendarInviteEmailResultDto { EmailSent = emailSent });

        private TeamCalendarInviteService Service(IUserRoleAssignmentService? roles = null)
        {
            var db = CreateContext(_db);
            return new TeamCalendarInviteService(db, new TeamHealthAccessService(db, roles ?? Roles()), _questions.Object, _cache);
        }

        private void AssertSends(Times times) =>
            _questions.Verify(
                q => q.SendTeamCalendarInviteEmailAsync(It.IsAny<uint>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
                times);

        private void AssertFailed(TeamMilestoneResult<TeamCalendarInviteResultDto> result, TeamMilestoneError error, string message)
        {
            Assert.Equal(error, result.Error);
            Assert.Equal(message, result.Message);
        }

        [Fact]
        public async Task Sends_to_the_registered_email_of_the_logged_in_manager()
        {
            var result = await Service().SendAsync(Team, ManagerOfTeam);

            Assert.True(result.IsSuccess);
            Assert.Equal(Team, result.Data!.TeamId);
            Assert.Equal($"Team {Team}", result.Data.TeamName);
            Assert.Equal($"user{ManagerOfTeam}@yopmail.com", result.Data.Email);
            Assert.True(result.Data.InvitationSent);
            // No reminder asked for = the standard 0 minutes.
            _questions.Verify(q => q.SendTeamCalendarInviteEmailAsync(Team, $"Team {Team}", $"user{ManagerOfTeam}@yopmail.com", 0), Times.Once);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(15)]
        [InlineData(10080)]
        public async Task Passes_the_reminder_through_and_zero_is_not_dropped(int minutes)
        {
            var result = await Service().SendAsync(Team, ManagerOfTeam, minutes);

            Assert.True(result.IsSuccess);
            _questions.Verify(q => q.SendTeamCalendarInviteEmailAsync(Team, $"Team {Team}", $"user{ManagerOfTeam}@yopmail.com", minutes), Times.Once);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(10081)]
        public async Task Refuses_a_reminder_out_of_range(int minutes)
        {
            var result = await Service().SendAsync(Team, ManagerOfTeam, minutes);

            AssertFailed(result, TeamMilestoneError.BadRequest, ResponseMessages.Errors.CalendarReminderMinutesInvalid);
            AssertSends(Times.Never());
        }

        [Fact]
        public async Task Asking_again_with_a_different_reminder_sends_the_updated_invite()
        {
            await Service().SendAsync(Team, ManagerOfTeam, 15);
            await Service().SendAsync(Team, ManagerOfTeam, 0);
            var repeat = await Service().SendAsync(Team, ManagerOfTeam, 0);

            Assert.False(repeat.Data!.InvitationSent);
            AssertSends(Times.Exactly(2));
        }

        [Fact]
        public async Task A_manager_of_the_parent_team_reaches_the_team_below()
        {
            var result = await Service().SendAsync(Team, ManagerOfParent);

            Assert.True(result.IsSuccess);
            Assert.Equal($"user{ManagerOfParent}@yopmail.com", result.Data!.Email);
        }

        [Theory]
        [InlineData(RridaAdmin)]
        [InlineData(ProfileAdminA)]
        [InlineData(PortfolioManagerOfTeam)]
        [InlineData(UnassignedManager)]
        [InlineData(ProfileAdminB)]
        public async Task Only_a_team_manager_of_the_team_may_ask(int userId)
        {
            var result = await Service().SendAsync(Team, userId);

            AssertFailed(result, TeamMilestoneError.Forbidden, ResponseMessages.Errors.TeamCalendarInviteForbidden);
            AssertSends(Times.Never());
        }

        [Fact]
        public async Task A_manager_cannot_ask_for_a_team_they_do_not_manage()
        {
            var result = await Service().SendAsync(OtherTeamA, ManagerOfTeam);

            AssertFailed(result, TeamMilestoneError.Forbidden, ResponseMessages.Errors.TeamCalendarInviteForbidden);
            AssertSends(Times.Never());
        }

        [Fact]
        public async Task A_second_role_does_not_widen_a_manager_to_other_teams()
        {
            var roles = new Mock<IUserRoleAssignmentService>();
            roles.Setup(r => r.GetRoleIdsAsync(ManagerOfTeam, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<int> { (int)UserGroups.ProfileAdmin, (int)UserGroups.TeamManager });

            var result = await Service(roles.Object).SendAsync(OtherTeamA, ManagerOfTeam);

            AssertFailed(result, TeamMilestoneError.Forbidden, ResponseMessages.Errors.TeamCalendarInviteForbidden);
        }

        [Fact]
        public async Task Refuses_a_zero_team_id() =>
            AssertFailed(await Service().SendAsync(0, ManagerOfTeam), TeamMilestoneError.BadRequest, ResponseMessages.Errors.TimelineTeamIdInvalid);

        [Theory]
        [InlineData(9999u)]
        [InlineData(DeletedTeam)]
        public async Task Refuses_a_team_that_does_not_exist(uint teamId) =>
            AssertFailed(await Service().SendAsync(teamId, ManagerOfTeam), TeamMilestoneError.NotFound, ResponseMessages.Errors.MilestoneTeamNotFound);

        [Theory]
        [InlineData("")]
        [InlineData("not-an-email")]
        public async Task Refuses_an_account_with_no_valid_email(string username)
        {
            using (var db = CreateContext(_db))
            {
                db.Users.Single(u => u.UserId == ManagerOfTeam).Username = username;
                db.SaveChanges();
            }

            var result = await Service().SendAsync(Team, ManagerOfTeam);

            AssertFailed(result, TeamMilestoneError.BadRequest, ResponseMessages.Errors.TeamCalendarInviteEmailInvalid);
            AssertSends(Times.Never());
        }

        [Fact]
        public async Task A_repeat_within_the_window_does_not_mail_again()
        {
            await Service().SendAsync(Team, ManagerOfTeam);

            var repeat = await Service().SendAsync(Team, ManagerOfTeam);

            Assert.True(repeat.IsSuccess);
            Assert.False(repeat.Data!.InvitationSent);
            AssertSends(Times.Once());
        }

        [Fact]
        public async Task Simultaneous_requests_mail_once()
        {
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Service().SendAsync(Team, ManagerOfTeam)));

            AssertSends(Times.Once());
        }

        [Fact]
        public async Task The_window_is_per_manager_and_per_team()
        {
            await Service().SendAsync(Team, ManagerOfTeam);
            await Service().SendAsync(Team, ManagerOfParent);
            await Service().SendAsync(ParentUnit, ManagerOfParent);

            AssertSends(Times.Exactly(3));
        }

        [Fact]
        public async Task A_failed_delivery_is_reported_and_can_be_retried()
        {
            Delivers(false);
            var failed = await Service().SendAsync(Team, ManagerOfTeam);
            AssertFailed(failed, TeamMilestoneError.ServerError, ResponseMessages.Errors.TeamCalendarInviteNotDelivered);

            Delivers(true);
            var retry = await Service().SendAsync(Team, ManagerOfTeam);

            Assert.True(retry.Data!.InvitationSent);
            AssertSends(Times.Exactly(2));
        }

        [Fact]
        public async Task A_team_with_no_event_is_reported_as_not_generated()
        {
            _questions.Setup(q => q.SendTeamCalendarInviteEmailAsync(It.IsAny<uint>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()))
                .ReturnsAsync((DailyQuestionCalendarInviteEmailResultDto?)null);

            var result = await Service().SendAsync(Team, ManagerOfTeam);

            AssertFailed(result, TeamMilestoneError.ServerError, ResponseMessages.Errors.TeamCalendarInviteNotGenerated);
        }

        [Fact]
        public async Task A_send_that_throws_can_be_retried()
        {
            _questions.Setup(q => q.SendTeamCalendarInviteEmailAsync(It.IsAny<uint>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()))
                .ThrowsAsync(new HttpRequestException("mail provider down"));
            await Assert.ThrowsAsync<HttpRequestException>(() => Service().SendAsync(Team, ManagerOfTeam));

            Delivers(true);
            var retry = await Service().SendAsync(Team, ManagerOfTeam);

            Assert.True(retry.Data!.InvitationSent);
        }

        [Fact]
        public void The_manager_event_keeps_the_standard_title_and_names_the_team_in_the_description()
        {
            var calendarEvent = NewEvent();
            calendarEvent.TeamName = "Regional care team";
            calendarEvent.EmailSubject = "RRIDA Daily Check-in: Regional care team";

            var calendar = DailyQuestionCalendarBuilder.BuildInvite(
                calendarEvent, DateTime.UtcNow, "noreply@rrida.com", "RRIDA Team", new[] { "manager@example.com" });
            var unfolded = calendar.Replace("\r\n ", string.Empty);

            // The mail subject names the team; the calendar event stays the standard one.
            Assert.Equal("RRIDA Daily Check-in: Regional care team", calendarEvent.Subject);
            Assert.Contains("\r\nSUMMARY:RRIDA Daily Check-in\r\n", unfolded);
            Assert.Contains("DESCRIPTION:Team: Regional care team\\nTeam Code: abc123\\nAnswer here: https://feedback.test/abc123\r\n", unfolded);
            Assert.DoesNotContain("Did you feel heard?", unfolded);
            Assert.DoesNotContain("Does the team share?", unfolded);
            Assert.Contains("DTSTART:20261030T170000", unfolded);
            Assert.Contains("ATTENDEE;CUTYPE=INDIVIDUAL;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE:mailto:manager@example.com", unfolded);

            var link = DailyQuestionCalendarBuilder.BuildEventLink(calendarEvent);
            Assert.Contains("&text=" + Uri.EscapeDataString("RRIDA Daily Check-in") + "&", link);
            Assert.DoesNotContain(Uri.EscapeDataString("Did you feel heard?"), link);
        }

        [Fact]
        public void An_event_with_no_team_name_has_the_standard_title_and_no_team_line()
        {
            var calendarEvent = NewEvent();

            var unfolded = DailyQuestionCalendarBuilder.Build(calendarEvent, DateTime.UtcNow).Replace("\r\n ", string.Empty);

            Assert.Contains("\r\nSUMMARY:RRIDA Daily Check-in\r\n", unfolded);
            Assert.Equal("RRIDA Daily Check-in", calendarEvent.Subject);
            Assert.DoesNotContain("Team: ", DailyQuestionCalendarBuilder.BuildDescription(calendarEvent));
        }

        private static DailyQuestionCalendarEventDto NewEvent() => new()
        {
            TeamCode = "abc123",
            QuestionKey = 121,
            QuestionText = "Did you feel heard?",
            TeamQuestionText = "Does the team share?",
            QuestionLink = "https://feedback.test/abc123",
            EventStartLocal = new DateTime(2026, 10, 30, 17, 0, 0),
            EventEndLocal = new DateTime(2026, 10, 30, 17, 15, 0)
        };
    }
}
