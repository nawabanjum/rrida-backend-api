using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    public class LiteLLMService : ILiteLLMService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<LiteLLMService> _logger;
        private readonly LiteLLMOptions _options;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public LiteLLMService(
            HttpClient httpClient,
            IOptions<LiteLLMOptions> options,
            ILogger<LiteLLMService> logger,
            IHttpContextAccessor httpContextAccessor)
        {
            _httpClient = httpClient;
            _logger = logger;
            _options = options.Value;
            _httpContextAccessor = httpContextAccessor;

            // Ensure BaseAddress is set (safety check)
            if (_httpClient.BaseAddress == null)
            {
                _httpClient.BaseAddress = new Uri(_options.BaseUrl);
                _logger.LogDebug("Set BaseAddress in constructor: {BaseAddress}", _options.BaseUrl);
            }

            // Ensure timeout is set
            if (_httpClient.Timeout == TimeSpan.Zero || _httpClient.Timeout == TimeSpan.FromSeconds(100))
            {
                _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
                _logger.LogDebug("Set Timeout in constructor: {Timeout}s", _options.TimeoutSeconds);
            }

            _logger.LogInformation("LiteLLMService initialized with BaseAddress: {BaseAddress}",
                _httpClient.BaseAddress);
        }

        // Method 1: Simple user prompt
        public async Task<string> GetChatCompletionAsync(string userPrompt, CancellationToken ct = default)
        {
            return await GetChatCompletionAsync("You are a helpful assistant.", userPrompt, ct);
        }

        // Method 2: With system prompt (UPDATED WITH METADATA)
        public async Task<string> GetChatCompletionAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
        {
            // Get user ID from HttpContext if available
            var userId = _httpContextAccessor.HttpContext?.User?.Identity?.Name
                         ?? "anonymous";

            // Get request path
            var requestPath = _httpContextAccessor.HttpContext?.Request?.Path.Value
                              ?? "unknown-endpoint";

            var requestData = new
            {
                model = _options.DefaultModel,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = _options.Temperature,
                max_tokens = _options.MaxTokens,
                top_p = _options.TopP,

                // ========== ENHANCED METADATA FOR DASHBOARD TRACKING ==========
                metadata = new
                {
                    project = "cloverera-backoffice",
                    user_id = userId,
                    team_id = "fe524e75-fb7a-46f2-8cc8-76031188b086",
                    source = "cloverera-backoffice-api",
                    api_version = "1.0",
                    environment = "development",
                    endpoint = requestPath,
                    request_type = "chat_completion",
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    session_id = Guid.NewGuid().ToString(),
                    deployment = "azure-gpt-4o",
                    application = "CloverBackoffice",
                    feature = GetCurrentFeatureName(requestPath)
                }
            };

            _logger.LogInformation("Sending request to LiteLLM - User: {UserId}, Endpoint: {Endpoint}, Model: {Model}",
                userId, requestPath, _options.DefaultModel);

            return await GetChatCompletionAsync(requestData, ct);
        }

        // Helper method to extract feature name from path
        private string GetCurrentFeatureName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "unknown";

            if (path.Contains("/api/chat", StringComparison.OrdinalIgnoreCase)) return "chat";
            if (path.Contains("/api/summarize", StringComparison.OrdinalIgnoreCase)) return "summarize";
            if (path.Contains("/api/translate", StringComparison.OrdinalIgnoreCase)) return "translate";
            if (path.Contains("/api/analyze", StringComparison.OrdinalIgnoreCase)) return "analyze";

            return "general";
        }

        // Method 3: Custom request data
        public async Task<string> GetChatCompletionAsync(object requestData, CancellationToken ct = default)
        {
            string requestId = Guid.NewGuid().ToString();

            try
            {
                var json = JsonSerializer.Serialize(requestData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });

                _logger.LogDebug("LiteLLM Request JSON: {Json}", json);

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Add request ID header for tracking
                content.Headers.Add("X-Request-ID", requestId);

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                // FIX: Use absolute URI to avoid BaseAddress issues
                var requestUri = GetRequestUri("/v1/chat/completions");
                _logger.LogDebug("Making request to: {RequestUri}", requestUri);

                var response = await _httpClient.PostAsync(requestUri, content, ct);
                stopwatch.Stop();

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogError("LiteLLM API error - RequestID: {RequestId}, Status: {StatusCode}, Error: {Error}",
                        requestId, response.StatusCode, errorContent);

                    throw new HttpRequestException($"LiteLLM API error: {response.StatusCode} - {errorContent}");
                }

                var responseJson = await response.Content.ReadAsStringAsync(ct);
                _logger.LogDebug("LiteLLM Response JSON: {Json}", responseJson);

                // Parse the response
                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;

                // Log token usage
                if (root.TryGetProperty("usage", out var usage))
                {
                    var promptTokens = usage.GetProperty("prompt_tokens").GetInt32();
                    var completionTokens = usage.GetProperty("completion_tokens").GetInt32();
                    var totalTokens = usage.GetProperty("total_tokens").GetInt32();

                    _logger.LogInformation("LiteLLM Token Usage - RequestID: {RequestId}, Prompt: {Prompt}, Completion: {Completion}, Total: {Total}, Duration: {Duration}ms",
                        requestId, promptTokens, completionTokens, totalTokens, stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    _logger.LogInformation("LiteLLM Request Completed - RequestID: {RequestId}, Duration: {Duration}ms",
                        requestId, stopwatch.ElapsedMilliseconds);
                }

                // Extract the content
                var contentText = root
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? string.Empty;

                _logger.LogDebug("Received response from LiteLLM: {Length} characters", contentText.Length);

                return contentText;
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                _logger.LogError("LiteLLM request timeout after {Timeout}s - RequestID: {RequestId}",
                    _options.TimeoutSeconds, requestId);
                throw new TimeoutException($"LiteLLM request timed out after {_options.TimeoutSeconds} seconds");
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse LiteLLM response - RequestID: {RequestId}", requestId);
                throw new InvalidOperationException("Invalid response from LiteLLM", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling LiteLLM service - RequestID: {RequestId}", requestId);
                throw;
            }
        }

        // Method 4: Test connection
        public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
        {
            try
            {
                // Test by getting available models using absolute URI
                var requestUri = GetRequestUri("/v1/models");
                _logger.LogDebug("Testing connection to: {RequestUri}", requestUri);

                var response = await _httpClient.GetAsync(requestUri, ct);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogInformation("LiteLLM connection test successful");
                    return true;
                }

                _logger.LogWarning("LiteLLM connection test failed: {StatusCode}", response.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LiteLLM connection test failed");
                return false;
            }
        }

        // Method 5: Test with metadata for dashboard
        public async Task<string> TestDashboardConnectionAsync(CancellationToken ct = default)
        {
            var testRequestData = new
            {
                model = _options.DefaultModel,
                messages = new[]
                {
                    new { role = "system", content = "You are a test assistant for dashboard tracking." },
                    new { role = "user", content = "This is a test message to verify LiteLLM dashboard integration. Please respond with 'Dashboard tracking is working successfully!'" }
                },
                temperature = 0.3,
                max_tokens = 100,
                top_p = 1.0,
                metadata = new
                {
                    project = "cloverera",
                    user_id = "dashboard-test",
                    source = "test-endpoint",
                    api_version = "1.0",
                    environment = "development",
                    test_type = "dashboard_integration",
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }
            };

            var json = JsonSerializer.Serialize(testRequestData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Use absolute URI
            var requestUri = GetRequestUri("/v1/chat/completions");
            _logger.LogDebug("Dashboard test request to: {RequestUri}", requestUri);

            var response = await _httpClient.PostAsync(requestUri, content, ct);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);

            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        }

        // Method 6: Debug request
        public async Task<string> DebugRequestAsync()
        {
            try
            {
                // Check what headers are actually being sent
                var headers = _httpClient.DefaultRequestHeaders.ToString();
                var baseAddress = _httpClient.BaseAddress?.ToString() ?? "Not set";
                var timeout = _httpClient.Timeout.TotalSeconds;

                _logger.LogInformation("HttpClient Configuration - BaseAddress: {BaseAddress}, Timeout: {Timeout}s, Headers: {Headers}",
                    baseAddress, timeout, headers);

                // Test the connection using absolute URI
                var requestUri = GetRequestUri("/v1/models");
                _logger.LogDebug("Debug request to: {RequestUri}", requestUri);

                var response = await _httpClient.GetAsync(requestUri);
                var content = await response.Content.ReadAsStringAsync();

                return $"Status: {response.StatusCode}\n" +
                       $"BaseAddress: {baseAddress}\n" +
                       $"Timeout: {timeout}s\n" +
                       $"Headers: {headers}\n" +
                       $"Request URI: {requestUri}\n" +
                       $"Response: {content}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}\nType: {ex.GetType().Name}";
            }
        }

        // Helper method to build absolute URI
        private string GetRequestUri(string relativePath)
        {
            // If BaseAddress is set, combine with relative path
            if (_httpClient.BaseAddress != null)
            {
                return new Uri(_httpClient.BaseAddress, relativePath.TrimStart('/')).ToString();
            }

            // Fallback: Use _options.BaseUrl
            return $"{_options.BaseUrl.TrimEnd('/')}/{relativePath.TrimStart('/')}";
        }

        // Method 7: Get available models
        public async Task<List<string>> GetAvailableModelsAsync(CancellationToken ct = default)
        {
            try
            {
                var requestUri = GetRequestUri("/v1/models");
                var response = await _httpClient.GetAsync(requestUri, ct);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;

                var models = new List<string>();

                if (root.TryGetProperty("data", out var dataArray))
                {
                    foreach (var model in dataArray.EnumerateArray())
                    {
                        if (model.TryGetProperty("id", out var id))
                        {
                            models.Add(id.GetString() ?? string.Empty);
                        }
                    }
                }

                _logger.LogInformation("Found {Count} available models in LiteLLM", models.Count);
                return models;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get available models from LiteLLM");
                return new List<string>();
            }
        }
    }
}