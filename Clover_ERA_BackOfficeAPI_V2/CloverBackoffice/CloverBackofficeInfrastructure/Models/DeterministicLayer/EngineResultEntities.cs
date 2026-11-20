using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models.DeterministicLayer
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Deterministic layer — the results.
    //
    // Everything here is DERIVED: the replay rebuilds it from the facts, so every
    // write is an upsert on a natural key and re-running a day changes nothing.
    // Nothing downstream may treat these as the record of what happened; the facts
    // are that. These exist so a surface can read yesterday's answer without a
    // replay, and so "why did this fire on the 14th" is answerable months later.
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// One team's resolved state for one day: the canonical log record the resolver
    /// produces, as flat columns for querying plus the whole record as JSON.
    ///
    /// The JSON is kept because the flat columns are a reading convenience and the record
    /// is the evidence. A state figure without its thresholds is not interpretable, so
    /// every row carries the schema and the config id it was produced under.
    /// </summary>
    public class EngineStateDaily
    {
        public long Id { get; init; }

        [ForeignKey(nameof(BusinessUnit))]
        public uint BusinessUnitId { get; set; }
        public virtual BusinessUnit? BusinessUnit { get; set; }

        /// <summary>The team's own day index. With the unit id this is the natural key.</summary>
        public int Day { get; set; }

        [MaxLength(50)]
        public required string Schema { get; set; }

        /// <summary>Which thresholds produced this. They are provisional, so this is not optional.</summary>
        [MaxLength(50)]
        public required string ConfigId { get; set; }

        /// <summary>cruise, drift or stall.</summary>
        [MaxLength(20)]
        public required string Vertical { get; set; }

        /// <summary>unlocated, internal or external.</summary>
        [MaxLength(20)]
        public required string Horizontal { get; set; }

        /// <summary>
        /// Raised whenever the horizontal is external, whatever the vertical. A team can be
        /// in Stall and flagged at once.
        /// </summary>
        public bool HeadwindsFlag { get; set; }

        /// <summary>Which limb opened the gate: absolute or relative. Null while it is shut.</summary>
        [MaxLength(20)]
        public string? Limb { get; set; }

        public int? OpenedDay { get; set; }

        /// <summary>A word. Never a member — naming who carried the evidence names the person.</summary>
        [MaxLength(30)]
        public string? ConcentrationReading { get; set; }

        public double? ConcentrationTopShare { get; set; }

        public double? ClassifierZ { get; set; }

        [MaxLength(20)]
        public string? ClassifierHorizontal { get; set; }

        public bool WatchExposed { get; set; }

        /// <summary>The full log record as the resolver produced it, rounded as it rounds.</summary>
        public required string RecordJson { get; set; }

        public DateTime WrittenAt { get; set; }
    }

    /// <summary>
    /// One active action, from selection to resolution. One per team at a time.
    ///
    /// The engine's cycle mutates in place when it ladders, so what survives per rung is in
    /// <see cref="EngineCycleDelivery"/> and this row carries only what belongs to the cycle.
    /// </summary>
    public class EngineCycle
    {
        public long Id { get; init; }

        [ForeignKey(nameof(BusinessUnit))]
        public uint BusinessUnitId { get; set; }
        public virtual BusinessUnit? BusinessUnit { get; set; }

        /// <summary>The engine's own id for the run, "C1", "C2". Natural key with the unit.</summary>
        [MaxLength(20)]
        public required string CycleKey { get; set; }

        public int CycleIndex { get; set; }

        [MaxLength(30)]
        public required string IssueFileKey { get; set; }

        [MaxLength(300)]
        public string? Issue { get; set; }

        /// <summary>The chip that triggered it. Theme level is what a team is ever told.</summary>
        [MaxLength(50)]
        public string? TriggeringChipKey { get; set; }

        public int TriggeringQuestionKey { get; set; }

        /// <summary>
        /// The action the votes were filed under — the ask action on every routed chip. On
        /// the team and content routes this is NOT the action that was delivered.
        /// </summary>
        [MaxLength(30)]
        public string? TallyActionKey { get; set; }

        [MaxLength(20)]
        public string? Route { get; set; }

        /// <summary>Proportions, never counts: "most of the team", not "5 of 9".</summary>
        [MaxLength(100)]
        public string? ShareText { get; set; }

        public int SelectedDay { get; set; }
        public int DeliveredDay { get; set; }
        public int ConfirmationDueDay { get; set; }
        public int? SignalReadDay { get; set; }
        public int? ResolvedDay { get; set; }

        /// <summary>closed, failed, unattributed or unread. Null while it runs.</summary>
        [MaxLength(30)]
        public string? Outcome { get; set; }

        public bool Rung2Skipped { get; set; }

        [MaxLength(300)]
        public string? Rung2SkipReason { get; set; }

        /// <summary>A contraindication the manager declined. Recorded as a failed cycle with its reason.</summary>
        public int? DeclinedDay { get; set; }

        [MaxLength(500)]
        public string? DeclineReason { get; set; }

        public DateTime WrittenAt { get; set; }

        public virtual ICollection<EngineCycleDelivery> Deliveries { get; set; } = new List<EngineCycleDelivery>();
    }

    /// <summary>
    /// What was actually delivered on one rung, snapshotted.
    ///
    /// Reference content changes while cycles are live — sixteen action texts were rewritten
    /// in one week — so the text, the condition and the phase run are stored as delivered
    /// rather than referenced. A cycle must report what it did, not what the action says
    /// today, and where a substitution fired the delivered worksheet is the one the team
    /// was in the room for.
    /// </summary>
    public class EngineCycleDelivery
    {
        public long Id { get; init; }

        [ForeignKey(nameof(Cycle))]
        public long CycleId { get; set; }
        public virtual EngineCycle? Cycle { get; set; }

        /// <summary>1 or 2. Each rung keeps its own row, so nothing has to remember.</summary>
        public int Rung { get; set; }

        [MaxLength(20)]
        public string? Route { get; set; }

        [MaxLength(30)]
        public required string ActionKey { get; set; }

        [MaxLength(1000)]
        public string? DeliveredText { get; set; }

        /// <summary>The condition the manager confirms against, as delivered.</summary>
        [MaxLength(1000)]
        public string? DeliveredWorkedIf { get; set; }

        [MaxLength(20)]
        public string? DeliveredWorksheetCode { get; set; }

        /// <summary>The ordered phase run, as delivered. A substitution changes what ran.</summary>
        public List<string> DeliveredPhaseRun { get; set; } = new();

        /// <summary>
        /// Rung one only: the worksheet part of the MVP's action card as the engine routed it
        /// (src/server.js cycle card): the worksheets with title and former name, the session
        /// time and the work outside it, the run kind, the conditions not to run it under, and
        /// a prerequisite substitution. JSON of <c>EngineWorksheetCard</c>; null when the move
        /// hands over no worksheet.
        /// </summary>
        public string? DeliveredWorksheetCardJson { get; set; }

        public int DeliveredDay { get; set; }
        public int? ConfirmedDay { get; set; }

        /// <summary>The manager's own observation, separate from the data signal.</summary>
        public bool? WorkedIfMet { get; set; }

        public int? SignalReadDay { get; set; }
        public bool? SignalMoved { get; set; }

        /// <summary>"fewer people selected it" or "polarity improved".</summary>
        [MaxLength(100)]
        public string? HowRead { get; set; }

        [MaxLength(30)]
        public string? Outcome { get; set; }
    }

    /// <summary>
    /// One item the engine put on the team's feed: what Eran is handed. The payload is the
    /// boundary — flat keys, no class instances — so nothing downstream needs the engine to
    /// read it.
    /// </summary>
    public class EngineFeedItem
    {
        public long Id { get; init; }

        [ForeignKey(nameof(BusinessUnit))]
        public uint BusinessUnitId { get; set; }
        public virtual BusinessUnit? BusinessUnit { get; set; }

        /// <summary>The engine's own feed id, "F1", "F2". Natural key with the unit.</summary>
        [MaxLength(20)]
        public required string FeedKey { get; set; }

        public int Day { get; set; }

        [MaxLength(50)]
        public required string Kind { get; set; }

        [MaxLength(20)]
        public string? CycleKey { get; set; }

        public int? Rung { get; set; }

        [MaxLength(200)]
        public string? Situation { get; set; }

        [MaxLength(20)]
        public string? NoteType { get; set; }

        public required string PayloadJson { get; set; }

        /// <summary>
        /// What Eran made of the payload. Null until module 3.2 phrases it, and phrased
        /// once: re-running a day must not re-bill or change the wording under a manager
        /// who has already read it.
        /// </summary>
        [MaxLength(4000)]
        public string? Prose { get; set; }

        public DateTime? ProseGeneratedAt { get; set; }

        /// <summary>
        /// What Eran's guard made of <see cref="Prose"/>: passed, passed_on_retry, fallback (the
        /// fixed card, tried once more on the next nightly run) or fallback_final. Null for
        /// prose written before the guard; it is checked on its next read.
        /// </summary>
        [MaxLength(30)]
        public string? GuardOutcome { get; set; }

        /// <summary>
        /// Which prompt wrote <see cref="Prose"/>: the first 12 hex of a SHA-256 over Eran's prompt
        /// text (EranNarratorService.PromptFingerprint), as the MVP keys its cache on
        /// PROMPT_FINGERPRINT (0729864, item 5). Null for the fixed card and for anything written
        /// before this column existed; a null is never retired, so stored history is left as it is.
        /// </summary>
        [MaxLength(12)]
        public string? PromptFingerprint { get; set; }

        public DateTime WrittenAt { get; set; }
    }

    /// <summary>
    /// The note the team sees, in the manager's voice. Written after the signal read, never
    /// at confirmation: a note that arrives before the team answers again changes the answer.
    ///
    /// The note reports the CHANGE, never the action. Theme level, never chip level, and no
    /// individual named or implied.
    /// </summary>
    public class EngineTeamNote
    {
        public long Id { get; init; }

        [ForeignKey(nameof(BusinessUnit))]
        public uint BusinessUnitId { get; set; }
        public virtual BusinessUnit? BusinessUnit { get; set; }

        [MaxLength(20)]
        public string? CycleKey { get; set; }

        public int Day { get; set; }

        /// <summary>Change, Learned or Null. A null is an honest outcome, not a failure.</summary>
        [MaxLength(20)]
        public string? NoteType { get; set; }

        /// <summary>Filled by Eran in module 3; the row exists as soon as the note is due.</summary>
        [MaxLength(2000)]
        public string? Text { get; set; }

        public DateTime? IssuedAt { get; set; }
        public DateTime WrittenAt { get; set; }
    }
}
