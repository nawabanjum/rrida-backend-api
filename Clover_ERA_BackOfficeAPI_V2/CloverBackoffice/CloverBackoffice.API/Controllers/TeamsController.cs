using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    /// <summary>
    /// Team-scoped endpoints addressed as /api/teams/… .
    ///
    /// teamId is a route value the global tenant guard does not recognise (it only
    /// knows *UnitId names), so each service checks company and role itself through
    /// ITeamHealthAccessService.
    /// </summary>
    [Route("api/teams")]
    [ApiController]
    [Authorize]
    [Produces("application/json")]
    public class TeamsController : ControllerBase
    {
        private readonly ITeamWeeklyFlagService _weeklyFlags;
        private readonly ITeamStateService _teamState;
        private readonly ITeamCalendarInviteService _calendarInvite;
        private readonly ITokenReaderService _tokenReader;

        public TeamsController(
            ITeamWeeklyFlagService weeklyFlags,
            ITeamStateService teamState,
            ITeamCalendarInviteService calendarInvite,
            ITokenReaderService tokenReader)
        {
            _weeklyFlags = weeklyFlags;
            _teamState = teamState;
            _calendarInvite = calendarInvite;
            _tokenReader = tokenReader;
        }

        // Weekly Flag card on Team Health.
        //
        //   GET /api/teams/188/weekly-flags                         every week, latest first
        //   GET /api/teams/188/weekly-flags?weekStartDate=2026-08-25 that week only
        //
        // 200 with { teamId, weeklyFlags[] } — a week with no flag is an empty list;
        // 400 bad teamId or date; 401 no user in token; 403 not the caller's team;
        // 404 no such team.
        [HttpGet("{teamId}/weekly-flags")]
        [EndpointSummary("Weekly Flags for a team")]
        [EndpointDescription("Without weekStartDate: every Weekly Flag for the team, latest week first, for the week dropdown. " +
                             "With weekStartDate (YYYY-MM-DD or DD-MM-YYYY): the flag whose week contains that date, or an empty list.")]
        [ProducesResponseType(typeof(ResponseDto<TeamWeeklyFlagsDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ResponseDto<string>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ResponseDto<string>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ResponseDto<string>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ResponseDto<string>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ResponseDto<string>), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetWeeklyFlags([FromRoute] string teamId, [FromQuery] string? weekStartDate)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (userId == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                var result = await _weeklyFlags.GetAsync(teamId, weekStartDate, userId.Value, HttpContext.RequestAborted);
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

                var message = result.Data!.WeeklyFlags.Count == 0
                    ? ResponseMessages.Success.WeeklyFlagsNone
                    : ResponseMessages.Success.WeeklyFlagsFetched;
                return Ok(ApiResponseHandler.Success(result.Data, message));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        // Team State card on Team Health.
        //
        //   GET /api/teams/188/health/state?days=60
        //
        // 200 with { teamId, teamName, state, cruise, drift, headwinds, stall, ..., history };
        // a team the engine has not resolved yet is CRUISE, all zeros, isCalibrating true.
        // days: the trend chart window (history), 30, 60 or 90; missing or anything else is 60.
        // 400 bad teamId; 401 no user in token; 403 not the caller's team; 404 no such team.
        [HttpGet("{teamId}/health/state")]
        [EndpointSummary("Team State for a team")]
        [EndpointDescription("Current state (CRUISE, DRIFT, HEADWINDS or STALL) from the team's latest resolved day, " +
                             "plus the share of resolved days in the last 30 days spent in each state (whole percentages summing to 100), " +
                             "and history: one row per calculated day in the last ?days= (30, 60 or 90; default 60) days for the trend chart.")]
        [ProducesResponseType(typeof(ResponseDto<TeamStateDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ResponseDto<string>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ResponseDto<string>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ResponseDto<string>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ResponseDto<string>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ResponseDto<string>), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetTeamState([FromRoute] string teamId, [FromQuery] string? days = null)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (userId == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                // Read as text so a bad value falls back to 60 instead of failing the request.
                int? period = int.TryParse(days?.Trim(), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
                var result = await _teamState.GetAsync(teamId, userId.Value, HttpContext.RequestAborted, period);
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

                return Ok(ApiResponseHandler.Success(result.Data, ResponseMessages.Success.TeamStateFetched));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        // "Email this to me" on the Invite Your Team modal.
        //
        //   POST /api/teams/send-calendar-invite   { "teamId": 307, "reminderMinutes": 0 }
        //
        // reminderMinutes is optional (0–10080, 0 = at the start time); omitted = 0 minutes.
        // The event is the standard "RRIDA Daily Check-in" (no questions, Free, recurring
        // Mon–Fri); the mail subject names the team. The mail carries the invitation (accept /
        // decline) and a separate editable .ics file.
        //
        // 200 with { teamId, teamName, email, invitationSent }; invitationSent is false when
        // the same request was served in the last minute, so nothing was mailed again.
        // 400 bad teamId or no valid email on the account; 401 no user in token;
        // 403 not a Team Manager of this team; 404 no such team;
        // 500 the invitation could not be generated or delivered.
        [HttpPost("send-calendar-invite")]
        [EndpointSummary("Email the team's calendar invitation to the logged-in Team Manager")]
        [EndpointDescription("Sends an .ics calendar invitation for the team to the caller's registered email address, " +
                             "taken from the token. Only a Team Manager of the team may ask.")]
        [ProducesResponseType(typeof(ResponseDto<TeamCalendarInviteResultDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ResponseDto<string>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ResponseDto<string>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ResponseDto<string>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ResponseDto<string>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ResponseDto<string>), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SendCalendarInvite([FromBody] SendTeamCalendarInviteDto request)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (userId == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                var result = await _calendarInvite.SendAsync(
                    request.TeamId, userId.Value, request.ReminderMinutes, HttpContext.RequestAborted);
                if (!result.IsSuccess)
                {
                    var body = ApiResponseHandler.Fail<string>(result.Message);
                    return result.Error switch
                    {
                        TeamMilestoneError.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
                        TeamMilestoneError.NotFound => NotFound(body),
                        TeamMilestoneError.ServerError => StatusCode(StatusCodes.Status500InternalServerError, body),
                        _ => BadRequest(body)
                    };
                }

                var message = result.Data!.InvitationSent
                    ? ResponseMessages.Success.TeamCalendarInviteSent
                    : ResponseMessages.Success.TeamCalendarInviteAlreadySent;
                return Ok(ApiResponseHandler.Success(result.Data, message));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }
}
