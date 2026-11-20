using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Seeds a team with a scripted demo scenario and runs the engine over it.
    ///
    /// Test data only. It writes facts — members, answers, confirmations — and lets the
    /// ordinary replay derive everything else, so what it proves is the real path.
    /// </summary>
    public interface IEngineScenarioSeeder
    {
        Task<List<EngineScenarioDto>> ListAsync(CancellationToken ct = default);

        Task<EngineSeedResultDto> SeedAsync(EngineSeedRequestDto dto, int actorUserId, CancellationToken ct = default);

        /// <summary>
        /// The same seed, from a scenario already in hand (the shipped scripts, or one read
        /// from an uploaded workbook). Day 1 of the team's clock is <paramref name="startDate" />.
        /// </summary>
        Task<EngineSeedResultDto> SeedScenarioAsync(
            uint businessUnitId,
            Implementation.EngineScenarioFile scenario,
            DateOnly startDate,
            bool autoConfirm,
            bool reset,
            int actorUserId,
            CancellationToken ct = default);
    }
}
