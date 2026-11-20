using System.Net;
using System.Net.Http.Json;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Moq;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.TeamHealth
{
    // HTTP tests for POST /api/teams/send-calendar-invite, on the shared test host.
    public partial class TeamHealthMilestonesApiTests
    {
        private const string InviteRoute = "/api/teams/send-calendar-invite";

        private readonly Mock<IQuestionService> _questions = new();

        private void InviteDelivers(bool emailSent) =>
            _questions.Setup(q => q.SendTeamCalendarInviteEmailAsync(It.IsAny<uint>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()))
                .ReturnsAsync(new DailyQuestionCalendarInviteEmailResultDto { EmailSent = emailSent });

        private Task<HttpResponseMessage> SendInvite(object body, int? asUser) =>
            Send(HttpMethod.Post, InviteRoute, asUser, JsonContent.Create(body));

        [Fact]
        public async Task SendCalendarInvite_Returns200_ToTheCallersOwnEmail()
        {
            InviteDelivers(true);

            var r = await SendInvite(new { teamId = Team }, ManagerOfTeam);

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var json = await Json(r);
            Assert.True(json.GetProperty("isSuccess").GetBoolean());
            Assert.Equal(ResponseMessages.Success.TeamCalendarInviteSent, json.GetProperty("message").GetString());
            var c = json.GetProperty("content");
            Assert.Equal(Team, c.GetProperty("teamId").GetUInt32());
            Assert.Equal("Team 294", c.GetProperty("teamName").GetString());
            Assert.Equal("user300@yopmail.com", c.GetProperty("email").GetString());
            Assert.True(c.GetProperty("invitationSent").GetBoolean());
        }

        [Fact]
        public async Task SendCalendarInvite_IgnoresAnEmailInTheBody()
        {
            InviteDelivers(true);

            await SendInvite(new { teamId = Team, email = "someone.else@example.com" }, ManagerOfTeam);

            _questions.Verify(q => q.SendTeamCalendarInviteEmailAsync(Team, "Team 294", "user300@yopmail.com", 0), Times.Once);
        }

        [Fact]
        public async Task SendCalendarInvite_PassesAZeroReminderThrough()
        {
            InviteDelivers(true);

            var r = await SendInvite(new { teamId = Team, reminderMinutes = 0 }, ManagerOfTeam);

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            _questions.Verify(q => q.SendTeamCalendarInviteEmailAsync(Team, "Team 294", "user300@yopmail.com", 0), Times.Once);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(10081)]
        public async Task SendCalendarInvite_RefusesAReminderOutOfRange(int minutes)
        {
            InviteDelivers(true);

            var r = await SendInvite(new { teamId = Team, reminderMinutes = minutes }, ManagerOfTeam);

            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            _questions.Verify(
                q => q.SendTeamCalendarInviteEmailAsync(It.IsAny<uint>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
                Times.Never);
        }

        [Fact]
        public async Task SendCalendarInvite_Repeat_Returns200_WithNothingSent()
        {
            InviteDelivers(true);
            await SendInvite(new { teamId = Team }, ManagerOfTeam);

            var r = await SendInvite(new { teamId = Team }, ManagerOfTeam);

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var json = await Json(r);
            Assert.Equal(ResponseMessages.Success.TeamCalendarInviteAlreadySent, json.GetProperty("message").GetString());
            Assert.False(json.GetProperty("content").GetProperty("invitationSent").GetBoolean());
        }

        [Fact]
        public async Task SendCalendarInvite_Errors()
        {
            InviteDelivers(true);

            Assert.Equal(HttpStatusCode.Unauthorized, (await SendInvite(new { teamId = Team }, asUser: null)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await SendInvite(new { teamId = 0 }, ManagerOfTeam)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await SendInvite(new { teamId = "abc" }, ManagerOfTeam)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await SendInvite(new { teamId = 9999 }, ManagerOfTeam)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendInvite(new { teamId = OtherTeamA }, ManagerOfTeam)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendInvite(new { teamId = TeamB }, ManagerOfTeam)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendInvite(new { teamId = Team }, ProfileAdminA)).StatusCode);
            _questions.Verify(
                q => q.SendTeamCalendarInviteEmailAsync(It.IsAny<uint>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()),
                Times.Never);
        }

        private void EventFor(uint unitId) =>
            _questions.Setup(q => q.GetDailyQuestionCalendarEventAsync(null, unitId))
                .ReturnsAsync(() => new DailyQuestionCalendarEventDto
                {
                    TeamCode = "abc123",
                    QuestionText = "Did you feel heard?",
                    QuestionLink = "https://feedback.test/abc123",
                    EventStartLocal = new DateTime(2026, 10, 30, 17, 0, 0),
                    EventEndLocal = new DateTime(2026, 10, 30, 17, 15, 0)
                });

        [Fact]
        public async Task GetCalendarInvite_ZeroReminder_IsWrittenToTheEditableDownload()
        {
            EventFor(Team);

            var r = await Send(HttpMethod.Get, $"/api/daily-question/calendar-invite?unitId={Team}&reminderMinutes=0", ManagerOfTeam);

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var c = (await Json(r)).GetProperty("content");
            Assert.Equal(0, c.GetProperty("reminderMinutes").GetInt32());
            var ics = c.GetProperty("calendarContent").GetString()!.Replace("\r\n ", string.Empty);
            Assert.Contains("METHOD:PUBLISH", ics);
            Assert.DoesNotContain("ORGANIZER", ics);
            Assert.Contains("UID:abc123-weekdays-download@dailyquestion.rrida.com", ics);
            Assert.Contains("TRIGGER:PT0M", ics);
        }

        [Fact]
        public async Task GetCalendarInvite_FileDownload_CarriesTheReminder()
        {
            EventFor(Team);

            var r = await Send(HttpMethod.Get, $"/api/daily-question/calendar-invite?unitId={Team}&type=true&reminderMinutes=30", ManagerOfTeam);

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal("text/calendar", r.Content.Headers.ContentType!.MediaType);
            var ics = await r.Content.ReadAsStringAsync();
            Assert.Contains("TRIGGER:-PT30M", ics);
            Assert.Contains("METHOD:PUBLISH", ics);
        }

        [Fact]
        public async Task GetCalendarInvite_NoReminder_IsTheStandardCheckInWithAZeroMinuteAlarm()
        {
            EventFor(Team);

            var r = await Send(HttpMethod.Get, $"/api/daily-question/calendar-invite?unitId={Team}", ManagerOfTeam);

            var c = (await Json(r)).GetProperty("content");
            Assert.Equal(0, c.GetProperty("reminderMinutes").GetInt32());
            Assert.Equal("RRIDA Daily Check-in", c.GetProperty("subject").GetString());
            Assert.DoesNotContain("Did you feel heard?", c.GetProperty("description").GetString());
            var ics = c.GetProperty("calendarContent").GetString()!.Replace("\r\n ", string.Empty);
            Assert.Contains("\r\nSUMMARY:RRIDA Daily Check-in\r\n", ics);
            Assert.DoesNotContain("Did you feel heard?", ics);
            Assert.Contains("TRIGGER:PT0M", ics);
            Assert.Contains("TRANSP:TRANSPARENT", ics);
            Assert.Contains("X-MICROSOFT-CDO-BUSYSTATUS:FREE", ics);
            Assert.Contains("RRULE:FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR", ics);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(10081)]
        public async Task GetCalendarInvite_ReminderOutOfRange_Returns400(int minutes)
        {
            EventFor(Team);

            var r = await Send(HttpMethod.Get, $"/api/daily-question/calendar-invite?unitId={Team}&reminderMinutes={minutes}", ManagerOfTeam);

            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }

        [Fact]
        public async Task SendCalendarInvite_DeliveryFailure_Returns500()
        {
            InviteDelivers(false);

            var r = await SendInvite(new { teamId = Team }, ManagerOfTeam);

            Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode);
            var json = await Json(r);
            Assert.False(json.GetProperty("isSuccess").GetBoolean());
            Assert.Equal(ResponseMessages.Errors.TeamCalendarInviteNotDelivered, json.GetProperty("message").GetString());
        }
    }
}
