using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.Resource;

namespace CloverBackoffice.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly ILogger<AuthenticateController> _logger;
        private readonly IOptions<AppSettings> _options;
        private readonly IUserService _service;
        private readonly ITokenReaderService _tokenReader;

        // Inject logger into constructor
        public UserController(ILogger<AuthenticateController> logger, IOptions<AppSettings> options, IUserService service, ITokenReaderService tokenReader)
        {
            _logger = logger;
            _options = options;
            _service = service;
            _tokenReader = tokenReader;
        }

        [HttpGet("GetUserByUserId")]
        public async Task<IActionResult> GetUserByUserId(int UserId)
        {
            try
            {
                var User = await _service.GetUserInfoById(UserId);

                if (User == null || User.UserId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UserNotFound));
                }

                return Ok(ApiResponseHandler.Success(User, ResponseMessages.Success.UserFound));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpGet("UpdateUserGroup")]
        public async Task<IActionResult> UpdateUserGroup(int UserId, int GroupId)
        {
            try
            {
                if (UserId <= 0 || GroupId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidData));
                }
                var user = await _service.UpdateUserGroup(UserId, GroupId);
                if (user == null || user.UserId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UserNotFoundFailed));
                }

                return Ok(ApiResponseHandler.Success(user, ResponseMessages.Success.GroupUpdateSuccess));

            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpGet("GetNotifications")]
        public async Task<IActionResult> NotificationByUserId()
        {
            try
            {
                //  Read token from request
                string token = _tokenReader.GetToken();

                var data = await _service.GetNotificationUserByIdAsync(token);
                if (data == null || !data.Any())
                    return Ok(ApiResponseHandler.Fail(data, ResponseMessages.Errors.NoRecords));
                return Ok(ApiResponseHandler.Success(data, ResponseMessages.Success.NotificationsFetched));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }
}
