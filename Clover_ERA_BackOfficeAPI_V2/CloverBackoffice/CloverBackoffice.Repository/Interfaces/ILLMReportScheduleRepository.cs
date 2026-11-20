using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface ILLMReportScheduleRepository
    {
        Task<bool> CompanyExistsAsync(uint profileId);

        // Returns the list of (non-deleted) units under the company. Used by the
        // GET endpoint to enumerate units and merge with their schedule row.
        Task<List<BusinessUnit>> GetUnitsByCompanyAsync(uint profileId);

        // Returns the BusinessUnit if it exists under the supplied company (and is not soft-deleted).
        Task<BusinessUnit?> GetUnitAsync(uint profileId, uint unitId);

        // Returns all schedule rows for the company (used to merge with units in GET).
        Task<List<LLMReportSchedule>> GetByCompanyAsync(uint profileId);

        // Tracked: callers may mutate the returned row and pass it to UpdateAsync.
        Task<LLMReportSchedule?> GetByUnitAsync(uint profileId, uint unitId);

        // No-tracking lookup by UnitId only — used by the scheduler which doesn't carry ProfileId.
        Task<LLMReportSchedule?> GetByUnitIdAsync(uint unitId);

        // Returns only the non-deleted, schedule-enabled rows. Drives the scheduler:
        // only these units participate in report generation.
        Task<List<LLMReportSchedule>> GetEnabledSchedulesAsync();

        // Units the scheduler would otherwise never visit: live units with no
        // schedule row, or whose row is deleted/disabled. Drives the two-week
        // fallback in GuardShortageJob. Clovera Personal App units are excluded —
        // they have their own snapshot pipeline and never take an LLM report.
        Task<List<(uint ProfileId, uint UnitId)>> GetUnitsWithoutEnabledScheduleAsync();

        Task<LLMReportSchedule> AddAsync(LLMReportSchedule entity, int? actorUserId);
        Task<LLMReportSchedule> UpdateAsync(LLMReportSchedule entity, int? actorUserId);
    }
}
