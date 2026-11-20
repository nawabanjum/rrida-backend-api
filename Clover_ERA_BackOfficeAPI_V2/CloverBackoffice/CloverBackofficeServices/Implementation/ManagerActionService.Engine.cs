using System.Globalization;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // Engine actions in api/actions.
    //
    // The engine recommends an action; a manager takes it up with "I'll do it"
    // (api/rrida/manager/acknowledge). From then on it is listed here beside the manager's own
    // actions, in the same shape:
    //   actionId     "engine-{id}", where id is the api/team-health/actions id
    //   source       ENGINE
    //   createdBy    whoever took it up (it went on the team's action list then)
    //   createdAt    when it was taken up
    //   status       IN_PROGRESS while in flight, DONE once closed
    //
    // Nothing is copied. Reads come from the engine's rows and the manager's record beside them
    // (TeamActionService); a PATCH goes through TeamActionService too, so close / reopen stay the
    // manager's record and never end the engine cycle. "I did this" (api/rrida/manager/confirm),
    // which the engine reads, is unchanged and separate.
    public partial class ManagerActionService
    {
        public const string EngineIdPrefix = "engine-";

        private sealed record EngineTeam(uint UnitId, uint ProfileId, string? Name);

        private static bool TryEngineId(string? actionId, out string engineId)
        {
            engineId = string.Empty;
            var id = actionId?.Trim();
            if (id == null || !id.StartsWith(EngineIdPrefix, StringComparison.OrdinalIgnoreCase))
                return false;
            engineId = id[EngineIdPrefix.Length..];
            return true;
        }

        /// <summary>IN_FLIGHT → IN_PROGRESS, CLOSED → DONE.</summary>
        internal static string ToActionStatus(string engineStatus) =>
            engineStatus == TeamActionStatuses.Closed ? ManagerActionStatuses.Done : ManagerActionStatuses.InProgress;

        /// <summary>DONE / CANCELLED → CLOSED, OPEN / IN_PROGRESS → IN_FLIGHT.</summary>
        private static string ToEngineStatus(string status) =>
            status is ManagerActionStatuses.Done or ManagerActionStatuses.Cancelled
                ? TeamActionStatuses.Closed
                : TeamActionStatuses.InFlight;

        // ── Read ─────────────────────────────────────────────────────────────

        private async Task<TeamMilestoneResult<ManagerActionDto>> GetEngineAsync(string engineId, int actorUserId, CancellationToken ct)
        {
            var result = await _teamActions.GetAsync(engineId, actorUserId, ct);
            if (!result.IsSuccess)
                return Fail(result.Error, EngineMessage(result));
            return TeamMilestoneResult<ManagerActionDto>.Ok(await FromEngineAsync(result.Data!, ct));
        }

        private async Task<ManagerActionDto> FromEngineAsync(TeamActionDto a, CancellationToken ct)
        {
            var unitId = uint.Parse(a.TeamId, CultureInfo.InvariantCulture);
            var team = await _context.BusinessUnits.AsNoTracking()
                .Where(b => b.Id == unitId)
                .Select(b => new { b.ProfileId, b.Name })
                .FirstOrDefaultAsync(ct);
            return EngineDto(a.ActionId, new EngineTeam(unitId, team?.ProfileId ?? 0, team?.Name),
                a.Text, a.DueDate, a.Status, a.EngineOutcome, a.AcknowledgedAt, a.AcknowledgedBy, a.ClosedAt, a.Comments);
        }

        /// <summary>The profile's teams (or the one team) that have taken up an engine action.</summary>
        private async Task<List<EngineTeam>> EngineTeamsAsync(uint profileId, uint? oneTeam, CancellationToken ct)
        {
            var units = _context.BusinessUnits.AsNoTracking()
                .Where(b => b.ProfileId == profileId && b.IsDel != true
                            && _context.EngineAcknowledgements.Any(k => k.BusinessUnitId == b.Id));
            if (oneTeam is uint only)
                units = units.Where(b => b.Id == only);

            return (await units.Select(b => new { b.Id, b.ProfileId, b.Name }).ToListAsync(ct))
                .Select(b => new EngineTeam(b.Id, b.ProfileId, b.Name))
                .ToList();
        }

        private async Task<List<ManagerActionDto>> ListEngineAsync(
            IReadOnlyList<EngineTeam> teams, int? createdBy, string? status, CancellationToken ct)
        {
            var list = new List<ManagerActionDto>();
            foreach (var team in teams)
            {
                // The same items as the Team Health timeline dots and GET team-health/actions.
                var items = await TeamActionService.LoadTimelineActionsAsync(
                    _context, team.UnitId, DateOnly.MinValue, DateOnly.MaxValue, ct);
                foreach (var i in items)
                {
                    var dto = EngineDto(i.ActionId, team, i.Text, i.DueDate, i.Status, i.EngineOutcome,
                        i.AcknowledgedAt, i.AcknowledgedBy, closedAt: null, i.Comments);
                    if (createdBy is > 0 && dto.CreatedBy != createdBy) continue;
                    if (!string.IsNullOrEmpty(status) && dto.Status != status) continue;
                    list.Add(dto);
                }
            }
            return list;
        }

        private ManagerActionDto EngineDto(long id, EngineTeam team, string text, string? dueDate, string engineStatus,
            string engineOutcome, DateTime? takenUpAt, TeamActionUserDto? takenUpBy, DateTime? closedAt,
            List<TeamActionCommentDto>? comments)
        {
            comments ??= new List<TeamActionCommentDto>();
            var status = ToActionStatus(engineStatus);
            var at = takenUpAt.HasValue ? DateTime.SpecifyKind(takenUpAt.Value, DateTimeKind.Utc) : default;
            var due = TeamHealthDates.Parse(dueDate);
            return new ManagerActionDto
            {
                ActionId = EngineIdPrefix + id.ToString(CultureInfo.InvariantCulture),
                Title = text,
                ProfileId = team.ProfileId,
                TeamId = team.UnitId,
                TeamName = team.Name,
                CreatedBy = takenUpBy?.UserId,
                CreatedByName = takenUpBy?.Name,
                DueDate = dueDate,
                IsOverdue = due is { } d && d < Today && status == ManagerActionStatuses.InProgress,
                Source = ManagerActionSources.Engine,
                Status = status,
                CreatedAt = at,
                UpdatedAt = closedAt.HasValue ? DateTime.SpecifyKind(closedAt.Value, DateTimeKind.Utc) : at,
                CompletedAt = closedAt.HasValue ? DateTime.SpecifyKind(closedAt.Value, DateTimeKind.Utc) : null,
                EngineActionId = id,
                EngineOutcome = engineOutcome,
                Comments = comments,
                CommentCount = comments.Count
            };
        }

        // ── Update ───────────────────────────────────────────────────────────

        /// <summary>
        /// title and dueDate are the manager's wording and due date; status DONE / CANCELLED closes
        /// it and OPEN / IN_PROGRESS reopens it. An engine action has no description.
        /// </summary>
        private async Task<TeamMilestoneResult<ManagerActionDto>> UpdateEngineAsync(
            string engineId, UpdateManagerActionDto dto, int actorUserId, CancellationToken ct)
        {
            if (dto.Description != null)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ManagerActionEngineFieldNotEditable);

            string? engineStatus = null;
            if (dto.Status != null)
            {
                var status = dto.Status.Trim().ToUpperInvariant();
                if (!ManagerActionStatuses.All.Contains(status))
                    return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ManagerActionStatusInvalid);
                engineStatus = ToEngineStatus(status);
            }

            // Access and "in flight" are checked by the Team Health action service.
            var current = await _teamActions.GetAsync(engineId, actorUserId, ct);
            if (!current.IsSuccess)
                return Fail(current.Error, EngineMessage(current));

            if (dto.Title != null || dto.DueDate != null)
            {
                var edited = await _teamActions.UpdateAsync(engineId,
                    new UpdateTeamActionDto { Text = dto.Title, DueDate = dto.DueDate }, actorUserId, ct);
                if (!edited.IsSuccess)
                    return Fail(edited.Error, EngineMessage(edited));
            }

            // Setting the status it already has is a no-op, not an error.
            if (engineStatus != null && engineStatus != current.Data!.Status)
            {
                var changed = await _teamActions.UpdateStatusAsync(engineId,
                    new UpdateTeamActionStatusDto { Status = engineStatus }, actorUserId, ct);
                if (!changed.IsSuccess)
                    return Fail(changed.Error, EngineMessage(changed));
            }

            return await GetEngineAsync(engineId, actorUserId, ct);
        }

        /// <summary>The engine action's own message, except a bad id, which is worded for api/actions.</summary>
        private static string EngineMessage<T>(TeamMilestoneResult<T> result) =>
            result.Message == ResponseMessages.Errors.ActionIdInvalid
                ? ResponseMessages.Errors.ManagerActionIdInvalid
                : result.Message;
    }
}
