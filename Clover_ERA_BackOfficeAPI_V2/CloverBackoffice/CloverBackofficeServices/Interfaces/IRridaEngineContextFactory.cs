using Rrida.Engine;
using Rrida.State;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// The deterministic layer's injected roots, loaded once from the vendored kit.
    /// Singleton: both are immutable. The resolver is built per run instead, because it
    /// carries per-team running state.
    /// </summary>
    public interface IRridaEngineContextFactory
    {
        /// <summary>Where the kit was loaded from, for diagnostics.</summary>
        string KitDataPath { get; }

        /// <summary>
        /// The kit's data bank and engine config on the production rotation (D34): all ninety
        /// individual questions and all ninety team questions, one of each per asked day, looping.
        /// Every team is asked on it.
        /// </summary>
        EngineContext Context { get; }

        /// <summary>The resolver's thresholds from kit/data/thresholds.json. Provisional, pending ratification.</summary>
        StateConfig StateConfig { get; }

        /// <summary>The kit's named working weeks, from kit/data/calendar.json. Sunday is 0.</summary>
        CalendarConfig Calendar { get; }
    }
}
