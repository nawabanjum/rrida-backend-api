using System.Globalization;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // Team Health → Action Management. An action is one rung of one engine cycle
    // (EngineCycleDeliveries; actionId is that row's id). The engine owns it and rebuilds
    // it on every run, so the manager's edits, status and comments are kept beside it in
    // TeamActionStates / TeamActionComments, matched on (team, cycle key, rung, action
    // key), and never change what the engine reads or decides. One exception: the timeline's
    // "I did this" (PATCH team-health/actions/{id}/status CLOSED) on the cycle's current, open,
    // unconfirmed rung also records the engine confirmation, the same tap as This Week's Move
    // "I did this" (api/rrida/manager/confirm), so the two never disagree.
    //
    // In flight: a suggested action is only an Action in Flight once a manager has taken
    // it up — "I'll do it" (or "I did this", which records the same), stored in
    // EngineAcknowledgements, one per (team, cycle key, rung). Until then it has no dot on
    // the timeline, is not listed and its panel is a 404. Its date is the day it was taken up.
    //
    // Status
    //   engine outcome  running (no outcome yet), closed, failed, cannot_attribute — the
    //                   engine's own words, always returned as they are.
    //   status          IN_FLIGHT or CLOSED. Follows the engine (running = IN_FLIGHT,
    //                   anything resolved = CLOSED) until a manager closes or reopens it;
    //                   from then on the manager's choice is shown.
    //   transitions     IN_FLIGHT -> CLOSED (mark as closed), CLOSED -> IN_FLIGHT
    //                   (reopen). Anything else is a 400.
    //
    // Concurrency: every change bumps Version. A caller that sends the version it read
    // gets 409 when someone changed it since; two first writes racing are caught by the
    // unique key and also come back 409. Every change is written to TeamActionAudits.
    //
    // Access: the Team Health rule (TeamHealthAccessService), the same as milestones.
    public partial class TeamActionService : ITeamActionService
    {
        private readonly ApplicationDbContext _context;
        private readonly ITeamHealthAccessService _access;
        private readonly TimeProvider _clock;
        private readonly IRridaEngineContextFactory? _engine;

        public TeamActionService(ApplicationDbContext context, ITeamHealthAccessService access, TimeProvider? clock = null,
            IRridaEngineContextFactory? engine = null)
        {
            _context = context;
            _access = access;
            _clock = clock ?? TimeProvider.System;
            // The kit's calendar, for the engine day of a timeline "I did this" confirmation.
            _engine = engine;
        }

        // ── Read ─────────────────────────────────────────────────────────────

        public async Task<TeamMilestoneResult<TeamActionDto>> GetAsync(string? actionId, int actorUserId, CancellationToken ct = default)
        {
            var (action, denied) = await LoadAsync(actionId, actorUserId, ct);
            if (denied != null)
                return denied;

            return TeamMilestoneResult<TeamActionDto>.Ok(await BuildAsync(action!, ct));
        }

        public async Task<TeamMilestoneResult<TeamActionListDto>> ListAsync(string? teamId, string? from, string? to, int actorUserId, CancellationToken ct = default)
        {
            if (!uint.TryParse(teamId?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var unitId) || unitId == 0)
                return ListFail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.TimelineTeamIdInvalid);

            // Same rules as GET custom-questions: both optional, inclusive, YYYY-MM-DD or DD-MM-YYYY.
            DateOnly? fromDate = null, toDate = null;
            if (!string.IsNullOrWhiteSpace(from))
            {
                fromDate = TeamHealthDates.Parse(from);
                if (fromDate == null)
                    return ListFail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.MilestoneDateInvalid);
            }
            if (!string.IsNullOrWhiteSpace(to))
            {
                toDate = TeamHealthDates.Parse(to);
                if (toDate == null)
                    return ListFail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.MilestoneDateInvalid);
            }
            if (fromDate > toDate)
                return ListFail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.MilestoneDateRangeInvalid);

            var unit = await _context.BusinessUnits
                .AsNoTracking()
                .Where(u => u.Id == unitId && u.IsDel != true)
                .Select(u => new { u.ProfileId })
                .FirstOrDefaultAsync(ct);
            if (unit == null)
                return ListFail(TeamMilestoneError.NotFound, ResponseMessages.Errors.MilestoneTeamNotFound);

            if (!await _access.CanAccessTeamAsync(unitId, unit.ProfileId, actorUserId, ct))
                return ListFail(TeamMilestoneError.Forbidden, ResponseMessages.Errors.ActionAccessDenied);

            // No range = every action the team has had.
            var actions = await LoadTimelineActionsAsync(_context, unitId,
                fromDate ?? DateOnly.MinValue, toDate ?? DateOnly.MaxValue, ct);

            return TeamMilestoneResult<TeamActionListDto>.Ok(new TeamActionListDto
            {
                TeamId = unitId.ToString(CultureInfo.InvariantCulture),
                From = fromDate.HasValue ? Iso(fromDate.Value) : null,
                To = toDate.HasValue ? Iso(toDate.Value) : null,
                Actions = actions
            });
        }

        private static TeamMilestoneResult<TeamActionListDto> ListFail(TeamMilestoneError error, string message) =>
            TeamMilestoneResult<TeamActionListDto>.Fail(error, message);

        // One action with what the panel needs, from the engine's rows.
        private sealed record ActionRow(
            long ActionId, long CycleId, uint UnitId, uint ProfileId, string CycleKey, int Rung, string ActionKey,
            string? DeliveredText, string? WorkedIf, int DeliveredDay, int? ConfirmedDay,
            string? DeliveryOutcome, string? CycleOutcome, DateOnly? StartDate,
            DateTime? AcknowledgedAt, int? AcknowledgedBy)
        {
            // The day it was taken up ("I'll do it"): the day it went in flight.
            public DateOnly? Date => AcknowledgedAt.HasValue ? DateOnly.FromDateTime(AcknowledgedAt.Value) : null;

            public string EngineOutcome => EngineOutcomeOf(DeliveryOutcome, CycleOutcome);
        }

        private async Task<(ActionRow? Action, TeamMilestoneResult<TeamActionDto>? Denied)> LoadAsync(string? actionId, int actorUserId, CancellationToken ct)
        {
            if (!long.TryParse(actionId?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                return (null, Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionIdInvalid));

            var action = await (
                from d in _context.EngineCycleDeliveries.AsNoTracking()
                join c in _context.EngineCycles.AsNoTracking() on d.CycleId equals c.Id
                join u in _context.BusinessUnits.AsNoTracking() on c.BusinessUnitId equals u.Id
                where d.Id == id && u.IsDel != true
                select new ActionRow(
                    d.Id, c.Id, c.BusinessUnitId, u.ProfileId, c.CycleKey, d.Rung, d.ActionKey,
                    d.DeliveredText, d.DeliveredWorkedIf, d.DeliveredDay, d.ConfirmedDay,
                    d.Outcome, c.Outcome,
                    _context.EngineTeamSettings
                        .Where(s => s.BusinessUnitId == c.BusinessUnitId)
                        .Select(s => (DateOnly?)s.StartDate)
                        .FirstOrDefault(),
                    _context.EngineAcknowledgements
                        .Where(k => k.BusinessUnitId == c.BusinessUnitId && k.CycleKey == c.CycleKey && k.Rung == d.Rung)
                        .Select(k => (DateTime?)k.AcknowledgedAt)
                        .FirstOrDefault(),
                    _context.EngineAcknowledgements
                        .Where(k => k.BusinessUnitId == c.BusinessUnitId && k.CycleKey == c.CycleKey && k.Rung == d.Rung)
                        .Select(k => k.AcknowledgedByUserId)
                        .FirstOrDefault()))
                .FirstOrDefaultAsync(ct);
            if (action == null)
                return (null, Fail(TeamMilestoneError.NotFound, ResponseMessages.Errors.ActionNotFound));

            if (!await _access.CanAccessTeamAsync(action.UnitId, action.ProfileId, actorUserId, ct))
                return (null, Fail(TeamMilestoneError.Forbidden, ResponseMessages.Errors.ActionAccessDenied));

            // Suggested but never taken up: not an action in flight, so nothing to show or change.
            if (action.AcknowledgedAt == null)
                return (null, Fail(TeamMilestoneError.NotFound, ResponseMessages.Errors.ActionNotInFlight));

            return (action, null);
        }

        // The manager's record for this action, ignoring one left from a different
        // action under the same cycle key (engine data reset).
        private Task<TeamActionState?> FindStateAsync(ActionRow a, bool tracked, CancellationToken ct)
        {
            var query = tracked ? _context.TeamActionStates : _context.TeamActionStates.AsNoTracking();
            return query.FirstOrDefaultAsync(s => s.UnitId == a.UnitId && s.CycleKey == a.CycleKey && s.Rung == a.Rung, ct);
        }

        private async Task<TeamActionDto> BuildAsync(ActionRow a, CancellationToken ct)
        {
            var state = await FindStateAsync(a, tracked: false, ct);
            if (state != null && state.ActionKey != a.ActionKey)
                state = null;

            var comments = await _context.TeamActionComments
                .AsNoTracking()
                .Where(c => c.UnitId == a.UnitId && c.CycleKey == a.CycleKey && c.Rung == a.Rung
                            && c.ActionKey == a.ActionKey && !c.IsDel)
                .OrderBy(c => c.CreatedAt)
                .ThenBy(c => c.Id)
                .ToListAsync(ct);

            var userIds = comments.Where(c => c.CreatedBy.HasValue).Select(c => c.CreatedBy!.Value).ToList();
            if (state?.ClosedBy != null)
                userIds.Add(state.ClosedBy.Value);
            if (a.AcknowledgedBy != null)
                userIds.Add(a.AcknowledgedBy.Value);
            var names = userIds.Count == 0
                ? new Dictionary<int, string>()
                : await _context.Users
                    .AsNoTracking()
                    .Where(u => userIds.Contains(u.UserId))
                    .ToDictionaryAsync(u => u.UserId, u => u.Name ?? string.Empty, ct);

            var original = a.DeliveredText ?? string.Empty;
            var status = StatusOf(state, a.EngineOutcome);
            var closed = status == TeamActionStatuses.Closed && state?.Status == TeamActionStatuses.Closed;

            return new TeamActionDto
            {
                ActionId = a.ActionId,
                TeamId = a.UnitId.ToString(CultureInfo.InvariantCulture),
                CycleId = a.CycleId,
                Rung = a.Rung,
                Date = a.Date.HasValue ? Iso(a.Date.Value) : string.Empty,
                Text = state?.Text ?? original,
                OriginalText = original,
                IsEdited = state?.Text != null,
                WorkedIf = a.WorkedIf,
                DueDate = state?.DueDate.HasValue == true ? Iso(state.DueDate!.Value) : null,
                Status = status,
                StatusSource = state?.Status != null ? "manager" : "engine",
                EngineOutcome = a.EngineOutcome,
                ConfirmedOn = a.ConfirmedDay.HasValue && a.StartDate.HasValue
                    ? Iso(a.StartDate.Value.AddDays(a.ConfirmedDay.Value - 1))
                    : null,
                AcknowledgedAt = a.AcknowledgedAt,
                AcknowledgedBy = a.AcknowledgedBy.HasValue ? User(a.AcknowledgedBy.Value, names) : null,
                ClosedAt = closed ? state!.ClosedAt : null,
                ClosedBy = closed && state!.ClosedBy.HasValue ? User(state.ClosedBy.Value, names) : null,
                CanEdit = true,
                CanClose = status == TeamActionStatuses.InFlight,
                CanReopen = status == TeamActionStatuses.Closed,
                Version = state?.Version ?? 0,
                Comments = comments.Select(c => ToCommentDto(c, names)).ToList()
            };
        }

        // ── Timeline ─────────────────────────────────────────────────────────

        /// <summary>
        /// The team's actions in flight — taken up ("I'll do it") in [from, to] — oldest
        /// first, for the dots on the Team Health timeline. A suggested action nobody took up
        /// has no dot. Shared with TeamHealthTimelineService so the dot and the panel always agree.
        /// </summary>
        public static async Task<List<TimelineActionDto>> LoadTimelineActionsAsync(
            ApplicationDbContext context, uint unitId, DateOnly from, DateOnly to, CancellationToken ct)
        {
            // One acknowledgement per (team, cycle key, rung) and one delivery per rung, so
            // tapping "I'll do it" again never makes a second dot.
            var acknowledged = await (
                from d in context.EngineCycleDeliveries.AsNoTracking()
                join c in context.EngineCycles.AsNoTracking() on d.CycleId equals c.Id
                join k in context.EngineAcknowledgements.AsNoTracking()
                    on new { Unit = c.BusinessUnitId, Key = c.CycleKey, d.Rung }
                    equals new { Unit = k.BusinessUnitId, Key = k.CycleKey, k.Rung }
                where c.BusinessUnitId == unitId
                select new
                {
                    d.Id, CycleId = c.Id, c.CycleKey, d.Rung, d.ActionKey, d.DeliveredText,
                    DOutcome = d.Outcome, COutcome = c.Outcome, k.AcknowledgedAt, k.AcknowledgedByUserId
                })
                .ToListAsync(ct);

            // The window is on the day it was taken up, in memory: a team takes up a handful.
            var rows = acknowledged
                .Where(r => DateOnly.FromDateTime(r.AcknowledgedAt) >= from && DateOnly.FromDateTime(r.AcknowledgedAt) <= to)
                .OrderBy(r => r.AcknowledgedAt)
                .ThenBy(r => r.Rung)
                .ThenBy(r => r.Id)
                .ToList();
            if (rows.Count == 0)
                return new List<TimelineActionDto>();

            var keys = rows.Select(r => r.CycleKey).Distinct().ToList();
            var states = await context.TeamActionStates
                .AsNoTracking()
                .Where(s => s.UnitId == unitId && keys.Contains(s.CycleKey))
                .ToListAsync(ct);
            var comments = await context.TeamActionComments
                .AsNoTracking()
                .Where(c => c.UnitId == unitId && keys.Contains(c.CycleKey) && !c.IsDel)
                .OrderBy(c => c.CreatedAt)
                .ThenBy(c => c.Id)
                .ToListAsync(ct);
            var authorIds = comments.Where(c => c.CreatedBy.HasValue).Select(c => c.CreatedBy!.Value)
                .Concat(rows.Where(r => r.AcknowledgedByUserId.HasValue).Select(r => r.AcknowledgedByUserId!.Value))
                .Distinct().ToList();
            var names = authorIds.Count == 0
                ? new Dictionary<int, string>()
                : await context.Users
                    .AsNoTracking()
                    .Where(u => authorIds.Contains(u.UserId))
                    .ToDictionaryAsync(u => u.UserId, u => u.Name ?? string.Empty, ct);

            return rows.Select(r =>
            {
                var state = states.FirstOrDefault(s => s.CycleKey == r.CycleKey && s.Rung == r.Rung && s.ActionKey == r.ActionKey);
                var outcome = EngineOutcomeOf(r.DOutcome, r.COutcome);
                var own = comments
                    .Where(c => c.CycleKey == r.CycleKey && c.Rung == r.Rung && c.ActionKey == r.ActionKey)
                    .Select(c => ToCommentDto(c, names))
                    .ToList();
                return new TimelineActionDto
                {
                    ActionId = r.Id,
                    CycleId = r.CycleId,
                    Rung = r.Rung,
                    Date = Iso(DateOnly.FromDateTime(r.AcknowledgedAt)),
                    Text = state?.Text ?? r.DeliveredText ?? string.Empty,
                    Status = StatusOf(state, outcome),
                    DueDate = state?.DueDate.HasValue == true ? Iso(state.DueDate!.Value) : null,
                    EngineOutcome = outcome,
                    AcknowledgedAt = r.AcknowledgedAt,
                    AcknowledgedBy = r.AcknowledgedByUserId.HasValue ? User(r.AcknowledgedByUserId.Value, names) : null,
                    CommentCount = own.Count,
                    Comments = own
                };
            }).ToList();
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        // The rung's own outcome first (a laddered rung 1 is resolved while the cycle
        // runs on), then the cycle's (a decline resolves the cycle), else still running.
        private static string EngineOutcomeOf(string? deliveryOutcome, string? cycleOutcome) =>
            deliveryOutcome ?? cycleOutcome ?? TeamActionEngineOutcomes.Running;

        private static string StatusOf(TeamActionState? state, string engineOutcome) =>
            state?.Status
            ?? (engineOutcome == TeamActionEngineOutcomes.Running ? TeamActionStatuses.InFlight : TeamActionStatuses.Closed);

        private static TeamActionCommentDto ToCommentDto(TeamActionComment c, Dictionary<int, string> names) => new()
        {
            CommentId = c.Id,
            Text = c.Text,
            CreatedAt = c.CreatedAt,
            CreatedBy = c.CreatedBy.HasValue ? User(c.CreatedBy.Value, names) : null
        };

        private static TeamActionUserDto User(int userId, Dictionary<int, string> names) =>
            new() { UserId = userId, Name = names.GetValueOrDefault(userId, string.Empty) };

        private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static TeamMilestoneResult<TeamActionDto> Fail(TeamMilestoneError error, string message) =>
            TeamMilestoneResult<TeamActionDto>.Fail(error, message);
    }
}
