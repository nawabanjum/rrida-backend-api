using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    // ═══════════════════════════════════════════════════════════════════════════
    // The manager-of-managers view.
    //
    // Three rules from build spec §7, and all three are here rather than in a UI:
    //   * the roll-up NEVER produces an action for a child team. Handing a senior
    //     manager someone else's action undercuts the manager who owns it and turns
    //     the product into surveillance;
    //   * a child team below the visibility floor shows only as calibrating,
    //     whatever its real state. On a team of three, naming the state is close to
    //     naming who is unhappy;
    //   * the senior manager's own signal is cross-team concentration, and nothing
    //     else. Its thresholds are guessed rather than derived, so they say so.
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>One child team as the senior manager sees it.</summary>
    public class EngineRollUpRowDto
    {
        public uint BusinessUnitId { get; set; }
        public string TeamName { get; set; } = string.Empty;

        /// <summary>Null when the team is calibrating or below the visibility floor.</summary>
        public string? Vertical { get; set; }

        public string? Horizontal { get; set; }

        /// <summary>Surfaced first: those are the ones only this reader can fix.</summary>
        public bool HeadwindsFlag { get; set; }

        /// <summary>none, active, closing or failed.</summary>
        public string CycleStatus { get; set; } = "none";

        public int CyclesClosed { get; set; }
        public int CyclesFailed { get; set; }

        /// <summary>
        /// True when the team is below the visibility floor, so its state is withheld. Said
        /// out loud rather than shown as an empty reading.
        /// </summary>
        public bool BelowVisibilityFloor { get; set; }

        public bool IsCalibrating { get; set; }
    }

    /// <summary>The senior manager's roll-up.</summary>
    public class EngineRollUpDto
    {
        public int ChildTeams { get; set; }
        public int VisibilityFloor { get; set; }
        public List<EngineRollUpRowDto> Teams { get; set; } = new();

        /// <summary>Cross-team patterns, if any have fired.</summary>
        public List<EngineCrossTeamConditionDto> CrossTeam { get; set; } = new();
    }

    /// <summary>
    /// A pattern no individual manager can see: the same issue file active across several
    /// teams at once, or failing in more than one.
    /// </summary>
    public class EngineCrossTeamConditionDto
    {
        public string IssueFileKey { get; set; } = string.Empty;
        public string? Issue { get; set; }

        /// <summary>concentration, or repeated_failure.</summary>
        public string Trigger { get; set; } = string.Empty;

        public int ActiveTeams { get; set; }
        public int FailingTeams { get; set; }

        /// <summary>The threshold this fired against, so the number is never implicit.</summary>
        public int Threshold { get; set; }

        /// <summary>
        /// Always true today. Three teams concurrent and two failing were guessed rather
        /// than derived, and the UI is expected to say so.
        /// </summary>
        public bool IsProvisional { get; set; } = true;
    }
}
