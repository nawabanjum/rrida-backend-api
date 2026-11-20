using System.Globalization;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // Milestones on the Team Health timeline ("+ Add milestone").
    //
    // Writes to TeamMilestones and nothing else. Readings are never recalculated:
    // a milestone only changes what the timeline shows alongside them, so a
    // back-dated one is safe.
    //
    // Who may add or read them for a team: see TeamHealthAccessService. The global
    // tenant guard has already refused another company's unitId by the time this
    // runs; the profile checks there are the second line.
    public class TeamMilestoneService : ITeamMilestoneService
    {
        private readonly ApplicationDbContext _context;
        private readonly ITeamHealthAccessService _access;
        private readonly TimeProvider _clock;

        public TeamMilestoneService(ApplicationDbContext context, ITeamHealthAccessService access, TimeProvider? clock = null)
        {
            _context = context;
            _access = access;
            _clock = clock ?? TimeProvider.System;
        }

        public async Task<TeamMilestoneResult<TeamMilestoneDto>> UpsertAsync(UpsertTeamMilestoneDto? dto, int actorUserId, CancellationToken ct = default)
        {
            var unitId = dto?.UnitId ?? 0;
            var access = await CheckAccessAsync<TeamMilestoneDto>(unitId, actorUserId, ct);
            if (access.Denied != null)
                return access.Denied;

            var title = dto?.Title?.Trim();
            var description = dto?.Description?.Trim();

            if (string.IsNullOrEmpty(title))
                return BadRequest<TeamMilestoneDto>(ResponseMessages.Errors.MilestoneTitleRequired);
            if (title.Length > TeamMilestone.TitleMaxLength)
                return BadRequest<TeamMilestoneDto>(ResponseMessages.Errors.MilestoneTitleTooLong);

            if (string.IsNullOrEmpty(description))
                return BadRequest<TeamMilestoneDto>(ResponseMessages.Errors.MilestoneDescriptionRequired);
            if (description.Length > TeamMilestone.DescriptionMaxLength)
                return BadRequest<TeamMilestoneDto>(ResponseMessages.Errors.MilestoneDescriptionTooLong);

            if (string.IsNullOrWhiteSpace(dto?.Date))
                return BadRequest<TeamMilestoneDto>(ResponseMessages.Errors.MilestoneDateRequired);

            var date = ParseDate(dto.Date);
            if (date == null)
                return BadRequest<TeamMilestoneDto>(ResponseMessages.Errors.MilestoneDateInvalid);

            // A milestone is something that has already happened. One day of slack
            // so a manager ahead of UTC can still log "today" after their midnight.
            var latestAllowed = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime).AddDays(1);
            if (date.Value > latestAllowed)
                return BadRequest<TeamMilestoneDto>(ResponseMessages.Errors.MilestoneDateInFuture);

            var now = _clock.GetUtcNow().UtcDateTime;

            if (dto.Id > 0)
            {
                // Edit. The milestone must be a live one of this same team, so a caller
                // allowed on one team cannot reach another team's milestone by its id.
                var existing = await _context.TeamMilestones
                    .FirstOrDefaultAsync(m => m.Id == dto.Id && m.UnitId == unitId && !m.IsDel, ct);
                if (existing == null)
                    return TeamMilestoneResult<TeamMilestoneDto>.Fail(TeamMilestoneError.NotFound, ResponseMessages.Errors.MilestoneNotFound);

                existing.Title = title;
                existing.Description = description;
                existing.MilestoneDate = date.Value;
                existing.UpdatedBy = actorUserId;
                existing.UpdatedAt = now;

                await _context.SaveChangesAsync(ct);
                return TeamMilestoneResult<TeamMilestoneDto>.Ok(ToDto(existing));
            }

            var milestone = new TeamMilestone
            {
                ProfileId = access.ProfileId,
                UnitId = unitId,
                Title = title,
                Description = description,
                MilestoneDate = date.Value,
                CreatedBy = actorUserId,
                UpdatedBy = actorUserId,
                CreatedAt = now,
                UpdatedAt = now,
                IsDel = false
            };

            _context.TeamMilestones.Add(milestone);
            await _context.SaveChangesAsync(ct);

            return TeamMilestoneResult<TeamMilestoneDto>.Ok(ToDto(milestone));
        }

        public async Task<TeamMilestoneResult<List<TeamMilestoneDto>>> GetForTeamAsync(uint unitId, string? from, string? to, int actorUserId, CancellationToken ct = default)
        {
            var access = await CheckAccessAsync<List<TeamMilestoneDto>>(unitId, actorUserId, ct);
            if (access.Denied != null)
                return access.Denied;

            DateOnly? fromDate = null, toDate = null;
            if (!string.IsNullOrWhiteSpace(from))
            {
                fromDate = ParseDate(from);
                if (fromDate == null)
                    return BadRequest<List<TeamMilestoneDto>>(ResponseMessages.Errors.MilestoneDateInvalid);
            }
            if (!string.IsNullOrWhiteSpace(to))
            {
                toDate = ParseDate(to);
                if (toDate == null)
                    return BadRequest<List<TeamMilestoneDto>>(ResponseMessages.Errors.MilestoneDateInvalid);
            }
            if (fromDate > toDate)
                return BadRequest<List<TeamMilestoneDto>>(ResponseMessages.Errors.MilestoneDateRangeInvalid);

            var query = _context.TeamMilestones
                .AsNoTracking()
                .Where(m => m.UnitId == unitId && !m.IsDel);

            if (fromDate.HasValue)
                query = query.Where(m => m.MilestoneDate >= fromDate.Value);
            if (toDate.HasValue)
                query = query.Where(m => m.MilestoneDate <= toDate.Value);

            var rows = await query
                .OrderBy(m => m.MilestoneDate)
                .ThenBy(m => m.Id)
                .ToListAsync(ct);

            return TeamMilestoneResult<List<TeamMilestoneDto>>.Ok(rows.Select(ToDto).ToList());
        }

        public DateOnly? ParseDate(string? value) => TeamHealthDates.Parse(value);

        internal static TeamMilestoneDto ToDto(TeamMilestone m) => new()
        {
            Id = m.Id,
            UnitId = m.UnitId,
            Title = m.Title,
            Description = m.Description,
            Date = m.MilestoneDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            CreatedBy = m.CreatedBy,
            CreatedAt = m.CreatedAt
        };

        private static TeamMilestoneResult<T> BadRequest<T>(string message) =>
            TeamMilestoneResult<T>.Fail(TeamMilestoneError.BadRequest, message);

        private sealed record AccessCheck<T>(TeamMilestoneResult<T>? Denied, uint ProfileId);

        private async Task<AccessCheck<T>> CheckAccessAsync<T>(uint unitId, int actorUserId, CancellationToken ct)
        {
            if (unitId == 0)
                return new(BadRequest<T>(ResponseMessages.Errors.MilestoneUnitRequired), 0);

            var unit = await _context.BusinessUnits
                .AsNoTracking()
                .Where(u => u.Id == unitId && u.IsDel != true)
                .Select(u => new { u.ProfileId })
                .FirstOrDefaultAsync(ct);
            if (unit == null)
                return new(TeamMilestoneResult<T>.Fail(TeamMilestoneError.NotFound, ResponseMessages.Errors.MilestoneTeamNotFound), 0);

            if (!await _access.CanAccessTeamAsync(unitId, unit.ProfileId, actorUserId, ct))
                return new(TeamMilestoneResult<T>.Fail(TeamMilestoneError.Forbidden, ResponseMessages.Errors.MilestoneAccessDenied), 0);

            return new(null, unit.ProfileId);
        }
    }
}
