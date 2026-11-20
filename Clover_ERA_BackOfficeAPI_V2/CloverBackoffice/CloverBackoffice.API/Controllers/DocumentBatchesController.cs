using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    [AllowAnonymous]
    [Route("api/document-batches")]
    [ApiController]
    public class DocumentBatchesController : ControllerBase
    {
        private readonly IDocumentBatchesService _documentbatches;

        public DocumentBatchesController(IDocumentBatchesService service)
        {
            _documentbatches = service;
        }
        [HttpPost("openai-query-batches")]
        public async Task<IActionResult> Query([FromQuery] long unitId, [FromQuery] int days)
        {
            var feedback = await _documentbatches.AnalyzeFeedbackBatchesEmbedding(unitId, days);
            return Ok(ApiResponseHandler.Success(feedback, ResponseMessages.Success.AzureAISuccess));

        }

        [HttpGet("batches-job-status/{businessUnitId}")]
        public async Task<IActionResult> GetBatchJobsWithInsightsEmbedding(int businessUnitId)
        {
            var result = await _documentbatches.GetBatchJobsWithInsightsAsyncEmbedding(businessUnitId);

            if (result.BatchJobs == null || !result.BatchJobs.Any())

                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.BatchJobFail));
            return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.BatchJobStatus));
        }
      
        [HttpPost("openai-azure-batches-results")]
        public async Task<IActionResult> Query([FromQuery] long unitId, [FromQuery] DateTimeOffset fromDate, [FromQuery] DateTimeOffset toDate)
        {
            var feedback = await _documentbatches.AnalyzeFeedbackAzureResultsEmbedding(unitId, fromDate, toDate);

            if (feedback == null)
            {
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.BatchFail));
            }

            return Ok(ApiResponseHandler.Success(feedback, ResponseMessages.Success.BatchJobSucc));
        }

    }
}
