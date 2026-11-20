using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Threading.Tasks;

namespace CloverBackoffice.API.Controllers
{
    [Route("api/push-notifications")]
    [ApiController]
    [Authorize]
    public class PushNotificationController : ControllerBase
    {
        private readonly IPushNotificationService _pushNotificationService;
        private readonly ITokenReaderService _tokenReader;

        public PushNotificationController(
            IPushNotificationService pushNotificationService,
            ITokenReaderService tokenReader)
        {
            _pushNotificationService = pushNotificationService;
            _tokenReader = tokenReader;
        }


        /// <summary>
        /// Save user system details (device, browser, OS info)
        /// Automatically extracts IP address and User-Agent from the request
        /// </summary>
        /// <param name="request">Optional request body containing OneSignalPlayerId</param>
        /// <returns>Saved system details</returns>
        [HttpPost("/api/user/system-details")]
        public async Task<IActionResult> SaveSystemDetails([FromBody] SaveSystemDetailsRequestDto? request = null)
        {
            try
            {
                // Extract user ID from token
                int? userId = _tokenReader.GetUserIdFromToken();
                if (!userId.HasValue)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>("Invalid or missing token."));
                }

                // Get IP address from request
                string ipAddress = GetClientIpAddress();

                // Get user agent from request headers
                string userAgent = Request.Headers["User-Agent"].FirstOrDefault() ?? string.Empty;

                // Get OneSignalPlayerId from request body (nullable)
                string? oneSignalPlayerId = request?.OneSignalPlayerId;

                var result = await _pushNotificationService.SaveSystemDetailsAsync(userId.Value, ipAddress, userAgent, oneSignalPlayerId);

                if (result.IsSuccess)
                {
                    return Ok(ApiResponseHandler.Success(result.Data, result.Message));
                }

                return Ok(ApiResponseHandler.Fail<string>(result.Message));
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>($"An error occurred: {ex.Message}"));
            }
        }

        /// <summary>
        /// Gets the client IP address from the request, checking proxy headers first
        /// </summary>
        private string GetClientIpAddress()
        {
            // Check for forwarded IP address (when behind proxy/load balancer)
            var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwardedFor))
            {
                // X-Forwarded-For can contain multiple IPs, take the first one
                var ip = forwardedFor.Split(',')[0].Trim();
                if (!string.IsNullOrWhiteSpace(ip))
                {
                    return ip;
                }
            }

            // Check for real IP header
            var realIp = Request.Headers["X-Real-IP"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(realIp))
            {
                return realIp;
            }

            // Fall back to connection remote IP address
            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        }
        [HttpPost("update-player-id")]
        public async Task<IActionResult> UpdateLatestRecordWithPlayerId(
          [FromBody] UpdatePlayerIdRequestDto request)
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new { success = false, message = "Request body is required" });
                }

                if (request.UserId <= 0)
                {
                    return BadRequest(new { success = false, message = "Valid UserId is required" });
                }

                if (string.IsNullOrWhiteSpace(request.OneSignalPlayerId))
                {
                    return BadRequest(new { success = false, message = "OneSignal Player ID is required" });
                }

                var result = await _pushNotificationService.UpdateLatestRecordWithPlayerIdAsync(
                    request.UserId,
                    request.OneSignalPlayerId);

                if (result.IsSuccess)
                {
                    return Ok(new
                    {
                        success = true,
                        message = result.Message,
                        data = result.Data
                    });
                }
                else
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = result.Message
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Internal server error"
                });
            }
        }
    }
}
