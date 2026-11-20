using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models.DeterministicLayer
{
    /// <summary>
    /// The team size the manager set (BusinessUnits.TeamSize), dated: one row per day the
    /// size changed, in force from <see cref="EffectiveFrom"/> until the next row.
    ///
    /// <b>Why dated.</b> The team size is the headcount: the response rate's "possible" and
    /// the engine's floor (max(2, 33% of headcount)) are both counted against it, because
    /// app enrolments count devices, not people (a reinstall enrols the same person again).
    /// The engine is replayed from day one every night, so the size it reads for a past day
    /// must be the size that was in force THAT day. A plain column would let one edit today
    /// re-decide last month's actions.
    ///
    /// <b>Written by ApplicationDbContext.SaveChanges</b> whenever BusinessUnits.TeamSize is
    /// created or changed, so every path that edits a team records it without being touched.
    /// Days before a team's first row, and rows with a null size, fall back to members
    /// enrolled (the rule before team size was used).
    /// </summary>
    public class TeamSizeHistory
    {
        public long Id { get; init; }

        [ForeignKey(nameof(BusinessUnit))]
        public uint BusinessUnitId { get; set; }
        public virtual BusinessUnit? BusinessUnit { get; set; }

        /// <summary>The size in force from <see cref="EffectiveFrom"/>. Null: no size set, so enrolled members count.</summary>
        public int? TeamSize { get; set; }

        /// <summary>UTC date the size took effect. One row per team per date; a second change that day updates it.</summary>
        public DateOnly EffectiveFrom { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
