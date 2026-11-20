using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models.TeamSetups
{
    // ═════════════════════════════════════════════════════════════════════════
    // RRIDA Team Setup — what the manager tells us about a team when the team is
    // set up, beyond the name and size the business unit already carries.
    //
    //   BusinessUnits   Name, TeamSize and BusinessUnitShortCode (the invite code)
    //                   already hold steps 1 and 5 of the wizard. Untouched.
    //   TeamSetups      one row per unit: the team type and the manager's context.
    //   TeamGoals       two to five goals per unit, ordered, soft-deleted.
    //   TeamGoalAudits  the full history of every goal.
    //
    // Deliberately NOT columns on BusinessUnits. That table is the hub of both the
    // legacy CLOVER product and RRIDA — ten foreign keys point at it — and is read
    // whole by every hierarchy and listing endpoint. Setup is RRIDA-only, arrives
    // late (the units that exist today have none) and carries bulk text. A separate
    // row also removes the ambiguity a nullable column would create: the row
    // existing IS the "this team has been set up" flag.
    //
    // Ownership follows CustomQuestion: ProfileId plus UnitId say exactly which
    // company and which business unit a row belongs to.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// How the team works, from step 2 of the setup wizard. Changes which questions
    /// the check-in asks and what advice comes back.
    ///
    /// Not the same thing as BusinessUnit.ActiveDailyQuestionSetGroupId — that is the
    /// legacy CLOVER set selector, and every unit in the database points at the single
    /// seeded group. This is the RRIDA team-question switch and is new.
    /// </summary>
    public enum TeamType
    {
        /// <summary>People depend on each other for regular outputs and outcomes.</summary>
        Interdependent = 1,

        /// <summary>People with independent targets or tasks.</summary>
        IndependentContributors = 2
    }

    /// <summary>
    /// One team's setup. Exactly one live row per business unit, enforced by a
    /// filtered unique index on UnitId.
    ///
    /// Written once, at the end of the wizard, in the same transaction as the unit
    /// and its goals. So there is no half-finished state to model and no "completed"
    /// flag: if the row is here, the team is set up.
    /// </summary>
    [Table("TeamSetups", Schema = "public")]
    public class TeamSetup
    {
        public uint Id { get; init; }

        /// <summary>The company the team belongs to.</summary>
        [ForeignKey(nameof(Profile))]
        public required uint ProfileId { get; set; }

        public virtual Profile? Profile { get; set; }

        /// <summary>The business unit this setup describes. One live row per unit.</summary>
        [ForeignKey(nameof(BusinessUnit))]
        public required uint UnitId { get; set; }

        public virtual BusinessUnit? BusinessUnit { get; set; }

        public required TeamType TeamType { get; set; }

        /// <summary>
        /// The manager's own words about the team — how long it has been running,
        /// recent changes, anything that helps read the data. The wizard asks for at
        /// least 20 characters; the ceiling is generous because this is prose.
        /// </summary>
        [MaxLength(4000)]
        public required string Context { get; set; }

        // ── House audit / soft-delete convention ──
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsDel { get; set; }
    }

    /// <summary>
    /// One goal a team is working towards. Two to five per unit — the count is a
    /// wizard rule the service enforces, since no row-level constraint can count
    /// siblings.
    ///
    /// Goals are shown in the daily check-in beside the goal-anchored questions
    /// (TeamDailyQuestionSpecification.Variant = GoalAnchored), which is why they are
    /// rows with a stable id and an order rather than an array column on the unit.
    /// </summary>
    [Table("TeamGoals", Schema = "public")]
    public class TeamGoal
    {
        public uint Id { get; init; }

        [ForeignKey(nameof(Profile))]
        public required uint ProfileId { get; set; }

        public virtual Profile? Profile { get; set; }

        [ForeignKey(nameof(BusinessUnit))]
        public required uint UnitId { get; set; }

        public virtual BusinessUnit? BusinessUnit { get; set; }

        /// <summary>Same shape as TeamDailyQuestion.Text and CustomQuestion.Text.</summary>
        [MaxLength(1000)]
        public required string GoalText { get; set; }

        /// <summary>
        /// Position in the manager's list, 1-based. Unique per unit among live, active
        /// goals, so two goals can never claim the same slot.
        /// </summary>
        public short DisplayOrder { get; set; }

        /// <summary>
        /// The date the goal is aimed at, when the manager gave one. A goal with a
        /// number and a date is one the team can tell whether it has reached.
        /// </summary>
        public DateOnly? TargetDate { get; set; }

        /// <summary>
        /// Whether the goal is still being worked towards. Retiring a goal without
        /// deleting it keeps it out of the check-in but leaves past answers readable.
        /// </summary>
        public bool IsActive { get; set; } = true;

        // ── House audit / soft-delete convention ──
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// Removing a goal sets this, never a hard delete: reports and past answers
        /// need the wording as it stood when the question was asked.
        /// </summary>
        public bool IsDel { get; set; }
    }

    /// <summary>
    /// Full history of every goal: what it said, which team it belonged to, who
    /// changed it, when, and what the change was.
    ///
    /// Follows the row-snapshot convention already used by CustomQuestionAudits and
    /// TeamDailyFeedbackAuditLogs — one row per operation holding the state at that
    /// moment, and no foreign key back to the goal, so the history outlives even a
    /// hard delete.
    /// </summary>
    [Table("TeamGoalAudits", Schema = "public")]
    public class TeamGoalAudit
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AuditId { get; set; }

        /// <summary>The goal this row describes.</summary>
        public uint TeamGoalId { get; set; }

        // ── Snapshot of the goal at log time ──
        public uint ProfileId { get; set; }
        public uint UnitId { get; set; }

        [MaxLength(1000)]
        public string GoalText { get; set; } = string.Empty;

        public short DisplayOrder { get; set; }
        public DateOnly? TargetDate { get; set; }
        public bool IsActive { get; set; }

        /// <summary>
        /// What actually changed, in words — "goal reworded", "removed from the team's
        /// goals", "moved from position 3 to 1". The snapshot columns say what the
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

        /// <summary>INSERT, UPDATE, DELETE or REORDER.</summary>
        [MaxLength(20)]
        public string OperationType { get; set; } = string.Empty;
    }
}
