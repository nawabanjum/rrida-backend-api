using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Interfaces
{
    public interface ILLMReportScheduleService
    {
        // GET: profileId required, unitId optional.
        //   unitId == null  → return one row per unit under the company.
        //   unitId == 0     → same as null (treated as "no filter").
        //   unitId > 0      → return the single matching row.
        Task<List<LLMReportScheduleDto>> GetSchedulesAsync(uint profileId, uint? unitId, int? actorUserId);

        // UPDATE (bulk): accepts a list of (ProfileId, UnitId, IsScheduleEnabled,
        // ReportGenerationDays) rows. Only entries with IsScheduleEnabled=true are
        // persisted; entries with IsScheduleEnabled=false are skipped per spec
        // ("only enabled units should be saved").
        // Returns the list of rows that were actually saved.
        Task<List<LLMReportScheduleDto>> UpdateSchedulesAsync(List<UpdateLLMReportScheduleDto> dtos, int? actorUserId);
    }
}
