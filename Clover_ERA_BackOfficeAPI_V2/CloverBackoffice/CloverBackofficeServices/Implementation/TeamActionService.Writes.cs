using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // Edit, close / reopen and comment. See TeamActionService.cs for the rules.
    public partial class TeamActionService
    {
        public async Task<TeamMilestoneResult<TeamActionDto>> UpdateAsync(string? actionId, UpdateTeamActionDto? dto, int actorUserId, CancellationToken ct = default)
        {
            var (action, denied) = await LoadAsync(actionId, actorUserId, ct);
            if (denied != null)
                return denied;

            if (dto == null || (dto.Text == null && dto.DueDate == null))
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionNothingToUpdate);

            string? text = null;
            if (dto.Text != null)
            {
                text = dto.Text.Trim();
                if (text.Length == 0)
                    return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionTextRequired);
                if (text.Length > TeamActionState.TextMaxLength)
                    return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionTextTooLong);
            }

            DateOnly? dueDate = null;
            var clearDueDate = dto.DueDate != null && dto.DueDate.Trim().Length == 0;
            if (dto.DueDate != null && !clearDueDate)
            {
                dueDate = TeamHealthDates.Parse(dto.DueDate);
                if (dueDate == null)
                    return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionDueDateInvalid);
                if (action!.Date.HasValue && dueDate.Value < action.Date.Value)
                    return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionDueDateBeforeAction);
            }

            return await WriteAsync(action!, dto.Version, actorUserId, ct, state =>
            {
                var changes = new List<string>();
                if (text != null)
                {
                    // Back to the delivered wording reads as "not edited".
                    var newText = text == (action!.DeliveredText ?? string.Empty).Trim() ? null : text;
                    if (newText != state.Text)
                        changes.Add(newText == null ? "text restored to the delivered wording" : "text edited");
                    state.Text = newText;
                }
                if (clearDueDate || dueDate.HasValue)
                {
                    var newDue = clearDueDate ? null : dueDate;
                    if (newDue != state.DueDate)
                        changes.Add(newDue == null ? "due date cleared" : $"due date set to {Iso(newDue.Value)}");
                    state.DueDate = newDue;
                }
                return ("EDIT", changes.Count == 0 ? "saved with no change" : string.Join("; ", changes));
            });
        }

        public async Task<TeamMilestoneResult<TeamActionDto>> UpdateStatusAsync(string? actionId, UpdateTeamActionStatusDto? dto, int actorUserId, CancellationToken ct = default, bool confirmEngineOnClose = false)
        {
            var (action, denied) = await LoadAsync(actionId, actorUserId, ct);
            if (denied != null)
                return denied;

            var wanted = dto?.Status?.Trim().ToUpperInvariant();
            if (wanted != TeamActionStatuses.Closed && wanted != TeamActionStatuses.InFlight)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionStatusInvalid);

            var current = await FindStateAsync(action!, tracked: false, ct);
            if (current != null && current.ActionKey != action!.ActionKey)
                current = null;
            if (StatusOf(current, action!.EngineOutcome) == wanted)
                return Fail(TeamMilestoneError.BadRequest, wanted == TeamActionStatuses.Closed
                    ? ResponseMessages.Errors.ActionAlreadyClosed
                    : ResponseMessages.Errors.ActionAlreadyInFlight);

            var confirmation = confirmEngineOnClose && wanted == TeamActionStatuses.Closed
                ? await EngineConfirmationForAsync(action, actorUserId, ct)
                : null;

            return await WriteAsync(action, dto!.Version, actorUserId, ct, state =>
            {
                state.Status = wanted;
                if (wanted == TeamActionStatuses.Closed)
                {
                    state.ClosedAt = _clock.GetUtcNow().UtcDateTime;
                    state.ClosedBy = actorUserId;
                    if (confirmation != null)
                    {
                        // Saved with the close, in the same SaveChanges: both or neither.
                        _context.EngineConfirmations.Add(confirmation);
                        return ("CLOSE", $"marked as closed (engine outcome: {action.EngineOutcome}); \"I did this\" confirmed with the engine");
                    }
                    return ("CLOSE", $"marked as closed (engine outcome: {action.EngineOutcome})");
                }

                // Reopened: the closure is history now, kept in the audit, not on the record.
                state.ClosedAt = null;
                state.ClosedBy = null;
                return ("REOPEN", $"reopened (engine outcome: {action.EngineOutcome})");
            });
        }

        // The timeline's "I did this" is the same tap as This Week's Move "I did this"
        // (EngineManagerService.ConfirmAsync): the action was taken and its condition met. It is
        // recorded with the engine under the same rules — the cycle's current rung, the cycle still
        // open, not declined and not confirmed yet. Anything else (an older rung, a resolved or
        // declined cycle, already confirmed, no engine settings) closes the manager's record only,
        // as before. Like ConfirmAsync, nothing is replayed: the daily run takes the tap in.
        private async Task<EngineConfirmation?> EngineConfirmationForAsync(ActionRow action, int actorUserId, CancellationToken ct)
        {
            if (_engine == null || action.CycleOutcome != null || action.DeliveryOutcome != null || action.ConfirmedDay != null)
                return null;

            var currentRung = await _context.EngineCycleDeliveries.AsNoTracking()
                .Where(d => d.CycleId == action.CycleId)
                .MaxAsync(d => (int?)d.Rung, ct);
            if (currentRung != action.Rung)
                return null;

            if (await _context.EngineDeclines.AsNoTracking()
                    .AnyAsync(d => d.BusinessUnitId == action.UnitId && d.CycleKey == action.CycleKey, ct))
                return null;

            if (await _context.EngineConfirmations.AsNoTracking()
                    .AnyAsync(c => c.BusinessUnitId == action.UnitId && c.CycleKey == action.CycleKey && c.Rung == action.Rung, ct))
                return null;

            var settings = await _context.EngineTeamSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.BusinessUnitId == action.UnitId, ct);
            if (settings == null)
                return null;

            EngineTeamClock clock;
            try
            {
                clock = new EngineTeamClock(settings.StartDate, settings.WorkingWeek, _engine.Calendar);
            }
            catch (InvalidOperationException)
            {
                return null;   // a bad stored working week: close only, never fail the close
            }

            var now = _clock.GetUtcNow().UtcDateTime;
            var today = DateOnly.FromDateTime(now);
            return new EngineConfirmation
            {
                BusinessUnitId = action.UnitId,
                CycleKey = action.CycleKey,
                Rung = action.Rung,
                ConfirmedDate = today,
                Day = clock.DayOf(today),
                AskedDayIndex = clock.AskedDayIndexOf(today),
                WorkedIfMet = true,
                ConfirmedByUserId = actorUserId,
                Source = "manager",
                CreatedAt = now
            };
        }

        public async Task<TeamMilestoneResult<TeamActionDto>> AddCommentAsync(string? actionId, AddTeamActionCommentDto? dto, int actorUserId, CancellationToken ct = default)
        {
            var (action, denied) = await LoadAsync(actionId, actorUserId, ct);
            if (denied != null)
                return denied;

            var text = dto?.Text?.Trim() ?? string.Empty;
            if (text.Length == 0)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionCommentRequired);
            if (text.Length > TeamActionComment.TextMaxLength)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionCommentTooLong);

            var now = _clock.GetUtcNow().UtcDateTime;
            var comment = new TeamActionComment
            {
                ProfileId = action!.ProfileId,
                UnitId = action.UnitId,
                CycleKey = action.CycleKey,
                Rung = action.Rung,
                ActionKey = action.ActionKey,
                Text = text,
                CreatedBy = actorUserId,
                CreatedAt = now
            };
            _context.TeamActionComments.Add(comment);
            await _context.SaveChangesAsync(ct);

            var state = await FindStateAsync(action, tracked: false, ct);
            _context.TeamActionAudits.Add(Audit(action, state?.ActionKey == action.ActionKey ? state : null,
                "COMMENT", "comment added", actorUserId, now, comment.Id));
            await _context.SaveChangesAsync(ct);

            return TeamMilestoneResult<TeamActionDto>.Ok(await BuildAsync(action, ct));
        }

        // Loads (or starts) the manager's record, checks the caller's version, applies
        // the change, bumps the version and writes the audit row in one save. A stale
        // version, a concurrent change or a racing first write is a 409.
        private async Task<TeamMilestoneResult<TeamActionDto>> WriteAsync(
            ActionRow action, int? expectedVersion, int actorUserId, CancellationToken ct,
            Func<TeamActionState, (string Operation, string Summary)> apply)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var state = await FindStateAsync(action, tracked: true, ct);

            // The version the caller was shown: 0 for a record left from another action,
            // since reads ignore it.
            var currentVersion = state != null && state.ActionKey == action.ActionKey ? state.Version : 0;

            if (state != null && state.ActionKey != action.ActionKey)
            {
                // Left from a different action under the same cycle key: start it afresh.
                state.ActionKey = action.ActionKey;
                state.Text = null;
                state.DueDate = null;
                state.Status = null;
                state.ClosedAt = null;
                state.ClosedBy = null;
            }

            if (expectedVersion.HasValue && expectedVersion.Value != currentVersion)
                return Fail(TeamMilestoneError.Conflict, ResponseMessages.Errors.ActionStale);

            if (state == null)
            {
                state = new TeamActionState
                {
                    ProfileId = action.ProfileId,
                    UnitId = action.UnitId,
                    CycleKey = action.CycleKey,
                    Rung = action.Rung,
                    ActionKey = action.ActionKey,
                    CreatedBy = actorUserId,
                    CreatedAt = now
                };
                _context.TeamActionStates.Add(state);
            }

            var (operation, summary) = apply(state);
            state.Version = currentVersion + 1;
            state.UpdatedBy = actorUserId;
            state.UpdatedAt = now;
            _context.TeamActionAudits.Add(Audit(action, state, operation, summary, actorUserId, now, null));

            try
            {
                await _context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // DbUpdateConcurrencyException (version moved) or the unique key (two
                // first writes): either way someone else got there first.
                _context.ChangeTracker.Clear();
                return Fail(TeamMilestoneError.Conflict, ResponseMessages.Errors.ActionStale);
            }

            return TeamMilestoneResult<TeamActionDto>.Ok(await BuildAsync(action, ct));
        }

        private static TeamActionAudit Audit(ActionRow action, TeamActionState? state, string operation,
            string summary, int actorUserId, DateTime now, long? commentId) => new()
        {
            ProfileId = action.ProfileId,
            UnitId = action.UnitId,
            CycleKey = action.CycleKey,
            Rung = action.Rung,
            ActionKey = action.ActionKey,
            OperationType = operation,
            Text = state?.Text,
            DueDate = state?.DueDate,
            Status = state?.Status,
            Version = state?.Version ?? 0,
            CommentId = commentId,
            ChangeSummary = summary,
            ByUserId = actorUserId,
            LoggedAt = now
        };
    }
}
