using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    /// <summary>
    /// The deterministic layer's reference content: the questions, chips, actions,
    /// worksheets and phases of the vendored RRIDA kit.
    ///
    /// Loading is a RRIDA Admin action. The content is authored material that decides what
    /// every team is asked, so it is not a per-profile setting and no customer administers
    /// it. Reading a question is open to any authenticated caller, because that is what the
    /// check-in screen needs.
    /// </summary>
    [Route("api/rrida/reference-data")]
    [ApiController]
    [Authorize]
    public class RridaReferenceDataController : ControllerBase
    {
        private readonly IRridaReferenceDataLoader _loader;
        private readonly IUserRoleAssignmentService _roles;
        private readonly ITokenReaderService _tokenReader;
        private readonly ILogger<RridaReferenceDataController> _logger;

        public RridaReferenceDataController(
            IRridaReferenceDataLoader loader,
            IUserRoleAssignmentService roles,
            ITokenReaderService tokenReader,
            ILogger<RridaReferenceDataController> logger)
        {
            _loader = loader;
            _roles = roles;
            _tokenReader = tokenReader;
            _logger = logger;
        }

        /// <summary>
        /// POST api/rrida/reference-data/load — loads the vendored kit as a NEW version and
        /// makes it active. Nothing is updated in place: the previous version keeps every
        /// row, because a delivery made against it must still resolve months later.
        /// </summary>
        [HttpPost("load")]
        public async Task<IActionResult> Load(CancellationToken ct)
        {
            try
            {
                var actor = _tokenReader.GetUserIdFromToken();
                if (actor is null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                if (!await _roles.HasAnyRoleAsync(actor.Value, UserGroups.RridaAdmin))
                    return StatusCode(StatusCodes.Status403Forbidden,
                        ApiResponseHandler.Fail<string>("Only a RRIDA Admin may load the reference data."));

                var result = await _loader.LoadAsync(actor, ct);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (InvalidOperationException ex)
            {
                // A count mismatch or a missing kit. Nothing was written.
                _logger.LogError(ex, "RRIDA reference data load refused.");
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RRIDA reference data load failed.");
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>GET api/rrida/reference-data/active — the live version and its counts.</summary>
        [HttpGet("active")]
        public async Task<IActionResult> Active(CancellationToken ct)
        {
            try
            {
                var version = await _loader.GetActiveAsync(ct);
                return version is null
                    ? Ok(ApiResponseHandler.Fail<string>("No reference data has been loaded yet."))
                    : Ok(ApiResponseHandler.Success(version, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reading the active reference data version failed.");
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// GET api/rrida/reference-data/question/{questionKey} — a question with its three
        /// chip sets in authored order. What the check-in screen renders.
        /// </summary>
        [HttpGet("question/{questionKey:int}")]
        public async Task<IActionResult> Question(int questionKey, CancellationToken ct)
        {
            try
            {
                var question = await _loader.GetQuestionAsync(questionKey, ct);
                return question is null
                    ? NotFound(ApiResponseHandler.Fail<string>("No such question in the active reference data."))
                    : Ok(ApiResponseHandler.Success(question, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reading question {QuestionKey} failed.", questionKey);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }
}
