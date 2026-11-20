using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace CloverBackoffice.API.Controllers
{
    [AllowAnonymous]
    [Route("api/rag")]
    [ApiController]
    public class RagController : ControllerBase
    {
        private readonly IRagService _rag;
        private readonly IFeedbackAnalysisService _feedbackservice;
        private readonly ITokenReaderService _tokenReader;
        private readonly IConfiguration _config;


        private readonly LegacyReportPathOptions _legacyReportPaths;

        public RagController(IRagService rag, IFeedbackAnalysisService feedbackService, ITokenReaderService tokenReader, IConfiguration config, IOptions<LegacyReportPathOptions> legacyReportPaths)
        {
            _rag = rag;
            _feedbackservice = feedbackService;
            _tokenReader = tokenReader;
            _config = config;
            _legacyReportPaths = legacyReportPaths?.Value ?? new LegacyReportPathOptions();
        }

        [HttpPost("openai-query")]
        public async Task<IActionResult> Query([FromQuery] long unitId, [FromQuery] int days)
        {
            var feedback = await _feedbackservice.AnalyzeFeedbackBatches(unitId, days);
            return Ok(ApiResponseHandler.Success(feedback, ResponseMessages.Success.AzureAISuccess));

        }

        [HttpGet("batches-job-status/{businessUnitId}")]
        public async Task<IActionResult> GetBatchJobsWithInsights(int businessUnitId)
        {
            var result = await _feedbackservice.GetBatchJobsWithInsightsAsync(businessUnitId);

            if (result.BatchJobs == null || !result.BatchJobs.Any()) 
            
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.BatchJobFail));
            return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.BatchJobStatus));
        }

        [HttpPost("openai-azure-results")]
        public async Task<IActionResult> Query([FromQuery] long unitId, [FromQuery] DateTimeOffset fromDate, [FromQuery] DateTimeOffset toDate)
        {
            var feedback = await _feedbackservice.AnalyzeFeedbackAzureResults(unitId, fromDate, toDate);

            if (feedback == null)
            {
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.BatchFail));
            }

            return Ok(ApiResponseHandler.Success(feedback, ResponseMessages.Success.BatchJobSucc));
        }


        // DISABLED by default via LegacyReportPaths.ManualBatchInsightsEnabled.
        //
        // Publishes a report straight from posted batch-insight JSON, bypassing
        // the scheduled cycle and the admin review queue. The service method
        // (FeedbackAnalysisService.SaveInsightsAsync) is untouched; only this
        // route is closed. Flip the flag in appsettings to restore it.
        [HttpPost("approved-batchinsights-manually")]
        public async Task<IActionResult> SaveInsights([FromQuery] Guid batchJobId, [FromBody] JsonElement jsonResponse)
        {
            if (!_legacyReportPaths.ManualBatchInsightsEnabled)
            {
                return Ok(ApiResponseHandler.Fail<string>(LegacyReportPathOptions.DisabledMessage));
            }

            int? userId = _tokenReader.GetUserIdFromToken();
            if (!userId.HasValue)
            {
                return Unauthorized(ApiResponseHandler.Fail<string>("Invalid token - UserId missing."));
            }
            var resp = await _feedbackservice.SaveInsightsAsync(batchJobId, jsonResponse.GetRawText(), userId.Value);
            return Ok(ApiResponseHandler.Success<string>(null, resp));
        }

        // The POST "azure-backgroundjob-function" endpoint used to live here. It
        // existed solely so the CloverBackoffice.Jobs Function App's 5-minute
        // timer trigger could drive Azure OpenAI batch polling from outside the
        // API, and it was anonymous for that reason. The Function App is gone;
        // BatchPollingBackgroundService now runs that work in-process on the
        // same cadence, so the unauthenticated entry point is removed rather
        // than left open with no caller.

        [HttpGet("get-worksheet-file")]
        public async Task<IActionResult> GetWorksheetFile([FromQuery] string filename)
        {
            try
            {
                // 1️ Validate token
                int? userId = _tokenReader.GetUserIdFromToken();
                if (!userId.HasValue)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidToken));
                }

                // 2️ Validate filename
                if (string.IsNullOrWhiteSpace(filename))
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.MissingFileName));
                }

                // Normalize: append .pdf when caller passes a bare name like "V9"
                filename = filename.Trim();
                if (!filename.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    filename = $"{filename}.pdf";
                }

                // 3️ Call service
                var result = await _feedbackservice.GetWorksheetFileAsync(filename);

                // 4️ Handle failure
                if (!result.IsSuccess)
                {
                    return Ok(ApiResponseHandler.Fail<string>(result.Message));
                }

                // 5️ Read and return file directly for download
                var fileBytes = await System.IO.File.ReadAllBytesAsync(result.FilePath);
                var fileName = Path.GetFileName(result.FilePath);
                if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    fileName = $"{fileName}.pdf";
                }
                var contentType = "application/pdf";

                return File(fileBytes, contentType, fileName);
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.UnexpectedError}: {ex.Message}"
                ));
            }
        }
        
        [HttpGet("worksheet-check-file")]
        public async Task<IActionResult> CheckFileExists([FromQuery][Required] string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.MissingFileName));

            var exists = await _feedbackservice.CheckWorksheetFileExistsAsync(fileName.Trim());

            if (!exists)
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.FileNotFound));

            return Ok(ApiResponseHandler.Success<string>(null, ResponseMessages.Success.FileFound));
        }
    }
}
