using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace CloverBackoffice.API.Controllers
{
    /// <summary>
    /// The RRIDA team setup screen: create or edit a team together with everything the
    /// five steps collect — name and size, team type, the manager's context, and the
    /// team's goals.
    ///
    /// Separate from <c>api/teamhierarchy/upsert-team</c> rather than bolted onto it.
    /// That endpoint stores a business unit and is used by the back office team screens;
    /// this one stores a business unit AND its setup and goals as a single transaction,
    /// which is what the wizard needs when it saves at the end. The unit half behaves
    /// identically — same generated codes, same placeholder contact, same rule that a
    /// new unit never consumes a pooled license.
    ///
    /// Tenant isolation is not hand-rolled here: the request DTO carries
    /// <c>ProfileId</c> and <c>UnitId</c>, and CompanyTenantGuardFilter refuses a caller
    /// reaching into a company or unit that is not theirs before the action runs.
    /// </summary>
    [Route("api/team-setup")]
    [ApiController]
    [Authorize]
    public class TeamSetupController : ControllerBase
    {
        private readonly ITeamSetupService _teamSetupService;
        private readonly ITokenReaderService _tokenReader;
        private readonly ISystemNotificationService _notificationService;

        public TeamSetupController(
            ITeamSetupService teamSetupService,
            ITokenReaderService tokenReader,
            ISystemNotificationService notificationService)
        {
            _teamSetupService = teamSetupService;
            _tokenReader = tokenReader;
            _notificationService = notificationService;
        }

        private string GetClientIpAddress()
        {
            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        /// <summary>
        /// Sets a team up, or edits one that is already set up.
        ///
        /// <c>unitId</c> 0 creates the team; anything else edits the team with that id,
        /// the same convention <c>upsert-team</c> uses. The unit, its setup row, its goals
        /// and the goal audit rows are written in one transaction — a failure anywhere
        /// leaves nothing behind.
        ///
        /// <c>goals</c> is the COMPLETE set of goals for the team, two to five of them.
        /// A goal stored before and absent from the list is removed and audited as a
        /// removal; a goal sent with its <c>teamGoalId</c> is edited in place, which keeps
        /// its history on one thread instead of reading as a delete followed by an add.
        ///
        /// <c>teamType</c> takes "Interdependent" or "IndependentContributors" (the setup
        /// screen's two choices), or the numeric enum value.
        /// </summary>
        [HttpPost("upsert-team-setup")]
        public async Task<IActionResult> UpsertTeamSetup([FromBody] UpsertTeamSetupDto dto)
        {
            try
            {
                if (dto == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.RequestBodyEmpty));
                }

                // Recorded as the author on the setup row, every goal and every audit row.
                // A token without a user id still writes the setup; those columns are
                // nullable and the audit rows carry the same null.
                var userId = _tokenReader.GetUserIdFromToken();

                var result = await _teamSetupService.UpsertTeamSetupAsync(dto, userId);

                if (!result.IsSuccess)
                {
                    // Validation failures come back as a failed response, not a 500 — the
                    // screen shows the reason against the step the manager is on.
                    return Ok(ApiResponseHandler.Fail<string>(result.ErrorMessage ?? "Failed to save the team setup."));
                }

                await NotifyAsync(result, dto, userId);

                return Ok(ApiResponseHandler.Success(
                    result.TeamSetup,
                    result.IsNewTeam
                        ? ResponseMessages.Success.TeamSetupCreated
                        : ResponseMessages.Success.TeamSetupUpdated));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// Everything the setup screen needs for one team: the unit (name, size, parent,
        /// invite code), its team type and context, and its live goals in display order.
        ///
        /// A team that exists but has never been through setup is a SUCCESS carrying the
        /// unit, an empty goal list and <c>isSetupComplete: false</c>. The screen needs to
        /// tell "not set up yet" apart from "no such team", which is a failure.
        /// </summary>
        [HttpGet("team-setup-by-id")]
        public async Task<IActionResult> GetTeamSetupById([FromQuery] long unitId)
        {
            try
            {
                if (unitId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>("UnitId is required."));
                }

                var result = await _teamSetupService.GetTeamSetupByUnitIdAsync(unitId);

                if (!result.IsSuccess)
                {
                    return Ok(ApiResponseHandler.Fail<string>(result.ErrorMessage ?? ResponseMessages.Errors.TeamNotFound));
                }

                return Ok(ApiResponseHandler.Success(result.TeamSetup, ResponseMessages.Success.TeamSetupFound));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// "Edit attributes" on the dashboard: team name, team size and team type
        /// (Interdependent / IndependentContributors) of an EXISTING, set-up team.
        /// The context and the goals are not touched. Load the form with
        /// <c>team-setup-by-id</c>; the response is the same record shape.
        /// </summary>
        [HttpPut("team-attributes")]
        public Task<IActionResult> UpdateTeamAttributes([FromBody] UpdateTeamAttributesDto dto) =>
            SaveSectionAsync(dto, userId => _teamSetupService.UpdateTeamAttributesAsync(dto, userId),
                ResponseMessages.Success.TeamAttributesUpdated);

        /// <summary>
        /// "Edit team goals" on the dashboard. <c>goals</c> is the complete list after the
        /// edit (two to five): <c>teamGoalId</c> 0 adds, an existing id edits, a stored goal
        /// left out is removed. The team's name, size, type and context are not touched.
        /// Load the form with <c>team-setup-by-id</c>; the response is the same record shape.
        /// </summary>
        [HttpPut("team-goals")]
        public Task<IActionResult> UpdateTeamGoals([FromBody] UpdateTeamGoalsDto dto) =>
            SaveSectionAsync(dto, userId => _teamSetupService.UpdateTeamGoalsAsync(dto, userId),
                ResponseMessages.Success.TeamGoalsUpdated);

        private async Task<IActionResult> SaveSectionAsync(
            object? dto, Func<int?, Task<UpsertTeamSetupResultDto>> save, string successMessage)
        {
            try
            {
                if (dto == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.RequestBodyEmpty));
                }

                var result = await save(_tokenReader.GetUserIdFromToken());

                if (!result.IsSuccess)
                {
                    return Ok(ApiResponseHandler.Fail<string>(result.ErrorMessage ?? ResponseMessages.Errors.ExceptionMsg));
                }

                return Ok(ApiResponseHandler.Success(result.TeamSetup, successMessage));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// The same notifications upsert-team raises, so a team set up through the wizard
        /// is announced exactly as one created from the back office screen.
        ///
        /// Best-effort: the setup is already committed by the time this runs, so a
        /// notification failure must not turn a stored setup into an error for the caller.
        /// </summary>
        private async Task NotifyAsync(UpsertTeamSetupResultDto result, UpsertTeamSetupDto dto, int? userId)
        {
            try
            {
                var unitId = result.TeamSetup?.UnitId ?? dto.UnitId;

                var notificationDto = new CreateSystemNotificationDto
                {
                    Title = result.IsNewTeam ? "Team Created" : "Team Updated",
                    Message = result.IsNewTeam
                        ? $"Team '{dto.TeamName}' has been set up successfully."
                        : $"Team '{dto.TeamName}' setup has been updated successfully.",
                    NotificationType = result.IsNewTeam ? "TeamCreated" : "TeamUpdated",
                    IpAddress = GetClientIpAddress(),
                    NotificationUrl = $"/teams/{unitId}"
                };

                await _notificationService.CreateNotificationForAdminsAsync(notificationDto);

                if (userId.HasValue)
                {
                    await _notificationService.CreateNotificationForCompanyAdminsAsync(userId.Value, notificationDto);
                }

                // New team: the parent's managers are the ones who need to know. Edit: the
                // team's own managers do.
                var notifyUnitId = result.IsNewTeam && dto.ParentId is > 0
                    ? (int)dto.ParentId.Value
                    : (int)unitId;

                await _notificationService.CreateNotificationForTeamManagersAsync(notifyUnitId, notificationDto, userId);
            }
            catch (Exception)
            {
                // Swallowed by design. The setup is stored; announcing it is secondary.
            }
        }
    }
}
