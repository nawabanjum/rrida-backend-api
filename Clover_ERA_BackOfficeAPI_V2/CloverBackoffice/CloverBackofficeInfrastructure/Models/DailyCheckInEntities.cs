using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CloverBackoffice.Infrastructure.Models.TeamQuestions;

namespace CloverBackoffice.Infrastructure.Models
{
    // ═════════════════════════════════════════════════════════════════════════
    // Daily check-in: multi-select reasons and optional team feedback.
    //
    // The app now lets a member pick several reasons per question. The legacy
    // single-reason columns (DailyFeedback.DailyReasonId, TeamDailyFeedback.
    // TeamDailyReasonId) keep the FIRST selected reason so every existing reader
    // works unchanged; the full selection is stored as a set in the tables below.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>One selected reason of a DailyFeedback answer.</summary>
    [Table("DailyFeedbackReasons", Schema = "public")]
    public class DailyFeedbackReason
    {
        public uint Id { get; init; }

        [ForeignKey(nameof(DailyFeedback))]
        public uint DailyFeedbackId { get; set; }

        public virtual DailyFeedback? DailyFeedback { get; set; }

        [ForeignKey(nameof(Reason))]
        public uint DailyReasonId { get; set; }

        public virtual DailyReason? Reason { get; set; }
    }

    /// <summary>One selected reason of a TeamDailyFeedback answer.</summary>
    [Table("TeamDailyFeedbackReasons", Schema = "public")]
    public class TeamDailyFeedbackReason
    {
        public uint Id { get; init; }

        [ForeignKey(nameof(TeamDailyFeedback))]
        public uint TeamDailyFeedbackId { get; set; }

        public virtual TeamDailyFeedback? TeamDailyFeedback { get; set; }

        [ForeignKey(nameof(Reason))]
        public uint TeamDailyReasonId { get; set; }

        public virtual TeamDailyReason? Reason { get; set; }
    }

    /// <summary>
    /// Optional "Any other feedback about your team?" text from the check-in
    /// wrap-up screen. Anonymous like daily feedback: only the team is stored.
    /// </summary>
    [Table("TeamFeedback", Schema = "public")]
    public class TeamFeedback
    {
        public uint Id { get; init; }

        [ForeignKey(nameof(BusinessUnit))]
        public uint BusinessUnitId { get; set; }

        public virtual BusinessUnit? BusinessUnit { get; set; }

        [MaxLength(300)]
        public required string FeedbackText { get; set; }

        /// <summary>When the member submitted it, as reported by the app (UTC).</summary>
        public DateTime ClientSubmittedAtUtc { get; set; }

        /// <summary>When the server received it (UTC).</summary>
        public DateTime SubmissionDateTime { get; set; }
    }
}
