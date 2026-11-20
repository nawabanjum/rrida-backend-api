using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    [Route("api/llm-report-schedules")]
    [ApiController]
    [Authorize]
    public class LLMReportScheduleController : ControllerBase
    {
        private readonly ILLMReportScheduleService _service;
        private readonly ITokenReaderService _tokenReader;

        public LLMReportScheduleController(
            ILLMReportScheduleService service,
            ITokenReaderService tokenReader)
        {
            _service = service;
            _tokenReader = tokenReader;
        }

        // GET /api/llm-report-schedules?profileId=123             → all units of the company
        // GET /api/llm-report-schedules?profileId=123&unitId=0    → same as above (treated as "all")
        // GET /api/llm-report-schedules?profileId=123&unitId=45   → just unit 45
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Get([FromQuery] uint profileId, [FromQuery] uint? unitId = null)
        {
            try
            {
                if (profileId == 0)
                    return Ok(ApiResponseHandler.Fail<string>("Invalid company id."));

                Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
                Response.Headers["Pragma"] = "no-cache";
                Response.Headers["Expires"] = "0";

                var actorUserId = _tokenReader.GetUserIdFromToken();

                // unitId == 0 is treated as "no filter" — controller normalises so
                // the service never has to special-case it.
                uint? normalisedUnitId = (unitId.HasValue && unitId.Value > 0) ? unitId : null;

                var result = await _service.GetSchedulesAsync(profileId, normalisedUnitId, actorUserId);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
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

        // PUT /api/llm-report-schedules
        // Body: List< { ProfileId, UnitId, IsScheduleEnabled, ReportGenerationDays } >
        //
        // Only entries with IsScheduleEnabled=true are persisted. Disabled entries
        // are dropped silently. Returns the rows actually saved.
        [HttpPut]
        public async Task<IActionResult> Update([FromBody] List<UpdateLLMReportScheduleDto> dtos)
        {
            try
            {
                if (dtos == null || dtos.Count == 0)
                    return Ok(ApiResponseHandler.Fail<string>("Body must contain at least one schedule entry."));

                if (!ModelState.IsValid)
                    return Ok(ApiResponseHandler.Fail<string>("Invalid payload."));

                var actorUserId = _tokenReader.GetUserIdFromToken();

                var result = await _service.UpdateSchedulesAsync(dtos, actorUserId);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
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
