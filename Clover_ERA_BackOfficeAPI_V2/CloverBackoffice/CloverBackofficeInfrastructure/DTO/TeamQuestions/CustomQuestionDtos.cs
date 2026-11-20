using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Custom Questions — API contracts.
    //
    // A custom question belongs to one company and one of its business units, is
    // scheduled with a single stamp, and carries a lifecycle status. Answers to it
    // are free text and come back through the feedback contracts below.
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Body of the custom-question add endpoint.</summary>
    public class CreateCustomQuestionDto
    {
        public string QuestionText { get; set; } = string.Empty;

        /// <summary>The company the question belongs to.</summary>
        public long ProfileId { get; set; }

        /// <summary>The business unit within that company the question belongs to.</summary>
        public long UnitId { get; set; }

        /// <summary>When the question is due to be asked. Null means not scheduled yet.</summary>
        public DateTime? ScheduledAt { get; set; }

        /// <summary>
        /// Draft, Scheduled, Active, Expired or Cancelled. Left out, the status is derived
        /// from the stamp: a future ScheduledAt is Scheduled, anything else is Draft.
        /// </summary>
        public string? Status { get; set; }
    }

    /// <summary>A stored custom question, as returned after a write.</summary>
    public class CustomQuestionRecordDto
    {
        public long CustomQuestionId { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public long ProfileId { get; set; }
        public long UnitId { get; set; }
        public DateTime? ScheduledAt { get; set; }
        public string Status { get; set; } = string.Empty;
        public int? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Outcome of an add. A validation failure carries the reason and no record; the
    /// controller turns that into a failed response without a 500.
    /// </summary>
    public class CreateCustomQuestionResultDto
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public CustomQuestionRecordDto? Question { get; set; }
    }

    /// <summary>Body of the custom-question feedback endpoint.</summary>
    public class CreateCustomQuestionFeedbackDto
    {
        public long CustomQuestionId { get; set; }

        /// <summary>
        /// The business unit the answer is submitted from. Required, and it has to be the
        /// unit that owns the question — the read endpoint returns it as <c>unitId</c> on
        /// every question, so the caller already has the right value to send back.
        /// </summary>
        public long UnitId { get; set; }

        /// <summary>What the respondent typed. Capped at 300 characters by the column.</summary>
        public string? FreeText { get; set; }
    }

    /// <summary>One stored answer to a custom question.</summary>
    public class CustomQuestionFeedbackRecordDto
    {
        public long FeedbackId { get; set; }
        public long CustomQuestionId { get; set; }

        /// <summary>The business unit the answer was submitted from.</summary>
        public long UnitId { get; set; }

        public string? FreeText { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>Outcome of a feedback write, shaped like the add result.</summary>
    public class CreateCustomQuestionFeedbackResultDto
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public CustomQuestionFeedbackRecordDto? Feedback { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Reading the questions due for a business unit today.
    //
    // A custom question carries a single ScheduledAt stamp, not a from/to pair
    // (see the design note on CustomQuestionEntities). Its validity period is
    // therefore the UTC calendar day that stamp falls in: it becomes readable
    // at the stamp and stops being readable when that day ends.
    //
    // The day is the backend's to decide. The caller names a unit and nothing
    // else — it asks what its unit should be answering now, not what was due on
    // some other date.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>One custom question due for a unit, with the unit it belongs to.</summary>
    public class ScheduledCustomQuestionDto
    {
        public long CustomQuestionId { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public long ProfileId { get; set; }
        public long UnitId { get; set; }

        /// <summary>Echoed back so a caller who asked by short code can match rows up.</summary>
        public string UnitShortCode { get; set; } = string.Empty;

        public string UnitName { get; set; } = string.Empty;

        public DateTime? ScheduledAt { get; set; }

        /// <summary>
        /// End of the question's validity period — midnight UTC after the scheduled day.
        /// Once the clock passes this the question no longer answers this endpoint, and
        /// the expiry job moves it to Expired.
        /// </summary>
        public DateTime? ValidUntil { get; set; }

        public string Status { get; set; } = string.Empty;
    }

    /// <summary>
    /// The questions due for one unit today, with the resolved unit so the caller can
    /// confirm which unit a short code matched.
    /// </summary>
    public class ScheduledCustomQuestionsDto
    {
        /// <summary>
        /// The day the questions were selected for — midnight UTC today, decided by the
        /// backend. Echoed back so a client can tell which day an answer belongs to
        /// without having to agree on a clock.
        /// </summary>
        public DateTime Date { get; set; }

        public long ProfileId { get; set; }
        public long UnitId { get; set; }
        public string UnitShortCode { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;

        public int TotalCount { get; set; }

        public List<ScheduledCustomQuestionDto> Questions { get; set; } = new List<ScheduledCustomQuestionDto>();
    }

    /// <summary>
    /// Outcome of a read. Shaped like the write results: an unknown or ambiguous unit
    /// comes back as a failed result carrying the reason, so the controller can answer
    /// without a 500. A unit that simply has nothing due is a success with an empty list.
    /// </summary>
    public class ScheduledCustomQuestionsResultDto
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public ScheduledCustomQuestionsDto? Data { get; set; }
    }

    /// <summary>
    /// What one pass of the custom-question expiry job did. Returned so the background
    /// service can log it and so the same call can be driven from a test or a tool.
    /// </summary>
    public class ExpireCustomQuestionsResultDto
    {
        /// <summary>The UTC instant the pass treated as "now".</summary>
        public DateTime EvaluatedAtUtc { get; set; }

        public int ExpiredCount { get; set; }

        /// <summary>Ids moved to Expired by this pass. Empty when there was nothing to do.</summary>
        public List<long> ExpiredQuestionIds { get; set; } = new List<long>();
    }
}
