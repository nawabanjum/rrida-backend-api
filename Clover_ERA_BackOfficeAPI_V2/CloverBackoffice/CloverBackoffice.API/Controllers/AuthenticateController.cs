using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace CloverBackoffice.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AuthenticateController : ControllerBase
    {
        private readonly ILogger<AuthenticateController> _logger;
        private readonly IOptions<AppSettings> _options;
        private readonly IUserService _userService;
        private readonly JwtTokenGenerator _tokenGenerator;
        private readonly ITokenReaderService _tokenReader;

        // Inject logger into constructor
        public AuthenticateController(ILogger<AuthenticateController> logger, IOptions<AppSettings> options, IUserService userService, JwtTokenGenerator tokenGenerator, ITokenReaderService tokenReader)
        {
            _logger = logger;
            _options = options;
            _userService = userService;
            _tokenReader = tokenReader;
        }

        // For any authenticated user
        [HttpPost("ValidateToken")]
        public async Task<IActionResult> ValidateToken([FromBody] UserInfoDto dto)
        {
            //  Read token from request
            string token = _tokenReader.GetToken();

            var user = new User
            {
                TenantId = dto.TenantId,
                Username = dto.Username,
                Name = dto.Name,
                UserGroupId = Convert.ToInt32(CloverBackoffice.Components.Enums.UserGroups.ProfileAdmin)
            };
            if (user == null || string.IsNullOrWhiteSpace(user.Username))
            {
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidUser));
            }
            var UserExists = await _userService.UserExistsAsync(user.Username, token);
            if (UserExists != null)
            {
                return Ok(ApiResponseHandler.Success(UserExists, ResponseMessages.Success.UserFound));
            }
            try
            {
                var createdUser = await _userService.CreateUserAsync(token, user);

                if (createdUser == null || createdUser.UserId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.NotCreatedUser));
                }

                return Ok(ApiResponseHandler.Success(createdUser, ResponseMessages.Success.UserCreated));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        // For creating admin user
        [HttpPost("CreateAdmin")]
        public async Task<IActionResult> CreateAdmin([FromBody] UserInfoDto dto)
        {
            //  Read token from request
            string token = _tokenReader.GetToken();

            var user = new User
            {
                TenantId = dto.TenantId,
                Username = dto.Username,
                Name = dto.Name,
                UserGroupId = Convert.ToInt32(CloverBackoffice.Components.Enums.UserGroups.ProfileAdmin)
            };
            if (user == null || string.IsNullOrWhiteSpace(user.Username))
            {
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidUser));
            }
            var UserExists = await _userService.UserExistsAsync(user.Username, token);
            if (UserExists != null)
            {
                return Ok(ApiResponseHandler.Success(UserExists, ResponseMessages.Success.UserFound));
            }
            try
            {
                var createdUser = await _userService.CreateUserAsync(token, user);

                if (createdUser == null || createdUser.UserId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.NotCreatedUser));
                }

                return Ok(ApiResponseHandler.Success(createdUser, ResponseMessages.Success.UserCreated));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPost("update-password")]
        public async Task<IActionResult> UpdatePassword([FromBody] UpdatePasswordDto dto)
        {
            var result = await _userService.UpdatePasswordAsync(dto);

            if (!result.IsSuccess)
                return Ok(ApiResponseHandler.Fail<string>(result.Message));

            return Ok(ApiResponseHandler.Success<string>(null, result.Message));
        }
    }
}
