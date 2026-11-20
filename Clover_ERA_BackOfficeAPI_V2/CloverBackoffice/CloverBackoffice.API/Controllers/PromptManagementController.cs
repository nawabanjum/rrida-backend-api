using CloverBackoffice.Components;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace CloverBackoffice.API.Controllers
{
    [Authorize]
    [Route("api/prompt-management")]
    [ApiController]
    public class PromptManagementController : ControllerBase
    {
        private readonly IPromptManagementService _promptService;
        private readonly IFeedbackAnalysisService _feedbackService;
        private readonly ILogger<PromptManagementController> _logger;
        private readonly ILogCollector _logCollector;
        private readonly PromptfooWorkQueue _workQueue;
        private readonly CloverEraWorkQueue _cloverEraWorkQueue;
        private readonly ApplicationDbContext _db;
        private readonly IDocumentBatchesRepository _documentBatchesRepo;
        private readonly ITokenReaderService _tokenReader;

        public PromptManagementController(
            IPromptManagementService promptService,
            IFeedbackAnalysisService feedbackService,
            ILogger<PromptManagementController> logger,
            ILogCollector logCollector,
            PromptfooWorkQueue workQueue,
            CloverEraWorkQueue cloverEraWorkQueue,
            ApplicationDbContext db,
            IDocumentBatchesRepository documentBatchesRepo,
            ITokenReaderService tokenReader)
        {
            _promptService = promptService;
            _feedbackService = feedbackService;
            _logger = logger;
            _logCollector = logCollector;
            _workQueue = workQueue;
            _cloverEraWorkQueue = cloverEraWorkQueue;
            _db = db;
            _documentBatchesRepo = documentBatchesRepo;
            _tokenReader = tokenReader;
        }

        [HttpGet("active")]
        public async Task<IActionResult> GetAllActive()
        {
            var result = await _promptService.GetAllActiveAsync();
            return Ok(ApiResponseHandler.Success(result, "Active prompts retrieved."));
        }

        [HttpGet("{name}/active")]
        public async Task<IActionResult> GetActiveByName(string name)
        {
            var prompt = await _promptService.GetActivePromptAsync(name);
            if (prompt == null)
                return Ok(ApiResponseHandler.Fail<string>("No active prompt found for this name."));
            return Ok(ApiResponseHandler.Success(prompt, "Active prompt retrieved."));
        }

        [HttpGet("{name}/versions")]
        public async Task<IActionResult> GetVersions(string name)
        {
            var versions = await _promptService.GetVersionsAsync(name);
            return Ok(ApiResponseHandler.Success(versions, "Prompt versions retrieved."));
        }

        [HttpPost("upsert-prompt-v2")]
        public async Task<IActionResult> UpsertPrompt([FromBody] CreatePromptTemplateDto dto)
        {
            try
            {
                if (dto == null)
                    return Ok(ApiResponseHandler.Fail<string>("Request body is null or could not be deserialized."));

                if (string.IsNullOrWhiteSpace(dto.Name))
                    return Ok(ApiResponseHandler.Fail<string>("Prompt name is required."));

                var result = await _promptService.UpsertPromptAsync(dto);
                var message = dto.Id.HasValue && dto.Id.Value > 0
                    ? "Prompt version updated."
                    : "Prompt version created.";
                return Ok(ApiResponseHandler.Success(result, message));
            }
            catch (KeyNotFoundException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UpsertPromptV2] Unhandled error for prompt '{Name}'", dto?.Name);

                // Walk the full exception chain to surface the root cause
                var messages = new System.Collections.Generic.List<string>();
                var current = ex;
                while (current != null)
                {
                    messages.Add($"[{current.GetType().Name}] {current.Message}");
                    current = current.InnerException;
                }
                var fullError = string.Join(" -> ", messages);

                return StatusCode(500, ApiResponseHandler.Fail<string>($"Internal error: {fullError}\n\nStackTrace: {ex.StackTrace}"));
            }
        }

        [HttpPut("{name}/activate/{version}")]
        public async Task<IActionResult> ActivateVersion(string name, string version)
        {
            var success = await _promptService.ActivateVersionAsync(name, version);
            if (!success)
                return Ok(ApiResponseHandler.Fail<string>("Prompt version not found."));
            return Ok(ApiResponseHandler.Success(true, "Prompt version activated."));
        }

        [HttpDelete("delete-prompt/{id}")]
        public async Task<IActionResult> DeletePrompt(int id)
        {
            if (id <= 0)
                return Ok(ApiResponseHandler.Fail<string>("Invalid prompt id."));

            var success = await _promptService.DeletePromptAsync(id);
            if (!success)
                return Ok(ApiResponseHandler.Fail<string>("Prompt not found."));

            return Ok(ApiResponseHandler.Success(true, "Prompt deleted successfully."));
        }

        [HttpPost("render")]
        public async Task<IActionResult> RenderPrompt([FromBody] RenderPromptRequestDto dto)
        {
            var prompt = await _promptService.GetActivePromptAsync(dto.PromptName);
            if (prompt == null)
                return Ok(ApiResponseHandler.Fail<string>("Prompt not found."));
            var rendered = _promptService.RenderTemplate(prompt.Template, dto.Variables);
            return Ok(ApiResponseHandler.Success(rendered, "Prompt rendered."));
        }

        // ── Token Estimation ──

        [HttpPost("estimate-tokens")]
        public IActionResult EstimateTokens([FromBody] EstimateTokensRequestDto dto)
        {
            var charCount = dto.Text?.Length ?? 0;
            var response = new EstimateTokensResponseDto
            {
                CharCount = charCount,
                EstimatedTokenCount = charCount / 4
            };
            return Ok(ApiResponseHandler.Success(response, "Token estimation completed."));
        }

        // ── Test Cases (Dataset Tab) ──

        [HttpPost("upsert-test-case")]
        public async Task<IActionResult> UpsertTestCase([FromBody] UpsertTestCaseRequest dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Name))
                    return Ok(ApiResponseHandler.Fail<string>("Test case name is required."));

                var result = await _promptService.UpsertTestCaseAsync(dto);
                var message = dto.Id.HasValue && dto.Id.Value > 0
                    ? "Test case updated."
                    : "Test case created.";
                return Ok(ApiResponseHandler.Success(result, message));
            }
            catch (KeyNotFoundException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
        }

        [HttpGet("test-cases")]
        public async Task<IActionResult> GetTestCases([FromQuery] string? promptName)
        {
            var result = await _promptService.GetTestCasesAsync(promptName);
            return Ok(ApiResponseHandler.Success(result, "Test cases retrieved."));
        }

        [HttpGet("test-cases/{id}")]
        public async Task<IActionResult> GetTestCaseById(int id)
        {
            var result = await _promptService.GetTestCaseByIdAsync(id);
            if (result == null)
                return Ok(ApiResponseHandler.Fail<string>("Test case not found."));
            return Ok(ApiResponseHandler.Success(result, "Test case retrieved."));
        }

        [HttpDelete("test-cases/{id}")]
        public async Task<IActionResult> DeleteTestCase(int id)
        {
            var success = await _promptService.DeleteTestCaseAsync(id);
            if (!success)
                return Ok(ApiResponseHandler.Fail<string>("Test case not found."));
            return Ok(ApiResponseHandler.Success(true, "Test case deleted."));
        }

        // ── Workbench Run ──

        [HttpPost("workbench-run")]
        [RequestSizeLimit(52_428_800)] // 50 MB
        [RequestFormLimits(MultipartBodyLengthLimit = 52_428_800)]
        public async Task<IActionResult> RunWorkbench([FromForm] WorkbenchRunRequestDto request, IFormFile? testCaseFile, CancellationToken ct)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _logger.LogInformation("[WorkbenchRun] ===== Request received: PromptId={PromptId}, ModelIds=[{ModelIds}] =====",
                request.PromptId, string.Join(",", request.ModelIds ?? new List<int>()));
            _logCollector.Add("INFO", $"[WorkbenchRun] ===== Request received: PromptId={request.PromptId}, ModelIds=[{string.Join(",", request.ModelIds ?? new List<int>())}] =====");

            try
            {
                if (request.PromptId <= 0)
                    return Ok(ApiResponseHandler.Fail<string>("Prompt ID is required."));
                if (!request.ModelIds.Any())
                    return Ok(ApiResponseHandler.Fail<string>("At least one model is required."));

                if (request.Temperature.HasValue && (request.Temperature.Value < 0 || request.Temperature.Value > 2))
                    return Ok(ApiResponseHandler.Fail<string>("Temperature must be between 0 and 2."));
                if (request.TopP.HasValue && (request.TopP.Value < 0 || request.TopP.Value > 1))
                    return Ok(ApiResponseHandler.Fail<string>("TopP must be between 0 and 1."));
                if (request.MaxTokens.HasValue && request.MaxTokens.Value <= 0)
                    return Ok(ApiResponseHandler.Fail<string>("MaxTokens must be greater than 0."));

                // Validation rules:
                //  - All 3 (Company+Unit+DateRange) provided → fetch feedback; data → RunId, else fail.
                //  - None of the 3 + file uploaded → proceed with file (RunId via existing path).
                //  - None of the 3 + no file → "Please enter Company ID, Unit ID, and Date Range".
                //  - Partial 3 provided → specific missing-field message.
                var hasCompany = request.ProfileId.HasValue && request.ProfileId.Value > 0;
                var hasUnit = request.UnitId.HasValue && request.UnitId.Value > 0;
                var hasDateRange = request.FromDate.HasValue && request.ToDate.HasValue;
                var hasAnyId = hasCompany || hasUnit
                               || request.FromDate.HasValue || request.ToDate.HasValue;
                var hasAllIds = hasCompany && hasUnit && hasDateRange;
                var fileUploaded = testCaseFile != null && testCaseFile.Length > 0;

                // Neither IDs nor file → hard stop, do not proceed.
                if (!hasAnyId && !fileUploaded)
                    return Ok(ApiResponseHandler.Fail<string>("Please select Company ID, Unit ID, and Date Range."));

                // Partial IDs → specific missing-field message.
                if (hasAnyId && !hasAllIds)
                {
                    var missing = new List<string>();
                    if (!hasCompany) missing.Add("Company ID is required.");
                    if (!hasUnit) missing.Add("Unit ID is required.");
                    if (!request.FromDate.HasValue) missing.Add("From Date is required.");
                    if (!request.ToDate.HasValue) missing.Add("To Date is required.");
                    return Ok(ApiResponseHandler.Fail<string>(string.Join(" ", missing)));
                }

                if (hasAllIds)
                {
                    if (request.FromDate!.Value > request.ToDate!.Value)
                        return Ok(ApiResponseHandler.Fail<string>("FromDate must be earlier than or equal to ToDate."));

                    _logger.LogInformation(
                        "[WorkbenchRun] Fetching feedback by IDs: CompanyId={CompanyId}, UnitId={UnitId}, From={From}, To={To}",
                        request.ProfileId, request.UnitId, request.FromDate, request.ToDate);
                    _logCollector.Add("INFO",
                        $"[WorkbenchRun] Fetching feedback by IDs: CompanyId={request.ProfileId}, UnitId={request.UnitId}, From={request.FromDate}, To={request.ToDate}");

                    var feedbackList = await _feedbackService.FeedbackDataAsync(
                        request.ProfileId!.Value,
                        request.UnitId!.Value,
                        request.FromDate!.Value,
                        request.ToDate!.Value);

                    if (feedbackList == null || feedbackList.Count == 0)
                        return Ok(ApiResponseHandler.Fail<string>("No data found for the selected range."));

                    var csv = BussinessManager.GenerateCsv(feedbackList);
                    var bytes = Encoding.UTF8.GetBytes(csv);
                    var feedbackFileName = $"FeedbackExport_{request.UnitId}_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
                    var stream = new MemoryStream(bytes);
                    testCaseFile = new FormFile(stream, 0, bytes.Length, "testCaseFile", feedbackFileName)
                    {
                        Headers = new HeaderDictionary(),
                        ContentType = "text/csv"
                    };
                }

                var hasFile = testCaseFile != null && testCaseFile.Length > 0;
                var hasManual = !string.IsNullOrWhiteSpace(request.ManualTestCaseJson);

                if (!hasFile && !hasManual)
                    return Ok(ApiResponseHandler.Fail<string>("Provide ProfileId/UnitId/DateRange, a test case file (.xlsx or .csv), or ManualTestCaseJson."));

                if (hasFile)
                {
                    var ext = Path.GetExtension(testCaseFile!.FileName).ToLowerInvariant();
                    if (ext != ".xlsx" && ext != ".csv")
                        return Ok(ApiResponseHandler.Fail<string>("Only .xlsx and .csv files are supported."));
                }

                var runType = "Direct";
                _logger.LogInformation("[WorkbenchRun] Routing to BACKGROUND path (RunType={RunType})...", runType);
                _logCollector.Add("INFO", $"[WorkbenchRun] Routing to BACKGROUND path (RunType={runType})...");

                // Copy file bytes before the request stream is disposed
                byte[]? fileBytes = null;
                string? fileNameQueued = null;
                string? contentType = null;
                if (testCaseFile != null && testCaseFile.Length > 0)
                {
                    using var ms = new MemoryStream();
                    await testCaseFile.CopyToAsync(ms, ct);
                    fileBytes = ms.ToArray();
                    fileNameQueued = testCaseFile.FileName;
                    contentType = testCaseFile.ContentType;
                }

                // Persist initial status record (RunId is auto-generated)
                var runStatus = new WorkbenchRunStatus
                {
                    Status = "Queued",
                    CreatedAt = DateTime.UtcNow
                };
                _db.WorkbenchRunStatuses.Add(runStatus);
                await _db.SaveChangesAsync(ct);
                var runId = runStatus.RunId; // DB-generated identity

                // Route to CloverEra engine (same queue/DB pattern, no frontend change needed)
                await _cloverEraWorkQueue.Writer.WriteAsync(new CloverEraWorkItem
                {
                    RunId = runId,
                    Request = request,
                    CsvFileBytes = fileBytes,
                    CsvFileName = fileNameQueued,
                    CsvContentType = contentType
                }, ct);

                _logger.LogInformation("[WorkbenchRun] Queued RunId={RunId} (RunType={RunType}), returning 202 Accepted", runId, runType);
                return Accepted(ApiResponseHandler.Success(
                    new { RunId = runId, Status = "Queued" },
                    "Workbench evaluation queued."));
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "[WorkbenchRun] KeyNotFound after {ElapsedMs}ms: {Message}", sw.ElapsedMilliseconds, ex.Message);
                _logCollector.Add("WARN", $"[WorkbenchRun] KeyNotFound after {sw.ElapsedMilliseconds}ms: {ex.Message}");
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "[WorkbenchRun] InvalidOperation after {ElapsedMs}ms: {Message}", sw.ElapsedMilliseconds, ex.Message);
                _logCollector.Add("ERROR", $"[WorkbenchRun] InvalidOperation after {sw.ElapsedMilliseconds}ms: {ex.Message}\n{ex.StackTrace}");
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogError(ex, "[WorkbenchRun] CANCELLED/TIMEOUT after {ElapsedMs}ms", sw.ElapsedMilliseconds);
                _logCollector.Add("ERROR", $"[WorkbenchRun] CANCELLED/TIMEOUT after {sw.ElapsedMilliseconds}ms\n{ex.StackTrace}");
                return Ok(ApiResponseHandler.Fail<string>("Evaluation was cancelled or timed out."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WorkbenchRun] UNEXPECTED ERROR after {ElapsedMs}ms: {Message}", sw.ElapsedMilliseconds, ex.Message);
                _logCollector.Add("ERROR", $"[WorkbenchRun] UNEXPECTED ERROR after {sw.ElapsedMilliseconds}ms: {ex.Message}\n{ex.StackTrace}");
                return Ok(ApiResponseHandler.Fail<string>($"An unexpected error occurred: {ex.Message}"));
            }
        }

        // ── Workbench Run Status (Polling Endpoint) ──

        [HttpGet("workbench-run/{runId}/status")]
        public async Task<IActionResult> GetWorkbenchRunStatus(int runId)
        {
            var status = await _db.WorkbenchRunStatuses.FindAsync(runId);
            if (status == null)
                return Ok(ApiResponseHandler.Fail<string>("Run not found."));

            var meta = ExtractMetadataFromResultJson(status.ResultJson);

            // Fallback DB lookup for runs completed before names were stored in ResultJson
            var companyName = meta.CompanyName;
            var unitName    = meta.UnitName;
            if (companyName == null && meta.ProfileId.HasValue)
                companyName = await _db.Profiles
                    .Where(c => c.Id == (uint)meta.ProfileId.Value)
                    .Select(c => c.ProfileName)
                    .FirstOrDefaultAsync();
            if (unitName == null && meta.UnitId.HasValue)
                unitName = await _db.BusinessUnits
                    .Where(u => u.Id == (uint)meta.UnitId.Value)
                    .Select(u => u.Name)
                    .FirstOrDefaultAsync();

            object responsePayload;
            string responseMessage;

            if (status.Status == "Completed" && !string.IsNullOrEmpty(status.ResultJson))
            {
                var result = WorksheetMatcher.ExtractEvalResultFromResultJson(status.ResultJson);
                var worksheetCorpus = await _documentBatchesRepo.GetWorksheetFileContentsAsync();

                var publishedLog = await _db.InsightPublishLogs
                    .Where(x => x.RunId == runId)
                    .OrderByDescending(x => x.Id)
                    .FirstOrDefaultAsync();

                var shapedResult = BuildResultWithPerModelInsights(result, worksheetCorpus, publishedLog);
                responsePayload = new
                {
                    status.RunId,
                    status.Status,
                    status.CreatedAt,
                    status.CompletedAt,
                    meta.ProfileId,
                    CompanyName = companyName,
                    meta.UnitId,
                    UnitName = unitName,
                    meta.FromDate,
                    meta.ToDate,
                    Result = shapedResult,
                    InsightsPublished = publishedLog != null,
                    InsightsPublishedAt = publishedLog?.CreatedAt,
                    status.DetailedLogs
                };
                responseMessage = "Promptfoo evaluation completed.";
            }
            else
            {
                responsePayload = new
                {
                    status.RunId,
                    status.Status,
                    status.CreatedAt,
                    status.CompletedAt,
                    meta.ProfileId,
                    CompanyName = companyName,
                    meta.UnitId,
                    UnitName = unitName,
                    meta.FromDate,
                    meta.ToDate,
                    status.ErrorMessage,
                    status.DetailedLogs
                };
                responseMessage = $"Run status: {status.Status}";
            }

            return Ok(ApiResponseHandler.Success(responsePayload, responseMessage));
        }

        // ── Workbench History (Last 15 days) ──

        [HttpGet("workbench-history")]
        public async Task<IActionResult> GetWorkbenchHistory()
        {
            // Last 15 days from today (inclusive): start at 00:00 UTC of (today - 15 days),
            // end at 23:59:59.999 UTC of today.
            var todayUtc = DateTime.UtcNow.Date;
            var fromUtc = todayUtc.AddDays(-15);
            var toUtc = todayUtc.AddDays(1).AddTicks(-1);

            // Scheduled runs included. The reviewer email points admins at this
            // screen and names the Run ID of each generated report, so a run the
            // scheduler raised has to be findable here. It is the same kind of
            // run either way — only the trigger differs.
            var rows = await _db.WorkbenchRunStatuses
                .Where(r => r.CreatedAt >= fromUtc && r.CreatedAt <= toUtc)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            var history = rows.Select(r => new
            {
                r.RunId,
                r.Status,
                r.CreatedAt,
                r.CompletedAt,
                r.ErrorMessage,
                Result = r.Status == "Completed" && !string.IsNullOrEmpty(r.ResultJson)
                    ? WorksheetMatcher.ExtractEvalResultFromResultJson(r.ResultJson)
                    : null
            }).ToList();

            return Ok(ApiResponseHandler.Success(history, $"Workbench history for the last 15 days ({history.Count} record(s))."));
        }

        // ── Workbench Run List (Last 15 days, lightweight) ──
        // Returns only RunId, Status, CreatedAt, CompletedAt for the last 15 days.

        [HttpGet("workbench-run-list")]
        public async Task<IActionResult> GetWorkbenchRunList()
        {
            var todayUtc = DateTime.UtcNow.Date;
            var fromUtc = todayUtc.AddDays(-15);
            var toUtc = todayUtc.AddDays(1).AddTicks(-1);

            // Scheduled runs included, for the same reason as workbench-history.
            var rows = await _db.WorkbenchRunStatuses
                .Where(r => r.CreatedAt >= fromUtc && r.CreatedAt <= toUtc)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new
                {
                    r.RunId,
                    r.Status,
                    r.CreatedAt,
                    r.CompletedAt
                })
                .ToListAsync();

            return Ok(ApiResponseHandler.Success(rows, $"Workbench run list for the last 15 days ({rows.Count} record(s))."));
        }

        // ── Latest Run Insights ──
        // Returns the latest RunId together with its top themes, top actions,
        // and top recommendations from the most recent completed workbench run.

        [HttpGet("latest-run-insights")]
        public async Task<IActionResult> GetLatestRunInsights()
        {
            try
            {
                // "Latest" means the operator's latest, not the scheduler's —
                // otherwise a cadence run finishing overnight would replace what
                // the workbench screen last showed its user. Scheduled runs are
                // still listed by workbench-history and reachable by id; this is
                // the one place they are skipped.
                var latestRun = await _db.WorkbenchRunStatuses
                    .Where(r => r.Status == "Completed")
                    .Where(r => r.ResultJson == null ||
                                !r.ResultJson.Contains(WorkbenchRunSources.SchedulerJsonMarker))
                    .OrderByDescending(r => r.RunId)
                    .FirstOrDefaultAsync();

                if (latestRun == null)
                    return Ok(ApiResponseHandler.Fail<string>("No completed runs found."));

                var runId = latestRun.RunId;
                var meta = ExtractMetadataFromResultJson(latestRun.ResultJson);

                var companyName = meta.CompanyName;
                var unitName    = meta.UnitName;
                if (companyName == null && meta.ProfileId.HasValue)
                    companyName = await _db.Profiles
                        .Where(c => c.Id == (uint)meta.ProfileId.Value)
                        .Select(c => c.ProfileName)
                        .FirstOrDefaultAsync();
                if (unitName == null && meta.UnitId.HasValue)
                    unitName = await _db.BusinessUnits
                        .Where(u => u.Id == (uint)meta.UnitId.Value)
                        .Select(u => u.Name)
                        .FirstOrDefaultAsync();

                object responsePayload;
                string responseMessage;

                if (latestRun.Status == "Completed" && !string.IsNullOrEmpty(latestRun.ResultJson))
                {
                    var result = WorksheetMatcher.ExtractEvalResultFromResultJson(latestRun.ResultJson);
                    var worksheetCorpus = await _documentBatchesRepo.GetWorksheetFileContentsAsync();

                    var publishedLog = await _db.InsightPublishLogs
                        .Where(x => x.RunId == runId)
                        .OrderByDescending(x => x.Id)
                        .FirstOrDefaultAsync();

                    var shapedResult = BuildResultWithPerModelInsights(result, worksheetCorpus, publishedLog);
                    responsePayload = new
                    {
                        latestRun.RunId,
                        latestRun.Status,
                        latestRun.CreatedAt,
                        latestRun.CompletedAt,
                        meta.ProfileId,
                        CompanyName = companyName,
                        meta.UnitId,
                        UnitName = unitName,
                        meta.FromDate,
                        meta.ToDate,
                        Result = shapedResult,
                        InsightsPublished = publishedLog != null,
                        InsightsPublishedAt = publishedLog?.CreatedAt,
                        latestRun.DetailedLogs
                    };
                    responseMessage = "Promptfoo evaluation completed.";
                }
                else
                {
                    responsePayload = new
                    {
                        latestRun.RunId,
                        latestRun.Status,
                        latestRun.CreatedAt,
                        latestRun.CompletedAt,
                        meta.ProfileId,
                        CompanyName = companyName,
                        meta.UnitId,
                        UnitName = unitName,
                        meta.FromDate,
                        meta.ToDate,
                        latestRun.ErrorMessage,
                        latestRun.DetailedLogs
                    };
                    responseMessage = $"Run status: {latestRun.Status}";
                }

                return Ok(ApiResponseHandler.Success(responsePayload, responseMessage));
            }
            catch
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        private static (long? ProfileId, string? CompanyName, long? UnitId, string? UnitName, DateTimeOffset? FromDate, DateTimeOffset? ToDate)
            ExtractMetadataFromResultJson(string? resultJson)
        {
            if (string.IsNullOrEmpty(resultJson)) return default;
            try
            {
                using var doc = JsonDocument.Parse(resultJson);
                var root = doc.RootElement;

                long? profileId = TryGetPropertyCaseInsensitive(root, "CompanyId", out var cp) && cp.ValueKind != JsonValueKind.Null
                    ? cp.GetInt64() : null;
                string? companyName = TryGetPropertyCaseInsensitive(root, "CompanyName", out var cn) && cn.ValueKind == JsonValueKind.String
                    ? cn.GetString() : null;
                long? unitId = TryGetPropertyCaseInsensitive(root, "UnitId", out var up) && up.ValueKind != JsonValueKind.Null
                    ? up.GetInt64() : null;
                string? unitName = TryGetPropertyCaseInsensitive(root, "UnitName", out var un) && un.ValueKind == JsonValueKind.String
                    ? un.GetString() : null;
                DateTimeOffset? fromDate = TryGetPropertyCaseInsensitive(root, "FromDate", out var fd) && fd.ValueKind != JsonValueKind.Null
                    ? fd.GetDateTimeOffset() : null;
                DateTimeOffset? toDate = TryGetPropertyCaseInsensitive(root, "ToDate", out var td) && td.ValueKind != JsonValueKind.Null
                    ? td.GetDateTimeOffset() : null;

                return (profileId, companyName, unitId, unitName, fromDate, toDate);
            }
            catch { return default; }
        }

        // Reshapes the eval result so testResults is grouped by model. Each model
        // group contains its own list of test results followed by a topInsight
        // section (Top Themes / Top Recommendations / Top Actions) computed only
        // from that model's test responses.
        private static object? BuildResultWithPerModelInsights(
            PromptfooEvalResultDto? result,
            List<(string FileName, string Content)> worksheetCorpus,
            InsightPublishLog? publishedLog = null)
        {
            if (result == null)
                return null;

            // Group test results by model, preserving first-seen order.
            var groups = new Dictionary<string, (string ProviderName, string ModelName, List<PromptfooTestResultDto> Tests)>();
            var order = new List<string>();

            foreach (var test in result.TestResults ?? new List<PromptfooTestResultDto>())
            {
                var key = $"{test.ProviderName}|{test.ModelName}";
                if (!groups.TryGetValue(key, out var grp))
                {
                    grp = (test.ProviderName ?? "", test.ModelName ?? "", new List<PromptfooTestResultDto>());
                    groups[key] = grp;
                    order.Add(key);
                }
                grp.Tests.Add(test);
            }

            var modelResults = order.Select(k =>
            {
                var g = groups[k];
                var recommendedWorksheet = WorksheetMatcher.BuildWorksheetAnchor(WorksheetMatcher.MatchWorksheetFromDb(g.Tests, worksheetCorpus));
                var computedInsight = BuildTopInsight(g.Tests, recommendedWorksheet);
                var publishedInsight = publishedLog != null
                    ? BuildTopInsightFromPublishLog(publishedLog, recommendedWorksheet)
                    : null;
                return (object)new
                {
                    ProviderName = g.ProviderName,
                    ModelName = g.ModelName,
                    TestResults = g.Tests,
                    TopInsight = publishedInsight ?? computedInsight,
                    OriginalTopInsight = publishedInsight != null ? computedInsight : null
                };
            }).ToList();

            return new
            {
                result.PromptId,
                result.PromptName,
                result.Summary,
                ModelResults = modelResults,
                result.ShareUrl,
                result.DashboardUrl
            };
        }

        private static object BuildTopInsightFromPublishLog(InsightPublishLog log, string? recommendedWorksheet = null)
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var themes = TryDeserializeStringList(log.TopThemes, opts);
            var actions = TryDeserializeStringList(log.TopActions, opts);
            var recs   = TryDeserializeStringList(log.TopRecommendations, opts);
            return new { TopThemes = themes, TopRecommendations = recs, TopActions = actions, RecommendedWorksheet = recommendedWorksheet };
        }

        private static List<string> TryDeserializeStringList(string json, JsonSerializerOptions opts)
        {
            try { return JsonSerializer.Deserialize<List<string>>(json, opts) ?? new(); }
            catch { return new(); }
        }

        // Parses the LLM `response` JSON from each test result in the supplied
        // list and aggregates the report's top themes, top recommendations, and
        // a flattened list of top actions (collected from each recommendation's
        // action_steps). Each list is a flat array of strings.
        private static object BuildTopInsight(List<PromptfooTestResultDto> tests, string? recommendedWorksheet = null)
        {
            var (themes, actions, recs) = ExtractInsightListsFromTests(tests);
            return new { TopThemes = themes, TopRecommendations = recs, TopActions = actions, RecommendedWorksheet = recommendedWorksheet };
        }

        // Parsing lives in WorkbenchInsightExtractor, which the report scheduler
        // also calls — the two used to carry separate copies of this logic and
        // produced different text for the same engine output. Argument order is
        // kept (themes, actions, recommendations) for the callers here.
        private static (List<string> TopThemes, List<string> TopActions, List<string> TopRecommendations)
            ExtractInsightListsFromTests(List<PromptfooTestResultDto> tests)
        {
            var (themes, recommendations, actions) = WorkbenchInsightExtractor.Extract(tests);
            return (themes, actions, recommendations);
        }

        // ── Convert Workbench Run to Report ──

        /*
        [HttpPost("workbench-to-report")]
        public async Task<IActionResult> ConvertWorkbenchToReport([FromBody] WorkbenchToReportRequestDto dto)
        {
            if (dto.RunId <= 0)
                return Ok(ApiResponseHandler.Fail<string>("RunId is required."));

            var runStatus = await _db.WorkbenchRunStatuses.FindAsync(dto.RunId);
            if (runStatus == null)
                return Ok(ApiResponseHandler.Fail<string>("Workbench run not found."));
            if (runStatus.Status != "Completed" || string.IsNullOrEmpty(runStatus.ResultJson))
                return Ok(ApiResponseHandler.Fail<string>($"Workbench run is not completed (status: {runStatus.Status})."));

            var evalResult = ExtractEvalResultFromResultJson(runStatus.ResultJson);
            if (evalResult == null)
                return Ok(ApiResponseHandler.Fail<string>("Failed to parse workbench result JSON."));

            var allTests = evalResult.TestResults ?? new List<PromptfooTestResultDto>();

            // Group tests by model, pick the single best model (highest average score,
            // then highest pass rate as tie-break), and save only that model's insights.
            var bestGroup = allTests
                .Where(t => t != null)
                .GroupBy(t => $"{t.ProviderName}|{t.ModelName}")
                .Select(g =>
                {
                    var tests   = g.ToList();
                    var avgScore = tests.Count > 0 ? tests.Average(t => t.Score) : 0;
                    var passRate = tests.Count > 0 ? tests.Average(t => t.Pass ? 1.0 : 0.0) : 0;
                    return new { Tests = tests, AvgScore = avgScore, PassRate = passRate };
                })
                .OrderByDescending(g => g.AvgScore)
                .ThenByDescending(g => g.PassRate)
                .FirstOrDefault();

            if (bestGroup == null || bestGroup.Tests.Count == 0)
                return Ok(ApiResponseHandler.Fail<string>("No test results found in workbench run."));

            var topInsight = WorkbenchInsightExtractor.Extract(bestGroup.Tests);

            if (!topInsight.TopThemes.Any() && !topInsight.TopRecommendations.Any() && !topInsight.TopActions.Any())
                return Ok(ApiResponseHandler.Fail<string>($"Against Run ID {dto.RunId}, the model has not generated Top Insights."));

            var (unitId, fromDate, toDate) = ExtractMetaFromResultJson(runStatus);

            var worksheetCorpus = await _documentBatchesRepo.GetWorksheetFileContentsAsync();
            var worksheetFile   = MatchWorksheetFromDb(bestGroup.Tests, worksheetCorpus);

            var report = new LLMReport
            {
                FromDate      = fromDate,
                ToDate        = toDate,
                UnitId        = unitId,
                WorksheetFile = worksheetFile,
                CreatedAt     = DateTime.UtcNow
            };
            _db.LLMReports.Add(report);
            await _db.SaveChangesAsync();

            if (topInsight.TopThemes.Any())
            {
                await _db.TopThemes.AddRangeAsync(topInsight.TopThemes.Select(t => new TopTheme
                {
                    UnitId    = (uint)unitId,
                    Theme     = t,
                    ReportId  = report.Id,
                    CreatedAt = DateTime.UtcNow
                }));
            }

            if (topInsight.TopRecommendations.Any())
            {
                await _db.TopRecommendations.AddRangeAsync(topInsight.TopRecommendations.Select(r => new TopRecommendation
                {
                    UnitId         = (uint)unitId,
                    Recommendation = r,
                    ReportId       = report.Id,
                    CreatedAt      = DateTime.UtcNow
                }));
            }

            if (topInsight.TopActions.Any())
            {
                await _db.TopActions.AddRangeAsync(topInsight.TopActions.Select(a => new TopAction
                {
                    UnitId    = (uint)unitId,
                    Action    = a,
                    ReportId  = report.Id,
                    CreatedAt = DateTime.UtcNow
                }));
            }

            _db.InsightPublishLogs.Add(new InsightPublishLog
            {
                ReportId           = report.Id,
                RunId              = dto.RunId,
                UserId             = _tokenReader.GetUserIdFromToken(),
                DateFrom           = report.FromDate,
                DateTo             = report.ToDate,
                TopThemes          = System.Text.Json.JsonSerializer.Serialize(topInsight.TopThemes),
                TopActions         = System.Text.Json.JsonSerializer.Serialize(topInsight.TopActions),
                TopRecommendations = System.Text.Json.JsonSerializer.Serialize(topInsight.TopRecommendations),
                CreatedAt          = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            return Ok(ApiResponseHandler.Success(new
            {
                ReportId                = report.Id,
                RunId                   = dto.RunId,
                UnitId                  = unitId,
                SelectedModel           = bestGroup.Tests.FirstOrDefault()?.ModelName,
                TopThemesCount          = topInsight.TopThemes.Count,
                TopRecommendationsCount = topInsight.TopRecommendations.Count,
                TopActionsCount         = topInsight.TopActions.Count
            }, "Top insights saved successfully."));
        }
        */

        // Reads UnitId, FromDate, ToDate from the enriched ResultJson wrapper written
        // by the background service: { UnitId, ProfileId, FromDate, ToDate, Result: {...} }.
        // Falls back to run timestamps when fields are absent (older runs without wrapper).
        private static (long UnitId, DateTime FromDate, DateTime ToDate) ExtractMetaFromResultJson(
            WorkbenchRunStatus runStatus)
        {
            long unitId = 0;
            DateTime? fromDate = null;
            DateTime? toDate   = null;

            try
            {
                using var doc = JsonDocument.Parse(runStatus.ResultJson!);
                var root = doc.RootElement;

                if (TryGetPropertyCaseInsensitive(root, "UnitId", out var unitEl) &&
                    unitEl.ValueKind != JsonValueKind.Null)
                    long.TryParse(unitEl.GetRawText().Trim('"'), out unitId);

                if (TryGetPropertyCaseInsensitive(root, "FromDate", out var fromEl) &&
                    fromEl.ValueKind != JsonValueKind.Null &&
                    DateTime.TryParse(fromEl.GetString(), null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var f))
                    fromDate = f.ToUniversalTime();

                if (TryGetPropertyCaseInsensitive(root, "ToDate", out var toEl) &&
                    toEl.ValueKind != JsonValueKind.Null &&
                    DateTime.TryParse(toEl.GetString(), null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var t))
                    toDate = t.ToUniversalTime();
            }
            catch { }

            return (
                unitId,
                fromDate ?? runStatus.CreatedAt,
                toDate   ?? runStatus.CompletedAt ?? DateTime.UtcNow
            );
        }

        private static bool TryGetPropertyCaseInsensitive(JsonElement element, string name, out JsonElement value)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }

        // ── Get Top Insights by Report ID ──

        /*
        [HttpGet("report/{reportId}/top-insights")]
        public async Task<IActionResult> GetTopInsightsByReportId(long reportId)
        {
            if (reportId <= 0)
                return Ok(ApiResponseHandler.Fail<string>("Invalid report id."));

            var report = await _db.LLMReports.FindAsync(reportId);
            if (report == null)
                return Ok(ApiResponseHandler.Fail<string>("Report not found."));

            var themes = await _db.TopThemes
                .Where(t => t.ReportId == reportId)
                .OrderBy(t => t.Id)
                .Select(t => t.Theme)
                .ToListAsync();

            var recommendations = await _db.TopRecommendations
                .Where(r => r.ReportId == reportId)
                .OrderBy(r => r.Id)
                .Select(r => r.Recommendation)
                .ToListAsync();

            var actions = await _db.TopActions
                .Where(a => a.ReportId == reportId)
                .OrderBy(a => a.Id)
                .Select(a => a.Action)
                .ToListAsync();

            var result = new TopInsightsResponseDto
            {
                ReportId           = reportId,
                TopThemes          = themes,
                TopRecommendations = recommendations,
                TopActions         = actions
            };

            return Ok(ApiResponseHandler.Success(result, "Top insights fetched successfully."));
        }
        */

        // ── Edit Top Insights by Report ID ──

        /*
        [HttpPut("report/{reportId}/top-insights")]
        public async Task<IActionResult> EditTopInsightsByReportId(long reportId, [FromBody] EditTopInsightsRequestDto dto)
        {
            if (reportId <= 0)
                return Ok(ApiResponseHandler.Fail<string>("Invalid report id."));

            if (dto == null)
                return Ok(ApiResponseHandler.Fail<string>("Request body is required."));

            // Validate no empty values inside lists
            if (dto.TopThemes?.Any(t => string.IsNullOrWhiteSpace(t)) == true)
                return Ok(ApiResponseHandler.Fail<string>("TopThemes contains one or more empty values."));

            if (dto.TopRecommendations?.Any(r => string.IsNullOrWhiteSpace(r)) == true)
                return Ok(ApiResponseHandler.Fail<string>("TopRecommendations contains one or more empty values."));

            if (dto.TopActions?.Any(a => string.IsNullOrWhiteSpace(a)) == true)
                return Ok(ApiResponseHandler.Fail<string>("TopActions contains one or more empty values."));

            var report = await _db.LLMReports.FindAsync(reportId);
            if (report == null)
                return Ok(ApiResponseHandler.Fail<string>("Report not found."));

            // Remove existing entries for this report
            _db.TopThemes.RemoveRange(_db.TopThemes.Where(t => t.ReportId == reportId));
            _db.TopRecommendations.RemoveRange(_db.TopRecommendations.Where(r => r.ReportId == reportId));
            _db.TopActions.RemoveRange(_db.TopActions.Where(a => a.ReportId == reportId));

            // Save new entries
            if (dto.TopThemes?.Any() == true)
            {
                await _db.TopThemes.AddRangeAsync(dto.TopThemes.Select(t => new TopTheme
                {
                    UnitId    = (uint)report.UnitId,
                    Theme     = t.Trim(),
                    ReportId  = reportId,
                    CreatedAt = DateTime.UtcNow
                }));
            }

            if (dto.TopRecommendations?.Any() == true)
            {
                await _db.TopRecommendations.AddRangeAsync(dto.TopRecommendations.Select(r => new TopRecommendation
                {
                    UnitId         = (uint)report.UnitId,
                    Recommendation = r.Trim(),
                    ReportId       = reportId,
                    CreatedAt      = DateTime.UtcNow
                }));
            }

            if (dto.TopActions?.Any() == true)
            {
                await _db.TopActions.AddRangeAsync(dto.TopActions.Select(a => new TopAction
                {
                    UnitId    = (uint)report.UnitId,
                    Action    = a.Trim(),
                    ReportId  = reportId,
                    CreatedAt = DateTime.UtcNow
                }));
            }

            _db.InsightPublishLogs.Add(new InsightPublishLog
            {
                ReportId           = reportId,
                RunId              = 0,
                UserId             = _tokenReader.GetUserIdFromToken(),
                DateFrom           = report.FromDate,
                DateTo             = report.ToDate,
                TopThemes          = System.Text.Json.JsonSerializer.Serialize(dto.TopThemes          ?? new List<string>()),
                TopActions         = System.Text.Json.JsonSerializer.Serialize(dto.TopActions         ?? new List<string>()),
                TopRecommendations = System.Text.Json.JsonSerializer.Serialize(dto.TopRecommendations ?? new List<string>()),
                CreatedAt          = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            return Ok(ApiResponseHandler.Success(new
            {
                ReportId                = reportId,
                TopThemesCount          = dto.TopThemes?.Count ?? 0,
                TopRecommendationsCount = dto.TopRecommendations?.Count ?? 0,
                TopActionsCount         = dto.TopActions?.Count ?? 0
            }, "Top insights updated successfully."));
        }
        */
    }
}
