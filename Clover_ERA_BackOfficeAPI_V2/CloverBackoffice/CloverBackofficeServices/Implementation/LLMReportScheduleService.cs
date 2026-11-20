using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    public class LLMReportScheduleService : ILLMReportScheduleService
    {
        private readonly ILLMReportScheduleRepository _repo;
        private readonly ApplicationDbContext _db;

        public LLMReportScheduleService(
            ILLMReportScheduleRepository repo,
            ApplicationDbContext db)
        {
            _repo = repo;
            _db = db;
        }

        public async Task<List<LLMReportScheduleDto>> GetSchedulesAsync(uint profileId, uint? unitId, int? actorUserId)
        {
            if (profileId == 0)
                throw new InvalidOperationException("ProfileId is required.");

            if (!await _repo.CompanyExistsAsync(profileId))
                throw new InvalidOperationException("Invalid Company ID");

            var companyName = await _db.Profiles
                .AsNoTracking()
                .Where(c => c.Id == profileId)
                .Select(c => c.ProfileName)
                .FirstOrDefaultAsync()
                ?? string.Empty;

            // Treat unitId=null OR unitId=0 the same — both mean "all units".
            // Only > 0 narrows the result to a single unit.
            bool filterByUnit = unitId.HasValue && unitId.Value > 0;

            List<BusinessUnit> units;
            if (filterByUnit)
            {
                var unit = await _repo.GetUnitAsync(profileId, unitId!.Value);
                if (unit == null)
                    throw new InvalidOperationException("Invalid Unit ID for the supplied Company");
                units = new List<BusinessUnit> { unit };
            }
            else
            {
                units = await _repo.GetUnitsByCompanyAsync(profileId);
                if (units.Count == 0)
                    return new List<LLMReportScheduleDto>();
            }

            // Existing schedule rows for the company (only enabled units have rows
            // now; everything else is computed on the fly as disabled+0).
            var schedules = (await _repo.GetByCompanyAsync(profileId))
                .ToDictionary(s => s.UnitId);

            var result = new List<LLMReportScheduleDto>(units.Count);
            foreach (var unit in units)
            {
                schedules.TryGetValue(unit.Id, out var schedule);

                // Response mapping rule:
                //   IsScheduleEnabled = true  → show stored ReportGenerationDays
                //   IsScheduleEnabled = false → ReportGenerationDays = 0
                //                               (forced regardless of any legacy value
                //                                stored on the row)
                bool isEnabled = schedule?.IsScheduleEnabled == true;
                int days = isEnabled ? schedule!.ReportGenerationDays : 0;
                DateTime createdAt = schedule?.CreatedAt ?? default;

                result.Add(new LLMReportScheduleDto
                {
                    ProfileId = profileId,
                    CompanyName = companyName,
                    UnitId = unit.Id,
                    UnitName = unit.Name,
                    IsScheduleEnabled = isEnabled,
                    ReportGenerationDays = days,
                    CreatedAt = createdAt
                });
            }

            return result;
        }

        public async Task<List<LLMReportScheduleDto>> UpdateSchedulesAsync(List<UpdateLLMReportScheduleDto> dtos, int? actorUserId)
        {
            // Both enable AND disable are persisted now. Enable creates/updates a
            // row with the cadence; disable flips an existing row's
            // IsScheduleEnabled to false. Disable entries with no existing row
            // are no-ops (nothing to turn off).
            if (dtos == null || dtos.Count == 0)
                return new List<LLMReportScheduleDto>();

            // Dedupe by (ProfileId, UnitId) — last write wins if caller sends
            // the same unit twice.
            var distinct = dtos
                .Where(d => d.ProfileId != 0 && d.UnitId != 0)
                .GroupBy(d => (d.ProfileId, d.UnitId))
                .Select(g => g.Last())
                .ToList();

            if (distinct.Count == 0)
                return new List<LLMReportScheduleDto>();

            // Validate every company in the payload (enable + disable both).
            var companyNames = new Dictionary<uint, string>();
            foreach (var grp in distinct.GroupBy(d => d.ProfileId))
            {
                if (!await _repo.CompanyExistsAsync(grp.Key))
                    throw new InvalidOperationException($"Invalid Company ID: {grp.Key}");

                var name = await _db.Profiles
                    .AsNoTracking()
                    .Where(c => c.Id == grp.Key)
                    .Select(c => c.ProfileName)
                    .FirstOrDefaultAsync() ?? string.Empty;
                companyNames[grp.Key] = name;
            }

            var saved = new List<LLMReportScheduleDto>(distinct.Count);

            foreach (var dto in distinct)
            {
                var unit = await _repo.GetUnitAsync(dto.ProfileId, dto.UnitId)
                    ?? throw new InvalidOperationException($"Invalid Unit ID {dto.UnitId} for Company {dto.ProfileId}");

                var row = await _repo.GetByUnitAsync(dto.ProfileId, dto.UnitId);

                if (dto.IsScheduleEnabled)
                {
                    // Enable path — same rules as before.
                    if (dto.ReportGenerationDays < 1)
                        throw new InvalidOperationException(
                            $"ReportGenerationDays must be >= 1 when IsScheduleEnabled is true (CompanyId={dto.ProfileId}, UnitId={dto.UnitId}).");

                    if (row == null)
                    {
                        row = new LLMReportSchedule
                        {
                            ProfileId = dto.ProfileId,
                            UnitId = dto.UnitId,
                            IsScheduleEnabled = true,
                            ReportGenerationDays = dto.ReportGenerationDays,
                            IsDel = false
                        };
                        row = await _repo.AddAsync(row, actorUserId);
                    }
                    else
                    {
                        row.IsScheduleEnabled = true;
                        row.ReportGenerationDays = dto.ReportGenerationDays;
                        row = await _repo.UpdateAsync(row, actorUserId);
                    }
                }
                else
                {
                    // Disable path — no row to flip is a no-op. We do not create
                    // a disabled row from nothing.
                    if (row == null)
                        continue;

                    row.IsScheduleEnabled = false;
                    // Spec: GET maps disabled rows to ReportGenerationDays = 0,
                    // so persist 0 here for consistency with what the UI sees.
                    row.ReportGenerationDays = 0;
                    row = await _repo.UpdateAsync(row, actorUserId);
                }

                saved.Add(new LLMReportScheduleDto
                {
                    ProfileId = row.ProfileId,
                    CompanyName = companyNames.TryGetValue(row.ProfileId, out var cn) ? cn : string.Empty,
                    UnitId = row.UnitId,
                    UnitName = unit.Name,
                    IsScheduleEnabled = row.IsScheduleEnabled,
                    ReportGenerationDays = row.ReportGenerationDays,
                    CreatedAt = row.CreatedAt
                });
            }

            return saved;
        }
    }
}
