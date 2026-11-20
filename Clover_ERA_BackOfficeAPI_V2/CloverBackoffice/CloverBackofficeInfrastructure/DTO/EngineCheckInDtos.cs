using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    /// <summary>Enrol a member on a team, using the team's join code.</summary>
    public class EngineEnrolRequestDto
    {
        /// <summary>The team's short code, as the app already collects it.</summary>
        public string TeamCode { get; set; } = string.Empty;

        /// <summary>
        /// The app's own member token. Stored only as a hash, so it is the one thing that
        /// must survive a reinstall; a new token is a new member.
        /// </summary>
        public string MemberToken { get; set; } = string.Empty;
    }

    /// <summary>
    /// Leave a team: the member has been removed from it in the app. Their membership ends
    /// today, so the floor's denominator stops counting them from tomorrow (the MVP roster's
    /// leftDay). Their answers stay: they happened.
    /// </summary>
    public class EngineLeaveRequestDto
    {
        public string MemberToken { get; set; } = string.Empty;
        public uint BusinessUnitId { get; set; }
    }

    public class EngineLeaveResultDto
    {
        public uint BusinessUnitId { get; set; }

        /// <summary>The member's last day on the team.</summary>
        public DateOnly EffectiveTo { get; set; }
    }

    public class EngineEnrolResultDto
    {
        public string EngineMemberKey { get; set; } = string.Empty;
        public uint BusinessUnitId { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public DateOnly EffectiveFrom { get; set; }
        public bool AlreadyEnrolled { get; set; }
    }

    /// <summary>One answer: a level, then chips or free text.</summary>
    public class EngineAnswerRequestDto
    {
        public string MemberToken { get; set; } = string.Empty;
        public uint BusinessUnitId { get; set; }

        /// <summary>"individual" or "team".</summary>
        public string Track { get; set; } = "individual";

        /// <summary>red, orange, yellow or green.</summary>
        public string Level { get; set; } = string.Empty;

        /// <summary>Zero to three chip ids from the day's question, e.g. "191-N3".</summary>
        public List<string> Chips { get; set; } = new();

        /// <summary>Required when no chip is chosen. A reason is compulsory.</summary>
        public string? FreeText { get; set; }

        /// <summary>
        /// Optional: the team question's answer, saved with this one. Leave null when the
        /// top-level answer is already the team track.
        /// </summary>
        public EngineTrackAnswerDto? Team { get; set; }

        /// <summary>Optional: the answer to today's custom question.</summary>
        public EngineCustomAnswerDto? CustomQuestion { get; set; }
    }

    /// <summary>A second track's answer: a level, then chips or free text.</summary>
    public class EngineTrackAnswerDto
    {
        /// <summary>red, orange, yellow or green.</summary>
        public string Level { get; set; } = string.Empty;

        /// <summary>Zero to three chip ids from the team question, e.g. "305-N1".</summary>
        public List<string> Chips { get; set; } = new();

        /// <summary>Required when no chip is chosen.</summary>
        public string? FreeText { get; set; }
    }

    /// <summary>The custom question's answer, as submit-daily-feedback takes it.</summary>
    public class EngineCustomAnswerDto
    {
        /// <summary>customQuestion.customDailyQuestionId from the today endpoint.</summary>
        public uint CustomDailyQuestionId { get; set; }

        /// <summary>What the member typed, at most 300 characters. Optional.</summary>
        public string? FreeText { get; set; }

        /// <summary>True for "Skip and finish": recorded as a row with no text.</summary>
        public bool Skipped { get; set; }
    }

    public class EngineAnswerResultDto
    {
        public long AnswerId { get; set; }
        public int Day { get; set; }
        public int AskedDayIndex { get; set; }
        public int QuestionKey { get; set; }
        public string Track { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public int ChipsRecorded { get; set; }
        public bool FreeTextRecorded { get; set; }

        /// <summary>The team answer saved with this one, or null when none was sent.</summary>
        public EngineAnswerResultDto? Team { get; set; }

        /// <summary>The CustomQuestionDailyFeedback row id, or null when none was sent.</summary>
        public long? CustomQuestionFeedbackId { get; set; }
    }

    /// <summary>The question a member is asked today, with its chip set for the level they pick.</summary>
    public class EngineTodayDto
    {
        public uint BusinessUnitId { get; set; }
        public DateOnly Date { get; set; }
        public bool IsAskedToday { get; set; }
        public int Day { get; set; }
        public int AskedDayIndex { get; set; }
        public List<EngineTodayQuestionDto> Questions { get; set; } = new();

        /// <summary>
        /// The team's custom question for today, as fetch-daily-question returns it;
        /// null when there is none.
        /// </summary>
        public Mobile.CustomQuestionFetchResponse? CustomQuestion { get; set; }
    }

    /// <summary>
    /// The team's next asked days after today (up to five), each in the same shape as
    /// today, so the app can prefetch them and put each day's text on its reminder.
    /// </summary>
    public class EngineNextDto
    {
        public uint BusinessUnitId { get; set; }
        public List<EngineTodayDto> Days { get; set; } = new();
    }

    public class EngineTodayQuestionDto
    {
        public string Track { get; set; } = string.Empty;
        public int QuestionKey { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Element { get; set; } = string.Empty;
        public bool AlreadyAnswered { get; set; }

        /// <summary>Chip sets by polarity: negative, indifferent, positive.</summary>
        public Dictionary<string, List<RefChipDto>> Chips { get; set; } = new();
    }
}
