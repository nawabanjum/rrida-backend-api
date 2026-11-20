using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    /// <summary>
    /// The manager's view of the deterministic layer: the team's state, the one action it
    /// is working on, the confirmation, and the feed.
    ///
    /// Access is the same rule the dashboards use (IUnitAccessService), so a manager sees
    /// their own teams and nobody else's. Nothing here returns a member, a vote count or
    /// free text: a manager told that three people said something starts working out which
    /// three, and at a floor of two that is close to naming them.
    /// </summary>
    [Route("api/rrida/manager")]
    [ApiController]
    [Authorize]
    public class EngineManagerController : ControllerBase
    {
        private readonly IEngineManagerService _manager;
        private readonly IEngineRollUpService _rollUp;
        private readonly ITokenReaderService _tokenReader;
        private readonly ILogger<EngineManagerController> _logger;

        public EngineManagerController(
            IEngineManagerService manager,
            IEngineRollUpService rollUp,
            ITokenReaderService tokenReader,
            ILogger<EngineManagerController> logger)
        {
            _manager = manager;
            _rollUp = rollUp;
            _tokenReader = tokenReader;
            _logger = logger;
        }

        /// <summary>GET api/rrida/manager/state?businessUnitId= — the team's reading today.</summary>
        [HttpGet("state")]
        public Task<IActionResult> State([FromQuery] uint businessUnitId, CancellationToken ct) =>
            RunAsync(async actor =>
                ApiResponseHandler.Success(await _manager.GetStateAsync(actor, businessUnitId, ct),
                    ResponseMessages.Success.OperationSuccess), businessUnitId);

        /// <summary>
        /// GET api/rrida/manager/active-action?businessUnitId= — the one action, as delivered,
        /// or none when nothing has cleared the floor. Headwinds does not suppress it (D21): the
        /// flag only filters external-cause chips out of the tally.
        /// </summary>
        [HttpGet("active-action")]
        public Task<IActionResult> ActiveAction([FromQuery] uint businessUnitId, CancellationToken ct) =>
            RunAsync(async actor =>
            {
                var action = await _manager.GetActiveActionAsync(actor, businessUnitId, ct);
                return action is null
                    ? ApiResponseHandler.Fail<string>("No action is active for this team.")
                    : ApiResponseHandler.Success(action, ResponseMessages.Success.OperationSuccess);
            }, businessUnitId);

        /// <summary>
        /// GET api/rrida/manager/outward?businessUnitId= — D31: a cause the team is naming that
        /// sits outside it, with the one move the manager can make and who to raise it with.
        /// Shown beside the move card, never as a task: nothing to confirm, no reading. Data is
        /// null when the team's newest message (its last asked day; the run is through yesterday)
        /// carries none, or when that message is stale.
        /// </summary>
        [HttpGet("outward")]
        public Task<IActionResult> Outward([FromQuery] uint businessUnitId, CancellationToken ct) =>
            RunAsync(async actor =>
                ApiResponseHandler.Success(await _manager.GetOutwardConditionAsync(actor, businessUnitId, ct),
                    ResponseMessages.Success.OperationSuccess), businessUnitId);

        /// <summary>GET api/rrida/manager/feed?businessUnitId=&amp;take= — the message feed.</summary>
        [HttpGet("feed")]
        public Task<IActionResult> Feed([FromQuery] uint businessUnitId, [FromQuery] int take, CancellationToken ct) =>
            RunAsync(async actor =>
                ApiResponseHandler.Success(await _manager.GetFeedAsync(actor, businessUnitId, take <= 0 ? 30 : take, ct),
                    ResponseMessages.Success.OperationSuccess), businessUnitId);

        /// <summary>
        /// POST api/rrida/manager/confirm — one tap at 72 hours, saying the action was
        /// taken and whether the condition was met. Nothing goes to the team at this point.
        /// </summary>
        [HttpPost("confirm")]
        public Task<IActionResult> Confirm([FromBody] EngineConfirmRequestDto dto, CancellationToken ct) =>
            RunAsync(async actor =>
                ApiResponseHandler.Success(await _manager.ConfirmAsync(actor, dto, ct),
                    "Confirmed. The signal is read when that question next comes round."),
                dto?.BusinessUnitId ?? 0);

        /// <summary>
        /// POST api/rrida/manager/decline — the manager judges a stated contraindication
        /// applies. Recorded as a failed cycle with its reason; succession moves on.
        /// </summary>
        [HttpPost("decline")]
        public Task<IActionResult> Decline([FromBody] EngineDeclineRequestDto dto, CancellationToken ct) =>
            RunAsync(async actor =>
                ApiResponseHandler.Success(await _manager.DeclineAsync(actor, dto, ct),
                    "Recorded. This exercise will not be delivered to this team."),
                dto?.BusinessUnitId ?? 0);

        /// <summary>
        /// POST api/rrida/manager/acknowledge — "I'll do it". Recorded so every manager of the
        /// team sees the move was taken up. It confirms nothing: confirmation comes when due.
        /// </summary>
        [HttpPost("acknowledge")]
        public Task<IActionResult> Acknowledge([FromBody] EngineAcknowledgeRequestDto dto, CancellationToken ct) =>
            RunAsync(async actor =>
                ApiResponseHandler.Success(await _manager.AcknowledgeAsync(actor, dto, ct),
                    "Noted. You will be asked to confirm it was done when it falls due."),
                dto?.BusinessUnitId ?? 0);

        /// <summary>
        /// GET api/rrida/manager/roll-up — for a manager of managers.
        ///
        /// Each child team's state and cycle status, never its action, its issue file or
        /// its chips. A team below the visibility floor reads as calibrating whatever its
        /// real state, and the cross-team conditions are flagged provisional because their
        /// thresholds were guessed rather than derived.
        /// </summary>
        [HttpGet("roll-up")]
        public async Task<IActionResult> RollUp(CancellationToken ct)
        {
            try
            {
                var actor = await _tokenReader.GetCurrentUserAsync();
                if (actor is null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                var rollUp = await _rollUp.GetRollUpAsync(actor, ct);
                return Ok(ApiResponseHandler.Success(rollUp, ResponseMessages.Success.OperationSuccess));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Roll-up failed.");
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// The shape every action here shares: resolve the caller, run, and turn the three
        /// things that can go wrong into the three answers a client can act on.
        /// </summary>
        private async Task<IActionResult> RunAsync(
            Func<Infrastructure.Models.User, Task<object>> work, uint businessUnitId)
        {
            try
            {
                var actor = await _tokenReader.GetCurrentUserAsync();
                if (actor is null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                if (businessUnitId == 0)
                    return Ok(ApiResponseHandler.Fail<string>("BusinessUnitId is required."));

                return Ok(await work(actor));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                // Not set up, no such cycle, already confirmed, already resolved.
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Manager engine call failed for unit {Unit}.", businessUnitId);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }
}
