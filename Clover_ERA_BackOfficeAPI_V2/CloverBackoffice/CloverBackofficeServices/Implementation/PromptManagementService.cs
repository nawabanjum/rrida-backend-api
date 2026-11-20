using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    public class PromptManagementService : IPromptManagementService
    {
        private readonly IPromptTemplateRepository _repo;
        private readonly IDocumentEmbeddingRepository _embeddingRepo;
        private readonly IAIRequestLogRepository _logRepo;
        private readonly IHttpClientFactory _httpFactory;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<PromptManagementService> _logger;
        private readonly ILogCollector _logCollector;

        // Known model pricing per 1K tokens (USD). Used as fallback when DB values are 0.
        // Source: https://openai.com/pricing, https://docs.anthropic.com/en/docs/about-claude/models
        // Update this dictionary when new models are added or prices change.
        private static readonly Dictionary<string, (decimal InputPer1k, decimal OutputPer1k)> KnownModelPricing =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // Anthropic Claude
                ["claude-opus-4-6"]          = (0.015m,  0.075m),
                ["claude-opus-4-0"]          = (0.015m,  0.075m),
                ["claude-sonnet-4-6"]        = (0.003m,  0.015m),
                ["claude-sonnet-4-5"]        = (0.003m,  0.015m),
                ["claude-sonnet-4-0"]        = (0.003m,  0.015m),
                ["claude-haiku-4-5"]         = (0.0008m, 0.004m),
                ["claude-3-5-sonnet"]        = (0.003m,  0.015m),
                ["claude-3-5-haiku"]         = (0.0008m, 0.004m),
                ["claude-3-opus"]            = (0.015m,  0.075m),
                ["claude-3-sonnet"]          = (0.003m,  0.015m),
                ["claude-3-haiku"]           = (0.00025m,0.00125m),
                // OpenAI GPT
                ["gpt-4o"]                   = (0.0025m, 0.01m),
                ["gpt-4o-mini"]              = (0.00015m,0.0006m),
                ["gpt-4-turbo"]              = (0.01m,   0.03m),
                ["gpt-4"]                    = (0.03m,   0.06m),
                ["gpt-4.1"]                  = (0.002m,  0.008m),
                ["gpt-4.1-mini"]             = (0.0004m, 0.0016m),
                ["gpt-4.1-nano"]             = (0.0001m, 0.0004m),
                ["gpt-3.5-turbo"]            = (0.0005m, 0.0015m),
                ["o3"]                       = (0.01m,   0.04m),
                ["o3-mini"]                  = (0.00115m,0.0044m),
                ["o4-mini"]                  = (0.00115m,0.0044m),
                // DeepSeek
                ["deepseek-chat"]            = (0.00027m,0.0011m),
                ["deepseek-reasoner"]        = (0.00055m,0.00219m),
                // Google Gemini
                ["gemini-2.5-pro"]           = (0.00125m,0.01m),
                ["gemini-2.5-flash"]         = (0.00015m,0.0006m),
                ["gemini-2.0-flash"]         = (0.0001m, 0.0004m),
                // Meta Llama
                ["llama-3.3-70b"]            = (0.00059m,0.00079m),
                ["llama-3.1-405b"]           = (0.00106m,0.00106m),
                ["llama-3.1-70b"]            = (0.00059m,0.00079m),
                ["llama-3.1-8b"]             = (0.00006m,0.00006m),
                // Mistral
                ["mistral-large"]            = (0.002m,  0.006m),
                ["mistral-small"]            = (0.0001m, 0.0003m),
            };

        public PromptManagementService(
            IPromptTemplateRepository repo,
            IDocumentEmbeddingRepository embeddingRepo,
            IAIRequestLogRepository logRepo,
            IHttpClientFactory httpFactory,
            IMemoryCache cache,
            IConfiguration configuration,
            IWebHostEnvironment env,
            ILogger<PromptManagementService> logger,
            ILogCollector logCollector)
        {
            _repo = repo;
            _embeddingRepo = embeddingRepo;
            _logRepo = logRepo;
            _httpFactory = httpFactory;
            _cache = cache;
            _configuration = configuration;
            _env = env;
            _logger = logger;
            _logCollector = logCollector;
        }

        // ── Standardised Output Format ──
        // This suffix is appended to every user prompt before sending to the LLM.
        // It ensures that regardless of what any user writes in their prompt, the
        // final LLM response always conforms to the JSON structure that the backend
        // (BuildTopInsight) and frontend expect.
        // Ratio (themes / recommendations / action steps) is parsed from the
        // SystemPrompt when present; otherwise defaults of 4 / 3 / 3 apply.

        private static string BuildStandardOutputFormatSuffix(int themes, int recommendations, int actionSteps)
        {
            var themesArray = string.Join(",\n    ",
                Enumerable.Range(0, themes).Select(_ => @"{ ""sentiment"": ""positive|negative|warning"", ""text"": ""string"" }"));

            var actionStepsArray = string.Join(",\n        ",
                Enumerable.Range(0, actionSteps).Select(_ => @"{ ""title"": ""string"", ""description"": ""string"" }"));

            var recommendationsArray = string.Join(",\n    ",
                Enumerable.Range(0, recommendations).Select(_ =>
$@"{{
      ""area_title"": ""string"",
      ""urgency"": ""critical|high|standard"",
      ""action_steps"": [
        {actionStepsArray}
      ]
    }}"));

            return $@"

--- MANDATORY OUTPUT FORMAT ---
Regardless of any other formatting instructions above, you MUST return your response as a single JSON object with exactly this structure. Do NOT wrap it in markdown code blocks. Do NOT include any text before or after the JSON.

IMPORTANT: You MUST produce EXACTLY {themes} theme(s), EXACTLY {recommendations} recommendation(s), and EXACTLY {actionSteps} action step(s) per recommendation. No more, no less.

{{
  ""report_metadata"": {{
    ""business_unit"": ""string"",
    ""company"": ""string"",
    ""period_start"": ""YYYY-MM-DD"",
    ""period_end"": ""YYYY-MM-DD"",
    ""total_responses"": 0,
    ""overall_score"": 0
  }},
  ""health_scores"": {{
    ""Communication"": {{ ""score"": 0, ""status"": ""string"" }},
    ""Learning"": {{ ""score"": 0, ""status"": ""string"" }},
    ""Opportunities"": {{ ""score"": 0, ""status"": ""string"" }},
    ""Vulnerability"": {{ ""score"": 0, ""status"": ""string"" }},
    ""Enablement"": {{ ""score"": 0, ""status"": ""string"" }},
    ""Reflection"": {{ ""score"": 0, ""status"": ""string"" }}
  }},
  ""top_themes"": [
    {themesArray}
  ],
  ""top_recommendations"": [
    {recommendationsArray}
  ]
}}

Return ONLY this JSON object. English only. Be concise.";
        }

        /// <summary>
        /// Appends the mandatory output format to the user content so the LLM
        /// always returns the standardised JSON structure. Parses ratio from
        /// systemPrompt if present; otherwise applies defaults (4/3/3).
        /// </summary>
        private static string AppendStandardOutputFormat(string userContent, string? systemPrompt = null)
        {
            if (string.IsNullOrWhiteSpace(userContent))
                return userContent;

            // If the system prompt already defines its own JSON output schema, do NOT
            // append a second conflicting one — the duplicate confuses the LLM
            // and causes it to produce unpredictable array counts.
            if (PromptAlreadyDefinesOutputFormat(systemPrompt) || PromptAlreadyDefinesOutputFormat(userContent))
                return userContent;

            var (themes, recommendations, actionSteps) =
                ResponseValidatorService.ParseRatioFromSystemPrompt(systemPrompt);

            return userContent + BuildStandardOutputFormatSuffix(themes, recommendations, actionSteps);
        }

        /// <summary>
        /// Returns true when the prompt text already contains a JSON output
        /// schema (e.g. "top_themes", "top_recommendations", or the mandatory
        /// output format header). In that case we must NOT append a second schema.
        /// </summary>
        private static bool PromptAlreadyDefinesOutputFormat(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return false;

            // Check for the suffix we would add (already present from a prior call)
            if (content.Contains("MANDATORY OUTPUT FORMAT", StringComparison.OrdinalIgnoreCase))
                return true;

            // Check for inline JSON schema keys that indicate the prompt author
            // already specified the output structure
            var hasThemes = content.Contains("\"top_themes\"", StringComparison.OrdinalIgnoreCase)
                         || content.Contains("\"TopThemes\"", StringComparison.Ordinal);
            var hasRecs = content.Contains("\"top_recommendations\"", StringComparison.OrdinalIgnoreCase)
                       || content.Contains("\"TopRecommendations\"", StringComparison.Ordinal);

            return hasThemes && hasRecs;
        }

        /// <summary>
        /// When the SystemPrompt already defines a complete output schema,
        /// strip any conflicting "Output Format" section from the Template
        /// so the model sees only ONE schema (the SystemPrompt's).
        /// Replaces the stripped section with a short redirect instruction.
        /// </summary>
        private static string StripConflictingOutputFormat(string template, string? systemPrompt)
        {
            // Only strip if SystemPrompt defines a schema AND Template also defines one
            if (!PromptAlreadyDefinesOutputFormat(systemPrompt) ||
                !PromptAlreadyDefinesOutputFormat(template))
                return template;

            // Remove the "Output Format" section and everything after it that looks
            // like a JSON schema block, up to the next heading or end of string.
            // Common patterns: "## Output Format", "Output Format\n", "--- MANDATORY OUTPUT FORMAT ---"
            var pattern = @"(?:#+\s*)?Output\s+Format[^\n]*\n[\s\S]*?(?=\n#+\s|\z)";
            var stripped = Regex.Replace(template, pattern, "", RegexOptions.IgnoreCase).TrimEnd();

            // Also strip standalone "IMPORTANT: Return ONLY..." lines that duplicate SystemPrompt
            stripped = Regex.Replace(stripped,
                @"\n*IMPORTANT:\s*Return ONLY[^\n]*",
                "", RegexOptions.IgnoreCase).TrimEnd();

            // Append a redirect to the system instructions
            stripped += @"

IMPORTANT: Output your response as a single JSON object following the EXACT schema defined in your system instructions. No markdown. No commentary. No chain-of-thought. Return ONLY the JSON object.";

            return stripped;
        }

        /// <summary>
        /// Ensures maxTokens is large enough for the expected response.
        /// Estimates input token count (~4 chars per token for English text)
        /// and sets a floor based on the input size plus a fixed minimum.
        /// </summary>
        private static int EnsureAdequateMaxTokens(string? systemPrompt, string userContent, int currentMaxTokens)
        {
            const int absoluteMinimum = 4096;
            const int charsPerToken = 4;
            const int maxCeiling = 16384;

            var totalInputChars = (systemPrompt?.Length ?? 0) + (userContent?.Length ?? 0);
            var estimatedInputTokens = totalInputChars / charsPerToken;

            // Response typically needs at least 50% of input tokens for detailed analysis,
            // with a minimum floor of 4096 to prevent truncation on complex prompts.
            var recommended = Math.Max(absoluteMinimum, estimatedInputTokens / 2);

            // Don't exceed a reasonable ceiling
            recommended = Math.Min(recommended, maxCeiling);

            // Only adjust upward, never reduce what was explicitly set
            return Math.Max(currentMaxTokens, (int)recommended);
        }

        /// <summary>
        /// Attempts to normalise an LLM response into valid JSON. Handles common
        /// cases: markdown code fences, leading/trailing prose around a JSON block.
        /// Returns the original string if it is already valid JSON.
        /// </summary>
        private static string NormalizeToJson(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return response;

            var trimmed = response.Trim();

            // Already valid JSON?
            if (IsValidJson(trimmed))
                return trimmed;

            // Strip markdown code fences: ```json ... ``` or ``` ... ```
            var fenceMatch = Regex.Match(trimmed, @"```(?:json)?\s*\n?([\s\S]*?)\n?\s*```", RegexOptions.IgnoreCase);
            if (fenceMatch.Success)
            {
                var inner = fenceMatch.Groups[1].Value.Trim();
                if (IsValidJson(inner))
                    return inner;
            }

            // Try to extract the first { ... } block
            var firstBrace = trimmed.IndexOf('{');
            var lastBrace = trimmed.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                var candidate = trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
                if (IsValidJson(candidate))
                    return candidate;
            }

            // Nothing worked; return as-is and let BuildTopInsight handle the skip.
            return trimmed;
        }

        private static bool IsValidJson(string text)
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                return doc.RootElement.ValueKind == JsonValueKind.Object
                    || doc.RootElement.ValueKind == JsonValueKind.Array;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Truncates JSON arrays to match the exact counts specified in the prompt.
        /// Handles both snake_case (top_themes, top_recommendations) and
        /// PascalCase (TopThemes, TopRecommendations, TopActions) keys.
        /// </summary>
        private static string EnforceArrayCounts(string json, int themes, int recommendations, int actionSteps)
        {
            if (string.IsNullOrWhiteSpace(json))
                return json;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return json;

                var root = doc.RootElement;
                using var ms = new System.IO.MemoryStream();
                using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    foreach (var prop in root.EnumerateObject())
                    {
                        var limit = GetArrayLimit(prop.Name, themes, recommendations, actionSteps);
                        if (limit.HasValue && prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            writer.WritePropertyName(prop.Name);
                            writer.WriteStartArray();
                            int count = 0;
                            foreach (var item in prop.Value.EnumerateArray())
                            {
                                if (count >= limit.Value) break;
                                // If recommendation objects contain action_steps arrays, truncate those too
                                if (item.ValueKind == JsonValueKind.Object)
                                {
                                    writer.WriteStartObject();
                                    foreach (var inner in item.EnumerateObject())
                                    {
                                        var innerLimit = GetActionStepsLimit(inner.Name, actionSteps);
                                        if (innerLimit.HasValue && inner.Value.ValueKind == JsonValueKind.Array)
                                        {
                                            writer.WritePropertyName(inner.Name);
                                            writer.WriteStartArray();
                                            int innerCount = 0;
                                            foreach (var step in inner.Value.EnumerateArray())
                                            {
                                                if (innerCount >= innerLimit.Value) break;
                                                step.WriteTo(writer);
                                                innerCount++;
                                            }
                                            writer.WriteEndArray();
                                        }
                                        else
                                        {
                                            inner.WriteTo(writer);
                                        }
                                    }
                                    writer.WriteEndObject();
                                }
                                else
                                {
                                    item.WriteTo(writer);
                                }
                                count++;
                            }
                            writer.WriteEndArray();
                        }
                        else
                        {
                            prop.WriteTo(writer);
                        }
                    }
                    writer.WriteEndObject();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
            catch
            {
                return json; // If anything fails, return original
            }
        }

        private static int? GetArrayLimit(string propertyName, int themes, int recommendations, int actionSteps)
        {
            return propertyName switch
            {
                "top_themes" or "TopThemes" => themes,
                "top_recommendations" or "TopRecommendations" => recommendations,
                "TopActions" or "top_actions" => actionSteps,
                _ => null
            };
        }

        private static int? GetActionStepsLimit(string propertyName, int actionSteps)
        {
            return propertyName switch
            {
                "action_steps" or "ActionSteps" => actionSteps,
                _ => null
            };
        }

        public async Task<PromptTemplate?> GetActivePromptAsync(string name, CancellationToken ct)
        {
            var cacheKey = $"prompt:{name}:active";
            if (_cache.TryGetValue(cacheKey, out PromptTemplate? cached))
                return cached;

            var prompt = await _repo.GetActiveByNameAsync(name, ct);
            if (prompt != null)
                _cache.Set(cacheKey, prompt, TimeSpan.FromMinutes(5));

            return prompt;
        }

        public async Task<PromptTemplate?> GetPromptByVersionAsync(string name, string version, CancellationToken ct)
        {
            return await _repo.GetByNameAndVersionAsync(name, version, ct);
        }

        public async Task<List<PromptTemplateResponseDto>> GetVersionsAsync(string name, CancellationToken ct)
        {
            var list = await _repo.GetVersionsByNameAsync(name, ct);
            return list.Select(MapToDto).ToList();
        }

        public async Task<List<PromptTemplateResponseDto>> GetAllActiveAsync(CancellationToken ct)
        {
            var list = await _repo.GetAllActiveAsync(ct);
            return list.Select(MapToDto).ToList();
        }

        public async Task<PromptTemplateResponseDto> UpsertPromptAsync(CreatePromptTemplateDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Template))
                dto.Template = "For Required Result";

            if (dto.ActivateImmediately)
                await _repo.DeactivateAllByNameAsync(dto.Name, ct);

            PromptTemplate entity;

            if (dto.Id.HasValue && dto.Id.Value > 0)
            {
                // Update existing prompt
                entity = await _repo.GetByIdAsync(dto.Id.Value, ct)
                    ?? throw new KeyNotFoundException($"Prompt with Id {dto.Id.Value} not found.");

                entity.Name = dto.Name;
                entity.Version = dto.Version;
                entity.Description = dto.Description;
                entity.SystemPrompt = dto.SystemPrompt;
                entity.Template = dto.Template;
                entity.IsActive = dto.ActivateImmediately;
                entity.MetadataJson = dto.MetadataJson;
                entity.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                // Create new prompt
                entity = new PromptTemplate
                {
                    Name = dto.Name,
                    Version = dto.Version,
                    Description = dto.Description,
                    SystemPrompt = dto.SystemPrompt,
                    Template = dto.Template,
                    IsActive = dto.ActivateImmediately,
                    MetadataJson = dto.MetadataJson,
                    CreatedAt = DateTime.UtcNow
                };

                await _repo.AddAsync(entity);
            }

            await _repo.SaveChangesAsync(ct);

            _cache.Remove($"prompt:{dto.Name}:active");

            return MapToDto(entity);
        }

        public async Task<bool> ActivateVersionAsync(string name, string version, CancellationToken ct)
        {
            var target = await _repo.GetByNameAndVersionAsync(name, version, ct);
            if (target == null) return false;

            await _repo.DeactivateAllByNameAsync(name, ct);
            target.IsActive = true;
            target.UpdatedAt = DateTime.UtcNow;
            await _repo.SaveChangesAsync(ct);

            _cache.Remove($"prompt:{name}:active");
            return true;
        }

        public string RenderTemplate(string template, Dictionary<string, string> variables)
        {
            var result = template;
            foreach (var kv in variables)
                result = result.Replace($"{{{{{kv.Key}}}}}", kv.Value);
            return result;
        }

        // ── Test Case CRUD ──

        public async Task<TestCaseResponseDto> UpsertTestCaseAsync(UpsertTestCaseRequest dto, CancellationToken ct)
        {
            TestCase entity;

            if (dto.Id.HasValue && dto.Id.Value > 0)
            {
                entity = await _repo.GetTestCaseByIdAsync(dto.Id.Value, ct)
                    ?? throw new KeyNotFoundException($"TestCase with Id {dto.Id.Value} not found.");

                entity.Name = dto.Name;
                entity.PromptTemplateName = dto.PromptTemplateName;
                entity.InputDataJson = dto.InputDataJson;
                entity.ModifiedAt = DateTime.UtcNow;

                // Replace assertions: soft-delete old, add new
                await _repo.DeleteAssertionsByTestCaseIdAsync(entity.Id, ct);
            }
            else
            {
                entity = new TestCase
                {
                    Name = dto.Name,
                    PromptTemplateName = dto.PromptTemplateName,
                    InputDataJson = dto.InputDataJson,
                    CreatedAt = DateTime.UtcNow
                };
                await _repo.AddTestCaseAsync(entity);
                await _repo.SaveChangesAsync(ct); // get the generated Id
            }

            if (dto.Assertions.Any())
            {
                var assertions = dto.Assertions.Select(a => new TestCaseAssertion
                {
                    TestCaseId = entity.Id,
                    Name = a.Name,
                    AssertionType = a.AssertionType,
                    JsonPath = a.JsonPath,
                    Operator = a.Operator,
                    ExpectedValue = a.ExpectedValue,
                    CreatedAt = DateTime.UtcNow
                }).ToList();

                await _repo.AddAssertionsAsync(assertions);
            }

            await _repo.SaveChangesAsync(ct);

            var count = await _repo.GetAssertionCountByTestCaseIdAsync(entity.Id, ct);
            return MapTestCaseToDto(entity, count);
        }

        public async Task<List<TestCaseResponseDto>> GetTestCasesAsync(string? promptName, CancellationToken ct)
        {
            var list = string.IsNullOrWhiteSpace(promptName)
                ? await _repo.GetAllTestCasesAsync(ct)
                : await _repo.GetTestCasesByPromptNameAsync(promptName, ct);

            var result = new List<TestCaseResponseDto>();
            foreach (var tc in list)
            {
                var count = await _repo.GetAssertionCountByTestCaseIdAsync(tc.Id, ct);
                result.Add(MapTestCaseToDto(tc, count));
            }
            return result;
        }

        public async Task<TestCaseDetailResponseDto?> GetTestCaseByIdAsync(int id, CancellationToken ct)
        {
            var entity = await _repo.GetTestCaseByIdAsync(id, ct);
            if (entity == null) return null;

            var assertions = await _repo.GetAssertionsByTestCaseIdAsync(id, ct);

            return new TestCaseDetailResponseDto
            {
                Id = entity.Id,
                Name = entity.Name,
                PromptTemplateName = entity.PromptTemplateName,
                InputDataJson = entity.InputDataJson,
                AssertionCount = assertions.Count,
                CreatedAt = entity.CreatedAt,
                CreatedBy = entity.CreatedBy,
                ModifiedAt = entity.ModifiedAt,
                Assertions = assertions.Select(a => new TestCaseAssertionDto
                {
                    Id = a.Id,
                    Name = a.Name,
                    AssertionType = a.AssertionType,
                    JsonPath = a.JsonPath,
                    Operator = a.Operator,
                    ExpectedValue = a.ExpectedValue
                }).ToList()
            };
        }

        public async Task<bool> DeletePromptAsync(int id, CancellationToken ct)
        {
            var entity = await _repo.GetByIdAsync(id, ct);
            if (entity == null) return false;

            await _repo.SoftDeletePromptAsync(id, ct);
            await _repo.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> DeleteTestCaseAsync(int id, CancellationToken ct)
        {
            var entity = await _repo.GetTestCaseByIdAsync(id, ct);
            if (entity == null) return false;

            await _repo.DeleteAssertionsByTestCaseIdAsync(id, ct);
            await _repo.SoftDeleteTestCaseAsync(id, ct);
            await _repo.SaveChangesAsync(ct);
            return true;
        }

        // ── Workbench Run ──

        public async Task<WorkbenchRunResultDto> RunWorkbenchAsync(
            WorkbenchRunRequestDto request, List<int> modelIds, IFormFile? testCaseFile, CancellationToken ct)
        {
            var prompt = await _repo.GetByIdAsync(request.PromptId, ct)
                ?? throw new KeyNotFoundException($"Prompt with Id {request.PromptId} not found.");

            var models = await _embeddingRepo.GetModelsByIdsAsync(modelIds);
            if (!models.Any())
                throw new KeyNotFoundException("No valid models found for the provided IDs.");

            // Resolve test cases based on source type
            var inlineTestCases = await ResolveTestCasesAsync(request, testCaseFile, ct);
            if (!inlineTestCases.Any())
                throw new KeyNotFoundException("No test cases provided. Please supply at least one test case.");

            var results = new List<WorkbenchRunItemDto>();

            foreach (var model in models)
            {
                foreach (var tc in inlineTestCases)
                {
                    var item = new WorkbenchRunItemDto
                    {
                        ModelId = model.Id,
                        ModelName = model.FriendlyName ?? model.ModelName,
                        Provider = model.Provider,
                        TestCaseId = 0,
                        TestCaseName = tc.Name
                    };

                    try
                    {
                        // Parse input variables from test case JSON
                        var variables = new Dictionary<string, string>();
                        if (!string.IsNullOrWhiteSpace(tc.InputDataJson))
                        {
                            using var doc = JsonDocument.Parse(tc.InputDataJson);
                            foreach (var prop in doc.RootElement.EnumerateObject())
                            {
                                variables[prop.Name] = prop.Value.ToString();
                            }
                        }

                        // Skip LLM call if no variables or all values are empty
                        if (!variables.Any() || variables.Values.All(v => string.IsNullOrWhiteSpace(v)))
                        {
                            item.Error = "Skipped: All input variables are empty. Provide at least one non-empty value.";
                            results.Add(item);
                            continue;
                        }

                        // Strip conflicting Output Format from Template when SystemPrompt
                        // already defines the schema — prevents the model seeing two schemas
                        var cleanedTemplate = StripConflictingOutputFormat(prompt.Template, prompt.SystemPrompt);
                        var renderedPrompt = RenderTemplate(cleanedTemplate, variables);

                        // Skip if rendered prompt still contains unresolved placeholders or is effectively empty
                        var renderedContent = renderedPrompt.Trim();
                        if (string.IsNullOrWhiteSpace(renderedContent))
                        {
                            item.Error = "Skipped: Rendered prompt is empty after variable substitution.";
                            results.Add(item);
                            continue;
                        }

                        // Call LLM – append standardised output format to every prompt
                        var temperature = request.Temperature ?? model.Temperature;
                        var maxTokens = request.MaxTokens ?? model.MaxTokens;
                        var topP = request.TopP ?? model.TopP;

                        var enforced = AppendStandardOutputFormat(renderedPrompt, prompt.SystemPrompt);

                        // Auto-adjust maxTokens: ensure enough room for the response.
                        // Estimate input tokens (~4 chars per token) and set output tokens
                        // to at least 4096 or 50% of estimated input, whichever is larger.
                        maxTokens = EnsureAdequateMaxTokens(prompt.SystemPrompt, enforced, maxTokens);

                        var sw = Stopwatch.StartNew();
                        var (content, promptTokens, completionTokens, totalTokens) =
                            await CallLlmAsync(prompt.SystemPrompt, enforced, model, temperature, maxTokens, topP, ct);
                        sw.Stop();

                        // Normalize response to valid JSON (strips code fences, prose, etc.)
                        content = NormalizeToJson(content);

                        // Enforce exact array counts from prompt ratio
                        var (enfThemes, enfRecs, enfActions) =
                            ResponseValidatorService.ParseRatioFromSystemPrompt(prompt.SystemPrompt);
                        content = EnforceArrayCounts(content, enfThemes, enfRecs, enfActions);

                        item.Response = content;
                        item.PromptTokens = promptTokens;
                        item.CompletionTokens = completionTokens;
                        item.TotalTokens = totalTokens;
                        item.LatencyMs = sw.ElapsedMilliseconds;

                        // Evaluate inline assertions
                        if (tc.Assertions.Any())
                        {
                            item.Assertions = EvaluateInlineAssertions(tc.Assertions, content);
                        }
                    }
                    catch (Exception ex)
                    {
                        item.Error = ex.Message;
                    }

                    results.Add(item);
                }
            }

            return new WorkbenchRunResultDto
            {
                PromptId = prompt.Id,
                PromptName = prompt.Name,
                Results = results
            };
        }

        // ── Workbench Run via Promptfoo ──

        public async Task<PromptfooEvalResultDto> RunWorkbenchWithPromptfooAsync(
            WorkbenchRunRequestDto request, List<int> modelIds, IFormFile? testCaseFile, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInformation("[Promptfoo] ======= Starting RunWorkbenchWithPromptfooAsync =======");
            _logCollector.Add("INFO", "[Promptfoo] ======= Starting RunWorkbenchWithPromptfooAsync =======");
            _logger.LogInformation("[Promptfoo] PromptId={PromptId}, ModelIds=[{ModelIds}], Temperature={Temp}, MaxTokens={MaxTokens}, TopP={TopP}",
                request.PromptId, string.Join(",", modelIds), request.Temperature, request.MaxTokens, request.TopP);
            _logCollector.Add("INFO", $"[Promptfoo] PromptId={request.PromptId}, ModelIds=[{string.Join(",", modelIds)}], Temperature={request.Temperature}, MaxTokens={request.MaxTokens}, TopP={request.TopP}");
            _logger.LogInformation("[Promptfoo] HasFile={HasFile}, FileName={FileName}, HasManualJson={HasManual}",
                testCaseFile != null, testCaseFile?.FileName, !string.IsNullOrWhiteSpace(request.ManualTestCaseJson));
            _logCollector.Add("INFO", $"[Promptfoo] HasFile={testCaseFile != null}, FileName={testCaseFile?.FileName}, HasManualJson={!string.IsNullOrWhiteSpace(request.ManualTestCaseJson)}");
            _logger.LogInformation("[Promptfoo] AppBaseDirectory={AppDir}", AppContext.BaseDirectory);
            _logCollector.Add("INFO", $"[Promptfoo] AppBaseDirectory={AppContext.BaseDirectory}");
            _logger.LogInformation("[Promptfoo] TempPath={TempPath}", Path.GetTempPath());
            _logCollector.Add("INFO", $"[Promptfoo] TempPath={Path.GetTempPath()}");
            _logger.LogInformation("[Promptfoo] Environment={Env}", _env.EnvironmentName);
            _logCollector.Add("INFO", $"[Promptfoo] Environment={_env.EnvironmentName}");

            // ── Step 1: Validate inputs from DB ──
            _logger.LogInformation("[Promptfoo] Step 1: Validating inputs from DB...");
            _logCollector.Add("INFO", "[Promptfoo] Step 1: Validating inputs from DB...");
            var prompt = await _repo.GetByIdAsync(request.PromptId, ct)
                ?? throw new KeyNotFoundException($"Prompt with Id {request.PromptId} not found.");
            _logger.LogInformation("[Promptfoo] Prompt found: Id={Id}, Name={Name}", prompt.Id, prompt.Name);
            _logCollector.Add("INFO", $"[Promptfoo] Prompt found: Id={prompt.Id}, Name={prompt.Name}");

            var models = await _embeddingRepo.GetModelsByIdsAsync(modelIds);
            if (!models.Any())
                throw new KeyNotFoundException("No valid models found for the provided IDs.");
            _logger.LogInformation("[Promptfoo] Models found: {Count} models - [{ModelNames}]",
                models.Count, string.Join(", ", models.Select(m => $"{m.Id}:{m.ModelName}")));
            _logCollector.Add("INFO", $"[Promptfoo] Models found: {models.Count} models - [{string.Join(", ", models.Select(m => $"{m.Id}:{m.ModelName}"))}]");

            // ── Step 2: Parse test cases from file or manual JSON ──
            _logger.LogInformation("[Promptfoo] Step 2: Parsing test cases...");
            _logCollector.Add("INFO", "[Promptfoo] Step 2: Parsing test cases...");
            List<InlineTestCaseDto> inlineTestCases;
            try
            {
                inlineTestCases = await ResolveTestCasesAsync(request, testCaseFile, ct);
            }
            catch (KeyNotFoundException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Promptfoo] Failed to parse test cases");
                _logCollector.Add("ERROR", $"[Promptfoo] Failed to parse test cases: {ex.Message}");
                throw new InvalidOperationException($"Failed to parse test cases: {ex.Message}", ex);
            }

            if (!inlineTestCases.Any())
                throw new KeyNotFoundException("No test cases provided. Please supply at least one test case.");
            _logger.LogInformation("[Promptfoo] Parsed {Count} test cases in {Elapsed}ms", inlineTestCases.Count, sw.ElapsedMilliseconds);
            _logCollector.Add("INFO", $"[Promptfoo] Parsed {inlineTestCases.Count} test cases in {sw.ElapsedMilliseconds}ms");

            // ── Step 3: Build config, execute Promptfoo, parse results ──
            var runId = Guid.NewGuid().ToString("N");
            var tempDir = Path.Combine(Path.GetTempPath(), "promptfoo_workbench", runId);
            Directory.CreateDirectory(tempDir);
            _logger.LogInformation("[Promptfoo] Step 3: TempDir created at {TempDir}", tempDir);
            _logCollector.Add("INFO", $"[Promptfoo] Step 3: TempDir created at {tempDir}");

            try
            {
                // Build promptfoo YAML config
                _logger.LogInformation("[Promptfoo] Building YAML config...");
                _logCollector.Add("INFO", "[Promptfoo] Building YAML config...");
                string configYaml;
                try
                {
                    configYaml = BuildPromptfooConfigYaml(prompt, models, inlineTestCases, request, tempDir);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Promptfoo] Failed to build YAML config");
                    _logCollector.Add("ERROR", $"[Promptfoo] Failed to build YAML config: {ex.Message}");
                    throw new InvalidOperationException($"Failed to build Promptfoo config: {ex.Message}", ex);
                }

                var configPath = Path.Combine(tempDir, "promptfooconfig.yaml");
                var outputPath = Path.Combine(tempDir, "output.json");

                await File.WriteAllTextAsync(configPath, configYaml, ct);
                _logger.LogInformation("[Promptfoo] YAML config written to {ConfigPath} ({Elapsed}ms elapsed)", configPath, sw.ElapsedMilliseconds);
                _logCollector.Add("INFO", $"[Promptfoo] YAML config written to {configPath} ({sw.ElapsedMilliseconds}ms elapsed)");
                _logger.LogDebug("[Promptfoo] YAML config content:\n{ConfigYaml}", configYaml);

                // Execute promptfoo CLI
                _logger.LogInformation("[Promptfoo] Step 4: Executing promptfoo CLI...");
                _logCollector.Add("INFO", "[Promptfoo] Step 4: Executing promptfoo CLI...");
                PromptfooProcessResult promptfooResult;
                try
                {
                    promptfooResult = await ExecutePromptfooAsync(configPath, outputPath, tempDir, ct);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("[Promptfoo] Process was CANCELLED/TIMED OUT after {Elapsed}ms", sw.ElapsedMilliseconds);
                    _logCollector.Add("WARN", $"[Promptfoo] Process was CANCELLED/TIMED OUT after {sw.ElapsedMilliseconds}ms");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Promptfoo] Process execution FAILED after {Elapsed}ms", sw.ElapsedMilliseconds);
                    _logCollector.Add("ERROR", $"[Promptfoo] Process execution FAILED after {sw.ElapsedMilliseconds}ms: {ex.Message}");
                    throw new InvalidOperationException($"Promptfoo execution failed: {ex.Message}", ex);
                }

                _logger.LogInformation("[Promptfoo] Process completed with ExitCode={ExitCode} after {Elapsed}ms",
                    promptfooResult.ExitCode, sw.ElapsedMilliseconds);
                _logCollector.Add("INFO", $"[Promptfoo] Process completed with ExitCode={promptfooResult.ExitCode} after {sw.ElapsedMilliseconds}ms");

                // Parse the output
                if (!File.Exists(outputPath))
                {
                    var errorDetails = !string.IsNullOrWhiteSpace(promptfooResult.Stderr)
                        ? promptfooResult.Stderr
                        : promptfooResult.Stdout;
                    _logger.LogError("[Promptfoo] Output file NOT found at {OutputPath}. Stderr={Stderr}, Stdout={Stdout}",
                        outputPath, promptfooResult.Stderr, promptfooResult.Stdout);
                    _logCollector.Add("ERROR", $"[Promptfoo] Output file NOT found at {outputPath}. Stderr={promptfooResult.Stderr}, Stdout={promptfooResult.Stdout}");
                    throw new InvalidOperationException(
                        $"Promptfoo did not generate results (exit code: {promptfooResult.ExitCode}). Details: {errorDetails}");
                }

                _logger.LogInformation("[Promptfoo] Step 5: Parsing output file {OutputPath}...", outputPath);
                _logCollector.Add("INFO", $"[Promptfoo] Step 5: Parsing output file {outputPath}...");
                var outputJson = await File.ReadAllTextAsync(outputPath, ct);
                _logger.LogInformation("[Promptfoo] Output JSON size: {Size} bytes", outputJson.Length);
                _logCollector.Add("INFO", $"[Promptfoo] Output JSON size: {outputJson.Length} bytes");
                var evalResult = ParsePromptfooResults(outputJson, prompt, promptfooResult.ShareUrl);
                _logger.LogInformation("[Promptfoo] Parsed results: ShareUrl={ShareUrl}", promptfooResult.ShareUrl);
                _logCollector.Add("INFO", $"[Promptfoo] Parsed results: ShareUrl={promptfooResult.ShareUrl}");

                await CalculateCostsAsync(evalResult, models, ct);
                _logger.LogInformation("[Promptfoo] Cost calculation complete: TotalCost={TotalCost}", evalResult.Summary.TotalEstimatedCost);
                _logCollector.Add("INFO", $"[Promptfoo] Cost calculation complete: TotalCost={evalResult.Summary.TotalEstimatedCost}");

                await SavePromptfooLogsAsync(evalResult, prompt, ct);
                _logger.LogInformation("[Promptfoo] ======= Completed successfully in {Elapsed}ms =======", sw.ElapsedMilliseconds);
                _logCollector.Add("INFO", $"[Promptfoo] ======= Completed successfully in {sw.ElapsedMilliseconds}ms =======");

                return evalResult;
            }
            finally
            {
                // Cleanup temp files
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
                catch { /* best-effort cleanup */ }
            }
        }

        private async Task SavePromptfooLogsAsync(PromptfooEvalResultDto result, PromptTemplate prompt, CancellationToken ct)
        {
            try
            {
                var evalId = result.ShareUrl ?? result.DashboardUrl ?? Guid.NewGuid().ToString();

                foreach (var testResult in result.TestResults)
                {
                    bool isJsonValid = false;
                    if (!string.IsNullOrWhiteSpace(testResult.Response))
                    {
                        try { using var doc = JsonDocument.Parse(testResult.Response); isJsonValid = true; }
                        catch { /* not valid JSON */ }
                    }

                    var log = new AIRequestLog
                    {
                        PromptName = prompt.Name,
                        PromptVersion = prompt.Version,
                        ModelRequested = !string.IsNullOrEmpty(testResult.ModelName) ? testResult.ModelName : testResult.ProviderName,
                        ModelUsed = !string.IsNullOrEmpty(testResult.ModelName) ? testResult.ModelName : testResult.ProviderName,
                        FallbackUsed = false,
                        LatencyMs = testResult.LatencyMs,
                        PromptTokens = testResult.PromptTokens,
                        CompletionTokens = testResult.CompletionTokens,
                        EstimatedCost = testResult.EstimatedCost,
                        IsJsonValid = isJsonValid,
                        AssertionsPassed = testResult.Assertions.Count(a => a.Pass),
                        AssertionsTotal = testResult.Assertions.Count,
                        Success = testResult.Pass,
                        ErrorMessage = testResult.Error,
                        ResponsePayload = testResult.Response,
                        EvalId = evalId,
                        CreatedAt = DateTime.UtcNow
                    };

                    await _logRepo.AddAsync(log);
                }

                await _logRepo.SaveChangesAsync(ct);
            }
            catch
            {
                // Logging failure must never break the response
            }
        }

        private async Task CalculateCostsAsync(PromptfooEvalResultDto result, List<ModelConfiguration> models, CancellationToken ct)
        {
            // Build lookups by FriendlyName and ModelName for matching
            var byFriendly = new Dictionary<string, ModelConfiguration>(StringComparer.OrdinalIgnoreCase);
            var byModelName = new Dictionary<string, ModelConfiguration>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in models)
            {
                byFriendly.TryAdd(m.FriendlyName, m);
                byModelName.TryAdd(m.ModelName, m);
            }

            // Track models whose DB pricing needs updating
            var modelsToUpdate = new Dictionary<int, (decimal InputPer1k, decimal OutputPer1k)>();

            _logCollector.Add("INFO", $"[CostCalc] Models available: [{string.Join(", ", models.Select(m => $"FriendlyName='{m.FriendlyName}' ModelName='{m.ModelName}' InCost={m.InputCostPer1k} OutCost={m.OutputCostPer1k}"))}]");

            foreach (var tr in result.TestResults)
            {
                // Estimate tokens from text length when provider doesn't return counts (~4 chars per token for Claude)
                if (tr.PromptTokens == 0 && !string.IsNullOrEmpty(tr.RenderedPrompt))
                    tr.PromptTokens = (int)Math.Ceiling(tr.RenderedPrompt.Length / 4.0);
                if (tr.CompletionTokens == 0 && !string.IsNullOrEmpty(tr.Response))
                    tr.CompletionTokens = (int)Math.Ceiling(tr.Response.Length / 4.0);
                if (tr.TotalTokens == 0)
                    tr.TotalTokens = tr.PromptTokens + tr.CompletionTokens;

                _logCollector.Add("INFO", $"[CostCalc] TestResult: ProviderName='{tr.ProviderName}' ModelName='{tr.ModelName}' PromptTokens={tr.PromptTokens} CompletionTokens={tr.CompletionTokens}");

                // Skip if cost was already set by the provider
                if (tr.EstimatedCost > 0)
                {
                    _logCollector.Add("INFO", $"[CostCalc] Skipping — already has cost={tr.EstimatedCost}");
                    continue;
                }

                // Match test result to a model config
                ModelConfiguration? matched = null;
                if (!string.IsNullOrEmpty(tr.ProviderName) && byFriendly.TryGetValue(tr.ProviderName, out var m1))
                    matched = m1;
                else if (!string.IsNullOrEmpty(tr.ModelName) && byModelName.TryGetValue(tr.ModelName, out var m2))
                    matched = m2;
                else if (!string.IsNullOrEmpty(tr.ModelName) && byFriendly.TryGetValue(tr.ModelName, out var m3))
                    matched = m3;
                else if (!string.IsNullOrEmpty(tr.ProviderName) && byModelName.TryGetValue(tr.ProviderName, out var m4))
                    matched = m4;

                if (matched != null)
                {
                    var inputCost = matched.InputCostPer1k;
                    var outputCost = matched.OutputCostPer1k;

                    // Fallback to known pricing if DB values are 0, and queue DB update
                    if (inputCost == 0 && outputCost == 0)
                    {
                        var modelKey = matched.ModelName?.Trim() ?? "";
                        if (KnownModelPricing.TryGetValue(modelKey, out var known))
                        {
                            inputCost = known.InputPer1k;
                            outputCost = known.OutputPer1k;
                            modelsToUpdate[matched.Id] = (inputCost, outputCost);
                            _logCollector.Add("INFO", $"[CostCalc] Using known pricing for '{modelKey}': InCost={inputCost} OutCost={outputCost} (will update DB)");
                        }
                        else
                        {
                            _logCollector.Add("WARN", $"[CostCalc] No known pricing for model '{modelKey}' and DB has 0 — cost will be 0");
                        }
                    }

                    tr.EstimatedCost = (tr.PromptTokens / 1000m) * inputCost
                                     + (tr.CompletionTokens / 1000m) * outputCost;
                    _logCollector.Add("INFO", $"[CostCalc] Matched to '{matched.FriendlyName}' (model='{matched.ModelName}') — InCost={inputCost} OutCost={outputCost} => EstimatedCost={tr.EstimatedCost}");
                }
                else
                {
                    _logCollector.Add("WARN", $"[CostCalc] NO MATCH found for ProviderName='{tr.ProviderName}' ModelName='{tr.ModelName}'");
                }
            }

            // Persist known pricing back to DB so it's available for other features
            foreach (var (modelId, pricing) in modelsToUpdate)
            {
                try
                {
                    await _embeddingRepo.UpdateModelPricingAsync(modelId, pricing.InputPer1k, pricing.OutputPer1k);
                    _logCollector.Add("INFO", $"[CostCalc] Updated DB pricing for modelId={modelId}: InCost={pricing.InputPer1k} OutCost={pricing.OutputPer1k}");
                }
                catch (Exception ex)
                {
                    _logCollector.Add("WARN", $"[CostCalc] Failed to update DB pricing for modelId={modelId}: {ex.Message}");
                }
            }

            // Aggregate into summary
            result.Summary.TotalEstimatedCost = result.TestResults.Sum(r => r.EstimatedCost);
            result.Summary.TotalPromptTokens = result.TestResults.Sum(r => r.PromptTokens);
            result.Summary.TotalCompletionTokens = result.TestResults.Sum(r => r.CompletionTokens);
            result.Summary.TotalTokens = result.TestResults.Sum(r => r.TotalTokens);
        }

        private string BuildPromptfooConfigYaml(
            PromptTemplate prompt,
            List<ModelConfiguration> models,
            List<InlineTestCaseDto> testCases,
            WorkbenchRunRequestDto request,
            string tempDir)
        {
            var sb = new StringBuilder();
            sb.AppendLine("description: \"Workbench Promptfoo Evaluation\"");
            sb.AppendLine();

            // ── Prompts ──
            // Write only the user-role template to the prompt file.
            // System prompt goes via provider-level config, NOT in the prompt file.
            // Append the standardised output format so every prompt, regardless of
            // what the user wrote, always produces the expected JSON structure.
            var promptFilePath = Path.Combine(tempDir, "prompt.txt");
            // Promptfoo splits .txt prompt files on lines that contain only "---",
            // treating each section as a separate prompt. Strip any such standalone
            // dash-only lines (3 or more dashes) so the combined template + suffix
            // stays as a single prompt regardless of what the template author used.
            var (themes, recs, actions) = ResponseValidatorService.ParseRatioFromSystemPrompt(prompt.SystemPrompt);
            // When SystemPrompt already defines the output schema, strip any
            // conflicting Output Format section from the Template so the model
            // sees only ONE authoritative schema. Otherwise append the standard suffix.
            string combinedPrompt;
            if (PromptAlreadyDefinesOutputFormat(prompt.SystemPrompt))
            {
                combinedPrompt = StripConflictingOutputFormat(prompt.Template, prompt.SystemPrompt);
            }
            else if (PromptAlreadyDefinesOutputFormat(prompt.Template))
            {
                combinedPrompt = prompt.Template;
            }
            else
            {
                combinedPrompt = prompt.Template + BuildStandardOutputFormatSuffix(themes, recs, actions);
            }
            combinedPrompt = System.Text.RegularExpressions.Regex.Replace(
                combinedPrompt,
                @"^[ \t]*-{3,}[ \t]*$",
                "",
                System.Text.RegularExpressions.RegexOptions.Multiline);
            File.WriteAllText(promptFilePath, combinedPrompt);

            sb.AppendLine("prompts:");
            sb.AppendLine($"  - file://{promptFilePath.Replace("\\", "/")}");
            sb.AppendLine();

            // ── Providers ──
            sb.AppendLine("providers:");
            // Parse prompt-level metadata overrides (Temperature, MaxTokens, TopP)
            double? promptTemperature = null;
            int? promptMaxTokens = null;
            double? promptTopP = null;
            if (!string.IsNullOrWhiteSpace(prompt.MetadataJson))
            {
                try
                {
                    using var metaDoc = JsonDocument.Parse(prompt.MetadataJson);
                    var root = metaDoc.RootElement;
                    if (root.TryGetProperty("Temperature", out var tempEl) && tempEl.TryGetDouble(out var tempVal))
                        promptTemperature = tempVal;
                    if (root.TryGetProperty("MaxTokens", out var maxEl) && maxEl.TryGetInt32(out var maxVal))
                        promptMaxTokens = maxVal;
                    if (root.TryGetProperty("TopP", out var topPEl) && topPEl.TryGetDouble(out var topPVal))
                        promptTopP = topPVal;
                }
                catch { /* invalid metadata JSON – fall through to model defaults */ }
            }

            foreach (var model in models)
            {
                // Priority: request override → prompt metadata → model default
                var temperature = request.Temperature ?? promptTemperature ?? model.Temperature;
                var maxTokens = request.MaxTokens ?? promptMaxTokens ?? model.MaxTokens;
                var topP = request.TopP ?? promptTopP ?? model.TopP;

                // Auto-adjust maxTokens based on prompt + system prompt length
                maxTokens = EnsureAdequateMaxTokens(prompt.SystemPrompt, combinedPrompt, maxTokens);

                var isAnthropic = IsAnthropicEndpoint(model);
                var isAIFoundryServerless = !isAnthropic && IsAIFoundryServerless(model);
                var isAzure = !isAIFoundryServerless && !isAnthropic && IsAzureEndpoint(model);

                if (isAnthropic)
                {
                    // Azure AI Foundry Anthropic endpoints are not supported by promptfoo's
                    // built-in anthropic:messages provider. Use the http provider for full
                    // control over headers, auth, and request/response format.
                    var endpointUrl = model.Endpoint?.Trim() ?? "";

                    // Use a unique label that includes the actual model name so that
                    // results from different Anthropic models can be distinguished in
                    // the promptfoo output (the http provider id doesn't carry model info).
                    var anthropicLabel = !string.IsNullOrWhiteSpace(model.FriendlyName)
                        ? $"{model.FriendlyName} ({model.ModelName?.Trim()})"
                        : model.ModelName?.Trim() ?? "http";
                    sb.AppendLine($"  - id: \"http\"");
                    sb.AppendLine($"    label: \"{EscapeYaml(anthropicLabel)}\"");
                    sb.AppendLine($"    config:");
                    sb.AppendLine($"      url: \"{endpointUrl}\"");
                    sb.AppendLine($"      method: POST");
                    sb.AppendLine($"      headers:");
                    sb.AppendLine($"        Content-Type: \"application/json\"");
                    sb.AppendLine($"        x-api-key: \"{model.ApiKey}\"");
                    sb.AppendLine($"        anthropic-version: \"2023-06-01\"");
                    sb.AppendLine($"      body:");
                    sb.AppendLine($"        model: \"{model.ModelName?.Trim()}\"");
                    // Include system prompt in the body for Anthropic format
                    if (!string.IsNullOrWhiteSpace(prompt.SystemPrompt))
                    {
                        var escapedSystem = EscapeYaml(prompt.SystemPrompt.Replace("\r\n", "\\n").Replace("\r", "\\n").Replace("\n", "\\n"));
                        sb.AppendLine($"        system: \"{escapedSystem}\"");
                    }
                    // Claude doesn't allow both temperature and top_p
                    if (topP < 1.0)
                        sb.AppendLine($"        top_p: {topP}");
                    else
                        sb.AppendLine($"        temperature: {temperature}");
                    sb.AppendLine($"        max_tokens: {maxTokens}");
                    sb.AppendLine($"        messages:");
                    sb.AppendLine($"          - role: \"user\"");
                    sb.AppendLine($"            content: \"{{{{prompt}}}}\"");
                    // Strip markdown code fences and extract the outermost JSON
                    // object from Claude's response. Azure AI Foundry's Anthropic
                    // endpoint does not support assistant-turn prefill, so we clean
                    // the response here instead: remove optional ```json ... ```
                    // wrappers and any prose before/after the JSON body.
                    sb.AppendLine($"      responseParser: \"(json.content[0].text.match(/\\\\{{[\\\\s\\\\S]*\\\\}}/) || [json.content[0].text])[0]\"");
                }
                else if (isAIFoundryServerless)
                {
                    // AI Foundry serverless (e.g. DeepSeek, Llama): use openai provider with apiBaseUrl
                    // pointing to /models so promptfoo calls /models/chat/completions
                    var baseUrl = ExtractAIFoundryBaseUrl(model.Endpoint);

                    sb.AppendLine($"  - id: \"openai:chat:{model.ModelName}\"");
                    sb.AppendLine($"    label: \"{EscapeYaml(model.FriendlyName ?? model.ModelName)}\"");
                    sb.AppendLine($"    config:");
                    sb.AppendLine($"      apiBaseUrl: \"{baseUrl}\"");
                    sb.AppendLine($"      apiKey: \"{model.ApiKey}\"");
                    sb.AppendLine($"      temperature: {temperature}");
                    sb.AppendLine($"      max_tokens: {maxTokens}");
                    sb.AppendLine($"      top_p: {topP}");
                }
                else if (isAzure)
                {
                    // Azure OpenAI: use azureopenai provider with apiHost + deploymentId
                    var (apiHost, deploymentId, apiVersion) = ParseAzureEndpoint(model.Endpoint);

                    // Fallback: if no deployment in the URL path, use the model name as the deployment name
                    if (string.IsNullOrEmpty(deploymentId))
                        deploymentId = model.ModelName;

                    // Fallback: if no api-version in URL query string, use the model's ApiVersion column
                    if (string.IsNullOrEmpty(apiVersion))
                        apiVersion = model.ApiVersion;

                    sb.AppendLine($"  - id: \"azureopenai:chat:{deploymentId}\"");
                    sb.AppendLine($"    label: \"{EscapeYaml(model.FriendlyName ?? model.ModelName)}\"");
                    sb.AppendLine($"    config:");
                    sb.AppendLine($"      apiHost: \"{apiHost}\"");
                    sb.AppendLine($"      apiKey: \"{model.ApiKey}\"");
                    if (!string.IsNullOrEmpty(apiVersion))
                        sb.AppendLine($"      apiVersion: \"{apiVersion}\"");
                    sb.AppendLine($"      temperature: {temperature}");
                    sb.AppendLine($"      max_tokens: {maxTokens}");
                    sb.AppendLine($"      top_p: {topP}");
                }
                else
                {
                    // OpenAI-compatible (LiteLLM, OpenAI, etc.): use openai provider with apiBaseUrl
                    var baseUrl = ExtractOpenAIBaseUrl(model.Endpoint);

                    sb.AppendLine($"  - id: \"openai:chat:{model.ModelName}\"");
                    sb.AppendLine($"    label: \"{EscapeYaml(model.FriendlyName ?? model.ModelName)}\"");
                    sb.AppendLine($"    config:");
                    if (!string.IsNullOrEmpty(baseUrl))
                        sb.AppendLine($"      apiBaseUrl: \"{baseUrl}\"");
                    sb.AppendLine($"      apiKey: \"{model.ApiKey}\"");
                    sb.AppendLine($"      temperature: {temperature}");
                    sb.AppendLine($"      max_tokens: {maxTokens}");
                    sb.AppendLine($"      top_p: {topP}");
                }

                // System prompt and JSON mode via provider config (OpenAI/Azure only, not HTTP provider)
                if (!isAnthropic && !string.IsNullOrWhiteSpace(prompt.SystemPrompt))
                {
                    var systemLines = prompt.SystemPrompt.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
                    sb.AppendLine($"      systemPrompt: |");
                    foreach (var line in systemLines)
                        sb.AppendLine($"        {line}");
                }

                // Enable JSON mode when the system prompt indicates JSON output (not for Anthropic HTTP provider)
                if (!isAnthropic && !string.IsNullOrWhiteSpace(prompt.SystemPrompt) &&
                    prompt.SystemPrompt.Contains("JSON", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("      response_format:");
                    sb.AppendLine("        type: json_object");
                }
            }
            sb.AppendLine();

            // ── Tests ──
            sb.AppendLine("tests:");
            foreach (var tc in testCases)
            {
                sb.AppendLine($"  - description: \"{EscapeYaml(tc.Name)}\"");

                // Parse input variables from test case JSON and map to promptfoo vars
                if (!string.IsNullOrWhiteSpace(tc.InputDataJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(tc.InputDataJson);
                        sb.AppendLine("    vars:");
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            var value = prop.Value.ToString();
                            if (value.Contains('\n'))
                            {
                                sb.AppendLine($"      {prop.Name}: |");
                                foreach (var line in value.Split('\n'))
                                    sb.AppendLine($"        {line.TrimEnd('\r')}");
                            }
                            else
                            {
                                sb.AppendLine($"      {prop.Name}: \"{EscapeYaml(value)}\"");
                            }
                        }
                    }
                    catch
                    {
                        sb.AppendLine("    vars:");
                        sb.AppendLine($"      input: \"{EscapeYaml(tc.InputDataJson)}\"");
                    }
                }

                // Assertions — map from our types to Promptfoo assertion types
                if (tc.Assertions.Any())
                {
                    sb.AppendLine("    assert:");
                    foreach (var assertion in tc.Assertions)
                    {
                        BuildPromptfooAssertion(sb, assertion);
                    }
                }
                else
                {
                    // Auto-inject default assertions when none are provided
                    BuildDefaultAssertions(sb);
                }
            }

            return sb.ToString();
        }

        private static bool IsAnthropicEndpoint(ModelConfiguration model)
        {
            var endpoint = (model.Endpoint ?? "").ToLowerInvariant();
            // Anthropic models on Azure AI Foundry use /anthropic/ in the path
            return endpoint.Contains("/anthropic/");
        }

        private static bool IsAIFoundryServerless(ModelConfiguration model)
        {
            var endpoint = (model.Endpoint ?? "").ToLowerInvariant();
            // AI Foundry serverless endpoints use .services.ai.azure.com with /models/ path
            return endpoint.Contains(".services.ai.azure.com") && endpoint.Contains("/models/");
        }

        private static string ExtractAIFoundryBaseUrl(string endpoint)
        {
            // Extract base URL up to and including /models
            // Input:  https://host.services.ai.azure.com/models/chat/completions?api-version=xxx
            // Output: https://host.services.ai.azure.com/models
            // Promptfoo's openai provider appends /chat/completions to apiBaseUrl
            try
            {
                var uri = new Uri(endpoint);
                var path = uri.AbsolutePath;
                var modelsIndex = path.IndexOf("/models", StringComparison.OrdinalIgnoreCase);
                if (modelsIndex >= 0)
                {
                    var basePath = path.Substring(0, modelsIndex + "/models".Length);
                    return $"{uri.Scheme}://{uri.Host}{basePath}";
                }
                return $"{uri.Scheme}://{uri.Host}";
            }
            catch
            {
                return endpoint;
            }
        }

        private static bool IsAzureEndpoint(ModelConfiguration model)
        {
            var source = (model.Source ?? "").ToLowerInvariant();
            var provider = (model.Provider ?? "").ToLowerInvariant();
            var endpoint = (model.Endpoint ?? "").ToLowerInvariant();

            // Check if it's an Azure endpoint by source/provider metadata or URL pattern
            if (source.Contains("azure") || provider.Contains("azure"))
                return true;

            if (endpoint.Contains(".openai.azure.com") || endpoint.Contains(".cognitiveservices.azure.com"))
                return true;

            return false;
        }

        private static (string ApiHost, string DeploymentId, string ApiVersion) ParseAzureEndpoint(string endpoint)
        {
            // Azure endpoint format:
            // https://RESOURCE.openai.azure.com/openai/deployments/DEPLOYMENT/chat/completions?api-version=2024-02-01
            try
            {
                var uri = new Uri(endpoint);
                var apiHost = uri.Host;

                // Extract deployment name from path: /openai/deployments/DEPLOYMENT/...
                var deploymentId = "";
                var pathParts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < pathParts.Length - 1; i++)
                {
                    if (pathParts[i].Equals("deployments", StringComparison.OrdinalIgnoreCase))
                    {
                        deploymentId = pathParts[i + 1];
                        break;
                    }
                }

                // Extract api-version from query string
                var apiVersion = "";
                var query = uri.Query;
                if (!string.IsNullOrEmpty(query))
                {
                    var queryParams = System.Web.HttpUtility.ParseQueryString(query);
                    apiVersion = queryParams["api-version"] ?? "";
                }

                return (apiHost, deploymentId, apiVersion);
            }
            catch
            {
                // Fallback: return host and model name
                try
                {
                    var uri = new Uri(endpoint);
                    return (uri.Host, "", "");
                }
                catch
                {
                    return (endpoint, "", "");
                }
            }
        }

        private static string ExtractOpenAIBaseUrl(string endpoint)
        {
            // For OpenAI-compatible endpoints (LiteLLM, OpenAI, etc.)
            // Input:  https://cloverera.litellm-sandbox.ai/v1/chat/completions
            // Output: https://cloverera.litellm-sandbox.ai/v1
            if (string.IsNullOrWhiteSpace(endpoint))
                return "";

            try
            {
                var uri = new Uri(endpoint);
                var baseUrl = $"{uri.Scheme}://{uri.Host}";
                if (uri.Port != 80 && uri.Port != 443)
                    baseUrl += $":{uri.Port}";

                // Walk path segments, stop at version segment (v1, v2, etc.)
                var pathParts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < pathParts.Length; i++)
                {
                    if (Regex.IsMatch(pathParts[i], @"^v\d+$", RegexOptions.IgnoreCase))
                    {
                        baseUrl += "/" + string.Join("/", pathParts.Take(i + 1));
                        return baseUrl;
                    }
                }

                // No version segment found — return scheme://host (let Promptfoo append /v1)
                return baseUrl;
            }
            catch
            {
                return endpoint;
            }
        }

        private static void BuildPromptfooAssertion(StringBuilder sb, InlineAssertionDto assertion)
        {
            var type = (assertion.AssertionType ?? "").ToLowerInvariant();

            switch (type)
            {
                case "json_path":
                    // json_path needs to become a JavaScript function that parses output,
                    // navigates the path, and evaluates the operator against expected value.
                    var jsCode = BuildJsonPathJavascript(
                        assertion.JsonPath, assertion.Operator, assertion.ExpectedValue);
                    sb.AppendLine("      - type: javascript");
                    sb.AppendLine("        value: |");
                    sb.AppendLine($"          {jsCode}");
                    break;

                case "contains":
                    sb.AppendLine("      - type: contains");
                    if (!string.IsNullOrWhiteSpace(assertion.ExpectedValue))
                        sb.AppendLine($"        value: \"{EscapeYaml(assertion.ExpectedValue)}\"");
                    break;

                case "not_contains":
                    sb.AppendLine("      - type: not-contains");
                    if (!string.IsNullOrWhiteSpace(assertion.ExpectedValue))
                        sb.AppendLine($"        value: \"{EscapeYaml(assertion.ExpectedValue)}\"");
                    break;

                case "icontains":
                    sb.AppendLine("      - type: icontains");
                    if (!string.IsNullOrWhiteSpace(assertion.ExpectedValue))
                        sb.AppendLine($"        value: \"{EscapeYaml(assertion.ExpectedValue)}\"");
                    break;

                case "equals":
                    sb.AppendLine("      - type: equals");
                    if (!string.IsNullOrWhiteSpace(assertion.ExpectedValue))
                        sb.AppendLine($"        value: \"{EscapeYaml(assertion.ExpectedValue)}\"");
                    break;

                case "is_json":
                    sb.AppendLine("      - type: is-json");
                    break;

                case "regex":
                    sb.AppendLine("      - type: contains");
                    if (!string.IsNullOrWhiteSpace(assertion.ExpectedValue))
                        sb.AppendLine($"        value: \"{EscapeYaml(assertion.ExpectedValue)}\"");
                    break;

                case "javascript":
                    // User-provided JavaScript — pass through as-is using block scalar
                    sb.AppendLine("      - type: javascript");
                    if (!string.IsNullOrWhiteSpace(assertion.ExpectedValue))
                    {
                        sb.AppendLine("        value: |");
                        sb.AppendLine($"          {assertion.ExpectedValue}");
                    }
                    break;

                case "similar":
                    sb.AppendLine("      - type: similar");
                    if (!string.IsNullOrWhiteSpace(assertion.ExpectedValue))
                        sb.AppendLine($"        value: \"{EscapeYaml(assertion.ExpectedValue)}\"");
                    break;

                case "llm_rubric":
                    sb.AppendLine("      - type: llm-rubric");
                    if (!string.IsNullOrWhiteSpace(assertion.ExpectedValue))
                        sb.AppendLine($"        value: \"{EscapeYaml(assertion.ExpectedValue)}\"");
                    break;

                default:
                    // Fallback: use contains
                    sb.AppendLine("      - type: contains");
                    if (!string.IsNullOrWhiteSpace(assertion.ExpectedValue))
                        sb.AppendLine($"        value: \"{EscapeYaml(assertion.ExpectedValue)}\"");
                    break;
            }
        }

        /// <summary>
        /// Injects default assertions when a test case has no custom assertions.
        /// Validates: valid JSON, required top-level keys, score range, and all 6 health categories.
        /// </summary>
        private static void BuildDefaultAssertions(StringBuilder sb)
        {
            sb.AppendLine("    assert:");

            // 1. Response must be valid JSON
            sb.AppendLine("      - type: is-json");

            // 2. Must contain report_metadata
            sb.AppendLine("      - type: javascript");
            sb.AppendLine("        value: |");
            sb.AppendLine("          const parsed = typeof output === 'string' ? JSON.parse(output) : output;");
            sb.AppendLine("          return parsed.report_metadata !== undefined || 'Missing report_metadata';");

            // 3. Must contain health_scores
            sb.AppendLine("      - type: javascript");
            sb.AppendLine("        value: |");
            sb.AppendLine("          const parsed = typeof output === 'string' ? JSON.parse(output) : output;");
            sb.AppendLine("          return parsed.health_scores !== undefined || 'Missing health_scores';");

            // 4. Must contain top_themes
            sb.AppendLine("      - type: javascript");
            sb.AppendLine("        value: |");
            sb.AppendLine("          const parsed = typeof output === 'string' ? JSON.parse(output) : output;");
            sb.AppendLine("          return parsed.top_themes !== undefined || 'Missing top_themes';");

            // 5. Must contain top_recommendations
            sb.AppendLine("      - type: javascript");
            sb.AppendLine("        value: |");
            sb.AppendLine("          const parsed = typeof output === 'string' ? JSON.parse(output) : output;");
            sb.AppendLine("          return parsed.top_recommendations !== undefined || 'Missing top_recommendations';");

            // 6. overall score must be a number >= 0 (supports both schema variants)
            sb.AppendLine("      - type: javascript");
            sb.AppendLine("        value: |");
            sb.AppendLine("          const parsed = typeof output === 'string' ? JSON.parse(output) : output;");
            sb.AppendLine("          const md = parsed.report_metadata || {};");
            sb.AppendLine("          const score = md.overall_corrected_score ?? md.overall_score;");
            sb.AppendLine("          return (typeof score === 'number' && score >= 0) || 'overall score must be a number >= 0, got: ' + score;");

            // 7. All 6 health categories must be present
            sb.AppendLine("      - type: javascript");
            sb.AppendLine("        value: |");
            sb.AppendLine("          const parsed = typeof output === 'string' ? JSON.parse(output) : output;");
            sb.AppendLine("          const required = ['Communication', 'Learning', 'Opportunities', 'Vulnerability', 'Enablement', 'Reflection'];");
            sb.AppendLine("          const hs = parsed.health_scores || {};");
            sb.AppendLine("          const missing = required.filter(k => hs[k] === undefined);");
            sb.AppendLine("          return missing.length === 0 || 'Missing health categories: ' + missing.join(', ');");
        }

        private static string BuildJsonPathJavascript(string? jsonPath, string? op, string? expectedValue)
        {
            // Build a JavaScript one-liner that:
            // 1. Parses the LLM output as JSON
            // 2. Navigates the JSON path
            // 3. Evaluates the operator against the expected value
            // 4. Returns true/false

            // Convert JSON path like "$.TopThemes" or "TopThemes[0]" to JS property access
            var path = (jsonPath ?? "").TrimStart('$', '.');
            var jsPath = string.Join("", path.Split('.').Select(seg =>
            {
                if (string.IsNullOrEmpty(seg)) return "";
                // Handle array index like "items[0]"
                return $"[\"{seg.Split('[')[0]}\"]" +
                    (seg.Contains('[') ? "[" + seg.Split('[')[1] : "");
            }));

            var safeExpected = (expectedValue ?? "").Replace("\\", "\\\\").Replace("'", "\\'");
            var oper = (op ?? "equals").ToLowerInvariant();

            // Build the JS expression
            // Use safe parse that handles output being either a string or already-parsed object
            const string safeParse = "const d = typeof output === 'string' ? JSON.parse(output) : output";
            var jsExpr = oper switch
            {
                "contains" => $"(() => {{ try {{ {safeParse}; const v = d{jsPath}; return typeof v === 'string' ? v.toLowerCase().includes('{safeExpected}'.toLowerCase()) : JSON.stringify(v).toLowerCase().includes('{safeExpected}'.toLowerCase()); }} catch(e) {{ return false; }} }})()",
                "not_contains" => $"(() => {{ try {{ {safeParse}; const v = d{jsPath}; return typeof v === 'string' ? !v.toLowerCase().includes('{safeExpected}'.toLowerCase()) : !JSON.stringify(v).toLowerCase().includes('{safeExpected}'.toLowerCase()); }} catch(e) {{ return false; }} }})()",
                "greater_than" => $"(() => {{ try {{ {safeParse}; return Number(d{jsPath}) > Number('{safeExpected}'); }} catch(e) {{ return false; }} }})()",
                "less_than" => $"(() => {{ try {{ {safeParse}; return Number(d{jsPath}) < Number('{safeExpected}'); }} catch(e) {{ return false; }} }})()",
                "exists" => $"(() => {{ try {{ {safeParse}; return d{jsPath} !== undefined && d{jsPath} !== null; }} catch(e) {{ return false; }} }})()",
                _ => $"(() => {{ try {{ {safeParse}; const v = d{jsPath}; return typeof v === 'string' ? v === '{safeExpected}' : JSON.stringify(v) === '{safeExpected}'; }} catch(e) {{ return false; }} }})()"
            };

            return jsExpr;
        }

        private (string command, string args) ResolvePromptfooCommand()
        {
            // 1. Check for bundled tools shipped with the app (CI/CD pipeline bundles these)
            var appDir = AppContext.BaseDirectory;
            _logger.LogInformation("[Promptfoo:Resolve] AppBaseDirectory={AppDir}", appDir);
            _logCollector.Add("INFO", $"[Promptfoo:Resolve] AppBaseDirectory={appDir}");

            var toolsDir = Path.Combine(appDir, "tools", "nodejs");
            var toolsDirExists = Directory.Exists(toolsDir);
            _logger.LogInformation("[Promptfoo:Resolve] ToolsDir={ToolsDir}, Exists={Exists}", toolsDir, toolsDirExists);
            _logCollector.Add("INFO", $"[Promptfoo:Resolve] ToolsDir={toolsDir}, Exists={toolsDirExists}");

            if (toolsDirExists)
            {
                try
                {
                    var files = Directory.GetFiles(toolsDir, "*.cmd");
                    _logger.LogInformation("[Promptfoo:Resolve] .cmd files in tools/nodejs: [{Files}]", string.Join(", ", files.Select(Path.GetFileName)));
                    _logCollector.Add("INFO", $"[Promptfoo:Resolve] .cmd files in tools/nodejs: [{string.Join(", ", files.Select(Path.GetFileName))}]");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Promptfoo:Resolve] Failed to list tools/nodejs directory");
                }
            }

            // Check tools/promptfoo/promptfoo.cmd first (separate install dir)
            var promptfooDir = Path.Combine(appDir, "tools", "promptfoo");
            var separatePromptfoo = Path.Combine(promptfooDir, "promptfoo.cmd");
            if (File.Exists(separatePromptfoo))
            {
                _logger.LogInformation("[Promptfoo:Resolve] FOUND promptfoo.cmd at {Path}", separatePromptfoo);
                _logCollector.Add("INFO", $"[Promptfoo:Resolve] FOUND promptfoo.cmd at {separatePromptfoo}");
                return (separatePromptfoo, "");
            }

            // Check tools/nodejs/promptfoo.cmd (shim pointing to separate dir)
            var bundledPromptfoo = Path.Combine(appDir, "tools", "nodejs", "promptfoo.cmd");
            if (File.Exists(bundledPromptfoo))
            {
                _logger.LogInformation("[Promptfoo:Resolve] FOUND bundled promptfoo.cmd at {Path}", bundledPromptfoo);
                _logCollector.Add("INFO", $"[Promptfoo:Resolve] FOUND bundled promptfoo.cmd at {bundledPromptfoo}");
                return (bundledPromptfoo, "");
            }
            _logger.LogWarning("[Promptfoo:Resolve] promptfoo.cmd NOT found at {Path}", bundledPromptfoo);
            _logCollector.Add("WARN", $"[Promptfoo:Resolve] promptfoo.cmd NOT found at {bundledPromptfoo}");

            var bundledNpx = Path.Combine(appDir, "tools", "nodejs", "npx.cmd");
            if (File.Exists(bundledNpx))
            {
                _logger.LogInformation("[Promptfoo:Resolve] FOUND bundled npx.cmd at {Path} — will use npx fallback", bundledNpx);
                _logCollector.Add("INFO", $"[Promptfoo:Resolve] FOUND bundled npx.cmd at {bundledNpx} — will use npx fallback");
                return (bundledNpx, "--yes promptfoo@0.103.0");
            }
            _logger.LogWarning("[Promptfoo:Resolve] npx.cmd NOT found at {Path}", bundledNpx);
            _logCollector.Add("WARN", $"[Promptfoo:Resolve] npx.cmd NOT found at {bundledNpx}");

            // 2. Check for local npm-installed promptfoo (avoids npx/node-gyp issues on Node v24)
            var localPromptfooMain = Path.Combine("D:", "Dev", "promptfoo-local", "node_modules", "promptfoo", "dist", "src", "main.js");
            if (File.Exists(localPromptfooMain))
            {
                _logger.LogInformation("[Promptfoo:Resolve] FOUND local promptfoo at {Path}", localPromptfooMain);
                _logCollector.Add("INFO", $"[Promptfoo:Resolve] FOUND local promptfoo at {localPromptfooMain}");
                return ("node", $"\"{localPromptfooMain}\"");
            }

            // 3. Fallback: local development (uses system PATH)
            _logger.LogWarning("[Promptfoo:Resolve] Using SYSTEM npx from PATH — no bundled tools found");
            _logCollector.Add("WARN", "[Promptfoo:Resolve] Using SYSTEM npx from PATH — no bundled tools found");
            return ("npx", "--yes promptfoo@0.103.0");
        }

        private async Task<PromptfooProcessResult> ExecutePromptfooAsync(
            string configPath, string outputPath, string workingDir, CancellationToken ct)
        {
            var execSw = Stopwatch.StartNew();
            _logger.LogInformation("[Promptfoo:Exec] ======= Starting ExecutePromptfooAsync =======");
            _logCollector.Add("INFO", "[Promptfoo:Exec] ======= Starting ExecutePromptfooAsync =======");

            var promptfooAuth = _configuration["Promptfoo:AuthorizationKey"];
            var sharingEnabled = _configuration.GetValue<bool>("Promptfoo:SharingEnabled");
            var promptfooHost = _configuration["Promptfoo:BaseUrl"] ?? "https://www.promptfoo.app";
            var shareFlag = sharingEnabled ? "--share" : "";
            _logger.LogInformation("[Promptfoo:Exec] SharingEnabled={Sharing}, PromptfooHost={Host}, HasApiKey={HasKey}",
                sharingEnabled, promptfooHost, !string.IsNullOrEmpty(promptfooAuth));
            _logCollector.Add("INFO", $"[Promptfoo:Exec] SharingEnabled={sharingEnabled}, PromptfooHost={promptfooHost}, HasApiKey={!string.IsNullOrEmpty(promptfooAuth)}");

            var (cmd, cmdArgs) = ResolvePromptfooCommand();

            var fullArguments = $"/c {cmd} {cmdArgs} eval --config \"{configPath}\" --output \"{outputPath}\" --no-cache --no-write {shareFlag}";
            _logger.LogInformation("[Promptfoo:Exec] Command: cmd.exe {Args}", fullArguments);
            _logCollector.Add("INFO", $"[Promptfoo:Exec] Command: cmd.exe {fullArguments}");
            _logger.LogInformation("[Promptfoo:Exec] WorkingDirectory: {WorkDir}", workingDir);
            _logCollector.Add("INFO", $"[Promptfoo:Exec] WorkingDirectory: {workingDir}");

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = fullArguments,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (!string.IsNullOrEmpty(promptfooAuth))
                startInfo.EnvironmentVariables["PROMPTFOO_API_KEY"] = promptfooAuth;

            startInfo.EnvironmentVariables["PROMPTFOO_HOST"] = promptfooHost;
            startInfo.EnvironmentVariables["PROMPTFOO_DISABLE_TELEMETRY"] = "1";
            startInfo.EnvironmentVariables["NO_COLOR"] = "1";
            startInfo.EnvironmentVariables["CI"] = "true";

            // Delete old promptfoo SQLite DB to avoid schema conflicts between versions
            // Promptfoo stores its DB in ~/.promptfoo/ — check all possible locations
            var possibleDbDirs = new[]
            {
                Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "C:\\home", ".promptfoo"),
                Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE") ?? "", ".promptfoo"),
                Path.Combine(Environment.GetEnvironmentVariable("APPDATA") ?? "", "promptfoo"),
                Path.Combine(Path.GetTempPath(), "promptfoo_config"),
            };
            foreach (var dbDir in possibleDbDirs)
            {
                try
                {
                    if (Directory.Exists(dbDir))
                    {
                        // Delete all .db files
                        foreach (var dbFile in Directory.GetFiles(dbDir, "*.db"))
                        {
                            File.Delete(dbFile);
                            _logCollector.Add("INFO", $"[Promptfoo:Exec] Deleted old DB: {dbFile}");
                        }
                    }
                }
                catch { }
            }

            // Point promptfoo to a clean config dir
            var promptfooConfigDir = Path.Combine(Path.GetTempPath(), "promptfoo_config");
            if (Directory.Exists(promptfooConfigDir))
            {
                try { Directory.Delete(promptfooConfigDir, recursive: true); } catch { }
            }
            Directory.CreateDirectory(promptfooConfigDir);
            startInfo.EnvironmentVariables["PROMPTFOO_CONFIG_DIR"] = promptfooConfigDir;
            _logCollector.Add("INFO", $"[Promptfoo:Exec] PROMPTFOO_CONFIG_DIR={promptfooConfigDir}");

            // Ensure bundled Node.js and promptfoo are in the PATH
            var toolsDir = Path.Combine(AppContext.BaseDirectory, "tools", "nodejs");
            var promptfooToolsDir = Path.Combine(AppContext.BaseDirectory, "tools", "promptfoo");
            var existingPath = startInfo.EnvironmentVariables["PATH"] ?? Environment.GetEnvironmentVariable("PATH") ?? "";
            var prependPaths = new List<string>();
            if (Directory.Exists(toolsDir)) prependPaths.Add(toolsDir);
            if (Directory.Exists(promptfooToolsDir)) prependPaths.Add(promptfooToolsDir);
            if (prependPaths.Any())
            {
                startInfo.EnvironmentVariables["PATH"] = $"{string.Join(";", prependPaths)};{existingPath}";
                _logger.LogInformation("[Promptfoo:Exec] Prepended to PATH: {Paths}", string.Join("; ", prependPaths));
                _logCollector.Add("INFO", $"[Promptfoo:Exec] Prepended to PATH: {string.Join("; ", prependPaths)}");
            }

            // Clear corrupted npx cache to avoid MODULE_NOT_FOUND errors
            try
            {
                var npxCacheDir = Path.Combine(
                    Environment.GetEnvironmentVariable("LocalAppData")
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
                    "npm-cache", "_npx");
                if (Directory.Exists(npxCacheDir))
                {
                    Directory.Delete(npxCacheDir, recursive: true);
                    _logCollector.Add("INFO", $"[Promptfoo:Exec] Cleared corrupted npx cache at {npxCacheDir}");
                }
            }
            catch (Exception ex)
            {
                _logCollector.Add("WARN", $"[Promptfoo:Exec] Failed to clear npx cache: {ex.Message}");
            }

            // ── Comprehensive diagnostics before running promptfoo ──
            _logCollector.Add("INFO", "[Diagnostic] ======= Starting environment diagnostics =======");
            try
            {
                // 1. List all files in tools/nodejs
                if (Directory.Exists(toolsDir))
                {
                    var allFiles = Directory.GetFiles(toolsDir, "*.*", SearchOption.TopDirectoryOnly);
                    var cmdFiles = allFiles.Where(f => f.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                                                     || f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToArray();
                    _logCollector.Add("INFO", $"[Diagnostic] tools/nodejs has {allFiles.Length} files total, executables: [{string.Join(", ", cmdFiles.Select(Path.GetFileName))}]");

                    // Check for node_modules (if npm install -g was done with --prefix)
                    var nodeModulesDir = Path.Combine(toolsDir, "node_modules");
                    if (Directory.Exists(nodeModulesDir))
                    {
                        var moduleDirs = Directory.GetDirectories(nodeModulesDir).Select(Path.GetFileName).ToArray();
                        _logCollector.Add("INFO", $"[Diagnostic] node_modules exists with {moduleDirs.Length} packages: [{string.Join(", ", moduleDirs.Take(20))}]");

                        var binDir = Path.Combine(nodeModulesDir, ".bin");
                        if (Directory.Exists(binDir))
                        {
                            var binFiles = Directory.GetFiles(binDir, "promptfoo*").Select(Path.GetFileName).ToArray();
                            _logCollector.Add("INFO", $"[Diagnostic] node_modules/.bin promptfoo files: [{string.Join(", ", binFiles)}]");
                        }
                    }
                    else
                    {
                        _logCollector.Add("WARN", "[Diagnostic] node_modules directory does NOT exist in tools/nodejs");
                    }
                }
                else
                {
                    _logCollector.Add("ERROR", $"[Diagnostic] tools/nodejs directory does NOT exist at {toolsDir}");
                }

                // 2. Check disk space in temp
                var tempDrive = Path.GetPathRoot(Path.GetTempPath());
                if (tempDrive != null)
                {
                    var driveInfo = new DriveInfo(tempDrive);
                    _logCollector.Add("INFO", $"[Diagnostic] Temp drive {tempDrive}: {driveInfo.AvailableFreeSpace / 1024 / 1024}MB free of {driveInfo.TotalSize / 1024 / 1024}MB");
                }

                // 3. Check npm cache location
                var npmCacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "npm-cache");
                _logCollector.Add("INFO", $"[Diagnostic] npm-cache dir exists: {Directory.Exists(npmCacheDir)}, path: {npmCacheDir}");

                // 4. Run quick diagnostic commands with 30s timeout each
                async Task<string> RunDiagnosticAsync(string fileName, string arguments, string label)
                {
                    try
                    {
                        var diagProc = Process.Start(new ProcessStartInfo
                        {
                            FileName = fileName,
                            Arguments = arguments,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WorkingDirectory = workingDir
                        });
                        if (diagProc == null) return $"{label}: failed to start";

                        // Set PATH for diagnostic processes too
                        using var diagTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        var stdout = await diagProc.StandardOutput.ReadToEndAsync();
                        var stderr = await diagProc.StandardError.ReadToEndAsync();
                        try { await diagProc.WaitForExitAsync(diagTimeout.Token); }
                        catch { try { diagProc.Kill(); } catch { } return $"{label}: TIMED OUT after 30s"; }

                        var result = $"{label}: exit={diagProc.ExitCode}, stdout=[{stdout.Trim().Take(500).ToArray().Length > 0}]{(stdout.Trim().Length > 200 ? stdout.Trim()[..200] + "..." : stdout.Trim())}, stderr=[{stderr.Trim()}]";
                        return result;
                    }
                    catch (Exception ex) { return $"{label}: EXCEPTION: {ex.Message}"; }
                }

                var nodeExe = Path.Combine(toolsDir, "node.exe");
                var npmCmd = Path.Combine(toolsDir, "npm.cmd");
                var npxCmd = Path.Combine(toolsDir, "npx.cmd");

                // node --version
                _logCollector.Add("INFO", await RunDiagnosticAsync(nodeExe, "--version", "node --version"));

                // npm --version
                _logCollector.Add("INFO", await RunDiagnosticAsync("cmd.exe", $"/c \"{npmCmd}\" --version", "npm --version"));

                // npx --version
                _logCollector.Add("INFO", await RunDiagnosticAsync("cmd.exe", $"/c \"{npxCmd}\" --version", "npx --version"));

                // npm config get prefix (where does -g install to?)
                _logCollector.Add("INFO", await RunDiagnosticAsync("cmd.exe", $"/c \"{npmCmd}\" config get prefix", "npm prefix"));

                // npm list -g promptfoo (is promptfoo installed globally?)
                _logCollector.Add("INFO", await RunDiagnosticAsync("cmd.exe", $"/c \"{npmCmd}\" list -g promptfoo --depth=0", "npm list promptfoo"));

                // npx which promptfoo (can npx find it?)
                _logCollector.Add("INFO", await RunDiagnosticAsync("cmd.exe", $"/c \"{npxCmd}\" --no-install promptfoo --version", "promptfoo --version (no-install)"));

                // 5. Environment info
                _logCollector.Add("INFO", $"[Diagnostic] HOME={Environment.GetEnvironmentVariable("HOME")}");
                _logCollector.Add("INFO", $"[Diagnostic] USERPROFILE={Environment.GetEnvironmentVariable("USERPROFILE")}");
                _logCollector.Add("INFO", $"[Diagnostic] APPDATA={Environment.GetEnvironmentVariable("APPDATA")}");
                _logCollector.Add("INFO", $"[Diagnostic] WEBSITE_INSTANCE_ID={Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID")}");
                _logCollector.Add("INFO", $"[Diagnostic] WEBSITE_SITE_NAME={Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME")}");

                // 6. PATH (first 5 entries)
                var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
                var pathEntries = pathVar.Split(';').Where(p => !string.IsNullOrWhiteSpace(p)).Take(10).ToArray();
                _logCollector.Add("INFO", $"[Diagnostic] PATH (first 10): [{string.Join("; ", pathEntries)}]");
            }
            catch (Exception ex)
            {
                _logCollector.Add("ERROR", $"[Diagnostic] Diagnostics failed: {ex.Message}");
            }
            _logCollector.Add("INFO", "[Diagnostic] ======= Diagnostics complete =======");

            _logger.LogInformation("[Promptfoo:Exec] Starting process...");
            _logCollector.Add("INFO", "[Promptfoo:Exec] Starting process...");
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start promptfoo process.");

            _logger.LogInformation("[Promptfoo:Exec] Process started: PID={PID} ({ElapsedMs}ms)", process.Id, execSw.ElapsedMilliseconds);
            _logCollector.Add("INFO", $"[Promptfoo:Exec] Process started: PID={process.Id} ({execSw.ElapsedMilliseconds}ms)");

            // Stream stdout/stderr line-by-line so we capture output even on timeout
            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    stdoutBuilder.AppendLine(e.Data);
                    _logCollector.Add("STDOUT", e.Data);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    stderrBuilder.AppendLine(e.Data);
                    _logCollector.Add("STDERR", e.Data);
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Use a linked token so we can cancel if the caller cancels
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(10));

            try
            {
                _logger.LogInformation("[Promptfoo:Exec] Waiting for process to exit (10 min timeout)...");
                _logCollector.Add("INFO", "[Promptfoo:Exec] Waiting for process to exit (10 min timeout)...");
                await process.WaitForExitAsync(timeoutCts.Token);
                _logger.LogInformation("[Promptfoo:Exec] Process exited with code {ExitCode} after {ElapsedMs}ms",
                    process.ExitCode, execSw.ElapsedMilliseconds);
                _logCollector.Add("INFO", $"[Promptfoo:Exec] Process exited with code {process.ExitCode} after {execSw.ElapsedMilliseconds}ms");
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("[Promptfoo:Exec] TIMEOUT/CANCELLED after {ElapsedMs}ms — killing process tree (PID={PID})",
                    execSw.ElapsedMilliseconds, process.Id);
                _logCollector.Add("ERROR", $"[Promptfoo:Exec] TIMEOUT/CANCELLED after {execSw.ElapsedMilliseconds}ms — killing process tree (PID={process.Id})");
                try { process.Kill(entireProcessTree: true); } catch { }

                var partialStdout = stdoutBuilder.ToString();
                var partialStderr = stderrBuilder.ToString();
                _logger.LogError("[Promptfoo:Exec] Partial Stdout before timeout:\n{Stdout}", partialStdout);
                _logCollector.Add("ERROR", $"[Promptfoo:Exec] Captured Stdout ({partialStdout.Length} chars):\n{partialStdout}");
                _logger.LogError("[Promptfoo:Exec] Partial Stderr before timeout:\n{Stderr}", partialStderr);
                _logCollector.Add("ERROR", $"[Promptfoo:Exec] Captured Stderr ({partialStderr.Length} chars):\n{partialStderr}");

                throw new OperationCanceledException("Promptfoo evaluation timed out or was cancelled.");
            }

            var stdout = stdoutBuilder.ToString();
            var stderr = stderrBuilder.ToString();

            _logger.LogInformation("[Promptfoo:Exec] Stdout ({StdoutLen} chars):\n{Stdout}", stdout.Length, stdout);
            _logCollector.Add("INFO", $"[Promptfoo:Exec] Stdout ({stdout.Length} chars):\n{stdout}");
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                _logger.LogWarning("[Promptfoo:Exec] Stderr ({StderrLen} chars):\n{Stderr}", stderr.Length, stderr);
                _logCollector.Add("WARN", $"[Promptfoo:Exec] Stderr ({stderr.Length} chars):\n{stderr}");
            }

            // Check if output file was created
            var outputExists = File.Exists(outputPath);
            _logger.LogInformation("[Promptfoo:Exec] Output file exists={Exists} at {OutputPath}", outputExists, outputPath);
            _logCollector.Add("INFO", $"[Promptfoo:Exec] Output file exists={outputExists} at {outputPath}");
            if (outputExists)
            {
                var fileInfo = new FileInfo(outputPath);
                _logger.LogInformation("[Promptfoo:Exec] Output file size: {Size} bytes", fileInfo.Length);
                _logCollector.Add("INFO", $"[Promptfoo:Exec] Output file size: {fileInfo.Length} bytes");
            }

            // Extract share/dashboard URLs from stdout if present
            string? shareUrl = null;
            if (!string.IsNullOrEmpty(stdout))
            {
                // Match any URLs in the output (share links, dashboard links, etc.)
                var urlMatches = Regex.Matches(stdout, @"https?://[^\s\]""']+");
                foreach (Match match in urlMatches)
                {
                    var url = match.Value.TrimEnd('.', ',', ')', ']');
                    if (url.Contains("promptfoo") || url.Contains("app.promptfoo"))
                    {
                        shareUrl = url;
                        _logger.LogInformation("[Promptfoo:Exec] Found share URL: {ShareUrl}", shareUrl);
                        _logCollector.Add("INFO", $"[Promptfoo:Exec] Found share URL: {shareUrl}");
                        break;
                    }
                }
            }

            _logger.LogInformation("[Promptfoo:Exec] ======= ExecutePromptfooAsync completed in {ElapsedMs}ms =======", execSw.ElapsedMilliseconds);
            _logCollector.Add("INFO", $"[Promptfoo:Exec] ======= ExecutePromptfooAsync completed in {execSw.ElapsedMilliseconds}ms =======");

            return new PromptfooProcessResult
            {
                ExitCode = process.ExitCode,
                Stdout = stdout,
                Stderr = stderr,
                ShareUrl = shareUrl
            };
        }

        private static PromptfooEvalResultDto ParsePromptfooResults(
            string outputJson, PromptTemplate prompt, string? shareUrl)
        {
            var result = new PromptfooEvalResultDto
            {
                PromptId = prompt.Id,
                PromptName = prompt.Name,
                ShareUrl = shareUrl
            };

            try
            {
                using var doc = JsonDocument.Parse(outputJson);
                var root = doc.RootElement;

                // ── Locate the results array ──
                // Promptfoo output can be structured in multiple ways:
                //   Format A: { "results": [ ... ] }                    — flat array
                //   Format B: { "results": { "results": [ ... ] } }     — nested object
                //   Format C: { "evalId": "...", "results": { ... } }   — with eval metadata
                JsonElement resultsArray = default;
                JsonElement statsElement = default;
                bool foundResults = false;

                if (root.TryGetProperty("results", out var resultsRaw))
                {
                    if (resultsRaw.ValueKind == JsonValueKind.Array)
                    {
                        // Format A: direct array
                        resultsArray = resultsRaw;
                        foundResults = true;
                        // Stats at root level
                        root.TryGetProperty("stats", out statsElement);
                    }
                    else if (resultsRaw.ValueKind == JsonValueKind.Object)
                    {
                        // Format B/C: nested object — look for results.results array
                        if (resultsRaw.TryGetProperty("results", out var innerResults)
                            && innerResults.ValueKind == JsonValueKind.Array)
                        {
                            resultsArray = innerResults;
                            foundResults = true;
                        }
                        // Stats inside the results object
                        resultsRaw.TryGetProperty("stats", out statsElement);

                        // Extract shareableUrl from nested results
                        if (resultsRaw.TryGetProperty("shareableUrl", out var nestedShareUrl))
                            result.DashboardUrl = nestedShareUrl.GetString();
                    }
                }

                // Also check root-level shareableUrl
                if (string.IsNullOrEmpty(result.DashboardUrl)
                    && root.TryGetProperty("shareableUrl", out var rootShareUrl))
                {
                    result.DashboardUrl = rootShareUrl.GetString();
                }

                if (!foundResults)
                {
                    // Could not find results — return raw output for debugging
                    result.TestResults.Add(new PromptfooTestResultDto
                    {
                        TestCaseName = "Parse Error",
                        Response = outputJson.Length > 5000 ? outputJson[..5000] + "..." : outputJson,
                        Error = "Could not locate results array in Promptfoo output."
                    });
                    result.Summary = new PromptfooEvalSummaryDto();
                    return result;
                }

                // ── Parse each test result ──
                foreach (var item in resultsArray.EnumerateArray())
                {
                    var testResult = new PromptfooTestResultDto();

                    // ── Test case description ──
                    if (item.TryGetProperty("description", out var desc))
                        testResult.TestCaseName = desc.GetString() ?? "";
                    if (string.IsNullOrEmpty(testResult.TestCaseName)
                        && item.TryGetProperty("testCase", out var tcEl)
                        && tcEl.TryGetProperty("description", out var tcDesc))
                        testResult.TestCaseName = tcDesc.GetString() ?? "";

                    // ── Provider ──
                    if (item.TryGetProperty("provider", out var providerEl))
                    {
                        if (providerEl.ValueKind == JsonValueKind.String)
                            testResult.ProviderName = providerEl.GetString() ?? "";
                        else if (providerEl.ValueKind == JsonValueKind.Object)
                        {
                            if (providerEl.TryGetProperty("label", out var label))
                                testResult.ProviderName = label.GetString() ?? "";
                            else if (providerEl.TryGetProperty("id", out var provId))
                                testResult.ProviderName = provId.GetString() ?? "";

                            // Extract actual model name from provider id (e.g. "openai:chat:grok-3" → "grok-3")
                            if (providerEl.TryGetProperty("id", out var modelId))
                            {
                                var idStr = modelId.GetString() ?? "";
                                var lastColon = idStr.LastIndexOf(':');
                                testResult.ModelName = lastColon >= 0 ? idStr[(lastColon + 1)..] : idStr;
                            }

                            // For HTTP providers the id-based model name is a URL, not useful.
                            // Extract the real model name from the label pattern "FriendlyName (modelName)".
                            if (testResult.ModelName.Contains('/') || testResult.ModelName == "http")
                            {
                                var lbl = testResult.ProviderName ?? "";
                                var openParen = lbl.LastIndexOf('(');
                                var closeParen = lbl.LastIndexOf(')');
                                if (openParen >= 0 && closeParen > openParen)
                                    testResult.ModelName = lbl[(openParen + 1)..closeParen].Trim();
                            }
                        }
                    }

                    // ── Full prompt output (LLM response) ──
                    if (item.TryGetProperty("response", out var responseEl)
                        && responseEl.ValueKind == JsonValueKind.Object)
                    {
                        if (responseEl.TryGetProperty("output", out var outputEl))
                        {
                            testResult.Response = outputEl.ValueKind == JsonValueKind.String
                                ? outputEl.GetString() ?? ""
                                : outputEl.GetRawText();
                        }

                        // Token usage from response object
                        if (responseEl.TryGetProperty("tokenUsage", out var tu)
                            && tu.ValueKind == JsonValueKind.Object)
                        {
                            if (tu.TryGetProperty("prompt", out var promptTk))
                                testResult.PromptTokens = SafeGetInt(promptTk);
                            if (tu.TryGetProperty("completion", out var compTk))
                                testResult.CompletionTokens = SafeGetInt(compTk);
                            if (tu.TryGetProperty("total", out var totalTk))
                                testResult.TotalTokens = SafeGetInt(totalTk);
                        }

                        // Cost from response object (if provider returns it)
                        if (responseEl.TryGetProperty("cost", out var costEl)
                            && costEl.ValueKind == JsonValueKind.Number)
                        {
                            testResult.EstimatedCost = (decimal)SafeGetDouble(costEl);
                        }
                    }

                    // Fallback: direct output field
                    if (string.IsNullOrEmpty(testResult.Response)
                        && item.TryGetProperty("output", out var directOutput))
                    {
                        testResult.Response = directOutput.ValueKind == JsonValueKind.String
                            ? directOutput.GetString() ?? ""
                            : directOutput.GetRawText();
                    }

                    // Normalize response to valid JSON (strips code fences, prose, etc.)
                    if (!string.IsNullOrWhiteSpace(testResult.Response))
                        testResult.Response = NormalizeToJson(testResult.Response);

                    // ── Rendered prompt (what was actually sent to LLM) ──
                    if (item.TryGetProperty("prompt", out var promptEl))
                    {
                        if (promptEl.TryGetProperty("raw", out var rawPrompt))
                            testResult.RenderedPrompt = rawPrompt.GetString() ?? "";
                        else if (promptEl.ValueKind == JsonValueKind.String)
                            testResult.RenderedPrompt = promptEl.GetString() ?? "";
                        else if (promptEl.TryGetProperty("label", out var promptLabel))
                            testResult.RenderedPrompt = promptLabel.GetString() ?? "";
                    }

                    // ── Variables used for this test ──
                    if (item.TryGetProperty("vars", out var varsEl)
                        && varsEl.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in varsEl.EnumerateObject())
                            testResult.Variables[prop.Name] = prop.Value.ToString();
                    }
                    else if (item.TryGetProperty("testCase", out var tcForVars)
                             && tcForVars.TryGetProperty("vars", out var tcVarsEl)
                             && tcVarsEl.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in tcVarsEl.EnumerateObject())
                            testResult.Variables[prop.Name] = prop.Value.ToString();
                    }

                    // ── Pass/Fail ──
                    if (item.TryGetProperty("success", out var success))
                        testResult.Pass = success.GetBoolean();

                    // ── Score ──
                    if (item.TryGetProperty("score", out var score))
                        testResult.Score = SafeGetDouble(score);

                    // ── Latency ──
                    if (item.TryGetProperty("latencyMs", out var latency))
                        testResult.LatencyMs = SafeGetLong(latency);

                    // ── Error ──
                    if (item.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                    {
                        testResult.Error = error.ValueKind == JsonValueKind.String
                            ? error.GetString()
                            : error.GetRawText();
                    }

                    // ── Assertions / grading results ──
                    if (item.TryGetProperty("gradingResult", out var grading)
                        && grading.ValueKind == JsonValueKind.Object)
                    {
                        // Overall grading pass/score (may differ from success)
                        if (grading.TryGetProperty("pass", out var gradingPass))
                            testResult.Pass = gradingPass.GetBoolean();
                        if (grading.TryGetProperty("score", out var gradingScore))
                            testResult.Score = SafeGetDouble(gradingScore);
                        if (grading.TryGetProperty("reason", out var gradingReason))
                        {
                            // Add overall grading reason if there's no error already
                            if (string.IsNullOrEmpty(testResult.Error) && !testResult.Pass)
                                testResult.Error = gradingReason.GetString();
                        }

                        if (grading.TryGetProperty("componentResults", out var components)
                            && components.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var comp in components.EnumerateArray())
                            {
                                var ar = new PromptfooAssertionResultDto();

                                if (comp.TryGetProperty("assertion", out var assertionEl)
                                    && assertionEl.ValueKind == JsonValueKind.Object)
                                {
                                    if (assertionEl.TryGetProperty("type", out var aType))
                                        ar.Type = aType.GetString() ?? "";
                                    if (assertionEl.TryGetProperty("value", out var aValue))
                                    {
                                        ar.Expected = aValue.ValueKind == JsonValueKind.String
                                            ? aValue.GetString()
                                            : aValue.GetRawText();
                                    }
                                }

                                if (comp.TryGetProperty("pass", out var aPass))
                                    ar.Pass = aPass.GetBoolean();
                                if (comp.TryGetProperty("score", out var aScore))
                                    ar.Score = SafeGetDouble(aScore);
                                if (comp.TryGetProperty("reason", out var aReason))
                                    ar.Reason = aReason.GetString();
                                if (comp.TryGetProperty("actual", out var aActual)
                                    && aActual.ValueKind != JsonValueKind.Null)
                                {
                                    ar.Actual = aActual.ValueKind == JsonValueKind.String
                                        ? aActual.GetString()
                                        : aActual.GetRawText();
                                }

                                testResult.Assertions.Add(ar);
                            }
                        }
                    }

                    result.TestResults.Add(testResult);
                }

                // ── Build summary ──
                var totalTests = result.TestResults.Count;
                var totalPassed = result.TestResults.Count(r => r.Pass);
                var totalErrors = result.TestResults.Count(r => !string.IsNullOrEmpty(r.Error));
                result.Summary = new PromptfooEvalSummaryDto
                {
                    TotalTests = totalTests,
                    TotalPassed = totalPassed,
                    TotalFailed = totalTests - totalPassed,
                    TotalErrors = totalErrors,
                    PassRate = totalTests > 0 ? Math.Round((double)totalPassed / totalTests * 100, 2) : 0
                };

                // Override from Promptfoo stats if available
                if (statsElement.ValueKind == JsonValueKind.Object)
                {
                    if (statsElement.TryGetProperty("successes", out var successes))
                        result.Summary.TotalPassed = SafeGetInt(successes);
                    if (statsElement.TryGetProperty("failures", out var failures))
                        result.Summary.TotalFailed = SafeGetInt(failures);
                }
            }
            catch (JsonException)
            {
                result.TestResults.Add(new PromptfooTestResultDto
                {
                    TestCaseName = "Raw Output",
                    Response = outputJson.Length > 5000 ? outputJson[..5000] + "..." : outputJson,
                    Error = "Failed to parse Promptfoo JSON output."
                });
                result.Summary = new PromptfooEvalSummaryDto();
            }

            return result;
        }

        private static int SafeGetInt(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Number) return el.GetInt32();
            if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var v)) return v;
            return 0;
        }

        private static long SafeGetLong(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Number) return el.GetInt64();
            if (el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), out var v)) return v;
            return 0;
        }

        private static double SafeGetDouble(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Number) return el.GetDouble();
            if (el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(), out var v)) return v;
            return 0;
        }

        private static string EscapeYaml(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "");
        }

        private static string EscapeYamlMultiline(string value)
        {
            return value.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private class PromptfooProcessResult
        {
            public int ExitCode { get; set; }
            public string Stdout { get; set; } = "";
            public string Stderr { get; set; } = "";
            public string? ShareUrl { get; set; }
        }

        private async Task<List<InlineTestCaseDto>> ResolveTestCasesAsync(
            WorkbenchRunRequestDto request, IFormFile? testCaseFile, CancellationToken ct)
        {
            // File takes priority
            if (testCaseFile != null && testCaseFile.Length > 0)
            {
                var ext = Path.GetExtension(testCaseFile.FileName).ToLowerInvariant();
                return ext switch
                {
                    ".csv" => await ParseTestCasesFromCsvAsync(testCaseFile, ct),
                    ".xlsx" => await ParseTestCasesFromExcelAsync(testCaseFile, ct),
                    _ => throw new KeyNotFoundException($"Unsupported file type: {ext}. Use .xlsx or .csv.")
                };
            }

            if (!string.IsNullOrWhiteSpace(request.ManualTestCaseJson))
            {
                return JsonSerializer.Deserialize<List<InlineTestCaseDto>>(
                    request.ManualTestCaseJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new();
            }

            return new();
        }

        private static async Task<List<InlineTestCaseDto>> ParseTestCasesFromExcelAsync(IFormFile file, CancellationToken ct)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct);
            stream.Position = 0;

            using var package = new ExcelPackage(stream);

            // ── Sheet 1: TestCases ──
            var testCaseSheet = package.Workbook.Worksheets.FirstOrDefault(ws =>
                ws.Name.Equals("TestCases", StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException("Excel file must contain a 'TestCases' sheet.");

            var testCases = new Dictionary<string, InlineTestCaseDto>(StringComparer.OrdinalIgnoreCase);
            var rowCount = testCaseSheet.Dimension?.Rows ?? 0;

            for (int row = 2; row <= rowCount; row++) // skip header
            {
                var name = testCaseSheet.Cells[row, 1].Text?.Trim();
                var inputDataJson = testCaseSheet.Cells[row, 2].Text?.Trim();

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                testCases[name] = new InlineTestCaseDto
                {
                    Name = name,
                    InputDataJson = inputDataJson ?? "{}"
                };
            }

            // ── Sheet 2: Assertions (optional) ──
            var assertionSheet = package.Workbook.Worksheets.FirstOrDefault(ws =>
                ws.Name.Equals("Assertions", StringComparison.OrdinalIgnoreCase));

            if (assertionSheet != null)
            {
                var assertionRowCount = assertionSheet.Dimension?.Rows ?? 0;

                for (int row = 2; row <= assertionRowCount; row++) // skip header
                {
                    var testCaseName = assertionSheet.Cells[row, 1].Text?.Trim();
                    if (string.IsNullOrWhiteSpace(testCaseName))
                        continue;

                    if (!testCases.TryGetValue(testCaseName, out var tc))
                        continue; // skip assertions for unknown test cases

                    tc.Assertions.Add(new InlineAssertionDto
                    {
                        Name = assertionSheet.Cells[row, 2].Text?.Trim() ?? "",
                        AssertionType = assertionSheet.Cells[row, 3].Text?.Trim() ?? "",
                        JsonPath = assertionSheet.Cells[row, 4].Text?.Trim(),
                        Operator = assertionSheet.Cells[row, 5].Text?.Trim(),
                        ExpectedValue = assertionSheet.Cells[row, 6].Text?.Trim()
                    });
                }
            }

            return testCases.Values.ToList();
        }

        private static async Task<List<InlineTestCaseDto>> ParseTestCasesFromCsvAsync(IFormFile file, CancellationToken ct)
        {
            string csvContent;
            using (var stream = file.OpenReadStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                csvContent = await reader.ReadToEndAsync();
            }

            if (string.IsNullOrWhiteSpace(csvContent))
                throw new KeyNotFoundException("CSV file is empty.");

            // Remove BOM if present
            csvContent = csvContent.TrimStart('\uFEFF');

            var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
                throw new KeyNotFoundException("CSV file must have a header row and at least one data row.");

            // Parse header to find column indices
            var header = ParseCsvLine(lines[0]);
            var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Length; i++)
            {
                var colName = header[i].Trim();
                if (!string.IsNullOrWhiteSpace(colName))
                    colMap[colName] = i;
            }

            if (!colMap.Any())
                throw new KeyNotFoundException("CSV file has no valid columns in the header row.");

            // Determine the CSV structure and group rows into test cases
            var hasCategoryColumn = colMap.ContainsKey("Category");

            if (hasCategoryColumn)
            {
                // Feedback CSV: group rows by Category, format as feedbackData
                return ParseFeedbackCsv(lines, header, colMap);
            }
            else
            {
                // Generic CSV: each row becomes a test case, all columns become variables
                return ParseGenericCsv(lines, header, colMap);
            }
        }

        private static List<InlineTestCaseDto> ParseFeedbackCsv(
            string[] lines, string[] header, Dictionary<string, int> colMap)
        {
            // Group rows by Category → each category becomes one test case
            // with feedbackData as the CSV rows
            var categoryGroups = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);
            var headerLine = string.Join(",", header.Select(h => h.Trim()));

            var hasBusinessUnitName = colMap.ContainsKey("BusinessUnitName");
            var hasCompanyName = colMap.ContainsKey("CompanyName");

            for (int i = 1; i < lines.Length; i++)
            {
                var fields = ParseCsvLine(lines[i]);
                if (fields.Length == 0) continue;

                var category = colMap.ContainsKey("Category") && fields.Length > colMap["Category"]
                    ? fields[colMap["Category"]].Trim()
                    : "General";

                if (string.IsNullOrWhiteSpace(category))
                    category = "General";

                if (!categoryGroups.ContainsKey(category))
                    categoryGroups[category] = new List<string[]>();

                categoryGroups[category].Add(fields);
            }

            var testCases = new List<InlineTestCaseDto>();

            foreach (var (category, rows) in categoryGroups)
            {
                // Build the feedbackData as formatted CSV text (header + rows for this category)
                var feedbackSb = new StringBuilder();
                feedbackSb.AppendLine(headerLine);
                foreach (var row in rows)
                {
                    feedbackSb.AppendLine(string.Join(",", row.Select(f => f.Contains(',') ? $"\"{f}\"" : f)));
                }

                // Build InputDataJson with the variables the prompt template expects
                var inputData = new Dictionary<string, string>();

                // New format: use explicit BusinessUnitName column; Old format: fall back to category
                if (hasBusinessUnitName)
                {
                    var buIdx = colMap["BusinessUnitName"];
                    var buName = rows.FirstOrDefault(r => r.Length > buIdx)?[buIdx]?.Trim();
                    inputData["businessUnitName"] = !string.IsNullOrWhiteSpace(buName) ? buName : category;
                }
                else
                {
                    inputData["businessUnitName"] = category;
                }

                // Include companyName if the column exists (new format)
                if (hasCompanyName)
                {
                    var cnIdx = colMap["CompanyName"];
                    var companyName = rows.FirstOrDefault(r => r.Length > cnIdx)?[cnIdx]?.Trim();
                    if (!string.IsNullOrWhiteSpace(companyName))
                        inputData["companyName"] = companyName;
                }

                inputData["feedbackData"] = feedbackSb.ToString().TrimEnd();

                testCases.Add(new InlineTestCaseDto
                {
                    Name = $"{category} Feedback",
                    InputDataJson = JsonSerializer.Serialize(inputData)
                });
            }

            return testCases;
        }

        private static List<InlineTestCaseDto> ParseGenericCsv(
            string[] lines, string[] header, Dictionary<string, int> colMap)
        {
            // Each row becomes a separate test case
            // Column headers become variable names
            var testCases = new List<InlineTestCaseDto>();

            for (int i = 1; i < lines.Length; i++)
            {
                var fields = ParseCsvLine(lines[i]);
                if (fields.Length == 0) continue;

                var variables = new Dictionary<string, string>();
                for (int j = 0; j < header.Length && j < fields.Length; j++)
                {
                    var colName = header[j].Trim();
                    if (!string.IsNullOrWhiteSpace(colName))
                        variables[colName] = fields[j].Trim();
                }

                testCases.Add(new InlineTestCaseDto
                {
                    Name = $"Test Case {i}",
                    InputDataJson = JsonSerializer.Serialize(variables)
                });
            }

            return testCases;
        }

        private static string[] ParseCsvLine(string line)
        {
            // Simple CSV parser that handles quoted fields with commas
            var fields = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        // Escaped quote
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else if (c == '\r')
                {
                    // Skip carriage return
                }
                else
                {
                    current.Append(c);
                }
            }

            fields.Add(current.ToString());
            return fields.ToArray();
        }

        private static List<WorkbenchAssertionResultDto> EvaluateInlineAssertions(
            List<InlineAssertionDto> assertions, string response)
        {
            var results = new List<WorkbenchAssertionResultDto>();

            foreach (var a in assertions)
            {
                var result = new WorkbenchAssertionResultDto
                {
                    AssertionId = 0,
                    Name = a.Name,
                    AssertionType = a.AssertionType,
                    ExpectedValue = a.ExpectedValue
                };

                switch (a.AssertionType.ToLowerInvariant())
                {
                    case "contains":
                        result.ActualValue = response;
                        result.Passed = response.Contains(a.ExpectedValue ?? "", StringComparison.OrdinalIgnoreCase);
                        break;

                    case "not_contains":
                        result.ActualValue = response;
                        result.Passed = !response.Contains(a.ExpectedValue ?? "", StringComparison.OrdinalIgnoreCase);
                        break;

                    case "json_path":
                        try
                        {
                            using var doc = JsonDocument.Parse(response);
                            var token = ExtractJsonPath(doc.RootElement, a.JsonPath ?? "");
                            result.ActualValue = token;
                            result.Passed = EvaluateOperator(token, a.Operator, a.ExpectedValue);
                        }
                        catch
                        {
                            result.ActualValue = null;
                            result.Passed = false;
                        }
                        break;

                    default:
                        result.Passed = false;
                        result.ActualValue = $"Unknown assertion type: {a.AssertionType}";
                        break;
                }

                results.Add(result);
            }

            return results;
        }

        private async Task<(string Content, int PromptTokens, int CompletionTokens, int TotalTokens)> CallLlmAsync(
            string systemPrompt, string userContent, ModelConfiguration model,
            double temperature, int maxTokens, double topP, CancellationToken ct)
        {
            // Enforce English language in all LLM responses
            var effectiveSystemPrompt = string.IsNullOrWhiteSpace(systemPrompt)
                ? "You must always respond in English only."
                : $"{systemPrompt}\n\nIMPORTANT: You must always respond in English only.";

            var endpoint = (model.Endpoint ?? "").Trim();
            var isAnthropic = endpoint.Contains("/anthropic/", StringComparison.OrdinalIgnoreCase);

            if (isAnthropic)
            {
                return await CallAnthropicLlmAsync(effectiveSystemPrompt, userContent, model, endpoint, temperature, maxTokens, topP, ct);
            }

            var messages = new List<object>
            {
                new { role = "system", content = effectiveSystemPrompt },
                new { role = "user", content = userContent }
            };

            var isAIFoundryServerless = endpoint.Contains("/models/chat/completions", StringComparison.OrdinalIgnoreCase)
                || (endpoint.Contains(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase)
                    && endpoint.Contains("/models/", StringComparison.OrdinalIgnoreCase));

            var body = isAIFoundryServerless
                ? JsonSerializer.Serialize(new
                {
                    model = model.ModelName?.Trim(),
                    messages,
                    max_tokens = maxTokens,
                    temperature,
                    top_p = topP
                })
                : JsonSerializer.Serialize(new
                {
                    messages,
                    max_tokens = maxTokens,
                    temperature,
                    top_p = topP
                });

            using var client = _httpFactory.CreateClient();
            client.DefaultRequestHeaders.Add("api-key", model.ApiKey);

            using var res = await client.PostAsync(
                endpoint,
                new StringContent(body, Encoding.UTF8, "application/json"),
                ct);

            var responseText = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"LLM call failed ({res.StatusCode}): {responseText}");

            using var doc = JsonDocument.Parse(responseText);

            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";

            int promptTokens = 0, completionTokens = 0, totalTokens = 0;
            if (doc.RootElement.TryGetProperty("usage", out var usage))
            {
                promptTokens = usage.GetProperty("prompt_tokens").GetInt32();
                completionTokens = usage.GetProperty("completion_tokens").GetInt32();
                totalTokens = usage.GetProperty("total_tokens").GetInt32();
            }

            return (content, promptTokens, completionTokens, totalTokens);
        }

        private async Task<(string Content, int PromptTokens, int CompletionTokens, int TotalTokens)> CallAnthropicLlmAsync(
            string systemPrompt, string userContent, ModelConfiguration model, string endpoint,
            double temperature, int maxTokens, double topP, CancellationToken ct)
        {
            // Anthropic Messages API format:
            // - system is a top-level field, not a message
            // - messages only contain user/assistant roles
            var messages = new List<object>
            {
                new { role = "user", content = userContent }
            };

            // Claude does not allow both temperature and top_p simultaneously.
            // Use temperature by default; only use top_p if temperature is at default (1.0).
            var bodyDict = new Dictionary<string, object?>
            {
                ["model"] = model.ModelName?.Trim(),
                ["system"] = systemPrompt,
                ["messages"] = messages,
                ["max_tokens"] = maxTokens
            };

            if (topP < 1.0)
                bodyDict["top_p"] = topP;
            else
                bodyDict["temperature"] = temperature;

            var body = JsonSerializer.Serialize(bodyDict);

            using var client = _httpFactory.CreateClient();
            // Azure AI Foundry Anthropic endpoints use x-api-key (not api-key)
            client.DefaultRequestHeaders.Add("x-api-key", model.ApiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            using var res = await client.PostAsync(
                endpoint,
                new StringContent(body, Encoding.UTF8, "application/json"),
                ct);

            var responseText = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"LLM call failed ({res.StatusCode}): {responseText}");

            using var doc = JsonDocument.Parse(responseText);

            // Anthropic response format: content[0].text
            var content = "";
            if (doc.RootElement.TryGetProperty("content", out var contentArray)
                && contentArray.GetArrayLength() > 0)
            {
                content = contentArray[0].GetProperty("text").GetString() ?? "";
            }

            int promptTokens = 0, completionTokens = 0, totalTokens = 0;
            if (doc.RootElement.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("input_tokens", out var inp))
                    promptTokens = inp.GetInt32();
                if (usage.TryGetProperty("output_tokens", out var outp))
                    completionTokens = outp.GetInt32();
                totalTokens = promptTokens + completionTokens;
            }

            return (content, promptTokens, completionTokens, totalTokens);
        }

        private static List<WorkbenchAssertionResultDto> EvaluateAssertions(List<TestCaseAssertion> assertions, string response)
        {
            var results = new List<WorkbenchAssertionResultDto>();

            foreach (var a in assertions)
            {
                var result = new WorkbenchAssertionResultDto
                {
                    AssertionId = a.Id,
                    Name = a.Name,
                    AssertionType = a.AssertionType,
                    ExpectedValue = a.ExpectedValue
                };

                switch (a.AssertionType.ToLowerInvariant())
                {
                    case "contains":
                        result.ActualValue = response;
                        result.Passed = response.Contains(a.ExpectedValue ?? "", StringComparison.OrdinalIgnoreCase);
                        break;

                    case "not_contains":
                        result.ActualValue = response;
                        result.Passed = !response.Contains(a.ExpectedValue ?? "", StringComparison.OrdinalIgnoreCase);
                        break;

                    case "json_path":
                        try
                        {
                            using var doc = JsonDocument.Parse(response);
                            var token = ExtractJsonPath(doc.RootElement, a.JsonPath ?? "");
                            result.ActualValue = token;
                            result.Passed = EvaluateOperator(token, a.Operator, a.ExpectedValue);
                        }
                        catch
                        {
                            result.ActualValue = null;
                            result.Passed = false;
                        }
                        break;

                    default:
                        result.Passed = false;
                        result.ActualValue = $"Unknown assertion type: {a.AssertionType}";
                        break;
                }

                results.Add(result);
            }

            return results;
        }

        private static string? ExtractJsonPath(JsonElement root, string path)
        {
            var current = root;
            foreach (var segment in path.TrimStart('$', '.').Split('.'))
            {
                if (string.IsNullOrEmpty(segment)) continue;

                if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(segment, out var next))
                    current = next;
                else
                    return null;
            }
            return current.ToString();
        }

        private static bool EvaluateOperator(string? actual, string? op, string? expected)
        {
            if (actual == null) return false;

            return (op?.ToLowerInvariant()) switch
            {
                "equals" => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                "contains" => actual.Contains(expected ?? "", StringComparison.OrdinalIgnoreCase),
                "greater_than" => double.TryParse(actual, out var a) && double.TryParse(expected, out var b) && a > b,
                "less_than" => double.TryParse(actual, out var a2) && double.TryParse(expected, out var b2) && a2 < b2,
                _ => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
            };
        }

        // ── Mappers ──

        private static PromptTemplateResponseDto MapToDto(PromptTemplate p)
        {
            var charCount = (p.Template?.Length ?? 0) + (p.SystemPrompt?.Length ?? 0);
            return new PromptTemplateResponseDto
            {
                Id = p.Id,
                Name = p.Name,
                Version = p.Version,
                Description = p.Description,
                SystemPrompt = p.SystemPrompt,
                Template = p.Template,
                IsActive = p.IsActive,
                CreatedAt = p.CreatedAt,
                CreatedBy = p.CreatedBy,
                MetadataJson = p.MetadataJson,
                CharCount = charCount,
                EstimatedTokenCount = charCount / 4
            };
        }

        private static TestCaseResponseDto MapTestCaseToDto(TestCase tc, int assertionCount) => new()
        {
            Id = tc.Id,
            Name = tc.Name,
            PromptTemplateName = tc.PromptTemplateName,
            InputDataJson = tc.InputDataJson,
            AssertionCount = assertionCount,
            CreatedAt = tc.CreatedAt,
            CreatedBy = tc.CreatedBy,
            ModifiedAt = tc.ModifiedAt
        };
    }
}