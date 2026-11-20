using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SendGrid.Helpers.Mail;
using SharpToken;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
namespace CloverBackoffice.Services.Implementation
{
    public class RagService : IRagService
    {
        private readonly IEmbeddingRepository _emb;
        private readonly ISearchRepository _search;
        private readonly IHttpClientFactory _httpFactory;
        private readonly IDocumentEmbeddingRepository _docEmbeddingRepository;
        private readonly IRateLimiterService _rateLimiter;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ApplicationDbContext _context;
        private readonly IFeedbackRepository _repository;
        private readonly IAuditLogsService _auditLogsService;
        private readonly ILiteLLMService _liteLLMService;
        private readonly LegacyReportPathOptions _legacyReportPaths;

        private ModelConfiguration? _cachedBatchModel;

        public RagService(IEmbeddingRepository emb, ISearchRepository search, IHttpClientFactory httpFactory, IDocumentEmbeddingRepository docEmbeddingRepository, IRateLimiterService rateLimiter, IHttpContextAccessor httpContextAccessor, ApplicationDbContext context, IFeedbackRepository repository, IAuditLogsService auditLogsService, ILiteLLMService liteLLMService, IOptions<LegacyReportPathOptions> legacyReportPaths)
        {
            _legacyReportPaths = legacyReportPaths?.Value ?? new LegacyReportPathOptions();
            _emb = emb;
            _search = search;
            _httpFactory = httpFactory;
            _docEmbeddingRepository = docEmbeddingRepository;
            _rateLimiter = rateLimiter;
            _httpContextAccessor = httpContextAccessor;
            _context = context;
            _repository = repository;
            _auditLogsService = auditLogsService;
            _liteLLMService = liteLLMService;
        }

        private async Task<ModelConfiguration> GetBatchModelAsync()
        {
            _cachedBatchModel ??= await _docEmbeddingRepository.GetDefaultModelAsync(modelConfigTypeId: 2)
                ?? throw new InvalidOperationException("No default Batch-model found in DB.");
            return _cachedBatchModel;
        }

        private HttpClient CreateClientForModel(ModelConfiguration config)
        {
            var client = _httpFactory.CreateClient();
            var baseUri = new Uri(config.Endpoint.TrimEnd('/') + "/");
            client.BaseAddress = baseUri;
            client.DefaultRequestHeaders.Add("api-key", config.ApiKey);
            return client;
        }
        public async Task<string> AnswerAsync(string query, int topK = 5, CancellationToken ct = default)
        {
            var qVec = await _emb.GetEmbeddingAsync(query, ct);
            var docs = await _search.VectorSearchAsync(qVec, topK, ct);

            // Split docs into batches (3 docs per request)
            int batchSize = 3;
            var batches = docs.Select((d, i) => new { d, i })
                              .GroupBy(x => x.i / batchSize)
                              .Select(g => g.Select(x => x.d).ToList())
                              .ToList();

            var partialAnswers = new List<string>();

            foreach (var batch in batches)
            {
                var sb = new StringBuilder();

                sb.AppendLine(@"
                        You are an assistant that MUST return ONLY valid JSON.
                        You are analyzing employee engagement data.
                        Use the documents below, the Azure input and output templates, and the schema definition.

                        Your output must be a valid JSON object following this structure:

                        {
                          ""TopThemes"": [""string""],
                          ""TopRecommendations"": [""string""],
                          ""TopActions"": [""string""],
                          ""AzureOutput"": { ... }  // Follows Azure_optimized_constrained_OUTPUT_v4 format
                        }

                        No explanations, no markdown, ONLY JSON.

                        ### AZURE_optimized_constrained_INPUT_v4 ###
                        {
                          ""VERSION"": ""AZURE_3.0_ULTRA_CONSTRAINED"",
                          ""TASK"": ""Analyze employee engagement data. Output 1 priority element, maximum 2 themes, and EXACTLY 2 actions."",
                          ""DATA_ANALYSIS"": {
                            ""input_file"": ""xlsx with columns: Clover Category, QUESTION, Answer, Reason"",
                            ""sentiment_scoring"": { ""positive"": ""60-100"", ""neutral"": ""40-59"", ""needs_work"": ""0-39"" },
                            ""priority_element"": ""The CLOVER element with the lowest average sentiment score"",
                            ""themes"": ""Maximum 2 themes with sentiment scores under 40%"",
                            ""quotes"": ""2 anonymized quotes per theme, 6-18 words each""
                          },
                          ""OUTPUT_EXAMPLE"": {
                            ""priority_element"": {""name"": ""Reflection"", ""sentiment_score"": 41.0},
                            ""themes_requiring_attention"": [
                              {""theme_label"": ""Days lack meaning"", ""sentiment_score"": 25.0},
                              {""theme_label"": ""Distraction overwhelm"", ""sentiment_score"": 28.0}
                            ],
                            ""action_plan"": {
                              ""action_1_worksheet"": {
                                ""title"": ""Complete Reflection 1: The Reflection Hour"",
                                ""worksheet_identifier"": ""Reflection 1"",
                                ""worksheet_verification"": {
                                  ""alternatives_considered"": [
                                    {""identifier"": ""Reflection 9"", ""actual_title"": ""..."", ""score"": 8.5, ""why_not_selected"": ""...""},
                                    {""identifier"": ""Reflection 6"", ""actual_title"": ""..."", ""score"": 7.0, ""why_not_selected"": ""...""}
                                  ]
                                }
                              },
                              ""action_2_micro"": {
                                ""title"": ""End-of-Day Done List"",
                                ""duration_minutes"": 5
                              }
                            }
                          }
                        }

                ### AZURE_optimized_constrained_OUTPUT_v4 ###
                {
                  ""priority_element"": { ""name"": ""string"", ""sentiment_score"": ""number"", ""confidence"": ""number"" },
                  ""why_this_matters"": {
                    ""primary_reason"": ""string"",
                    ""supporting_evidence"": [""string"", ""string"", ""string""],
                    ""impact_if_ignored"": ""string""
                  },
                  ""themes_requiring_attention"": [
                    {
                      ""theme_label"": ""string"",
                      ""supporting_responses"": ""integer"",
                      ""sentiment_score"": ""number"",
                      ""related_elements"": [""string""],
                      ""example_quotes"": [""string"", ""string""]
                    }
                  ],
                  ""action_plan"": {
                    ""action_1_worksheet"": { ""title"": ""Complete [Element] [Number]: [Title]"", ""duration_minutes"": 30 },
                    ""action_2_micro"": { ""title"": ""string"", ""duration_minutes"": ""integer"" }
                  }
                }

                ### DOCUMENTS ###
                ");

                foreach (var d in batch)
                {
                    var content = d.Content.Length > 3000 ? d.Content.Substring(0, 3000) + "..." : d.Content;
                    sb.AppendLine($"Doc ID: {d.Id}");
                    sb.AppendLine(content);
                }

                sb.AppendLine($"\nQuestion: {query}");
                sb.AppendLine("\nReturn ONLY JSON following the schema shown above.");

                //  var partial = await CallChatCompletionAsync(sb.ToString(), ct);
                var partial = await CallChatCompletionViaLiteLLMAsync(sb.ToString(), ct);
                partialAnswers.Add(partial);
            }

            // Combine partial answers into one JSON
            var finalPrompt = $@"
                You are an assistant that MUST return ONLY valid JSON.
                Here are several partial JSON answers for the same question:
                {string.Join("\n---\n", partialAnswers)}

                Merge them into ONE valid JSON object that includes:
                - TopThemes
                - TopRecommendations
                - TopActions
                - AzureOutput (matching Azure_optimized_constrained_OUTPUT_v4)

                Return ONLY JSON. No explanations, no markdown.
                ";

          
            //return await CallChatCompletionAsync(finalPrompt, ct);  //old logic
            return await CallChatCompletionViaLiteLLMAsync(finalPrompt, ct);
        }
        private async Task<string> CallChatCompletionViaLiteLLMAsync(string userContent, CancellationToken ct)
        {
            int tokenCount = CountTokens(userContent);
            await _rateLimiter.ThrottleAsync(tokenCount); // Keep your rate limiting

            // Build the final prompt string for logging
            var messages = new[]
            {
                 new { role = "system", content = "You are a helpful assistant." },
                 new { role = "user", content = userContent }
             };
            var finalPrompt = string.Join("\n", messages.Select(m => $"{m.role}: {m.content}"));

            // Save into HttpContext so middleware can pick it later
            _httpContextAccessor.HttpContext.Items["FinalPrompt"] = finalPrompt;

            try
            {
                // Call LiteLLM instead of direct Azure
                var systemPrompt = "You are a helpful assistant.";
                var response = await _liteLLMService.GetChatCompletionAsync(systemPrompt, userContent, ct);

                return response;
            }
            catch (Exception ex)
            {
                throw;
            }
        }
        

        private async Task<string> CallChatCompletionAsyncoldworking(string userContent, CancellationToken ct)
        {
            int tokenCount = CountTokens(userContent);
            await _rateLimiter.ThrottleAsync(tokenCount); //  request+token aware limiter

            var searchModel = await _docEmbeddingRepository.GetDefaultModelAsync(modelConfigTypeId: 1)
                ?? throw new InvalidOperationException("No default Search-model found in DB.");

            var endpoint = (searchModel.Endpoint ?? "").Trim();
            var isAIFoundryServerless = endpoint.Contains("/models/chat/completions", StringComparison.OrdinalIgnoreCase)
                || (endpoint.Contains(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase)
                    && endpoint.Contains("/models/", StringComparison.OrdinalIgnoreCase));

            var messages = new[]
            {
             new { role = "system", content = "You are a helpful assistant." },
             new { role = "user", content = userContent }
            };

            var body = isAIFoundryServerless
                ? JsonSerializer.Serialize(new
                {
                    model = searchModel.ModelName?.Trim(),
                    messages,
                    max_tokens = searchModel.MaxTokens,
                    temperature = searchModel.Temperature
                })
                : JsonSerializer.Serialize(new
                {
                    messages,
                    max_tokens = searchModel.MaxTokens,
                    temperature = searchModel.Temperature
                });

            // Build the final prompt string
            var finalPrompt = string.Join("\n", messages.Select(m => $"{m.role}: {m.content}"));

            // Save into HttpContext so middleware can pick it later
            _httpContextAccessor.HttpContext.Items["FinalPrompt"] = finalPrompt;

            HttpClient client;
            string url;
            if (isAIFoundryServerless)
            {
                // AI Foundry serverless: use the full endpoint URL directly
                url = endpoint;
                client = _httpFactory.CreateClient();
                client.DefaultRequestHeaders.Add("api-key", searchModel.ApiKey);
            }
            else
            {
                url = $"/openai/deployments/{searchModel.ModelName}/chat/completions?api-version={searchModel.ApiVersion}";
                client = CreateClientForModel(searchModel);
            }

            using var res = await client.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"), ct);
            res.EnsureSuccessStatusCode();

            string responseText = await res.Content.ReadAsStringAsync(ct);
            client.Dispose();
            using var doc = JsonDocument.Parse(responseText);

            // log usage if available
            if (doc.RootElement.TryGetProperty("usage", out var usage))
            {
                int promptTokens = usage.GetProperty("prompt_tokens").GetInt32();
                int completionTokens = usage.GetProperty("completion_tokens").GetInt32();
                int totalTokens = usage.GetProperty("total_tokens").GetInt32();
            }
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }

        private int CountTokens(string text)
        {
            var encoding = GptEncoding.GetEncoding("cl100k_base");
            return encoding.Encode(text).Count;
        }
        public async Task<string> AnswerBatchAsync(string prompt, long unitId, int days, CancellationToken ct = default)
        {
            var fileId = await UploadPromptAsJsonlAsync(prompt, unitId, ct);

            // Submit batch job
            var openAIJobId = await SubmitBatchJobWithFileIdAsync(fileId, ct);

            // Save batch job to DB
            var batchJob = await CreateBatchJobRecordAsync(openAIJobId, "FeedbackAnalysis", (uint)unitId);

            return openAIJobId;
        }

        public async Task<string> PollBatchCompletionAsync(Guid batchJobId, CancellationToken ct = default)
        {

            var batchJob = await _context.BatchJobs.FindAsync(batchJobId);
            if (batchJob == null)
                throw new Exception("BatchJob not found in DB.");

            await _auditLogsService.LogHangfireJobAsync(batchJob.BusinessUnitId, "BatchPolling", "PollingStart", $"Polling batch {batchJob.Id} (OpenAI {batchJob.OpenAIJobId}) current={batchJob.Status}");

            var batchModel = await GetBatchModelAsync();
            using var client = CreateClientForModel(batchModel);
            var res = await client.GetAsync($"/openai/batches/{batchJob.OpenAIJobId}?api-version={batchModel.ApiVersion}", ct);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync(ct);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string status = root.TryGetProperty("status", out var statusProp)
                ? statusProp.GetString() ?? "unknown"
                : "unknown";

            // Do not mark completed until insights are saved successfully
            if (status == "completed")
            {
                string fileId = root.TryGetProperty("output_file_id", out var fileIdProp)
                    ? fileIdProp.GetString()
                    : root.TryGetProperty("output_file_url", out var urlProp) ? urlProp.GetString() : null;

                // Download final output
                var outputJson = await DownloadBatchOutputAsync(fileId, ct);

                // Persist insights and only then mark job as completed
                bool insightsSaved = false;
                try
                {
                    insightsSaved = await SaveEngagementInsightsAsync(outputJson, batchJob.BusinessUnitId, batchJob.Id);
                }
                catch (Exception ex)
                {
                    insightsSaved = false;
                    await _auditLogsService.LogHangfireJobAsync(batchJob.BusinessUnitId, "BatchPolling", "SaveInsightsError",
                        $"Batch {batchJob.Id} insights save failed: {ex}");

                    // If inserts actually succeeded before exception surfaced, detect by presence of EngagementInsights
                    var hasInsights = await _context.EngagementInsights.AnyAsync(e => e.BatchJobsId == batchJob.Id, ct);
                    if (hasInsights)
                    {
                        insightsSaved = true;
                    }
                }

                if (insightsSaved)
                {
                    batchJob.Status = "completed";
                    batchJob.OutputFileUrl = fileId;
                    batchJob.CompletedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    await _auditLogsService.LogHangfireJobAsync(batchJob.BusinessUnitId, "BatchPolling", "Completed", $"Batch {batchJob.Id} marked completed");
                    return outputJson;
                }
                else
                {
                    batchJob.Status = "finalizing";
                    await _context.SaveChangesAsync();
                    await _auditLogsService.LogHangfireJobAsync(batchJob.BusinessUnitId, "BatchPolling", "Finalizing",
                        $"Batch {batchJob.Id} awaiting insights persistence (SaveEngagementInsightsAsync returned false)");
                    return "Processing...";
                }

            }

            // Persist intermediate status updates without regressing from finalizing/completed
            if (!string.Equals(batchJob.Status, "completed", StringComparison.OrdinalIgnoreCase)
                && !(string.Equals(batchJob.Status, "finalizing", StringComparison.OrdinalIgnoreCase)
                     && string.Equals(status, "in_progress", StringComparison.OrdinalIgnoreCase)))
            {
                await _auditLogsService.LogHangfireJobAsync(batchJob.BusinessUnitId, "BatchPolling", "StatusAdvance", $"Batch {batchJob.Id}: {batchJob.Status} -> {status}");
                batchJob.Status = status;
            }

            if (status == "failed")
            {
                string errorMessage = root.TryGetProperty("error", out var errorElement)
                    ? (errorElement.ValueKind == JsonValueKind.String ? errorElement.GetString() : errorElement.ToString())
                    : "Unknown error";

                batchJob.Status = "failed";
                batchJob.ErrorMessage = errorMessage;
                batchJob.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await _auditLogsService.LogHangfireJobAsync(batchJob.BusinessUnitId, "BatchPolling", "Failed", $"Batch {batchJob.Id} failed: {errorMessage}");

                return errorMessage;
            }

            // Still validating/progress → persist status update
            await _context.SaveChangesAsync();
            await _auditLogsService.LogHangfireJobAsync(batchJob.BusinessUnitId, "BatchPolling", "PollingProgress", $"Batch {batchJob.Id} status={batchJob.Status}");
            return "Processing...";
        }

        private async Task<string> DownloadBatchOutputAsync(string fileId, CancellationToken ct)
        {
            var batchModel = await GetBatchModelAsync();
            using var client = CreateClientForModel(batchModel);

            var res = await client.GetAsync($"/openai/files/{fileId}/content?api-version={batchModel.ApiVersion}", ct);
            res.EnsureSuccessStatusCode();

            return await res.Content.ReadAsStringAsync(ct);
        }

        private async Task<string> UploadBatchFileAsync(string filePath, CancellationToken ct)
        {
            var batchModel = await GetBatchModelAsync();

            using var fileStream = File.OpenRead(filePath);
            var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/jsonl");

            using var content = new MultipartFormDataContent
            {
                { new StringContent("batch"), "purpose" },
                { fileContent, "file", Path.GetFileName(filePath) }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, $"/openai/files?api-version={batchModel.ApiVersion}")
            {
                Content = content
            };

            using var client = CreateClientForModel(batchModel);
            var res = await client.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"File upload failed: {json}");

            Console.WriteLine($"Upload Response: {json}");

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("id").GetString()!;
        }

        private async Task<string> SubmitBatchJobWithFileIdAsync(string fileId, CancellationToken ct)
        {
            var batchModel = await GetBatchModelAsync();

            var requestBody = new
            {
                input_file_id = fileId,
                endpoint = $"/openai/deployments/{batchModel.ModelName}/chat/completions",
                completion_window = "24h",
                metadata = new
                {
                    description = "RAG batch processing job (via file upload)"
                }
            };

            var body = JsonSerializer.Serialize(requestBody);

            using var req = new HttpRequestMessage(HttpMethod.Post, $"/openai/batches?api-version={batchModel.ApiVersion}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            using var client = CreateClientForModel(batchModel);
            var res = await client.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"Batch job submission failed: {json}");

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("id").GetString()!;
        }

        private async Task<BatchJob> CreateBatchJobRecordAsync(string openAIJobId, string requestType, uint businessUnitId, DateTimeOffset? fromDate = null, DateTimeOffset? toDate = null)
        {
            bool IsSystemGenerated = false;
            if (fromDate == null && toDate == null)
            {
                fromDate = DateTime.UtcNow;
                toDate = DateTime.UtcNow.AddDays(-14);
                IsSystemGenerated = true;
            }
            var metadata = new BatchJobMetadataJosn
            {
                FromDate = fromDate,
                ToDate = toDate,
                IsSystemGenerated = IsSystemGenerated,
            };

            var batchJob = new BatchJob
            {
                Id = Guid.NewGuid(),
                OpenAIJobId = openAIJobId,
                Status = "validating",  // initial status
                RequestType = requestType,
                SubmittedAt = DateTime.UtcNow,
                BusinessUnitId = businessUnitId,
                InputFileUrl = string.Empty,
                MetadataJson = JsonSerializer.Serialize(metadata)
            };

            // Save to DB
            _context.BatchJobs.Add(batchJob);
            await _context.SaveChangesAsync();

            return batchJob;
        }

        private async Task<bool> SaveEngagementInsightsAsync(string batchOutputJson, uint businessUnitId, Guid BatchJobsId)
        {
            // STEP 1: Extract inner content JSON from batch output
            using var docw = JsonDocument.Parse(batchOutputJson);
            var root1 = docw.RootElement;

            string? innerContent = null;

            innerContent = root1
                .GetProperty("response")
                .GetProperty("body")
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            if (string.IsNullOrWhiteSpace(innerContent))
            {
                // throw new InvalidOperationException("Batch output JSON contained no content.");
                return false;
            }


            // STEP 2: Clean braces (in case extra text comes along)
            string cleanedContent = innerContent.Trim();
            int firstBrace = cleanedContent.IndexOf('{');
            int lastBrace = cleanedContent.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                cleanedContent = cleanedContent.Substring(firstBrace, lastBrace - firstBrace + 1);
            }


            // STEP 3: Deserialize into DTO safely
            ChatGptSentimentResponse? chatResponse;

            chatResponse = JsonSerializer.Deserialize<ChatGptSentimentResponse>(
                cleanedContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            var batchJob = await _repository.GetBatchJobByIdAsync(BatchJobsId);

            // ── Report creation/publication: DISABLED by default ──────────────
            //
            // Everything from here to the end of the publish-log write used to
            // run on every completed batch: it deleted whatever report the unit
            // had inside the last 14 days and replaced it with a new, already
            // published one. That window is exactly the bi-weekly cycle the
            // scheduled job owns, so this path could silently destroy and
            // replace a scheduler-generated report — including one waiting for
            // admin review.
            //
            // Gated on LegacyReportPaths.BatchPollPublishEnabled (default
            // false). The code is intact and runs unchanged if the flag is
            // turned on. Batch polling, the batch metadata update and the
            // EngagementInsights save below are all OUTSIDE this block and keep
            // working exactly as before, so the LLM Logs screen is unaffected.
            LLMReport? reportInfo = null;

            if (!_legacyReportPaths.BatchPollPublishEnabled)
            {
                await _auditLogsService.LogHangfireJobAsync(
                    businessUnitId, "BatchPolling", "ReportCreationDisabled",
                    $"Batch {BatchJobsId}: report creation/publication skipped " +
                    $"(LegacyReportPaths.BatchPollPublishEnabled=false). The scheduled job owns report generation. " +
                    $"EngagementInsights and batch metadata still saved.");
            }
            else
            {
            // 5) check for an existing report for same unit + same date range (date-only match)
            var existingReport = await _repository.GetReportByUnitAndDatesAsync(
                batchJob.BusinessUnitId,
                DateTime.UtcNow.AddDays(-14),
                DateTime.UtcNow);

            // if exists, delete the old top-entities then delete the report
            if (existingReport != null)
            {
                await _repository.DeleteTopThemesByUnitAndReportAsync(batchJob.BusinessUnitId, (int)existingReport.Id);
                await _repository.DeleteTopRecommendationsByUnitAndReportAsync(batchJob.BusinessUnitId, (int)existingReport.Id);
                await _repository.DeleteTopActionsByUnitAndReportAsync(batchJob.BusinessUnitId, (int)existingReport.Id);

                await _repository.DeleteReportAsync((int)existingReport.Id);
            }

            // STEP 4: Create report record
            var report = new LLMReport()
            {
                FromDate = DateTime.UtcNow.AddDays(-14),
                ToDate = DateTime.UtcNow,
                UnitId = businessUnitId,
                CreatedAt = DateTime.UtcNow
            };
            reportInfo = await _repository.CreateReportAsync(report);

            // STEP 5: Save TopThemes (max 5)
            var topThemes = chatResponse.TopThemes?.Where(t => !string.IsNullOrWhiteSpace(t)).Take(5).ToList();
            if (topThemes != null && topThemes.Any())
            {
                var topThemeEntities = topThemes.Select(theme => new TopTheme
                {
                    UnitId = (uint)businessUnitId,
                    CreatedAt = DateTime.UtcNow,
                    Theme = theme,
                    ReportId = reportInfo.Id
                }).ToList();

                await _repository.SaveTopThemesAsync(topThemeEntities);
            }

            // STEP 6: Save TopRecommendations (max 3)
            var topRecommendations = chatResponse.TopRecommendations?.Where(r => !string.IsNullOrWhiteSpace(r)).Take(3).ToList();
            if (topRecommendations != null && topRecommendations.Any())
            {
                var recommendationEntities = topRecommendations.Select(rec => new TopRecommendation
                {
                    UnitId = (uint)businessUnitId,
                    CreatedAt = DateTime.UtcNow,
                    Recommendation = rec,
                    ReportId = reportInfo.Id
                }).ToList();

                await _repository.SaveTopRecommendationsAsync(recommendationEntities);
            }

            // STEP 7: Save TopActions (max 3)
            var topActions = chatResponse.TopActions?.Where(a => !string.IsNullOrWhiteSpace(a)).Take(3).ToList();
            if (topActions != null && topActions.Any())
            {
                var actionEntities = topActions.Select(action => new TopAction
                {
                    UnitId = (uint)businessUnitId,
                    CreatedAt = DateTime.UtcNow,
                    Action = action,
                    ReportId = reportInfo.Id
                }).ToList();

                await _repository.SaveTopActionsAsync(actionEntities);
            }

            await _repository.SaveInsightPublishLogAsync(new InsightPublishLog
            {
                ReportId           = reportInfo.Id,
                RunId              = 0,
                UserId             = null,
                DateFrom           = reportInfo.FromDate,
                DateTo             = reportInfo.ToDate,
                TopThemes          = System.Text.Json.JsonSerializer.Serialize(topThemes          ?? new List<string>()),
                TopActions         = System.Text.Json.JsonSerializer.Serialize(topActions         ?? new List<string>()),
                TopRecommendations = System.Text.Json.JsonSerializer.Serialize(topRecommendations ?? new List<string>()),
                CreatedAt          = DateTime.UtcNow
            });
            } // end LegacyReportPaths.BatchPollPublishEnabled

            var metadata = string.IsNullOrWhiteSpace(batchJob.MetadataJson) ? new BatchJobMetadataJosn() : JsonSerializer.Deserialize<BatchJobMetadataJosn>(batchJob.MetadataJson);

            // update metadata fields
            metadata.TopTheme = JsonSerializer.Serialize(chatResponse.TopThemes);
            metadata.TopRecomandation = JsonSerializer.Serialize(chatResponse.TopRecommendations);
            metadata.TopAction = JsonSerializer.Serialize(chatResponse.TopActions);
            metadata.Status = "Pending";
            metadata.ApprovedAt = DateTime.UtcNow;
            metadata.ApprovedBy = 1;
            metadata.ReportId = reportInfo?.Id ?? 0;
            // new worksheet info
            metadata.WorksheetCategory = chatResponse.SuggestedWorksheet?.Category;
            metadata.WorksheetFileName = chatResponse.SuggestedWorksheet?.FileName;
            batchJob.MetadataJson = JsonSerializer.Serialize(metadata);
            // Ensure SubmittedAt is always UTC
            if (batchJob.SubmittedAt.Kind != DateTimeKind.Utc)
            {
                batchJob.SubmittedAt = DateTime.SpecifyKind(batchJob.SubmittedAt, DateTimeKind.Utc);
            }

            // Optionally also ensure CompletedAt (if present) is UTC-safe
            if (batchJob.CompletedAt.HasValue && batchJob.CompletedAt.Value.Kind != DateTimeKind.Utc)
            {
                batchJob.CompletedAt = DateTime.SpecifyKind(batchJob.CompletedAt.Value, DateTimeKind.Utc);
            }

            await _repository.UpdateBatchJobAsync(batchJob);



            using var doc = JsonDocument.Parse(batchOutputJson);
            var root = doc.RootElement;

            // Check if insights already exist for this batch to avoid duplicates
            var existingInsights = await _context.EngagementInsights
                .FirstOrDefaultAsync(e => e.BatchJobsId == BatchJobsId);

            if (existingInsights != null)
            {
                await _auditLogsService.LogHangfireJobAsync(businessUnitId, "BatchPolling", "InsightsAlreadyExists", $"Batch {BatchJobsId} already has EngagementInsights (Id={existingInsights.Id})");
                return true; // Already saved, return success
            }

            // Since the database Id column is NOT an identity column, we need to manually generate the Id
            // Get the max Id and increment by 1
            var maxId = await _context.EngagementInsights
                .MaxAsync(e => (int?)e.Id) ?? 0;

            var insights = new EngagementInsights
            {
                Id = maxId + 1, // Manually generate Id since database column is not identity
                BusinessUnitId = businessUnitId,
                GeneratedAt = DateTime.UtcNow,
                PeriodStart = DateTime.UtcNow.AddDays(-14),
                PeriodEnd = DateTime.UtcNow,
                OverallScore = root.TryGetProperty("OverallScore", out var score) && score.ValueKind == JsonValueKind.Number ? score.GetDecimal() : null,
                CategoryScoresJson = root.TryGetProperty("CategoryScores", out var cat) ? cat.ToString() : "[]",
                TopThemesJson = JsonSerializer.Serialize(chatResponse.TopThemes?.Where(t => !string.IsNullOrWhiteSpace(t)).Take(5).ToList() ?? new List<string>()),
                RecommendationsJson = JsonSerializer.Serialize(chatResponse.TopRecommendations?.Where(r => !string.IsNullOrWhiteSpace(r)).Take(3).ToList() ?? new List<string>()),
                TopActionsJson = JsonSerializer.Serialize(chatResponse.TopActions?.Where(a => !string.IsNullOrWhiteSpace(a)).Take(3).ToList() ?? new List<string>()),
                StatisticalSummaryJson = root.TryGetProperty("StatisticalAnalysis", out var stats) ? stats.GetRawText() : "{}",
                CreatedBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "system",
                BatchJobsId = BatchJobsId
            };

            // Mark entity as added with manually generated Id
            _context.EngagementInsights.Add(insights);

            try
            {
                var changes = await _context.SaveChangesAsync();
                if (changes <= 0)
                {
                    await _auditLogsService.LogHangfireJobAsync(businessUnitId, "BatchPolling", "InsightsSaveNoChanges", $"Batch {BatchJobsId} save returned 0 changes");
                    // Verify presence anyway
                    var exists = await _context.EngagementInsights.AnyAsync(e => e.BatchJobsId == BatchJobsId);
                    return exists;
                }

                // Verify persisted row exists for this batch
                var persisted = await _context.EngagementInsights.AnyAsync(e => e.BatchJobsId == BatchJobsId);
                if (!persisted)
                {
                    await _auditLogsService.LogHangfireJobAsync(businessUnitId, "BatchPolling", "InsightsSaveMissing", $"Batch {BatchJobsId} row missing after save");
                }
                return true;
            }
            catch (Exception ex)
            {
                await _auditLogsService.LogHangfireJobAsync(businessUnitId, "BatchPolling", "InsightsSaveException", $"Batch {BatchJobsId} save threw: {ex}");
                return false;
            }
        }

        //public async Task<List<BatchJob>> GetPendingBatchJobsAsync()
        //{
        //    return await _context.BatchJobs
        //        .Where(b => b.Status == "validating"
        //                 || b.Status == "in_progress"
        //                 || b.Status == "finalizing")
        //        .ToListAsync();
        //}

        public async Task<List<BatchJob>> GetPendingBatchJobsAsync()
        {
            // Step 1: Get jobs by Status
            var jobsByStatus = await _context.BatchJobs
                .Where(b => b.Status == "validating"
                         || b.Status == "in_progress"
                         || b.Status == "finalizing")
                .ToListAsync();

            // If found jobs by status, return them immediately
            if (jobsByStatus != null && jobsByStatus.Count > 0)
                return jobsByStatus;

            // Step 2: If none found by status, check MetadataJson
            var allJobs = await _context.BatchJobs.ToListAsync();
            var pendingJobs = new List<BatchJob>();

            foreach (var job in allJobs)
            {
                try
                {
                    if (!string.IsNullOrEmpty(job.MetadataJson))
                    {
                        var metadata = JsonDocument.Parse(job.MetadataJson).RootElement;

                        if (metadata.TryGetProperty("TopRecomandation", out var topRecElement))
                        {
                            var topRecValue = topRecElement.GetString();

                            if (string.IsNullOrWhiteSpace(topRecValue) ||
                                topRecValue.Contains("pending", StringComparison.OrdinalIgnoreCase) ||
                                topRecValue == "[]")
                            {
                                pendingJobs.Add(job);
                            }
                        }
                    }
                }
                catch
                {
                    // If JSON is invalid, consider as pending
                    pendingJobs.Add(job);
                }
            }

            return pendingJobs;
        }


        private async Task<string> UploadPromptAsJsonlAsync(string prompt, long unitId, CancellationToken ct = default)
        {
            var batchModel = await GetBatchModelAsync();

            // 1. Build JSONL object
            var jsonlObj = new
            {
                custom_id = "1",
                method = "POST",
                url = "/chat/completions",
                body = new
                {
                    model = batchModel.ModelName,
                    messages = new object[]
                    {
            new
            {
                role = "system",
                content = "You are a CLOVER ERA and CLOVER FRAMEWORK EXPERT. You must return ONLY valid JSON output with keys: TopThemes, TopRecommendations, TopActions, QualitativeInsights, StatisticalAnalysis, Recommendation, RelevantWorksheet."
            },
            new
            {
                role = "user",
                content = prompt
            }
                    }
                }
            };

            // 2. Serialize to JSON (single-line JSONL)
            string jsonlContent = JsonSerializer.Serialize(jsonlObj);

            // 3. Put content into an in-memory stream
            var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(jsonlContent));
            var fileContent = new StreamContent(memoryStream);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/jsonl");

            // 4. Build multipart form with "fake" file
            using var content = new MultipartFormDataContent
                {
                    { new StringContent("batch"), "purpose" },
                    { fileContent, "file", $"learning-engagement-{unitId}-{DateTime.UtcNow:yyyyMMddHHmmss}.jsonl" }
                };

            // 5. Upload request
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/openai/files?api-version={batchModel.ApiVersion}")
            {
                Content = content
            };

            using var client = CreateClientForModel(batchModel);
            var res = await client.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"File upload failed: {json}");

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("id").GetString()!;
        }

        public async Task<BatchJob> GetBatchJobsAsync(string BatchJobId)
        {
            return await _context.BatchJobs.Where(a => a.OpenAIJobId == BatchJobId).FirstOrDefaultAsync();
        }

        public async Task<List<BatchJob>> GetPendingBatchJobsAzureResults(long unitId)
        {
            return await _context.BatchJobs
                .Where(b => b.BusinessUnitId == unitId && (b.Status == "validating"
                         || b.Status == "in_progress"
                         || b.Status == "finalizing"))
                .ToListAsync();
        }

        public async Task CheckBatchJobAzureResults(long unitId)
        {
            string jobName = "BatchPolling";

            try
            {
                await _auditLogsService.LogHangfireJobAsync(0, jobName, "Started", "Hangfire job triggered");

                var batchJobs = await GetPendingBatchJobsAzureResults(unitId);

                if (!batchJobs.Any())
                {
                    await _auditLogsService.LogHangfireJobAsync(0, jobName, "NoJobs", "No pending jobs found");
                    return;
                }

                foreach (var batchJob in batchJobs)
                {
                    try
                    {
                        string result = await PollBatchCompletionAzureResults(batchJob.Id);
                        await _auditLogsService.LogHangfireJobAsync(
                            batchJob.BusinessUnitId,
                            jobName,
                            "Success",
                            $"Batch {batchJob.Id} completed: {result}"
                        );
                    }
                    catch (Exception ex)
                    {
                        await _auditLogsService.LogHangfireJobAsync(
                            batchJob.BusinessUnitId,
                            jobName,
                            "Error",
                            $"Error polling batch {batchJob.Id}: {ex.Message}"
                        );
                    }
                }

                await _auditLogsService.LogHangfireJobAsync(0, jobName, "Finished", "Hangfire job finished");
            }
            catch (Exception ex)
            {
                await _auditLogsService.LogHangfireJobAsync(0, jobName, "FatalError", ex.Message);
            }
        }

        public async Task<string> PollBatchCompletionAzureResults(Guid batchJobId, CancellationToken ct = default)
        {

            var batchJob = await _context.BatchJobs.FindAsync(batchJobId);
            if (batchJob == null)
                throw new Exception("BatchJob not found in DB.");

            await _auditLogsService.LogHangfireJobAsync(batchJob.BusinessUnitId, "BatchPolling", "PollingStart", $"Polling (AZ) batch {batchJob.Id} (OpenAI {batchJob.OpenAIJobId}) current={batchJob.Status}");

            var batchModel2 = await GetBatchModelAsync();
            using var client2 = CreateClientForModel(batchModel2);
            var res = await client2.GetAsync($"/openai/batches/{batchJob.OpenAIJobId}?api-version={batchModel2.ApiVersion}", ct);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync(ct);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string status = root.TryGetProperty("status", out var statusProp)
                ? statusProp.GetString() ?? "unknown"
                : "unknown";

            if (status == "completed")
            {
                string fileId = root.TryGetProperty("output_file_id", out var fileIdProp)
                    ? fileIdProp.GetString()
                    : root.TryGetProperty("output_file_url", out var urlProp) ? urlProp.GetString() : null;

                // Download final output
                var outputJson = await DownloadBatchOutputAsync(fileId, ct);

                // Save insights first; only then mark as completed
                try
                {
                    await SaveEngagementInsightsAzureResults(outputJson, batchJob.BusinessUnitId, batchJob.Id);
                    batchJob.Status = "completed";
                    batchJob.OutputFileUrl = fileId;
                    batchJob.CompletedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    await _auditLogsService.LogHangfireJobAsync(batchJob.BusinessUnitId, "BatchPolling", "Completed", $"(AZ) Batch {batchJob.Id} marked completed");
                    return outputJson;
                }
                catch
                {
                    // If inserts actually succeeded before exception, detect via EngagementInsights presence
                    var hasInsights = await _context.EngagementInsights.AnyAsync(e => e.BatchJobsId == batchJob.Id, ct);
                    if (hasInsights)
                    {
                        batchJob.Status = "completed";
                        batchJob.OutputFileUrl = fileId;
                        batchJob.CompletedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync();
                        await _auditLogsService.LogHangfireJobAsync(batchJob.BusinessUnitId, "BatchPolling", "Completed", $"(AZ) Batch {batchJob.Id} marked completed (post-exception, insights detected)");
                        return outputJson;
                    }

                    // Keep job in finalizing until insights can be saved
                    batchJob.Status = "finalizing";
                    await _context.SaveChangesAsync();
                    await _auditLogsService.LogHangfireJobAsync(batchJob.BusinessUnitId, "BatchPolling", "Finalizing", $"(AZ) Batch {batchJob.Id} awaiting insights persistence");
                    return "Processing...";
                }
            }

            // Persist intermediate status updates without regressing from finalizing/completed
            if (!string.Equals(batchJob.Status, "completed", StringComparison.OrdinalIgnoreCase)
                && !(string.Equals(batchJob.Status, "finalizing", StringComparison.OrdinalIgnoreCase)
                     && string.Equals(status, "in_progress", StringComparison.OrdinalIgnoreCase)))
            {
                await _auditLogsService.LogHangfireJobAsync(batchJob.BusinessUnitId, "BatchPolling", "StatusAdvance", $"(AZ) Batch {batchJob.Id}: {batchJob.Status} -> {status}");
                batchJob.Status = status;
            }

            if (status == "failed")
            {
                string errorMessage = root.TryGetProperty("error", out var errorElement)
                    ? (errorElement.ValueKind == JsonValueKind.String ? errorElement.GetString() : errorElement.ToString())
                    : "Unknown error";

                batchJob.Status = "failed";
                batchJob.ErrorMessage = errorMessage;
                batchJob.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await _auditLogsService.LogHangfireJobAsync(batchJob.BusinessUnitId, "BatchPolling", "Failed", $"(AZ) Batch {batchJob.Id} failed: {errorMessage}");

                return errorMessage;
            }

            // Still validating/progress → persist status update
            await _context.SaveChangesAsync();
            await _auditLogsService.LogHangfireJobAsync(batchJob.BusinessUnitId, "BatchPolling", "PollingProgress", $"(AZ) Batch {batchJob.Id} status={batchJob.Status}");
            return "Processing...";
        }

        private async Task SaveEngagementInsightsAzureResults(string batchOutputJson, uint businessUnitId, Guid BatchJobsId)
        {
            // STEP 1: Extract inner content JSON from batch output
            using var docw = JsonDocument.Parse(batchOutputJson);
            var root1 = docw.RootElement;

            string? innerContent = null;

            innerContent = root1
                .GetProperty("response")
                .GetProperty("body")
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            if (string.IsNullOrWhiteSpace(innerContent))
                throw new InvalidOperationException("Batch output JSON contained no content.");

            // STEP 2: Clean braces (in case extra text comes along)
            string cleanedContent = innerContent.Trim();
            int firstBrace = cleanedContent.IndexOf('{');
            int lastBrace = cleanedContent.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                cleanedContent = cleanedContent.Substring(firstBrace, lastBrace - firstBrace + 1);
            }


            // STEP 3: Deserialize into DTO safely
            ChatGptSentimentResponse? chatResponse;

            chatResponse = JsonSerializer.Deserialize<ChatGptSentimentResponse>(
                cleanedContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            var topThemes = chatResponse.TopThemes?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            var topRecommendations = chatResponse.TopRecommendations?.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
            var topActions = chatResponse.TopActions?.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();

            using var doc = JsonDocument.Parse(batchOutputJson);
            var root = doc.RootElement;

            // Check if insights already exist for this batch to avoid duplicates
            var existingInsights = await _context.EngagementInsights
                .FirstOrDefaultAsync(e => e.BatchJobsId == BatchJobsId);

            if (existingInsights != null)
            {
                await _auditLogsService.LogHangfireJobAsync(businessUnitId, "BatchPolling", "InsightsAlreadyExists", $"(AZ) Batch {BatchJobsId} already has EngagementInsights (Id={existingInsights.Id})");
                return; // Already saved, exit early
            }

            var insights = new EngagementInsights
            {
                BusinessUnitId = businessUnitId,
                GeneratedAt = DateTime.UtcNow,
                PeriodStart = DateTime.UtcNow.AddDays(-14),
                PeriodEnd = DateTime.UtcNow,
                OverallScore = root.TryGetProperty("OverallScore", out var score) && score.ValueKind == JsonValueKind.Number ? score.GetDecimal() : null,
                CategoryScoresJson = root.TryGetProperty("CategoryScores", out var cat) ? cat.ToString() : "[]",
                TopThemesJson = JsonSerializer.Serialize(chatResponse.TopThemes?.Where(t => !string.IsNullOrWhiteSpace(t)).Take(5).ToList() ?? new List<string>()),
                RecommendationsJson = JsonSerializer.Serialize(chatResponse.TopRecommendations?.Where(r => !string.IsNullOrWhiteSpace(r)).Take(3).ToList() ?? new List<string>()),
                TopActionsJson = JsonSerializer.Serialize(chatResponse.TopActions?.Where(a => !string.IsNullOrWhiteSpace(a)).Take(3).ToList() ?? new List<string>()),
                StatisticalSummaryJson = root.TryGetProperty("StatisticalAnalysis", out var stats) ? stats.GetRawText() : "{}",
                CreatedBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "system",
                BatchJobsId = BatchJobsId
            };

            // Since the database Id column is NOT an identity column, we need to manually generate the Id
            // Get the max Id and increment by 1
            var maxId = await _context.EngagementInsights
                .MaxAsync(e => (int?)e.Id) ?? 0;
            insights.Id = maxId + 1;

            // Mark entity as added with manually generated Id
            _context.EngagementInsights.Add(insights);
            await _context.SaveChangesAsync();
        }


        public async Task<string> AnswerBatchAzureResults(string prompt, long unitId, int days, DateTimeOffset fromDate, DateTimeOffset toDate, CancellationToken ct = default)
        {
            var fileId = await UploadPromptAsJsonlAsync(prompt, unitId, ct);

            // Submit batch job
            var openAIJobId = await SubmitBatchJobWithFileIdAsync(fileId, ct);

            // Save batch job to DB
            var batchJob = await CreateBatchJobRecordAsync(openAIJobId, "FeedbackAnalysis", (uint)unitId, fromDate, toDate);

            return openAIJobId;
        }

    }
}