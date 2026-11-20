using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    /// <summary>
    /// Public pricing: the active plans (for the pricing / purchase screen) and the price of N
    /// managers on a plan. No login needed (skipped by TokenAuthentication too). Drafts,
    /// inactive plans and internal notes are never shown; the RRIDA Admin side is api/admin/plans.
    ///
    /// 400 invalid query; 404 plan not found or inactive; 500 failure.
    /// </summary>
    [Route("api/plans")]
    [ApiController]
    [AllowAnonymous]
    public class PlansController : ControllerBase
    {
        private readonly IPlanCatalogService _catalog;

        public PlansController(IPlanCatalogService catalog)
        {
            _catalog = catalog;
        }

        //   GET /api/plans
        // Active plans as the comparison table: plans[] (prices, tiers), rows[] (features with
        // one cell per plan), footnotes[].
        [HttpGet]
        public async Task<IActionResult> List()
        {
            try
            {
                var plans = await _catalog.GetActivePlansAsync(HttpContext.RequestAborted);
                return Ok(ApiResponseHandler.Success(plans, ResponseMessages.Success.PlansFetched));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        //   GET /api/plans/price?planCode=pro&quantity=6&billingCycle=annual
        //   GET /api/plans/price?planId=2&quantity=1&billingCycle=monthly
        // The quantity picks one tier; every manager pays that tier's price.
        [HttpGet("price")]
        public async Task<IActionResult> Price([FromQuery] int? planId, [FromQuery] string? planCode,
            [FromQuery] int? quantity, [FromQuery] string? billingCycle)
        {
            try
            {
                var result = await _catalog.QuoteAsync(planId, planCode, quantity, billingCycle, HttpContext.RequestAborted);
                return PlanManagementHttp.ToResponse(this, result, ResponseMessages.Success.PlanPriceQuoted);
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }
}
