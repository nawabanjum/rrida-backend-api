using CloverBackoffice.API.Filters;
using CloverBackoffice.Components;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace CloverBackoffice.API.Controllers
{
    /// <summary>
    /// Creating a Team Manager who must have a team, and routing them after they
    /// accept the invitation.
    ///
    /// Two things decide where the manager lands, and they are decided
    /// differently. The COMPANY is read off the manager's email domain — "abc"
    /// for manager@abc.com, manager@abc.co.uk and manager@abc.co.us alike — and
    /// created only when that organisation has never been onboarded. The TEAM is
    /// always chosen by the admin, and choosing one is mandatory.
    ///
    /// Separate from <c>api/company/upsert-user</c> rather than bolted onto it.
    /// That endpoint creates users of every role and is shared by the user list,
    /// the partner screens and the mobile registration flows; making a team
    /// mandatory inside it would have changed behaviour for all of them. Nothing
    /// here alters an existing endpoint — this controller composes the same
    /// services those endpoints already use.
    /// </summary>
    [Route("api/manager-onboarding")]
    [ApiController]
    [Authorize]
    public class ManagerOnboardingController : ControllerBase
    {
        private readonly IManagerOnboardingService _managerOnboarding;
        private readonly ITokenReaderService _tokenReader;
        private readonly IAccountRegistrationService _accountRegistration;
        private readonly ISystemNotificationService _notificationService;
        private readonly ITeamOnboardingNotificationService _onboardingNotification;
        private readonly ILogger<ManagerOnboardingController> _logger;

        public ManagerOnboardingController(
            IManagerOnboardingService managerOnboarding,
            ITokenReaderService tokenReader,
            IAccountRegistrationService accountRegistration,
            ISystemNotificationService notificationService,
            ITeamOnboardingNotificationService onboardingNotification,
            ILogger<ManagerOnboardingController> logger)
        {
            _managerOnboarding = managerOnboarding;
            _tokenReader = tokenReader;
            _accountRegistration = accountRegistration;
            _notificationService = notificationService;
            _onboardingNotification = onboardingNotification;
            _logger = logger;
        }

        private string GetClientIpAddress()
        {
            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        /// <summary>
        /// Who is allowed to onboard a manager.
        ///
        /// A valid token alone is not enough. Both endpoints here can bring a
        /// profile into existence and place a manager inside somebody else's
        /// tenant, so they are limited to the one admin role the product still
        /// has: Profile Admin.
        ///
        /// Managers are refused. They have no business creating managers, and
        /// before this they could.
        ///
        /// Returns the caller when they are permitted, null when they are not —
        /// the caller is needed anyway, so this resolves it once.
        /// </summary>
        private async Task<User?> GetPermittedActorAsync()
        {
            var user = await _tokenReader.GetCurrentUserAsync();
            if (user == null)
                return null;

            return user.UserGroupId is (int)UserGroups.RridaAdmin
                                     or (int)UserGroups.ProfileAdmin
                ? user
                : null;
        }

        /// <summary>
        /// Which company a manager's email address puts them in, and which teams
        /// they could therefore be given.
        ///
        /// The manager-creation screen needs this before it can ask for a team:
        /// the company comes from the address, so the selectable teams are that
        /// company's, and until the address is typed the screen does not know
        /// which company it is filling in for.
        ///
        /// <c>companyExists: false</c> means this manager would be the first on
        /// their domain — there is nothing to select and the team must be created
        /// with them.
        ///
        /// Read-only. Nothing is created here and calling it commits to nothing.
        /// </summary>
        // Tenant guard skipped: the company this resolves is named by the
        // MANAGER'S address, not by the caller, so it is by design one the caller
        // may not belong to. Placement is authorised by the same rule the create
        // call uses.
        [SkipTenantGuard]
        [HttpPost("resolve-organization")]
        public async Task<IActionResult> ResolveOrganization([FromBody] ResolveManagerOrganizationRequestDto dto)
        {
            try
            {
                if (dto == null || string.IsNullOrWhiteSpace(dto.Email))
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.RequestBodyEmpty));
                }

                var actor = await GetPermittedActorAsync();
                if (actor == null)
                {
                    return StatusCode(StatusCodes.Status403Forbidden,
                        ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ManagerCreateNotPermitted));
                }

                var resolved = await _managerOnboarding.ResolveOrganizationAsync(dto.Email, actor);
                if (resolved == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ManagerEmailDomainUnresolved));
                }

                return Ok(ApiResponseHandler.Success(resolved, ResponseMessages.Success.RecordFound));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// Creates a new Team Manager together with the team they will manage, and
        /// sends them their invitation.
        ///
        /// The company is not asked for — it is the one the manager's email domain
        /// names, created here when that organisation has never been onboarded and
        /// REUSED when it has. A second manager on a domain never mints a second
        /// company.
        ///
        /// The team is asked for, and is mandatory. The admin supplies exactly one
        /// of two things: <c>unitId</c>, an existing team of that same company, or
        /// <c>createNewTeam: true</c>, a team created here. Neither is rejected —
        /// a manager is never created without a team — and both is rejected too,
        /// because the two are alternatives and silently preferring one would hide
        /// a client bug. Nothing is written when either check fails, so no company,
        /// no team, no user and no invitation.
        ///
        /// A team created here gets a name and nothing else — size, type, context
        /// and goals belong to team setup, which the Company Admin does
        /// separately. The name is generated from the manager's own name; the
        /// request has no field for it.
        ///
        /// Assigning a team that already has a manager REPLACES that manager. A
        /// team has one manager, so the incumbent is unassigned and sent the
        /// standard "you have been unassigned" message, so they know they have
        ///
        /// stopped receiving the team's reports and alerts. A manager may hold
        /// several teams; a team never has more than one manager.
        /// The company, the team, the user and the assignment are one transaction.
        /// The invitation is sent only once that commits — exactly one email, and
        /// never one to a manager whose creation then failed.
        /// </summary>
        // Tenant-guarded, and it MUST be. The guard used to be skipped here on the
        // grounds that the caller never named a tenant — the profile came from the
        // manager's email domain, and the service refused any unit outside it. The
        // existing-team route now takes the profile FROM the chosen unit, which
        // makes existingUnitId a caller-named tenant reference and the skip a
        // cross-tenant hole: a Profile Admin could name another profile's team and
        // put their own manager on it, displacing the incumbent.
        //
        // With the guard active, existingUnitId (matched by its "UnitId" suffix) is
        // checked against the caller before the action runs. The new-team route
        // carries no unit or profile id at all, so the guard finds nothing to check
        // and lets it through exactly as before.
        [HttpPost("create-manager")]
        public async Task<IActionResult> CreateManager([FromBody] CreateManagerWithTeamRequestDto dto)
        {
            try
            {
                if (dto == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.RequestBodyEmpty));
                }

                var actor = await GetPermittedActorAsync();
                if (actor == null)
                {
                    return StatusCode(StatusCodes.Status403Forbidden,
                        ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ManagerCreateNotPermitted));
                }

                // Always from the token, never from the body.
                dto.CreatedBy = actor.UserId;

                var outcome = await _managerOnboarding.CreateManagerWithTeamAsync(dto);

                if (!outcome.IsSuccess || outcome.Data == null)
                {
                    // Validation and team failures come back as a failed response,
                    // not a 500 — the screen shows the reason against the field the
                    // admin has to fix. Nothing was written.
                    return Ok(ApiResponseHandler.Fail<string>(
                        outcome.ErrorMessage ?? ResponseMessages.Errors.ManagerCreateFailed));
                }

                var data = outcome.Data;

                // The single invitation, sent only now that the manager and their
                // team are committed. It carries the link that takes them to Set
                // Your Password.
                //
                // Best-effort: a mail provider outage must not report a created
                // manager as a failure, so the outcome is reported on the response
                // instead and the admin can re-send from the user list.
                try
                {
                    // Goes through the service so the link is recorded as single-use.
                    data.InvitationSent = await _accountRegistration.SendInvitationEmailAsync(
                        data.Email, data.UserId, data.CompanyName);
                }
                catch (Exception ex)
                {
                    data.InvitationSent = false;
                    _logger.LogError(ex,
                        "Manager {UserId} was created on team {UnitId} but the invitation email failed to send.",
                        data.UserId, data.UnitId);
                }

                // Taking a team over means somebody else lost it.
                if (data.ReplacedManagerUserId.HasValue)
                {
                    try
                    {
                        await _onboardingNotification.NotifyManagerUnassignedAsync(
                            data.ReplacedManagerUserId.Value, (uint)data.UnitId);
                    }
                    catch (Exception)
                    {
                        // Swallowed by design; the service logs its own failures.
                    }
                }

                await NotifyAsync(data, actor.UserId);

                var message = data.InvitationSent
                    ? string.Format(ResponseMessages.Success.ManagerCreatedWithTeam, data.TeamName)
                    : string.Format(ResponseMessages.Success.ManagerCreatedInviteFailed, data.TeamName);

                return Ok(ApiResponseHandler.Success(data, message));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// Where to send a user immediately after they set their password from an
        /// invitation link.
        ///
        /// A Team Manager is created with a team from the very beginning, so there
        /// is never a team setup step to route them through: they go straight to
        /// their team's Team Health page, and the response names the team so the
        /// client lands on the right one — the team they were assigned, or the one
        /// created with them.
        ///
        /// Anonymous on purpose — the caller has just set a password and has no
        /// token yet. It reveals nothing a holder of the invitation link does not
        /// already have: the parameter is the same encrypted blob the link carried,
        /// and an unreadable one returns the default landing rather than an error.
        ///
        /// Separate from <c>reset-password</c> so that endpoint keeps its current
        /// contract exactly.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("post-password-landing")]
        public async Task<IActionResult> PostPasswordLanding([FromBody] PostPasswordLandingRequestDto dto)
        {
            try
            {
                if (dto == null)
                {
                    return Ok(ApiResponseHandler.Success<PostPasswordLandingDto?>(
                        null, ResponseMessages.Success.RecordFound));
                }

                // Setting the password is the whole point of the click, so when one
                // is supplied it happens FIRST and a failure stops here — an expired
                // or already-used invitation must not read as a successful landing.
                // Delegated to the reset-password service rather than reimplemented,
                // so expiry, single-use and session invalidation stay in one place.
                if (!string.IsNullOrWhiteSpace(dto.NewPassword))
                {
                    var reset = await _accountRegistration.ResetPasswordAsync(new ResetPasswordDto
                    {
                        Param = dto.Param,
                        NewPassword = dto.NewPassword
                    });

                    if (!reset.IsSuccess)
                        return Ok(ApiResponseHandler.Fail<string>(reset.Message));
                }

                var landing = await _managerOnboarding.GetPostPasswordLandingAsync(dto.Param);

                // A null landing is a success carrying nothing, not a failure: the
                // client falls back to its own default route. The password is
                // already set by this point and nothing here should read as that
                // having gone wrong.
                return Ok(ApiResponseHandler.Success<PostPasswordLandingDto?>(
                    landing, ResponseMessages.Success.RecordFound));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// The same notifications the team screens raise, so a team created during
        /// manager setup is announced exactly as one created from
        /// <c>upsert-team</c>, and the assignment exactly as
        /// <c>assign-user-to-team</c>.
        ///
        /// A manager joining a company and team that already existed announces the
        /// assignment only — there is nothing new to report.
        ///
        /// Best-effort: the manager and the team are already committed by the time
        /// this runs, so a notification failure must not turn a completed creation
        /// into an error for the caller.
        /// </summary>
        private async Task NotifyAsync(CreateManagerWithTeamResponseDto data, int actorUserId)
        {
            try
            {
                var ip = GetClientIpAddress();

                if (data.TeamWasCreated)
                {
                    var teamCreated = new CreateSystemNotificationDto
                    {
                        Title = "Team Created",
                        Message = $"Team '{data.TeamName}' has been created successfully.",
                        NotificationType = "TeamCreated",
                        IpAddress = ip,
                        NotificationUrl = $"/teams/{data.UnitId}"
                    };

                    await _notificationService.CreateNotificationForAdminsAsync(teamCreated);
                    await _notificationService.CreateNotificationForCompanyAdminsAsync(actorUserId, teamCreated);
                }

                var assigned = new CreateSystemNotificationDto
                {
                    Title = "User Assigned to Team",
                    Message = $"User (ID: {data.UserId}) has been assigned to team (ID: {data.UnitId}).",
                    NotificationType = "UserAssignedToTeam",
                    IpAddress = ip,
                    NotificationUrl = $"/teams/{data.UnitId}"
                };

                await _notificationService.CreateNotificationForAdminsAsync(assigned);
                await _notificationService.CreateNotificationForCompanyAdminsAsync(actorUserId, assigned);
                await _notificationService.CreateNotificationForTeamManagersAsync((int)data.UnitId, assigned, actorUserId);
            }
            catch (Exception)
            {
                // Swallowed by design. The manager and the team are stored;
                // announcing them is secondary.
            }
        }
    }
}
