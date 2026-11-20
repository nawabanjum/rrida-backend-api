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
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static CloverBackoffice.Components.BussinessManager;
using CloverCategory = CloverBackoffice.Components.Enums.CloverCategory;
using DailyFeedbackSentimentType = CloverBackoffice.Infrastructure.Models.DailyFeedbackSentimentType;
using ReasonSentimentType = CloverBackoffice.Components.Enums.DailyReasonSentimentType;
namespace CloverBackoffice.Services.Implementation
{
    public class FeedbackAnalysisService : IFeedbackAnalysisService
    {
        private readonly IFeedbackRepository _repository;
        private readonly IDocumentEmbeddingRepository _docEmbeddingRepository;
        private readonly IDocumentBatchesRepository _documentBatchesRepo;
        private readonly IHttpClientFactory _httpFactory;
        private readonly FeedbackScoreThresholdsOptions _thresholds;
        private readonly ApplicationDbContext _context;
        private readonly IRagService _azureRagService;
        private readonly IAuditLogsService _auditLogsService;
        private readonly IWebHostEnvironment _env;
        private readonly IAggregateDashboardService _aggregateDashboardService;
        private readonly IConfiguration _configuration;
        private readonly FeedbackAutoImportSettings _settings;

        private ModelConfiguration? _cachedSearchModel;

        // Client-side rate limiter — same pattern as DocumentEmbeddingService.ThrottleAsync
        private static readonly SemaphoreSlim _semaphore = new(1, 1);
        private static readonly Queue<DateTime> _requestLog = new();
        private static readonly Queue<(DateTime, int)> _tokenLog = new();
        private static readonly TimeSpan _window = TimeSpan.FromSeconds(60);
        private const int _rpm = 300;   // match Azure deployment limit
        private const int _tpm = 50000; // match Azure deployment limit

        private readonly IDocumentEmbeddingService _embeddingService;
        private readonly IReportNotificationService _reportNotificationService;

        public FeedbackAnalysisService(IFeedbackRepository repository, IDocumentEmbeddingRepository docEmbeddingRepository, IHttpClientFactory httpFactory, IOptions<FeedbackScoreThresholdsOptions> thresholds, ApplicationDbContext context, IRagService azureRagService, IAuditLogsService auditLogsService, IWebHostEnvironment env, IAggregateDashboardService aggregateDashboardService, IConfiguration configuration, IDocumentEmbeddingService embeddingService, IDocumentBatchesRepository documentBatchesRepo, IReportNotificationService reportNotificationService)
        {
            _repository = repository;
            _docEmbeddingRepository = docEmbeddingRepository;
            _httpFactory = httpFactory;
            _thresholds = thresholds.Value;
            _context = context;
            _azureRagService = azureRagService;
            _auditLogsService = auditLogsService;
            _env = env;
            _aggregateDashboardService = aggregateDashboardService;
            _configuration = configuration;
            _settings = _configuration.GetSection("FeedbackAutoImport").Get<FeedbackAutoImportSettings>();
            _embeddingService = embeddingService;
            _documentBatchesRepo = documentBatchesRepo;
            _reportNotificationService = reportNotificationService;
        }

        private async Task<ModelConfiguration> GetSearchModelAsync()
        {
            _cachedSearchModel ??= await _docEmbeddingRepository.GetDefaultModelAsync(modelConfigTypeId: 1)
                ?? throw new InvalidOperationException("No default Search-model found in DB.");
            return _cachedSearchModel;
        }

        private HttpClient CreateClientForModel(ModelConfiguration config)
        {
            var client = _httpFactory.CreateClient();
            var baseUri = new Uri(config.Endpoint.TrimEnd('/') + "/");
            client.BaseAddress = baseUri;
            client.DefaultRequestHeaders.Add("api-key", config.ApiKey);
            return client;
        }

        private async Task ThrottleAsync(int estimatedTokens)
        {
            await _semaphore.WaitAsync();
            try
            {
                var now = DateTime.UtcNow;

                // Clean old entries outside the 60s window
                while (_requestLog.Count > 0 && now - _requestLog.Peek() > _window)
                    _requestLog.Dequeue();
                while (_tokenLog.Count > 0 && now - _tokenLog.Peek().Item1 > _window)
                    _tokenLog.Dequeue();

                var tokenSum = _tokenLog.Sum(t => t.Item2);

                // Wait if we'd exceed RPM or TPM limits
                if (_requestLog.Count >= _rpm || tokenSum + estimatedTokens > _tpm)
                {
                    var waitTime = _requestLog.Count > 0
                        ? _window - (now - _requestLog.Peek())
                        : TimeSpan.FromSeconds(5);
                    if (waitTime > TimeSpan.Zero)
                        await Task.Delay(waitTime);
                }

                _requestLog.Enqueue(DateTime.UtcNow);
                _tokenLog.Enqueue((DateTime.UtcNow, estimatedTokens));
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private static int EstimateTokens(string text) => text.Length / 4; // rough 4 chars per token

        private async Task<string> CallChatCompletionAsync(string prompt)
        {
            var model = await GetSearchModelAsync();

            int estimatedTokens = EstimateTokens(prompt) + model.MaxTokens;
            await ThrottleAsync(estimatedTokens);

            // Build full absolute URL
            var endpoint = (model.Endpoint ?? "").Trim();
            var isAIFoundryServerless = endpoint.Contains("/models/chat/completions", StringComparison.OrdinalIgnoreCase)
                || (endpoint.Contains(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase)
                    && endpoint.Contains("/models/", StringComparison.OrdinalIgnoreCase));

            string url;
            if (isAIFoundryServerless)
                url = endpoint;
            else
                url = $"{endpoint.TrimEnd('/')}/openai/deployments/{model.ModelName}/chat/completions?api-version={model.ApiVersion}";

            var messages = new[]
            {
                new { role = "system", content = "You are a JSON API. Always respond ONLY with valid JSON." },
                new { role = "user", content = prompt }
            };

            var body = isAIFoundryServerless
                ? JsonSerializer.Serialize(new
                {
                    model = model.ModelName?.Trim(),
                    messages,
                    max_tokens = model.MaxTokens,
                    temperature = model.Temperature
                })
                : JsonSerializer.Serialize(new
                {
                    messages,
                    max_tokens = model.MaxTokens,
                    temperature = model.Temperature
                });

            const int maxRetries = 3;
            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                using var client = _httpFactory.CreateClient();
                client.DefaultRequestHeaders.Add("api-key", model.ApiKey);

                using var res = await client.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));

                if (res.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < maxRetries)
                {
                    // Azure says "retry after 60 seconds" — respect it
                    var delaySeconds = res.Headers.RetryAfter?.Delta?.TotalSeconds ?? 60;
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                    continue;
                }

                res.EnsureSuccessStatusCode();

                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            }

            throw new HttpRequestException("Azure OpenAI call failed after maximum retries.");
        }

        /// <summary>
        /// Splits feedback into chunks that fit within Azure token limits,
        /// analyzes each chunk, then merges results into one response.
        /// </summary>
        private async Task<ChatGptSentimentResponse> AnalyzeFeedbackInChunksAsync(List<DailyFeedbackReportRowDto> feedbackList)
        {
            // Max tokens budget for prompt text: leave room for system message + response
            const int maxPromptTokens = 30000; // safe limit within 50K TPM
            const int overheadTokens = 2000;   // system prompt + instructions overhead

            // Split feedback into chunks that fit within token budget
            var chunks = new List<List<DailyFeedbackReportRowDto>>();
            var currentChunk = new List<DailyFeedbackReportRowDto>();
            int currentTokens = overheadTokens;

            foreach (var item in feedbackList)
            {
                var rowText = $"CloverCategory: {item.SpecificationCloverCategoryName}, Question: {item.QuestionText}, Answer: {item.FreeText}, Score: {item.Score}, Reason: {item.ReasonText}";
                int rowTokens = EstimateTokens(rowText);

                if (currentTokens + rowTokens > maxPromptTokens && currentChunk.Any())
                {
                    chunks.Add(currentChunk);
                    currentChunk = new List<DailyFeedbackReportRowDto>();
                    currentTokens = overheadTokens;
                }

                currentChunk.Add(item);
                currentTokens += rowTokens;
            }
            if (currentChunk.Any())
                chunks.Add(currentChunk);

            var vectorContext = await RetrieveVectorContextAsync(feedbackList);

            // Single chunk — no splitting needed
            if (chunks.Count == 1)
            {
                var prompt = BuildPrompt(chunks[0], vectorContext);
                var rawJson = await CallChatCompletionAsync(prompt);
                return ParseChatResponse(rawJson);
            }

            // Multiple chunks — analyze each, then merge
            var partialResults = new List<ChatGptSentimentResponse>();
            for (int i = 0; i < chunks.Count; i++)
            {
                var prompt = BuildPrompt(chunks[i], vectorContext);
                var rawJson = await CallChatCompletionAsync(prompt);
                var partial = ParseChatResponse(rawJson);
                if (partial != null)
                    partialResults.Add(partial);
            }

            if (!partialResults.Any())
                return null;

            // Merge all partial results into a single summary prompt
            var mergePrompt = BuildMergePrompt(partialResults);
            var mergedJson = await CallChatCompletionAsync(mergePrompt);
            return ParseChatResponse(mergedJson);
        }

        private ChatGptSentimentResponse ParseChatResponse(string rawJson)
        {
            var cleaned = rawJson.Trim();
            if (cleaned.StartsWith("```"))
            {
                int first = cleaned.IndexOf('{');
                int last = cleaned.LastIndexOf('}');
                if (first >= 0 && last > first)
                    cleaned = cleaned.Substring(first, last - first + 1);
            }
            return JsonSerializer.Deserialize<ChatGptSentimentResponse>(cleaned);
        }

        private string BuildMergePrompt(List<ChatGptSentimentResponse> partialResults)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are a JSON API. Always respond ONLY with valid JSON.");
            sb.AppendLine("Below are partial analysis results from multiple batches of employee feedback.");
            sb.AppendLine("Merge and deduplicate them into a single consolidated result.");
            sb.AppendLine();

            for (int i = 0; i < partialResults.Count; i++)
            {
                sb.AppendLine($"--- Batch {i + 1} ---");
                sb.AppendLine(JsonSerializer.Serialize(partialResults[i]));
                sb.AppendLine();
            }

            sb.AppendLine("Return a single JSON object with these keys:");
            sb.AppendLine("{");
            sb.AppendLine("  \"TopThemes\": [\"string\"],        // max 5, deduplicated");
            sb.AppendLine("  \"TopRecommendations\": [\"string\"], // max 3, deduplicated");
            sb.AppendLine("  \"TopActions\": [\"string\"]          // max 3, deduplicated");
            sb.AppendLine("}");
            sb.AppendLine("Return ONLY raw JSON. No explanations. No markdown.");

            return sb.ToString();
        }

        public async Task<ChatGptSentimentResponse> AnalyzeFeedbackAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate)
        {
            // 1) Get the feedback dataset from DB
            var DailyFeedbackReportRowDtos = await _repository.GetDailyFeedbackReportAsync(unitId, fromDate, toDate);

            return null;
        }
        public async Task<DashboardSummaryDto> AnalyzeFeedbackRunTimeAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate)
        {
            var DailyFeedbackReportRowDtos = await _repository.GetDailyFeedbackReportAsync(unitId, fromDate, toDate);
            return DailyFeedbackReportRowDtos;
        }
        public async Task<FeedbackInsightsDto> AnalyzeFeedbackAsync2(long unitId, int days)
        {
            // 1. Get feedback from DB
            var feedbackList = await _repository.GetDailyFeedbackReportAsync2(unitId, days);

            if (!feedbackList.Any())
            {
                return null;
            }

            // 2. Map Enums to readable names
            foreach (var obj in feedbackList)
            {
                if (Enum.IsDefined(typeof(DailyFeedbackSentimentType), obj.SentimentType))
                    obj.SentimentTypeName = ((DailyFeedbackSentimentType)obj.SentimentType).ToString();

                if (obj.SpecificationCloverCategory.HasValue &&
                    Enum.IsDefined(typeof(CloverCategory), obj.SpecificationCloverCategory.Value))
                {
                    obj.SpecificationCloverCategoryName = ((CloverCategory)obj.SpecificationCloverCategory.Value).ToString();
                }
            }

            // 3-4. Analyze feedback in token-safe chunks via Azure OpenAI
            var chatResponse = await AnalyzeFeedbackInChunksAsync(feedbackList);

            // 5. Store Top Themes (max 5)
            var topThemes = chatResponse.TopThemes?.Take(5).ToList() ?? new List<string>();
            var report = new LLMReport()
            {
                FromDate = DateTime.UtcNow.AddDays(-14),
                ToDate = DateTime.UtcNow,
                UnitId = unitId,
                CreatedAt = DateTime.UtcNow
            };
            var repostInfo = await _repository.CreateReportAsync(report);

            if (topThemes.Any())
            {
                var topThemeEntities = topThemes.Select(theme => new TopTheme
                {
                    UnitId = (uint)unitId,
                    CreatedAt = DateTime.UtcNow,
                    Theme = theme,
                    ReportId = repostInfo.Id
                }).ToList();

                await _repository.SaveTopThemesAsync(topThemeEntities);
            }

            // 6. Store Recommendations (max 3)
            var topRecommendations = chatResponse.TopRecommendations?.Take(3).ToList() ?? new List<string>();
            if (topRecommendations.Any())
            {
                var recommendationEntities = topRecommendations.Select(rec => new TopRecommendation
                {
                    UnitId = (uint)unitId,
                    CreatedAt = DateTime.UtcNow,
                    Recommendation = rec,
                    ReportId = repostInfo.Id
                }).ToList();

                await _repository.SaveTopRecommendationsAsync(recommendationEntities);
            }

            // 7. Store Top Actions (max 3)
            var topActions = chatResponse.TopActions?.Take(3).ToList() ?? new List<string>();
            if (topActions.Any())
            {
                var actionEntities = topActions.Select(action => new TopAction
                {
                    UnitId = (uint)unitId,
                    CreatedAt = DateTime.UtcNow,
                    Action = action,
                    ReportId = repostInfo.Id
                }).ToList();

                await _repository.SaveTopActionsAsync(actionEntities);
            }

            await _repository.SaveInsightPublishLogAsync(new InsightPublishLog
            {
                ReportId           = repostInfo.Id,
                RunId              = 0,
                UserId             = null,
                DateFrom           = repostInfo.FromDate,
                DateTo             = repostInfo.ToDate,
                TopThemes          = System.Text.Json.JsonSerializer.Serialize(topThemes),
                TopActions         = System.Text.Json.JsonSerializer.Serialize(topActions),
                TopRecommendations = System.Text.Json.JsonSerializer.Serialize(topRecommendations),
                CreatedAt          = DateTime.UtcNow
            });

            // 8. Return simplified string-only result
            return new FeedbackInsightsDto
            {
                TopThemes = topThemes,
                TopRecommendations = topRecommendations,
                TopActions = topActions
            };
        }
        public async Task<string> AnalyzeFeedbackBatches(long unitId, int days)
        {
            // 1. Get feedback from DB
            var feedbackList = await _repository.GetDailyFeedbackReportAsync2(unitId, days);
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

            // 3. Retrieve vector context from DocumentEmbedding table
            var vectorContext = await RetrieveVectorContextAsync(feedbackList);

            // 4. Serialize feedback
            var feedbackListSerialized = JsonSerializer.Serialize(feedbackList);

            string pmt = @"{
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
                  ""analysis_instructions.theme_extraction.theme_requirements.max_positives"": 2,
                  ""analysis_instructions.theme_extraction.theme_requirements.max_needs_work"": 2,
                  ""analysis_instructions.theme_extraction.theme_requirements.label_max_length"": 50,
                  ""analysis_instructions.theme_extraction.theme_requirements.example_quotes_count"": 2,
                  ""analysis_instructions.theme_extraction.theme_requirements.quote_word_range.min"": 6,
                  ""analysis_instructions.theme_extraction.theme_requirements.quote_word_range.max"": 18,
                  ""analysis_instructions.action_steps.count"": 2,
                  ""analysis_instructions.action_steps.structure[0].type"": ""worksheet"",
                  ""analysis_instructions.action_steps.structure[0].duration_minutes"": 30,
                  ""analysis_instructions.action_steps.structure[0].requires_worksheet_reference"": ""true"",
                  ""analysis_instructions.action_steps.structure[0].knowledge_base_search"": ""Search for worksheets with format [Element Number] and map to actual blob filenames using file naming rules provided in knowledge_base."",
                  ""analysis_instructions.action_steps.structure[1].type"": ""micro"",
                  ""analysis_instructions.action_steps.structure[1].duration_minutes_range.min"": 2,
                  ""analysis_instructions.action_steps.structure[1].duration_minutes_range.max"": 5,
                  ""analysis_instructions.action_steps.summary_bullets.count"": 2,
                  ""analysis_instructions.action_steps.summary_bullets.max_characters"": 120,
                  ""constraints.pii_policy"": ""Never output company names or personal identifiers"",
                  ""constraints.rounding"": ""Round percentages to 1 decimal place"",
                  ""constraints.confidence_threshold"": ""Only include themes with 3+ supporting responses"",
                  ""constraints.worksheet_identifier_format"": ""[Element] [Number] format required (e.g., Communication 2, Vulnerability 6)"",
                  ""constraints.knowledge_base_usage"": ""Action 1 MUST reference a specific worksheet from the knowledge base and the SuggestedWorksheet.FileName MUST match actual blob filename format (see file_naming_rules)."",
                  ""knowledge_base"": {
                    ""categories"": [""Communication"", ""Enablement"", ""Learning"", ""Opportunity"", ""Reflection"", ""Vulnerability""],
                    ""file_naming_rules"": {
                      ""description"": ""Actual blob files use the format 'Worksheet <CategoryInitial><Number>.pdf' (examples below). When producing SuggestedWorksheet.FileName use this exact blob filename format."",
                      ""pattern_examples"": {
                        ""Communication 1"": ""Worksheet C1.pdf"",
                        ""Communication 2"": ""Worksheet C2.pdf"",
                        ""Learning 1"": ""Worksheet L1.pdf"",
                        ""Learning 2"": ""Worksheet L2.pdf"",
                        ""Vulnerability 6"": ""Worksheet V6.pdf""
                      },
                      ""rule"": ""Map the assistant's human-readable identifier '<Category> <Number>' to blob filename 'Worksheet <CategoryInitial><Number>.pdf'.""
                    }
                  }
                }";

            var prompt = $@"
                {pmt}

                You are an assistant that MUST return ONLY valid JSON.
                FOLLOW ALL ANALYSIS INSTRUCTIONS FROM THE SPECIFICATION ABOVE to analyze the feedback data.
                Apply the sentiment scoring, theme extraction, and action step generation logic as defined.

                BUT return ONLY a simplified JSON object that matches this schema:
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
                - TopThemes: Extract the top 2 positive and top 2 needs-work themes as **two-line concise statements** (each theme may be up to two short sentences, not exceeding two lines)
                - TopRecommendations: Generate 2–3 key strategic recommendations, each written as **two-line insights** that provide clarity and context (avoid overly short single-line text)
                - TopActions: Create 2–3 actionable steps (each up to **two lines maximum**) — include worksheet references where appropriate
                - SuggestedWorksheet: From the entire analysis, select ONLY ONE most relevant worksheet and:
                    • Provide Category (one of the categories)
                    • Provide FileName in exact blob format (e.g., 'Worksheet L8.pdf')

                Important rules (ENFORCE STRICTLY):
                1) The assistant must NOT invent file names that don't follow the 'Worksheet <Initial><Number>.pdf' pattern.
                2) Each Theme, Recommendation, and Action MUST be two lines maximum — not shorter or longer.
                3) Return ONLY raw JSON. No explanations. No markdown.

                {(string.IsNullOrWhiteSpace(vectorContext) ? "" : vectorContext)}

                Feedback:
                {feedbackListSerialized}
                ";

            // 5. === Batch API Flow ===
            string rawResponse;
            rawResponse = await _azureRagService.AnswerBatchAsync(prompt, unitId, days);


            return rawResponse;

        }
        public async Task<string> AnalyzeFeedbackPromptData(string startPoint, IFormFile file, string endpoint)
        {
            // 1. Resolve model configuration from DB (same pattern as upload-prompt-data-file-embedding)
            ModelConfiguration modelConfig = null;

            if (!string.IsNullOrWhiteSpace(endpoint))
                modelConfig = await _docEmbeddingRepository.GetModelByNameAsync(endpoint);

            modelConfig ??= await _docEmbeddingRepository.GetDefaultModelAsync(modelConfigTypeId: 1);

            if (modelConfig == null)
                throw new InvalidOperationException("No valid ModelConfiguration found (neither by name nor default).");

            // 2. Read feedback from Excel
            var importList = await ImportPromptDataAsync(file);
            if (!importList.Any())
            {
                return null;
            }

            // 3. Map ImportPromptDataDto -> DailyFeedbackReportRowDto
            var feedbackList = importList.Select(obj =>
            {
                var dto = new DailyFeedbackReportRowDto
                {
                    QuestionText = obj.Question,
                    Answer = obj.Answer,
                    ReasonText = obj.Reason
                };

                // map CloverCategory enum if valid
                if (Enum.TryParse<CloverCategory>(obj.CloverCategory, out var category))
                {
                    dto.SpecificationCloverCategoryName = category.ToString();
                }
                else
                {
                    dto.SpecificationCloverCategoryName = obj.CloverCategory;
                }

                //Sentiment Type
                if (Enum.TryParse<DailyFeedbackSentimentType>(obj.Sentiment, out var sentiment))
                {
                    dto.SentimentTypeName = sentiment.ToString();
                }

                return dto;
            }).ToList();

            // 4. Build prompt
            var prompt = await BuildPromptExelData(startPoint, feedbackList, modelConfig.Endpoint);

            // 5. Get response from Azure OpenAI using DB-driven model config
            var rawResponse = await CallChatCompletionAsync(prompt);

            // 6. Strip markdown code-block wrapping if present
            var cleaned = rawResponse?.Trim();
            if (cleaned != null && cleaned.StartsWith("```"))
            {
                int first = cleaned.IndexOf('{');
                int last = cleaned.LastIndexOf('}');
                if (first >= 0 && last > first)
                    cleaned = cleaned.Substring(first, last - first + 1);
            }

            return cleaned;
        }
        private async Task<string> RetrieveVectorContextAsync(List<DailyFeedbackReportRowDto> feedbackRows)
        {
            try
            {
                var categories = feedbackRows
                    .Where(r => !string.IsNullOrWhiteSpace(r.SpecificationCloverCategoryName))
                    .Select(r => r.SpecificationCloverCategoryName)
                    .Distinct();

                var reasons = feedbackRows
                    .Where(r => !string.IsNullOrWhiteSpace(r.ReasonText))
                    .Select(r => r.ReasonText)
                    .Take(10);

                var query = string.Join(". ", categories.Concat(reasons));
                if (string.IsNullOrWhiteSpace(query))
                    return string.Empty;

                var docs = await _embeddingService.SearchAsync(new SearchDocumentEmbeddingRequest
                {
                    Query = query,
                    TopK = 5
                });

                if (!docs.Any())
                    return string.Empty;

                var sb = new StringBuilder();
                sb.AppendLine("### RETRIEVED CONTEXT FROM KNOWLEDGE BASE ###");
                foreach (var doc in docs)
                {
                    sb.AppendLine($"[{doc.Title}]");
                    var content = doc.Content?.Length > 1500
                        ? doc.Content.Substring(0, 1500) + "..."
                        : doc.Content;
                    sb.AppendLine(content);
                    sb.AppendLine();
                }
                sb.AppendLine("### END RETRIEVED CONTEXT ###");
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private string BuildPrompt(List<DailyFeedbackReportRowDto> feedbackRows, string vectorContext = "")
        {
            // Fetch static part from DB
            var staticPrompt = _context.LLMPrompts
                .Where(p => p.Id == 1)
                .Select(p => p.InitialPrompt)
                .FirstOrDefault();

            var sb = new StringBuilder();
            sb.AppendLine(staticPrompt);

            if (!string.IsNullOrWhiteSpace(vectorContext))
                sb.AppendLine(vectorContext);

            // Add dynamic feedback rows
            foreach (var f in feedbackRows)
            {
                sb.AppendLine(

                    $"CloverCategory: {f.SpecificationCloverCategoryName}, " +
                    $"Question: {f.QuestionText}, " +
                    $"Answer: {f.FreeText}, " +
                    $"Score: {f.Score}, " +
                    $"Reason: {f.ReasonText}"
                );
            }

            sb.AppendLine();
            sb.AppendLine("Instructions:");
            sb.AppendLine("1. Assess the reasons provided (both prepopulated and free text) and provide 3 positives and 3 areas that need work.");
            sb.AppendLine("2. Provide a report structured with the following top-level sections:");
            sb.AppendLine("   - TopThemes: array of key recurring themes.");
            sb.AppendLine("   - TopRecommendations: array of actionable recommendations.");
            sb.AppendLine("   - TopActions: array of key observed or suggested actions.");
            sb.AppendLine();
            sb.AppendLine("Important: Return only a single JSON object containing these top-level keys: TopThemes, TopRecommendations, TopActions, QualitativeInsights, StatisticalAnalysis, Recommendation, RelevantWorksheet.");
            sb.AppendLine("Do not include any explanations outside the JSON. The JSON structure must match this format exactly:");
            sb.AppendLine("{");
            sb.AppendLine("  \"TopThemes\": [\"string\"],");
            sb.AppendLine("  \"TopRecommendations\": [\"string\"],");
            sb.AppendLine("  \"TopActions\": [\"string\"],");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private async Task<string> BuildPromptExelData(string startPoint, List<DailyFeedbackReportRowDto> feedbackRows, string endpoint)
        {
            var sb = new StringBuilder();

            // Add static prompt
            sb.AppendLine(startPoint);

            // Add extra context from API parameters
            sb.AppendLine($"StartPoint Info: {startPoint}");
            sb.AppendLine($"Endpoint Info: {endpoint}");
            sb.AppendLine();

            // Add dynamic feedback rows from Excel
            foreach (var f in feedbackRows)
            {
                sb.AppendLine(
                    $"CloverCategory: {f.SpecificationCloverCategoryName}, " +
                    $"Question: {f.QuestionText}, " +
                    $"Sentiment: {f.SentimentType}, " +
                    $"Answer: {f.Answer}," +
                    $"Reason: {f.ReasonText}"
                );
            }

            sb.AppendLine();
            sb.AppendLine(endpoint);

            return sb.ToString();
        }
        public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(long unitId, int daysBack)
        {
            return await _repository.GetDashboardSummaryAsync(unitId, daysBack);
        }
        public async Task<DashboardSummaryDto> GetDashboardDateRangeAsync(long unitId, DateTime fromDate, DateTime toDate)
        {
            return await _repository.GetDashboardDateRangeAsync(unitId, fromDate, toDate);
        }
        public async Task<bool> CheckBusinessUnitAsync(long unitId)
        {
            return await _repository.BusinessUnitExistsAsync(unitId);
        }
        public async Task<TopDataResponse> GetTopDataAsync(long unitId)
        {
            var recommendations = await _repository.GetTopRecommendationsByUnitIdAsync(unitId);
            var themes = await _repository.GetTopThemesByUnitIdAsync(unitId);
            var actions = await _repository.GetTopActionsByUnitIdAsync(unitId);


            //var cutoffDate = DateTime.UtcNow.AddDays(-14);
            //int daysDifference = (DateTime.UtcNow - cutoffDate).Days;

            //var latestRecommendation = recommendations.FirstOrDefault();
            //var latestTheme = themes.FirstOrDefault();
            //var latestAction = actions.FirstOrDefault();

            //if ((latestRecommendation == null || latestRecommendation.CreatedAt <= cutoffDate) ||
            //       (latestTheme == null || latestTheme.CreatedAt <= cutoffDate) ||
            //       (latestAction == null || latestAction.CreatedAt <= cutoffDate))
            //{
            //    await AnalyzeFeedbackAsync2(unitId, daysDifference);

            //    // reload after analysis
            //    recommendations = await _repository.GetTopRecommendationsByUnitIdAsync(unitId);
            //    themes = await _repository.GetTopThemesByUnitIdAsync(unitId);
            //    actions = await _repository.GetTopActionsByUnitIdAsync(unitId);

            //    return new TopDataResponse
            //    {
            //        Recommendations = recommendations.Select(r => r.Recommendation).ToList(),
            //        Themes = themes.Select(t => t.Theme).ToList(),
            //        Actions = actions.Select(a => a.Action).ToList()
            //    };
            //}



            // If any one of the three lists is missing, regenerate using LLM
            //if (!recommendations.Any() || !themes.Any() || !actions.Any())
            //{

            //    await AnalyzeFeedbackAsync2(unitId, 90);

            //    // Re-fetch after regeneration
            //    recommendations = await _repository.GetTopRecommendationsByUnitIdAsync(unitId);
            //    themes = await _repository.GetTopThemesByUnitIdAsync(unitId);
            //    actions = await _repository.GetTopActionsByUnitIdAsync(unitId);
            //}

            return new TopDataResponse
            {
                Recommendations = recommendations.Select(r => r.Recommendation).ToList(),
                Themes = themes.Select(t => t.Theme).ToList(),
                Actions = actions.Select(a => a.Action).ToList()
            };
        }
        public async Task<string> DumpUsersAsync()
        {
            var companyUnits = await _repository.GetAllCompanyUnitPairsAsync();
            int createdCount = 0;

            foreach (var pair in companyUnits.DistinctBy(x => new { x.ProfileId, x.BusinessUnitId }))
            {
                int profileId = pair.ProfileId;
                int? unitId = pair.BusinessUnitId == 0 ? null : pair.BusinessUnitId;

                // If no unit exists for the company, create a new BusinessUnit
                if (unitId == null)
                {
                    var company = await _repository.GetCompanyByIdAsync(profileId);
                    if (company == null)
                        continue;

                    // Generate codes and create a new business unit
                    var businessUnitCodes = BussinessManager.GenerateCodes();
                    var dailyQuestionSetGroup = await _context.DailyQuestionSetGroups.FirstOrDefaultAsync(g => g.Id == 1);
                    var newUnit = new BusinessUnit
                    {
                        BusinessUnitCode = businessUnitCodes.Code,
                        BusinessUnitShortCode = businessUnitCodes.ShortCode,
                        Name = company.ProfileName,
                        NameInvariant = company.ProfileName,
                        RegistrationDate = DateTime.UtcNow,
                        Profile = company,
                        // Contact details are inherited from the profile: this unit is
                        // synthesised for a company that has none.
                        ContactFirstName = company.ContactFirstName ?? company.ProfileName,
                        ContactLastName = company.ContactLastName ?? company.ProfileName,
                        ContactEmailAddress = company.ContactEmailAddress ?? company.DomainName,
                        ActiveDailyQuestionSetGroupId = dailyQuestionSetGroup.Id,
                        ActiveDailyQuestionSetGroup = dailyQuestionSetGroup

                    };

                    uint bunitId = await _repository.CreateBussinessUnitAsync(newUnit);

                    unitId = (int)bunitId;

                    // Update pair for later use
                    pair.BusinessUnitId = (int)bunitId;
                }

                // Check if a CompanyLink already exists — skip if yes
                bool exists = await _repository.CompanyLinkExistsAsync(profileId, unitId.Value);
                if (exists)
                    continue;

                // Check if a user already exists for the given email
                var existingUser = await _repository.GetUserByEmailAsync(pair.Email);
                User user;

                if (existingUser != null)
                {
                    user = existingUser;
                }
                else
                {
                    // Create a new user
                    user = new User
                    {
                        Username = pair.Email,
                        Name = pair.CompanyName,
                        Password = PasswordUtility.Encrypt("12345678"),
                        CreatedDate = DateTime.UtcNow.ToString("o"),
                        ModifiedDate = DateTime.UtcNow.ToString("o"),
                        IsActive = true,
                        IsDel = false,
                        UserGroupId = (int)CloverBackoffice.Components.Enums.UserGroups.ProfileAdmin,
                        LastLoginDate = null,
                        IsVerified = false,
                        Status = UserStatus.Pending.ToString(),
                        CreatedBy = CreatedByType.System.ToString(),
                        TenantId = "",
                        Type = UserType.Unit.ToString()
                    };

                    user.UserId = await _repository.CreateUserAsync(user);
                }

                // Create the CompanyLink for user, company, and unit
                var link = new CompanyLink
                {
                    ProfileId = profileId,
                    UserId = user.UserId,
                    ParentId = 0,
                    UnitId = unitId.Value,
                    ExpiryDate = string.Empty,
                    BussinessUnitId = unitId.Value
                };

                await _repository.CreateCompanyLinkAsync(link);
                createdCount++;
            }

            return $"{createdCount} users created and linked successfully.";
        }

        public async Task<IEnumerable<LLMReportDto>> GetReportsByUnitAsync(long unitId)
        {
            var recommendations = await _repository.GetTopRecommendationsByUnitIdAsync(unitId);
            var themes = await _repository.GetTopThemesByUnitIdAsync(unitId);
            var actions = await _repository.GetTopActionsByUnitIdAsync(unitId);

            //var cutoffDate = DateTime.UtcNow.AddDays(-14);
            //int daysDifference = (DateTime.UtcNow - cutoffDate).Days;

            //var latestRecommendation = recommendations.FirstOrDefault();
            //var latestTheme = themes.FirstOrDefault();
            //var latestAction = actions.FirstOrDefault();

            //if ((latestRecommendation == null || latestRecommendation.CreatedAt <= cutoffDate) ||
            //        (latestTheme == null || latestTheme.CreatedAt <= cutoffDate) ||
            //        (latestAction == null || latestAction.CreatedAt <= cutoffDate))
            //{
            //    await AnalyzeFeedbackAsync2(unitId, daysDifference);

            //    // reload after analysis
            //    recommendations = await _repository.GetTopRecommendationsByUnitIdAsync(unitId);
            //    themes = await _repository.GetTopThemesByUnitIdAsync(unitId);
            //    actions = await _repository.GetTopActionsByUnitIdAsync(unitId);
            //}

            var reports = await _repository.GetReportsByUnitAsync(unitId);

            return reports.Select(r => new LLMReportDto
            {
                Id = r.Id,
                DateRange = r.FromDate.ToString("dd/MM/yyyy") + " - " + r.ToDate.ToString("dd/MM/yyyy")
            }).ToList();
        }

        public async Task<TopReportDataResponse> GetTopReportDataAsync(long Id, long unitId)
        {
            // Get recommendations, themes, and actions (unchanged logic)
            var recommendations = await _repository.GetTopRecommendationsByUnitIdAndReportIdAsync(Id, unitId);
            var themes = await _repository.GetTopThemesByUnitIdAndReportAsync(Id, unitId);
            var actions = await _repository.GetTopActionsByUnitIdAndReportAsync(Id, unitId);

            var reports = await _repository.GetReportsByUnitAndReportIdAsync(Id, unitId);
            OverallScoreDto overallScore = null;
            TrendChartDto trendChart = null;
            List<HealthScoreDto> healthScores = new();
            List<MetricDto> metrics = new();
            string? recommendedWorkSheet = null;

            if (reports != null)
            {
                // Convert DateTime to DateTimeOffset for the aggregation service
                var fromDate = new DateTimeOffset(reports.FromDate, TimeSpan.Zero);
                var toDate = new DateTimeOffset(reports.ToDate, TimeSpan.Zero);

                // Use the same aggregation logic as aggregate-stats-analysis
                // Get unitIds with parent-child aggregation logic
                var unitIds = await _aggregateDashboardService.GetBusinessUnitIdsForAnalysisAsync(unitId, fromDate, toDate);

                if (unitIds != null && unitIds.Any())
                {
                    // Use the same analysis logic as aggregate-stats-analysis
                    var dashboardSummary = await _aggregateDashboardService.AnalyzeFeedbackRunTimeAsync(unitIds, fromDate, toDate);

                    if (dashboardSummary != null)
                    {
                        overallScore = dashboardSummary.OverallScore;
                        trendChart = dashboardSummary.TrendChart;
                        healthScores = dashboardSummary.HealthScores ?? new List<HealthScoreDto>();
                        metrics = dashboardSummary.Metrics ?? new List<MetricDto>();
                    }
                }

                recommendedWorkSheet = await ResolveRecommendedWorksheetAsync(reports);
            }

            return new TopReportDataResponse
            {
                OverallScore = overallScore,
                TrendChart = trendChart,
                HealthScores = healthScores,
                Metrics = metrics,
                Recommendations = recommendations.Select(r => r.Recommendation).ToList(),
                Themes = themes.Select(t => t.Theme).ToList(),
                Actions = actions.Select(a => a.Action).ToList(),
                RecommendedWorkSheet = recommendedWorkSheet
            };
        }

        // Manual reports persist the worksheet on LLMReport.WorksheetFile at create.
        // Workbench reports leave it null — derive from the linked WorkbenchRunStatus
        // tests against the worksheet corpus.
        private async Task<string?> ResolveRecommendedWorksheetAsync(LLMReport report)
        {
            if (!string.IsNullOrWhiteSpace(report.WorksheetFile))
            {
                return WorksheetMatcher.BuildWorksheetAnchor(
                    WorksheetMatcher.FormatWorksheetName(report.WorksheetFile));
            }

            var publishLog = await _context.InsightPublishLogs
                .Where(l => l.ReportId == report.Id && l.RunId > 0)
                .OrderByDescending(l => l.Id)
                .FirstOrDefaultAsync();
            if (publishLog == null) return null;

            var runStatus = await _repository.GetWorkbenchRunStatusByIdAsync(publishLog.RunId);
            if (runStatus == null || string.IsNullOrWhiteSpace(runStatus.ResultJson)) return null;

            var evalResult = WorksheetMatcher.ExtractEvalResultFromResultJson(runStatus.ResultJson);
            if (evalResult?.TestResults == null || evalResult.TestResults.Count == 0) return null;

            var worksheetCorpus = await _documentBatchesRepo.GetWorksheetFileContentsAsync();
            var match = WorksheetMatcher.MatchWorksheetFromDb(evalResult.TestResults, worksheetCorpus);
            return WorksheetMatcher.BuildWorksheetAnchor(match);
        }

        public async Task<LLMReportRequestDto> CreateReportAsync(LLMReportRequestDto dto)
        {
            var reports = await _repository.GetReportsByDateRange(dto.FromDate, dto.ToDate);

            if (reports != null)
            {
                var recommendations = await _repository.GetTopRecommendationsByUnitIdAndReportIdAsync(reports.Id, dto.UnitId);
                var themes = await _repository.GetTopThemesByUnitIdAndReportAsync(reports.Id, dto.UnitId);
                var actions = await _repository.GetTopActionsByUnitIdAndReportAsync(reports.Id, dto.UnitId);

                if (recommendations != null && recommendations.Any())
                {
                    await _repository.DeleteRecommendationsAsync(recommendations);
                }
                if (themes != null && themes.Any())
                {
                    await _repository.DeleteThemesAsync(themes);
                }
                if (actions != null && actions.Any())
                {
                    await _repository.DeleteActionsAsync(actions);
                }

                await _repository.DeleteReportAsync(reports);

            }

            await _repository.CreateDailyFeedbackReportAsync(dto.UnitId, dto.FromDate, dto.ToDate);
            var report = new LLMReport()
            {
                FromDate = dto.FromDate,
                ToDate = dto.ToDate,
                UnitId = dto.UnitId,
                WorksheetFile = dto.WorksheetFile,
                CreatedAt = DateTime.UtcNow
            };
            var repostInfo = await _repository.CreateReportAsync(report);

            if (dto.TopThemes != null && dto.TopThemes.Any())
            {
                var topThemeEntities = dto.TopThemes.Select(theme => new TopTheme
                {
                    UnitId = (uint)dto.UnitId,
                    CreatedAt = DateTime.UtcNow,
                    Theme = theme,
                    ReportId = repostInfo.Id
                }).ToList();

                await _repository.SaveTopThemesAsync(topThemeEntities);
            }

            // 6. Store Recommendations (max 3)
            if (dto.TopRecommendations != null && dto.TopRecommendations.Any())
            {
                var recommendationEntities = dto.TopRecommendations.Select(rec => new TopRecommendation
                {
                    UnitId = (uint)dto.UnitId,
                    CreatedAt = DateTime.UtcNow,
                    Recommendation = rec,
                    ReportId = repostInfo.Id
                }).ToList();

                await _repository.SaveTopRecommendationsAsync(recommendationEntities);
            }

            // 7. Store Top Actions 
            if (dto.TopActions.Any() && dto.TopActions != null)
            {
                var actionEntities = dto.TopActions.Select(action => new TopAction
                {
                    UnitId = (uint)dto.UnitId,
                    CreatedAt = DateTime.UtcNow,
                    Action = action,
                    ReportId = repostInfo.Id
                }).ToList();

                await _repository.SaveTopActionsAsync(actionEntities);
            }

            await _repository.SaveInsightPublishLogAsync(new InsightPublishLog
            {
                ReportId           = repostInfo.Id,
                RunId              = 0,
                UserId             = null,
                DateFrom           = repostInfo.FromDate,
                DateTo             = repostInfo.ToDate,
                TopThemes          = System.Text.Json.JsonSerializer.Serialize(dto.TopThemes          ?? new List<string>()),
                TopActions         = System.Text.Json.JsonSerializer.Serialize(dto.TopActions         ?? new List<string>()),
                TopRecommendations = System.Text.Json.JsonSerializer.Serialize(dto.TopRecommendations ?? new List<string>()),
                CreatedAt          = DateTime.UtcNow
            });

            var reportSave = new LLMReportRequestDto
            {
                UnitId = dto.UnitId,
                FromDate = dto.FromDate,
                ToDate = dto.ToDate,
                WorksheetFile = dto.WorksheetFile,
                TopActions = dto.TopActions,
                TopRecommendations = dto.TopRecommendations,
                TopThemes = dto.TopThemes
            };

            // Email + push to the unit's assigned manager. The service contains
            // its own failures, so a provider outage can't fail an
            // already-created report.
            //
            // This path skips the admin review queue on purpose: it writes its
            // InsightPublishLog row above, which means the report is already
            // published in the sense the rest of the product uses — an operator
            // assembled and published it by hand, and the review step exists to
            // put a human in front of MACHINE-generated output.
            await _reportNotificationService.NotifyReportReadyAsync(
                dto.UnitId, repostInfo.Id, dto.FromDate, dto.ToDate);

            return reportSave;
        }
        public async Task<List<FeedbackDataResultDto>> FeedbackDataAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate)
        {
            List<FeedbackDataResultDto> DailyFeedbackReportRowDtos = new List<FeedbackDataResultDto>();
            DailyFeedbackReportRowDtos = await _repository.GetFeedbackDataAsyncRuntime(unitId, fromDate, toDate);
            return DailyFeedbackReportRowDtos;
        }

        public async Task<List<FeedbackDataResultDto>> FeedbackDataAsync(long profileId, long unitId, DateTimeOffset fromDate, DateTimeOffset toDate)
        {
            return await _repository.GetFeedbackDataAsyncRuntime(profileId, unitId, fromDate, toDate);
        }
        public async Task<List<WorkAllocationResultDto>> WorkAllocationDataAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate)
        {
            return await _repository.GetWorkAllocationDataAsync(unitId, fromDate, toDate);
        }
        public async Task<List<DailyReflectionLinesDto>> DailyReflectionLinesAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate)
        {
            return await _repository.GetDailyReflectionLinesAsync(unitId, fromDate, toDate);
        }
        public async Task ImportExcelAsync(IFormFile file)
        {
            try
            {
                OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

                using var stream = new MemoryStream();
                await file.CopyToAsync(stream);
                using var package = new ExcelPackage(stream);
                var worksheet = package.Workbook.Worksheets[0];
                int rowCount = worksheet.Dimension.Rows;

                // Preload all existing data
                var businessUnits = await _context.BusinessUnits.ToListAsync();
                var questions = await _context.DailyQuestions.ToListAsync();
                var reasons = await _context.DailyReasons.ToListAsync();

                var feedbacksToAdd = new List<DailyFeedback>();

                for (int row = 2; row <= rowCount; row++) // skip header
                {
                    try
                    {
                        var dto = new ExcelFeedbackDto
                        {
                            SubmissionDateTime = DateTimeOffset.Parse(worksheet.Cells[row, 1].Text),
                            BusinessUnitName = worksheet.Cells[row, 2].Text?.Trim(),
                            CompanyName = worksheet.Cells[row, 3].Text?.Trim(),
                            QuestionText = worksheet.Cells[row, 4].Text?.Trim(),
                            Category = worksheet.Cells[row, 5].Text?.Trim(),
                            SentimentType = int.TryParse(worksheet.Cells[row, 6].Text, out var st) ? st : 0,
                            ReasonText = worksheet.Cells[row, 7].Text?.Trim(),
                            FreeText = worksheet.Cells[row, 8].Text?.Trim()
                        };

                        // BusinessUnit is required
                        var businessUnit = businessUnits.FirstOrDefault(b =>
                            b.Name.Trim().Equals(dto.BusinessUnitName, StringComparison.OrdinalIgnoreCase));

                        if (businessUnit == null)
                        {
                            continue; // Skip row if BusinessUnit not found
                        }

                        // Question must exist
                        var question = questions.FirstOrDefault(q =>
                            q.Text.Trim().Equals(dto.QuestionText, StringComparison.OrdinalIgnoreCase));

                        // Reason must exist
                        var reason = reasons.FirstOrDefault(r =>
                            r.Text.Trim().Equals(dto.ReasonText, StringComparison.OrdinalIgnoreCase));

                        // Skip if either Question OR Reason is missing
                        if (question == null || reason == null)
                        {
                            continue;
                        }

                        //  Only insert when BOTH Question and Reason exist
                        var feedback = new DailyFeedback
                        {
                            SubmissionDateTime = dto.SubmissionDateTime.UtcDateTime,
                            BusinessUnitId = businessUnit.Id,
                            SentimentType = (DailyFeedbackSentimentType)dto.SentimentType,
                            FreeText = dto.FreeText,
                            Question = question,
                            Reason = reason
                        };

                        feedbacksToAdd.Add(feedback);
                    }
                    catch
                    {
                        continue; // Skip row on error
                    }
                }

                // Save all feedbacks once, outside loop
                if (feedbacksToAdd.Any())
                {
                    await _context.DailyFeedbacks.AddRangeAsync(feedbacksToAdd);
                    await _context.SaveChangesAsync();
                }
            }
            catch
            {
                throw;
            }
        }

        public async Task<List<ImportPromptDataDto>> ImportPromptDataAsync(IFormFile file)
        {
            // Required for EPPlus
            OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

            var feedbacks = new List<ImportPromptDataDto>();

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            using var package = new ExcelPackage(stream);

            var worksheet = package.Workbook.Worksheets[0];
            int rowCount = worksheet.Dimension.Rows;

            for (int row = 2; row <= rowCount; row++)
            {
                var dto = new ImportPromptDataDto
                {
                    CloverCategory = worksheet.Cells[row, 1].Text?.Trim(),
                    Question = worksheet.Cells[row, 2].Text?.Trim(),
                    Sentiment = worksheet.Cells[row, 3].Text?.Trim(),
                    Answer = worksheet.Cells[row, 4].Text?.Trim(),
                    Reason = worksheet.Cells[row, 5].Text?.Trim(),

                };

                // Only add if at least one column is non-empty
                if (!string.IsNullOrWhiteSpace(dto.CloverCategory) ||
                    !string.IsNullOrWhiteSpace(dto.Question) ||
                    !string.IsNullOrWhiteSpace(dto.Sentiment) ||
                    !string.IsNullOrWhiteSpace(dto.Answer) ||
                    !string.IsNullOrWhiteSpace(dto.Reason))
                {
                    feedbacks.Add(dto);
                }
            }

            return feedbacks;
        }

        public async Task<List<QuestionDto>> GetQuestionListAsync()
        {
            return await _repository.GetQuestionListAsync();
        }

        public async Task<BusinessUnitBatchJobsDto> GetBatchJobsWithInsightsAsync(int businessUnitId)
        {
            await _azureRagService.CheckBatchJobAzureResults(businessUnitId);
            var batchJobs = await _repository.GetBatchJobsWithInsightsByBusinessUnitIdAsync(businessUnitId);

            return new BusinessUnitBatchJobsDto
            {
                BusinessUnitId = businessUnitId,
                BatchJobs = batchJobs
            };
        }

        //hangfile logic
        public async Task<BatchJob?> GetLatestJobAsync(uint businessUnitId)
        {
            return await _repository.GetLatestByBusinessUnitAsync(businessUnitId);
        }

        public async Task<BatchJob> CreateJobAsync(uint businessUnitId, string requestType, string metadataJson)
        {
            var job = new BatchJob
            {
                Id = Guid.NewGuid(),
                BusinessUnitId = businessUnitId,
                Status = "Submitted",
                RequestType = requestType,
                SubmittedAt = DateTime.UtcNow,
                MetadataJson = metadataJson
            };

            await _repository.AddAsync(job);
            return job;
        }

        public async Task UpdateJobStatusAsync(Guid jobId, string status, string? errorMessage = null)
        {
            // Load exact job by Id to avoid updating wrong rows
            var job = await _repository.GetBatchJobByIdAsync(jobId);
            if (job == null)
                throw new InvalidOperationException("Batch job not found");

            job.Status = status;
            if (!string.IsNullOrEmpty(errorMessage))
                job.ErrorMessage = errorMessage;

            if (status == "Completed")
                job.CompletedAt = DateTime.UtcNow;

            await _repository.UpdateAsync(job);
        }
        public async Task<List<BusinessUnit>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return await _repository.GetAllAsync(cancellationToken);
        }

        public async Task<BatchJobAzureDto> AnalyzeFeedbackAzureResults([FromQuery] long unitId, [FromQuery] DateTimeOffset fromDate, [FromQuery] DateTimeOffset toDate)
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

            // 3. Retrieve vector context and serialize feedback
            var vectorContext = await RetrieveVectorContextAsync(feedbackList);
            var feedbackListSerialized = JsonSerializer.Serialize(feedbackList);

            string pmt = @"{
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
                  ""analysis_instructions.theme_extraction.theme_requirements.max_positives"": 2,
                  ""analysis_instructions.theme_extraction.theme_requirements.max_needs_work"": 2,
                  ""analysis_instructions.theme_extraction.theme_requirements.label_max_length"": 50,
                  ""analysis_instructions.theme_extraction.theme_requirements.example_quotes_count"": 2,
                  ""analysis_instructions.theme_extraction.theme_requirements.quote_word_range.min"": 6,
                  ""analysis_instructions.theme_extraction.theme_requirements.quote_word_range.max"": 18,
                  ""analysis_instructions.action_steps.count"": 2,
                  ""analysis_instructions.action_steps.structure[0].type"": ""worksheet"",
                  ""analysis_instructions.action_steps.structure[0].duration_minutes"": 30,
                  ""analysis_instructions.action_steps.structure[0].requires_worksheet_reference"": ""true"",
                  ""analysis_instructions.action_steps.structure[0].knowledge_base_search"": ""Search for worksheets with format [Element Number] and map to actual blob filenames using file naming rules provided in knowledge_base."",
                  ""analysis_instructions.action_steps.structure[1].type"": ""micro"",
                  ""analysis_instructions.action_steps.structure[1].duration_minutes_range.min"": 2,
                  ""analysis_instructions.action_steps.structure[1].duration_minutes_range.max"": 5,
                  ""analysis_instructions.action_steps.summary_bullets.count"": 2,
                  ""analysis_instructions.action_steps.summary_bullets.max_characters"": 120,
                  ""constraints.pii_policy"": ""Never output company names or personal identifiers"",
                  ""constraints.rounding"": ""Round percentages to 1 decimal place"",
                  ""constraints.confidence_threshold"": ""Only include themes with 3+ supporting responses"",
                  ""constraints.worksheet_identifier_format"": ""[Element] [Number] format required (e.g., Communication 2, Vulnerability 6)"",
                  ""constraints.knowledge_base_usage"": ""Action 1 MUST reference a specific worksheet from the knowledge base and the SuggestedWorksheet.FileName MUST match actual blob filename format (see file_naming_rules)."",
                  ""knowledge_base"": {
                    ""categories"": [""Communication"", ""Enablement"", ""Learning"", ""Opportunity"", ""Reflection"", ""Vulnerability""],
                    ""file_naming_rules"": {
                      ""description"": ""Actual blob files use the format 'Worksheet <CategoryInitial><Number>.pdf' (examples below). When producing SuggestedWorksheet.FileName use this exact blob filename format."",
                      ""pattern_examples"": {
                        ""Communication 1"": ""Worksheet C1.pdf"",
                        ""Communication 2"": ""Worksheet C2.pdf"",
                        ""Learning 1"": ""Worksheet L1.pdf"",
                        ""Learning 2"": ""Worksheet L2.pdf"",
                        ""Vulnerability 6"": ""Worksheet V6.pdf""
                      },
                      ""rule"": ""Map the assistant's human-readable identifier '<Category> <Number>' to blob filename 'Worksheet <CategoryInitial><Number>.pdf'.""
                    }
                  }
                }";

            var prompt = $@"
                {pmt}

                You are an assistant that MUST return ONLY valid JSON.
                FOLLOW ALL ANALYSIS INSTRUCTIONS FROM THE SPECIFICATION ABOVE to analyze the feedback data.
                Apply the sentiment scoring, theme extraction, and action step generation logic as defined.

                BUT return ONLY a simplified JSON object that matches this schema:
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
                - TopThemes: Extract the top 2 positive and top 2 needs-work themes as **two-line concise statements** (each theme may be up to two short sentences, not exceeding two lines)
                - TopRecommendations: Generate 2–3 key strategic recommendations, each written as **two-line insights** that provide clarity and context (avoid overly short single-line text)
                - TopActions: Create 2–3 actionable steps (each up to **two lines maximum**) — include worksheet references where appropriate
                - SuggestedWorksheet: From the entire analysis, select ONLY ONE most relevant worksheet and:
                    • Provide Category (one of the categories)
                    • Provide FileName in exact blob format (e.g., 'Worksheet L8.pdf')

                Important rules (ENFORCE STRICTLY):
                1) The assistant must NOT invent file names that don't follow the 'Worksheet <Initial><Number>.pdf' pattern.
                2) Each Theme, Recommendation, and Action MUST be two lines maximum — not shorter or longer.
                3) Return ONLY raw JSON. No explanations. No markdown.

                {(string.IsNullOrWhiteSpace(vectorContext) ? "" : vectorContext)}

                Feedback:
                {feedbackListSerialized}
                ";

            // 5. === Batch API Flow ===
            string rawResponse;
            rawResponse = await _azureRagService.AnswerBatchAzureResults(prompt, unitId, 0, fromDate, toDate);

            var batchJob = await _azureRagService.GetBatchJobsAsync(rawResponse);

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

        public async Task<string> SaveInsightsAsync(Guid batchJobId, string jsonResponse, int userId)
        {
            // 1) quick existence check for batch job
            var batchJob = await _repository.GetBatchJobByIdAsync(batchJobId);
            if (batchJob == null)
                return "BatchJob with Id {batchJobId} not found.";

            // 2) parse incoming JSON
            var dto = JsonSerializer.Deserialize<FeedbackInsightsDto>(jsonResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dto == null)
                return "Invalid insights payload.";

            // 3) parse metadata (keep insensitive casing)
            var metadata = string.IsNullOrWhiteSpace(batchJob.MetadataJson) ? new BatchJobMetadataJosn() : JsonSerializer.Deserialize<BatchJobMetadataJosn>(batchJob.MetadataJson);

            // update metadata fields
            metadata.TopTheme = JsonSerializer.Serialize(dto.TopThemes);
            metadata.TopRecomandation = JsonSerializer.Serialize(dto.TopRecommendations);
            metadata.TopAction = JsonSerializer.Serialize(dto.TopActions);
            metadata.Status = "Approved";
            metadata.ApprovedAt = DateTime.UtcNow;
            metadata.ApprovedBy = userId;

            // compute period (fallbacks if metadata doesn't contain dates)
            var periodStart = (metadata.FromDate?.ToUniversalTime()) ?? DateTime.UtcNow.AddDays(-14);
            var periodEnd = (metadata.ToDate?.ToUniversalTime()) ?? DateTime.UtcNow;

            // 4) do all DB changes inside one repository-managed transaction for atomicity
            var resp = await _repository.ExecuteInTransactionAsync(async () =>
            {
                // update batch job metadata + status
                batchJob.MetadataJson = JsonSerializer.Serialize(metadata);
                batchJob.Status = "Approved";
                batchJob.SubmittedAt = (batchJob.SubmittedAt.ToUniversalTime());
                batchJob.CompletedAt = (batchJob.CompletedAt.Value.ToUniversalTime());
                await _repository.UpdateBatchJobAsync(batchJob);

                // fetch engagement insight — we require it to exist (per your requirement)
                var engagementInsight = await _repository.GetEngagementInsightByBatchJobIdAsync(batchJobId);

                // update engagement insight fields
                engagementInsight.GeneratedAt = DateTime.UtcNow;
                engagementInsight.PeriodStart = periodStart.UtcDateTime;
                engagementInsight.PeriodEnd = periodEnd.UtcDateTime;
                engagementInsight.TopThemesJson = JsonSerializer.Serialize(dto.TopThemes?.Where(t => !string.IsNullOrWhiteSpace(t)).Take(5).ToList() ?? new List<string>());
                engagementInsight.RecommendationsJson = JsonSerializer.Serialize(dto.TopRecommendations?.Where(r => !string.IsNullOrWhiteSpace(r)).Take(3).ToList() ?? new List<string>());
                engagementInsight.TopActionsJson = JsonSerializer.Serialize(dto.TopActions?.Where(a => !string.IsNullOrWhiteSpace(a)).Take(3).ToList() ?? new List<string>());

                await _repository.UpdateEngagementInsightAsync(engagementInsight);

                // 5) check for an existing report for same unit + same date range (date-only match)
                var existingReport = await _repository.GetReportByUnitAndDatesAsync(
                    batchJob.BusinessUnitId,
                    periodStart.UtcDateTime,
                    periodEnd.UtcDateTime);

                // if exists, delete the old top-entities then delete the report
                if (existingReport != null)
                {
                    await _repository.DeleteTopThemesByUnitAndReportAsync(batchJob.BusinessUnitId, (int)existingReport.Id);
                    await _repository.DeleteTopRecommendationsByUnitAndReportAsync(batchJob.BusinessUnitId, (int)existingReport.Id);
                    await _repository.DeleteTopActionsByUnitAndReportAsync(batchJob.BusinessUnitId, (int)existingReport.Id);

                    await _repository.DeleteReportAsync((int)existingReport.Id);
                }

                // 6) create a new report and insert new TopThemes/TopRecommendations/TopActions
                var report = new LLMReport
                {
                    FromDate = periodStart.UtcDateTime,
                    ToDate = periodEnd.UtcDateTime,
                    UnitId = batchJob.BusinessUnitId,
                    CreatedAt = DateTime.UtcNow
                };

                var reportInfo = await _repository.CreateReportAsync(report); // returns saved entity with Id

                // save top-themes (max 5)
                var topThemes = dto.TopThemes?.Where(t => !string.IsNullOrWhiteSpace(t)).Take(5).ToList();
                if (topThemes?.Any() == true)
                {
                    var topThemeEntities = topThemes.Select(theme => new TopTheme
                    {
                        UnitId = batchJob.BusinessUnitId,
                        CreatedAt = DateTime.UtcNow,
                        Theme = theme,
                        ReportId = reportInfo.Id
                    });
                    await _repository.SaveTopThemesAsync(topThemeEntities);
                }

                // save top-recommendations (max 3)
                var topRecommendations = dto.TopRecommendations?.Where(r => !string.IsNullOrWhiteSpace(r)).Take(3).ToList();
                if (topRecommendations?.Any() == true)
                {
                    var recEntities = topRecommendations.Select(rec => new TopRecommendation
                    {
                        UnitId = batchJob.BusinessUnitId,
                        CreatedAt = DateTime.UtcNow,
                        Recommendation = rec,
                        ReportId = reportInfo.Id
                    });
                    await _repository.SaveTopRecommendationsAsync(recEntities);
                }

                // save top-actions (max 3)
                var topActions = dto.TopActions?.Where(a => !string.IsNullOrWhiteSpace(a)).Take(3).ToList();
                if (topActions?.Any() == true)
                {
                    var actionEntities = topActions.Select(action => new TopAction
                    {
                        UnitId = batchJob.BusinessUnitId,
                        CreatedAt = DateTime.UtcNow,
                        Action = action,
                        ReportId = reportInfo.Id
                    });
                    await _repository.SaveTopActionsAsync(actionEntities);
                }

                await _repository.SaveInsightPublishLogAsync(new InsightPublishLog
                {
                    ReportId           = reportInfo.Id,
                    RunId              = 0,
                    UserId             = userId,
                    DateFrom           = reportInfo.FromDate,
                    DateTo             = reportInfo.ToDate,
                    TopThemes          = System.Text.Json.JsonSerializer.Serialize(topThemes          ?? new List<string>()),
                    TopActions         = System.Text.Json.JsonSerializer.Serialize(topActions         ?? new List<string>()),
                    TopRecommendations = System.Text.Json.JsonSerializer.Serialize(topRecommendations ?? new List<string>()),
                    CreatedAt          = DateTime.UtcNow
                });
            }); // transaction end

            if (resp)
            {
                return "Insights saved successfully";
            }
            else
            {
                return "Failed to save Insights";
            }

        }

        // AzureFunctionCall / RunJobAsync / RunJobListAsync were removed with the
        // CloverBackoffice.Jobs Function App. AzureFunctionCall was the entry point
        // for POST /api/rag/azure-backgroundjob-function and had no other caller;
        // RunJobListAsync had no live caller at all (its only invocation inside
        // AzureFunctionCall was already commented out). Both were duplicates of the
        // canonical copies in GuardShortageJob, which remain and are now driven by
        // BatchPollingBackgroundService and ReportSchedulerBackgroundService.

        public async Task<(bool IsSuccess, string FilePath, string Message)> GetWorksheetFileAsync(string fileName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName))
                    return (false, null, ResponseMessages.Errors.MissingFileName);

                string folderPath = Path.Combine(_env.WebRootPath, "WorkSheetFile");
                fileName = fileName.Trim();

                // Decode any percent-encoding that reached us literally (e.g. a client
                // or Swagger text box double-encoding a space as "%20"), so the name
                // tokenizes on real spaces. Harmless when already decoded — a plain
                // name has no '%' to unescape. Guarded so a malformed value can't throw.
                if (fileName.Contains('%'))
                {
                    try { fileName = Uri.UnescapeDataString(fileName).Trim(); }
                    catch { /* keep the original on a bad escape sequence */ }
                }

                // CASE 1: Full name match (e.g., "Worksheet E9.pdf")
                string directPath = Path.Combine(folderPath, fileName);
                if (System.IO.File.Exists(directPath))
                    return (true, directPath, ResponseMessages.Success.WorksheetFound);

                // Full name without extension (e.g., "Worksheet E9" -> "Worksheet E9.*")
                var fullNamePatternMatches = Directory.GetFiles(folderPath, fileName + ".*");
                if (fullNamePatternMatches.Length > 0)
                    return (true, fullNamePatternMatches[0], ResponseMessages.Success.WorksheetFound);

                // Extract last token (e.g., "Worksheet E9.pdf" -> token "E9", ext ".pdf")
                string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                string ext = Path.GetExtension(fileName);
                string lastToken = nameNoExt.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries)
                                            .LastOrDefault();

                if (!string.IsNullOrWhiteSpace(lastToken) &&
                    !lastToken.Equals(nameNoExt, StringComparison.OrdinalIgnoreCase))
                {
                    // CASE 2: Short name match (e.g., "E9.pdf")
                    if (!string.IsNullOrEmpty(ext))
                    {
                        string shortPath = Path.Combine(folderPath, lastToken + ext);
                        if (System.IO.File.Exists(shortPath))
                            return (true, shortPath, ResponseMessages.Success.WorksheetFound);
                    }

                    // CASE 3: Last part only, any extension (e.g., "E9.*")
                    var tokenMatches = Directory.GetFiles(folderPath, lastToken + ".*");
                    if (tokenMatches.Length > 0)
                        return (true, tokenMatches[0], ResponseMessages.Success.WorksheetFound);
                }

                // CASE 4: Pillar-alias-aware, case-insensitive match. Resolves a
                // requested name that references a pillar by a different alias to the
                // stored file — e.g. "Worksheet O.pdf" / "Worksheet o.pdf" ->
                // "Worksheet OP.pdf" (O = OP = Opportunity). The alias mapping is the
                // canonical pillar map defined in WorksheetMatcher (the same one the
                // worksheet codes C/L/OP/V/E/R use) — no extra configuration needed.
                var pillarMatch = WorksheetMatcher.ResolveByPillarAlias(folderPath, fileName);
                if (!string.IsNullOrEmpty(pillarMatch))
                    return (true, pillarMatch, ResponseMessages.Success.WorksheetFound);

                return (false, null, ResponseMessages.Errors.FileNotFound);
            }
            catch (Exception ex)
            {
                return (false, null, $"{ResponseMessages.Errors.UnexpectedError}: {ex.Message}");
            }
        }

        public async Task<bool> DeleteReportAsync(long id)
        {
            var report = await _repository.GetReportByIdAsync(id);
            if (report == null)
                return false;

            // Delete linked TopActions, TopRecommendations, TopThemes
            await _repository.DeleteLinkedReportItemsAsync(id);

            // Delete main report
            await _repository.DeleteReportManuallyAsync(report);

            return true;
        }

        // Workbench save + publish. Unchanged entry point for the
        // save-run-insights endpoint.
        public async Task<int> SaveRunInsightsAsync(SaveRunInsightsRequestDto dto, int? userId = null)
            => (await SaveRunInsightsAsync(dto, userId, publish: true)).Outcome;

        public async Task<SaveRunInsightsResultDto> SaveRunInsightsAsync(
            SaveRunInsightsRequestDto dto,
            int? userId,
            bool publish,
            CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            long reportId;
            uint unitIdUint;
            DateTime dateFrom, dateTo;
            bool isInsert;

            var publishLog = await _repository.GetInsightPublishLogByRunIdAsync((int)dto.RunId);

            if (publishLog == null)
            {
                // First entry for this runId — read the run's own metadata.
                var runStatus = await _repository.GetWorkbenchRunStatusByIdAsync((int)dto.RunId);
                if (runStatus == null)
                    return new SaveRunInsightsResultDto { Outcome = 0 };

                var (unitId, fromDate, toDate) = ExtractRunMeta(runStatus);

                // A report may already exist for this run even with no publish
                // log: the scheduler saves its reports unpublished, awaiting
                // review, and they carry the run's own unit and window. Publishing
                // such a report from the workbench must UPDATE it, not mint a
                // second report for the same unit and period.
                //
                // Deliberately limited to reports with no publish-log row at all
                // — a released report is never rewritten from under its manager.
                var pendingForWindow = await _context.LLMReports
                    .Where(r => r.UnitId == unitId && r.FromDate == fromDate && r.ToDate == toDate)
                    .Where(r => !_context.InsightPublishLogs.Any(l => l.ReportId == r.Id))
                    .OrderByDescending(r => r.Id)
                    .FirstOrDefaultAsync(ct);

                if (pendingForWindow != null)
                {
                    reportId   = pendingForWindow.Id;
                    unitIdUint = (uint)pendingForWindow.UnitId;
                    dateFrom   = pendingForWindow.FromDate;
                    dateTo     = pendingForWindow.ToDate;
                    isInsert   = false;
                }
                else
                {
                    var report = await _repository.CreateReportAsync(new LLMReport
                    {
                        FromDate  = fromDate,
                        ToDate    = toDate,
                        UnitId    = unitId,
                        CreatedAt = now
                    });

                    reportId   = report.Id;
                    unitIdUint = (uint)unitId;
                    dateFrom   = fromDate;
                    dateTo     = toDate;
                    isInsert   = true;
                }
            }
            else
            {
                // Subsequent call — update insights on the existing report
                var report = await _repository.GetReportByIdAsync(publishLog.ReportId);
                if (report == null)
                    return new SaveRunInsightsResultDto { Outcome = 0 };

                reportId   = report.Id;
                unitIdUint = (uint)report.UnitId;
                dateFrom   = report.FromDate;
                dateTo     = report.ToDate;
                isInsert   = false;
            }

            // Delete all existing rows for this report before re-inserting
            await _repository.DeleteTopThemesByUnitAndReportAsync(unitIdUint, (int)reportId);
            await _repository.DeleteTopRecommendationsByUnitAndReportAsync(unitIdUint, (int)reportId);
            await _repository.DeleteTopActionsByUnitAndReportAsync(unitIdUint, (int)reportId);

            await _repository.SaveTopThemesAsync(dto.TopThemes
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => new TopTheme { UnitId = unitIdUint, ReportId = reportId, Theme = t, CreatedAt = now })
                .ToList());

            await _repository.SaveTopRecommendationsAsync(dto.TopRecommendations
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => new TopRecommendation { UnitId = unitIdUint, ReportId = reportId, Recommendation = r, CreatedAt = now })
                .ToList());

            await _repository.SaveTopActionsAsync(dto.TopActions
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => new TopAction { UnitId = unitIdUint, ReportId = reportId, Action = a, CreatedAt = now })
                .ToList());

            // The publish-log row IS the release across this product: it is what
            // the workbench reads as InsightsPublished and what the review queue
            // reads as Released. Withholding it is the whole review gate, so a
            // scheduler save (publish = false) stops here and the row is written
            // later by ReportApprovalService.ApproveAndReleaseAsync.
            // Is this the report's FIRST release? Read before writing the row.
            // "Insert vs update" can't answer it: publishing a report the
            // scheduler left pending updates an existing row, yet it is still the
            // first time that report reaches its manager. This is what the caller
            // keys the manager notification off, so a re-save of an already
            // released report doesn't mail them again.
            var alreadyReleased = publish &&
                await _context.InsightPublishLogs.AnyAsync(l => l.ReportId == reportId, ct);

            if (publish)
            {
                await _repository.SaveInsightPublishLogAsync(new InsightPublishLog
                {
                    ReportId           = reportId,
                    RunId              = (int)dto.RunId,
                    UserId             = userId,
                    DateFrom           = dateFrom,
                    DateTo             = dateTo,
                    TopThemes          = System.Text.Json.JsonSerializer.Serialize(dto.TopThemes),
                    TopActions         = System.Text.Json.JsonSerializer.Serialize(dto.TopActions),
                    TopRecommendations = System.Text.Json.JsonSerializer.Serialize(dto.TopRecommendations),
                    CreatedAt          = now
                });
            }

            return new SaveRunInsightsResultDto
            {
                Outcome      = isInsert ? 1 : 2,
                ReportId     = reportId,
                Published    = publish,
                FirstPublish = publish && !alreadyReleased,
                UnitId       = unitIdUint,
                FromDate     = dateFrom,
                ToDate       = dateTo
            };
        }

        private static (long UnitId, DateTime FromDate, DateTime ToDate) ExtractRunMeta(WorkbenchRunStatus runStatus)
        {
            long unitId = 0;
            DateTime? fromDate = null;
            DateTime? toDate = null;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(runStatus.ResultJson!);
                var root = doc.RootElement;

                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name.Equals("UnitId", StringComparison.OrdinalIgnoreCase) &&
                        prop.Value.ValueKind != System.Text.Json.JsonValueKind.Null)
                        long.TryParse(prop.Value.GetRawText().Trim('"'), out unitId);

                    if (prop.Name.Equals("FromDate", StringComparison.OrdinalIgnoreCase) &&
                        prop.Value.ValueKind != System.Text.Json.JsonValueKind.Null &&
                        DateTime.TryParse(prop.Value.GetString(), null,
                            System.Globalization.DateTimeStyles.RoundtripKind, out var f))
                        fromDate = f.ToUniversalTime();

                    if (prop.Name.Equals("ToDate", StringComparison.OrdinalIgnoreCase) &&
                        prop.Value.ValueKind != System.Text.Json.JsonValueKind.Null &&
                        DateTime.TryParse(prop.Value.GetString(), null,
                            System.Globalization.DateTimeStyles.RoundtripKind, out var t))
                        toDate = t.ToUniversalTime();
                }
            }
            catch { }

            return (
                unitId,
                fromDate ?? runStatus.CreatedAt,
                toDate ?? runStatus.CompletedAt ?? DateTime.UtcNow
            );
        }

        public async Task<bool> CheckWorksheetFileExistsAsync(string fileName)
        {
            return await _repository.CheckFileExistsAsync(fileName);
        }
        public async Task<string> RunPromptfooEvaluationAsync()
        {
            try
            {
                // Note: Ensure promptfoo is installed globally (npm install -g promptfoo)
                // and that the current user has permissions to run it.
                var projectRoot = _env.ContentRootPath;
                var configPath = Path.Combine(projectRoot, "promptfooconfig.yaml");
                var outputPath = Path.Combine(projectRoot, "promptfoo_results.json");

                if (!System.IO.File.Exists(configPath))
                {
                    return "{\"error\": \"promptfooconfig.yaml not found at " + configPath.Replace("\\", "\\\\") + "\"}";
                }

                var promptfooAuth = _configuration["Promptfoo:AuthorizationKey"];
                var sharingEnabled = _configuration.GetValue<bool>("Promptfoo:SharingEnabled");
                var shareFlag = sharingEnabled ? "--share" : "";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c npx --yes promptfoo@latest eval --config \"{configPath}\" --output \"{outputPath}\" --no-cache {shareFlag}",
                    WorkingDirectory = projectRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                /*                if (!string.IsNullOrEmpty(promptfooAuth))
                {
                    startInfo.EnvironmentVariables["PROMPTFOO_AUTHORIZATION"] = promptfooAuth;
                }

                // Ensure it targets the correct cloud app and saves results
                startInfo.EnvironmentVariables["PROMPTFOO_REMOTE_API_BASE_URL"] = "https://www.promptfoo.app";
                startInfo.EnvironmentVariables["PROMPTFOO_REMOTE_APP_BASE_URL"] = "https://www.promptfoo.app";
                startInfo.EnvironmentVariables["PROMPTFOO_SAVE_RESULTS"] = "true";
                if (!string.IsNullOrEmpty(promptfooAuth))
                {
                    startInfo.EnvironmentVariables["PROMPTFOO_API_KEY"] = promptfooAuth;
                }*/

                // Authorization for Cloud
                if (!string.IsNullOrEmpty(promptfooAuth))
                {
                    startInfo.EnvironmentVariables["PROMPTFOO_API_KEY"] = promptfooAuth;
                }

                // Ensure it targets the correct cloud API
                // Ensure it targets the correct cloud app
                startInfo.EnvironmentVariables["PROMPTFOO_HOST"] = "https://www.promptfoo.app";

                using var process = Process.Start(startInfo);
                if (process == null) return "{\"error\": \"Failed to start promptfoo process.\"}";

                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (System.IO.File.Exists(outputPath))
                {
                    return await System.IO.File.ReadAllTextAsync(outputPath);
                }

                return $"{{\"error\": \"results.json not generated.\", \"stdout\": \"{stdout.Replace("\"", "\\\"").Replace("\r\n", " ")}\", \"stderr\": \"{stderr.Replace("\"", "\\\"").Replace("\r\n", " ")}\"}}";
            }
            catch (Exception ex)
            {
                return $"{{\"error\": \"Exception during Promptfoo eval: {ex.Message.Replace("\"", "\\\"")}\"}}";
            }
        }

        public async Task ImportFeedbackFromExcelAsync(string filePath, uint InComingbusinessUnitId)
        {
            try
            {
                OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

                if (!System.IO.File.Exists(filePath))
                {
                    throw new FileNotFoundException($"Excel file not found at path: {filePath}");
                }

                using var package = new ExcelPackage(new FileInfo(filePath));
                var worksheet = package.Workbook.Worksheets[0];
                int rowCount = worksheet.Dimension?.Rows ?? 0;

                if (rowCount < 2) // Need at least header + 1 data row
                {
                    throw new InvalidOperationException("Excel file is empty or has no data rows.");
                }

                // ===== STEP 1: Load ALL records from Excel (3000+) =====
                var allExcelRecords = new List<(string BusinessUnitName, string QuestionText, string ReasonText, int SentimentType, string FreeText)>();

                for (int row = 2; row <= rowCount; row++)
                {
                    var businessUnitName = worksheet.Cells[row, 2].Text?.Trim(); // Column B
                    var questionText = worksheet.Cells[row, 4].Text?.Trim();     // Column D
                    var reasonText = worksheet.Cells[row, 6].Text?.Trim();       // Column F
                    var sentimentTypeText = worksheet.Cells[row, 7].Text?.Trim(); // Column G
                    var freeText = worksheet.Cells[row, 8].Text?.Trim();         // Column H

                    // Skip empty rows
                    if (string.IsNullOrWhiteSpace(businessUnitName) ||
                        string.IsNullOrWhiteSpace(questionText) ||
                        string.IsNullOrWhiteSpace(reasonText))
                    {
                        continue;
                    }

                    // Parse sentiment type
                    if (!int.TryParse(sentimentTypeText, out var sentimentType))
                    {
                        continue; // Skip invalid sentiment types
                    }

                    allExcelRecords.Add((businessUnitName, questionText, reasonText, sentimentType, freeText ?? string.Empty));
                }

                if (!allExcelRecords.Any())
                {
                    throw new InvalidOperationException("No valid records found in Excel file.");
                }

                // ===== STEP 2: Shuffle all records randomly =====
                var shuffledRecords = allExcelRecords.OrderBy(x => Random.Shared.Next()).ToList();

                // ===== STEP 3: Lookup and cache IDs for unique combinations =====
                // Create caches to avoid repeated lookups
                var businessUnitCache = new Dictionary<string, uint>();
                var questionCache = new Dictionary<string, uint>();
                var reasonCache = new Dictionary<string, uint>();

                // Get latest submission date for this business unit (specifically for the incoming BU as a reference point)
                var latestSubmissionDate = await _repository.GetLatestSubmissionDateTimeAsync((uint)InComingbusinessUnitId);

                // Calculate today's date (UTC). SpecifyKind preserves the UTC tag
                // that .Date would otherwise strip, so Npgsql accepts the value
                // for 'timestamp with time zone' columns downstream.
                var today = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

                // Check if there is already an entry in DailyFeedback where SubmissionDateTime is today's date (UTC)
                if (latestSubmissionDate.HasValue && latestSubmissionDate.Value.Date == today)
                {
                    // Record already exists for today's date - terminate process
                    return;
                }

                if (latestSubmissionDate == null)
                {
                    latestSubmissionDate = _settings.SubmissionDateTime;
                }

                // ===== STEP 4: Calculate missing days =====
                var startDate = latestSubmissionDate.HasValue
                    ? DateTime.SpecifyKind(latestSubmissionDate.Value.Date.AddDays(1), DateTimeKind.Utc)
                    : today;

                var missingDays = new List<DateTime>();
                for (var date = startDate; date <= today; date = date.AddDays(1))
                {
                    missingDays.Add(date);
                }

                if (!missingDays.Any())
                {
                    // No missing days - nothing to insert
                    return;
                }

                // ===== STEP 5: Pick and insert ONE valid record per missing day =====
                int currentRecordIndex = 0;

                foreach (var day in missingDays.OrderBy(d => d))
                {
                    bool recordFoundForToday = false;

                    // Try records from the shuffled pool until we find a valid one for this day
                    while (currentRecordIndex < shuffledRecords.Count && !recordFoundForToday)
                    {
                        var record = shuffledRecords[currentRecordIndex];
                        currentRecordIndex++;

                        try
                        {
                            //// 1. Resolve BusinessUnitId (randomly picked from Excel row as per requirement)
                            //if (!businessUnitCache.ContainsKey(record.BusinessUnitName))
                            //{
                            //    var buId = await _repository.GetBusinessUnitIdByNameAsync(record.BusinessUnitName);
                            //    if (!buId.HasValue) continue; // Skip to next record if BU name doesn't exist
                            //    businessUnitCache[record.BusinessUnitName] = (uint)buId.Value;
                            //}

                            // 2. Resolve QuestionId
                            if (!questionCache.ContainsKey(record.QuestionText))
                            {
                                var qId = await _repository.GetQuestionIdByTextAsync(record.QuestionText);
                                if (!qId.HasValue) continue; // Skip if Question doesn't exist
                                questionCache[record.QuestionText] = (uint)qId.Value;
                            }

                            // 3. Resolve ReasonId
                            if (!reasonCache.ContainsKey(record.ReasonText))
                            {
                                var rId = await _repository.GetReasonIdByTextAsync(record.ReasonText);
                                if (!rId.HasValue) continue; // Skip if Reason doesn't exist
                                reasonCache[record.ReasonText] = (uint)rId.Value;
                            }

                            // If we reached here, we have all valid IDs
                            var feedback = new DailyFeedback
                            {
                                SubmissionDateTime = day.AddHours(12), // Noon UTC
                                BusinessUnitId = InComingbusinessUnitId,
                                SentimentType = (DailyFeedbackSentimentType)record.SentimentType,
                                DailyQuestionId = questionCache[record.QuestionText],
                                DailyReasonId = reasonCache[record.ReasonText],
                                FreeText = record.FreeText
                            };

                            // Insert exactly one record for this day
                            await _repository.BulkInsertFeedbackAsync(new List<DailyFeedback> { feedback });
                            recordFoundForToday = true;
                        }
                        catch
                        {
                            // On error with this specific record, try the next one in the shuffled pool
                            continue;
                        }
                    }

                    // If we ran out of records, we stop the whole process
                    if (currentRecordIndex >= shuffledRecords.Count && !recordFoundForToday)
                        break;
                }
            }
            catch (Exception ex)
            {
                throw;
            }
        }


    }
}
    
