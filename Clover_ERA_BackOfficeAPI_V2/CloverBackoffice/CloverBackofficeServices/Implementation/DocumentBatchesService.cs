using CloverBackoffice.Components;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using EllipticCurve.Utils;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OfficeOpenXml;
using SendGrid.Helpers.Mail;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using static CloverBackoffice.Components.BussinessManager;
using CloverCategory = CloverBackoffice.Components.Enums.CloverCategory;
using DailyFeedbackSentimentType = CloverBackoffice.Infrastructure.Models.DailyFeedbackSentimentType;
using ReasonSentimentType = CloverBackoffice.Components.Enums.DailyReasonSentimentType;
using SharpToken;
using System.Diagnostics;
using System.IO;
namespace CloverBackoffice.Services.Implementation
{
    public class DocumentBatchesService : IDocumentBatchesService
    {
        private readonly FeedbackScoreThresholdsOptions _thresholds;
        private readonly ApplicationDbContext _context;
        private readonly IAuditLogsService _auditLogsService;
        private readonly IWebHostEnvironment _env;
        private readonly SendGridSettings _sendGridSettings;
        private readonly IConfiguration _configuration;
        private readonly FeedbackAutoImportSettings _settings;
        private readonly IDocumentBatchesRepository _repository;
        private readonly IHttpClientFactory _httpFactory;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IDocumentEmbeddingRepository _embeddingRepository;

        private ModelConfiguration? _cachedBatchModel;

        public DocumentBatchesService(IHttpClientFactory httpFactory, IDocumentBatchesRepository repository, IDocumentEmbeddingRepository embeddingRepository, IOptions<FeedbackScoreThresholdsOptions> thresholds, ApplicationDbContext context, IHttpContextAccessor httpContextAccessor, IAuditLogsService auditLogsService, IWebHostEnvironment env, SendGridSettings sendGridSettings, IConfiguration configuration)
        {
            _repository = repository;
            _embeddingRepository = embeddingRepository;
            _thresholds = thresholds.Value;
            _context = context;
            _auditLogsService = auditLogsService;
            _env = env;
            _sendGridSettings = sendGridSettings;
            _configuration = configuration;
            _settings = _configuration.GetSection("FeedbackAutoImport").Get<FeedbackAutoImportSettings>();
            _httpFactory = httpFactory;
            _httpContextAccessor = httpContextAccessor;
        }

        private async Task<ModelConfiguration> GetBatchModelAsync()
        {
            _cachedBatchModel ??= await _embeddingRepository.GetDefaultModelAsync(modelConfigTypeId: 2)
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

        private const int ModelContextLimit = 128000;
        private const int ReservedOutputTokens = 4096;
        private const int PromptTemplateBuffer = 3000; // system message + JSONL wrapper overhead

        private static int CountTokens(string text)
        {
            var encoding = GptEncoding.GetEncoding("cl100k_base");
            return encoding.Encode(text).Count;
        }

        /// <summary>
        /// Trims the feedback list so the full prompt stays within the model's context window.
        /// Returns the serialized JSON of the (possibly trimmed) feedback list.
        /// </summary>
        private static string TruncateFeedbackToFitContext(List<DailyFeedbackReportRowDto> feedbackList, string promptTemplateWithoutFeedback)
        {
            int templateTokens = CountTokens(promptTemplateWithoutFeedback);
            int maxFeedbackTokens = ModelContextLimit - ReservedOutputTokens - PromptTemplateBuffer - templateTokens;

            if (maxFeedbackTokens <= 0)
                maxFeedbackTokens = 10000; // safety floor

            var serialized = JsonSerializer.Serialize(feedbackList);
            int feedbackTokens = CountTokens(serialized);

            if (feedbackTokens <= maxFeedbackTokens)
                return serialized;

            // Binary search for the largest subset that fits
            int lo = 1, hi = feedbackList.Count - 1;
            int bestCount = lo;

            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                var subset = JsonSerializer.Serialize(feedbackList.Take(mid).ToList());
                int tokens = CountTokens(subset);

                if (tokens <= maxFeedbackTokens)
                {
                    bestCount = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return JsonSerializer.Serialize(feedbackList.Take(bestCount).ToList());
        }

        private static string BuildPromptTemplate(string fileListJson, string feedbackListSerialized, string dbPromptText = "")
        {
            // Parse ratio from DB prompt; defaults: 4 themes, 3 recommendations, 3 action steps
            var (themes, recommendations, actionSteps) =
                ResponseValidatorService.ParseRatioFromSystemPrompt(dbPromptText);

            string pmt = $@"{{
                  ""metadata.task"": ""clover_era_analysis"",
                  ""metadata.version"": ""1.2"",
                  ""metadata.description"": ""Analyze CLOVER ERA employee engagement data to identify themes, recommend focus areas, and generate actionable steps for managers"",
                  ""data_source.file_format"": ""xlsx"",
                  ""data_source.sheet_name"": ""Sheet1"",
                  ""data_source.required_columns.clover_category.header_name"": ""Clover Category"",
                  ""data_source.required_columns.clover_category.type"": ""string"",
                  ""data_source.required_columns.clover_category.valid_values"": [""Communication"", ""Learning"", ""Opportunity"", ""Vulnerability"", ""Enablement"", ""Reflection""],
                  ""data_source.required_columns.question.header_name"": ""QUESTION"",
                  ""data_source.required_columns.answer.header_name"": ""Answer"",
                  ""data_source.required_columns.reason.header_name"": ""Reason"",
                  ""analysis_instructions.sentiment_scoring.method"": ""text_analysis"",
                  ""analysis_instructions.sentiment_scoring.score_ranges.positive.min_percent"": 60,
                  ""analysis_instructions.sentiment_scoring.score_ranges.positive.max_percent"": 100,
                  ""analysis_instructions.sentiment_scoring.score_ranges.positive.indicators"": [""constructive"", ""valuable"", ""accomplished"", ""empowered"", ""supportive"", ""clear"", ""respectful"", ""insightful"", ""progress"", ""freedom"", ""learned"", ""celebrated""],
                  ""analysis_instructions.sentiment_scoring.score_ranges.neutral.min_percent"": 40,
                  ""analysis_instructions.sentiment_scoring.score_ranges.neutral.max_percent"": 59,
                  ""analysis_instructions.sentiment_scoring.score_ranges.neutral.indicators"": [""adequate"", ""acceptable"", ""okay"", ""routine"", ""standard""],
                  ""analysis_instructions.sentiment_scoring.score_ranges.needs_work.min_percent"": 0,
                  ""analysis_instructions.sentiment_scoring.score_ranges.needs_work.max_percent"": 39,
                  ""analysis_instructions.sentiment_scoring.score_ranges.needs_work.indicators"": [""ambiguous"", ""confused"", ""disrespected"", ""undervalued"", ""irrelevant"", ""ineffective"", ""superficial"", ""unnoticed"", ""stagnant"", ""repetitive"", ""unclear"", ""frustrating""],
                  ""analysis_instructions.theme_extraction.theme_requirements.max_positives"": {themes / 2},
                  ""analysis_instructions.theme_extraction.theme_requirements.max_needs_work"": {themes - themes / 2},
                  ""analysis_instructions.theme_extraction.theme_requirements.label_max_length"": 50,
                  ""analysis_instructions.theme_extraction.theme_requirements.example_quotes_count"": 2,
                  ""analysis_instructions.theme_extraction.theme_requirements.quote_word_range.min"": 6,
                  ""analysis_instructions.theme_extraction.theme_requirements.quote_word_range.max"": 18,
                  ""analysis_instructions.action_steps.count"": {actionSteps},
                  ""analysis_instructions.action_steps.structure[0].type"": ""worksheet"",
                  ""analysis_instructions.action_steps.structure[0].duration_minutes"": 30,
                  ""analysis_instructions.action_steps.structure[0].requires_worksheet_reference"": ""true"",
                  ""analysis_instructions.action_steps.structure[0].knowledge_base_search"": ""Search for worksheets from the provided 'available_files' list and map to the most relevant one."",
                  ""analysis_instructions.action_steps.structure[1].type"": ""micro"",
                  ""analysis_instructions.action_steps.structure[1].duration_minutes_range.min"": 2,
                  ""analysis_instructions.action_steps.structure[1].duration_minutes_range.max"": 5,
                  ""analysis_instructions.action_steps.summary_bullets.count"": 2,
                  ""analysis_instructions.action_steps.summary_bullets.max_characters"": 120,
                  ""constraints.pii_policy"": ""Never output company names or personal identifiers"",
                  ""constraints.rounding"": ""Round percentages to 1 decimal place"",
                  ""constraints.confidence_threshold"": ""Only include themes with 3+ supporting responses"",
                  ""constraints.worksheet_identifier_format"": ""[Element] [Number] format required (e.g., Communication 2, Vulnerability 6)"",
                  ""constraints.knowledge_base_usage"": ""Action 1 MUST reference a specific worksheet from the knowledge base and the SuggestedWorksheet.FileName MUST match one of the files in 'available_files'."",
                  ""knowledge_base"": {{
                    ""available_files_source"": ""Vector Database"",
                    ""categories"": [""Communication"", ""Enablement"", ""Learning"", ""Opportunity"", ""Reflection"", ""Vulnerability""],
                    ""available_files"": {fileListJson},
                    ""file_naming_rules"": {{
                      ""description"": ""The available_files list contains all valid filenames. Choose the most relevant file from this list."",
                      ""rule"": ""Select a file from the available_files list that matches the concept.""
                    }}
                  }}
                }}";

            return $@"
                {dbPromptText}

                {pmt}

                You are an assistant that MUST return ONLY valid JSON.
                FOLLOW ALL ANALYSIS INSTRUCTIONS FROM THE SPECIFICATION ABOVE to analyze the feedback data.
                Apply the sentiment scoring, theme extraction, and action step generation logic as defined.

                IMPORTANT: You MUST produce EXACTLY {themes} theme(s), EXACTLY {recommendations} recommendation(s), and EXACTLY {actionSteps} action step(s). No more, no less.

                Return ONLY a simplified JSON object that matches this schema:
                {{
                  ""TopThemes"": [""string""],
                  ""TopRecommendations"": [""string""],
                  ""TopActions"": [""string""],
                  ""SuggestedWorksheet"": {{
                    ""Category"": ""string"",
                    ""FileName"": ""string""   // MUST be the actual blob filename, e.g. 'Worksheet L8.pdf'
                  }}
                }}

                USE THE ANALYSIS PROCESS FROM THE SPECIFICATION BUT SIMPLIFY THE OUTPUT:
                - Analyze feedback OVERALL (not per theme/category)
                - TopThemes: Extract EXACTLY {themes} themes as **two-line concise statements** (each theme may be up to two short sentences, not exceeding two lines)
                - TopRecommendations: Generate EXACTLY {recommendations} key strategic recommendations, each written as **two-line insights** that provide clarity and context (avoid overly short single-line text)
                - TopActions: Create EXACTLY {actionSteps} actionable steps (each up to **two lines maximum**) — include worksheet references where appropriate
                - SuggestedWorksheet: From the entire analysis, select ONLY ONE most relevant worksheet and:
                    • Provide Category (one of the categories)
                    • Provide FileName in exact blob format (e.g., 'Worksheet L8.pdf')

                Important rules (ENFORCE STRICTLY):
                1) The assistant must select a VALID file name from the 'available_files' list.
                2) Each Theme, Recommendation, and Action MUST be two lines maximum — not shorter or longer.
                3) Return ONLY raw JSON. No explanations. No markdown.
                4) TopThemes array MUST contain EXACTLY {themes} items. TopRecommendations MUST contain EXACTLY {recommendations} items. TopActions MUST contain EXACTLY {actionSteps} items.

                Feedback:
                {feedbackListSerialized}
                ";
        }

        public async Task<string> AnalyzeFeedbackBatchesEmbedding(long unitId, int days)
        {
            // 1. Get feedback from DB
            var feedbackList = await _repository.GetDailyFeedbackReportAsync2Embedding(unitId, days);
            if (!feedbackList.Any())
                return null;

            // 2. Map enums to readable names
            foreach (var obj in feedbackList)
            {
                if (Enum.IsDefined(typeof(DailyFeedbackSentimentType), obj.SentimentType))
                    obj.SentimentTypeName = ((DailyFeedbackSentimentType)obj.SentimentType).ToString();

                if (obj.SpecificationCloverCategory.HasValue &&
                    Enum.IsDefined(typeof(CloverCategory), obj.SpecificationCloverCategory.Value))
                {
                    obj.SpecificationCloverCategoryName =
                        ((CloverCategory)obj.SpecificationCloverCategory.Value).ToString();
                }
            }

            // 3. Get available worksheet files from DB
            var worksheetFiles = await _repository.GetAllWorksheetFilesAsync();
            var fileListJson = JsonSerializer.Serialize(worksheetFiles);

            // 3.5 Build the prompt template (without feedback) to measure its token size
            string promptTemplateWithoutFeedback = BuildPromptTemplate(fileListJson, "");

            // 4. Serialize feedback, truncating if it would exceed the model's context window
            var feedbackListSerialized = TruncateFeedbackToFitContext(feedbackList, promptTemplateWithoutFeedback);

            // 5. Build the final prompt with the (possibly truncated) feedback
            var prompt = BuildPromptTemplate(fileListJson, feedbackListSerialized);

            // 6. Batch API Flow
            string rawResponse;
            rawResponse = await AnswerBatchAsyncEmbedding(prompt, unitId, days);

            return rawResponse;
        }

        public async Task<BusinessUnitBatchJobsDto> GetBatchJobsWithInsightsAsyncEmbedding(int businessUnitId)
        {
            await CheckBatchJobAzureResultsEmbedding(businessUnitId);
            var batchJobs = await _repository.GetBatchJobsWithInsightsByBusinessUnitIdAsyncEmbedding(businessUnitId);

            return new BusinessUnitBatchJobsDto
            {
                BusinessUnitId = businessUnitId,
                BatchJobs = batchJobs
            };
        }

        // rag service function below for batches
        private async Task<string> AnswerBatchAsyncEmbedding(string prompt, long unitId, int days, CancellationToken ct = default)
        {
            var fileId = await UploadPromptAsJsonlAsyncEmbedding(prompt, unitId, null, ct);

            // Submit batch job
            var openAIJobId = await SubmitBatchJobWithFileIdAsyncEmbedding(fileId, null, ct);

            // Save batch job to DB
            var batchJob = await CreateBatchJobRecordAsyncEmbedding(openAIJobId, "FeedbackAnalysis", (uint)unitId);

            return openAIJobId;
        }

        private async Task<string> UploadPromptAsJsonlAsyncEmbedding(string prompt, long unitId, ModelConfiguration? modelConfig = null, CancellationToken ct = default)
        {
            modelConfig ??= await GetBatchModelAsync();
            var apiVersion = modelConfig.ApiVersion;
            var modelName = modelConfig.ModelName;

            // 1. Build JSONL object
            var jsonlObj = new
            {
                custom_id = "1",
                method = "POST",
                url = "/chat/completions",
                body = new
                {
                    model = modelName,
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
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/openai/files?api-version={apiVersion}")
            {
                Content = content
            };

            using var client = CreateClientForModel(modelConfig);
            var res = await client.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"File upload failed: {json}");

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("id").GetString()!;
        }

        private async Task<string> SubmitBatchJobWithFileIdAsyncEmbedding(string fileId, ModelConfiguration? modelConfig = null, CancellationToken ct = default)
        {
            modelConfig ??= await GetBatchModelAsync();
            var modelName = modelConfig.ModelName;
            var apiVersion = modelConfig.ApiVersion;

            var requestBody = new
            {
                input_file_id = fileId,
                endpoint = $"/openai/deployments/{modelName}/chat/completions",
                completion_window = "24h",
                metadata = new
                {
                    description = "RAG batch processing job (via file upload)"
                }
            };

            var body = JsonSerializer.Serialize(requestBody);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/openai/batches?api-version={apiVersion}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            using var client = CreateClientForModel(modelConfig);
            var res = await client.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"Batch job submission failed: {json}");

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("id").GetString()!;
        }

        private async Task<BatchJob> CreateBatchJobRecordAsyncEmbedding(string openAIJobId, string requestType, uint businessUnitId, DateTimeOffset? fromDate = null, DateTimeOffset? toDate = null)
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

        private async Task CheckBatchJobAzureResultsEmbedding(long unitId)
        {
            string jobName = "BatchPolling";

            try
            {
                await _auditLogsService.LogHangfireJobAsync(0, jobName, "Started", "Hangfire job triggered");

                var batchJobs = await GetPendingBatchJobsAzureResultsEmbedding(unitId);

                if (!batchJobs.Any())
                {
                    await _auditLogsService.LogHangfireJobAsync(0, jobName, "NoJobs", "No pending jobs found");
                    return;
                }

                foreach (var batchJob in batchJobs)
                {
                    try
                    {
                        string result = await PollBatchCompletionAzureResultsEmbedding(batchJob.Id);
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
        private async Task<List<BatchJob>> GetPendingBatchJobsAzureResultsEmbedding(long unitId)
        {
            return await _context.BatchJobs
                .Where(b => b.BusinessUnitId == unitId && (b.Status == "validating"
                         || b.Status == "in_progress"
                         || b.Status == "finalizing"))
                .ToListAsync();
        }

        private async Task<string> PollBatchCompletionAzureResultsEmbedding(Guid batchJobId, CancellationToken ct = default)
        {

            var batchJob = await _context.BatchJobs.FindAsync(batchJobId);
            if (batchJob == null)
                throw new Exception("BatchJob not found in DB.");

            await _auditLogsService.LogHangfireJobAsync(batchJob.BusinessUnitId, "BatchPolling", "PollingStart", $"Polling (AZ) batch {batchJob.Id} (OpenAI {batchJob.OpenAIJobId}) current={batchJob.Status}");

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
        private async Task<string> DownloadBatchOutputAsync(string fileId, CancellationToken ct)
        {
            var batchModel = await GetBatchModelAsync();
            using var client = CreateClientForModel(batchModel);

            var res = await client.GetAsync($"/openai/files/{fileId}/content?api-version={batchModel.ApiVersion}", ct);
            res.EnsureSuccessStatusCode();

            return await res.Content.ReadAsStringAsync(ct);
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

        // below 3rd api 
        public async Task<BatchJobAzureDto> AnalyzeFeedbackAzureResultsEmbedding([FromQuery] long unitId, [FromQuery] DateTimeOffset fromDate, [FromQuery] DateTimeOffset toDate)
        {
            // 1. Get feedback from DB
            var feedbackList = await _repository.GetDailyFeedbackReportAzureResults(unitId, fromDate, toDate);
            if (!feedbackList.Any())
                return null;

            // 2. Map enums to readable names
            foreach (var obj in feedbackList)
            {
                if (Enum.IsDefined(typeof(DailyFeedbackSentimentType), obj.SentimentType))
                    obj.SentimentTypeName = ((DailyFeedbackSentimentType)obj.SentimentType).ToString();

                if (obj.SpecificationCloverCategory.HasValue &&
                    Enum.IsDefined(typeof(CloverCategory), obj.SpecificationCloverCategory.Value))
                {
                    obj.SpecificationCloverCategoryName =
                        ((CloverCategory)obj.SpecificationCloverCategory.Value).ToString();
                }
            }

            // 3. Get available worksheet files from DB
            var worksheetFiles = await _repository.GetAllWorksheetFilesAsync();
            var fileListJson = JsonSerializer.Serialize(worksheetFiles);

            // 4. Fetch default prompt from DB
            var defaultPrompt = await _embeddingRepository.GetDefaultPromptAsync();
            var dbPromptText = defaultPrompt != null && !string.IsNullOrWhiteSpace(defaultPrompt.Text)
                ? defaultPrompt.Text
                : string.Empty;

            // 4.5 Build the prompt template (without feedback) to measure its token size
            string promptTemplateWithoutFeedback = BuildPromptTemplate(fileListJson, "", dbPromptText);

            // 5. Serialize feedback, truncating if it would exceed the model's context window
            var feedbackListSerialized = TruncateFeedbackToFitContext(feedbackList, promptTemplateWithoutFeedback);

            // 5.5 Build the final prompt with the (possibly truncated) feedback
            var prompt = BuildPromptTemplate(fileListJson, feedbackListSerialized, dbPromptText);

            // 6. Fetch default model from DB
            // ModelConfigTypeId 2 = Batch-model
            var defaultModel = await _embeddingRepository.GetDefaultModelAsync(modelConfigTypeId: 2);

            // 6. Batch API Flow
            string rawResponse;
            rawResponse = await AnswerBatchAzureResults(prompt, unitId, 0, fromDate, toDate, defaultModel);

            var batchJob = await GetBatchJobsAsync(rawResponse);

            // 6. Map entity  DTO
            return new BatchJobAzureDto
            {
                JobId = batchJob.OpenAIJobId,
                JobType = batchJob.RequestType,
                UnitId = batchJob.BusinessUnitId,
                Status = batchJob.Status,
                CreatedAt = batchJob.SubmittedAt,
                CompletedAt = batchJob.CompletedAt
            };

        }

        private async Task<string> AnswerBatchAzureResults(string prompt, long unitId, int days, DateTimeOffset fromDate, DateTimeOffset toDate, ModelConfiguration? modelConfig = null, CancellationToken ct = default)
        {
            var fileId = await UploadPromptAsJsonlAsyncEmbedding(prompt, unitId, modelConfig, ct);

            // Submit batch job
            var openAIJobId = await SubmitBatchJobWithFileIdAsyncEmbedding(fileId, modelConfig, ct);

            // Save batch job to DB
            var batchJob = await CreateBatchJobRecordAsyncEmbedding(openAIJobId, "FeedbackAnalysise", (uint)unitId, fromDate, toDate);

            return openAIJobId;
        }
        private async Task<BatchJob> GetBatchJobsAsync(string BatchJobId)
        {
            return await _context.BatchJobs.Where(a => a.OpenAIJobId == BatchJobId).FirstOrDefaultAsync();
        }
    }
}

