using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.API.Controllers
{
    [AllowAnonymous]
    [Route("api/insights")]
    [ApiController]
    public class InsightsController : ControllerBase
    {
        private readonly IInsightsGenerationService _insightsService;
        private readonly IBatchProcessingService _batchService;
        private readonly ILogger<InsightsController> _logger;

        public InsightsController(
            IInsightsGenerationService insightsService,
            IBatchProcessingService batchService,
            ILogger<InsightsController> logger)
        {
            _insightsService = insightsService;
            _batchService = batchService;
            _logger = logger;
        }

        public class GenerateInsightsRequest
        {
            [Required]
            public uint BusinessUnitId { get; set; }
            [Range(1, 365)]
            public int PeriodDays { get; set; } = 30;
        }

        [HttpPost("generate")]
        public async Task<IActionResult> Generate([FromBody] GenerateInsightsRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidRequest));
                }

                var job = await _batchService.CreateBatchJobAsync(request.BusinessUnitId, request.PeriodDays);

                return Ok(ApiResponseHandler.Success(new
                {
                    jobId = job.Id,
                    status = job.Status,
                    message = ResponseMessages.Success.InsightsGenerate
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initiate insights generation");
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpGet("status/{jobId}")]
        public async Task<IActionResult> Status([FromRoute] Guid jobId)
        {
            try
            {
                var status = await _batchService.GetBatchStatusAsync(jobId);
                return Ok(ApiResponseHandler.Success(status));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get batch status");
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpGet("results/{jobId}")]
        public async Task<IActionResult> Results([FromRoute] Guid jobId)
        {
            try
            {
                var insights = await _batchService.GetResultsAsync(jobId);
                if (insights == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ResultNotFound));
                }
                return Ok(ApiResponseHandler.Success(insights));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get results");
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpGet("history")]
        public async Task<IActionResult> History([FromQuery] uint businessUnitId, [FromQuery] int days = 90, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            try
            {
                if (page < 1) page = 1;
                if (pageSize < 1 || pageSize > 100) pageSize = 10;
                if (days < 1 || days > 365) days = 90;

                var insights = await _insightsService.GetInsightsHistoryAsync(businessUnitId, days, page, pageSize);
                return Ok(ApiResponseHandler.Success(insights));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get insights history");
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpGet("latest/{businessUnitId}")]
        public async Task<IActionResult> GetLatestInsights([FromRoute] uint businessUnitId)
        {
            try
            {
                var insights = await _insightsService.GetLatestInsightsAsync(businessUnitId);
                if (insights == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.NoInsightsFound));
                }
                return Ok(ApiResponseHandler.Success(insights));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get latest insights");
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpGet("batch/list")]
        public async Task<IActionResult> ListBatchJobs([FromQuery] string status = null, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            try
            {
                // TODO: Implement batch job listing with filtering
                return Ok(ApiResponseHandler.Success(new { message = ResponseMessages.Success.BatchJobSuccess }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list batch jobs");
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpDelete("batch/cancel/{jobId}")]
        public async Task<IActionResult> CancelBatchJob([FromRoute] Guid jobId)
        {
            try
            {
                // TODO: Implement batch job cancellation
                return Ok(ApiResponseHandler.Success(new { message = ResponseMessages.Errors.BatchJobFailure }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel batch job");
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }
}


