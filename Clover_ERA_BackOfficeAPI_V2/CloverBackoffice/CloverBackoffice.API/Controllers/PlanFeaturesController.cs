using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    /// <summary>
    /// The feature rows of the plan comparison table (RRIDA Admin only), shared by every plan.
    /// Each plan's value for a feature is set through PUT api/admin/plans/{id}/features.
    ///
    /// Key (e.g. PORTFOLIO_VIEW) never changes: product code checks it. System features keep
    /// their value type and cannot be deactivated. Nothing is ever deleted.
    ///
    /// 400 invalid field; 401 no user in token; 403 not a RRIDA Admin; 404 no such feature;
    /// 409 key taken; 500 unexpected failure.
    /// </summary>
    [Route("api/admin/plan-features")]
    [ApiController]
    [Authorize]
    public class PlanFeaturesController : ControllerBase
    {
        private readonly IPlanManagementService _plans;
        private readonly ITokenReaderService _tokenReader;

        public PlanFeaturesController(IPlanManagementService plans, ITokenReaderService tokenReader)
        {
            _plans = plans;
            _tokenReader = tokenReader;
        }

        //   GET /api/admin/plan-features?status=all|active|inactive     (default all)
        [HttpGet]
        public Task<IActionResult> List([FromQuery] string? status) =>
            Run((u, ct) => _plans.ListFeaturesAsync(status, u, ct), ResponseMessages.Success.PlanFeaturesFetched);

        //   GET /api/admin/plan-features/9
        [HttpGet("{featureId:int}")]
        public Task<IActionResult> Get(int featureId) =>
            Run((u, ct) => _plans.GetFeatureAsync(featureId, u, ct), ResponseMessages.Success.PlanFeatureFetched);

        //   POST /api/admin/plan-features
        //   { "key": "ERAN_VOICE", "title": "Eran voice sessions",          // required
        //     "valueType": "LIMIT",                                          // INCLUDED | LIMIT | TEXT
        //     "limitUnit": "sessions", "limitPeriod": "MONTH",               // LIMIT only
        //     "description": "...", "footnote": "...", "sortOrder": 12 }
        [HttpPost]
        public Task<IActionResult> Create([FromBody] CreatePlanFeatureDto? dto) =>
            Run((u, ct) => _plans.CreateFeatureAsync(dto, u, ct), ResponseMessages.Success.PlanFeatureCreated);

        //   PUT /api/admin/plan-features/9
        //   { "title": "...", "description": "..." }      only the fields sent change
        [HttpPut("{featureId:int}")]
        public Task<IActionResult> Update(int featureId, [FromBody] UpdatePlanFeatureDto? dto) =>
            Run((u, ct) => _plans.UpdateFeatureAsync(featureId, dto, u, ct), ResponseMessages.Success.PlanFeatureUpdated);

        //   PATCH /api/admin/plan-features/12/status
        //   { "isActive": false, "reason": "Not offered any more" }
        [HttpPatch("{featureId:int}/status")]
        public async Task<IActionResult> SetStatus(int featureId, [FromBody] SetPlanFeatureStatusDto? dto) =>
            await Run((u, ct) => _plans.SetFeatureStatusAsync(featureId, dto, u, ct),
                dto?.IsActive == true ? ResponseMessages.Success.PlanFeatureActivated : ResponseMessages.Success.PlanFeatureDeactivated);

        //   PUT /api/admin/plan-features/reorder
        //   { "ids": [1, 2, 3, ...] }      every feature id, in row order
        [HttpPut("reorder")]
        public Task<IActionResult> Reorder([FromBody] ReorderDto? dto) =>
            Run((u, ct) => _plans.ReorderFeaturesAsync(dto, u, ct), ResponseMessages.Success.PlanFeaturesReordered);

        //   GET /api/admin/plan-features/9/history?take=100
        [HttpGet("{featureId:int}/history")]
        public Task<IActionResult> History(int featureId, [FromQuery] int? take) =>
            Run((u, ct) => _plans.GetFeatureHistoryAsync(featureId, take, u, ct), ResponseMessages.Success.PlanHistoryFetched);

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
}
