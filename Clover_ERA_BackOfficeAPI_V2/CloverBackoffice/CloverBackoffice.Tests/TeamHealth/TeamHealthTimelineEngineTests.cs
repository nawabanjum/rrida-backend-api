using System.Runtime.CompilerServices;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Moq;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;
using static CloverBackoffice.Tests.TeamHealth.TimelineTestData;

namespace CloverBackoffice.Tests.TeamHealth
{
    /// <summary>
    /// The timeline of a team on the engine, from its engine answers, as the MVP draws its
    /// board (state.js dailyLines / lineSeries, server.js statTiles). Today is Tue 2026-09-29;
    /// the team runs Mon–Fri from Tue 2026-09-01 with three enrolled members.
    /// </summary>
    public class TeamHealthTimelineEngineTests
    {
        private readonly string _db = Guid.NewGuid().ToString();
        private static readonly Lazy<Rrida.Engine.EngineContext> Ctx = new(() => Rrida.Engine.EngineContext.FromKit(Path.Combine(EngineRoot(), "kit", "data")));

        private static int Individual(string element) =>
            Ctx.Value.Data.Questions.Values.First(q => q.Track == "individual" && q.Element == element).Id;

        private static int TeamQuestion(string element) =>
            Ctx.Value.Data.Questions.Values.First(q => q.Track == "team" && q.Element == element).Id;

        public TeamHealthTimelineEngineTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
            SeedBanks(db);
            db.EngineTeamSettings.Add(new EngineTeamSettings
            {
                BusinessUnitId = Team, StartDate = new DateOnly(2026, 9, 1), WorkingWeek = "mon-fri", IsEnabled = true, CreatedAt = DateTime.UtcNow
            });
            for (var m = 1; m <= 3; m++)
                db.EngineTeamMemberships.Add(new EngineTeamMembership
                {
                    MemberId = m, BusinessUnitId = Team, EffectiveFrom = new DateOnly(2026, 9, 1), CreatedAt = DateTime.UtcNow
                });
            db.SaveChanges();
        }

        private void Answer(string date, long member, string track, int question, string level)
        {
            using var db = CreateContext(_db);
            var d = DateOnly.Parse(date);
            db.EngineAnswers.Add(new EngineAnswer
            {
                BusinessUnitId = Team, MemberId = member, AnswerDate = d, Day = d.DayNumber - new DateOnly(2026, 9, 1).DayNumber + 1,
                Track = track, QuestionKey = question, Level = level, CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        private async Task<TeamHealthTimelineDto> Get()
        {
            var db = CreateContext(_db);
            var engine = new Mock<IRridaEngineContextFactory>();
            engine.Setup(e => e.Context).Returns(Ctx.Value);
            engine.Setup(e => e.Calendar).Returns(Ctx.Value.Calendar);
            var service = new TeamHealthTimelineService(db, new TeamHealthAccessService(db, Roles()), Thresholds(), new FixedClock(Today), engine.Object);
            var r = await service.GetTimelineAsync("294", ProfileAdminA);
            Assert.True(r.IsSuccess, r.Message);
            return r.Data!;
        }

        [Fact]
        public async Task Lines_are_the_days_answers_on_the_MVP_scale_and_both_stop_on_a_day_with_no_answer()
        {
            var comm = Individual("Communication");
            var potency = TeamQuestion("Potency");
            // Fri 25 Sep: green + red individual = 50; one yellow team answer = 66.7.
            Answer("2026-09-25", 1, "individual", comm, "green");
            Answer("2026-09-25", 2, "individual", comm, "red");
            Answer("2026-09-25", 1, "team", potency, "yellow");
            // Mon 28 Sep: individual only; the team line has no point.
            Answer("2026-09-28", 3, "individual", comm, "orange");
            // Sat 26 Sep: a day the team is not asked; nothing is drawn on it.

            var t = await Get();

            var line1 = t.Timeline.IndividualEngagement.Data.ToDictionary(p => p.Date, p => p.Score);
            Assert.Equal(50, line1["2026-09-25"]);
            Assert.Equal(33, line1["2026-09-28"]);
            Assert.False(line1.ContainsKey("2026-09-26"));

            var line2 = t.Timeline.TeamConditions.Data.ToDictionary(p => p.Date, p => p.Score);
            Assert.Equal(67, line2["2026-09-25"]);
            Assert.False(line2.ContainsKey("2026-09-28"));   // asked, no team answer: no point
            Assert.False(line2.ContainsKey("2026-09-29"));   // today, nothing yet: no point
            Assert.False(line2.ContainsKey("2026-09-26"));
            Assert.False(line2.ContainsKey("2026-09-24"));   // nothing before the first reading

            // Tile: the mean of the last seven days' points that had answers (25th and 28th).
            Assert.Equal(42, t.Timeline.IndividualEngagement.Current);   // (50 + 33.3) / 2
            Assert.Null(t.Timeline.IndividualEngagement.Change);        // nothing in the seven days before
            Assert.Equal(67, t.Timeline.TeamConditions.Current);

            Assert.Equal(44, t.IndividualEngagementDetails.Communication.Score);   // its answers: (100 + 0 + 33.3) / 3
            Assert.Null(t.IndividualEngagementDetails.Learning.Score);
            Assert.Equal(67, t.TeamConditionsDetails.WhatTeamBelieves.Score);
        }

        [Fact]
        public async Task Response_rate_counts_screen_1_and_screen_2_separately_against_the_team_size()
        {
            var comm = Individual("Communication");
            var potency = TeamQuestion("Potency");
            // Mon 28 Sep: member 1 answers both screens (2), member 2 only screen 1 (1, a partial).
            Answer("2026-09-28", 1, "individual", comm, "green");
            Answer("2026-09-28", 1, "team", potency, "green");
            Answer("2026-09-28", 2, "individual", comm, "yellow");
            // Today's answers are not in the reading: the day is not over.
            Answer("2026-09-29", 3, "individual", comm, "yellow");

            var rr = (await Get()).ResponseRateDetails;

            // Fri 18, Mon 21 .. Fri 25, Mon 28: seven asked days. Three are enrolled, but the
            // team size the manager set is 4, and the size is the headcount: 4 x 2 screens a day.
            Assert.Equal(new[] { "2026-09-18", "2026-09-21", "2026-09-22", "2026-09-23", "2026-09-24", "2026-09-25", "2026-09-28" },
                rr.Data.Select(d => d.Date));
            Assert.Equal(38, rr.Data[^1].Rate);   // 3 of 8 (37.5)
            Assert.Equal(0, rr.Data[0].Rate);
            Assert.Equal(3, rr.Summary.Answers);
            Assert.Equal(56, rr.Summary.Possible); // 4 x 2 x 7
            Assert.Equal(5, rr.Current);          // 3 / 56
            Assert.False(rr.TeamSizeExceeded);
            Assert.Equal($"3 answers from 56 possible. {ResponseRateSummaryDto.Explanation}", rr.Summary.Text);
        }

        [Fact]
        public async Task Answering_only_one_question_is_a_partial_response()
        {
            var comm = Individual("Communication");
            var potency = TeamQuestion("Potency");
            Answer("2026-09-25", 1, "individual", comm, "green");   // Fri: screen 1 only
            Answer("2026-09-28", 1, "individual", comm, "green");   // Mon: both screens
            Answer("2026-09-28", 1, "team", potency, "green");

            var rr = (await Get()).ResponseRateDetails;

            Assert.Equal(13, rr.Data[^2].Rate);   // 1 of 8 (12.5): half of one member's day
            Assert.Equal(25, rr.Data[^1].Rate);   // 2 of 8: one member's full day
        }

        [Fact]
        public async Task Response_rate_counts_the_team_size_not_duplicate_enrolments()
        {
            // The Product Dev Team case: a size of 4, but reinstalls have enrolled 10 devices.
            using (var db = CreateContext(_db))
            {
                for (var m = 4; m <= 10; m++)
                    db.EngineTeamMemberships.Add(new EngineTeamMembership
                    {
                        MemberId = m, BusinessUnitId = Team, EffectiveFrom = new DateOnly(2026, 9, 1), CreatedAt = DateTime.UtcNow
                    });
                db.SaveChanges();
            }
            var comm = Individual("Communication");
            var potency = TeamQuestion("Potency");
            Answer("2026-09-28", 1, "individual", comm, "green");
            Answer("2026-09-28", 1, "team", potency, "green");
            Answer("2026-09-28", 2, "individual", comm, "green");
            Answer("2026-09-28", 2, "team", potency, "green");

            var rr = (await Get()).ResponseRateDetails;

            Assert.Equal(50, rr.Data[^1].Rate);   // 4 of 8 (4 people x 2), not 4 of 20
            Assert.Equal(56, rr.Summary.Possible);
        }

        [Fact]
        public async Task Response_rate_falls_back_to_members_enrolled_when_no_team_size_is_set()
        {
            SetTeamSize(null);
            var comm = Individual("Communication");
            var potency = TeamQuestion("Potency");
            Answer("2026-09-28", 1, "individual", comm, "green");
            Answer("2026-09-28", 1, "team", potency, "green");
            Answer("2026-09-28", 2, "individual", comm, "yellow");

            var rr = (await Get()).ResponseRateDetails;

            Assert.Equal(50, rr.Data[^1].Rate);   // 3 of 6 (the 3 enrolled x 2)
            Assert.Equal(42, rr.Summary.Possible);
            Assert.Equal(7, rr.Current);          // 3 / 42
        }

        [Fact]
        public async Task More_answers_than_the_team_size_caps_the_day_at_100_and_flags_the_size()
        {
            SetTeamSize(2);
            var comm = Individual("Communication");
            var potency = TeamQuestion("Potency");
            for (var m = 1; m <= 3; m++)
            {
                Answer("2026-09-28", m, "individual", comm, "green");
                Answer("2026-09-28", m, "team", potency, "green");
            }

            var rr = (await Get()).ResponseRateDetails;

            Assert.Equal(100, rr.Data[^1].Rate);  // 6 of 4, capped
            Assert.True(rr.TeamSizeExceeded);
        }

        [Fact]
        public async Task A_dated_team_size_applies_from_its_own_day()
        {
            // The size went from 4 (set now) to 10 on Fri 25 Sep, by the dated history.
            using (var db = CreateContext(_db))
            {
                db.TeamSizeHistory.Add(new TeamSizeHistory
                {
                    BusinessUnitId = Team, TeamSize = 10, EffectiveFrom = new DateOnly(2026, 9, 25), CreatedAt = DateTime.UtcNow
                });
                db.SaveChanges();
            }

            var rr = (await Get()).ResponseRateDetails;

            // 18, 21, 22, 23, 24 before it (no row: the size set now, 4); 25 and 28 from it (10); x 2 screens.
            Assert.Equal((5 * 4 + 2 * 10) * 2, rr.Summary.Possible);
        }

        [Fact]
        public async Task Screen_completion_counts_each_track_on_its_own_against_the_team_size()
        {
            var comm = Individual("Communication");
            var potency = TeamQuestion("Potency");
            Answer("2026-09-28", 1, "individual", comm, "green");
            Answer("2026-09-28", 1, "team", potency, "green");
            Answer("2026-09-28", 2, "individual", comm, "yellow");
            Answer("2026-09-29", 3, "team", potency, "yellow");   // today: not counted

            var screens = (await Get()).IndividualEngagementDetails.ScreenCompletion;

            Assert.Equal(7, screens.Screen1);    // 2 of 28 (team size 4 x seven asked days)
            Assert.Equal(4, screens.Screen2);    // 1 of 28
        }

        private void SetTeamSize(int? size)
        {
            using var db = CreateContext(_db);
            db.BusinessUnits.Single(b => b.Id == Team).TeamSize = size;
            db.SaveChanges();
        }

        [Fact]
        public async Task A_team_not_on_the_engine_keeps_the_back_office_reading()
        {
            using (var db = CreateContext(_db))
            {
                db.EngineTeamSettings.RemoveRange(db.EngineTeamSettings);
                db.SaveChanges();
                IndividualAnswer(db, Team, "2026-09-20", Infrastructure.Models.CloverCategory.Learning, Happy);
            }

            var t = await Get();

            Assert.Equal(100, t.Timeline.IndividualEngagement.Current);   // back-office weights and windows
            Assert.Equal(56, t.ResponseRateDetails.Summary.Possible);      // TeamSize 4 x 2 screens x 7 days
            Assert.False(t.ResponseRateDetails.TeamSizeExceeded);
            Assert.StartsWith("0 answers from 56 possible. Screen 1 and screen 2 are counted separately",
                t.ResponseRateDetails.Summary.Text);                         // the back-office wording is unchanged
        }

        private static string EngineRoot([CallerFilePath] string source = "")
        {
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(source)!); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "external", "RRIDA-Engine");
                if (Directory.Exists(candidate)) return candidate;
            }
            throw new DirectoryNotFoundException("external/RRIDA-Engine not found");
        }
    }
}
