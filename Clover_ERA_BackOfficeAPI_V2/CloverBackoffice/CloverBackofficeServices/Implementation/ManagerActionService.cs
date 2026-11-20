using System.Globalization;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // Manager-created team actions (api/actions). See ManagerAction.cs.
    //
    // One list for both kinds of action: the manager's own (ManagerActions, source MANUAL,
    // id "12") and the engine's recommendations a manager took up with "I'll do it" (source
    // ENGINE, id "engine-57"). Engine actions are never copied: they are read from, and
    // changed through, the Team Health action service (ManagerActionService.Engine.cs).
    //
    // Actions are team-based only. The creator is always the logged-in user.
    //
    // Access: the Team Health rule (TeamHealthAccessService), the same as milestones and
    // engine actions: the team's Team Manager or a manager of a team above it, their
    // Portfolio Manager, the Profile Admin, a RRIDA Admin. It applies to create, read and update.
    public partial class ManagerActionService : IManagerActionService
    {
        public const int DefaultTake = 100;
        public const int MaxTake = 500;

        private readonly ApplicationDbContext _context;
        private readonly ITeamHealthAccessService _access;
        private readonly IUserRoleAssignmentService _roles;
        private readonly TimeProvider _clock;
        private readonly ITeamActionService _teamActions;

        public ManagerActionService(ApplicationDbContext context, ITeamHealthAccessService access,
            IUserRoleAssignmentService roles, TimeProvider? clock = null, ITeamActionService? teamActions = null)
        {
            _context = context;
            _access = access;
            _roles = roles;
            _clock = clock ?? TimeProvider.System;
            // Engine actions are read and changed through the existing Team Health action
            // service, so their rules (in flight only after "I'll do it", manager's record
            // beside the engine's rows) stay in one place.
            _teamActions = teamActions ?? new TeamActionService(context, access, clock);
        }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;
        private DateOnly Today => DateOnly.FromDateTime(Now);

        private sealed record Actor(int UserId, uint? ProfileId, bool IsRridaAdmin, bool IsProfileAdmin);

        private async Task<Actor?> LoadActorAsync(int userId, CancellationToken ct)
        {
            var user = await _context.Users.AsNoTracking()
                .Where(u => u.UserId == userId && !u.IsDel)
                .Select(u => new { u.ProfileId })
                .FirstOrDefaultAsync(ct);
            if (user == null) return null;

            var roles = await _roles.GetRoleIdsAsync(userId, ct);
            uint? pid = user.ProfileId is > 0 ? (uint)user.ProfileId.Value : null;
            return new Actor(userId, pid,
                roles.Contains((int)UserGroups.RridaAdmin),
                roles.Contains((int)UserGroups.ProfileAdmin));
        }

        // ── Create ───────────────────────────────────────────────────────────

        public async Task<TeamMilestoneResult<ManagerActionDto>> CreateAsync(CreateManagerActionDto? dto, int actorUserId, CancellationToken ct = default)
        {
            if (dto == null)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ManagerActionBodyRequired);

            if (dto.TeamId is not > 0)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ManagerActionTeamRequired);
            var unitId = dto.TeamId.Value;

            var title = dto.Title?.Trim();
            var titleError = ValidateTitle(title);
            if (titleError != null) return Fail(TeamMilestoneError.BadRequest, titleError);

            var description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
            if (description?.Length > ManagerAction.DescriptionMaxLength)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ManagerActionDescriptionTooLong);

            // Create accepts any valid date: past, today or future.
            var (due, dueError) = ParseDue(dto.DueDate, allowPast: true);
            if (dueError != null) return Fail(TeamMilestoneError.BadRequest, dueError);

            // Start date: required, any valid day; the due date may not come before it.
            if (string.IsNullOrWhiteSpace(dto.StartDate))
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ManagerActionStartDateRequired);
            var start = TeamHealthDates.Parse(dto.StartDate);
            if (start == null)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ManagerActionStartDateInvalid);
            if (due < start)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ManagerActionDueBeforeStart);

            var team = await _context.BusinessUnits.AsNoTracking()
                .Where(b => b.Id == unitId && b.IsDel != true)
                .Select(b => new { b.ProfileId })
                .FirstOrDefaultAsync(ct);
            if (team == null)
                return Fail(TeamMilestoneError.NotFound, ResponseMessages.Errors.ManagerActionTeamNotFound);
            if (!await _access.CanAccessTeamAsync(unitId, team.ProfileId, actorUserId, ct))
                return Fail(TeamMilestoneError.Forbidden, ResponseMessages.Errors.ManagerActionCreateDenied);

            // Optional ERAN suggestion (dropdown). Not sent: no link, as before.
            var (pick, pickError) = await ResolveSuggestionAsync(unitId, dto.EngineActionId, dto.SuggestionChoice, ct);
            if (pickError != null) return Fail(TeamMilestoneError.BadRequest, pickError);

            var now = Now;
            var action = new ManagerAction
            {
                ProfileId = team.ProfileId,
                UnitId = unitId,
                Title = title!,
                Description = description,
                StartDate = start,
                DueDate = due,
                Source = ManagerActionSources.Manual,
                Status = ManagerActionStatuses.Open,
                // Always the logged-in user; never taken from the request.
                CreatedBy = actorUserId,
                CreatedAt = now,
                UpdatedAt = now
            };
            // Source stays MANUAL whether linked or not.
            ApplySuggestion(action, pick);
            _context.ManagerActions.Add(action);
            await _context.SaveChangesAsync(ct);

            return TeamMilestoneResult<ManagerActionDto>.Ok((await MapAsync(new[] { action }, ct))[0]);
        }

        // ── Shared helpers ───────────────────────────────────────────────────

        private static string? ValidateTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return ResponseMessages.Errors.ManagerActionTitleRequired;
            if (title.Length > ManagerAction.TitleMaxLength) return ResponseMessages.Errors.ManagerActionTitleTooLong;
            return null;
        }

        /// <summary>Empty = no due date. Unless <paramref name="allowPast"/>, a due date may be today, never earlier.</summary>
        private (DateOnly? Due, string? Error) ParseDue(string? value, bool allowPast = false)
        {
            if (string.IsNullOrWhiteSpace(value)) return (null, null);
            var due = TeamHealthDates.Parse(value);
            if (due == null) return (null, ResponseMessages.Errors.ManagerActionDueDateInvalid);
            if (!allowPast && due < Today) return (null, ResponseMessages.Errors.ManagerActionDueDateInPast);
            return (due, null);
        }

        private static TeamMilestoneResult<ManagerActionDto> Fail(TeamMilestoneError error, string message) =>
            TeamMilestoneResult<ManagerActionDto>.Fail(error, message);

        private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
