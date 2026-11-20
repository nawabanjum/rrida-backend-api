using CloverBackoffice.Components;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CloverBackoffice.API.Controllers
{
    [AllowAnonymous]
    [Route("api/[controller]")]
    [ApiController]
    public class FeedbackAnalysisController : ControllerBase
    {
        private readonly IFeedbackAnalysisService _service;
        private readonly IDocumentEmbeddingService _Emdservice;
        private readonly ITokenReaderService _tokenReader;
        private readonly IReportNotificationService _reportNotification;
        private readonly ILogger<FeedbackAnalysisController> _logger;

        private readonly LegacyReportPathOptions _legacyReportPaths;
        private readonly ReportApprovalOptions _reportApproval;

        public FeedbackAnalysisController(
            IDocumentEmbeddingService Emdservice,
            IFeedbackAnalysisService service,
            ITokenReaderService tokenReader,
            IReportNotificationService reportNotification,
            ILogger<FeedbackAnalysisController> logger,
            IOptions<LegacyReportPathOptions> legacyReportPaths,
            IOptions<ReportApprovalOptions> reportApproval)
        {
            _service = service;
            _Emdservice = Emdservice;
            _tokenReader = tokenReader;
            _reportNotification = reportNotification;
            _logger = logger;
            _legacyReportPaths = legacyReportPaths?.Value ?? new LegacyReportPathOptions();
            _reportApproval = reportApproval?.Value ?? new ReportApprovalOptions();
        }

        //[HttpGet("analyze")]
        //public async Task<IActionResult> AnalyzeFeedback([FromQuery] long unitId, [FromQuery] DateTimeOffset fromDate, [FromQuery] DateTimeOffset toDate)
        //{
        //    var result = await _service.AnalyzeFeedbackAsync(unitId, fromDate, toDate);
        //    return Ok(result);
        //}

        // DISABLED by default via LegacyReportPaths.AnalyzeByDaysEnabled.
        //
        // Despite being a GET, this endpoint CREATES and PUBLISHES a report for
        // the unit as a side effect of running the analysis — with no licence
        // check, no cadence check and no admin review. That makes it an
        // alternative report-generation path competing with the scheduled job,
        // which is now the authoritative one.
        //
        // The service method (FeedbackAnalysisService.AnalyzeFeedbackAsync2) is
        // untouched and still works; only the route into it is closed. Flip the
        // flag in appsettings to restore it.
        [HttpGet("get-analysis-by-days")]
        public async Task<IActionResult> AnalyzeFeedback2([FromQuery] long unitId, [FromQuery] int days)
        {
            if (!_legacyReportPaths.AnalyzeByDaysEnabled)
            {
                return Ok(ApiResponseHandler.Fail<string>(LegacyReportPathOptions.DisabledMessage));
            }

            if (unitId <= 0)
            {
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidUnit));
            }

            if (days <= 0)
            {
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidDays));
            }

            var unitExists = await _service.CheckBusinessUnitAsync(unitId);
            if (!unitExists)
            {
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.BusinessUnitNotFound));
            }

            FeedbackInsightsDto result = await _service.AnalyzeFeedbackAsync2(unitId, days);
            if (result == null)
            {
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.NoRecords));
            }
            return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.RecordFound));

        }

        [HttpGet("get-report-dates-by-unitId")]
        public async Task<ActionResult> GetReportDatesByUnit(long unitId)
        {
            try
            {
                if (unitId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidUnit));
                }

                var reports = await _service.GetReportsByUnitAsync(unitId);
                if (reports == null || reports.Count() == 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.NoReportsFound));
                }
                return Ok(ApiResponseHandler.Success(reports, ResponseMessages.Success.RecordFound));
            }
            catch (Exception ex)
            {

                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }

        }

        [HttpGet("get-reports")]
        public async Task<ActionResult> GetReports(long Id, long unitId)
        {
            try
            {
                if (unitId <= 0 || Id <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidReportData));
                }
                var unitExists = await _service.CheckBusinessUnitAsync(unitId);
                if (!unitExists)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.BusinessUnitNotFound));
                }

                var result = await _service.GetTopReportDataAsync(Id, unitId);

                if (result == null || (!result.Recommendations.Any() && !result.Themes.Any()) && !result.Actions.Any())
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.TopInsightsNotFound));
                }

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.TopInsightsGenerated));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }


        }

        [HttpGet("feedback/export")]
        public async Task<IActionResult> ExportFeedback(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate)
        {
            try
            {
                if (unitId <= 0)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidUnit));

                var feedbackList = await _service.FeedbackDataAsync(unitId, fromDate, toDate);
                if (feedbackList == null || !feedbackList.Any())
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.TopInsightsNotFound));
                }

                var csv = BussinessManager.GenerateCsv(feedbackList);
                var fileName = $"FeedbackExport_{unitId}_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
                var bytes = System.Text.Encoding.UTF8.GetBytes(csv);

                return File(bytes, "text/csv", fileName);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPost("upload-file")]
        public async Task<IActionResult> UploadExcel(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.FileUploadError));

            await _service.ImportExcelAsync(file);
            return Ok(ApiResponseHandler.Success<string>(null, ResponseMessages.Success.FeedbackDataImport));
        }

        [HttpPost("upload-prompt-data-file")]
        public async Task<IActionResult> UploadPrompt([FromForm] string startPoint, IFormFile file, [FromForm] string endpoint)
        {
            //if (file == null || file.Length == 0)
            //     return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.FileUploadError));

            // var data = await _service.AnalyzeFeedbackPromptData(startPoint, file, endpoint);

            // return Ok(ApiResponseHandler.Success(data, ResponseMessages.Success.ImportPromptData));

            if (file == null || file.Length == 0)
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.FileUploadError));

            var data = await _Emdservice.AnalyzeFeedbackPromptDataEmbedding(startPoint, file, endpoint);

            return Ok(ApiResponseHandler.Success(data, ResponseMessages.Success.ImportPromptData));
        }

        [HttpPost("save-run-insights")]
        public async Task<IActionResult> SaveRunInsights([FromBody] SaveRunInsightsRequestDto dto)
        {
            try
            {
                if (dto == null || dto.RunId <= 0)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidRunId));

                if (dto.TopThemes == null || !dto.TopThemes.Any(t => !string.IsNullOrWhiteSpace(t)))
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.TopThemesRequired));

                if (dto.TopRecommendations == null || !dto.TopRecommendations.Any(r => !string.IsNullOrWhiteSpace(r)))
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.TopRecommendationsRequired));

                if (dto.TopActions == null || !dto.TopActions.Any(a => !string.IsNullOrWhiteSpace(a)))
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.TopActionsRequired));

                var result = await _service.SaveRunInsightsAsync(
                    dto, _tokenReader.GetUserIdFromToken(), publish: true);

                if (result.Outcome == 0)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidRunId));

                var msg = result.Outcome == 1
                    ? ResponseMessages.Success.RunInsightsSaved
                    : ResponseMessages.Success.RunInsightsUpdated;

                // Publishing here IS the release — for a scheduled report this is
                // the admin approving it on the workbench history screen, the one
                // the reviewer email links to. Same notification the review API's
                // approve step sends, and only on a report's first release, so
                // re-saving a published report mails nobody again.
                //
                // A failure to notify must not fail the save: the report is
                // published and readable either way.
                if (result.FirstPublish && _reportApproval.NotifyManagerOnWorkbenchPublish)
                {
                    try
                    {
                        var notify = await _reportNotification.NotifyReportReadyAsync(
                            result.UnitId, result.ReportId, result.FromDate, result.ToDate);

                        if (!(notify.EmailSent || notify.InAppCreated || notify.PushSent))
                            _logger.LogWarning(
                                "[SaveRunInsights] ReportId={ReportId} published but its manager was not notified: {Reason}",
                                result.ReportId, notify.Reason);
                    }
                    catch (Exception notifyEx)
                    {
                        _logger.LogWarning(notifyEx,
                            "[SaveRunInsights] Manager notification failed for ReportId={ReportId}", result.ReportId);
                    }
                }

                return Ok(ApiResponseHandler.Success<string>(null, msg));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpDelete("report/{id}")]
        public async Task<IActionResult> DeleteReport(long id)
        {
            try
            {
                var result = await _service.DeleteReportAsync(id);

                if (!result)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.NoReportFoundId));

                return Ok(ApiResponseHandler.Success<string>(null, ResponseMessages.Success.ReportsDelSuccess));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>("Internal server error."));
            }
        }
        [HttpPost("run-promptfoo-eval")]
        public async Task<IActionResult> RunPromptFoo()
        {

            var evalResultsRaw = await _service.RunPromptfooEvaluationAsync();
            object evalResults;
            try
            {
                evalResults = System.Text.Json.JsonSerializer.Deserialize<object>(evalResultsRaw);
            }
            catch
            { 
                evalResults = evalResultsRaw;
            }

            return Ok(ApiResponseHandler.Success(new
            {
                evaluationFeedback = evalResults
            }, ResponseMessages.Success.ImportPromptData));
        }
    }
}
