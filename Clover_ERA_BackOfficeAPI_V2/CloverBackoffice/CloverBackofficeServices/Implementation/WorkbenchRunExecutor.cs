using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    // The single implementation of "run the workbench engine for this run row".
    // Moved verbatim out of CloverEraBackgroundService.ProcessWorkItemAsync so
    // the manual and scheduled paths cannot drift: same engine call, same
    // response-JSON shape, same ResultJson wrapper, same log capture.
    public class WorkbenchRunExecutor : IWorkbenchRunExecutor
    {
        private readonly ApplicationDbContext _db;
        private readonly ICloverEraEngineService _engineService;
        private readonly IDocumentEmbeddingRepository _embeddingRepo;
        private readonly ILogCollector _logCollector;
        private readonly ILogger<WorkbenchRunExecutor> _logger;

        public WorkbenchRunExecutor(
            ApplicationDbContext db,
            ICloverEraEngineService engineService,
            IDocumentEmbeddingRepository embeddingRepo,
            ILogCollector logCollector,
            ILogger<WorkbenchRunExecutor> logger)
        {
            _db = db;
            _engineService = engineService;
            _embeddingRepo = embeddingRepo;
            _logCollector = logCollector;
            _logger = logger;
        }

        public async Task<bool> ExecuteAsync(CloverEraWorkItem workItem, CancellationToken ct = default)
        {
            _logger.LogInformation("[WorkbenchRunExecutor] Processing RunId={RunId}", workItem.RunId);

            var modelId = workItem.Request.ModelIds?.Count > 0 ? workItem.Request.ModelIds[0] : 0;
            var modelConfig = modelId > 0 ? await _embeddingRepo.GetModelByIdAsync(modelId) : null;
            var modelName = modelConfig?.ModelName ?? "";

            var status = await _db.WorkbenchRunStatuses.FindAsync(new object[] { workItem.RunId }, ct);
            if (status == null)
            {
                _logger.LogWarning("[WorkbenchRunExecutor] RunId={RunId} not found in DB, skipping.", workItem.RunId);
                return false;
            }

            status.Status = "Running";
            await _db.SaveChangesAsync(ct);

            var completed = false;

            try
            {
                IFormFile? formFile = null;
                if (workItem.CsvFileBytes != null && workItem.CsvFileBytes.Length > 0)
                {
                    var ms = new MemoryStream(workItem.CsvFileBytes);
                    formFile = new FormFile(ms, 0, ms.Length, "csvFile", workItem.CsvFileName ?? "upload.csv")
                    {
                        Headers = new HeaderDictionary(),
                        ContentType = workItem.CsvContentType ?? "text/csv"
                    };
                }

                var report = await _engineService.RunEngineAsync(workItem.Request, formFile!, ct);

                // Build the response JSON in the shape BuildTopInsight expects
                var responseJson = JsonSerializer.Serialize(new
                {
                    top_themes = report.Themes.Select(t => new
                    {
                        text = string.IsNullOrWhiteSpace(t.Text)
                            ? t.Headline
                            : $"{t.Headline}. {t.Text}"
                    }),
                    top_recommendations = report.Recommendations.Select(r => new { text = r.Text }),
                    action_steps = (report.WorksheetAction != null && report.WorksheetAction.WorksheetCode != "NONE_FOUND"
                        ? new[] { new { text = $"{report.WorksheetAction.WorksheetTitle} ({report.WorksheetAction.DurationMinutes} min): {report.WorksheetAction.Text}" } }
                            .Concat(report.MicroActions.Select(a => new { text = a.Text }))
                        : report.MicroActions.Select(a => new { text = a.Text }))
                        .ToArray()
                });

                var evalResult = new PromptfooEvalResultDto
                {
                    PromptId = workItem.Request.PromptId,
                    PromptName = "",
                    Summary = new PromptfooEvalSummaryDto
                    {
                        TotalTests = 1,
                        TotalPassed = 1,
                        TotalFailed = 0,
                        TotalErrors = 0,
                        PassRate = 100,
                        TotalEstimatedCost = 0,
                        TotalPromptTokens = 0,
                        TotalCompletionTokens = 0,
                        TotalTokens = 0
                    },
                    TestResults = new List<PromptfooTestResultDto>
                    {
                        new PromptfooTestResultDto
                        {
                            TestCaseName     = "CloverEra Report",
                            ProviderName     = "CloverEra Engine",
                            ModelName        = modelName,
                            Response         = responseJson,
                            RenderedPrompt   = "",
                            Pass             = true,
                            Score            = 1.0,
                            LatencyMs        = 0,
                            PromptTokens     = 0,
                            CompletionTokens = 0,
                            TotalTokens      = 0,
                            EstimatedCost    = 0,
                            Variables        = new Dictionary<string, string>(),
                            Error            = null,
                            Assertions       = new List<PromptfooAssertionResultDto>()
                        }
                    },
                    ShareUrl     = null,
                    DashboardUrl = null
                };

                string? companyName = null;
                string? unitName = null;
                if (workItem.Request.ProfileId.HasValue)
                    companyName = await _db.Profiles
                        .Where(c => c.Id == (uint)workItem.Request.ProfileId.Value)
                        .Select(c => c.ProfileName)
                        .FirstOrDefaultAsync(ct);
                if (workItem.Request.UnitId.HasValue)
                    unitName = await _db.BusinessUnits
                        .Where(u => u.Id == (uint)workItem.Request.UnitId.Value)
                        .Select(u => u.Name)
                        .FirstOrDefaultAsync(ct);

                status.Status = "Completed";
                status.ResultJson = JsonSerializer.Serialize(new
                {
                    // First field on purpose: the workbench list endpoints match
                    // it as a substring, and every reader of this wrapper looks
                    // properties up by name, so adding one changes nothing for
                    // them.
                    Source      = string.IsNullOrWhiteSpace(workItem.Source)
                                      ? WorkbenchRunSources.Workbench
                                      : workItem.Source,
                    UnitId      = workItem.Request.UnitId,
                    UnitName    = unitName,
                    ProfileId   = workItem.Request.ProfileId,
                    CompanyName = companyName,
                    FromDate    = workItem.Request.FromDate,
                    ToDate      = workItem.Request.ToDate,
                    Result      = evalResult
                });
                status.CompletedAt = DateTime.UtcNow;
                status.DetailedLogs = _logCollector.HasEntries ? _logCollector.GetAll() : null;

                completed = true;
                _logger.LogInformation("[WorkbenchRunExecutor] RunId={RunId} COMPLETED successfully.", workItem.RunId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WorkbenchRunExecutor] RunId={RunId} FAILED: {Message}", workItem.RunId, ex.Message);
                status.Status = "Failed";
                status.ErrorMessage = ex.Message;
                status.CompletedAt = DateTime.UtcNow;
                status.DetailedLogs = _logCollector.HasEntries ? _logCollector.GetAll() : null;
            }

            await _db.SaveChangesAsync(ct);
            return completed;
        }
    }
}
