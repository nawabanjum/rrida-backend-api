using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace CloverBackoffice.API.Controllers
{
    [Route("api/teamhierarchy")]
    [ApiController]
    [Authorize]
    public class TeamHierarchyController : ControllerBase
    {
        private readonly ICompanyTeamService _service;
        private readonly ICompanyTeamRepository _repo;
        private readonly ITokenReaderService _tokenReader;
        private readonly ApplicationDbContext _context;
        private readonly IAccountRegistrationService _accountRegistrationService;
        private readonly ISystemNotificationService _notificationService;
        private readonly ICompanyLicenseService _licenseService;
        private readonly ITeamOnboardingNotificationService _onboardingNotification;
        private readonly ITeamStructureService _teamStructure;
        private readonly IUserRoleAssignmentService _userRoleAssignment;
        public TeamHierarchyController(IAccountRegistrationService accountRegistrationService, ICompanyTeamService service, ITokenReaderService tokenReader, ApplicationDbContext context, IAccountRegistrationService registrationService, ICompanyTeamRepository repo, ISystemNotificationService notificationService, ICompanyLicenseService licenseService, ITeamOnboardingNotificationService onboardingNotification, ITeamStructureService teamStructure, IUserRoleAssignmentService userRoleAssignment)
        {
            _teamStructure = teamStructure;
            _userRoleAssignment = userRoleAssignment;
            _onboardingNotification = onboardingNotification;
            _service = service;
            _repo = repo;
            _tokenReader = tokenReader;
            _context = context;
            _accountRegistrationService = accountRegistrationService;
            _notificationService = notificationService;
            _licenseService = licenseService;
        }

        private string GetClientIpAddress()
        {
            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        [HttpPost("upsert-team")]
        public async Task<IActionResult> UpsertBusinessUnit([FromBody] UpsertBusinessUnitDto dto)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();

                // If userId is not found in token, check the AuthTokens table using the raw token
                if (userId.HasValue)
                {
                    dto.CreatedBy = userId.Value;
                }

                bool isNewTeam = dto.Id == 0;

                // Team size must be supplied when the team is created; on edit it is
                // optional and only applied when a value is sent.
                if (isNewTeam && (!dto.TeamSize.HasValue || dto.TeamSize.Value <= 0))
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.TeamSizeRequired));
                }

                if (dto.TeamSize.HasValue && dto.TeamSize.Value <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.TeamSizeInvalid));
                }

                var nameExits = await _accountRegistrationService.BussinessNameExistsAsync(dto.TeamName, dto.ProfileId);
                if (nameExits != null && nameExits.Id != dto.Id)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.BussinessNameUnique));

                var result = await _service.UpsertBusinessUnitAsync(dto);

                // Id still 0 after a create means the service refused it — the only
                // reason that is not one of the three strings below is "no free
                // licence", which must reach the admin as a failure rather than as a
                // success carrying a team that was never written.
                if (result.Id <= 0
                    || result.message.Contains("Invalid ProfileId") || result.message.Contains("Invalid ParentId") || result.message.Contains("not found"))
                {
                    return Ok(ApiResponseHandler.Fail<string>(result.message));
                }

                var notificationDto = new CreateSystemNotificationDto
                {
                    Title = isNewTeam ? "Team Created" : "Team Updated",
                    Message = isNewTeam
                        ? $"Team '{dto.TeamName}' has been created successfully."
                        : $"Team '{dto.TeamName}' has been updated successfully.",
                    NotificationType = isNewTeam ? "TeamCreated" : "TeamUpdated",
                    IpAddress = GetClientIpAddress(),
                    NotificationUrl = $"/teams/{result.Id}"
                };
                await _notificationService.CreateNotificationForAdminsAsync(notificationDto);
                await _notificationService.CreateNotificationForCompanyAdminsAsync(userId.Value, notificationDto);

                // Notify team managers in the hierarchy
                // For new teams: notify managers of the parent team
                // For updates: notify managers of the team itself
                var notifyUnitId = isNewTeam && dto.ParentId.HasValue ? dto.ParentId.Value : result.Id;
                await _notificationService.CreateNotificationForTeamManagersAsync(notifyUnitId, notificationDto, userId);

                return Ok(ApiResponseHandler.Success(result, result.message));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpDelete("team/{id}")]
        public async Task<IActionResult> DeleteTeamById(uint id)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();

                // Deleting a team also frees any license it holds (purchased
                // assignment unassigned, complimentary grant revoked) so the
                // license can be reassigned. actorUserId drives the audit trail.
                var result = await _service.DeleteAsync(id, userId);
                if (!result)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.TeamNotFound));
                }

                var notificationDto = new CreateSystemNotificationDto
                {
                    Title = "Team Deleted",
                    Message = $"Team (ID: {id}) has been deleted.",
                    NotificationType = "TeamDeleted",
                    IpAddress = GetClientIpAddress(),
                    NotificationUrl = "/teams"
                };
                await _notificationService.CreateNotificationForAdminsAsync(notificationDto);
                if (userId.HasValue)
                    await _notificationService.CreateNotificationForCompanyAdminsAsync(userId.Value, notificationDto);
                await _notificationService.CreateNotificationForTeamManagersAsync((int)id, notificationDto, userId);

                return Ok(ApiResponseHandler.Success(true, ResponseMessages.Success.TeamDelSuccess));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPost("assign-user-to-team")]
        public async Task<IActionResult> AssignUserToTeam([FromBody] UserTeamDto dto)
        {

            try
            {
                if (dto == null || dto.UserId <= 0 || dto.UnitId <= 0)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidUserTeam));

                int? createdBy = _tokenReader.GetUserIdFromToken();
                if (createdBy == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                var result = await _service.AssignUserToTeamAsync(dto, createdBy.Value);

                if (result == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UserAssignteamFail));
                }

                var responseContent = new
                {
                    userId = result.UserId,
                    teamId = result.UnitId,
                    isReadOnly = result.IsReadOnly ?? false
                };

                await NotifyUserAssignedToTeamAsync(dto.UserId, dto.UnitId, createdBy.Value);

                return Ok(ApiResponseHandler.Success(responseContent, ResponseMessages.Success.UserAssignteamSuccess));
            }
            catch (InvalidOperationException ex)
            {
                // Validation failure with a reason, e.g. the manager team limit.
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        private async Task NotifyUserAssignedToTeamAsync(int userId, uint unitId, int actorUserId)
        {
            var notificationDto = new CreateSystemNotificationDto
            {
                Title = "User Assigned to Team",
                Message = $"User (ID: {userId}) has been assigned to team (ID: {unitId}).",
                NotificationType = "UserAssignedToTeam",
                IpAddress = GetClientIpAddress(),
                NotificationUrl = $"/teams/{unitId}"
            };
            await _notificationService.CreateNotificationForAdminsAsync(notificationDto);
            await _notificationService.CreateNotificationForCompanyAdminsAsync(actorUserId, notificationDto);
            await _notificationService.CreateNotificationForTeamManagersAsync((int)unitId, notificationDto, actorUserId);
        }

        private async Task<bool> IsProfileOrRridaAdminAsync(int userId)
        {
            var roles = await _userRoleAssignment.GetRoleIdsAsync(userId);
            return roles.Contains((int)UserGroups.ProfileAdmin) || roles.Contains((int)UserGroups.RridaAdmin);
        }

        // Team Structure page: managers, the teams each one runs, who they report
        // to, teams with no manager and the banner alerts.
        //
        //   GET /api/teamhierarchy/team-structure?profileId=56
        [HttpGet("team-structure")]
        public async Task<IActionResult> GetTeamStructure([Required][FromQuery] int profileId)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (userId == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                if (!await IsProfileOrRridaAdminAsync(userId.Value))
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.TeamStructureAccessDenied));

                var result = await _teamStructure.GetTeamStructureAsync(profileId);
                if (result == null)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidCompanyUnit));

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.TeamStructureSuccess));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        // Drag a team onto a Team Manager: assigns a team with no manager, or
        // moves it from its current manager. Returns the updated structure.
        //
        //   POST /api/teamhierarchy/team-structure/assign-team
        //   { "profileId": 56, "unitId": 211, "userId": 122 }
        [HttpPost("team-structure/assign-team")]
        public async Task<IActionResult> AssignTeamToManager([FromBody] AssignTeamManagerDto dto)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (userId == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                if (!await IsProfileOrRridaAdminAsync(userId.Value))
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.TeamStructureAccessDenied));

                var (result, error) = await _teamStructure.AssignTeamAsync(dto, userId.Value);
                if (result == null)
                    return Ok(ApiResponseHandler.Fail<string>(error ?? ResponseMessages.Errors.UserAssignteamFail));

                // Portfolio Manager without a Team Manager licence: nothing changed.
                // Free licence → confirm and resend with assignTeamManagerLicence:
                // true; none → say so (no redirect to the Licences tab).
                if (result.RequiresTeamManagerLicence)
                    return Ok(ApiResponseHandler.Fail(result, result.LicenceAvailable
                        ? ResponseMessages.Errors.TeamManagerLicenceAvailableConfirm
                        : ResponseMessages.Errors.TeamManagerLicenceNotAvailable));

                if (!result.Changed)
                    return Ok(ApiResponseHandler.Success(result,
                        string.Format(ResponseMessages.Success.TeamAlreadyWithManager, result.TeamName, result.ManagerName)));

                await NotifyUserAssignedToTeamAsync(result.ManagerUserId, result.UnitId, userId.Value);

                var moved = string.Format(ResponseMessages.Success.TeamMovedToManager, result.TeamName, result.ManagerName);
                return Ok(ApiResponseHandler.Success(result, result.TeamManagerLicenceAssigned
                    ? $"{ResponseMessages.Success.TeamManagerLicenceAndRoleAssigned} {moved}"
                    : moved));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        // Drag a manager onto another manager: the one dropped on becomes their
        // Portfolio Manager (role added if missing). portfolioManagerUserId null
        // or 0 moves them back under the company. Their reports and teams move
        // with them. Returns the updated structure.
        //
        //   POST /api/teamhierarchy/team-structure/move-manager
        //   { "profileId": 56, "userId": 131, "portfolioManagerUserId": 142 }
        [HttpPost("team-structure/move-manager")]
        public async Task<IActionResult> MoveManager([FromBody] MoveManagerDto dto)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (userId == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                if (!await IsProfileOrRridaAdminAsync(userId.Value))
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.TeamStructureAccessDenied));

                var (result, error) = await _teamStructure.MoveManagerAsync(dto, userId.Value);
                if (result == null)
                    return Ok(ApiResponseHandler.Fail<string>(error ?? ResponseMessages.Errors.ExceptionMsg));

                var message = string.Format(
                    result.Changed ? ResponseMessages.Success.ManagerMoved : ResponseMessages.Success.ManagerAlreadyReports,
                    result.Name, result.PortfolioManagerName);
                return Ok(ApiResponseHandler.Success(result, message));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPost("unassign-team-manager")]
        public async Task<IActionResult> UnAssignUserFromTeam([FromBody] UnAssignedUserTeamDto dto)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();

                // Validate input
                if (dto == null || dto.UserId <= 0 || dto.UnitId <= 0)
                    return Ok(ApiResponseHandler.Fail<string>(
                        ResponseMessages.Errors.InvalidUserTeam));

                // Get the latest record for the given UserId and UnitId
                // Order by CreatedDate descending, then by Id descending as fallback
                var userTeam = await _context.UserTeams
                    .Where(ut => ut.UserId == dto.UserId && ut.UnitId == dto.UnitId)
                    .OrderByDescending(ut => ut.CreatedDate)
                    .ThenByDescending(ut => ut.Id)
                    .FirstOrDefaultAsync();

                // Case 1: Record does not exist
                if (userTeam == null)
                    return Ok(ApiResponseHandler.Fail<string>(
                        ResponseMessages.Errors.InvalidUserTeam));

                // Case 2: Already unassigned
                if (userTeam.IsUnAssigned)
                    return Ok(ApiResponseHandler.Fail<string>("This team has already been unassigned.")); // TeamAlreadyUnassigned

                // Case 3: this would be the team's LAST manager. Every team must keep
                // one, so the release is refused and the admin replaces the manager
                // instead — assign-user-to-team and Add User both unassign the
                // incumbent as part of handing the team over, so the team is never
                // manager-less for a moment. A replacement still waiting to accept
                // their invitation (IsPendingActivation on an unassigned row) is not
                // a manager yet and does not count.
                var teamKeepsAManager = await _context.UserTeams
                    .AnyAsync(ut => ut.UnitId == dto.UnitId
                                    && ut.UserId != dto.UserId
                                    && !ut.IsUnAssigned);

                if (!teamKeepsAManager)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.TeamMustKeepManager));

                // Case 4: another manager remains → update IsUnAssigned = true
                userTeam.IsUnAssigned = true;
                _context.UserTeams.Update(userTeam);
                await _context.SaveChangesAsync();

                // Losing this unit's manager may leave a complimentary grant
                // without a shared manager — revoke stale ones. Best-effort: the
                // reconciliation job backstops any failure here.
                try { await _licenseService.ReconcileGrantsForUnitAsync((uint)dto.UnitId, userId); }
                catch { /* swallowed by design; safety-net job will catch it */ }

                // Tell the manager they no longer hold this team.
                //
                // This lives here rather than in CompanyTeamService because this
                // endpoint performs the unassignment inline — it does not call
                // CompanyTeamService.UnAssignUserFromTeamAsync, which currently
                // has no callers at all. Anything hooked onto that method would
                // silently never run for the real unassign path.
                try { await _onboardingNotification.NotifyManagerUnassignedAsync(dto.UserId, (uint)dto.UnitId); }
                catch { /* swallowed by design; the service logs its own failures */ }

                var notificationDto = new CreateSystemNotificationDto
                {
                    Title = "User Unassigned from Team",
                    Message = $"User (ID: {dto.UserId}) has been unassigned from team (ID: {dto.UnitId}).",
                    NotificationType = "UserUnassignedFromTeam",
                    IpAddress = GetClientIpAddress(),
                    NotificationUrl = $"/teams/{dto.UnitId}"
                };
                await _notificationService.CreateNotificationForAdminsAsync(notificationDto);
                if (userId.HasValue)
                    await _notificationService.CreateNotificationForCompanyAdminsAsync(userId.Value, notificationDto);
                await _notificationService.CreateNotificationForTeamManagersAsync((int)dto.UnitId, notificationDto, userId);

                return Ok(ApiResponseHandler.Success(
                    new { userId = dto.UserId, unitId = dto.UnitId },
                    "User has been successfully unassigned from the team.")); // UserUnAssignTeamSuccess
            }
            catch (Exception)
            {
                return StatusCode(500,
                    ApiResponseHandler.Fail<string>(
                        ResponseMessages.Errors.ExceptionMsg));
            }
        }


        [HttpGet("get-teams-structure")]
        public async Task<APIResponse<GetCompanyUnitsWithHierarchyDto>> GetCompanyTeams([Required][FromQuery] int profileId)
        {
            APIResponse<GetCompanyUnitsWithHierarchyDto> response = new APIResponse<GetCompanyUnitsWithHierarchyDto>();
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (userId == null)
                {
                    response.IsSuccess = false;
                    response.Message = "Invalid or missing token.";
                    return response;
                }

                var result = await _service.GetCompanyUnitsWithHierarchy(profileId, userId.Value);
                if (result == null)
                {
                    response.IsSuccess = false;
                    response.Message = ResponseMessages.Errors.InvalidCompanyUnit;
                    return response;
                }
                response.IsSuccess = true;
                response.Message = ResponseMessages.Success.TeamListSuccess;
                response.Content = result;
                return response;
            }
            catch (Exception ex)
            {
                response.IsSuccess = false;
                response.Message = ResponseMessages.Errors.ExceptionMsg;
                return response;
            }
        }

        [HttpGet("get-team-notifications")]
        public async Task<IActionResult> GetTeamNotifications([FromQuery][Required] int profileId)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (!userId.HasValue)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                var result = await _service.GetTeamNotificationsAsync(profileId, userId.Value);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }
}
