using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Replays one team through the engine from day one and stores what came out.
    ///
    /// Safe to run again for the same day: the facts do not change and every result is
    /// upserted, so a retry, a redeploy or a manual re-run all land in the same rows.
    /// </summary>
    public interface IEngineReplayRunner
    {
        Task<EngineRunResultDto> RunAsync(uint businessUnitId, DateOnly through, CancellationToken ct = default);
    }
}
