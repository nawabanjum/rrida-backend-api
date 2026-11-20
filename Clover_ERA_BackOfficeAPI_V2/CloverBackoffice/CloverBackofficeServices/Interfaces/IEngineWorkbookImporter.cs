using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Imports the "RRIDA Daily Check-in - Test Data" workbook: one sheet per team, one row
    /// per member per asked day. The workbook format is the contract; see
    /// <see cref="Implementation.EngineWorkbookReader" /> for the cells it reads.
    ///
    /// Test data only. Every team is created (or found by name) in one profile, its rows go
    /// through the same seeder the shipped scenarios use, and the engine is replayed over
    /// them. The same check-ins are also written to Team Health's tables so its screens
    /// have something to show.
    /// </summary>
    public interface IEngineWorkbookImporter
    {
        Task<EngineWorkbookImportResultDto> ImportAsync(
            Stream workbook,
            string fileName,
            EngineWorkbookImportOptionsDto options,
            int actorUserId,
            CancellationToken ct = default);
    }
}
