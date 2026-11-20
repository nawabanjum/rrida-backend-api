using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    public class AzureAIGatewayService : IAIGatewayService
    {
        private readonly HttpClient _httpClient;
        private readonly IDocumentEmbeddingRepository _modelRepo;
        private readonly IAIRequestLogRepository _logRepo;
        private readonly IResponseValidatorService _validator;
        private readonly ILogger<AzureAIGatewayService> _logger;

        public AzureAIGatewayService(
            IHttpClientFactory httpFactory,
            IDocumentEmbeddingRepository modelRepo,
            IAIRequestLogRepository logRepo,
            IResponseValidatorService validator,
            ILogger<AzureAIGatewayService> logger)
        {
            _httpClient = httpFactory.CreateClient("aigateway");
            _modelRepo = modelRepo;
            _logRepo = logRepo;
            _validator = validator;
            _logger = logger;
        }

        public async Task<AIGatewayResponseDto> ChatCompletionAsync(
            string systemPrompt, string userContent,
            AIGatewayRequestDto? options, CancellationToken ct)
        {
            return await ExecuteWithFallbackAsync(systemPrompt, userContent, options, false, ct);
        }

        public async Task<AIGatewayResponseDto> ChatCompletionWithJsonAsync(
            string systemPrompt, string userContent,
            AIGatewayRequestDto? options, CancellationToken ct)
        {
            return await ExecuteWithFallbackAsync(systemPrompt, userContent, options, true, ct);
        }

        private async Task<List<ModelEndpoint>> GetModelChainAsync(string? requestedModel)
        {
            var allModels = await _modelRepo.GetAllModelsAsync();
            var chain = new List<ModelEndpoint>();

            // If a specific model is requested, put it first
            if (!string.IsNullOrEmpty(requestedModel))
            {
                var requested = allModels.FirstOrDefault(m =>
                    m.ModelName.Equals(requestedModel, StringComparison.OrdinalIgnoreCase));
                if (requested != null)
                    chain.Add(ToModelEndpoint(requested));
            }

            // Add default model if not already in chain
            var defaultModel = allModels.FirstOrDefault(m => m.IsDefault);
            if (defaultModel != null &&
                !chain.Any(c => c.Name.Equals(defaultModel.ModelName, StringComparison.OrdinalIgnoreCase)))
            {
                chain.Add(ToModelEndpoint(defaultModel));
            }

            // Add remaining active models as fallbacks
            foreach (var model in allModels)
            {
                if (!chain.Any(c => c.Name.Equals(model.ModelName, StringComparison.OrdinalIgnoreCase)))
                    chain.Add(ToModelEndpoint(model));
            }

            return chain;
        }

        private static ModelEndpoint ToModelEndpoint(ModelConfiguration m) =>
            new(m.ModelName, m.Endpoint, m.ApiKey, m.ApiVersion, m.Temperature, m.MaxTokens);

        private async Task<AIGatewayResponseDto> ExecuteWithFallbackAsync(
            string systemPrompt, string userContent,
            AIGatewayRequestDto? options, bool enforceJson, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var models = await GetModelChainAsync(options?.Model);

            if (models.Count == 0)
            {
                return new AIGatewayResponseDto
                {
                    Success = false,
                    ErrorMessage = "No model configurations found in database. Please add models via the upsert-models endpoint."
                };
            }

            AIGatewayResponseDto response = null!;
            var fallbackUsed = false;

            foreach (var model in models)
            {
                try
                {
                    response = await CallModelAsync(model, systemPrompt, userContent, options, ct);

                    if (enforceJson)
                        response.Content = StripMarkdownJson(response.Content);

                    if (response.Success) break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Model {Model} failed, trying fallback", model.Name);
                    response = new AIGatewayResponseDto
                        { Success = false, ErrorMessage = ex.Message };
                }
                fallbackUsed = true;
            }

            response.FallbackUsed = fallbackUsed && response.Success;
            response.LatencyMs = sw.ElapsedMilliseconds;

            var primaryModelName = models.FirstOrDefault()?.Name ?? "";
            await LogRequestAsync(response, options, enforceJson, primaryModelName, ct);

            return response;
        }

        private static string StripMarkdownJson(string content)
        {
            var trimmed = content.Trim();
            if (trimmed.StartsWith("```"))
            {
                var firstNewline = trimmed.IndexOf('\n');
                var lastFence = trimmed.LastIndexOf("```");
                if (firstNewline > 0 && lastFence > firstNewline)
                    return trimmed[(firstNewline + 1)..lastFence].Trim();
            }
            return content;
        }

        private async Task<AIGatewayResponseDto> CallModelAsync(
            ModelEndpoint model, string systemPrompt, string userContent,
            AIGatewayRequestDto? options, CancellationToken ct)
        {
            var temperature = options?.Temperature ?? model.Temperature;
            var maxTokens = options?.MaxTokens ?? model.MaxTokens;

            var endpoint = (model.Endpoint ?? "").Trim();
            var isAIFoundryServerless = endpoint.Contains("/models/chat/completions", StringComparison.OrdinalIgnoreCase)
                || (endpoint.Contains(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase)
                    && endpoint.Contains("/models/", StringComparison.OrdinalIgnoreCase));

            var messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent }
            };

            var json = isAIFoundryServerless
                ? JsonSerializer.Serialize(new
                {
                    model = model.Name?.Trim(),
                    messages,
                    temperature,
                    max_tokens = maxTokens,
                    top_p = options?.TopP ?? 1.0
                })
                : JsonSerializer.Serialize(new
                {
                    messages,
                    temperature,
                    max_tokens = maxTokens,
                    top_p = options?.TopP ?? 1.0
                });

            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            // Build endpoint URL with api-version if provided
            if (!string.IsNullOrEmpty(model.ApiVersion) && !endpoint.Contains("api-version"))
                endpoint += (endpoint.Contains('?') ? '&' : '?') + $"api-version={model.ApiVersion}";

            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = httpContent
            };

            // Add API key from DB model configuration
            if (!string.IsNullOrEmpty(model.ApiKey))
                request.Headers.TryAddWithoutValidation("api-key", model.ApiKey);

            var httpResponse = await _httpClient.SendAsync(request, ct);
            var responseBody = await httpResponse.Content.ReadAsStringAsync(ct);

            if (!httpResponse.IsSuccessStatusCode)
            {
                return new AIGatewayResponseDto
                {
                    Success = false,
                    ModelUsed = model.Name,
                    ErrorMessage = $"HTTP {(int)httpResponse.StatusCode}: {responseBody}"
                };
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            var content = "";
            var promptTokens = 0;
            var completionTokens = 0;

            if (root.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentElement))
                {
                    content = contentElement.GetString() ?? "";
                }
            }

            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var pt))
                    promptTokens = pt.GetInt32();
                if (usage.TryGetProperty("completion_tokens", out var cpt))
                    completionTokens = cpt.GetInt32();
            }

            return new AIGatewayResponseDto
            {
                Success = true,
                Content = content,
                ModelUsed = model.Name,
                Usage = new TokenUsageDto
                {
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens
                }
            };
        }

        private async Task LogRequestAsync(
            AIGatewayResponseDto response, AIGatewayRequestDto? options,
            bool enforceJson, string primaryModelName, CancellationToken ct)
        {
            try
            {
                ValidationResultDto? validation = null;
                if (enforceJson && response.Success)
                    validation = _validator.Validate(response.Content, options?.SystemPrompt);

                var log = new AIRequestLog
                {
                    PromptName = options?.SystemPrompt ?? "",
                    PromptVersion = "",
                    ModelRequested = options?.Model ?? primaryModelName,
                    ModelUsed = response.ModelUsed,
                    FallbackUsed = response.FallbackUsed,
                    LatencyMs = response.LatencyMs,
                    PromptTokens = response.Usage.PromptTokens,
                    CompletionTokens = response.Usage.CompletionTokens,
                    EstimatedCost = 0,
                    IsJsonValid = validation?.IsValid ?? false,
                    AssertionsPassed = validation?.Passed ?? 0,
                    AssertionsTotal = validation?.Total ?? 0,
                    Success = response.Success,
                    ErrorMessage = response.ErrorMessage,
                    CreatedAt = DateTime.UtcNow
                };

                await _logRepo.AddAsync(log);
                await _logRepo.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log AI request");
            }
        }
    }
}
