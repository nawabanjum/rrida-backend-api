using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    /// <summary>
    /// What a user's plan gives them right now: licence and plan, every feature (enabled or not,
    /// limit, used this month, remaining) and team capacity. Only Team Managers are plan-gated;
    /// for anyone else isPlanGated is false and every feature is enabled.
    ///
    /// Read-only for now: features are not blocked from here yet.
    ///
    /// 401 no user in token; 403 another profile's user; 404 no such user; 500 failure.
    /// </summary>
    [Route("api/entitlements")]
    [ApiController]
    [Authorize]
    public class EntitlementsController : ControllerBase
    {
        private readonly IPlanEntitlementService _entitlements;
        private readonly ITokenReaderService _tokenReader;

        public EntitlementsController(IPlanEntitlementService entitlements, ITokenReaderService tokenReader)
        {
            _entitlements = entitlements;
            _tokenReader = tokenReader;
        }

        //   GET /api/entitlements/me
        [HttpGet("me")]
        public Task<IActionResult> Me() => Run(actor => actor);

        //   GET /api/entitlements?userId=263
        // The user themselves, their profile's Profile Admin, or a RRIDA Admin.
        [HttpGet]
        public Task<IActionResult> ForUser([FromQuery] int? userId) => Run(actor => userId ?? actor);

        private async Task<IActionResult> Run(Func<int, int> target)
        {
            try
            {
                int? actor = _tokenReader.GetUserIdFromToken();
                if (actor == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                var result = await _entitlements.GetForUserAsync(target(actor.Value), actor.Value, HttpContext.RequestAborted);
                return PlanManagementHttp.ToResponse(this, result, ResponseMessages.Success.EntitlementsFetched);
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }
}
