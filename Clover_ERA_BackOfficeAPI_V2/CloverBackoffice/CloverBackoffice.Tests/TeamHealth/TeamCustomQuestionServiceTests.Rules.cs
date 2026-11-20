using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.TeamQuestions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.CustomQuestionTestData;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.TeamHealth
{
    public partial class TeamCustomQuestionServiceTests
    {
        // ── Schedule: validation ───────────────────────────────────────────

        [Theory]
        [InlineData(null, ResponseMessages.Errors.CustomQuestionTextRequired)]
        [InlineData("   ", ResponseMessages.Errors.CustomQuestionTextRequired)]
        public async Task Schedule_MissingQuestion_IsBadRequest(string? question, string message)
        {
            AssertBadRequest(await Service().ScheduleAsync(Body(question: question), ProfileAdminA), message);
        }

        [Fact]
        public async Task Schedule_QuestionOver140_IsBadRequest()
        {
            var r = await Service().ScheduleAsync(Body(question: new string('q', 141)), ProfileAdminA);

            AssertBadRequest(r, ResponseMessages.Errors.CustomQuestionTextTooLong);
        }

        [Theory]
        [InlineData(null, ResponseMessages.Errors.CustomQuestionDateRequired)]
        [InlineData("", ResponseMessages.Errors.CustomQuestionDateRequired)]
        [InlineData("next week", ResponseMessages.Errors.MilestoneDateInvalid)]
        [InlineData("31-02-2026", ResponseMessages.Errors.MilestoneDateInvalid)]
        [InlineData("2026-10-03", ResponseMessages.Errors.CustomQuestionWeekend)]   // Saturday
        [InlineData("2026-10-04", ResponseMessages.Errors.CustomQuestionWeekend)]   // Sunday
        [InlineData("2026-09-28", ResponseMessages.Errors.CustomQuestionDatePassed)]
        public async Task Schedule_InvalidDate_IsBadRequest(string? askOn, string message)
        {
            AssertBadRequest(await Service().ScheduleAsync(Body(askOn: askOn), ProfileAdminA), message);
        }

        [Fact]
        public async Task Schedule_TodayAfterCheckIn_IsBadRequest()
        {
            var afterCheckIn = new DateTimeOffset(2026, 9, 29, 16, 30, 0, TimeSpan.Zero);

            var r = await Service(afterCheckIn).ScheduleAsync(Body(askOn: "2026-09-29"), ProfileAdminA);

            AssertBadRequest(r, ResponseMessages.Errors.CustomQuestionTodayTooLate);
        }

        [Fact]
        public async Task Schedule_MissingTeamIdOrBody_IsBadRequest()
        {
            AssertBadRequest(await Service().ScheduleAsync(Body(teamId: 0), ProfileAdminA), ResponseMessages.Errors.TimelineTeamIdInvalid);
            AssertBadRequest(await Service().ScheduleAsync(null, ProfileAdminA), ResponseMessages.Errors.TimelineTeamIdInvalid);
        }

        // ── Schedule: slots and duplicates ─────────────────────────────────

        [Fact]
        public async Task Schedule_FourthQuestion_SlotsFull_NothingSaved()
        {
            using (var db = Db())
            {
                AddQuestion(db, Team, "2026-09-30", "One");
                AddQuestion(db, Team, "2026-10-01", "Two");
                AddQuestion(db, Team, "2026-10-02", "Three");
            }

            var r = await Service().ScheduleAsync(Body(askOn: "2026-10-06"), ProfileAdminA);

            AssertBadRequest(r, ResponseMessages.Errors.CustomQuestionSlotsFull);
            Assert.Equal(3, await Db().CustomQuestions.CountAsync());
        }

        [Fact]
        public async Task Schedule_PastAndCancelledQuestions_DoNotHoldSlots()
        {
            using (var db = Db())
            {
                AddQuestion(db, Team, "2026-09-10", "Past 1", CustomQuestionStatus.Expired);
                AddQuestion(db, Team, "2026-09-11", "Past 2", CustomQuestionStatus.Expired);
                AddQuestion(db, Team, "2026-09-14", "Past 3", CustomQuestionStatus.Expired);
                AddQuestion(db, Team, "2026-10-06", "Pulled", CustomQuestionStatus.Cancelled);
                AddQuestion(db, OtherTeamA, "2026-10-06", "Other team");
            }

            var r = await Service().ScheduleAsync(Body(askOn: "2026-10-06"), ProfileAdminA);

            Assert.True(r.IsSuccess, r.Message);
            Assert.Equal(1, r.Data!.SlotsUsed);
        }

        [Fact]
        public async Task Schedule_SameDayTaken_IsBadRequest()
        {
            using (var db = Db())
                AddQuestion(db, Team, "2026-10-06", "Already that day");

            var r = await Service().ScheduleAsync(Body(askOn: "06-10-2026"), ProfileAdminA);

            AssertBadRequest(r, ResponseMessages.Errors.CustomQuestionDayTaken);
        }

        [Fact]
        public async Task Schedule_SameWordingAlreadyScheduled_IsBadRequest()
        {
            using (var db = Db())
                AddQuestion(db, Team, "2026-10-07", "How was the workshop?");

            var r = await Service().ScheduleAsync(Body(question: "HOW WAS THE WORKSHOP?", askOn: "2026-10-08"), ProfileAdminA);

            AssertBadRequest(r, ResponseMessages.Errors.CustomQuestionDuplicate);
        }

        [Fact]
        public async Task Schedule_SameWordingAskedBefore_IsAllowed()
        {
            using (var db = Db())
                AddQuestion(db, Team, "2026-09-10", "How was the workshop?", CustomQuestionStatus.Expired);

            var r = await Service().ScheduleAsync(Body(question: "How was the workshop?"), ProfileAdminA);

            Assert.True(r.IsSuccess, r.Message);
        }

        // ── Remove ─────────────────────────────────────────────────────────

        [Fact]
        public async Task Remove_Scheduled_CancelsKeepsRowAndAudits()
        {
            var created = await Service().ScheduleAsync(Body(), ProfileAdminA);
            var id = created.Data!.Scheduled.Single().Id;

            var r = await Service().RemoveAsync(id, ManagerOfTeam);

            Assert.True(r.IsSuccess, r.Message);
            Assert.Equal(0, r.Data!.SlotsUsed);
            Assert.Empty(r.Data.Scheduled);

            using var db = Db();
            var row = await db.CustomQuestions.SingleAsync();
            Assert.Equal(CustomQuestionStatus.Cancelled, row.Status);
            Assert.False(row.IsDel);
            Assert.Equal(ManagerOfTeam, row.UpdatedBy);
            var audit = await db.CustomQuestionAudits.OrderBy(a => a.VersionNumber).ToListAsync();
            Assert.Equal(new[] { "INSERT", "CANCEL" }, audit.Select(a => a.OperationType));
            Assert.Equal(2, audit[1].VersionNumber);
        }

        [Fact]
        public async Task Remove_FreesSlotForAnotherQuestionThatDay()
        {
            var created = await Service().ScheduleAsync(Body(), ProfileAdminA);
            await Service().RemoveAsync(created.Data!.Scheduled.Single().Id, ProfileAdminA);

            var again = await Service().ScheduleAsync(Body(question: "A different question"), ProfileAdminA);

            Assert.True(again.IsSuccess, again.Message);
        }

        [Fact]
        public async Task Remove_AlreadyAsked_IsBadRequest()
        {
            uint id;
            using (var db = Db())
                id = AddQuestion(db, Team, "2026-09-10", "Asked", CustomQuestionStatus.Expired);

            AssertBadRequest(await Service().RemoveAsync(id, ProfileAdminA), ResponseMessages.Errors.CustomQuestionNotRemovable);
        }

        [Fact]
        public async Task Remove_AlreadyCancelled_IsBadRequest()
        {
            uint id;
            using (var db = Db())
                id = AddQuestion(db, Team, "2026-10-06", "Pulled", CustomQuestionStatus.Cancelled);

            AssertBadRequest(await Service().RemoveAsync(id, ProfileAdminA), ResponseMessages.Errors.CustomQuestionNotRemovable);
        }

        [Fact]
        public async Task Remove_Answered_IsBadRequest()
        {
            uint id;
            using (var db = Db())
            {
                id = AddQuestion(db, Team, "2026-09-29", "Today");
                AddAnswers(db, id, Team, 1);
            }

            AssertBadRequest(await Service().RemoveAsync(id, ProfileAdminA), ResponseMessages.Errors.CustomQuestionAlreadyAnswered);
        }

        [Fact]
        public async Task Remove_Unknown_IsNotFound()
        {
            Assert.Equal(TeamMilestoneError.NotFound, (await Service().RemoveAsync(9999, ProfileAdminA)).Error);
        }

        // ── Permissions ────────────────────────────────────────────────────

        [Theory]
        [InlineData(ProfileAdminB)]
        [InlineData(PortfolioManagerNoReports)]
        [InlineData(UnassignedManager)]
        public async Task NotPermitted_IsForbiddenEverywhere(int userId)
        {
            uint id;
            using (var db = Db())
                id = AddQuestion(db, Team, "2026-10-06", "Scheduled");

            Assert.Equal(TeamMilestoneError.Forbidden, (await Service().GetAsync("294", userId)).Error);
            Assert.Equal(TeamMilestoneError.Forbidden, (await Service().ScheduleAsync(Body(askOn: "2026-10-07"), userId)).Error);
            Assert.Equal(TeamMilestoneError.Forbidden, (await Service().RemoveAsync(id, userId)).Error);
            Assert.Equal(CustomQuestionStatus.Scheduled, (await Db().CustomQuestions.SingleAsync()).Status);
        }

        [Theory]
        [InlineData(RridaAdmin)]
        [InlineData(ManagerOfParent)]
        [InlineData(PortfolioManagerOfTeam)]
        public async Task PermittedRoles_CanSchedule(int userId)
        {
            Assert.True((await Service().ScheduleAsync(Body(), userId)).IsSuccess);
        }

        [Fact]
        public async Task UnknownOrDeletedTeam_IsNotFound()
        {
            Assert.Equal(TeamMilestoneError.NotFound, (await Service().GetAsync("9999", RridaAdmin)).Error);
            Assert.Equal(TeamMilestoneError.NotFound, (await Service().ScheduleAsync(Body(teamId: DeletedTeam), RridaAdmin)).Error);
        }
    }
}
