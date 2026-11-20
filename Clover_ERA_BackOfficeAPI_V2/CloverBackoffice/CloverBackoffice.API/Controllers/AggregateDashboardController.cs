using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    [Route("api/AggregateDashboard")]
    [ApiController]
    [Authorize]
    public class AggregateDashboardController : ControllerBase
    {
        private readonly IAggregateDashboardService _service;
        private readonly ITokenReaderService _tokenReader;

        public AggregateDashboardController(IAggregateDashboardService service, ITokenReaderService tokenReader)
        {
            _service = service;
            _tokenReader = tokenReader;
        }

        [HttpGet("aggregate-stats-analysis")]
        public async Task<IActionResult> AnalyzeFeedbackDaily([FromQuery] long unitId, [FromQuery] int days, [FromQuery] bool customRange, [FromQuery] DateTimeOffset? fromDate, [FromQuery] DateTimeOffset? toDate)
        {
            try
            {
                if (unitId <= 0)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidUnit));

                DateTimeOffset fromDateValue;
                DateTimeOffset toDateValue;

                if (customRange)
                {
                    if (!fromDate.HasValue || !toDate.HasValue)
                        return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Warnings.DatesNotProvided));
                    fromDateValue = fromDate.Value;
                    toDateValue = toDate.Value;
                }
                else
                {
                    if (days <= 0)
                        return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Warnings.DaysNotProvided));
                    toDateValue = DateTimeOffset.UtcNow;
                    fromDateValue = toDateValue.AddDays(-days);
                }

                // Get unit IDs with parent-child aggregation logic
                // If parent has no data, aggregate from children; if child is selected, use only that child
                var unitIds = await _service.GetBusinessUnitIdsForAnalysisAsync(unitId, fromDateValue, toDateValue);
                if (unitIds == null || !unitIds.Any())
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.BusinessUnitNotFound));

                // CloverERA Admins bypass license filtering and get the overall
                // data for the whole range; all other users stay licensed-period only.
                var isCloverAdmin = await _tokenReader.IsAdminAsync();

                // Analyze feedback for the determined unit IDs
                var result = await _service.AnalyzeFeedbackRunTimeAsync(unitIds, fromDateValue, toDateValue, ignoreLicenseFilter: isCloverAdmin);

                if (result == null)
                    return NotFound(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.DashboardSummaryNotFound));

                // Append (does not replace) the license-coverage breakdown for the
                // selected unit so the frontend can tell which sub-periods of the
                // range are licensed. Health data above is left untouched.
                result.LicensePeriods = await _service.GetLicensePeriodsAsync(unitId, fromDateValue, toDateValue);

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.FeedbackSummaryGenerated));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpGet("category-detail-analysis")]
        public async Task<IActionResult> GetCategoryDetail(
            [FromQuery] long unitId,
            [FromQuery] int days,
            [FromQuery] bool customRange,
            [FromQuery] DateTimeOffset? fromDate,
            [FromQuery] DateTimeOffset? toDate,
            [FromQuery] int cloveraCategory)
        {
            try
            {
                if (unitId <= 0)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidUnit));

                if (!Enum.IsDefined(typeof(CloverBackoffice.Components.Enums.CloverCategory), cloveraCategory))
                    return Ok(ApiResponseHandler.Fail<string>("Invalid CloverERA category. Valid values: 1=Communication, 2=Learning, 3=Opportunities, 4=Vulnerability, 5=Enablement, 6=Reflection."));

                DateTimeOffset fromDateValue;
                DateTimeOffset toDateValue;

                if (customRange)
                {
                    if (!fromDate.HasValue || !toDate.HasValue)
                        return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Warnings.DatesNotProvided));
                    fromDateValue = fromDate.Value;
                    toDateValue = toDate.Value;
                }
                else
                {
                    if (days <= 0)
                        return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Warnings.DaysNotProvided));
                    toDateValue = DateTimeOffset.UtcNow;
                    fromDateValue = toDateValue.AddDays(-days);
                }

                var unitIds = await _service.GetBusinessUnitIdsForAnalysisAsync(unitId, fromDateValue, toDateValue);
                if (unitIds == null || !unitIds.Any())
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.BusinessUnitNotFound));

                var result = await _service.GetCategoryDetailAsync(unitIds, fromDateValue, toDateValue, cloveraCategory);

                if (result == null)
                    return NotFound(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.DashboardSummaryNotFound));

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.FeedbackSummaryGenerated));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpGet("dashboard-overview")]
        public async Task<IActionResult> GetDashboardOverview([FromQuery] int? profileId)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (!userId.HasValue)
                    return Unauthorized(ApiResponseHandler.Fail<string>("Invalid token"));

                var result = await _service.GetDashboardData(userId.Value, profileId);

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (UnauthorizedAccessException)
            {
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ProfileAdminAccessDenied));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }
}
