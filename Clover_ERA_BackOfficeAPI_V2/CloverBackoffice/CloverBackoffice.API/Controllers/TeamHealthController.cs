using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    /// <summary>
    /// Team Health page. Milestones: things that happened to a team, pinned to the
    /// Team Health timeline so a change in the readings can be read against what
    /// caused it. Adding one never recalculates or changes a reading.
    ///
    /// The team is named by <c>unitId</c> (in the body for POST, the query string for
    /// GET), so the global CompanyTenantGuardFilter refuses another company's team
    /// (403) before the action runs; the service then checks the caller's role on it.
    /// </summary>
    [Route("api/team-health")]
    [ApiController]
    [Authorize]
    public class TeamHealthController : ControllerBase
    {
        private readonly ITeamMilestoneService _milestones;
        private readonly ITeamHealthTimelineService _timeline;
        private readonly ITeamCustomQuestionService _customQuestions;
        private readonly ITeamHealthUserTeamsService _userTeams;
        private readonly ITeamActionService _actions;
        private readonly ITokenReaderService _tokenReader;

        private readonly IPlanFeatureGate _planGate;

        public TeamHealthController(ITeamMilestoneService milestones, ITeamHealthTimelineService timeline, ITeamCustomQuestionService customQuestions, ITeamHealthUserTeamsService userTeams, ITeamActionService actions, ITokenReaderService tokenReader, IPlanFeatureGate planGate)
        {
            _planGate = planGate;
            _actions = actions;
            _milestones = milestones;
            _timeline = timeline;
            _customQuestions = customQuestions;
            _userTeams = userTeams;
            _tokenReader = tokenReader;
        }

        // The whole Team Health Timeline screen in one call: the 30-day chart
        // (Individual Engagement, Team Conditions, milestones), the response rate,
        // and every drill-down (the six CLOVER dimensions, the three Team
        // Conditions groups, the 7-working-day response-rate popup) and the custom
        // questions asked in the period with their anonymous answers. Opening a
        // card needs no further call.
        //
        //   GET /api/team-health/timeline?teamId=126
        //
        // 200 with the timeline; 400 teamId missing or not a positive number;
        // 401 no user in token; 403 not the caller's team; 404 no such team;
        // 500 unexpected failure. Values that cannot be worked out are null.
        //
        // teamId is not a *UnitId name, so the tenant guard does not see it; the
        // service's own team access check covers both company and role.
        [HttpGet("timeline")]
        public async Task<IActionResult> GetTimeline([FromQuery] string? teamId)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (userId == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                var result = await _timeline.GetTimelineAsync(teamId, userId.Value, HttpContext.RequestAborted);
                if (!result.IsSuccess)
                    return Failure(result.Error, result.Message);

                // Plan: this call also feeds the Team Health Dashboard (in every plan), so a plan
                // without the Interactive Team Health Timeline (Light) still gets the dashboard
                // values; only the 30-day chart series and milestones are left out.
                var gate = await _planGate.CheckAsync(userId.Value, PlanFeatureKeys.TeamHealthTimeline, HttpContext.RequestAborted);
                if (!gate.Allowed && result.Data != null)
                {
                    result.Data.IsTimelineAvailable = false;
                    result.Data.TimelineUnavailableMessage = gate.Message;
                    result.Data.Timeline.IndividualEngagement.Data.Clear();
                    result.Data.Timeline.TeamConditions.Data.Clear();
                    result.Data.Timeline.ResponseRate.Data.Clear();
                    result.Data.Timeline.Milestones.Clear();
                }

                return Ok(ApiResponseHandler.Success(result.Data, ResponseMessages.Success.TimelineSuccess));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        // "+ Add milestone" and "Edit" on the Team Health timeline (upsert).
        //
        //   POST /api/team-health/milestones
        //   { "id": 0,                          // 0 / absent = add, milestone id = edit
        //     "unitId": 294,
        //     "title": "New leader joined",
        //     "description": "A new team leader joined the Support Desk team.",
        //     "date": "2026-09-29" }            // or "29-09-2026"
        //
        // 200 with the milestone (date always "YYYY-MM-DD"); 400 invalid field;
        // 401 no user in token; 403 not the caller's team; 404 no such team, or
        // (edit) no such milestone on that team.
        [HttpPost("milestones")]
        public async Task<IActionResult> UpsertMilestone([FromBody] UpsertTeamMilestoneDto? dto)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (userId == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                // Plan: milestones belong to the Interactive Team Health Timeline (Pro).
                var gate = await _planGate.CheckAsync(userId.Value, PlanFeatureKeys.TeamHealthTimeline, HttpContext.RequestAborted);
                if (!gate.Allowed)
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponseHandler.Fail<string>(gate.Message!));

                var result = await _milestones.UpsertAsync(dto, userId.Value, HttpContext.RequestAborted);
                if (!result.IsSuccess)
                    return Failure(result.Error, result.Message);

                var message = dto!.Id > 0 ? ResponseMessages.Success.MilestoneUpdated : ResponseMessages.Success.MilestoneCreated;
                return Ok(ApiResponseHandler.Success(result.Data, message));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        // Milestones for the Team Health timeline, oldest first.
        //
        //   GET /api/team-health/milestones?unitId=294&from=2026-08-30&to=2026-09-29
        //
        // from / to are optional ("YYYY-MM-DD" or "DD-MM-YYYY"), both inclusive.
        [HttpGet("milestones")]
        public async Task<IActionResult> GetMilestones([FromQuery] uint unitId, [FromQuery] string? from, [FromQuery] string? to)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (userId == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                var result = await _milestones.GetForTeamAsync(unitId, from, to, userId.Value, HttpContext.RequestAborted);
                if (!result.IsSuccess)
                    return Failure(result.Error, result.Message);

                return Ok(ApiResponseHandler.Success(result.Data, ResponseMessages.Success.MilestoneListSuccess));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        // "Schedule a custom question" modal: the scheduled list with its slot
        // counter ("1 of 3 slots used") and the previously asked list.
        //
        //   GET /api/team-health/custom-questions?teamId=188
        //   GET /api/team-health/custom-questions?teamId=188&from=2026-08-01&to=2026-08-31
        //
        // from / to are optional ("YYYY-MM-DD" or "DD-MM-YYYY"), both inclusive. They
        // narrow previouslyAsked (with its answers) to questions asked in the range and
        // return all of them rather than the latest 20; scheduled is never narrowed.
        [HttpGet("custom-questions")]
        public Task<IActionResult> GetCustomQuestions([FromQuery] string? teamId, [FromQuery] string? from, [FromQuery] string? to) =>
            Run((userId, ct) => _customQuestions.GetAsync(teamId, userId, from, to, ct), ResponseMessages.Success.CustomQuestionsFetched);

        // Schedules a one-off question. Returns the team's updated lists.
        //
        //   POST /api/team-health/custom-questions
        //   { "teamId": 188, "question": "Is the new handover checklist saving you time yet?",
        //     "askOn": "2026-10-06" }            // or "06-10-2026"
        //
        // 400 for any rule the modal shows (weekend, past, 3 slots used, day taken,
        // duplicate, over 140 characters); 403 not the caller's team; 404 no such team.
        [HttpPost("custom-questions")]
        public Task<IActionResult> ScheduleCustomQuestion([FromBody] ScheduleCustomQuestionDto? dto) =>
            Run((userId, ct) => _customQuestions.ScheduleAsync(dto, userId, ct), ResponseMessages.Success.CustomQuestionScheduled);

        // The remove button on a scheduled question. Cancels it (history is kept)
        // and returns the team's updated lists. 400 once it has been asked or answered.
        //
        //   DELETE /api/team-health/custom-questions/57
        [HttpDelete("custom-questions/{id}")]
        public Task<IActionResult> RemoveCustomQuestion(uint id) =>
            Run((userId, ct) => _customQuestions.RemoveAsync(id, userId, ct), ResponseMessages.Success.CustomQuestionRemoved);

        // The Team dropdown on Team Health, and the team context for the ERAN Coach
        // chat: every team the user can open in Team Health, with its managers.
        //
        //   POST /api/team-health/user-teams
        //   { "userID": 300 }
        //
        // 200 with the teams (an empty list when the user has none); 400 userId
        // missing; 401 no user in token; 403 another company's user, or someone who
        // is not that user, a RRIDA Admin or their Profile Admin; 404 no such user.
        [HttpPost("user-teams")]
        public Task<IActionResult> GetUserTeams([FromBody] UserTeamsRequestDto? dto) =>
            Run((userId, ct) => _userTeams.GetUserTeamsAsync(dto?.UserId ?? 0, userId, ct), ResponseMessages.Success.UserTeamsFetched);

        // ── Actions (the panel behind an action dot on the timeline) ─────────
        //
        // An action is one rung of an engine cycle; {actionId} is the id in
        // timeline.actions[]. Everything here is the manager's own record: the engine's
        // outcome and what it decides next are never changed. Every response is the
        // updated action, so the panel and the dot can redraw from it.
        //
        // 400 invalid id/body or transition; 401 no user in token; 403 not the caller's
        // team; 404 no such action; 409 changed by someone else since it was read.

        // A team's actions over any range — the same items as timeline.actions[].
        //
        //   GET /api/team-health/actions?teamId=307
        //   GET /api/team-health/actions?teamId=307&from=2026-09-01&to=2026-09-30
        //
        // from / to are optional ("YYYY-MM-DD" or "DD-MM-YYYY"), both inclusive, on the
        // day the action was taken up ("I'll do it"). Without them, every action in flight
        // the team has had. A suggested action nobody took up is not listed.
        [HttpGet("actions")]
        public Task<IActionResult> GetActions([FromQuery] string? teamId, [FromQuery] string? from, [FromQuery] string? to) =>
            Run((userId, ct) => _actions.ListAsync(teamId, from, to, userId, ct), ResponseMessages.Success.ActionsFetched);

        //   GET /api/team-health/actions/57
        [HttpGet("actions/{actionId}")]
        public Task<IActionResult> GetAction(string actionId) =>
            Run((userId, ct) => _actions.GetAsync(actionId, userId, ct), ResponseMessages.Success.ActionFetched);

        //   PATCH /api/team-health/actions/57
        //   { "text": "Ask two people ...", "dueDate": "2026-10-20", "version": 3 }
        // Only the fields sent change; "dueDate": "" clears it; version is optional.
        [HttpPatch("actions/{actionId}")]
        public Task<IActionResult> UpdateAction(string actionId, [FromBody] UpdateTeamActionDto? dto) =>
            Run((userId, ct) => _actions.UpdateAsync(actionId, dto, userId, ct), ResponseMessages.Success.ActionUpdated);

        //   PATCH /api/team-health/actions/57/status
        //   { "status": "CLOSED" }  mark as closed     { "status": "IN_FLIGHT" }  reopen
        // CLOSED here is the timeline's "I did this": on the move's current rung it also records
        // the engine confirmation, so This Week's Move shows it as done.
        [HttpPatch("actions/{actionId}/status")]
        public Task<IActionResult> UpdateActionStatus(string actionId, [FromBody] UpdateTeamActionStatusDto? dto) =>
            Run((userId, ct) => _actions.UpdateStatusAsync(actionId, dto, userId, ct, confirmEngineOnClose: true),
                string.Equals(dto?.Status?.Trim(), "IN_FLIGHT", StringComparison.OrdinalIgnoreCase)
                    ? ResponseMessages.Success.ActionReopened
                    : ResponseMessages.Success.ActionClosed);

        //   POST /api/team-health/actions/57/comments
        //   { "text": "Did this in Monday's stand-up." }
        [HttpPost("actions/{actionId}/comments")]
        public Task<IActionResult> AddActionComment(string actionId, [FromBody] AddTeamActionCommentDto? dto) =>
            Run((userId, ct) => _actions.AddCommentAsync(actionId, dto, userId, ct), ResponseMessages.Success.ActionCommentAdded);

        private async Task<IActionResult> Run<T>(Func<int, CancellationToken, Task<TeamMilestoneResult<T>>> call, string successMessage)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (userId == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                var result = await call(userId.Value, HttpContext.RequestAborted);
                if (!result.IsSuccess)
                    return Failure(result.Error, result.Message);

                return Ok(ApiResponseHandler.Success(result.Data, successMessage));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        private IActionResult Failure(TeamMilestoneError error, string message)
        {
            var body = ApiResponseHandler.Fail<string>(message);
            return error switch
            {
                TeamMilestoneError.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
                TeamMilestoneError.NotFound => NotFound(body),
                TeamMilestoneError.Conflict => Conflict(body),
                _ => BadRequest(body)
            };
        }
    }
}
