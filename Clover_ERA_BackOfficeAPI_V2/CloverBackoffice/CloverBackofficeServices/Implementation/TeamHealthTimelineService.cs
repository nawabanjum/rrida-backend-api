using System.Globalization;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.TeamQuestions;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using CloverCategory = CloverBackoffice.Components.Enums.CloverCategory;

namespace CloverBackoffice.Services.Implementation
{
    // Team Health Timeline: one read that feeds the whole screen and its three
    // drill-downs, so the page never calls back when a card is opened.
    //
    // Sources
    //   Individual Engagement  DailyFeedback (screen 1), categorised by the CLOVER
    //                          dimension on the answered reason's specification.
    //   Team Conditions        TeamDailyFeedback (screen 2), categorised by the
    //                          condition (SubCategory) and group (TeamElement) on
    //                          the answered reason's specification.
    //   Response rate          both tables, against BusinessUnits.TeamSize.
    //   Custom questions       CustomQuestions asked in the period, with their
    //                          anonymous CustomQuestionDailyFeedback answers.
    //
    // Scoring (the same weights the aggregate dashboard uses)
    //   answer            Angry 1, Sad 34, Indifferent 67, Happy 100
    //   dimension score   mean of its answers
    //   engagement        mean of the dimension scores that have answers
    //   condition score   mean of its answers
    //   group score       mean of its condition scores ("each group is the mean of
    //                     its conditions")
    //   team conditions   mean of the group scores
    //   change            this 30 days minus the 30 days before, in whole points
    //   status            FeedbackScoreThresholds bands: 85+ EXCELLENT, 65+ GOOD,
    //                     35+ NEEDS_WORK, below that URGENT_ATTENTION
    //
    // Response rate: the last 7 working days (Mon–Fri) before today — today is left
    // out because the check-in is asked at 16:00 and the day is not over. Each team
    // member can give two answers a day (screen 1 and screen 2, counted separately),
    // so possible = TeamSize × 2 per day. Screen completion (the line under the CLOVER
    // conditions) is each screen on its own over the same days: possible = TeamSize per day.
    //
    // All dates are UTC calendar days. Nothing is stored; every call reads live.
    public partial class TeamHealthTimelineService : ITeamHealthTimelineService
    {
        public const int TimelineDays = 30;
        public const int ResponseRateWorkingDays = 7;
        public const int AnswersPerMemberPerDay = 2;

        private readonly ApplicationDbContext _context;
        private readonly ITeamHealthAccessService _access;
        private readonly FeedbackScoreThresholdsOptions _thresholds;
        private readonly TimeProvider _clock;
        private readonly IRridaEngineContextFactory? _engine;

        public TeamHealthTimelineService(
            ApplicationDbContext context,
            ITeamHealthAccessService access,
            IOptions<FeedbackScoreThresholdsOptions> thresholds,
            TimeProvider? clock = null,
            IRridaEngineContextFactory? engine = null)
        {
            _context = context;
            _access = access;
            _thresholds = thresholds.Value;
            _clock = clock ?? TimeProvider.System;
            _engine = engine;
        }

        public async Task<TeamMilestoneResult<TeamHealthTimelineDto>> GetTimelineAsync(string? teamId, int actorUserId, CancellationToken ct = default)
        {
            if (!uint.TryParse(teamId?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var unitId) || unitId == 0)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.TimelineTeamIdInvalid);

            var unit = await _context.BusinessUnits
                .AsNoTracking()
                .Where(u => u.Id == unitId && u.IsDel != true)
                .Select(u => new { u.ProfileId, u.TeamSize })
                .FirstOrDefaultAsync(ct);
            if (unit == null)
                return Fail(TeamMilestoneError.NotFound, ResponseMessages.Errors.MilestoneTeamNotFound);

            if (!await _access.CanAccessTeamAsync(unitId, unit.ProfileId, actorUserId, ct))
                return Fail(TeamMilestoneError.Forbidden, ResponseMessages.Errors.TimelineAccessDenied);

            var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
            var periodStart = today.AddDays(-(TimelineDays - 1));
            var previousStart = periodStart.AddDays(-TimelineDays);

            // A team on the engine is read from the engine's own answers, as the MVP draws
            // its board (TeamHealthTimelineService.Engine.cs); any other team from the
            // back-office check-in tables.
            ScoreSeriesDto individualEngagement, teamConditions;
            List<Answer> currentIndividual, previousIndividual, currentTeam;
            ResponseRateDetailsDto responseRate;
            ScreenCompletionDto screenCompletion;

            var engineReading = await LoadEngineReadingAsync(unitId, unit.TeamSize, today, periodStart, previousStart, ct);
            if (engineReading is not null)
            {
                individualEngagement = engineReading.IndividualEngagement;
                teamConditions = engineReading.TeamConditions;
                currentIndividual = engineReading.CurrentIndividual;
                previousIndividual = engineReading.PreviousIndividual;
                currentTeam = engineReading.CurrentTeam;
                responseRate = engineReading.ResponseRate;
                screenCompletion = engineReading.ScreenCompletion;
            }
            else
            {
                var individual = await LoadIndividualAnswersAsync(unitId, previousStart, today, ct);
                var team = await LoadTeamAnswersAsync(unitId, previousStart, today, ct);

                currentIndividual = individual.Where(a => a.Day >= periodStart).ToList();
                previousIndividual = individual.Where(a => a.Day < periodStart).ToList();
                currentTeam = team.Where(a => a.Day >= periodStart).ToList();
                var previousTeam = team.Where(a => a.Day < periodStart).ToList();

                individualEngagement = Series(currentIndividual, previousIndividual, EngagementScore);
                teamConditions = Series(currentTeam, previousTeam, ConditionsScore);
                (responseRate, screenCompletion) = await BuildResponseRateAsync(unitId, unit.TeamSize, today, ct);
            }

            var milestones = await _context.TeamMilestones
                .AsNoTracking()
                .Where(m => m.UnitId == unitId && !m.IsDel && m.MilestoneDate >= periodStart && m.MilestoneDate <= today)
                .OrderBy(m => m.MilestoneDate)
                .ThenBy(m => m.Id)
                .ToListAsync(ct);

            var actions = await TeamActionService.LoadTimelineActionsAsync(_context, unitId, periodStart, today, ct);

            var customQuestions = await LoadCustomQuestionsAsync(unitId, unit.TeamSize, periodStart, ct);

            var dto = new TeamHealthTimelineDto
            {
                TeamId = unitId.ToString(CultureInfo.InvariantCulture),
                Timeline = new TimelineSectionDto
                {
                    Period = new TimelinePeriodDto { StartDate = Iso(periodStart), EndDate = Iso(today) },
                    IndividualEngagement = individualEngagement,
                    TeamConditions = teamConditions,
                    ResponseRate = new ResponseRateSeriesDto
                    {
                        Current = responseRate.Current,
                        Period = responseRate.Period,
                        Data = responseRate.Data
                    },
                    Milestones = milestones.Select(TeamMilestoneService.ToDto).ToList(),
                    Actions = actions,
                    CustomQuestions = customQuestions
                },
                IndividualEngagementDetails = new IndividualEngagementDetailsDto
                {
                    Communication = Dimension(CloverCategory.Communication, currentIndividual, previousIndividual),
                    Learning = Dimension(CloverCategory.Learning, currentIndividual, previousIndividual),
                    Opportunities = Dimension(CloverCategory.Opportunities, currentIndividual, previousIndividual),
                    Vulnerability = Dimension(CloverCategory.Vulnerability, currentIndividual, previousIndividual),
                    Enablement = Dimension(CloverCategory.Enablement, currentIndividual, previousIndividual),
                    Reflection = Dimension(CloverCategory.Reflection, currentIndividual, previousIndividual),
                    ScreenCompletion = screenCompletion,
                    Legend = Legend()
                },
                TeamConditionsDetails = ConditionsDetails(
                    Group(TeamElement.DirectionAndLearning, currentTeam),
                    Group(TeamElement.DesignAndMechanics, currentTeam),
                    Group(TeamElement.WhatTheTeamBelieves, currentTeam),
                    Legend()),
                ResponseRateDetails = responseRate
            };

            return TeamMilestoneResult<TeamHealthTimelineDto>.Ok(dto);
        }

        // ── Loading ──────────────────────────────────────────────────────────

        // One answer, reduced to what scoring needs. Category is the CLOVER
        // dimension for screen 1; Group/Condition are set for screen 2.
        private sealed record Answer(DateOnly Day, double Weight, int Category, TeamElement Group, TeamSubCategory Condition);

        private async Task<List<Answer>> LoadIndividualAnswersAsync(uint unitId, DateOnly from, DateOnly to, CancellationToken ct)
        {
            var (fromUtc, toUtc) = UtcRange(from, to);

            var rows = await (
                from df in _context.DailyFeedbacks.AsNoTracking()
                join r in _context.DailyReasons.AsNoTracking() on df.DailyReasonId equals (uint?)r.Id
                join sp in _context.DailyQuestionSpecifications.AsNoTracking() on r.SpecificationId equals sp.Id
                where df.BusinessUnitId == unitId
                      && df.SubmissionDateTime >= fromUtc
                      && df.SubmissionDateTime < toUtc
                select new { df.SubmissionDateTime, Sentiment = (int)df.SentimentType, Category = (int)sp.CloverCategory })
                .ToListAsync(ct);

            return rows
                .Where(r => Weight(r.Sentiment).HasValue && Enum.IsDefined(typeof(CloverCategory), r.Category))
                .Select(r => new Answer(DateOnly.FromDateTime(r.SubmissionDateTime), Weight(r.Sentiment)!.Value, r.Category, default, default))
                .ToList();
        }

        private async Task<List<Answer>> LoadTeamAnswersAsync(uint unitId, DateOnly from, DateOnly to, CancellationToken ct)
        {
            var (fromUtc, toUtc) = UtcRange(from, to);

            var rows = await (
                from tf in _context.TeamDailyFeedback.AsNoTracking()
                join r in _context.TeamDailyReasons.AsNoTracking() on tf.TeamDailyReasonId equals r.Id
                join sp in _context.TeamDailyQuestionSpecifications.AsNoTracking() on r.SpecificationId equals sp.Id
                where tf.BusinessUnitId == unitId
                      && tf.SubmissionDateTime >= fromUtc
                      && tf.SubmissionDateTime < toUtc
                select new { tf.SubmissionDateTime, Sentiment = (int)tf.SentimentType, sp.TeamElement, sp.SubCategory })
                .ToListAsync(ct);

            return rows
                .Where(r => Weight(r.Sentiment).HasValue && Enum.IsDefined(r.TeamElement) && Enum.IsDefined(r.SubCategory))
                .Select(r => new Answer(DateOnly.FromDateTime(r.SubmissionDateTime), Weight(r.Sentiment)!.Value, 0, r.TeamElement, r.SubCategory))
                .ToList();
        }

        // ── Scoring ──────────────────────────────────────────────────────────

        // Angry 1, Sad 34, Indifferent 67, Happy 100 — the aggregate dashboard's
        // weights, shared by both sentiment enums (same numbering).
        private static double? Weight(int sentiment) => sentiment switch
        {
            1 => 1,
            2 => 34,
            3 => 67,
            4 => 100,
            _ => null
        };

        private static double? EngagementScore(IReadOnlyCollection<Answer> answers) =>
            MeanOrNull(answers.GroupBy(a => a.Category).Select(g => g.Average(a => a.Weight)));

        private static double? ConditionsScore(IReadOnlyCollection<Answer> answers) =>
            MeanOrNull(answers.GroupBy(a => a.Group).Select(g => GroupScore(g.ToList())!.Value));

        private static double? GroupScore(IReadOnlyCollection<Answer> answers) =>
            MeanOrNull(answers.GroupBy(a => a.Condition).Select(g => g.Average(a => a.Weight)));

        private static double? MeanOrNull(IEnumerable<double> values)
        {
            var list = values.ToList();
            return list.Count == 0 ? null : list.Average();
        }

        private static int? Round(double? value) =>
            value.HasValue ? (int)Math.Round(value.Value, MidpointRounding.AwayFromZero) : null;

        private static int? Change(int? current, int? previous) =>
            current.HasValue && previous.HasValue ? current.Value - previous.Value : null;

        private string? Status(int? score)
        {
            if (!score.HasValue) return null;
            if (score.Value >= _thresholds.ExcellentMin) return ScoreStatuses.Excellent;
            if (score.Value >= _thresholds.GoodMin) return ScoreStatuses.Good;
            if (score.Value >= _thresholds.NeedsWorkMin) return ScoreStatuses.NeedsWork;
            return ScoreStatuses.UrgentAttention;
        }

        private static ScoreSeriesDto Series(List<Answer> current, List<Answer> previous, Func<IReadOnlyCollection<Answer>, double?> score)
        {
            var currentScore = Round(score(current));
            return new ScoreSeriesDto
            {
                Current = currentScore,
                Change = Change(currentScore, Round(score(previous))),
                Data = current
                    .GroupBy(a => a.Day)
                    .OrderBy(g => g.Key)
                    .Select(g => new DailyScoreDto { Date = Iso(g.Key), Score = Round(score(g.ToList()))!.Value })
                    .ToList()
            };
        }

        private DimensionScoreDto Dimension(CloverCategory category, List<Answer> current, List<Answer> previous)
        {
            var score = Round(MeanOrNull(current.Where(a => a.Category == (int)category).Select(a => a.Weight)));
            var before = Round(MeanOrNull(previous.Where(a => a.Category == (int)category).Select(a => a.Weight)));
            return new DimensionScoreDto { Score = score, Status = Status(score), Change = Change(score, before) };
        }

        /// <summary>
        /// The three groups, and the line under them: the lowest group with a score (ties to the
        /// earlier group) and how that kind of problem is fixed, in the wireframe's own words,
        /// then its explanation.
        /// </summary>
        private static TeamConditionsDetailsDto ConditionsDetails(
            ConditionGroupDto directionAndLearning, ConditionGroupDto designAndMechanics, ConditionGroupDto whatTeamBelieves,
            List<ScoreBandDto> legend)
        {
            var lowest = new[]
                {
                    (Name: "Direction and learning", Fix: "so it needs a rhythm", Group: directionAndLearning),
                    (Name: "Design and mechanics", Fix: "so look for a structural fix", Group: designAndMechanics),
                    (Name: "What the team believes", Fix: "so it moves only with evidence over time", Group: whatTeamBelieves)
                }
                .Where(g => g.Group.Score is not null)
                .OrderBy(g => g.Group.Score)
                .Select(g => ((string Name, string Fix, int Score)?)(g.Name, g.Fix, g.Group.Score!.Value))
                .FirstOrDefault();

            var summary = new TeamConditionsSummaryDto();
            if (lowest is { } l)
            {
                summary.LowestGroup = l.Name;
                summary.LowestScore = l.Score;
                summary.Text = $"{l.Name} is the lowest group at {l.Score}, {l.Fix}. {TeamConditionsSummaryDto.Explanation}";
            }

            return new TeamConditionsDetailsDto
            {
                DirectionAndLearning = directionAndLearning,
                DesignAndMechanics = designAndMechanics,
                WhatTeamBelieves = whatTeamBelieves,
                Summary = summary,
                Legend = legend
            };
        }

        /// <summary>
        /// The score key under the Individual Engagement and Team Conditions popups, from the same
        /// thresholds <see cref="Status"/> grades by, highest band first. Colours are the page's own.
        /// </summary>
        private List<ScoreBandDto> Legend()
        {
            static ScoreBandDto Band(string status, int min, int max, string words, string color) => new()
            {
                Status = status,
                Min = min,
                Max = max,
                Label = max >= 100 ? $"{min}+ {words}" : $"{min}–{max} {words}",
                Color = color
            };

            return
            [
                Band(ScoreStatuses.Excellent, _thresholds.ExcellentMin, 100, "excellent", "#125EA8"),
                Band(ScoreStatuses.Good, _thresholds.GoodMin, _thresholds.ExcellentMin - 1, "good", "#16a34a"),
                Band(ScoreStatuses.NeedsWork, _thresholds.NeedsWorkMin, _thresholds.GoodMin - 1, "needs work", "#d97706"),
                Band(ScoreStatuses.UrgentAttention, _thresholds.UrgentAttentionMin, _thresholds.NeedsWorkMin - 1, "urgent attention", "#b91c1c")
            ];
        }

        private ConditionGroupDto Group(TeamElement element, List<Answer> current)
        {
            var answers = current.Where(a => a.Group == element).ToList();
            var score = Round(GroupScore(answers));

            // Lowest condition that has answers; ties go to the first in enum order.
            var lowest = answers
                .GroupBy(a => a.Condition)
                .Select(g => new { Condition = g.Key, Score = g.Average(a => a.Weight) })
                .OrderBy(c => c.Score)
                .ThenBy(c => (int)c.Condition)
                .FirstOrDefault();

            return new ConditionGroupDto
            {
                Score = score,
                Status = Status(score),
                Lowest = lowest == null
                    ? null
                    : new ConditionScoreDto { Name = TeamQuestionDisplay.Label(lowest.Condition), Score = Round(lowest.Score)!.Value }
            };
        }

        // ── Response rate ────────────────────────────────────────────────────

        private async Task<(ResponseRateDetailsDto Details, ScreenCompletionDto Screens)> BuildResponseRateAsync(
            uint unitId, int? teamSize, DateOnly today, CancellationToken ct)
        {
            var days = LastWorkingDays(today, ResponseRateWorkingDays);
            var (fromUtc, toUtc) = UtcRange(days[0], days[^1]);

            var individualTimes = await _context.DailyFeedbacks.AsNoTracking()
                .Where(f => f.BusinessUnitId == unitId && f.SubmissionDateTime >= fromUtc && f.SubmissionDateTime < toUtc)
                .Select(f => f.SubmissionDateTime)
                .ToListAsync(ct);
            var teamTimes = await _context.TeamDailyFeedback.AsNoTracking()
                .Where(f => f.BusinessUnitId == unitId && f.SubmissionDateTime >= fromUtc && f.SubmissionDateTime < toUtc)
                .Select(f => f.SubmissionDateTime)
                .ToListAsync(ct);

            var answersByDay = individualTimes.Concat(teamTimes)
                .GroupBy(t => DateOnly.FromDateTime(t))
                .ToDictionary(g => g.Key, g => g.Count());

            int? possiblePerDay = teamSize is > 0 ? teamSize.Value * AnswersPerMemberPerDay : null;

            var data = days.Select(d => new DailyRateDto
            {
                Date = Iso(d),
                Rate = Rate(answersByDay.GetValueOrDefault(d), possiblePerDay)
            }).ToList();

            // Weekend answers are outside the window, so only working-day answers count.
            var answers = days.Sum(d => answersByDay.GetValueOrDefault(d));
            int? possible = possiblePerDay * days.Count;

            // Each screen on its own: one answer a member a day is possible on each.
            var window = days.ToHashSet();
            int? possiblePerScreen = teamSize is > 0 ? teamSize.Value * days.Count : null;
            var screens = new ScreenCompletionDto
            {
                Screen1 = Rate(individualTimes.Count(t => window.Contains(DateOnly.FromDateTime(t))), possiblePerScreen),
                Screen2 = Rate(teamTimes.Count(t => window.Contains(DateOnly.FromDateTime(t))), possiblePerScreen)
            };

            return (new ResponseRateDetailsDto
            {
                Current = Rate(answers, possible),
                Period = ResponseRatePeriods.LastSevenWorkingDays,
                Data = data,
                Summary = new ResponseRateSummaryDto { Answers = answers, Possible = possible }
            }, screens);
        }

        // Capped at 100: a team that has grown past its recorded size can
        // over-answer, and a rate above 100% reads as a bug.
        private static int? Rate(int answers, int? possible) =>
            possible is > 0 ? Math.Min(100, Round(answers * 100.0 / possible.Value)!.Value) : null;

        // The last `count` Mon–Fri days before `today`, oldest first.
        private static List<DateOnly> LastWorkingDays(DateOnly today, int count)
        {
            var days = new List<DateOnly>(count);
            for (var d = today.AddDays(-1); days.Count < count; d = d.AddDays(-1))
            {
                if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                    days.Add(d);
            }
            days.Reverse();
            return days;
        }

        // ── Custom questions ─────────────────────────────────────────────────

        // Custom questions already asked inside the period (ScheduledAt between the
        // period start and now — today's question appears once its 16:00 slot has
        // passed), published ones only (not Draft, not Cancelled). Counts follow the
        // Schedule modal's "previously asked" list: every answer counts, possible is
        // the team size, the rate is capped at 100. Only answers with text are listed,
        // and only the text and the answer's own id leave the server — nothing that
        // says who answered.
        private async Task<List<TimelineCustomQuestionDto>> LoadCustomQuestionsAsync(uint unitId, int? teamSize, DateOnly periodStart, CancellationToken ct)
        {
            var fromUtc = periodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var nowUtc = _clock.GetUtcNow().UtcDateTime;

            var questions = await _context.CustomQuestions
                .AsNoTracking()
                .Where(q => q.UnitId == unitId
                            && !q.IsDel
                            && q.ScheduledAt != null
                            && q.ScheduledAt >= fromUtc
                            && q.ScheduledAt <= nowUtc
                            && (q.Status == CustomQuestionStatus.Scheduled
                                || q.Status == CustomQuestionStatus.Active
                                || q.Status == CustomQuestionStatus.Expired))
                .OrderBy(q => q.ScheduledAt)
                .ThenBy(q => q.Id)
                .Select(q => new { q.Id, q.Text, q.ScheduledAt })
                .ToListAsync(ct);
            if (questions.Count == 0)
                return new List<TimelineCustomQuestionDto>();

            var ids = questions.Select(q => q.Id).ToList();
            var answers = await _context.CustomQuestionDailyFeedback
                .AsNoTracking()
                .Where(f => ids.Contains(f.CustomQuestionId) && f.UnitId == unitId)
                .OrderBy(f => f.Id)
                .Select(f => new { f.Id, f.CustomQuestionId, f.FreeText })
                .ToListAsync(ct);
            var answersByQuestion = answers.ToLookup(a => a.CustomQuestionId);

            int? possible = teamSize is > 0 ? teamSize : null;

            return questions.Select(q =>
            {
                var given = answersByQuestion[q.Id].ToList();
                return new TimelineCustomQuestionDto
                {
                    QuestionId = q.Id,
                    QuestionText = q.Text,
                    Date = Iso(DateOnly.FromDateTime(q.ScheduledAt!.Value.ToUniversalTime())),
                    ResponseCount = given.Count,
                    EligibleRespondentCount = possible,
                    ResponseRate = Rate(given.Count, possible),
                    Responses = given
                        .Where(a => !string.IsNullOrWhiteSpace(a.FreeText))
                        .Select(a => new TimelineCustomQuestionAnswerDto { ResponseId = a.Id, ResponseText = a.FreeText!.Trim() })
                        .ToList()
                };
            }).ToList();
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static (DateTime From, DateTime To) UtcRange(DateOnly from, DateOnly toInclusive) =>
            (from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
             toInclusive.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static TeamMilestoneResult<TeamHealthTimelineDto> Fail(TeamMilestoneError error, string message) =>
            TeamMilestoneResult<TeamHealthTimelineDto>.Fail(error, message);
    }
}
