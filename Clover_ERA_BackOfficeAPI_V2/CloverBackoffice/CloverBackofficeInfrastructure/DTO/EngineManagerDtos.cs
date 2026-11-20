using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    // ═══════════════════════════════════════════════════════════════════════════
    // What a manager sees.
    //
    // Proportions, never counts. A manager told that three people said something
    // starts working out which three, and at a floor of two that is close to naming
    // them. Nothing here carries a member, a chip-level count, or free text.
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>The team's reading today.</summary>
    public class EngineTeamStateDto
    {
        public uint BusinessUnitId { get; set; }
        public int Day { get; set; }
        public int AskedDayIndex { get; set; }

        /// <summary>True for the first days, when no state is shown yet.</summary>
        public bool IsCalibrating { get; set; }

        /// <summary>cruise, drift or stall. Null while calibrating.</summary>
        public string? Vertical { get; set; }

        /// <summary>unlocated, internal or external. Null while calibrating.</summary>
        public string? Horizontal { get; set; }

        /// <summary>
        /// Raised whenever the causes point outside the team, whatever the vertical.
        /// Headwinds delivers no action: the manager gets the reading and the evidence,
        /// framed to go upward.
        /// </summary>
        public bool HeadwindsFlag { get; set; }

        /// <summary>Which limb opened the gate: absolute or relative.</summary>
        public string? Limb { get; set; }

        /// <summary>A word, never a member.</summary>
        public string? ConcentrationReading { get; set; }

        /// <summary>Which thresholds produced this reading. They are provisional.</summary>
        public string? ConfigId { get; set; }

        public DateTime? AsAt { get; set; }
    }

    /// <summary>The one action this team is working on, as delivered.</summary>
    public class EngineActiveActionDto
    {
        public uint BusinessUnitId { get; set; }
        public string CycleKey { get; set; } = string.Empty;
        public string IssueFileKey { get; set; } = string.Empty;

        /// <summary>The theme, which is the level a team is ever told about.</summary>
        public string? Issue { get; set; }

        /// <summary>ask, team, content or locus.</summary>
        public string? Route { get; set; }

        /// <summary>Proportions in words: "most of the team", never "5 of 9".</summary>
        public string? ShareText { get; set; }

        public int Rung { get; set; }

        /// <summary>The action as it was delivered, not as the library reads today.</summary>
        public string? ActionText { get; set; }

        /// <summary>The condition the manager confirms against, as delivered.</summary>
        public string? WorkedIf { get; set; }

        public string? WorksheetCode { get; set; }
        public List<string> PhaseRun { get; set; } = new();

        public int DeliveredDay { get; set; }
        public int ConfirmationDueDay { get; set; }
        public int? SignalReadDay { get; set; }

        public bool IsConfirmed { get; set; }
        public int? ConfirmedDay { get; set; }

        /// <summary>True once the confirmation is due and has not been given.</summary>
        public bool ConfirmationDue { get; set; }

        /// <summary>"I'll do it" was given on this rung. An acknowledgement only; it confirms nothing.</summary>
        public bool IsAcknowledged { get; set; }
        public DateTime? AcknowledgedAt { get; set; }

        /// <summary>
        /// The worksheet part of the MVP's card: worksheets, session time, work outside the
        /// session, run kind, "Do not run this if:", and a substitution. Rung one only.
        /// </summary>
        public EngineWorksheetCard? Worksheet { get; set; }

        /// <summary>
        /// The MVP's canDecline: a rung-one content move, not yet confirmed, that states
        /// conditions not to run it under. Shows "One of those applies".
        /// </summary>
        public bool CanDecline { get; set; }

        /// <summary>How the cycle ended: "closed" or "failed". Null while it is still open.</summary>
        public string? Outcome { get; set; }

        /// <summary>The weekly flag's "what came up" sentence for this cycle, word for word.</summary>
        public string? WhatCameUp { get; set; }

        /// <summary>
        /// The weekly flag's "suggested action" sentence for this cycle, word for word: the action,
        /// the exercise, the worked-if condition and, for a failed cycle, the note that it did not move.
        /// </summary>
        public string? SuggestedAction { get; set; }

        /// <summary>
        /// Eran's wording of this move, latched from the first day it was written for this
        /// rung and never re-read (DEVELOPER-GUIDE section 2, rule 3): Eran rephrases the move
        /// slightly every day, and a card that followed would read as the system changing its
        /// mind. All three are null until Eran has written the move — until then the delivered
        /// <see cref="ActionText"/> is the move (MVP renderMove) — and null again once the cycle
        /// has resolved, so a past move's card cannot be read back.
        /// </summary>
        public string? OneThing { get; set; }

        /// <summary>The words the manager can use. Shown folded by default (rule 4).</summary>
        public string? SayThis { get; set; }

        /// <summary>What tells the manager it landed. Shown folded by default (rule 4).</summary>
        public string? WhatTellsYou { get; set; }

        /// <summary>The engine day the wording was latched from; null with it. Not for display: see <see cref="WordedWhen"/>.</summary>
        public int? WordedOnDay { get; set; }

        // MVP 90fe06d: a manager-facing surface says time in the team's own working days, never
        // the run-day index ("day 98"). The day numbers above stay for anything that needs them;
        // these are what a page shows. Phrased by the engine's Team.WorkingDaysBack/Ahead.

        /// <summary>When Eran's wording was given: "today", "1 working day ago", "N working days ago".</summary>
        public string? WordedWhen { get; set; }

        /// <summary>When the manager confirmed it: "today", "N working days ago". Null until confirmed.</summary>
        public string? ConfirmedWhen { get; set; }

        /// <summary>When the confirmation falls due: "today", "in 1 working day", "in N working days".</summary>
        public string? ConfirmationDueIn { get; set; }
    }

    /// <summary>
    /// D31: a condition the team is naming whose cause sits outside the team, with the one move the
    /// manager can make about it and the role to raise it with. Never a cycle: nothing to confirm,
    /// no reading to wait for, and it is not an action (developer-actions-20261017.md item 4).
    /// Read from the team's newest daily message, as the reference puts it on that day's payload.
    /// </summary>
    public class EngineOutwardConditionDto
    {
        public uint BusinessUnitId { get; set; }

        /// <summary>The engine day of the daily message it was read from.</summary>
        public int Day { get; set; }

        /// <summary>outward_condition: the condition, in words.</summary>
        public string Condition { get; set; } = string.Empty;

        /// <summary>outward_move: what the manager can do about it.</summary>
        public string? Move { get; set; }

        /// <summary>outward_goes_to: the role to raise it with.</summary>
        public string? GoesTo { get; set; }

        /// <summary>outward_strength: how strongly it is coming through, in words, never a count.</summary>
        public string? Strength { get; set; }

        /// <summary>outward_days_above: asked days it has stayed above the line.</summary>
        public int? DaysAbove { get; set; }

        /// <summary>outward_how_you_will_know.</summary>
        public string? HowYouWillKnow { get; set; }
    }

    /// <summary>One message in the manager's feed.</summary>
    public class EngineFeedItemDto
    {
        public string FeedKey { get; set; } = string.Empty;
        public int Day { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string? CycleKey { get; set; }
        public int? Rung { get; set; }
        public string? Situation { get; set; }
        public string? NoteType { get; set; }

        /// <summary>
        /// What Eran is handed. Present until module 3.2 turns it into prose, and useful
        /// afterwards for showing why a message says what it says.
        /// </summary>
        public string? Payload { get; set; }

        /// <summary>What Eran made of it. Null until it has been phrased.</summary>
        public string? Prose { get; set; }
    }

    /// <summary>The note the team sees, in the manager's voice.</summary>
    public class EngineTeamNoteDto
    {
        public int Day { get; set; }
        public string? NoteType { get; set; }
        public string? Text { get; set; }
        public DateTime? IssuedAt { get; set; }
    }
}
