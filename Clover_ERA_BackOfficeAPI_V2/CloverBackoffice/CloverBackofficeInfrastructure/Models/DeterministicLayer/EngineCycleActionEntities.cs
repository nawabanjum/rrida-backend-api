using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models.DeterministicLayer
{
    // ═══════════════════════════════════════════════════════════════════════════
    // What the manager did — facts, like the answers.
    //
    // The replay rebuilds cycles from the answers, but a confirmation and a decline
    // are real-world events that no amount of replaying can derive. They are stored
    // here and fed back in on the day they happened, which is why each carries BOTH
    // the date and the asked-day index: the two are plain integers and confusing
    // them moves a confirmation to the wrong day, which moves the signal read with it.
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The 72-hour confirmation: one tap saying the action was taken, plus whether the
    /// worked-if condition was met. Nothing goes to the team at this point.
    /// </summary>
    public class EngineConfirmation
    {
        public long Id { get; init; }

        [ForeignKey(nameof(BusinessUnit))]
        public uint BusinessUnitId { get; set; }
        public virtual BusinessUnit? BusinessUnit { get; set; }

        /// <summary>The engine's own cycle id, "C1". The replay matches on this.</summary>
        [MaxLength(20)]
        public required string CycleKey { get; set; }

        /// <summary>Which rung was confirmed. A cycle that ladders is confirmed twice.</summary>
        public int Rung { get; set; }

        public DateOnly ConfirmedDate { get; set; }

        /// <summary>Days since the team's start date.</summary>
        public int Day { get; set; }

        /// <summary>Asked days at that point — the space every window counts in.</summary>
        public int AskedDayIndex { get; set; }

        /// <summary>
        /// The manager's own observation, separate from the data signal. It has to be able
        /// to fail honestly: performing the action and changing nothing is a false.
        /// </summary>
        public bool WorkedIfMet { get; set; }

        /// <summary>Who confirmed, for the audit trail.</summary>
        public int? ConfirmedByUserId { get; set; }

        [MaxLength(50)]
        public string Source { get; set; } = "manager";

        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// A contraindication the manager declined. The layer states them and never evaluates
    /// them: it does not know who is in a performance process and must not try to find out.
    /// A decline is recorded as a failed cycle with its reason, and succession moves on.
    /// </summary>
    public class EngineDecline
    {
        public long Id { get; init; }

        [ForeignKey(nameof(BusinessUnit))]
        public uint BusinessUnitId { get; set; }
        public virtual BusinessUnit? BusinessUnit { get; set; }

        [MaxLength(20)]
        public required string CycleKey { get; set; }

        public DateOnly DeclinedDate { get; set; }

        public int Day { get; set; }

        public int AskedDayIndex { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }

        public int? DeclinedByUserId { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// "I'll do it": the manager has seen the move and says they will act on it. An
    /// acknowledgement only, as in the MVP: it confirms nothing, frees no slot and changes no
    /// reading. It is kept so every manager of the team sees that the move was taken up.
    /// One per rung of a cycle; the first manager to acknowledge is the one recorded.
    /// </summary>
    public class EngineAcknowledgement
    {
        public long Id { get; init; }

        [ForeignKey(nameof(BusinessUnit))]
        public uint BusinessUnitId { get; set; }
        public virtual BusinessUnit? BusinessUnit { get; set; }

        [MaxLength(20)]
        public required string CycleKey { get; set; }

        public int Rung { get; set; }

        public int? AcknowledgedByUserId { get; set; }

        public DateTime AcknowledgedAt { get; set; }
    }
}
