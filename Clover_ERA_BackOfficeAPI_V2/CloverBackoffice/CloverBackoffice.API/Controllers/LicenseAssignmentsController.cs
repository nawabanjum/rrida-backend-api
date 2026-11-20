using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace CloverBackoffice.API.Controllers
{
    // Single-endpoint license lifecycle API.
    //
    //   POST /api/license-assignments
    //
    //   Body (LicenseAssignmentRequestDto):
    //     Action    | Required fields            | Behavior
    //     ----------+----------------------------+----------------------------
    //     Assign    | ManagerId                  | Gives the licence to a Team
    //     (manager) | (CompanyLicenseId optional)| Manager; it covers their
    //               |                            | teams (max 3). Caller must
    //               |                            | manage the manager's profile.
    //               |                            | { "action": "Assign",
    //               |                            |   "companyLicenseId": 93,
    //               |                            |   "managerId": 263 }
    //     Assign    | CompanyLicenseId,          | Pins license to the unit.
    //               | BusinessUnitId             | Rejects if license is
    //               |                            | already active on another
    //               |                            | unit — use Reassign.
    //     Reassign  | CompanyLicenseId,          | Atomic from→to move. From
    //               | FromBusinessUnitId,        | must match current active
    //               | ToBusinessUnitId           | assignee, otherwise reject.
    //     Remove    | CompanyLicenseId OR        | Closes the active
    //               | BusinessUnitId             | assignment. License id
    //               |                            | wins when both are sent.
    //               |                            | Removing a complimentary
    //               |                            | beneficiary revokes just its
    //               |                            | grant.
    //     AssignComp| CompanyLicenseId,          | Grants the purchased license
    //     limentary | BusinessUnitId (Team B)    | to one extra same-manager
    //               |                            | team. Consumes no purchased
    //               |                            | slot; recorded as a
    //               |                            | ComplimentaryLicenseGrant.
    //
    // Every state change keeps the IsActive invariant inside a transaction
    // and writes an audit row — AssignedCompanyLicenseAuditLogs for purchased
    // assignments, ComplimentaryLicenseGrantAuditLogs for complimentary grants —
    // so the full history of every assignment is recoverable.
    [Route("api/license-assignments")]
    [ApiController] 
    [Authorize]
    public class LicenseAssignmentsController : ControllerBase
    {
        private readonly ICompanyLicenseService _service;
        private readonly ITokenReaderService _tokenReader; 
        private readonly ILicensedPeriodService _licensedPeriod;
        private readonly ILicenseAvailabilityService _availability;
        private readonly ISubscriptionService _subscriptionService;

        public LicenseAssignmentsController(
            ICompanyLicenseService service,
            ITokenReaderService tokenReader,
            ILicensedPeriodService licensedPeriod,
            ILicenseAvailabilityService availability,
            ISubscriptionService subscriptionService)
        {
            _service = service;
            _tokenReader = tokenReader;
            _licensedPeriod = licensedPeriod;
            _availability = availability;
            _subscriptionService = subscriptionService;
        }

        // The unit a request would put a license ON, ogive r null when it puts one
        // nowhere. Only this end of the operation is license-gated:
        //   Assign / AssignComplimentary → BusinessUnitId is the recipient.
        //   Reassign                     → ToBusinessUnitId is the recipient;
        //                                  FromBusinessUnitId is losing the
        //                                  license, so it is never checked.
        //   Remove                       → no recipient at all.
        // Keeping Remove and the from-side unchecked means a special unit that
        // already holds a license (assigned before this guard existed) can still
        // be cleaned up — the guard only stops NEW coverage landing on one.
        //   Assign by ManagerId              → no unit in the request; the
        //                                      service skips special units.
        private static uint? ResolveLicenseRecipientUnitId(LicenseAssignmentRequestDto dto) => dto.Action switch
        {
            LicenseAssignmentAction.Assign when dto.ManagerId.HasValue => null,
            LicenseAssignmentAction.Assign => dto.BusinessUnitId,
            LicenseAssignmentAction.AssignComplimentary => dto.BusinessUnitId,
            LicenseAssignmentAction.Reassign => dto.ToBusinessUnitId,
            _ => null,
        };

        // Licence details of a profile, one row per Team Manager: managerId,
        // managerName, teamsUsed, teamsLimit, plan, renewalDate, status.
        //
        //   GET /api/license-assignments?profileId=69
        //
        // profileId is mandatory: missing, 0 or non-integer → 400 Bad Request.
        [HttpGet]
        public async Task<IActionResult> GetLicenseDetails([FromQuery, Required] uint? profileId)
        {
            try
            {
                if (profileId is null or 0)
                    return BadRequest(ApiResponseHandler.Fail<string>("ProfileId is required."));

                var result = await _service.GetLicenseDetailsAsync(profileId.Value, _tokenReader.GetUserIdFromToken());
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }

        // Team Managers' usage of the manager licence team limit ("2 of 3").
        //
        //   GET /api/license-assignments/manager-team-usage?profileId=69[&userId=150]
        [HttpGet("manager-team-usage")]
        public async Task<IActionResult> GetManagerTeamUsage([FromQuery] uint profileId, [FromQuery] int? userId)
        {
            try
            {
                if (profileId == 0)
                    return Ok(ApiResponseHandler.Fail<string>("ProfileId is required."));

                var result = await _service.GetManagerTeamUsageAsync(profileId, userId, _tokenReader.GetUserIdFromToken());
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }

        // Profile licence availability, checked before Add User: total, consumed,
        // available / remaining licences, Team Managers, teams and team capacity.
        // One licence = one Team Manager (up to 3 teams); teams consume none.
        // With none available, isLicenseLimitReached is
        // true and buyLicenseCheckoutUrl is the Stripe Checkout for one more.
        //
        //   GET /api/license-assignments/availability?profileId=69
        [HttpGet("availability")]
        public async Task<IActionResult> GetLicenseAvailability([FromQuery] uint profileId)
        {
            try
            {
                if (profileId == 0)
                    return Ok(ApiResponseHandler.Fail<string>("ProfileId is required."));

                var actor = await _tokenReader.GetCurrentUserAsync();
                if (actor == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                var result = await _availability.GetAvailabilityAsync(profileId, actor);
                var message = result.IsLicenseLimitReached
                    ? ResponseMessages.Errors.LicenseLimitReached
                    : ResponseMessages.Success.OperationSuccess;
                return Ok(ApiResponseHandler.Success(result, message));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }

        // Cancels a Team Manager's licence at the end of its current period. The
        // licence stays ACTIVE until periodEndAt and does not renew after it.
        // Repeating the call returns the same scheduled cancellation.
        //
        //   POST /api/license-assignments/cancel
        //   Body: { "userID": 10 }
        [HttpPost("cancel")]
        public async Task<IActionResult> CancelLicense([FromBody] CancelLicenseRequestDto dto)
        {
            try
            {
                if (dto?.UserID is null or <= 0)
                    return BadRequest(ApiResponseHandler.Fail<string>("UserID is required."));

                var actor = await _tokenReader.GetCurrentUserAsync();
                if (actor == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                var result = await _subscriptionService.CancelLicenseAsync(dto.UserID.Value, actor, dto.ProfileId);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.LicenseCancelScheduled));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }

        // RRIDA Admin (Super Admin) only: raise the permitted team count.
        //   * Profile — every Team Manager licence of the profile allows N more teams:
        //       { "profileId": 69, "additionalTeams": 2, "notes": "Pilot" }
        //   * Manager / licence — only that licence allows N more teams:
        //       { "profileId": 69, "managerId": 150, "additionalTeams": 1 }
        //       { "profileId": 69, "companyLicenseId": 130, "additionalTeams": 1 }
        // Team creation and team assignment checks use the new limit at once; existing
        // team assignments are untouched. Every call is audited (TeamLimitExtensions).
        //
        //   POST /api/license-assignments/extend-team-limit
        [HttpPost("extend-team-limit")]
        public async Task<IActionResult> ExtendTeamLimit([FromBody] ExtendTeamLimitRequestDto dto)
        {
            try
            {
                if (dto == null)
                    return Ok(ApiResponseHandler.Fail<string>("Request body is required."));

                var actor = await _tokenReader.GetCurrentUserAsync();
                if (actor == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                var result = await _service.ExtendTeamLimitAsync(dto, actor.UserId);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.TeamLimitExtended));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }

        //   POST /api/license-assignments
        //   { "action": "Assign", "managerId": 263, "planCode": "pro" }
        // planId / planCode (Assign by managerId): give a free licence of that plan. A manager
        // holding a licence on another plan switches (Light <-> Pro): the old licence is
        // unassigned and free again, the teams move to the new one. Refused while the manager
        // runs more teams than the plan allows.
        [HttpPost]
        public async Task<IActionResult> Execute([FromBody] LicenseAssignmentRequestDto dto)
        {
            try
            {
                if (dto == null || !ModelState.IsValid)
                    return Ok(ApiResponseHandler.Fail<string>("Invalid payload."));

                // Special units (FeedbackAutoImport:BusinessUnitIds) are exempt
                // from licensing — permanently covered without a license — so a
                // purchased seat must never be pinned to one.
                var recipientUnitId = ResolveLicenseRecipientUnitId(dto);
                if (recipientUnitId.HasValue && _licensedPeriod.IsExemptFromLicensing(recipientUnitId.Value))
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UnitExemptFromLicensing));

                var actor = _tokenReader.GetUserIdFromToken();
                var result = await _service.ExecuteLifecycleAsync(dto, actor);

                // Light <-> Pro switch: stop billing the licence the manager left, at the end of
                // the period already paid. The switch itself is already saved; a Stripe failure
                // here is reported, never undone.
                if (result.PlanSwitched && result.ReplacedLicenseId is int replaced && actor is int actorId)
                {
                    try
                    {
                        result.ReplacedLicenseCancelled = await _subscriptionService.CancelReplacedLicenceAsync(replaced, actorId);
                    }
                    catch (Exception ex)
                    {
                        result.ReplacedLicenseCancelMessage = string.Format(
                            ResponseMessages.Errors.ReplacedLicenceCancelFailed, replaced, ex.GetBaseException().Message);
                    }
                }
                var message = result.TeamManagerRoleAssigned
                    ? ResponseMessages.Success.TeamManagerLicenceAndRoleAssigned
                    : result.TrialConverted
                        ? ResponseMessages.Success.TrialConvertedToPaidLicence
                        : ResponseMessages.Success.OperationSuccess;
                return Ok(ApiResponseHandler.Success(result, message));
            }
            catch (UnauthorizedAccessException ex)
            {
                // Partner admin (or any non-Clovera role) tried to manage a
                // license belonging to a company that isn't theirs.
                return StatusCode(403, ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }

        // Standalone complimentary assignment.
        //
        //   POST /api/license-assignments/complimentary
        //
        //   Body (AutoComplimentaryLicenseRequestDto):
        //     BusinessUnitId  — the target team to grant complimentary coverage to.
        //
        // Unlike the AssignComplimentary action above, this does NOT need a
        // CompanyLicenseId. Given only the target team it:
        //   1. checks the team has a manager;
        //   2. finds a DIFFERENT team with the same manager that already holds an
        //      active purchased license (with a free complimentary slot);
        //   3. grants the target complimentary coverage from that license;
        //   4. returns a specific failure when the team has no manager or no
        //      eligible purchased license exists.
        //
        // Works independently of the purchased assign flow, so complimentary
        // coverage can be re-granted after removal WITHOUT first removing and
        // reassigning the purchased license. Writes a ComplimentaryLicenseGrant +
        // audit row; consumes no purchased slot.
        [HttpPost("complimentary")]
        public async Task<IActionResult> AssignComplimentary([FromBody] AutoComplimentaryLicenseRequestDto dto)
        {
            try
            {
                if (dto == null || !ModelState.IsValid)
                    return Ok(ApiResponseHandler.Fail<string>("Invalid payload."));

                // Same exemption gate as the lifecycle endpoint — this route also
                // ends with coverage pinned to dto.BusinessUnitId.
                if (_licensedPeriod.IsExemptFromLicensing(dto.BusinessUnitId))
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UnitExemptFromLicensing));

                var actor = _tokenReader.GetUserIdFromToken();
                var result = await _service.AutoAssignComplimentaryAsync(dto.BusinessUnitId, actor);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }
    }
}
