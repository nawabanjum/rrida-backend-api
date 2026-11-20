namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Turns the engine's feed payloads into the prose a manager reads, and fills the team
    /// note. Eran phrases and never selects: everything it is given has already been decided.
    /// </summary>
    public interface IEranNarratorService
    {
        /// <summary>
        /// Phrases up to <paramref name="take"/> unphrased feed items for one team, newest
        /// first, as the MVP does. Returns how many were written. An item that fails keeps no prose, so the
        /// next run tries it again; after the MVP's three attempts it gets the fixed fallback card.
        /// Every reply is checked by Eran's guard first (one retry, then the card).
        /// </summary>
        Task<int> NarrateAsync(uint businessUnitId, int take, CancellationToken ct = default, int? throughDay = null);

        /// <summary>
        /// The nightly second chance: each message that went out as the fallback card is written
        /// once more. One that passes replaces the card; one that does not keeps it for good.
        /// </summary>
        Task<int> RetryFallbacksAsync(uint businessUnitId, CancellationToken ct = default) => Task.FromResult(0);
    }
}
