using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    /// <summary>
    /// One place to track a manager's actions: the ones they create themselves (source MANUAL,
    /// id "12") and the engine's recommendations they took up with "I'll do it" (source ENGINE,
    /// id "engine-57"). Engine actions are read from and changed through the existing Team
    /// Health action service; api/team-health/actions, api/rrida/manager/* and the report Action
    /// Hub (api/actionhub) are unchanged. Eran is told about a team's open manual actions in its
    /// daily messages, chat and suggested questions (it already knows the engine's).
    ///
    /// 400 invalid field; 401 no user in token; 403 outside the caller's scope;
    /// 404 no such action or team; 500 unexpected failure.
    /// </summary>
    [Route("api/actions")]
    [ApiController]
    [Authorize]
    public class ManagerActionsController : ControllerBase
    {
        private readonly IManagerActionService _actions;
        private readonly ITokenReaderService _tokenReader;

        public ManagerActionsController(IManagerActionService actions, ITokenReaderService tokenReader)
        {
            _actions = actions;
            _tokenReader = tokenReader;
        }

        //   POST /api/actions
        //   { "teamId": 307,                       // required
        //     "title": "Run a retro on the release",                     // required
        //     "description": "30 minutes, whole team, before Friday.",   // optional
        //     "startDate": "2026-11-18",           // required, YYYY-MM-DD or DD-MM-YYYY, any day
        //     "dueDate": "2026-11-20",             // optional, YYYY-MM-DD or DD-MM-YYYY, not before startDate
        //     "engineActionId": 57,                // optional, an id from GET api/actions/eran-suggestions
        //     "suggestionChoice": "SUGGESTION" }   // optional: SUGGESTION (with the id), NONE or NONE_OF_THESE
        //
        // Source is always MANUAL and status OPEN, linked to a suggestion or not. createdBy is
        // the logged-in user. Neither suggestion field sent: no link (engineActionId null).
        [HttpPost]
        public Task<IActionResult> Create([FromBody] CreateManagerActionDto? dto) =>
            Run((userId, ct) => _actions.CreateAsync(dto, userId, ct), ResponseMessages.Success.ManagerActionCreated);

        //   GET /api/actions/eran-suggestions?teamId=307
        //
        // The Create Action dropdown: the team's 5 latest suggestions from ERAN (the engine),
        // newest first, taken up or not, each with engineActionId and title, plus otherOptions
        // None (NONE) and None of these (NONE_OF_THESE). Empty suggestions for a team not on
        // the engine. Same access as creating an action on the team.
        [HttpGet("eran-suggestions")]
        public Task<IActionResult> EranSuggestions([FromQuery] uint? teamId) =>
            Run((userId, ct) => _actions.ListEranSuggestionsAsync(teamId, userId, ct), ResponseMessages.Success.EranSuggestionsFetched);

        //   GET /api/actions?teamId=307
        //   GET /api/actions?profileId=151&createdBy=350&status=OPEN
        //
        // Filters (all optional): profileId, teamId, createdBy, status, source (MANUAL / ENGINE),
        // take (default 100, max 500). A RRIDA Admin sends profileId or teamId. Open actions
        // first, then by due date. Engine actions are listed once taken up ("I'll do it"); their
        // createdBy is the manager who took them up.
        [HttpGet]
        public Task<IActionResult> List([FromQuery] ManagerActionQueryDto query) =>
            Run((userId, ct) => _actions.ListAsync(query, userId, ct), ResponseMessages.Success.ManagerActionsFetched);

        //   GET /api/actions/12           a manager's action
        //   GET /api/actions/engine-57    an engine action
        [HttpGet("{actionId}")]
        public Task<IActionResult> Get(string actionId) =>
            Run((userId, ct) => _actions.GetAsync(actionId, userId, ct), ResponseMessages.Success.ManagerActionFetched);

        //   PATCH /api/actions/12
        //   { "status": "DONE" }   or any of title, description, dueDate
        // Only the fields sent change; "dueDate": "" and "description": "" clear them.
        // Manual action: engineActionId / suggestionChoice re-pick the ERAN suggestion as on
        // create; "suggestionChoice": "" clears it.
        // Engine action (engine-57): title, dueDate and status only. DONE / CANCELLED close it
        // and OPEN / IN_PROGRESS reopen it, as the manager's record; the engine cycle and its
        // outcome are never changed ("I did this" stays api/rrida/manager/confirm).
        [HttpPatch("{actionId}")]
        public Task<IActionResult> Update(string actionId, [FromBody] UpdateManagerActionDto? dto) =>
            Run((userId, ct) => _actions.UpdateAsync(actionId, dto, userId, ct), ResponseMessages.Success.ManagerActionUpdated);

        //   POST /api/actions/12/comments          a manager's action, at any status
        //   POST /api/actions/engine-57/comments   an engine action (Team Health action comments)
        //   { "text": "Booked for Thursday." }     // required, at most 500 characters
        //
        // Returns the action with all its comments, oldest first. A manual action needs no
        // "I'll do it": comment, then Mark Done (PATCH { "status": "DONE" }).
        [HttpPost("{actionId}/comments")]
        public Task<IActionResult> AddComment(string actionId, [FromBody] AddManagerActionCommentDto? dto) =>
            Run((userId, ct) => _actions.AddCommentAsync(actionId, dto, userId, ct), ResponseMessages.Success.ManagerActionCommentAdded);

        private async Task<IActionResult> Run<T>(Func<int, CancellationToken, Task<TeamMilestoneResult<T>>> call, string successMessage)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (userId == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                var result = await call(userId.Value, HttpContext.RequestAborted);
                if (!result.IsSuccess)
                {
                    var body = ApiResponseHandler.Fail<string>(result.Message);
                    return result.Error switch
                    {
                        TeamMilestoneError.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
                        TeamMilestoneError.NotFound => NotFound(body),
                        _ => BadRequest(body)
                    };
                }

                return Ok(ApiResponseHandler.Success(result.Data, successMessage));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }
}
