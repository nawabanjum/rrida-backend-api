using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models.DeterministicLayer
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Deterministic layer — reference data (spec 4.7).
    //
    // The seven JSON files of the handover pack, loaded as rows: questions, chips,
    // issue files, actions, worksheets with their phases, and the capability-gap
    // crosswalk. Authored content, not user data.
    //
    // Every row belongs to a RefDataVersion. Reference content is edited between
    // loads — sixteen action texts were rewritten in one week — so a load never
    // updates rows in place: it writes a NEW version and the old one stays exactly
    // as it was. A cycle that delivered an action last month can then still report
    // what it actually delivered.
    //
    // Ids as authored ("MA-C-061", "191-I6", "C1-P4") are kept as text and are the
    // join keys WITHIN a version. The surrogate Id is the database's own.
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>One load of the reference data. Rows are immutable once written.</summary>
    public class RefDataVersion
    {
        public long Id { get; init; }

        /// <summary>meta.json spec_version, e.g. "4.7".</summary>
        [MaxLength(20)]
        public required string SpecVersion { get; set; }

        /// <summary>kit meta.json kitVersion, e.g. "1.0.0".</summary>
        [MaxLength(20)]
        public string? KitVersion { get; set; }

        /// <summary>kit meta.json dataVersion, e.g. "2026-09-21-team-bank".</summary>
        [MaxLength(50)]
        public string? DataVersion { get; set; }

        /// <summary>The upstream tag the kit was generated from, e.g. "baseline-20260921".</summary>
        [MaxLength(50)]
        public string? BaselineTag { get; set; }

        /// <summary>meta.json tally_key. "rung_1_ask" at 4.7 — the route votes accumulate on.</summary>
        [MaxLength(50)]
        public required string TallyKey { get; set; }

        /// <summary>meta.json run_minutes_cap. 60 at 4.7 — the cap on a delivered phase run.</summary>
        public int RunMinutesCap { get; set; }

        // The counts meta.json declares, asserted against what was loaded. Kept so a
        // later load can be compared with this one without re-reading the files.
        public int QuestionCount { get; set; }
        public int ChipCount { get; set; }
        public int ActionCount { get; set; }
        public int IssueFileCount { get; set; }
        public int WorksheetCount { get; set; }
        public int WorksheetPhaseCount { get; set; }
        public int CapabilityFlagCount { get; set; }

        /// <summary>Where the files came from, for the audit trail.</summary>
        [MaxLength(500)]
        public string? Source { get; set; }

        public DateTime LoadedAt { get; set; }
        public int? LoadedBy { get; set; }

        /// <summary>
        /// The version the engine reads. Exactly one is active; a load activates the
        /// new version and deactivates the previous one in the same transaction.
        /// </summary>
        public bool IsActive { get; set; }
    }

    /// <summary>A daily question. Individual track ids are 121-210; the team track takes its own range.</summary>
    public class RefQuestion
    {
        public long Id { get; init; }

        [ForeignKey(nameof(Version))]
        public long VersionId { get; set; }
        public virtual RefDataVersion? Version { get; set; }

        /// <summary>The authored question id, e.g. 191.</summary>
        public int QuestionKey { get; set; }

        [MaxLength(500)]
        public required string Text { get; set; }

        /// <summary>
        /// CLOVER element on the individual track. An open value, not an enumeration:
        /// the team bank uses its own elements and the engine must not care.
        /// </summary>
        [MaxLength(100)]
        public required string Element { get; set; }

        /// <summary>"Individual" or the team track's name.</summary>
        [MaxLength(50)]
        public required string Track { get; set; }
    }

    /// <summary>
    /// One chip: a reason a member can pick. Carries the locus tag the state's
    /// horizontal axis is built from, and the four actions it routes to.
    /// </summary>
    public class RefChip
    {
        public long Id { get; init; }

        [ForeignKey(nameof(Version))]
        public long VersionId { get; set; }
        public virtual RefDataVersion? Version { get; set; }

        /// <summary>Authored chip id, e.g. "191-I6" (question, polarity letter, ordinal).</summary>
        [MaxLength(50)]
        public required string ChipKey { get; set; }

        public int QuestionKey { get; set; }

        /// <summary>
        /// Position within its polarity set, 1-6, taken from the chip id's own index.
        /// Authored order is load-bearing: a member sees N1 to N6 as written.
        /// </summary>
        public int Ordinal { get; set; }

        [MaxLength(50)]
        public required string Track { get; set; }

        [MaxLength(100)]
        public required string Element { get; set; }

        /// <summary>"Negative", "Indifferent" or "Positive".</summary>
        [MaxLength(20)]
        public required string Polarity { get; set; }

        [MaxLength(300)]
        public required string Text { get; set; }

        /// <summary>"Internal", "External" or "Neutral". Authored, never inferred at runtime.</summary>
        [MaxLength(20)]
        public required string Locus { get; set; }

        [MaxLength(30)]
        public required string IssueFileKey { get; set; }

        public bool CapabilityGap { get; set; }

        // The three rung one routes and the rung two mechanism, as action keys.
        // Votes accumulate on Rung1AskKey (the tally key); the route swap happens
        // once, at selection. A positive chip carries a single action, so only one
        // of the rung one columns is filled and the content route is always null.
        [MaxLength(30)] public string? Rung1AskKey { get; set; }
        [MaxLength(30)] public string? Rung1TeamKey { get; set; }
        [MaxLength(30)] public string? Rung1ContentKey { get; set; }
        [MaxLength(30)] public string? Rung2Key { get; set; }
    }

    /// <summary>A theme: the grouping of actions, and the unit succession holds.</summary>
    public class RefIssueFile
    {
        public long Id { get; init; }

        [ForeignKey(nameof(Version))]
        public long VersionId { get; set; }
        public virtual RefDataVersion? Version { get; set; }

        /// <summary>Authored issue file id, e.g. "MA-C-061".</summary>
        [MaxLength(30)]
        public required string IssueFileKey { get; set; }

        [MaxLength(300)]
        public required string Issue { get; set; }

        [MaxLength(100)]
        public required string Pillar { get; set; }

        // Action counts by kind, as exported.
        public int ConversationalCount { get; set; }
        public int StructuralCount { get; set; }
        public int GestureCount { get; set; }
    }

    /// <summary>A micro-action. Delivered text and condition are snapshotted at delivery, never read back from here.</summary>
    public class RefAction
    {
        public long Id { get; init; }

        [ForeignKey(nameof(Version))]
        public long VersionId { get; set; }
        public virtual RefDataVersion? Version { get; set; }

        /// <summary>Authored action id, e.g. "MA-E-002-22". Positional and frozen; never re-derived.</summary>
        [MaxLength(30)]
        public required string ActionKey { get; set; }

        [MaxLength(1000)]
        public required string Text { get; set; }

        [MaxLength(30)]
        public required string IssueFileKey { get; set; }

        [MaxLength(300)]
        public string? Issue { get; set; }

        [MaxLength(100)]
        public string? Pillar { get; set; }

        /// <summary>"Conversational", "Structural" or "Gesture". A gesture is eligible for neither rung.</summary>
        [MaxLength(30)]
        public required string Kind { get; set; }

        [MaxLength(30)]
        public string? Mode { get; set; }

        /// <summary>"Change", "Learned" or "Null" — what the team note may truthfully report.</summary>
        [MaxLength(20)]
        public string? NoteType { get; set; }

        /// <summary>The observable condition the manager confirms. Null on an action no chip routes to.</summary>
        [MaxLength(1000)]
        public string? WorkedIf { get; set; }

        /// <summary>"Ask", "Team", "Content" or "Locus" where the action is routed; null otherwise.</summary>
        [MaxLength(20)]
        public string? Route { get; set; }
    }

    /// <summary>A worksheet: the container and the citation. The phase is what gets delivered.</summary>
    public class RefWorksheet
    {
        public long Id { get; init; }

        [ForeignKey(nameof(Version))]
        public long VersionId { get; set; }
        public virtual RefDataVersion? Version { get; set; }

        /// <summary>Worksheet code, e.g. "C1".</summary>
        [MaxLength(20)]
        public required string Code { get; set; }

        [MaxLength(300)]
        public required string Title { get; set; }

        /// <summary>
        /// A retired design this worksheet replaced. NOT an alias: naming it in an
        /// action points a manager at the thing the worksheet exists to replace.
        /// </summary>
        [MaxLength(300)]
        public string? Formerly { get; set; }

        [MaxLength(100)]
        public string? Pillar { get; set; }

        /// <summary>1-3. Sequences rather than gates: a team with no history gets the lowest available.</summary>
        public int? Difficulty { get; set; }

        [MaxLength(50)]
        public string? TimeMinutes { get; set; }

        [MaxLength(50)]
        public string? GroupSize { get; set; }

        /// <summary>Worksheet codes that must have run first. Resolved as a chain, not one step.</summary>
        public List<string> Prerequisites { get; set; } = new();

        /// <summary>Conditions under which the worksheet must not be run. Stated to the manager, never evaluated.</summary>
        public List<string> Contraindications { get; set; } = new();

        public virtual ICollection<RefWorksheetPhase> Phases { get; set; } = new List<RefWorksheetPhase>();
    }

    /// <summary>
    /// One phase of a worksheet, and the unit of content delivery. A phase that
    /// surfaces something is never delivered without the phase that responds to it,
    /// which is what RequiresPhases records.
    /// </summary>
    public class RefWorksheetPhase
    {
        public long Id { get; init; }

        [ForeignKey(nameof(Version))]
        public long VersionId { get; set; }
        public virtual RefDataVersion? Version { get; set; }

        [ForeignKey(nameof(Worksheet))]
        public long WorksheetId { get; set; }
        public virtual RefWorksheet? Worksheet { get; set; }

        /// <summary>Authored phase id, e.g. "C1-P4".</summary>
        [MaxLength(30)]
        public required string PhaseKey { get; set; }

        [MaxLength(20)]
        public required string WorksheetCode { get; set; }

        public int Number { get; set; }

        [MaxLength(300)]
        public required string Title { get; set; }

        // The authored duration is a range. A run's length is the sum of its session
        // phases only; an async phase has no duration and must not be read as zero.
        public int? MinutesMin { get; set; }
        public int? MinutesMax { get; set; }

        /// <summary>What the phase does, e.g. "surface" or "commit".</summary>
        [MaxLength(50)]
        public string? Role { get; set; }

        [MaxLength(1000)]
        public string? Produces { get; set; }

        /// <summary>Phases that must run alongside this one, in the same session.</summary>
        public List<string> RequiresPhases { get; set; } = new();

        public bool Excerptable { get; set; }

        /// <summary>"session" or "async". Async work sits outside the session and is surfaced separately.</summary>
        [MaxLength(20)]
        public string? Timing { get; set; }

        [MaxLength(500)]
        public string? TimingNote { get; set; }
    }

    /// <summary>
    /// Per issue file: whether it is a capability gap, the phase run the content
    /// route delivers, and the locus-discovery run and action every file carries.
    /// </summary>
    public class RefCapabilityFlag
    {
        public long Id { get; init; }

        [ForeignKey(nameof(Version))]
        public long VersionId { get; set; }
        public virtual RefDataVersion? Version { get; set; }

        [MaxLength(30)]
        public required string IssueFileKey { get; set; }

        public bool CapabilityGap { get; set; }

        /// <summary>Worksheet codes crosswalked to this file.</summary>
        public List<string> Worksheets { get; set; } = new();

        [MaxLength(1000)]
        public string? Rationale { get; set; }

        /// <summary>The ordered phase run the content route delivers. Empty on a file that is not a gap.</summary>
        public List<string> ContentPhases { get; set; } = new();

        /// <summary>The ordered run delivered when the winning chips are majority neutral. Every file has one.</summary>
        public List<string> LocusDiscoveryPhases { get; set; } = new();

        /// <summary>The action that run is delivered under. Route "Locus".</summary>
        [MaxLength(30)]
        public string? LocusDiscoveryActionKey { get; set; }
    }
}
