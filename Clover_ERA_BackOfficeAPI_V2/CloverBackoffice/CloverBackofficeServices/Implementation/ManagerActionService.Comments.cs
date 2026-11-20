using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.TeamHealth;

namespace CloverBackoffice.Services.Implementation
{
    // Comments on api/actions (POST api/actions/{id}/comments).
    //
    // A manual action ("12") takes a comment at any status: it has no "I'll do it" step, so a
    // manager can comment straight from the timeline and then Mark Done (PATCH status DONE).
    // Comments are their own rows (ManagerActionComments), saved when posted, so completing
    // the action never touches them.
    //
    // An engine action ("engine-57") goes through the Team Health action comments
    // (TeamActionService.AddCommentAsync), unchanged: its rules and storage stay as they are.
    public partial class ManagerActionService
    {
        public async Task<TeamMilestoneResult<ManagerActionDto>> AddCommentAsync(
            string? actionId, AddManagerActionCommentDto? dto, int actorUserId, CancellationToken ct = default)
        {
            if (TryEngineId(actionId, out var engineId))
            {
                var added = await _teamActions.AddCommentAsync(engineId,
                    new AddTeamActionCommentDto { Text = dto?.Text }, actorUserId, ct);
                if (!added.IsSuccess)
                    return Fail(added.Error, EngineMessage(added));
                return TeamMilestoneResult<ManagerActionDto>.Ok(await FromEngineAsync(added.Data!, ct));
            }

            // Same access as reading and updating the action.
            var (action, denied) = await LoadVisibleAsync(actionId, actorUserId, track: false, ct);
            if (denied != null) return denied;

            // Same rules and messages as a comment on an engine action.
            var text = dto?.Text?.Trim() ?? string.Empty;
            if (text.Length == 0)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionCommentRequired);
            if (text.Length > ManagerActionComment.TextMaxLength)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ActionCommentTooLong);

            _context.ManagerActionComments.Add(new ManagerActionComment
            {
                ManagerActionId = action!.Id,
                Text = text,
                // Always the logged-in user; never taken from the request.
                CreatedBy = actorUserId,
                CreatedAt = Now
            });
            await _context.SaveChangesAsync(ct);

            return TeamMilestoneResult<ManagerActionDto>.Ok((await MapAsync(new[] { action }, ct))[0]);
        }
    }
}
