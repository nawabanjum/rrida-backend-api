using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Components;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class CloverEraEngineController : ControllerBase
    {
        private readonly IFeedbackAnalysisService _feedbackService;
        private readonly CloverEraWorkQueue _workQueue;
        private readonly ApplicationDbContext _db;
        private readonly ILogger<CloverEraEngineController> _logger;

        public CloverEraEngineController(
            IFeedbackAnalysisService feedbackService,
            CloverEraWorkQueue workQueue,
            ApplicationDbContext db,
            ILogger<CloverEraEngineController> logger)
        {
            _feedbackService = feedbackService;
            _workQueue = workQueue;
            _db = db;
            _logger = logger;
        }

        /*
        /// <summary>
        /// Queue a CLOVER ERA 6-stage engagement report run.
        /// Provide ProfileId/UnitId/DateRange to fetch feedback automatically, or upload a CSV file directly.
        /// Returns a RunId. Poll /api/clover-era-engine/run/{runId}/status to retrieve the result.
        /// </summary>
        [HttpPost("run")]
        [RequestSizeLimit(50 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 50 * 1024 * 1024)]
        public async Task<IActionResult> Run(
            [FromForm] WorkbenchRunRequestDto request,
            IFormFile? csvFile,
            CancellationToken ct)
        {
            try
            {
                if (request.PromptId <= 0)
                    return BadRequest(ApiResponseHandler.Fail<object>("Prompt ID is required."));
                if (request.ModelIds == null || !request.ModelIds.Any())
                    return BadRequest(ApiResponseHandler.Fail<object>("At least one model is required."));

                if (request.Temperature.HasValue && (request.Temperature.Value < 0 || request.Temperature.Value > 2))
                    return BadRequest(ApiResponseHandler.Fail<object>("Temperature must be between 0 and 2."));
                if (request.TopP.HasValue && (request.TopP.Value < 0 || request.TopP.Value > 1))
                    return BadRequest(ApiResponseHandler.Fail<object>("TopP must be between 0 and 1."));
                if (request.MaxTokens.HasValue && request.MaxTokens.Value <= 0)
                    return BadRequest(ApiResponseHandler.Fail<object>("MaxTokens must be greater than 0."));

                var hasCompany = request.ProfileId.HasValue && request.ProfileId.Value > 0;
                var hasUnit = request.UnitId.HasValue && request.UnitId.Value > 0;
                var hasDateRange = request.FromDate.HasValue && request.ToDate.HasValue;
                var hasAnyId = hasCompany || hasUnit || request.FromDate.HasValue || request.ToDate.HasValue;
                var hasAllIds = hasCompany && hasUnit && hasDateRange;
                var fileUploaded = csvFile != null && csvFile.Length > 0;

                if (!hasAnyId && !fileUploaded)
                    return BadRequest(ApiResponseHandler.Fail<object>("Please select Company ID, Unit ID, and Date Range, or upload a CSV file."));

                if (hasAnyId && !hasAllIds)
                {
                    var missing = new List<string>();
                    if (!hasCompany) missing.Add("Company ID is required.");
                    if (!hasUnit) missing.Add("Unit ID is required.");
                    if (!request.FromDate.HasValue) missing.Add("From Date is required.");
                    if (!request.ToDate.HasValue) missing.Add("To Date is required.");
                    return BadRequest(ApiResponseHandler.Fail<object>(string.Join(" ", missing)));
                }

                if (hasAllIds)
                {
                    if (request.FromDate!.Value > request.ToDate!.Value)
                        return BadRequest(ApiResponseHandler.Fail<object>("FromDate must be earlier than or equal to ToDate."));

                    _logger.LogInformation("[CloverEraEngine] Fetching feedback: CompanyId={CompanyId}, UnitId={UnitId}, From={From}, To={To}",
                        request.ProfileId, request.UnitId, request.FromDate, request.ToDate);

                    var feedbackList = await _feedbackService.FeedbackDataAsync(
                        request.ProfileId!.Value,
                        request.UnitId!.Value,
                        request.FromDate!.Value,
                        request.ToDate!.Value);

                    if (feedbackList == null || feedbackList.Count == 0)
                        return BadRequest(ApiResponseHandler.Fail<object>("No data found for the selected range."));

                    var csv = BussinessManager.GenerateCsv(feedbackList);
                    var bytes = Encoding.UTF8.GetBytes(csv);
                    var feedbackFileName = $"FeedbackExport_{request.UnitId}_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
                    var stream = new MemoryStream(bytes);
                    csvFile = new FormFile(stream, 0, bytes.Length, "csvFile", feedbackFileName)
                    {
                        Headers = new HeaderDictionary(),
                        ContentType = "text/csv"
                    };
                }

                if (csvFile == null || csvFile.Length == 0)
                    return BadRequest(ApiResponseHandler.Fail<object>("A CSV file is required. Provide ProfileId/UnitId/DateRange or upload a CSV directly."));

                // Copy file bytes before request stream is disposed
                byte[]? fileBytes;
                string fileName;
                string contentType;
                using (var ms = new MemoryStream())
                {
                    await csvFile.CopyToAsync(ms, ct);
                    fileBytes = ms.ToArray();
                    fileName = csvFile.FileName;
                    contentType = csvFile.ContentType;
                }

                // Persist initial status record (RunId is DB-generated)
                var runStatus = new WorkbenchRunStatus
                {
                    Status = "Queued",
                    CreatedAt = DateTime.UtcNow
                };
                _db.WorkbenchRunStatuses.Add(runStatus);
                await _db.SaveChangesAsync(ct);
                var runId = runStatus.RunId;

                // Queue for background processing
                await _workQueue.Writer.WriteAsync(new CloverEraWorkItem
                {
                    RunId = runId,
                    Request = request,
                    CsvFileBytes = fileBytes,
                    CsvFileName = fileName,
                    CsvContentType = contentType
                }, ct);

                _logger.LogInformation("[CloverEraEngine] Queued RunId={RunId}, returning 202 Accepted", runId);
                return Accepted(ApiResponseHandler.Success(
                    new { RunId = runId, Status = "Queued" },
                    "CloverEra engine run queued."));
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "[CloverEraEngine] KeyNotFound: {Message}", ex.Message);
                return BadRequest(ApiResponseHandler.Fail<object>(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "[CloverEraEngine] Validation error: {Message}", ex.Message);
                return BadRequest(ApiResponseHandler.Fail<object>(ex.Message));
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499, ApiResponseHandler.Fail<object>("Request was cancelled."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CloverEraEngine] Unhandled error: {Message}", ex.Message);
                return StatusCode(500, ApiResponseHandler.Fail<object>($"Engine error: {ex.Message}"));
            }
        }
        */

    }
}
