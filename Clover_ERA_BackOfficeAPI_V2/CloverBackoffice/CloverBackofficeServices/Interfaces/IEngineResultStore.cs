using CloverBackoffice.Services.Implementation;
using Rrida.Engine;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Writes what a run produced. Every write is an upsert on a natural key —
    /// (unit, day) for state, (unit, cycle key) for a cycle, (cycle, rung) for a delivery —
    /// so replaying a day updates its row instead of duplicating history.
    /// </summary>
    public interface IEngineResultStore
    {
        /// <summary>
        /// Stores one team's run in a single transaction: the daily state records the sink
        /// collected, the cycles with a row per rung, and the feed. Returns the number of
        /// state records written. <paramref name="throughDay"/> is the last day the run
        /// rebuilt: a stored feed item up to that day which the run did not produce is removed.
        /// </summary>
        Task<int> SaveRunAsync(
            uint businessUnitId,
            EngineContext ctx,
            Team team,
            BufferingStateSink sink,
            int throughDay,
            CancellationToken ct = default);
    }
}
