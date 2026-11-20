using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.API.Controllers
{
    /// <summary>
    /// Running the deterministic layer: a team's engine settings, and the replay itself.
    ///
    /// RRIDA Admin only for now. Switching the engine on for a team decides what its
    /// manager is told every day, and the thresholds behind it are still provisional, so
    /// it is not yet a customer-administered setting.
    /// </summary>
    [Route("api/rrida/engine")]
    [ApiController]
    [Authorize]
    public class RridaEngineRunController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IEngineReplayRunner _runner;
        private readonly EngineDailyRunJob _job;
        private readonly IRridaEngineContextFactory _engine;
        private readonly IEranNarratorService _narrator;
        private readonly IEngineScenarioSeeder _seeder;
        private readonly IEngineWorkbookImporter _workbook;
        private readonly IEngineDummyCheckInService _dummyCheckIn;
        private readonly IUserRoleAssignmentService _roles;
        private readonly ITokenReaderService _tokenReader;
        private readonly ILogger<RridaEngineRunController> _logger;

        public RridaEngineRunController(
            ApplicationDbContext context,
            IEngineReplayRunner runner,
            EngineDailyRunJob job,
            IRridaEngineContextFactory engine,
            IEranNarratorService narrator,
            IEngineScenarioSeeder seeder,
            IEngineWorkbookImporter workbook,
            IEngineDummyCheckInService dummyCheckIn,
            IUserRoleAssignmentService roles,
            ITokenReaderService tokenReader,
            ILogger<RridaEngineRunController> logger)
        {
            _context = context;
            _runner = runner;
            _job = job;
            _engine = engine;
            _narrator = narrator;
            _seeder = seeder;
            _workbook = workbook;
            _dummyCheckIn = dummyCheckIn;
            _roles = roles;
            _tokenReader = tokenReader;
            _logger = logger;
        }

        /// <summary>
        /// PUT api/rrida/engine/team-settings — set a team's start date and working week,
        /// and switch the engine on or off for it.
        ///
        /// The start date is day 1 of the team's engine clock and every stored day counts
        /// from it, so it is refused once answers exist: moving it renumbers history.
        /// </summary>
        [HttpPut("team-settings")]
        public async Task<IActionResult> UpsertSettings([FromBody] EngineTeamSettingsDto dto, CancellationToken ct)
        {
            try
            {
                var actor = await RequireRridaAdminAsync();
                if (actor is null) return Forbidden();
                if (dto is null || dto.BusinessUnitId == 0)
                    return Ok(ApiResponseHandler.Fail<string>("BusinessUnitId is required."));

                var unitExists = await _context.BusinessUnits
                    .AnyAsync(b => b.Id == dto.BusinessUnitId && b.IsDel != true, ct);
                if (!unitExists)
                    return NotFound(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.TeamNotFound));

                // A name from the kit's own calendar.json, so an unknown one is refused
                // here rather than at the first replay.
                var week = string.IsNullOrWhiteSpace(dto.WorkingWeek) ? "mon-fri" : dto.WorkingWeek.Trim();
                try
                {
                    _engine.Calendar.Resolve(week);
                }
                catch (Exception)
                {
                    // The kit throws on an unknown name; that is a bad request, not a fault.
                    return Ok(ApiResponseHandler.Fail<string>(
                        $"'{week}' is not a working week the kit knows. Use mon-fri, sun-thu, wed-sun, sat-wed, tue-sat or every-day."));
                }

                var row = await _context.EngineTeamSettings
                    .FirstOrDefaultAsync(s => s.BusinessUnitId == dto.BusinessUnitId, ct);

                var hasAnswers = await _context.EngineAnswers
                    .AnyAsync(a => a.BusinessUnitId == dto.BusinessUnitId, ct);

                if (row is null)
                {
                    row = new EngineTeamSettings
                    {
                        BusinessUnitId = dto.BusinessUnitId,
                        StartDate = dto.StartDate,
                        WorkingWeek = week,
                        IsEnabled = dto.IsEnabled,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.EngineTeamSettings.Add(row);
                }
                else
                {
                    if (hasAnswers && row.StartDate != dto.StartDate)
                        return Ok(ApiResponseHandler.Fail<string>(
                            "This team already has answers, so its start date cannot move: every stored day counts from it."));
                    // Each stored answer keeps the asked-day number it was given under the old
                    // week, and the replay walks the calendar under the new one: a change would
                    // move every answer to a different day, and new ones would collide.
                    if (hasAnswers && !string.Equals(row.WorkingWeek, week, StringComparison.OrdinalIgnoreCase))
                        return Ok(ApiResponseHandler.Fail<string>(
                            "This team already has answers, so its working week cannot change: every stored answer is numbered by it."));

                    row.StartDate = dto.StartDate;
                    row.WorkingWeek = week;
                    row.IsEnabled = dto.IsEnabled;
                    row.UpdatedAt = DateTime.UtcNow;
                    row.UpdatedBy = actor;
                }

                await _context.SaveChangesAsync(ct);

                return Ok(ApiResponseHandler.Success(new EngineTeamSettingsDto
                {
                    BusinessUnitId = row.BusinessUnitId,
                    StartDate = row.StartDate,
                    WorkingWeek = row.WorkingWeek,
                    IsEnabled = row.IsEnabled
                }, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Engine team settings failed for unit {Unit}.", dto?.BusinessUnitId);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>GET api/rrida/engine/team-settings?businessUnitId= — read one team's settings.</summary>
        [HttpGet("team-settings")]
        public async Task<IActionResult> GetSettings([FromQuery] uint businessUnitId, CancellationToken ct)
        {
            var actor = await RequireRridaAdminAsync();
            if (actor is null) return Forbidden();

            var row = await _context.EngineTeamSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.BusinessUnitId == businessUnitId, ct);

            return row is null
                ? NotFound(ApiResponseHandler.Fail<string>("This team has no engine settings."))
                : Ok(ApiResponseHandler.Success(new EngineTeamSettingsDto
                {
                    BusinessUnitId = row.BusinessUnitId,
                    StartDate = row.StartDate,
                    WorkingWeek = row.WorkingWeek,
                    IsEnabled = row.IsEnabled
                }, ResponseMessages.Success.OperationSuccess));
        }

        /// <summary>
        /// POST api/rrida/engine/run?businessUnitId=&amp;through= — replay one team now.
        ///
        /// The same work the daily job does. Safe to call repeatedly: the facts do not
        /// change and every result is upserted, so a second run rewrites the same rows.
        /// </summary>
        [HttpPost("run")]
        public async Task<IActionResult> Run(
            [FromQuery] uint businessUnitId, [FromQuery] DateOnly? through, CancellationToken ct)
        {
            try
            {
                var actor = await RequireRridaAdminAsync();
                if (actor is null) return Forbidden();

                // Without a date, the same day the daily job runs to: yesterday, the last day
                // whose answers are complete.
                var result = await _runner.RunAsync(
                    businessUnitId, through ?? EngineRunWindow.LastCompleteDay(), ct);

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Engine run failed for unit {Unit}.", businessUnitId);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>POST api/rrida/engine/run-all — the daily tick, on demand.</summary>
        [HttpPost("run-all")]
        public async Task<IActionResult> RunAll(CancellationToken ct)
        {
            try
            {
                var actor = await RequireRridaAdminAsync();
                if (actor is null) return Forbidden();

                await _job.RunJobAsync(ct);
                return Ok(ApiResponseHandler.Success(true, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Engine run-all failed.");
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// POST api/rrida/engine/narrate?businessUnitId=&amp;take= — has Eran phrase the
        /// feed items that have no prose yet.
        ///
        /// Each item is phrased once, so calling this twice costs nothing and changes no
        /// wording a manager has already read.
        /// </summary>
        [HttpPost("narrate")]
        public async Task<IActionResult> Narrate(
            [FromQuery] uint businessUnitId, [FromQuery] int take, CancellationToken ct)
        {
            try
            {
                var actor = await RequireRridaAdminAsync();
                if (actor is null) return Forbidden();

                var written = await _narrator.NarrateAsync(businessUnitId, take <= 0 ? 20 : take, ct);
                return Ok(ApiResponseHandler.Success(new { businessUnitId, phrased = written },
                    ResponseMessages.Success.OperationSuccess));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Eran narration failed for unit {Unit}.", businessUnitId);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>GET api/rrida/engine/scenarios — the demo scripts a team can be seeded from.</summary>
        [HttpGet("scenarios")]
        public async Task<IActionResult> Scenarios(CancellationToken ct)
        {
            try
            {
                var actor = await RequireRridaAdminAsync();
                if (actor is null) return Forbidden();

                var scenarios = await _seeder.ListAsync(ct);
                return Ok(ApiResponseHandler.Success(scenarios, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Listing engine scenarios failed.");
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// POST api/rrida/engine/seed-scenario — give a team ninety days of scripted answers
        /// and run the engine over them.
        ///
        /// TEST DATA. It writes real facts against a real team, so it is RRIDA Admin only and
        /// refuses a team that already holds engine data unless reset is sent. Use a test
        /// team, never a customer's.
        /// </summary>
        [HttpPost("seed-scenario")]
        public async Task<IActionResult> SeedScenario([FromBody] EngineSeedRequestDto dto, CancellationToken ct)
        {
            try
            {
                var actor = await RequireRridaAdminAsync();
                if (actor is null) return Forbidden();
                if (dto is null || dto.BusinessUnitId == 0)
                    return Ok(ApiResponseHandler.Fail<string>("BusinessUnitId is required."));

                var result = await _seeder.SeedAsync(dto, actor.Value, ct);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Seeding unit {Unit} from scenario {Scenario} failed.",
                    dto?.BusinessUnitId, dto?.ScenarioId);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// POST api/rrida/engine/import-workbook — upload the "RRIDA Daily Check-in - Test
        /// Data" workbook (multipart: file, profileId, alignToToday, replaceExisting,
        /// autoConfirm). One sheet per team: the team is found by name in the profile or
        /// created, its rows are seeded and replayed through the engine, and the same
        /// check-ins are written to Team Health's tables.
        ///
        /// TEST DATA, RRIDA Admin only. A team that already holds data is skipped unless
        /// replaceExisting is sent.
        /// </summary>
        [HttpPost("import-workbook")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(MaxWorkbookBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxWorkbookBytes)]
        public async Task<IActionResult> ImportWorkbook(
            IFormFile? file, [FromForm] EngineWorkbookImportOptionsDto options, CancellationToken ct)
        {
            try
            {
                var actor = await RequireRridaAdminAsync();
                if (actor is null) return Forbidden();
                if (file is null || file.Length == 0)
                    return Ok(ApiResponseHandler.Fail<string>("Attach the workbook as the form field \"file\"."));
                if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                    return Ok(ApiResponseHandler.Fail<string>("Only .xlsx workbooks are accepted."));
                if (options is null || options.ProfileId == 0)
                    return Ok(ApiResponseHandler.Fail<string>("profileId is required."));

                await using var stream = new MemoryStream();
                await file.CopyToAsync(stream, ct);
                stream.Position = 0;

                var result = await _workbook.ImportAsync(stream, file.FileName, options, actor.Value, ct);
                return Ok(result.Success
                    ? ApiResponseHandler.Success(result, "Workbook imported successfully.")
                    : ApiResponseHandler.Fail(result, "Workbook imported with problems. See each team's status and message."));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workbook import {File} failed.", file?.FileName);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// POST api/rrida/engine/dummy-check-in/run — run today's dummy check-in now, the
        /// same run the EngineDummyCheckIn job makes on its schedule. TEST DATA, RRIDA Admin
        /// only. Does nothing when "EngineDummyCheckIn:Enabled" is false or no team id is
        /// configured. Safe to repeat: members who already answered today are skipped.
        /// </summary>
        [HttpPost("dummy-check-in/run")]
        public async Task<IActionResult> RunDummyCheckIn(CancellationToken ct)
        {
            try
            {
                var actor = await RequireRridaAdminAsync();
                if (actor is null) return Forbidden();

                var result = await _dummyCheckIn.RunAsync(ct);
                return Ok(ApiResponseHandler.Success(result, result.Message ?? ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dummy check-in run failed.");
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        private const long MaxWorkbookBytes = 20 * 1024 * 1024;

        private async Task<int?> RequireRridaAdminAsync()
        {
            var actor = _tokenReader.GetUserIdFromToken();
            if (actor is null) return null;
            return await _roles.HasAnyRoleAsync(actor.Value, UserGroups.RridaAdmin) ? actor : null;
        }

        private IActionResult Forbidden() =>
            StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseHandler.Fail<string>("Only a RRIDA Admin may run the deterministic layer."));
    }
}
