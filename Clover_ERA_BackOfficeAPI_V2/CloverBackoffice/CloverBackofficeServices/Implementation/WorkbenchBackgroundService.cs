using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    // ── Work item queued by the controller ──
    public class PromptfooWorkItem
    {
        public int RunId { get; set; }
        public WorkbenchRunRequestDto Request { get; set; } = null!;
        public List<int> ModelIds { get; set; } = new();
        public byte[]? TestCaseFileBytes { get; set; }
        public string? TestCaseFileName { get; set; }
        public string? TestCaseContentType { get; set; }
    }

    // ── Singleton channel wrapper ──
    public class PromptfooWorkQueue
    {
        private readonly Channel<PromptfooWorkItem> _channel =
            Channel.CreateBounded<PromptfooWorkItem>(new BoundedChannelOptions(20)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

        public ChannelWriter<PromptfooWorkItem> Writer => _channel.Writer;
        public ChannelReader<PromptfooWorkItem> Reader => _channel.Reader;
    }

    // ── Background processor ──
    public class PromptfooBackgroundService : BackgroundService
    {
        private readonly PromptfooWorkQueue _queue;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PromptfooBackgroundService> _logger;

        public PromptfooBackgroundService(
            PromptfooWorkQueue queue,
            IServiceProvider serviceProvider,
            ILogger<PromptfooBackgroundService> logger)
        {
            _queue = queue;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[PromptfooBackground] Service started.");

            await foreach (var workItem in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessWorkItemAsync(workItem, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[PromptfooBackground] Unhandled error for RunId={RunId}", workItem.RunId);
                }
            }
        }

        private async Task ProcessWorkItemAsync(PromptfooWorkItem workItem, CancellationToken ct)
        {
            _logger.LogInformation("[PromptfooBackground] Processing RunId={RunId}", workItem.RunId);

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var promptService = scope.ServiceProvider.GetRequiredService<IPromptManagementService>();
            var logCollector = scope.ServiceProvider.GetRequiredService<ILogCollector>();

            // Mark as Running
            var status = await db.WorkbenchRunStatuses.FindAsync(workItem.RunId);
            if (status == null)
            {
                _logger.LogWarning("[PromptfooBackground] RunId={RunId} not found in DB, skipping.", workItem.RunId);
                return;
            }
            status.Status = "Running";
            await db.SaveChangesAsync(ct);

            try
            {
                // Reconstruct IFormFile from saved byte array
                IFormFile? formFile = null;
                if (workItem.TestCaseFileBytes != null && workItem.TestCaseFileBytes.Length > 0)
                {
                    var ms = new MemoryStream(workItem.TestCaseFileBytes);
                    formFile = new FormFile(ms, 0, ms.Length, "testCaseFile", workItem.TestCaseFileName ?? "upload.csv")
                    {
                        Headers = new HeaderDictionary(),
                        ContentType = workItem.TestCaseContentType ?? "application/octet-stream"
                    };
                }

                var directResult = await promptService.RunWorkbenchAsync(
                    workItem.Request, workItem.ModelIds, formFile, ct);
                PromptfooEvalResultDto evalResult = ConvertToPromptfooFormat(directResult);

                string? companyName = null;
                string? unitName = null;
                if (workItem.Request.ProfileId.HasValue)
                    companyName = await db.Profiles
                        .Where(c => c.Id == (uint)workItem.Request.ProfileId.Value)
                        .Select(c => c.ProfileName)
                        .FirstOrDefaultAsync(ct);
                if (workItem.Request.UnitId.HasValue)
                    unitName = await db.BusinessUnits
                        .Where(u => u.Id == (uint)workItem.Request.UnitId.Value)
                        .Select(u => u.Name)
                        .FirstOrDefaultAsync(ct);

                status.Status = "Completed";
                status.ResultJson = JsonSerializer.Serialize(new
                {
                    UnitId      = workItem.Request.UnitId,
                    UnitName    = unitName,
                    ProfileId   = workItem.Request.ProfileId,
                    CompanyName = companyName,
                    FromDate    = workItem.Request.FromDate,
                    ToDate      = workItem.Request.ToDate,
                    Result      = evalResult
                });
                status.CompletedAt = DateTime.UtcNow;
                status.DetailedLogs = logCollector.HasEntries ? logCollector.GetAll() : null;

                _logger.LogInformation("[PromptfooBackground] RunId={RunId} COMPLETED successfully.", workItem.RunId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PromptfooBackground] RunId={RunId} FAILED: {Message}", workItem.RunId, ex.Message);
                status.Status = "Failed";
                status.ErrorMessage = ex.Message;
                status.CompletedAt = DateTime.UtcNow;
                status.DetailedLogs = logCollector.HasEntries ? logCollector.GetAll() : null;
            }

            await db.SaveChangesAsync(ct);
        }

        private static PromptfooEvalResultDto ConvertToPromptfooFormat(WorkbenchRunResultDto direct)
        {
            var testResults = direct.Results.Select(item =>
            {
                var pass = string.IsNullOrEmpty(item.Error) &&
                           (!item.Assertions.Any() || item.Assertions.All(a => a.Passed));

                return new PromptfooTestResultDto
                {
                    TestCaseName      = item.TestCaseName,
                    ProviderName      = item.Provider,
                    ModelName         = item.ModelName,
                    Response          = item.Response ?? "",
                    RenderedPrompt    = "",
                    Pass              = pass,
                    Score             = item.Assertions.Any()
                                            ? item.Assertions.Average(a => a.Passed ? 1.0 : 0.0)
                                            : (string.IsNullOrEmpty(item.Error) ? 1.0 : 0.0),
                    LatencyMs         = item.LatencyMs,
                    PromptTokens      = item.PromptTokens,
                    CompletionTokens  = item.CompletionTokens,
                    TotalTokens       = item.TotalTokens,
                    EstimatedCost     = 0,
                    Variables         = new Dictionary<string, string>(),
                    Error             = item.Error,
                    Assertions        = item.Assertions.Select(a => new PromptfooAssertionResultDto
                    {
                        Type     = a.AssertionType,
                        Pass     = a.Passed,
                        Score    = a.Passed ? 1.0 : 0.0,
                        Expected = a.ExpectedValue,
                        Actual   = a.ActualValue,
                        Reason   = a.Name
                    }).ToList()
                };
            }).ToList();

            return new PromptfooEvalResultDto
            {
                PromptId   = direct.PromptId,
                PromptName = direct.PromptName,
                Summary    = new PromptfooEvalSummaryDto
                {
                    TotalTests            = testResults.Count,
                    TotalPassed           = testResults.Count(t => t.Pass),
                    TotalFailed           = testResults.Count(t => !t.Pass && string.IsNullOrEmpty(t.Error)),
                    TotalErrors           = testResults.Count(t => !string.IsNullOrEmpty(t.Error)),
                    PassRate              = testResults.Count > 0
                                               ? (double)testResults.Count(t => t.Pass) / testResults.Count * 100
                                               : 0,
                    TotalEstimatedCost    = 0,
                    TotalPromptTokens     = testResults.Sum(t => t.PromptTokens),
                    TotalCompletionTokens = testResults.Sum(t => t.CompletionTokens),
                    TotalTokens           = testResults.Sum(t => t.TotalTokens)
                },
                TestResults  = testResults,
                ShareUrl     = null,
                DashboardUrl = null
            };
        }
    }
}
