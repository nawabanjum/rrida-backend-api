using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    [Route("api/systemnotification")]
    [ApiController]
    [Authorize]
    public class SystemNotificationController : ControllerBase
    {
        private readonly ISystemNotificationService _service;
        private readonly ITokenReaderService _tokenReader;

        public SystemNotificationController(ISystemNotificationService service, ITokenReaderService tokenReader)
        {
            _service = service;
            _tokenReader = tokenReader;
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetNotifications([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (!userId.HasValue)
                    return Unauthorized(ApiResponseHandler.Fail<string>("Invalid token - UserId missing."));

                var result = await _service.GetNotificationsByUserIdAsync(userId.Value, page, pageSize);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.NotificationsFetched));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount()
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (!userId.HasValue)
                    return Unauthorized(ApiResponseHandler.Fail<string>("Invalid token - UserId missing."));

                var count = await _service.GetUnreadCountAsync(userId.Value);
                return Ok(ApiResponseHandler.Success(count, ResponseMessages.Success.NotificationsFetched));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPut("mark-as-read/{id}")]
        public async Task<IActionResult> MarkAsRead(int id)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (!userId.HasValue)
                    return Unauthorized(ApiResponseHandler.Fail<string>("Invalid token - UserId missing."));

                var result = await _service.MarkAsReadAsync(id, userId.Value);
                if (!result)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.NoRecords));

                return Ok(ApiResponseHandler.Success(true, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPut("mark-all-read")]
        public async Task<IActionResult> MarkAllAsRead()
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (!userId.HasValue)
                    return Unauthorized(ApiResponseHandler.Fail<string>("Invalid token - UserId missing."));

                var count = await _service.MarkAllAsReadAsync(userId.Value);
                return Ok(ApiResponseHandler.Success(new { markedCount = count }, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }
}
