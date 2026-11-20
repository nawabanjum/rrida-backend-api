using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Implementation
{
    public class LLMReportScheduleRepository : ILLMReportScheduleRepository
    {
        private readonly ApplicationDbContext _context;

        public LLMReportScheduleRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public Task<bool> CompanyExistsAsync(uint profileId)
        {
            return _context.Profiles.AnyAsync(c => c.Id == profileId);
        }

        public Task<List<BusinessUnit>> GetUnitsByCompanyAsync(uint profileId)
        {
            return _context.BusinessUnits
                .AsNoTracking()
                .Where(b => b.ProfileId == profileId
                            && (b.IsDel == false || b.IsDel == null))
                .OrderBy(b => b.Name)
                .ToListAsync();
        }

        public Task<BusinessUnit?> GetUnitAsync(uint profileId, uint unitId)
        {
            return _context.BusinessUnits
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == unitId
                    && b.ProfileId == profileId
                    && (b.IsDel == false || b.IsDel == null));
        }

        public Task<List<LLMReportSchedule>> GetByCompanyAsync(uint profileId)
        {
            return _context.LLMReportSchedules
                .AsNoTracking()
                .Where(s => s.ProfileId == profileId && !s.IsDel)
                .ToListAsync();
        }

        // Tracked: caller may mutate + pass back to UpdateAsync.
        public Task<LLMReportSchedule?> GetByUnitAsync(uint profileId, uint unitId)
        {
            return _context.LLMReportSchedules
                .FirstOrDefaultAsync(s => s.ProfileId == profileId && s.UnitId == unitId && !s.IsDel);
        }

        public Task<LLMReportSchedule?> GetByUnitIdAsync(uint unitId)
        {
            return _context.LLMReportSchedules
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.UnitId == unitId && !s.IsDel);
        }

        public Task<List<LLMReportSchedule>> GetEnabledSchedulesAsync()
        {
            return _context.LLMReportSchedules
                .AsNoTracking()
                .Where(s => !s.IsDel && s.IsScheduleEnabled)
                .ToListAsync();
        }

        public async Task<List<(uint ProfileId, uint UnitId)>> GetUnitsWithoutEnabledScheduleAsync()
        {
            // Anti-join against the enabled schedules. A unit whose row is
            // disabled (or soft-deleted) counts as unscheduled and therefore
            // falls back, same as a unit with no row at all.
            var enabledUnitIds = _context.LLMReportSchedules
                .Where(s => !s.IsDel && s.IsScheduleEnabled)
                .Select(s => s.UnitId);

            var rows = await _context.BusinessUnits
                .AsNoTracking()
                .Where(b => (b.IsDel == false || b.IsDel == null)
                            && !b.Profile.IsDel
                            && !enabledUnitIds.Contains(b.Id))
                .Select(b => new
                {
                    ProfileId = b.ProfileId,
                    UnitId = b.Id
                })
                .ToListAsync();

            return rows.Select(r => (r.ProfileId, r.UnitId)).ToList();
        }

        public async Task<LLMReportSchedule> AddAsync(LLMReportSchedule entity, int? actorUserId)
        {
            entity.CreatedAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.CreatedBy = actorUserId;
            entity.UpdatedBy = actorUserId;

            await _context.LLMReportSchedules.AddAsync(entity);
            await _context.SaveChangesAsync();

            await WriteAuditAsync(entity, "INSERT");
            return entity;
        }

        public async Task<LLMReportSchedule> UpdateAsync(LLMReportSchedule entity, int? actorUserId)
        {
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = actorUserId;

            _context.LLMReportSchedules.Update(entity);
            await _context.SaveChangesAsync();

            await WriteAuditAsync(entity, "UPDATE");
            return entity;
        }

        private async Task WriteAuditAsync(LLMReportSchedule entity, string operationType)
        {
            var lastVersion = await _context.LLMReportScheduleAudits
                .Where(a => a.ScheduleId == entity.Id)
                .OrderByDescending(a => a.VersionNumber)
                .Select(a => (int?)a.VersionNumber)
                .FirstOrDefaultAsync();

            var audit = new LLMReportScheduleAudit
            {
                ScheduleId = entity.Id,
                ProfileId = entity.ProfileId,
                UnitId = entity.UnitId,
                IsScheduleEnabled = entity.IsScheduleEnabled,
                ReportGenerationDays = entity.ReportGenerationDays,
                IsDel = entity.IsDel,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
                CreatedBy = entity.CreatedBy,
                UpdatedBy = entity.UpdatedBy,
                LoggedAt = DateTime.UtcNow,
                VersionNumber = (lastVersion ?? 0) + 1,
                OperationType = operationType
            };

            await _context.LLMReportScheduleAudits.AddAsync(audit);
            await _context.SaveChangesAsync();
        }
    }
}
