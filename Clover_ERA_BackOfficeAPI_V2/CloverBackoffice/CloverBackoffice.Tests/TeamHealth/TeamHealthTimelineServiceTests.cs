using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.TeamQuestions;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.CustomQuestionTestData;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;
using static CloverBackoffice.Tests.TeamHealth.TimelineTestData;

namespace CloverBackoffice.Tests.TeamHealth
{
    /// <summary>
    /// Unit tests for TeamHealthTimelineService. Today is Tue 2026-09-29, so:
    ///   period            2026-08-31 .. 2026-09-29
    ///   previous period   2026-08-01 .. 2026-08-30
    ///   response rate     Fri 18, Mon 21 .. Fri 25, Mon 28 September (today excluded)
    /// Team 294 has 4 members: 8 possible answers a day, 56 over the window.
    /// </summary>
    public class TeamHealthTimelineServiceTests
    {
        private readonly string _db = Guid.NewGuid().ToString();
        private readonly SaveTracker _tracker = new();

        public TeamHealthTimelineServiceTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
            SeedBanks(db);
        }

        private TeamHealthTimelineService Service() =>
            Service(CreateContext(_db, _tracker));

        private static TeamHealthTimelineService Service(ApplicationDbContext db) =>
            new(db, new TeamHealthAccessService(db, Roles()), Thresholds(), new FixedClock(Today));

        private ApplicationDbContext Db() => CreateContext(_db);

        private async Task<TeamHealthTimelineDto> Get(uint unitId = Team, int userId = ProfileAdminA)
        {
            var result = await Service().GetTimelineAsync(unitId.ToString(), userId);
            Assert.True(result.IsSuccess, result.Message);
            return result.Data!;
        }

        // ── Shape and empty data ───────────────────────────────────────────

        [Fact]
        public async Task NoAnswers_ReturnsPeriodAndNullsNotZeros()
        {
            var t = await Get();

            Assert.Equal("294", t.TeamId);
            Assert.Equal("2026-08-31", t.Timeline.Period.StartDate);
            Assert.Equal("2026-09-29", t.Timeline.Period.EndDate);

            Assert.Null(t.Timeline.IndividualEngagement.Current);
            Assert.Null(t.Timeline.IndividualEngagement.Change);
            Assert.Empty(t.Timeline.IndividualEngagement.Data);
            Assert.Null(t.Timeline.TeamConditions.Current);
            Assert.Empty(t.Timeline.TeamConditions.Data);

            foreach (var d in new[] { t.IndividualEngagementDetails.Communication, t.IndividualEngagementDetails.Learning,
                         t.IndividualEngagementDetails.Opportunities, t.IndividualEngagementDetails.Vulnerability,
                         t.IndividualEngagementDetails.Enablement, t.IndividualEngagementDetails.Reflection })
            {
                Assert.Null(d.Score);
                Assert.Null(d.Status);
                Assert.Null(d.Change);
            }

            foreach (var g in new[] { t.TeamConditionsDetails.DirectionAndLearning, t.TeamConditionsDetails.DesignAndMechanics,
                         t.TeamConditionsDetails.WhatTeamBelieves })
            {
                Assert.Null(g.Score);
                Assert.Null(g.Status);
                Assert.Null(g.Lowest);
            }

            // The team has a size, so an empty week is a real 0%, not missing data.
            Assert.Equal(0, t.ResponseRateDetails.Current);
            Assert.Equal(0, t.ResponseRateDetails.Summary.Answers);
            Assert.Equal(56, t.ResponseRateDetails.Summary.Possible);
            Assert.Empty(t.Timeline.Milestones);

            // The popups' lines: the figures there are, else the wireframe's words alone.
            Assert.Equal($"0 answers from 56 possible. {ResponseRateSummaryDto.Explanation}", t.ResponseRateDetails.Summary.Text);
            Assert.Null(t.TeamConditionsDetails.Summary.LowestGroup);
            Assert.Null(t.TeamConditionsDetails.Summary.LowestScore);
            Assert.Equal(TeamConditionsSummaryDto.Explanation, t.TeamConditionsDetails.Summary.Text);
        }

        [Fact]
        public async Task Legend_FollowsTheThresholds_InBothPopups()
        {
            var t = await Get();

            foreach (var legend in new[] { t.IndividualEngagementDetails.Legend, t.TeamConditionsDetails.Legend })
            {
                Assert.Equal(new[] { "85+ excellent", "65–84 good", "35–64 needs work", "0–34 urgent attention" },
                    legend.Select(b => b.Label));
                Assert.Equal(new[] { ScoreStatuses.Excellent, ScoreStatuses.Good, ScoreStatuses.NeedsWork, ScoreStatuses.UrgentAttention },
                    legend.Select(b => b.Status));
                Assert.Equal(new[] { (85, 100), (65, 84), (35, 64), (0, 34) }, legend.Select(b => (b.Min, b.Max)));
                Assert.Equal(new[] { "#125EA8", "#16a34a", "#d97706", "#b91c1c" }, legend.Select(b => b.Color));
            }
        }

        // ── Individual Engagement ──────────────────────────────────────────

        [Fact]
        public async Task IndividualEngagement_ScoresDimensionsChangeAndDailySeries()
        {
            using (var db = Db())
            {
                IndividualAnswer(db, Team, "2026-09-10", CloverCategory.Communication, Happy);        // 100
                IndividualAnswer(db, Team, "2026-09-10", CloverCategory.Communication, Indifferent);  // 67
                IndividualAnswer(db, Team, "2026-09-15", CloverCategory.Opportunities, Sad);          // 34
                IndividualAnswer(db, Team, "2026-08-20", CloverCategory.Communication, Sad);          // previous period
            }

            var t = await Get();

            // Engagement = mean(Communication 83.5, Opportunities 34) = 58.75 → 59; previous 34.
            Assert.Equal(59, t.Timeline.IndividualEngagement.Current);
            Assert.Equal(25, t.Timeline.IndividualEngagement.Change);
            Assert.Collection(t.Timeline.IndividualEngagement.Data,
                d => { Assert.Equal("2026-09-10", d.Date); Assert.Equal(84, d.Score); },
                d => { Assert.Equal("2026-09-15", d.Date); Assert.Equal(34, d.Score); });

            var comm = t.IndividualEngagementDetails.Communication;
            Assert.Equal(84, comm.Score);
            Assert.Equal(ScoreStatuses.Good, comm.Status);
            Assert.Equal(50, comm.Change);

            var opp = t.IndividualEngagementDetails.Opportunities;
            Assert.Equal(34, opp.Score);
            Assert.Equal(ScoreStatuses.UrgentAttention, opp.Status);
            Assert.Null(opp.Change); // nothing in the previous period

            Assert.Null(t.IndividualEngagementDetails.Learning.Score);
        }

        [Theory]
        [InlineData(Happy, 100, ScoreStatuses.Excellent)]
        [InlineData(Indifferent, 67, ScoreStatuses.Good)]
        [InlineData(Sad, 34, ScoreStatuses.UrgentAttention)]
        [InlineData(Angry, 1, ScoreStatuses.UrgentAttention)]
        public async Task Status_FollowsThresholdBands(int sentiment, int score, string status)
        {
            using (var db = Db())
                IndividualAnswer(db, Team, "2026-09-20", CloverCategory.Reflection, sentiment);

            var r = (await Get()).IndividualEngagementDetails.Reflection;

            Assert.Equal(score, r.Score);
            Assert.Equal(status, r.Status);
        }

        [Fact]
        public async Task Status_NeedsWorkBand()
        {
            using (var db = Db())
            {
                IndividualAnswer(db, Team, "2026-09-20", CloverCategory.Learning, Sad);          // 34
                IndividualAnswer(db, Team, "2026-09-20", CloverCategory.Learning, Indifferent);  // 67 → mean 50.5 → 51
            }

            var l = (await Get()).IndividualEngagementDetails.Learning;

            Assert.Equal(51, l.Score);
            Assert.Equal(ScoreStatuses.NeedsWork, l.Status);
        }

        [Fact]
        public async Task OnlyPreviousPeriodData_CurrentAndChangeAreNull()
        {
            using (var db = Db())
                IndividualAnswer(db, Team, "2026-08-10", CloverCategory.Learning, Happy);

            var t = await Get();

            Assert.Null(t.Timeline.IndividualEngagement.Current);
            Assert.Null(t.Timeline.IndividualEngagement.Change);
            Assert.Null(t.IndividualEngagementDetails.Learning.Score);
            Assert.Null(t.IndividualEngagementDetails.Learning.Change);
        }

        [Fact]
        public async Task AnswersOlderThanSixtyDays_AndOtherTeams_AreIgnored()
        {
            using (var db = Db())
            {
                IndividualAnswer(db, Team, "2026-07-01", CloverCategory.Learning, Angry);
                IndividualAnswer(db, OtherTeamA, "2026-09-20", CloverCategory.Learning, Angry);
                IndividualAnswer(db, Team, "2026-09-20", CloverCategory.Learning, Happy);
            }

            var t = await Get();

            Assert.Equal(100, t.IndividualEngagementDetails.Learning.Score);
            Assert.Null(t.IndividualEngagementDetails.Learning.Change);
        }

        // ── Team Conditions ────────────────────────────────────────────────

        [Fact]
        public async Task TeamConditions_GroupsAreMeanOfConditions_WithLowest()
        {
            using (var db = Db())
            {
                TeamAnswer(db, Team, "2026-09-12", TeamSubCategory.GoalAffinity, Happy);           // 100
                TeamAnswer(db, Team, "2026-09-12", TeamSubCategory.GoalAffinity, Happy);           // 100 (one condition, not two)
                TeamAnswer(db, Team, "2026-09-12", TeamSubCategory.ImprovementLoop, Sad);          // 34
                TeamAnswer(db, Team, "2026-09-14", TeamSubCategory.Structure, Indifferent);        // 67
                TeamAnswer(db, Team, "2026-09-14", TeamSubCategory.CoordinationQuality, Indifferent); // 67
                TeamAnswer(db, Team, "2026-08-14", TeamSubCategory.Potency, Angry);                // previous period: 1
            }

            var t = await Get();

            var dl = t.TeamConditionsDetails.DirectionAndLearning;
            Assert.Equal(67, dl.Score);                        // mean(100, 34) — conditions, not answers
            Assert.Equal(ScoreStatuses.Good, dl.Status);
            Assert.Equal("Improvement Loop", dl.Lowest!.Name);
            Assert.Equal(34, dl.Lowest.Score);

            var dm = t.TeamConditionsDetails.DesignAndMechanics;
            Assert.Equal(67, dm.Score);
            Assert.Equal("Structure", dm.Lowest!.Name);        // tie → first in order

            var wtb = t.TeamConditionsDetails.WhatTeamBelieves;
            Assert.Null(wtb.Score);
            Assert.Null(wtb.Status);
            Assert.Null(wtb.Lowest);

            // Lowest group with a score; 67 and 67 tie, so the earlier group.
            var summary = t.TeamConditionsDetails.Summary;
            Assert.Equal("Direction and learning", summary.LowestGroup);
            Assert.Equal(67, summary.LowestScore);
            Assert.Equal($"Direction and learning is the lowest group at 67, so it needs a rhythm. {TeamConditionsSummaryDto.Explanation}",
                summary.Text);

            Assert.Equal(67, t.Timeline.TeamConditions.Current);   // mean of the two groups with data
            Assert.Equal(66, t.Timeline.TeamConditions.Change);    // 67 − 1
            Assert.Collection(t.Timeline.TeamConditions.Data,
                d => { Assert.Equal("2026-09-12", d.Date); Assert.Equal(67, d.Score); },
                d => { Assert.Equal("2026-09-14", d.Date); Assert.Equal(67, d.Score); });
        }

        // ── Response rate ──────────────────────────────────────────────────

        [Fact]
        public async Task ResponseRate_LastSevenWorkingDays_ScreensCountedSeparately()
        {
            using (var db = Db())
            {
                IndividualAnswer(db, Team, "2026-09-28", CloverCategory.Learning, Happy, count: 3);
                TeamAnswer(db, Team, "2026-09-28", TeamSubCategory.Results, Happy, count: 3);      // 6 of 8 → 75%
                IndividualAnswer(db, Team, "2026-09-18", CloverCategory.Learning, Happy, count: 8);
                TeamAnswer(db, Team, "2026-09-18", TeamSubCategory.Results, Happy, count: 2);      // 10 of 8 → capped 100%
                IndividualAnswer(db, Team, "2026-09-26", CloverCategory.Learning, Happy, count: 2); // Saturday: not a working day
                IndividualAnswer(db, Team, "2026-09-29", CloverCategory.Learning, Happy, count: 2); // today: day not over
                IndividualAnswer(db, Team, "2026-09-17", CloverCategory.Learning, Happy, count: 2); // before the window
            }

            var rr = (await Get()).ResponseRateDetails;

            Assert.Equal(ResponseRatePeriods.LastSevenWorkingDays, rr.Period);
            Assert.Equal(new[] { "2026-09-18", "2026-09-21", "2026-09-22", "2026-09-23", "2026-09-24", "2026-09-25", "2026-09-28" },
                rr.Data.Select(d => d.Date));
            Assert.Equal(new int?[] { 100, 0, 0, 0, 0, 0, 75 }, rr.Data.Select(d => d.Rate));
            Assert.Equal(16, rr.Summary.Answers);
            Assert.Equal(56, rr.Summary.Possible);
            Assert.Equal(29, rr.Current);   // 16 / 56 = 28.6%
        }

        [Fact]
        public async Task ScreenCompletion_EachScreenOnItsOwn_OverTheResponseRateDays()
        {
            using (var db = Db())
            {
                IndividualAnswer(db, Team, "2026-09-28", CloverCategory.Learning, Happy, count: 3);
                TeamAnswer(db, Team, "2026-09-28", TeamSubCategory.Results, Happy, count: 3);
                IndividualAnswer(db, Team, "2026-09-18", CloverCategory.Learning, Happy, count: 8);
                TeamAnswer(db, Team, "2026-09-18", TeamSubCategory.Results, Happy, count: 2);
                IndividualAnswer(db, Team, "2026-09-26", CloverCategory.Learning, Happy, count: 2); // Saturday
                IndividualAnswer(db, Team, "2026-09-29", CloverCategory.Learning, Happy, count: 2); // today
            }

            var screens = (await Get()).IndividualEngagementDetails.ScreenCompletion;

            // TeamSize 4 x 7 working days = 28 possible on each screen.
            Assert.Equal(39, screens.Screen1);   // 11 / 28
            Assert.Equal(18, screens.Screen2);   // 5 / 28
            Assert.Equal(ResponseRatePeriods.LastSevenWorkingDays, screens.Period);
            Assert.Equal("Screen 1 completion 39% · Screen 2 completion 18% — tracked separately so a module can never break someone's streak.",
                screens.Text);
        }

        [Fact]
        public async Task ScreenCompletion_NoTeamSize_IsNull()
        {
            using (var db = Db())
                IndividualAnswer(db, OtherTeamA, "2026-09-24", CloverCategory.Learning, Happy, count: 3);

            var screens = (await Get(OtherTeamA, RridaAdmin)).IndividualEngagementDetails.ScreenCompletion;

            Assert.Null(screens.Screen1);
            Assert.Null(screens.Screen2);
            Assert.Equal("Screen 1 and screen 2 are tracked separately so a module can never break someone's streak.", screens.Text);
        }

        [Fact]
        public async Task ResponseRate_TimelineMatchesDetails()
        {
            using (var db = Db())
                IndividualAnswer(db, Team, "2026-09-24", CloverCategory.Learning, Happy, count: 4);

            var t = await Get();

            Assert.Equal(t.ResponseRateDetails.Current, t.Timeline.ResponseRate.Current);
            Assert.Equal(t.ResponseRateDetails.Period, t.Timeline.ResponseRate.Period);
            Assert.Equal(t.ResponseRateDetails.Data.Select(d => (d.Date, d.Rate)), t.Timeline.ResponseRate.Data.Select(d => (d.Date, d.Rate)));
            Assert.Equal(50, t.ResponseRateDetails.Data.Single(d => d.Date == "2026-09-24").Rate);
        }

        [Fact]
        public async Task ResponseRate_NoTeamSize_RatesAreNull_AnswersStillCounted()
        {
            using (var db = Db())
                IndividualAnswer(db, OtherTeamA, "2026-09-24", CloverCategory.Learning, Happy, count: 3);

            var rr = (await Get(OtherTeamA, RridaAdmin)).ResponseRateDetails;

            Assert.Null(rr.Current);
            Assert.All(rr.Data, d => Assert.Null(d.Rate));
            Assert.Equal(7, rr.Data.Count);
            Assert.Equal(3, rr.Summary.Answers);
            Assert.Null(rr.Summary.Possible);
            Assert.Equal($"No data yet. {ResponseRateSummaryDto.Explanation}", rr.Summary.Text);
        }

        // ── Milestones on the timeline ─────────────────────────────────────

        [Fact]
        public async Task Milestones_InsidePeriodOnly_OldestFirst()
        {
            using (var db = Db())
            {
                AddMilestone(db, Team, "2026-09-20", "Second");
                AddMilestone(db, Team, "2026-08-31", "First");
                AddMilestone(db, Team, "2026-08-30", "Too old");
                AddMilestone(db, Team, "2026-09-10", "Removed", isDel: true);
            }

            var t = await Get();

            Assert.Equal(new[] { "First", "Second" }, t.Timeline.Milestones.Select(m => m.Title));
        }

        // ── Custom questions on the timeline ───────────────────────────────

        [Fact]
        public async Task CustomQuestions_NoneAsked_IsEmptyList()
        {
            var t = await Get();

            Assert.NotNull(t.Timeline.CustomQuestions);
            Assert.Empty(t.Timeline.CustomQuestions);
        }

        [Fact]
        public async Task CustomQuestions_CountsRateAndAnswerTexts()
        {
            uint id;
            using (var db = Db())
            {
                SeedCulture(db);
                id = AddQuestion(db, Team, "2026-09-21", "How did you feel about yesterday's workshop?", CustomQuestionStatus.Expired);
                AddAnswers(db, id, Team, 3);
            }

            var q = Assert.Single((await Get()).Timeline.CustomQuestions);

            Assert.Equal(id, q.QuestionId);
            Assert.Equal("How did you feel about yesterday's workshop?", q.QuestionText);
            Assert.Equal("2026-09-21", q.Date);
            Assert.Equal(3, q.ResponseCount);
            Assert.Equal(TeamSize, q.EligibleRespondentCount);
            Assert.Equal(75, q.ResponseRate);
            Assert.Equal(new[] { "answer 0", "answer 1", "answer 2" }, q.Responses.Select(r => r.ResponseText));
            Assert.Equal(3, q.Responses.Select(r => r.ResponseId).Distinct().Count());
        }

        [Fact]
        public async Task CustomQuestions_ZeroAnswers_AndBlankAnswersCountedNotListed()
        {
            using (var db = Db())
            {
                SeedCulture(db);
                AddQuestion(db, Team, "2026-09-10", "Nobody answered");
                var blank = AddQuestion(db, Team, "2026-09-15", "Some blank");
                AddAnswers(db, blank, Team, 1);
                db.CustomQuestionDailyFeedback.Add(new CustomQuestionDailyFeedback { CustomQuestionId = blank, UnitId = Team, FreeText = null });
                db.SaveChanges();
            }

            var qs = (await Get()).Timeline.CustomQuestions;

            Assert.Equal(new[] { "Nobody answered", "Some blank" }, qs.Select(q => q.QuestionText));
            Assert.Equal(0, qs[0].ResponseCount);
            Assert.Equal(0, qs[0].ResponseRate);
            Assert.Empty(qs[0].Responses);
            Assert.Equal(2, qs[1].ResponseCount);
            Assert.Equal(50, qs[1].ResponseRate);
            Assert.Single(qs[1].Responses);
        }

        [Fact]
        public async Task CustomQuestions_PeriodStatusAndTeamFiltered_OldestFirst()
        {
            using (var db = Db())
            {
                SeedCulture(db);
                AddQuestion(db, Team, "2026-09-25", "Second");
                AddQuestion(db, Team, "2026-08-31", "First", CustomQuestionStatus.Active);
                AddQuestion(db, Team, "2026-08-30", "Too old");
                AddQuestion(db, Team, "2026-09-29", "Today, not asked until 16:00");
                AddQuestion(db, Team, "2026-09-12", "Cancelled", CustomQuestionStatus.Cancelled);
                AddQuestion(db, Team, "2026-09-13", "Draft", CustomQuestionStatus.Draft);
                AddQuestion(db, OtherTeamA, "2026-09-14", "Other team");
                var removed = AddQuestion(db, Team, "2026-09-16", "Removed");
                db.CustomQuestions.Find(removed)!.IsDel = true;
                db.SaveChanges();
            }

            var t = await Get();

            Assert.Equal(new[] { "First", "Second" }, t.Timeline.CustomQuestions.Select(q => q.QuestionText));
        }

        [Fact]
        public async Task CustomQuestions_NoTeamSize_RateAndEligibleNull()
        {
            using (var db = Db())
            {
                SeedCulture(db);
                db.BusinessUnits.Find(Team)!.TeamSize = null;
                db.SaveChanges();
                var id = AddQuestion(db, Team, "2026-09-21", "Q");
                AddAnswers(db, id, Team, 2);
            }

            var q = Assert.Single((await Get()).Timeline.CustomQuestions);

            Assert.Equal(2, q.ResponseCount);
            Assert.Null(q.EligibleRespondentCount);
            Assert.Null(q.ResponseRate);
        }

        // ── Read only ──────────────────────────────────────────────────────

        [Fact]
        public async Task Timeline_WritesNothing()
        {
            using (var db = Db())
                IndividualAnswer(db, Team, "2026-09-20", CloverCategory.Learning, Happy);
            _tracker.Writes.Clear();

            await Get();

            Assert.Empty(_tracker.Writes);
        }

        // ── Errors ─────────────────────────────────────────────────────────

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("abc")]
        [InlineData("0")]
        [InlineData("-5")]
        [InlineData("1.5")]
        [InlineData("99999999999")]
        public async Task InvalidTeamId_IsBadRequest(string? teamId)
        {
            var result = await Service().GetTimelineAsync(teamId, ProfileAdminA);

            Assert.Equal(TeamMilestoneError.BadRequest, result.Error);
            Assert.Equal(ResponseMessages.Errors.TimelineTeamIdInvalid, result.Message);
        }

        [Theory]
        [InlineData("9999")]
        [InlineData("296")] // soft-deleted
        public async Task UnknownOrDeletedTeam_IsNotFound(string teamId)
        {
            var result = await Service().GetTimelineAsync(teamId, RridaAdmin);

            Assert.Equal(TeamMilestoneError.NotFound, result.Error);
        }

        [Theory]
        [InlineData(ProfileAdminB)]
        [InlineData(PortfolioManagerNoReports)]
        [InlineData(UnassignedManager)]
        public async Task NotPermitted_IsForbidden(int userId)
        {
            var result = await Service().GetTimelineAsync("294", userId);

            Assert.Equal(TeamMilestoneError.Forbidden, result.Error);
        }

        [Theory]
        [InlineData(RridaAdmin)]
        [InlineData(ProfileAdminA)]
        [InlineData(ManagerOfTeam)]
        [InlineData(ManagerOfParent)]
        [InlineData(PortfolioManagerOfTeam)]
        public async Task PermittedRoles_Succeed(int userId)
        {
            var result = await Service().GetTimelineAsync(" 294 ", userId);

            Assert.True(result.IsSuccess, result.Message);
        }
    }
}
