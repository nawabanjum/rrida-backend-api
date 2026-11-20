namespace CloverBackoffice.Infrastructure.DTO.TeamHealth
{
    // "Schedule a custom question" on the Team Health page. A one-off question asked
    // once, on a chosen working day, after the two standard check-in questions.
    // Stored in CustomQuestions (Status Scheduled, ScheduledAt = askOn 16:00 UTC).

    /// <summary>Body of POST api/team-health/custom-questions.</summary>
    public class ScheduleCustomQuestionDto
    {
        /// <summary>The team (business unit) to ask.</summary>
        public uint TeamId { get; set; }

        /// <summary>The question, at most 140 characters.</summary>
        public string? Question { get; set; }

        /// <summary>"YYYY-MM-DD" or "DD-MM-YYYY". A working day, today or later.</summary>
        public string? AskOn { get; set; }
    }

    /// <summary>The modal's two lists and the slot counter.</summary>
    public class TeamCustomQuestionsDto
    {
        public string TeamId { get; set; } = string.Empty;

        /// <summary>Most questions a team can have scheduled at once (3).</summary>
        public int MaxSlots { get; set; }

        /// <summary>How many are scheduled now — "1 of 3 slots used".</summary>
        public int SlotsUsed { get; set; }

        /// <summary>Upcoming questions, soonest first.</summary>
        public List<ScheduledCustomQuestionDto> Scheduled { get; set; } = new();

        /// <summary>Questions already asked, most recent first.</summary>
        public List<AskedCustomQuestionDto> PreviouslyAsked { get; set; } = new();
    }

    public class ScheduledCustomQuestionDto
    {
        public uint Id { get; set; }
        public string Question { get; set; } = string.Empty;

        /// <summary>"YYYY-MM-DD".</summary>
        public string AskOn { get; set; } = string.Empty;

        /// <summary>False once anyone has answered it (today's question after the check-in).</summary>
        public bool CanRemove { get; set; }
    }

    public class AskedCustomQuestionDto
    {
        public uint Id { get; set; }
        public string Question { get; set; } = string.Empty;

        /// <summary>"YYYY-MM-DD".</summary>
        public string AskedOn { get; set; } = string.Empty;

        /// <summary>Number of answers given.</summary>
        public int Answers { get; set; }

        /// <summary>Team size — how many could have answered. Null when the team has no size set.</summary>
        public int? Possible { get; set; }

        /// <summary>Answers as a percent of team size, 0–100. Null when the team has no size set.</summary>
        public int? ResponseRate { get; set; }

        /// <summary>The answers that have text, anonymous. Same shape as the timeline's customQuestions[].responses.</summary>
        public List<TimelineCustomQuestionAnswerDto> Responses { get; set; } = new();
    }
}
