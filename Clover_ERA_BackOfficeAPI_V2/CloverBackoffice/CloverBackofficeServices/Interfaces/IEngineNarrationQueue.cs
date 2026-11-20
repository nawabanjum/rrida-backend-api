namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>Asks for a team's new engine messages to be phrased by Eran, in the background.</summary>
    public interface IEngineNarrationQueue
    {
        /// <param name="throughDay">
        /// Phrase pending messages up to this day, newest first. Null for the newest overall,
        /// which is what a run wants; the timeline passes the day it is showing.
        /// </param>
        void Enqueue(uint businessUnitId, int? throughDay = null);

        /// <summary>
        /// The nightly run's second chance for messages that went out as Eran's fixed fallback
        /// card: each is written once more, and keeps the card if that fails too.
        /// </summary>
        void EnqueueFallbackRetry(uint businessUnitId) { }
    }
}
