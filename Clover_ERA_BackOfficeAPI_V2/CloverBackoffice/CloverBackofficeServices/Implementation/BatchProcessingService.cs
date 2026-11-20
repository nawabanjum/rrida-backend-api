using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    public class BatchProcessingService : IBatchProcessingService
    {
        private readonly IDocumentEmbeddingRepository _docEmbeddingRepository;
        private readonly IHttpClientFactory _httpFactory;
        private readonly IInsightsRepository _insightsRepository;
        private readonly ILogger<BatchProcessingService> _logger;
        private readonly IFeedbackRepository _repository;
        private readonly ApplicationDbContext _context;

        private ModelConfiguration? _cachedBatchModel;

        public BatchProcessingService(
            IDocumentEmbeddingRepository docEmbeddingRepository,
            IHttpClientFactory httpFactory,
            IInsightsRepository insightsRepository,
            IFeedbackRepository repository,
            ApplicationDbContext context,
            ILogger<BatchProcessingService> logger)
        {
            _docEmbeddingRepository = docEmbeddingRepository;
            _httpFactory = httpFactory;
            _insightsRepository = insightsRepository;
            _logger = logger;
            _repository = repository;
            _context = context;
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

        public async Task<BatchJob> CreateBatchJobAsync(uint businessUnitId, int periodDays)
        {
            var feedbackList = await _repository.GetDailyFeedbackReportAsync2(businessUnitId, periodDays);

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

            // 3. Build GPT prompt
            var prompt = BuildPrompt(feedbackList);

            // 4. Upload prompt as JSONL file and submit batch via Azure OpenAI
            var batchModel = await GetBatchModelAsync();
            var fileId = await UploadPromptAsJsonlAsync(prompt, businessUnitId, batchModel);
            var openAiJobId = await SubmitBatchJobWithFileIdAsync(fileId, batchModel);

            var job = new BatchJob
            {
                Id = Guid.NewGuid(),
                OpenAIJobId = openAiJobId,
                BusinessUnitId = businessUnitId,
                Status = "submitted",
                RequestType = "insights_generation",
                SubmittedAt = DateTime.UtcNow
            };
            try
            {
                await _insightsRepository.SaveBatchJobAsync(job);
                return job;
            }
            catch (Exception ex)
            {
                return job;
            }
        }

        public async Task ProcessCompletedJobsAsync()
        {
            try
            {
                // Get all submitted jobs
                var submittedJobs = await GetSubmittedJobsAsync();

                foreach (var job in submittedJobs)
                {
                    try
                    {
                        // Check status with Azure OpenAI
                        var status = await GetBatchStatusFromAzureAsync(job.OpenAIJobId);

                        if (status.Status == "completed")
                        {
                            await ProcessCompletedJobAsync(job, status);
                        }
                        else if (status.Status == "failed")
                        {
                            await UpdateJobStatusAsync(job.Id, "failed", errorMessage: "Azure OpenAI batch processing failed");
                        }
                        else if (status.Status == "expired")
                        {
                            await UpdateJobStatusAsync(job.Id, "expired", errorMessage: "Batch job expired");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing job {JobId}", job.Id);
                        await UpdateJobStatusAsync(job.Id, "error", errorMessage: ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProcessCompletedJobsAsync");
            }
        }

        public async Task<BatchStatus> GetBatchStatusAsync(Guid jobId)
        {
            var job = await _insightsRepository.GetBatchJobAsync(jobId);
            if (job == null) return new BatchStatus { Status = "not_found" };
            var status = await GetBatchStatusFromAzureAsync(job.OpenAIJobId);
            return status;
        }

        public async Task<EngagementInsights> GetResultsAsync(Guid jobId)
        {
            // TODO: After processing completes, return stored insights
            var job = await _insightsRepository.GetBatchJobAsync(jobId);
            if (job == null) return null;
            return await _insightsRepository.GetLatestInsightsAsync(job.BusinessUnitId);
        }

        private async Task<List<BatchJob>> GetSubmittedJobsAsync()
        {
            List<BatchJob> list = new List<BatchJob>();
            list = await _insightsRepository.GetSubmittedJobsAsync();
            return list;
        }

        private async Task ProcessCompletedJobAsync(BatchJob job, BatchStatus status)
        {
            try
            {
                // Download and parse results
                var insights = await DownloadAndParseResultsAsync(job, status);

                // Save insights
                await _insightsRepository.SaveInsightsAsync(insights);

                // Update job status
                await UpdateJobStatusAsync(job.Id, "completed",
                    completedAt: status.CompletedAt?.DateTime,
                    outputFileUrl: status.OutputFileId);

                _logger.LogInformation("Successfully processed completed job {JobId}", job.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing completed job {JobId}", job.Id);
                await UpdateJobStatusAsync(job.Id, "error", errorMessage: ex.Message);
            }
        }

        private async Task<EngagementInsights> DownloadAndParseResultsAsync(BatchJob job, BatchStatus status)
        {
            // TODO: Implement actual result downloading from Azure OpenAI
            // For now, create placeholder insights
            return new EngagementInsights
            {
                BusinessUnitId = job.BusinessUnitId,
                GeneratedAt = DateTime.UtcNow,
                PeriodStart = DateTime.UtcNow.AddDays(-30),
                PeriodEnd = DateTime.UtcNow,
                OverallScore = 75.0m,
                CategoryScoresJson = JsonSerializer.Serialize(new { communication = 80, learning = 70 }),
                TopThemesJson = JsonSerializer.Serialize(new[] { "Team collaboration", "Skill development" }),
                RecommendationsJson = JsonSerializer.Serialize(new[] { "Implement regular feedback sessions" }),
                StatisticalSummaryJson = JsonSerializer.Serialize(new { responseRate = 85.5, totalResponses = 120 }),
                CreatedBy = "System"
            };
        }

        private async Task UpdateJobStatusAsync(Guid jobId, string status, DateTime? completedAt = null, string errorMessage = null, string outputFileUrl = null)
        {
            await _insightsRepository.UpdateBatchJobStatusAsync(jobId, status, completedAt, errorMessage, outputFileUrl);
        }

        private async Task<string> UploadPromptAsJsonlAsync(string prompt, uint unitId, ModelConfiguration batchModel)
        {
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

            // 4. Build multipart form
            using var content = new MultipartFormDataContent
            {
                { new StringContent("batch"), "purpose" },
                { fileContent, "file", $"batch-insights-{unitId}-{DateTime.UtcNow:yyyyMMddHHmmss}.jsonl" }
            };

            // 5. Upload request
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/openai/files?api-version={batchModel.ApiVersion}")
            {
                Content = content
            };

            using var client = CreateClientForModel(batchModel);
            var res = await client.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"File upload failed: {json}");

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("id").GetString()!;
        }

        private async Task<string> SubmitBatchJobWithFileIdAsync(string fileId, ModelConfiguration batchModel)
        {
            var requestBody = new
            {
                input_file_id = fileId,
                endpoint = $"/openai/deployments/{batchModel.ModelName}/chat/completions",
                completion_window = "24h",
                metadata = new
                {
                    description = "Batch processing insights generation job"
                }
            };

            var body = JsonSerializer.Serialize(requestBody);

            using var req = new HttpRequestMessage(HttpMethod.Post, $"/openai/batches?api-version={batchModel.ApiVersion}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            using var client = CreateClientForModel(batchModel);
            var res = await client.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"Batch job submission failed: {json}");

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("id").GetString()!;
        }

        private async Task<BatchStatus> GetBatchStatusFromAzureAsync(string batchId)
        {
            var batchModel = await GetBatchModelAsync();
            using var client = CreateClientForModel(batchModel);

            var res = await client.GetAsync($"/openai/batches/{batchId}?api-version={batchModel.ApiVersion}");
            if (!res.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Failed to get batch status: {res.StatusCode}");
            }

            var responseString = await res.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(responseString);

            return new BatchStatus
            {
                Status = json.RootElement.GetProperty("status").GetString(),
                CompletedAt = json.RootElement.TryGetProperty("completed_at", out var completedAt) && completedAt.ValueKind != JsonValueKind.Null
                    ? DateTimeOffset.Parse(completedAt.GetString())
                    : null,
                OutputFileId = json.RootElement.TryGetProperty("output_file_id", out var outputFileId)
                    ? outputFileId.GetString()
                    : null
            };
        }

        private string BuildPrompt(List<DailyFeedbackReportRowDto> feedbackRows)
        {
            // Fetch static part from DB
            var staticPrompt = _context.LLMPrompts
                .Where(p => p.Id == 1)
                .Select(p => p.InitialPrompt)
                .FirstOrDefault();

            var sb = new StringBuilder();
            sb.AppendLine(staticPrompt);

            // Add dynamic feedback rows
            foreach (var f in feedbackRows)
            {

                var ans = string.IsNullOrEmpty(f.FreeText) ? f.SpecificationCloverCategoryName : f.FreeText;

                sb.AppendLine(

                        $"CloverCategory: {f.SpecificationCloverCategoryName}, " +
                        $"Question: {f.QuestionText}, " +
                        $"Answer: {ans}, " +
                        $"Score: {f.Score}, " +
                        $"Reason: {f.ReasonText}"
                    );
            }

            // Parse ratio from static prompt; defaults: 4 themes, 3 recommendations, 3 action steps
            var (themes, recommendations, actionSteps) =
                ResponseValidatorService.ParseRatioFromSystemPrompt(staticPrompt);

            sb.AppendLine();
            sb.AppendLine("Instructions:");
            sb.AppendLine($"1. Assess the reasons provided (both prepopulated and free text) and provide {themes} key themes.");
            sb.AppendLine("2. Provide a report structured with the following top-level sections:");
            sb.AppendLine($"   - TopThemes: array of EXACTLY {themes} key recurring themes.");
            sb.AppendLine($"   - TopRecommendations: array of EXACTLY {recommendations} actionable recommendations.");
            sb.AppendLine($"   - TopActions: array of EXACTLY {actionSteps} key observed or suggested actions.");
            sb.AppendLine();
            sb.AppendLine($"IMPORTANT: You MUST produce EXACTLY {themes} theme(s), EXACTLY {recommendations} recommendation(s), and EXACTLY {actionSteps} action(s). No more, no less.");
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

    }
}
