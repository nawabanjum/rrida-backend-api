using System.Globalization;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    public partial class ManagerActionService
    {
        // ── Read ─────────────────────────────────────────────────────────────

        public async Task<TeamMilestoneResult<ManagerActionDto>> GetAsync(string? actionId, int actorUserId, CancellationToken ct = default)
        {
            if (TryEngineId(actionId, out var engineId))
                return await GetEngineAsync(engineId, actorUserId, ct);

            var (action, denied) = await LoadVisibleAsync(actionId, actorUserId, track: false, ct);
            if (denied != null) return denied;
            return TeamMilestoneResult<ManagerActionDto>.Ok((await MapAsync(new[] { action! }, ct))[0]);
        }

        public async Task<TeamMilestoneResult<ManagerActionListDto>> ListAsync(ManagerActionQueryDto query, int actorUserId, CancellationToken ct = default)
        {
            query ??= new ManagerActionQueryDto();

            var status = query.Status?.Trim().ToUpperInvariant();
            if (!string.IsNullOrEmpty(status) && !ManagerActionStatuses.All.Contains(status))
                return ListFail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ManagerActionStatusInvalid);
            var source = query.Source?.Trim().ToUpperInvariant();
            if (!string.IsNullOrEmpty(source) && !ManagerActionSources.All.Contains(source))
                return ListFail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ManagerActionSourceInvalid);
            var take = Math.Clamp(query.Take is > 0 ? query.Take.Value : DefaultTake, 1, MaxTake);

            var actor = await LoadActorAsync(actorUserId, ct);
            if (actor == null)
                return ListFail(TeamMilestoneError.Forbidden, ResponseMessages.Errors.ManagerActionAccessDenied);

            // The teams listed: one team, or every team of the profile the caller can open.
            uint? oneTeam = null;
            uint profileId;
            // Whether every team of the scope is already known to be reachable.
            bool allReachable;

            if (query.TeamId is > 0)
            {
                var unitId = query.TeamId.Value;
                var team = await _context.BusinessUnits.AsNoTracking()
                    .Where(b => b.Id == unitId && b.IsDel != true)
                    .Select(b => new { b.ProfileId })
                    .FirstOrDefaultAsync(ct);
                if (team == null)
                    return ListFail(TeamMilestoneError.NotFound, ResponseMessages.Errors.ManagerActionTeamNotFound);
                if (!await _access.CanAccessTeamAsync(unitId, team.ProfileId, actorUserId, ct))
                    return ListFail(TeamMilestoneError.Forbidden, ResponseMessages.Errors.ManagerActionAccessDenied);
                oneTeam = unitId;
                profileId = team.ProfileId;
                allReachable = true;
            }
            else if (actor.IsRridaAdmin)
            {
                if (query.ProfileId is not > 0)
                    return ListFail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ManagerActionProfileRequired);
                profileId = query.ProfileId.Value;
                allReachable = true;
            }
            else
            {
                if (actor.ProfileId is not uint own || (query.ProfileId is > 0 && query.ProfileId != own))
                    return ListFail(TeamMilestoneError.Forbidden, ResponseMessages.Errors.ManagerActionAccessDenied);
                profileId = own;
                allReachable = actor.IsProfileAdmin;
            }

            var teamAccess = new Dictionary<uint, bool>();
            async Task<bool> Reachable(uint unitId, uint unitProfileId)
            {
                if (allReachable) return true;
                if (!teamAccess.TryGetValue(unitId, out var ok))
                {
                    ok = await _access.CanAccessTeamAsync(unitId, unitProfileId, actorUserId, ct);
                    teamAccess[unitId] = ok;
                }
                return ok;
            }

            var actions = new List<ManagerActionDto>();

            // The manager-created actions.
            if (source != ManagerActionSources.Engine)
            {
                var rows = _context.ManagerActions.AsNoTracking().Where(a => !a.IsDel && a.ProfileId == profileId);
                if (oneTeam is uint only) rows = rows.Where(a => a.UnitId == only);
                if (query.CreatedBy is > 0) rows = rows.Where(a => a.CreatedBy == query.CreatedBy);
                if (!string.IsNullOrEmpty(status)) rows = rows.Where(a => a.Status == status);
                if (!string.IsNullOrEmpty(source)) rows = rows.Where(a => a.Source == source);

                var visible = new List<ManagerAction>();
                foreach (var a in await rows.ToListAsync(ct))
                    if (await Reachable(a.UnitId, a.ProfileId)) visible.Add(a);
                actions.AddRange(await MapAsync(visible, ct));
            }

            // The engine actions a manager took up.
            if (string.IsNullOrEmpty(source) || source == ManagerActionSources.Engine)
            {
                var teams = new List<EngineTeam>();
                foreach (var t in await EngineTeamsAsync(profileId, oneTeam, ct))
                    if (await Reachable(t.UnitId, t.ProfileId)) teams.Add(t);
                actions.AddRange(await ListEngineAsync(teams, query.CreatedBy, status, ct));
            }

            // Still to do first (then by due date, earliest first), then the newest.
            var ordered = actions
                .OrderBy(a => ManagerActionStatuses.Active.Contains(a.Status) ? 0 : 1)
                .ThenBy(a => a.DueDate ?? "9999-12-31", StringComparer.Ordinal)
                .ThenByDescending(a => a.CreatedAt)
                .ThenBy(a => a.ActionId, StringComparer.Ordinal)
                .ToList();

            return TeamMilestoneResult<ManagerActionListDto>.Ok(new ManagerActionListDto
            {
                Total = ordered.Count,
                Actions = ordered.Take(take).ToList()
            });
        }

        private static TeamMilestoneResult<ManagerActionListDto> ListFail(TeamMilestoneError error, string message) =>
            TeamMilestoneResult<ManagerActionListDto>.Fail(error, message);

        // ── Update ───────────────────────────────────────────────────────────

        public async Task<TeamMilestoneResult<ManagerActionDto>> UpdateAsync(string? actionId, UpdateManagerActionDto? dto, int actorUserId, CancellationToken ct = default)
        {
            if (dto == null)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ManagerActionBodyRequired);
            if (dto.Title == null && dto.Description == null && dto.DueDate == null && dto.Status == null
                && dto.EngineActionId == null && dto.SuggestionChoice == null)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ManagerActionNoChange);

            if (TryEngineId(actionId, out var engineId))
            {
                // An engine action is the suggestion itself; it cannot be linked to one.
                if (dto.EngineActionId != null || dto.SuggestionChoice != null)
                    return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ManagerActionSuggestionEngineAction);
                return await UpdateEngineAsync(engineId, dto, actorUserId, ct);
            }

            var (action, denied) = await LoadVisibleAsync(actionId, actorUserId, track: true, ct);
            if (denied != null) return denied;

            // Re-pick the ERAN suggestion as on create; "suggestionChoice": "" clears it.
            var (pick, pickError) = await ResolveSuggestionAsync(action!.UnitId, dto.EngineActionId, dto.SuggestionChoice, ct);
            if (pickError != null) return Fail(TeamMilestoneError.BadRequest, pickError);

            if (dto.Title != null)
            {
                var title = dto.Title.Trim();
                var error = ValidateTitle(title);
                if (error != null) return Fail(TeamMilestoneError.BadRequest, error);
                action.Title = title;
            }

            if (dto.Description != null)
            {
                var description = dto.Description.Trim();
                if (description.Length > ManagerAction.DescriptionMaxLength)
                    return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ManagerActionDescriptionTooLong);
                action.Description = description.Length == 0 ? null : description;
            }

            if (dto.DueDate != null)
            {
                // "" clears it. An unchanged past date may be sent back as it is.
                var parsed = TeamHealthDates.Parse(dto.DueDate);
                if (dto.DueDate.Trim().Length == 0) action.DueDate = null;
                else if (parsed == null) return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ManagerActionDueDateInvalid);
                else if (parsed != action.DueDate)
                {
                    var (due, error) = ParseDue(dto.DueDate);
                    if (error != null) return Fail(TeamMilestoneError.BadRequest, error);
                    action.DueDate = due;
                }
            }

            if (dto.Status != null)
            {
                var status = dto.Status.Trim().ToUpperInvariant();
                if (!ManagerActionStatuses.All.Contains(status))
                    return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ManagerActionStatusInvalid);
                if (status != action.Status)
                {
                    action.Status = status;
                    action.CompletedAt = status == ManagerActionStatuses.Done ? Now : null;
                }
            }

            ApplySuggestion(action, pick);
            action.UpdatedBy = actorUserId;
            action.UpdatedAt = Now;
            await _context.SaveChangesAsync(ct);

            return TeamMilestoneResult<ManagerActionDto>.Ok((await MapAsync(new[] { action }, ct))[0]);
        }

        // ── Eran ─────────────────────────────────────────────────────────────

        public async Task<string?> EranChatBlockAsync(uint unitId, CancellationToken ct = default)
        {
            var today = Today;
            var items = await ManagerActionEranContext.OpenAsync(_context, unitId, today, ct);
            return ManagerActionEranContext.ChatBlock(items, today);
        }

        // ── Access and mapping ───────────────────────────────────────────────

        private async Task<(ManagerAction? Action, TeamMilestoneResult<ManagerActionDto>? Denied)> LoadVisibleAsync(
            string? actionId, int actorUserId, bool track, CancellationToken ct)
        {
            if (!long.TryParse(actionId?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                return (null, Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ManagerActionIdInvalid));

            var query = track ? _context.ManagerActions : _context.ManagerActions.AsNoTracking();
            var action = await query.FirstOrDefaultAsync(a => a.Id == id && !a.IsDel, ct);
            if (action == null)
                return (null, Fail(TeamMilestoneError.NotFound, ResponseMessages.Errors.ManagerActionNotFound));

            // Whoever can open the team in Team Health can see and update its actions.
            if (!await _access.CanAccessTeamAsync(action.UnitId, action.ProfileId, actorUserId, ct))
                return (null, Fail(TeamMilestoneError.Forbidden, ResponseMessages.Errors.ManagerActionAccessDenied));

            return (action, null);
        }

        private async Task<List<ManagerActionDto>> MapAsync(IReadOnlyList<ManagerAction> actions, CancellationToken ct)
        {
            var actionIds = actions.Select(a => a.Id).ToList();
            var comments = await _context.ManagerActionComments.AsNoTracking()
                .Where(x => actionIds.Contains(x.ManagerActionId) && !x.IsDel)
                .OrderBy(x => x.CreatedAt)
                .ThenBy(x => x.Id)
                .ToListAsync(ct);

            var userIds = actions.Select(a => a.CreatedBy)
                .Concat(comments.Select(x => x.CreatedBy))
                .Where(id => id > 0).Distinct().ToList();
            var unitIds = actions.Select(a => a.UnitId).Distinct().ToList();

            var names = await _context.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.UserId))
                .Select(u => new { u.UserId, u.Name })
                .ToDictionaryAsync(u => u.UserId, u => u.Name, ct);
            var teams = await _context.BusinessUnits.AsNoTracking()
                .Where(b => unitIds.Contains(b.Id))
                .Select(b => new { b.Id, b.Name })
                .ToDictionaryAsync(b => b.Id, b => b.Name, ct);
            var currentSuggestions = await CurrentSuggestionIdsAsync(actions, ct);

            var today = Today;
            return actions.Select(a =>
            {
                var own = comments.Where(x => x.ManagerActionId == a.Id)
                    .Select(x => new TeamActionCommentDto
                    {
                        CommentId = x.Id,
                        Text = x.Text,
                        CreatedAt = DateTime.SpecifyKind(x.CreatedAt, DateTimeKind.Utc),
                        CreatedBy = new TeamActionUserDto
                        {
                            UserId = x.CreatedBy,
                            Name = names.GetValueOrDefault(x.CreatedBy) ?? string.Empty
                        }
                    })
                    .ToList();
                var (engineActionId, linked) = LinkOf(a, currentSuggestions);

                return new ManagerActionDto
                {
                    ActionId = a.Id.ToString(CultureInfo.InvariantCulture),
                    Title = a.Title,
                    Description = a.Description,
                    ProfileId = a.ProfileId,
                    TeamId = a.UnitId,
                    TeamName = teams.GetValueOrDefault(a.UnitId),
                    CreatedBy = a.CreatedBy,
                    CreatedByName = names.GetValueOrDefault(a.CreatedBy),
                    StartDate = a.StartDate is { } s ? Iso(s) : null,
                    DueDate = a.DueDate is { } d ? Iso(d) : null,
                    IsOverdue = a.DueDate is { } due && due < today && ManagerActionStatuses.Active.Contains(a.Status),
                    Source = a.Source,
                    Status = a.Status,
                    CreatedAt = DateTime.SpecifyKind(a.CreatedAt, DateTimeKind.Utc),
                    UpdatedAt = DateTime.SpecifyKind(a.UpdatedAt, DateTimeKind.Utc),
                    CompletedAt = a.CompletedAt is { } c ? DateTime.SpecifyKind(c, DateTimeKind.Utc) : null,
                    EngineActionId = engineActionId,
                    SuggestionChoice = a.SuggestionChoice,
                    LinkedSuggestion = linked,
                    Comments = own,
                    CommentCount = own.Count
                };
            }).ToList();
        }
    }
}
