using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models.TeamHealth
{
    // ═════════════════════════════════════════════════════════════════════════
    // Team Health — the manager's record of an engine action.
    //
    // The action itself is the engine's: one EngineCycleDelivery row per rung, with
    // the text as delivered and the engine's own outcome. Those rows are DERIVED and
    // rebuilt on every engine run, so nothing a manager does is written there. What
    // the manager does from the timeline — reword it, set a due date, mark it closed,
    // reopen it, comment on it — lives here, beside it, and never changes what the
    // engine reads or decides.
    //
    // The link is the engine's natural key (team, cycle key, rung), not the delivery
    // row id, so a re-run that rewrites the delivery keeps the manager's record.
    // ActionKey is kept as well: when a team's engine data is reset and "C1" is reused
    // for a different action, a record whose ActionKey no longer matches is ignored
    // rather than shown against the wrong action.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>The two statuses a manager sets from the action details panel.</summary>
    public static class TeamActionStatuses
    {
        public const string InFlight = "IN_FLIGHT";
        public const string Closed = "CLOSED";
    }

    /// <summary>
    /// The manager's version of one engine action (one rung of one cycle). Created the
    /// first time a manager edits, closes or reopens it; until then the action reads
    /// as the engine delivered it.
    /// </summary>
    [Table("TeamActionStates", Schema = "public")]
    public class TeamActionState
    {
        public const int TextMaxLength = 1000;

        public long Id { get; init; }

        [ForeignKey(nameof(Profile))]
        public required uint ProfileId { get; set; }

        public virtual Profile? Profile { get; set; }

        [ForeignKey(nameof(BusinessUnit))]
        public required uint UnitId { get; set; }

        public virtual BusinessUnit? BusinessUnit { get; set; }

        /// <summary>The engine's cycle id, "C1". With UnitId and Rung, the natural key.</summary>
        [MaxLength(20)]
        public required string CycleKey { get; set; }

        public int Rung { get; set; }

        /// <summary>The engine action this record was made against.</summary>
        [MaxLength(30)]
        public required string ActionKey { get; set; }

        /// <summary>The manager's wording. Null = the text as the engine delivered it.</summary>
        [MaxLength(TextMaxLength)]
        public string? Text { get; set; }

        public DateOnly? DueDate { get; set; }

        /// <summary>IN_FLIGHT or CLOSED. Null = follow the engine's outcome.</summary>
        [MaxLength(20)]
        public string? Status { get; set; }

        public DateTime? ClosedAt { get; set; }
        public int? ClosedBy { get; set; }

        /// <summary>Bumped on every change; a stale write (an older version) is refused.</summary>
        public int Version { get; set; }

        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>A manager's comment on an engine action.</summary>
    [Table("TeamActionComments", Schema = "public")]
    public class TeamActionComment
    {
        public const int TextMaxLength = 500;

        public long Id { get; init; }

        [ForeignKey(nameof(Profile))]
        public required uint ProfileId { get; set; }

        public virtual Profile? Profile { get; set; }

        [ForeignKey(nameof(BusinessUnit))]
        public required uint UnitId { get; set; }

        public virtual BusinessUnit? BusinessUnit { get; set; }

        [MaxLength(20)]
        public required string CycleKey { get; set; }

        public int Rung { get; set; }

        [MaxLength(30)]
        public required string ActionKey { get; set; }

        [MaxLength(TextMaxLength)]
        public required string Text { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsDel { get; set; }
    }

    /// <summary>
    /// History of every manager change to an action — the same row-snapshot convention
    /// as CustomQuestionAudits: what the record became, who did it, when, and why the
    /// row exists.
    /// </summary>
    [Table("TeamActionAudits", Schema = "public")]
    public class TeamActionAudit
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long AuditId { get; set; }

        public uint ProfileId { get; set; }
        public uint UnitId { get; set; }

        [MaxLength(20)]
        public string CycleKey { get; set; } = string.Empty;

        public int Rung { get; set; }

        [MaxLength(30)]
        public string ActionKey { get; set; } = string.Empty;

        /// <summary>EDIT, CLOSE, REOPEN or COMMENT.</summary>
        [MaxLength(20)]
        public string OperationType { get; set; } = string.Empty;

        // ── Snapshot after the change ──
        [MaxLength(TeamActionState.TextMaxLength)]
        public string? Text { get; set; }

        public DateOnly? DueDate { get; set; }

        [MaxLength(20)]
        public string? Status { get; set; }

        public int Version { get; set; }

        /// <summary>The comment, on a COMMENT row.</summary>
        public long? CommentId { get; set; }

        [MaxLength(1000)]
        public string? ChangeSummary { get; set; }

        public int? ByUserId { get; set; }
        public DateTime LoggedAt { get; set; }
    }
}
