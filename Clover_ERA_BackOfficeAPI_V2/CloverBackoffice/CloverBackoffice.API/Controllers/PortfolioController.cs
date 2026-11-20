using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    /// <summary>
    /// Portfolio View, per manager, for a Portfolio Manager or a RRIDA Admin. Every call
    /// takes an optional <c>managerId</c>: one of the managers GET managers returns, or left
    /// out (or 0) for All Managers. A manager outside the caller's portfolio is 403, never
    /// answered.
    ///
    /// A RRIDA Admin has no portfolio, so they also send <c>profileId</c>, the profile picked
    /// in the Profile dropdown, and see every manager in it. A Portfolio Manager leaves it
    /// out; their portfolio is always in their own profile.
    ///
    /// managerId is not a *UnitId name, so the tenant guard does not see it; the service
    /// checks the caller's role and their reporting lines. profileId is seen by the guard.
    ///
    /// 200 with data; 400 managerId negative, or a RRIDA Admin sent no profileId; 401 no
    /// user in token; 403 neither role, another company's profile, or the manager is not in
    /// their portfolio or the selected profile; 404 profile not found; 500 unexpected failure.
    /// </summary>
    [Route("api/portfolio")]
    [ApiController]
    [Authorize]
    public class PortfolioController : ControllerBase
    {
        private readonly IPortfolioViewService _portfolio;
        private readonly ITokenReaderService _tokenReader;

        private readonly IPlanFeatureGate _planGate;

        public PortfolioController(IPortfolioViewService portfolio, ITokenReaderService tokenReader, IPlanFeatureGate planGate)
        {
            _planGate = planGate;
            _portfolio = portfolio;
            _tokenReader = tokenReader;
        }

        //   GET /api/portfolio/managers?profileId=126
        [HttpGet("managers")]
        public Task<IActionResult> GetManagers([FromQuery] uint? profileId) =>
            RunAsync(userId => _portfolio.GetManagersAsync(userId, profileId, HttpContext.RequestAborted),
                ResponseMessages.Success.PortfolioManagersSuccess);

        //   GET /api/portfolio/teams?managerId=279&profileId=126
        [HttpGet("teams")]
        public Task<IActionResult> GetTeams([FromQuery] int? managerId, [FromQuery] uint? profileId) =>
            RunAsync(userId => _portfolio.GetTeamsAsync(userId, managerId, profileId, HttpContext.RequestAborted),
                ResponseMessages.Success.PortfolioTeamsSuccess);

        //   GET /api/portfolio/alerts?managerId=279&profileId=126
        [HttpGet("alerts")]
        public Task<IActionResult> GetAlerts([FromQuery] int? managerId, [FromQuery] uint? profileId) =>
            RunAsync(userId => _portfolio.GetAlertsAsync(userId, managerId, profileId, HttpContext.RequestAborted),
                ResponseMessages.Success.PortfolioAlertsSuccess);

        // The Eran column's brief and prompts for the scope. Chat turns on this page go to
        // POST api/eran-coach/chat with "portfolio": true and the same managerId and profileId.
        //
        //   GET /api/portfolio/eran-context?managerId=279&profileId=126
        [HttpGet("eran-context")]
        public Task<IActionResult> GetEranContext([FromQuery] int? managerId, [FromQuery] uint? profileId) =>
            RunAsync(userId => _portfolio.GetEranContextAsync(userId, managerId, profileId, HttpContext.RequestAborted),
                ResponseMessages.Success.PortfolioEranContextSuccess);

        private async Task<IActionResult> RunAsync<T>(Func<int, Task<TeamMilestoneResult<T>>> work, string successMessage)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (userId == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                // Plan: Team-of-teams Portfolio View (Pro) for a Team Manager; Profile Admins,
                // Portfolio Managers and RRIDA Admins are not limited. 403 when the plan lacks it.
                var gate = await _planGate.CheckAsync(userId.Value, PlanFeatureKeys.PortfolioView, HttpContext.RequestAborted);
                if (!gate.Allowed)
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponseHandler.Fail<string>(gate.Message!));

                var result = await work(userId.Value);
                if (!result.IsSuccess)
                {
                    var body = ApiResponseHandler.Fail<string>(result.Message);
                    return result.Error switch
                    {
                        TeamMilestoneError.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
                        TeamMilestoneError.NotFound => NotFound(body),
                        _ => BadRequest(body)
                    };
                }

                return Ok(ApiResponseHandler.Success(result.Data, successMessage));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }
}
