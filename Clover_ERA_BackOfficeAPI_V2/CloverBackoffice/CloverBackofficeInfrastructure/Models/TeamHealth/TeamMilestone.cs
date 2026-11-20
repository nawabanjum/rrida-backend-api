using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models.TeamHealth
{
    /// <summary>
    /// Something that happened to a team — a new leader, a leaver, a new system, a
    /// restructure — pinned to the Team Health timeline so a change in the readings
    /// can be read against what caused it.
    ///
    /// A milestone is annotation only. Nothing reads it when readings are calculated,
    /// so adding one (even back-dated) never recalculates or alters a reading.
    ///
    /// Ownership follows TeamSetup / CustomQuestion: ProfileId plus UnitId.
    /// </summary>
    [Table("TeamMilestones", Schema = "public")]
    public class TeamMilestone
    {
        public const int TitleMaxLength = 40;
        public const int DescriptionMaxLength = 200;

        public uint Id { get; init; }

        /// <summary>The company the team belongs to.</summary>
        [ForeignKey(nameof(Profile))]
        public required uint ProfileId { get; set; }

        public virtual Profile? Profile { get; set; }

        /// <summary>The team (business unit) the milestone belongs to.</summary>
        [ForeignKey(nameof(BusinessUnit))]
        public required uint UnitId { get; set; }

        public virtual BusinessUnit? BusinessUnit { get; set; }

        [MaxLength(TitleMaxLength)]
        public required string Title { get; set; }

        [MaxLength(DescriptionMaxLength)]
        public required string Description { get; set; }

        /// <summary>The day it happened. May be in the past; never in the future.</summary>
        public DateOnly MilestoneDate { get; set; }

        // ── House audit / soft-delete convention ──
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsDel { get; set; }
    }
}
