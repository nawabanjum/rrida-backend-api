using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.API.Controllers
{
    [Route("api/feedback")]
    [ApiController]
    [Authorize]
    public class FeedbackController : ControllerBase
    {
        private readonly IFeedbackListService _service;
        private readonly ITokenReaderService _tokenReader;
        private readonly ApplicationDbContext _context;

        public FeedbackController(IFeedbackListService service, ITokenReaderService tokenReader, ApplicationDbContext context)
        {
            _service = service;
            _tokenReader = tokenReader;
            _context = context;
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetFeedbackList([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            try
            {
                // Extract user id from token
                int? userId = _tokenReader.GetUserIdFromToken();
                if (!userId.HasValue)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidToken));
                }

                var user = await _context.Users.AsNoTracking()
                                               .FirstOrDefaultAsync(u => u.UserId == userId);
                if (user == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UserNotFound));
                }

                // Authorize only RRIDA Admins
                if (user.UserGroupId != (int)UserGroups.RridaAdmin)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ProfileAdminOnly));
                }

                var (items, total) = await _service.GetFeedbackPagedAsync(page, pageSize);

                return Ok(ApiResponseHandler.Success(new { items, total, page, pageSize }, ResponseMessages.Success.RecordFound));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }
}
