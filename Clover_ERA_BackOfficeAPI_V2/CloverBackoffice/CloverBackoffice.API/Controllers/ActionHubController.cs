using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    [Route("api/actionhub")]
    [ApiController]
    [Authorize]
    //changes
    public class ActionHubController : ControllerBase
    {
        private readonly IActionHubService _service;
        private readonly ITokenReaderService _tokenReader;

        public ActionHubController(IActionHubService service, ITokenReaderService tokenReader)
        {
            _service = service;
            _tokenReader = tokenReader;
        }

        [HttpPost("upsert-action-event")]
        public async Task<IActionResult> Upsert([FromBody] UpsertEventDto dto)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (userId.HasValue)
                    dto.CreatedBy = userId.Value;

                var result = await _service.UpsertAsync(dto);

                if (result.message.Contains("Invalid ActionEventId") || result.message.Contains("not found") || result.message.Contains("Invalid Type. Please enter either 'manually' or 'automatically'.") || result.message.Contains("Action Hub is not valid"))
                {
                    return Ok(ApiResponseHandler.Fail<string>(result.message));
                }

                return Ok(ApiResponseHandler.Success(result, result.message));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpDelete("action-event/{id}")]
        public async Task<IActionResult> DeleteActionEventById(int id)
        {
            try
            {
                var result = await _service.DeleteAsync(id);
                if (!result)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ActionEventNotFound));
                }

                return Ok(ApiResponseHandler.Success(true, ResponseMessages.Success.ActionEventDelSuccess));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPost("details")]
        public async Task<APIResponse<List<ActionHubWithEventsDto>>> GetActionEventDetails([FromQuery] int id, [FromQuery] string? type)
        {
            APIResponse<List<ActionHubWithEventsDto>> response = new APIResponse<List<ActionHubWithEventsDto>>();
            try
            {
                var resp = await _service.GetActionHubWithEventsAsync(id, type);
                response.Content = resp;
                response.IsSuccess = true;
                response.Message = ResponseMessages.Success.ActionHubDataFound;

                return response;
            }
            catch (ArgumentException ex)
            {
                response.IsSuccess = false;
                response.Message = ex.Message;
                return response;
            }
            catch (Exception ex)
            {
                response.IsSuccess = false;
                response.Message = ex.Message;
                return response;
            }
        }

        [HttpGet("summary")]
        public async Task<APIResponse<ActionHubSummaryDto>> GetActionHubSummary([FromQuery] int id, [FromQuery] string? type)
        {
            APIResponse<ActionHubSummaryDto> response = new APIResponse<ActionHubSummaryDto>();
            try
            {
                response.Content = await _service.GetActionHubSummaryAsync(id, type);
                response.IsSuccess = true;
                response.Message = ResponseMessages.Success.ActionHubSummaryFound;

                return response;
            }
            catch (ArgumentException ex)
            {
                response.IsSuccess = false;
                response.Message = ex.Message;
                return response;
            }
            catch (Exception ex)
            {
                response.IsSuccess = false;
                response.Message = ResponseMessages.Errors.ExceptionMsg;
                return response;
            }
        }

        [HttpPost("upsert-action-hub")]
        public async Task<IActionResult> Upsert([FromBody] UpsertActiontHubDto dto)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (userId.HasValue)
                    dto.CreatedBy = userId.Value;

                var result = await _service.UpsertActionHubAsync(dto);

                if (result.message.Contains("Invalid ActionHubId") || result.message.Contains("not found") || result.message.Contains("Invalid BusinessUnitId") || result.message.Contains("Invalid ProfileId") || result.message.Contains("Invalid Type. Please enter either 'manually' or 'automatically'."))
                {
                    return Ok(ApiResponseHandler.Fail<string>(result.message));
                }

                return Ok(ApiResponseHandler.Success(result, result.message));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpDelete("action-hub/{id}")]
        public async Task<IActionResult> DeleteActionHubById(int id)
        {
            try
            {
                var result = await _service.DeleteActionHubAsync(id);
                if (!result)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ActionHubNotFound));
                }

                return Ok(ApiResponseHandler.Success(true, ResponseMessages.Success.ActionHubDelSuccess));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }
}
