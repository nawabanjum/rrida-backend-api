namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Publishes the engine's decisions as Weekly Flags on the Team Health page.
    ///
    /// Only ever writes rows it owns (Source "Engine"). A week already carrying a flag from
    /// anywhere else is left untouched, so seeded and hand-written flags survive.
    /// </summary>
    public interface IEngineWeeklyFlagWriter
    {
        /// <summary>Brings one team's engine flags up to date. Returns how many rows were written.</summary>
        Task<int> WriteAsync(uint businessUnitId, CancellationToken ct = default);
    }
}
