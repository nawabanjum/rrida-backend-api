using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    /// <summary>
    /// Plan catalogue for the Admin side (RRIDA Admin only): create and edit plans, their
    /// per-manager price tiers and what each plan includes, activate / deactivate them, and
    /// preview the comparison table. The feature rows themselves are api/admin/plan-features.
    ///
    /// Separate from the legacy Plans table (registration / Stripe still use it). Licences do
    /// not read this catalogue yet.
    ///
    /// 400 invalid field; 401 no user in token; 403 not a RRIDA Admin; 404 no such plan;
    /// 409 stale version or code/name taken; 500 unexpected failure.
    /// </summary>
    [Route("api/admin/plans")]
    [ApiController]
    [Authorize]
    public class PlanManagementController : ControllerBase
    {
        private readonly IPlanManagementService _plans;
        private readonly ITokenReaderService _tokenReader;

        public PlanManagementController(IPlanManagementService plans, ITokenReaderService tokenReader)
        {
            _plans = plans;
            _tokenReader = tokenReader;
        }

        //   GET /api/admin/plans?status=all|active|inactive     (default all)
        [HttpGet]
        public Task<IActionResult> List([FromQuery] ServicePlanListQueryDto query) =>
            Run((u, ct) => _plans.ListPlansAsync(query, u, ct), ResponseMessages.Success.PlansFetched);

        //   GET /api/admin/plans/comparison?includeInactive=false
        // The "What you get on each plan" table: plans[] columns, rows[] with one cell per plan.
        [HttpGet("comparison")]
        public Task<IActionResult> Comparison([FromQuery] bool includeInactive = false) =>
            Run((u, ct) => _plans.GetComparisonAsync(includeInactive, u, ct), ResponseMessages.Success.PlanComparisonFetched);

        //   GET /api/admin/plans/2     details, price tiers and every feature with this plan's value
        [HttpGet("{planId:int}")]
        public Task<IActionResult> Get(int planId) =>
            Run((u, ct) => _plans.GetPlanAsync(planId, u, ct), ResponseMessages.Success.PlanFetched);

        //   POST /api/admin/plans
        //   { "code": "pro", "name": "Pro",                       // required
        //     "description": "...", "internalNotes": "...",
        //     "pricingUnit": "manager", "currency": "USD",          // defaults
        //     "isHighlighted": true, "allowsTeamLimitExtension": true, "sortOrder": 2,
        //     "isActive": false,                                    // default false (draft)
        //     "priceTiers": [ { "minManagers": 1, "maxManagers": 4, "monthlyPrice": 695, "annualPrice": 6950 } ],
        //     "features":   [ { "featureKey": "PORTFOLIO_VIEW", "isIncluded": true } ] }
        [HttpPost]
        public Task<IActionResult> Create([FromBody] CreateServicePlanDto? dto) =>
            Run((u, ct) => _plans.CreatePlanAsync(dto, u, ct), ResponseMessages.Success.PlanCreated);

        //   PUT /api/admin/plans/2
        //   { "version": 3, "name": "Pro", "internalNotes": "Client approved 2026-11" }
        // Only the fields sent change; code never changes.
        [HttpPut("{planId:int}")]
        public Task<IActionResult> Update(int planId, [FromBody] UpdateServicePlanDto? dto) =>
            Run((u, ct) => _plans.UpdatePlanAsync(planId, dto, u, ct), ResponseMessages.Success.PlanUpdated);

        //   PATCH /api/admin/plans/2/status
        //   { "isActive": false, "reason": "Replaced by Pro 2027", "version": 3 }   (version optional)
        [HttpPatch("{planId:int}/status")]
        public async Task<IActionResult> SetStatus(int planId, [FromBody] SetServicePlanStatusDto? dto) =>
            await Run((u, ct) => _plans.SetPlanStatusAsync(planId, dto, u, ct),
                dto?.IsActive == true ? ResponseMessages.Success.PlanActivated : ResponseMessages.Success.PlanDeactivated);

        //   PUT /api/admin/plans/2/price-tiers
        //   { "version": 3, "priceTiers": [
        //       { "minManagers": 1,  "maxManagers": 4,    "monthlyPrice": 695, "annualPrice": 6950 },
        //       { "minManagers": 5,  "maxManagers": 9,    "monthlyPrice": 595, "annualPrice": 5950 },
        //       { "minManagers": 10, "maxManagers": null, "monthlyPrice": 495, "annualPrice": 4950 } ] }
        // Replaces the whole set.
        [HttpPut("{planId:int}/price-tiers")]
        public Task<IActionResult> ReplacePriceTiers(int planId, [FromBody] ReplacePriceTiersDto? dto) =>
            Run((u, ct) => _plans.ReplacePriceTiersAsync(planId, dto, u, ct), ResponseMessages.Success.PlanPriceTiersUpdated);

        //   PUT /api/admin/plans/2/features
        //   { "version": 4, "features": [
        //       { "featureKey": "ERAN_COACH", "isIncluded": true, "limitValue": null },        // Unlimited
        //       { "featureKey": "LIVE_COACHING", "isIncluded": true, "displayText": "First reading plus ongoing" },
        //       { "featureId": 8, "isIncluded": false } ] }
        // Sets the features sent; the others keep their value.
        [HttpPut("{planId:int}/features")]
        public Task<IActionResult> SetFeatures(int planId, [FromBody] SetPlanFeaturesDto? dto) =>
            Run((u, ct) => _plans.SetPlanFeaturesAsync(planId, dto, u, ct), ResponseMessages.Success.PlanFeaturesUpdated);

        //   POST /api/admin/plans/2/duplicate
        //   { "code": "pro-2027", "name": "Pro 2027" }      → inactive copy with the same tiers and features
        [HttpPost("{planId:int}/duplicate")]
        public Task<IActionResult> Duplicate(int planId, [FromBody] DuplicateServicePlanDto? dto) =>
            Run((u, ct) => _plans.DuplicatePlanAsync(planId, dto, u, ct), ResponseMessages.Success.PlanDuplicated);

        //   PUT /api/admin/plans/reorder
        //   { "ids": [2, 1] }      every plan id, in display order
        [HttpPut("reorder")]
        public Task<IActionResult> Reorder([FromBody] ReorderDto? dto) =>
            Run((u, ct) => _plans.ReorderPlansAsync(dto, u, ct), ResponseMessages.Success.PlansReordered);

        //   GET /api/admin/plans/2/versions      every terms version, newest first, with its licence count
        // A new version is saved whenever the plan's terms change; licences keep the version they
        // were bought on.
        [HttpGet("{planId:int}/versions")]
        public Task<IActionResult> Versions(int planId) =>
            Run((u, ct) => _plans.GetPlanVersionsAsync(planId, u, ct), ResponseMessages.Success.PlanVersionsFetched);

        //   POST /api/admin/plans/2/migrate-licences
        //   { "fromVersionNumber": 1, "profileId": 119, "reason": "Client agreed new Pro terms" }   (all optional)
        // Moves licences (and their subscriptions) to the plan's newest version, so those
        // customers get its current terms. Nothing moves unless an admin calls this.
        [HttpPost("{planId:int}/migrate-licences")]
        public Task<IActionResult> MigrateLicences(int planId, [FromBody] MigratePlanLicencesDto? dto) =>
            Run((u, ct) => _plans.MigrateLicencesAsync(planId, dto, u, ct), ResponseMessages.Success.PlanLicencesMigrated);

        //   GET /api/admin/plans/2/history?take=100      newest first, with before / after JSON
        [HttpGet("{planId:int}/history")]
        public Task<IActionResult> History(int planId, [FromQuery] int? take) =>
            Run((u, ct) => _plans.GetPlanHistoryAsync(planId, take, u, ct), ResponseMessages.Success.PlanHistoryFetched);

        private async Task<IActionResult> Run<T>(Func<int, CancellationToken, Task<PlanManagementResult<T>>> call, string successMessage)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (userId == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                var result = await call(userId.Value, HttpContext.RequestAborted);
                return PlanManagementHttp.ToResponse(this, result, successMessage);
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }

    /// <summary>Maps a plan-management result to the house response shape and status code.</summary>
    internal static class PlanManagementHttp
    {
        public static IActionResult ToResponse<T>(ControllerBase c, PlanManagementResult<T> result, string successMessage)
        {
            if (result.IsSuccess)
                return c.Ok(ApiResponseHandler.Success(result.Data, successMessage));

            var body = ApiResponseHandler.Fail<string>(result.Message);
            return result.Error switch
            {
                PlanManagementError.Forbidden => c.StatusCode(StatusCodes.Status403Forbidden, body),
                PlanManagementError.NotFound => c.NotFound(body),
                PlanManagementError.Conflict => c.Conflict(body),
                _ => c.BadRequest(body)
            };
        }
    }
}
