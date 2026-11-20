using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models.TeamHealth
{
    /// <summary>
    /// The Weekly Flag on the Team Health page: for one team and one week, what came
    /// up in the answers and the action suggested for it.
    ///
    /// One live row per team per week (filtered unique index on UnitId +
    /// WeekStartDate). Written by whatever produces flags (the deterministic engine /
    /// Eran, or a seed); the Team Health API only reads it.
    /// </summary>
    [Table("TeamWeeklyFlags", Schema = "public")]
    public class TeamWeeklyFlag
    {
        public const int TextMaxLength = 2000;

        public uint Id { get; init; }

        [ForeignKey(nameof(Profile))]
        public required uint ProfileId { get; set; }

        public virtual Profile? Profile { get; set; }

        /// <summary>The team (business unit) the flag is for.</summary>
        [ForeignKey(nameof(BusinessUnit))]
        public required uint UnitId { get; set; }

        public virtual BusinessUnit? BusinessUnit { get; set; }

        /// <summary>First day of the week the flag covers.</summary>
        public DateOnly WeekStartDate { get; set; }

        /// <summary>Last day of the week the flag covers, inclusive.</summary>
        public DateOnly WeekEndDate { get; set; }

        /// <summary>"What came up" — the pattern in the week's answers.</summary>
        [MaxLength(TextMaxLength)]
        public required string WhatCameUp { get; set; }

        /// <summary>"Suggested action" — what the manager could do about it.</summary>
        [MaxLength(TextMaxLength)]
        public required string SuggestedAction { get; set; }

        /// <summary>What produced the flag, e.g. "Engine" or "Seed".</summary>
        [MaxLength(30)]
        public string? Source { get; set; }

        /// <summary>
        /// The engine cycle the week's action came from (EngineCycles.Id). Null for a week
        /// with no delivered action (Headwinds) and for flags the engine did not write.
        /// </summary>
        public long? CycleId { get; set; }

        // ── House audit / soft-delete convention ──
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsDel { get; set; }
    }
}
