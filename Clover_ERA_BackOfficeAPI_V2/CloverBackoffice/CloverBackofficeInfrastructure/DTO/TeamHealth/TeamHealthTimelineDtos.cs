namespace CloverBackoffice.Infrastructure.DTO.TeamHealth
{
    // GET api/team-health/timeline?teamId= — everything the Team Health Timeline
    // screen and its three drill-downs (Individual Engagement, Team Conditions,
    // Response Rate) render, in one response. Property names follow the ticket's
    // JSON contract. A value that cannot be worked out from the data is null,
    // never 0 or a placeholder.

    public class TeamHealthTimelineDto
    {
        public string TeamId { get; set; } = string.Empty;
        public TimelineSectionDto Timeline { get; set; } = new();
        public IndividualEngagementDetailsDto IndividualEngagementDetails { get; set; } = new();
        public TeamConditionsDetailsDto TeamConditionsDetails { get; set; } = new();
        public ResponseRateDetailsDto ResponseRateDetails { get; set; } = new();

        /// <summary>
        /// False when the caller's plan has no Interactive Team Health Timeline (Light): the
        /// Team Health Dashboard values (current scores, response rate, drill-downs, custom
        /// questions, actions) are still returned, the 30-day chart series and milestones are
        /// empty, and TimelineUnavailableMessage says why.
        /// </summary>
        public bool IsTimelineAvailable { get; set; } = true;
        public string? TimelineUnavailableMessage { get; set; }
    }

    public class TimelineSectionDto
    {
        public TimelinePeriodDto Period { get; set; } = new();
        public ScoreSeriesDto IndividualEngagement { get; set; } = new();
        public ScoreSeriesDto TeamConditions { get; set; } = new();
        public ResponseRateSeriesDto ResponseRate { get; set; } = new();

        /// <summary>Milestones inside the period, oldest first, for the dots on the chart.</summary>
        public List<TeamMilestoneDto> Milestones { get; set; } = new();

        /// <summary>
        /// Engine actions delivered inside the period, oldest first, for the action dots.
        /// Status IN_FLIGHT draws the open ring, CLOSED the tick; the panel behind a dot is
        /// GET api/team-health/actions/{actionId}.
        /// </summary>
        public List<TimelineActionDto> Actions { get; set; } = new();

        /// <summary>
        /// Custom questions asked inside the period, oldest first, for the custom
        /// question dots and their card. Answers are anonymous.
        /// </summary>
        public List<TimelineCustomQuestionDto> CustomQuestions { get; set; } = new();
    }

    /// <summary>One custom question on the timeline: "6 of 8 answered · 75%" and the answers.</summary>
    public class TimelineCustomQuestionDto
    {
        public uint QuestionId { get; set; }
        public string QuestionText { get; set; } = string.Empty;

        /// <summary>The day it was asked, "YYYY-MM-DD".</summary>
        public string Date { get; set; } = string.Empty;

        /// <summary>Answers given, including ones left without text.</summary>
        public int ResponseCount { get; set; }

        /// <summary>Team size. Null when the team has no size set.</summary>
        public int? EligibleRespondentCount { get; set; }

        /// <summary>ResponseCount as a percent of EligibleRespondentCount, 0–100. Null when the team has no size set.</summary>
        public int? ResponseRate { get; set; }

        /// <summary>The answers that have text. No respondent details.</summary>
        public List<TimelineCustomQuestionAnswerDto> Responses { get; set; } = new();
    }

    public class TimelineCustomQuestionAnswerDto
    {
        public uint ResponseId { get; set; }
        public string ResponseText { get; set; } = string.Empty;
    }

    /// <summary>Inclusive "YYYY-MM-DD" dates.</summary>
    public class TimelinePeriodDto
    {
        public string StartDate { get; set; } = string.Empty;
        public string EndDate { get; set; } = string.Empty;
    }

    public class ScoreSeriesDto
    {
        /// <summary>Score for the whole period, 0–100.</summary>
        public int? Current { get; set; }

        /// <summary>Current minus the previous 30 days, in points.</summary>
        public int? Change { get; set; }

        /// <summary>One point per day that has answers, oldest first.</summary>
        public List<DailyScoreDto> Data { get; set; } = new();
    }

    public class DailyScoreDto
    {
        public string Date { get; set; } = string.Empty;
        public int Score { get; set; }
    }

    public class ResponseRateSeriesDto
    {
        /// <summary>Percent over the whole window, 0–100.</summary>
        public int? Current { get; set; }
        public string Period { get; set; } = ResponseRatePeriods.LastSevenWorkingDays;

        /// <summary>One entry per working day in the window, oldest first.</summary>
        public List<DailyRateDto> Data { get; set; } = new();
    }

    public class DailyRateDto
    {
        public string Date { get; set; } = string.Empty;

        /// <summary>Null when the team has no size set, so nothing can be expected.</summary>
        public int? Rate { get; set; }
    }

    /// <summary>The Response rate popup. Same fields as the timeline card, plus the summary.</summary>
    public class ResponseRateDetailsDto
    {
        public int? Current { get; set; }
        public string Period { get; set; } = ResponseRatePeriods.LastSevenWorkingDays;
        public List<DailyRateDto> Data { get; set; } = new();

        /// <summary>
        /// True when, on any day in the window, more people answered than the team size the
        /// manager set: the size is out of date. That day's rate is capped at 100. Always false
        /// for a team not on the engine.
        /// </summary>
        public bool TeamSizeExceeded { get; set; }

        public ResponseRateSummaryDto Summary { get; set; } = new();
    }

    public class ResponseRateSummaryDto
    {
        /// <summary>The rule on every team: each screen is an answer of its own, so one screen is a partial.</summary>
        public const string Explanation =
            "Screen 1 and screen 2 are counted separately, so a day where someone answers the first question only "
            + "still counts as a partial. A team below 60% is telling you something on its own.";

        /// <summary>Screens answered over the window (a member answering both screens is 2).</summary>
        public int Answers { get; set; }

        /// <summary>Team size x 2 screens x asked days. Null when nothing could be expected.</summary>
        public int? Possible { get; set; }

        /// <summary>
        /// The line under the popup's bars, ready to show (client wireframe: "47 answers from 56
        /// possible. Screen 1 and screen 2 are counted separately, …").
        /// </summary>
        public string Text => Possible is int possible
            ? $"{Answers} answers from {possible} possible. {Explanation}"
            : $"No data yet. {Explanation}";
    }

    public static class ResponseRatePeriods
    {
        public const string LastSevenWorkingDays = "last_7_working_days";
    }

    /// <summary>EXCELLENT, GOOD, NEEDS_WORK or URGENT_ATTENTION (FeedbackScoreThresholds bands).</summary>
    public static class ScoreStatuses
    {
        public const string Excellent = "EXCELLENT";
        public const string Good = "GOOD";
        public const string NeedsWork = "NEEDS_WORK";
        public const string UrgentAttention = "URGENT_ATTENTION";
    }

    public class DimensionScoreDto
    {
        public int? Score { get; set; }
        public string? Status { get; set; }
        public int? Change { get; set; }
    }

    /// <summary>The six CLOVER dimensions.</summary>
    public class IndividualEngagementDetailsDto
    {
        public DimensionScoreDto Communication { get; set; } = new();
        public DimensionScoreDto Learning { get; set; } = new();
        public DimensionScoreDto Opportunities { get; set; } = new();
        public DimensionScoreDto Vulnerability { get; set; } = new();
        public DimensionScoreDto Enablement { get; set; } = new();
        public DimensionScoreDto Reflection { get; set; } = new();

        /// <summary>
        /// The line under the six conditions (client wireframe: "Screen 1 completion 82% · Screen 2
        /// completion 74% — tracked separately so a module can never break someone's streak").
        /// </summary>
        public ScreenCompletionDto ScreenCompletion { get; set; } = new();

        /// <summary>The popup's footer key, from the same thresholds the statuses use.</summary>
        public List<ScoreBandDto> Legend { get; set; } = new();
    }

    /// <summary>
    /// One entry of the score key ("85+ excellent"), from FeedbackScoreThresholds, so the key and
    /// the statuses always agree. Highest band first.
    /// </summary>
    public class ScoreBandDto
    {
        /// <summary>EXCELLENT, GOOD, NEEDS_WORK or URGENT_ATTENTION, as on each score.</summary>
        public string Status { get; set; } = string.Empty;
        public int Min { get; set; }
        public int Max { get; set; }
        /// <summary>Ready to show: "85+ excellent", "65–84 good", "35–64 needs work", "0–34 urgent attention".</summary>
        public string Label { get; set; } = string.Empty;
        /// <summary>The dot colour the page uses for this band.</summary>
        public string Color { get; set; } = string.Empty;
    }

    /// <summary>
    /// Each check-in screen's answers against what was possible, over the response rate's window
    /// (the last seven working days up to yesterday). Screen 1 is the individual question, screen 2
    /// the team question. Null when nothing could be expected: no team size, or nobody enrolled.
    /// </summary>
    public class ScreenCompletionDto
    {
        public const string Explanation = "tracked separately so a module can never break someone's streak.";

        public int? Screen1 { get; set; }
        public int? Screen2 { get; set; }
        public string Period { get; set; } = ResponseRatePeriods.LastSevenWorkingDays;

        /// <summary>
        /// The line ready to show, from the two figures above. Without them it is the line the
        /// popup has always shown.
        /// </summary>
        public string Text => Screen1 is int s1 && Screen2 is int s2
            ? $"Screen 1 completion {s1}% · Screen 2 completion {s2}% — {Explanation}"
            : $"Screen 1 and screen 2 are {Explanation}";
    }

    public class ConditionScoreDto
    {
        public string Name { get; set; } = string.Empty;
        public int Score { get; set; }
    }

    public class ConditionGroupDto
    {
        /// <summary>Mean of the group's conditions that have answers.</summary>
        public int? Score { get; set; }
        public string? Status { get; set; }

        /// <summary>The group's lowest-scoring condition; null when none has answers.</summary>
        public ConditionScoreDto? Lowest { get; set; }
    }

    /// <summary>The three Team Conditions groups.</summary>
    public class TeamConditionsDetailsDto
    {
        public ConditionGroupDto DirectionAndLearning { get; set; } = new();
        public ConditionGroupDto DesignAndMechanics { get; set; } = new();
        public ConditionGroupDto WhatTeamBelieves { get; set; } = new();

        /// <summary>The line under the three groups: the weakest group and how it is fixed, then the wireframe's explanation.</summary>
        public TeamConditionsSummaryDto Summary { get; set; } = new();

        /// <summary>The popup's footer key, the same as Individual Engagement's.</summary>
        public List<ScoreBandDto> Legend { get; set; } = new();
    }

    public class TeamConditionsSummaryDto
    {
        public const string Explanation =
            "Each group is the mean of its conditions. The groups differ in how a problem is fixed: mechanics have "
            + "structural fixes, the performance cycle needs a rhythm, and the human layer responds only to evidence over time.";

        /// <summary>"Direction and learning", "Design and mechanics" or "What the team believes". Null with no answers.</summary>
        public string? LowestGroup { get; set; }
        public int? LowestScore { get; set; }

        /// <summary>Ready to show. Without answers it is the wireframe's explanation alone.</summary>
        public string Text { get; set; } = Explanation;
    }
}
