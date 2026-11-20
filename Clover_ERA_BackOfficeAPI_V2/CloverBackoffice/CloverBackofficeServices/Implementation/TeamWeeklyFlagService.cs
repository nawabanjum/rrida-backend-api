using System.Globalization;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // Weekly Flag on the Team Health page. Reads TeamWeeklyFlags and never writes:
    // flags are produced elsewhere, and this ticket must not change them.
    //
    // A week is whatever range the flag row says (WeekStartDate..WeekEndDate), so a
    // producer can use Monday or Tuesday weeks without this code caring. Asking for a
    // week by a date matches the flag whose range contains that date, so the
    // dropdown's weekStartDate always finds its flag.
    //
    // Access is ITeamHealthAccessService, as for every other Team Health endpoint.
    public class TeamWeeklyFlagService : ITeamWeeklyFlagService
    {
        private readonly ApplicationDbContext _context;
        private readonly ITeamHealthAccessService _access;

        public TeamWeeklyFlagService(ApplicationDbContext context, ITeamHealthAccessService access)
        {
            _context = context;
            _access = access;
        }

        public async Task<TeamMilestoneResult<TeamWeeklyFlagsDto>> GetAsync(string? teamId, string? weekStartDate, int actorUserId, CancellationToken ct = default)
        {
            if (!uint.TryParse(teamId?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var unitId) || unitId == 0)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.TimelineTeamIdInvalid);

            DateOnly? week = null;
            if (weekStartDate != null)
            {
                week = TeamHealthDates.Parse(weekStartDate);
                if (week == null)
                    return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.WeeklyFlagWeekInvalid);
            }

            var unit = await _context.BusinessUnits
                .AsNoTracking()
                .Where(u => u.Id == unitId && u.IsDel != true)
                .Select(u => new { u.ProfileId })
                .FirstOrDefaultAsync(ct);
            if (unit == null)
                return Fail(TeamMilestoneError.NotFound, ResponseMessages.Errors.MilestoneTeamNotFound);

            if (!await _access.CanAccessTeamAsync(unitId, unit.ProfileId, actorUserId, ct))
                return Fail(TeamMilestoneError.Forbidden, ResponseMessages.Errors.TimelineAccessDenied);

            var query = _context.TeamWeeklyFlags
                .AsNoTracking()
                .Where(f => f.UnitId == unitId && !f.IsDel);

            if (week.HasValue)
                query = query.Where(f => f.WeekStartDate <= week.Value && f.WeekEndDate >= week.Value);

            var flags = await query
                .OrderByDescending(f => f.WeekStartDate)
                .ThenByDescending(f => f.Id)
                .ToListAsync(ct);

            // A date inside two overlapping rows would be a producer bug; answer with
            // the latest one rather than two flags for one week.
            if (week.HasValue && flags.Count > 1)
                flags = flags.Take(1).ToList();

            return TeamMilestoneResult<TeamWeeklyFlagsDto>.Ok(new TeamWeeklyFlagsDto
            {
                TeamId = unitId.ToString(CultureInfo.InvariantCulture),
                WeeklyFlags = flags.Select(ToDto).ToList()
            });
        }

        private static WeeklyFlagDto ToDto(TeamWeeklyFlag f) => new()
        {
            Id = f.Id,
            WeekStartDate = Iso(f.WeekStartDate),
            WeekEndDate = Iso(f.WeekEndDate),
            Label = "Week of " + f.WeekStartDate.ToString("d MMM", CultureInfo.InvariantCulture),
            WhatCameUp = f.WhatCameUp,
            SuggestedAction = f.SuggestedAction,
            CycleId = f.CycleId
        };

        private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static TeamMilestoneResult<TeamWeeklyFlagsDto> Fail(TeamMilestoneError error, string message) =>
            TeamMilestoneResult<TeamWeeklyFlagsDto>.Fail(error, message);
    }
}
