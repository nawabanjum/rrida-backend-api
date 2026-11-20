using CloverBackoffice.API.Filters;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers.Mobile
{
    /// <summary>
    /// The deterministic layer's check-in, for the member app.
    ///
    /// Separate from the existing api/v1/enrolled-member-feedback endpoints, which keep
    /// working exactly as they do today. These write engine FACTS: an answer carries a
    /// member, so the floor can count distinct contributors, and the member is never
    /// reachable from anything a manager can read.
    ///
    /// Authenticated the same way the other mobile endpoints are: the shared app token in
    /// X-Api-Key, plus the member's own token in the body.
    /// </summary>
    [Route("api/v1/engine")]
    [ApiController]
    [AllowAnonymous]
    [PersonalAppTokenAuthorize]
    public class EngineCheckInController : ControllerBase
    {
        private readonly IEngineCheckInService _checkIn;
        private readonly IEngineManagerService _manager;
        private readonly ILogger<EngineCheckInController> _logger;

        public EngineCheckInController(
            IEngineCheckInService checkIn,
            IEngineManagerService manager,
            ILogger<EngineCheckInController> logger)
        {
            _checkIn = checkIn;
            _manager = manager;
            _logger = logger;
        }

        /// <summary>POST api/v1/engine/enrol — join a team with its code.</summary>
        [HttpPost("enrol")]
        public async Task<IActionResult> Enrol([FromBody] EngineEnrolRequestDto dto, CancellationToken ct)
        {
            try
            {
                if (dto is null) return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.RequestBodyEmpty));
                var result = await _checkIn.EnrolAsync(dto, ct);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Engine enrolment failed.");
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// POST api/v1/engine/leave — the member has been removed from the team in the app.
        /// Their membership ends today: the team stops counting them from tomorrow, and the
        /// answers they gave stay. Calling it again changes nothing.
        /// </summary>
        [HttpPost("leave")]
        public async Task<IActionResult> Leave([FromBody] EngineLeaveRequestDto dto, CancellationToken ct)
        {
            try
            {
                if (dto is null) return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.RequestBodyEmpty));
                var result = await _checkIn.LeaveAsync(dto, ct);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Engine leave failed for unit {Unit}.", dto?.BusinessUnitId);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// GET api/v1/engine/today — what this member is asked today, with the chip sets,
        /// and whether they have already answered each track.
        /// </summary>
        [HttpGet("today")]
        public async Task<IActionResult> Today(
            [FromQuery] string memberToken, [FromQuery] uint businessUnitId, CancellationToken ct)
        {
            try
            {
                var result = await _checkIn.TodayAsync(memberToken, businessUnitId, ct);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Engine today failed for unit {Unit}.", businessUnitId);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// GET api/v1/engine/today-fetch-by-code — today for a client that has the team's
        /// short code rather than its id (the RRIDA Feedback web app). Same member token, same
        /// response as today. Read-only.
        /// </summary>
        [HttpGet("today-fetch-by-code")]
        public async Task<IActionResult> TodayByCode(
            [FromQuery] string memberToken, [FromQuery] string businessUnitShortCode, CancellationToken ct)
        {
            try
            {
                var result = await _checkIn.TodayByCodeAsync(memberToken, businessUnitShortCode, ct);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Engine today failed for code {Code}.", businessUnitShortCode);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// GET api/v1/engine/next — the team's next asked days after today, up to five, each
        /// in the same shape as today: its questions with chip sets, and the custom question
        /// scheduled for that day. Days the team is not asked are skipped. The app prefetches
        /// them and puts each day's text on its reminder. Read-only. The engine counterpart
        /// of fetch-next-daily-questions.
        /// </summary>
        [HttpGet("next")]
        public async Task<IActionResult> Next(
            [FromQuery] string memberToken, [FromQuery] uint businessUnitId, CancellationToken ct)
        {
            try
            {
                var result = await _checkIn.NextAsync(memberToken, businessUnitId, ct);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Engine next failed for unit {Unit}.", businessUnitId);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// GET api/v1/engine/notes — the notes the team has been sent.
        ///
        /// This is the whole of what a member sees beyond the daily question: no state, no
        /// action, no chart. The note reports what changed, at theme level, and names
        /// nobody.
        /// </summary>
        [HttpGet("notes")]
        public async Task<IActionResult> Notes(
            [FromQuery] uint businessUnitId, [FromQuery] int take, CancellationToken ct)
        {
            try
            {
                var notes = await _manager.GetTeamNotesAsync(businessUnitId, take <= 0 ? 10 : take, ct);
                return Ok(ApiResponseHandler.Success(notes, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Engine notes failed for unit {Unit}.", businessUnitId);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>POST api/v1/engine/answer — one answer: a level, then chips or free text.</summary>
        [HttpPost("answer")]
        public async Task<IActionResult> Answer([FromBody] EngineAnswerRequestDto dto, CancellationToken ct)
        {
            try
            {
                if (dto is null) return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.RequestBodyEmpty));
                var result = await _checkIn.SubmitAsync(dto, ct);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (InvalidOperationException ex)
            {
                // Everything the member could have got wrong: not enrolled, not a working
                // day, a chip from another question, no reason given, already answered.
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Engine answer failed for unit {Unit}.", dto?.BusinessUnitId);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }
}
