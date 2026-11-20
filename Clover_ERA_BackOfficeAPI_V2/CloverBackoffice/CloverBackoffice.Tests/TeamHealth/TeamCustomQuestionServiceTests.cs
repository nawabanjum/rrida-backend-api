using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.TeamQuestions;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.CustomQuestionTestData;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.TeamHealth
{
    /// <summary>
    /// Unit tests for TeamCustomQuestionService. Today is Tue 2026-09-29 10:00 UTC,
    /// before the 16:00 check-in. Team 294 has 4 members.
    /// </summary>
    public partial class TeamCustomQuestionServiceTests
    {
        private readonly string _db = Guid.NewGuid().ToString();

        public TeamCustomQuestionServiceTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
            SeedCulture(db);
        }

        private TeamCustomQuestionService Service(DateTimeOffset? now = null)
        {
            var db = CreateContext(_db);
            return new TeamCustomQuestionService(db, new TeamHealthAccessService(db, Roles()), new FixedClock(now ?? Today));
        }

        private ApplicationDbContext Db() => CreateContext(_db);

        private static ScheduleCustomQuestionDto Body(string? question = "Is the new handover checklist saving you time yet?",
            string? askOn = "2026-10-06", uint teamId = Team) =>
            new() { TeamId = teamId, Question = question, AskOn = askOn };

        private static void AssertBadRequest(TeamMilestoneResult<TeamCustomQuestionsDto> r, string message)
        {
            Assert.Equal(TeamMilestoneError.BadRequest, r.Error);
            Assert.Equal(message, r.Message);
        }

        // ── Read ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Get_NoQuestions_EmptyListsAndZeroOfThree()
        {
            var r = await Service().GetAsync("294", ProfileAdminA);

            Assert.True(r.IsSuccess, r.Message);
            Assert.Equal("294", r.Data!.TeamId);
            Assert.Equal(3, r.Data.MaxSlots);
            Assert.Equal(0, r.Data.SlotsUsed);
            Assert.Empty(r.Data.Scheduled);
            Assert.Empty(r.Data.PreviouslyAsked);
        }

        [Fact]
        public async Task Get_SplitsScheduledAndPreviouslyAsked_WithResponseRate()
        {
            using (var db = Db())
            {
                AddQuestion(db, Team, "2026-10-08", "Later");
                AddQuestion(db, Team, "2026-09-29", "Today");                                    // today counts as scheduled
                var asked = AddQuestion(db, Team, "2026-09-10", "Asked", CustomQuestionStatus.Expired);
                AddAnswers(db, asked, Team, 3);                                                   // 3 of 4 = 75%
                AddQuestion(db, Team, "2026-09-15", "Swept later", CustomQuestionStatus.Scheduled); // past but not yet expired
                AddQuestion(db, Team, "2026-09-20", "Pulled", CustomQuestionStatus.Cancelled);
                AddQuestion(db, Team, "2026-10-09", "Pulled future", CustomQuestionStatus.Cancelled);
                AddQuestion(db, Team, "2026-09-18", "Never published", CustomQuestionStatus.Draft);
                AddQuestion(db, OtherTeamA, "2026-10-07", "Other team");
            }

            var d = (await Service().GetAsync("294", ProfileAdminA)).Data!;

            Assert.Equal(2, d.SlotsUsed);
            Assert.Equal(new[] { "Today", "Later" }, d.Scheduled.Select(q => q.Question));
            Assert.Equal(new[] { "2026-09-29", "2026-10-08" }, d.Scheduled.Select(q => q.AskOn));
            Assert.All(d.Scheduled, q => Assert.True(q.CanRemove));

            Assert.Equal(new[] { "Swept later", "Asked" }, d.PreviouslyAsked.Select(q => q.Question));
            var a = d.PreviouslyAsked[1];
            Assert.Equal("2026-09-10", a.AskedOn);
            Assert.Equal(3, a.Answers);
            Assert.Equal(4, a.Possible);
            Assert.Equal(75, a.ResponseRate);
            Assert.Equal(new[] { "answer 0", "answer 1", "answer 2" }, a.Responses.Select(r => r.ResponseText));
            Assert.Empty(d.PreviouslyAsked[0].Responses);
        }

        [Fact]
        public async Task Get_PreviouslyAsked_BlankAnswersCountedNotListed()
        {
            using (var db = Db())
            {
                var id = AddQuestion(db, Team, "2026-09-10", "Asked", CustomQuestionStatus.Expired);
                AddAnswers(db, id, Team, 1);
                db.CustomQuestionDailyFeedback.Add(new CustomQuestionDailyFeedback { CustomQuestionId = id, UnitId = Team, FreeText = null });
                db.SaveChanges();
            }

            var a = (await Service().GetAsync("294", ProfileAdminA)).Data!.PreviouslyAsked.Single();

            Assert.Equal(2, a.Answers);
            Assert.Equal("answer 0", Assert.Single(a.Responses).ResponseText);
        }

        [Fact]
        public async Task Get_DateRange_NarrowsPreviouslyAskedOnly_Inclusive()
        {
            using (var db = Db())
            {
                AddQuestion(db, Team, "2026-08-31", "Before range", CustomQuestionStatus.Expired);
                var first = AddQuestion(db, Team, "2026-09-01", "First day", CustomQuestionStatus.Expired);
                AddAnswers(db, first, Team, 2);
                AddQuestion(db, Team, "2026-09-10", "Last day", CustomQuestionStatus.Expired);
                AddQuestion(db, Team, "2026-09-11", "After range", CustomQuestionStatus.Expired);
                AddQuestion(db, Team, "2026-10-08", "Upcoming");
            }

            var d = (await Service().GetAsync("294", ProfileAdminA, "2026-09-01", "10-09-2026")).Data!;

            Assert.Equal(new[] { "Last day", "First day" }, d.PreviouslyAsked.Select(q => q.Question));
            Assert.Equal(2, d.PreviouslyAsked[1].Responses.Count);
            Assert.Equal(new[] { "Upcoming" }, d.Scheduled.Select(q => q.Question)); // slots never narrowed
        }

        [Fact]
        public async Task Get_DateRange_OpenEndedAndLiftsLimit()
        {
            using (var db = Db())
            {
                for (var day = 1; day <= 25; day++)
                    AddQuestion(db, Team, $"2026-08-{day:00}", $"Q{day}", CustomQuestionStatus.Expired);
            }

            Assert.Equal(TeamCustomQuestionService.PreviouslyAskedLimit,
                (await Service().GetAsync("294", ProfileAdminA)).Data!.PreviouslyAsked.Count);
            Assert.Equal(25, (await Service().GetAsync("294", ProfileAdminA, from: "2026-08-01")).Data!.PreviouslyAsked.Count);
            Assert.Equal(5, (await Service().GetAsync("294", ProfileAdminA, to: "2026-08-05")).Data!.PreviouslyAsked.Count);
        }

        [Theory]
        [InlineData("2026-13-01", null, ResponseMessages.Errors.MilestoneDateInvalid)]
        [InlineData(null, "not-a-date", ResponseMessages.Errors.MilestoneDateInvalid)]
        [InlineData("2026-09-10", "2026-09-01", ResponseMessages.Errors.MilestoneDateRangeInvalid)]
        public async Task Get_BadDateRange_IsBadRequest(string? from, string? to, string message)
        {
            AssertBadRequest(await Service().GetAsync("294", ProfileAdminA, from, to), message);
        }

        [Fact]
        public async Task Get_TeamWithoutSize_ResponseRateNull()
        {
            using (var db = Db())
            {
                var id = AddQuestion(db, OtherTeamA, "2026-09-10", "Asked", CustomQuestionStatus.Expired);
                AddAnswers(db, id, OtherTeamA, 2);
            }

            var a = (await Service().GetAsync("295", RridaAdmin)).Data!.PreviouslyAsked.Single();

            Assert.Equal(2, a.Answers);
            Assert.Null(a.Possible);
            Assert.Null(a.ResponseRate);
        }

        [Fact]
        public async Task Get_AnsweredTodayQuestion_CannotBeRemoved()
        {
            using (var db = Db())
            {
                var id = AddQuestion(db, Team, "2026-09-29", "Today");
                AddAnswers(db, id, Team, 1);
            }

            var q = (await Service().GetAsync("294", ProfileAdminA)).Data!.Scheduled.Single();

            Assert.False(q.CanRemove);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("0")]
        public async Task Get_InvalidTeamId_IsBadRequest(string? teamId)
        {
            var r = await Service().GetAsync(teamId, ProfileAdminA);

            AssertBadRequest(r, ResponseMessages.Errors.TimelineTeamIdInvalid);
        }

        // ── Schedule: success ──────────────────────────────────────────────

        [Fact]
        public async Task Schedule_Valid_PersistsScheduledAt1600Utc_WithAudit()
        {
            var r = await Service().ScheduleAsync(Body(), ManagerOfTeam);

            Assert.True(r.IsSuccess, r.Message);
            Assert.Equal(1, r.Data!.SlotsUsed);
            var item = Assert.Single(r.Data.Scheduled);
            Assert.Equal("2026-10-06", item.AskOn);
            Assert.True(item.CanRemove);

            using var db = Db();
            var row = await db.CustomQuestions.SingleAsync();
            Assert.Equal(Team, row.UnitId);
            Assert.Equal(ProfileA, row.ProfileId);
            Assert.Equal(CustomQuestionStatus.Scheduled, row.Status);
            Assert.Equal(new DateTime(2026, 10, 6, 16, 0, 0, DateTimeKind.Utc), row.ScheduledAt);
            Assert.Equal(1u, row.ContentCultureId);
            Assert.Equal(ManagerOfTeam, row.CreatedBy);

            var audit = await db.CustomQuestionAudits.SingleAsync();
            Assert.Equal("INSERT", audit.OperationType);
            Assert.Equal(1, audit.VersionNumber);
            Assert.Equal(row.Id, audit.CustomQuestionId);
        }

        [Theory]
        [InlineData("06-10-2026", "2026-10-06")]
        [InlineData("2026-09-29", "2026-09-29")] // today, before 16:00
        public async Task Schedule_AcceptsFormatsAndToday(string askOn, string expected)
        {
            var r = await Service().ScheduleAsync(Body(askOn: askOn), ProfileAdminA);

            Assert.True(r.IsSuccess, r.Message);
            Assert.Equal(expected, r.Data!.Scheduled.Single().AskOn);
        }

        [Fact]
        public async Task Schedule_TrimsQuestion_And140CharactersIsAllowed()
        {
            var text = new string('q', 140);

            var r = await Service().ScheduleAsync(Body(question: "  " + text + "  "), ProfileAdminA);

            Assert.True(r.IsSuccess, r.Message);
            Assert.Equal(text, r.Data!.Scheduled.Single().Question);
        }
    }
}
