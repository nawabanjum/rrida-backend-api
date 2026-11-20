using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    /// <summary>
    /// The worksheet part of the MVP's action card (src/server.js, the cycle card shown by
    /// public/app.js oneThingBox), as the engine routed it. Only a rung-one content move
    /// hands over a worksheet; on any other move every list here is empty.
    /// </summary>
    public class EngineWorksheetCard
    {
        /// <summary>The route the move took: ask, team, content, build.</summary>
        public string? Route { get; set; }

        /// <summary>The worksheets handed over, code and title, with the former name where it had one.</summary>
        public List<EngineWorksheetRef> Worksheets { get; set; } = new();

        /// <summary>
        /// The MVP's runMinutes.text, e.g. "45 to 60 minutes, plus 1 piece(s) of work outside
        /// the session". The MVP card shows the part before the first comma.
        /// </summary>
        public string? RunMinutesText { get; set; }

        /// <summary>"content", or "locus_discovery", which the MVP card shows as "to find the cause, not to fix it yet".</summary>
        public string? RunKind { get; set; }

        /// <summary>The asynchronous phases, listed apart from the session time.</summary>
        public List<EngineWorksheetAsyncPiece> OutsideTheSession { get; set; } = new();

        /// <summary>"Do not run this if:". Stated to the manager, never evaluated by the layer.</summary>
        public List<string> Contraindications { get; set; } = new();

        /// <summary>Where the first choice assumed something the team has not done, what was delivered instead and why.</summary>
        public EngineWorksheetSubstitution? Substitution { get; set; }
    }

    public class EngineWorksheetRef
    {
        public string Code { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Formerly { get; set; }
    }

    public class EngineWorksheetAsyncPiece
    {
        public string? Title { get; set; }
        public string? When { get; set; }
    }

    public class EngineWorksheetSubstitution
    {
        public string? WantedTitle { get; set; }
        public List<string> Unmet { get; set; } = new();
        public string Reason { get; set; } = string.Empty;
    }
}
