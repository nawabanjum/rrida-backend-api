using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models.TeamQuestions
{
    // ═════════════════════════════════════════════════════════════════════════
    // Custom Questions — a question authored by a company for one of its units.
    //
    // The question is owned by a tenant: ProfileId plus UnitId say exactly which
    // company and which business unit it belongs to. It is scheduled with a single
    // ScheduledAt stamp and carries a lifecycle Status.
    //
    // Answers come back into CustomQuestionDailyFeedback — free text only, so the
    // table holds the question reference and the text and nothing else.
    //
    // Both tables are snapshotted on every write into CustomQuestionAudits and
    // CustomQuestionDailyFeedbackAudits, following the same row-snapshot audit
    // convention as LLMReportScheduleAudits and CompanyLicenseAuditLogs.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Lifecycle of a custom question, driven by its scheduling stamp.</summary>
    public enum CustomQuestionStatus
    {
        Draft = 1,
        Scheduled = 2,
        Active = 3,
        Expired = 4,
        Cancelled = 5
    }

    /// <summary>
    /// A custom question belonging to one company and one of its business units.
    /// </summary>
    public class CustomQuestion
    {
        public uint Id { get; init; }

        /// <summary>Same shape as DailyQuestion.Text and TeamDailyQuestion.Text.</summary>
        [MaxLength(1000)]
        public required string Text { get; set; }

        [ForeignKey(nameof(ContentCulture))]
        public required uint ContentCultureId { get; set; }

        public virtual ContentCulture? ContentCulture { get; set; }

        // ── Ownership of the question ──
        /// <summary>The company the question belongs to.</summary>
        [ForeignKey(nameof(Profile))]
        public required uint ProfileId { get; set; }

        public virtual Profile? Profile { get; set; }

        /// <summary>The business unit within that company the question belongs to.</summary>
        [ForeignKey(nameof(BusinessUnit))]
        public required uint UnitId { get; set; }

        public virtual BusinessUnit? BusinessUnit { get; set; }

        // ── Scheduling ──
        /// <summary>When the question is due to be asked. Null means not scheduled yet.</summary>
        public DateTime? ScheduledAt { get; set; }

        public required CustomQuestionStatus Status { get; set; }

        // ── House audit / soft-delete convention ──
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsDel { get; set; }
    }

    /// <summary>
    /// One free-text answer to a custom question.
    ///
    /// Deliberately narrow: a custom question has no reason set and no sentiment
    /// scale, so the only thing to record is what the respondent typed — plus the
    /// business unit it came from.
    /// </summary>
    [Table("CustomQuestionDailyFeedback", Schema = "public")]
    public class CustomQuestionDailyFeedback
    {
        public uint Id { get; init; }

        /// <summary>The custom question this answer was given to.</summary>
        [ForeignKey(nameof(CustomQuestion))]
        public required uint CustomQuestionId { get; set; }

        public virtual CustomQuestion? CustomQuestion { get; set; }

        /// <summary>
        /// The business unit the answer was submitted from.
        ///
        /// Reachable through CustomQuestion.UnitId, and stored here anyway: answers are
        /// reported per unit, and reaching the unit through a join on every such read is
        /// the kind of thing TeamDailyFeedback already avoids by carrying its own unit.
        /// It must equal the owning question's UnitId — the repository refuses anything
        /// else, so the two can never disagree.
        /// </summary>
        [ForeignKey(nameof(BusinessUnit))]
        public required uint UnitId { get; set; }

        public virtual BusinessUnit? BusinessUnit { get; set; }

        [MaxLength(300)]
        public string? FreeText { get; set; }

        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Full history of every custom question: what it said, who it belonged to,
    /// who changed it, when, and what the change was.
    ///
    /// Follows the row-snapshot convention already used by LLMReportScheduleAudits and
    /// CompanyLicenseAuditLogs — one row per operation holding the state at that
    /// moment, so history is readable without reconstructing it from diffs.
    /// </summary>
    [Table("CustomQuestionAudits", Schema = "public")]
    public class CustomQuestionAudit
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AuditId { get; set; }

        /// <summary>The custom question this row describes.</summary>
        public uint CustomQuestionId { get; set; }

        // ── Snapshot of the question at log time ──
        [MaxLength(1000)]
        public string Text { get; set; } = string.Empty;

        public uint ContentCultureId { get; set; }
        public uint ProfileId { get; set; }
        public uint UnitId { get; set; }
        public DateTime? ScheduledAt { get; set; }
        public CustomQuestionStatus Status { get; set; }

        /// <summary>
        /// What actually changed, in words — "moved from Draft to Scheduled",
        /// "ScheduledAt moved forward by 7 days". The snapshot columns say what the
        /// state became; this says why the row exists.
        /// </summary>
        [MaxLength(1000)]
        public string? ChangeSummary { get; set; }

        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsDel { get; set; }

        public DateTime LoggedAt { get; set; } = DateTime.UtcNow;
        public int VersionNumber { get; set; } = 1;

        /// <summary>INSERT, UPDATE, DELETE, or a status transition such as ACTIVATE.</summary>
        [MaxLength(20)]
        public string OperationType { get; set; } = string.Empty;
    }

    /// <summary>
    /// Full history of every answer to a custom question. Same row-snapshot
    /// convention as CustomQuestionAudits and DailyFeedbackAuditLogs.
    /// </summary>
    [Table("CustomQuestionDailyFeedbackAudits", Schema = "public")]
    public class CustomQuestionDailyFeedbackAudit
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AuditId { get; set; }

        /// <summary>The CustomQuestionDailyFeedback row this snapshot describes.</summary>
        public uint CustomQuestionDailyFeedbackId { get; set; }

        // ── Snapshot of the answer at log time ──
        public uint CustomQuestionId { get; set; }

        /// <summary>The business unit the answer was submitted from, at log time.</summary>
        public uint UnitId { get; set; }

        [MaxLength(300)]
        public string? FreeText { get; set; }

        [MaxLength(1000)]
        public string? ChangeSummary { get; set; }

        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public DateTime LoggedAt { get; set; } = DateTime.UtcNow;
        public int VersionNumber { get; set; } = 1;

        [MaxLength(20)]
        public string OperationType { get; set; } = string.Empty;
    }
}
