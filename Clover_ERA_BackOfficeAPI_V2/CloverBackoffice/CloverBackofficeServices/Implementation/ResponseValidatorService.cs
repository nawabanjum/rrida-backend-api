using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CloverBackoffice.Services.Implementation
{
    public class ResponseValidatorService : IResponseValidatorService
    {
        // Default ratio when SystemPrompt specifies none
        private const int DefaultThemes = 4;
        private const int DefaultRecommendations = 3;
        private const int DefaultActionSteps = 3;

        public ValidationResultDto Validate(string rawJson)
            => Validate(rawJson, systemPrompt: null);

        public ValidationResultDto Validate(string rawJson, string? systemPrompt)
        {
            var (themes, recommendations, actionSteps) = ParseRatioFromSystemPrompt(systemPrompt);

            var result = new ValidationResultDto { Total = 7 };

            // Assertion 1: is-json
            JsonDocument? doc;
            try { doc = JsonDocument.Parse(rawJson); AddPass(result, "is-json"); }
            catch { AddFail(result, "is-json", "Invalid JSON"); result.IsValid = false; return result; }

            var root = doc.RootElement;

            // Assertion 2: has-required-keys
            // Support both PascalCase (TopThemes) and snake_case (top_themes) key conventions
            var themesKey = root.TryGetProperty("TopThemes", out _) ? "TopThemes"
                         : root.TryGetProperty("top_themes", out _) ? "top_themes" : null;
            var recsKey = root.TryGetProperty("TopRecommendations", out _) ? "TopRecommendations"
                        : root.TryGetProperty("top_recommendations", out _) ? "top_recommendations" : null;
            var actionsKey = root.TryGetProperty("TopActions", out _) ? "TopActions"
                           : root.TryGetProperty("action_steps", out _) ? "action_steps"
                           : root.TryGetProperty("top_actions", out _) ? "top_actions" : null;

            var missingKeys = new List<string>();
            if (themesKey == null) missingKeys.Add("TopThemes/top_themes");
            if (recsKey == null) missingKeys.Add("TopRecommendations/top_recommendations");
            if (actionsKey == null) missingKeys.Add("TopActions/action_steps");

            if (missingKeys.Count == 0) AddPass(result, "has-required-keys");
            else AddFail(result, "has-required-keys", $"Missing: {string.Join(", ", missingKeys)}");

            // Assertion 3: themes-quality (exact count from ratio, each > 50 chars)
            CheckArrayQuality(result, root, themesKey ?? "TopThemes", "themes-quality", themes, 50);

            // Assertion 4: recommendations-quality (exact count from ratio, each > 30 chars)
            CheckArrayQuality(result, root, recsKey ?? "TopRecommendations", "recommendations-quality", recommendations, 30);

            // Assertion 5: actions-quality (exact count from ratio, each > 30 chars)
            CheckArrayQuality(result, root, actionsKey ?? "TopActions", "actions-quality", actionSteps, 30);

            // Assertion 6: worksheet-filename-pattern (Worksheet [CELOPVR]\d+.pdf)
            if (root.TryGetProperty("SuggestedWorksheet", out var ws) &&
                ws.TryGetProperty("FileName", out var fn))
            {
                var pattern = @"^Worksheet [CELOPVR]\d+\.pdf$";
                if (Regex.IsMatch(fn.GetString() ?? "", pattern))
                    AddPass(result, "worksheet-filename");
                else
                    AddFail(result, "worksheet-filename", $"Got: {fn.GetString()}");
            }
            else AddFail(result, "worksheet-filename", "SuggestedWorksheet.FileName missing");

            // Assertion 7: no-pii-leakage
            var fullText = rawJson.ToLowerInvariant();
            var emailPattern = @"[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,}";
            if (Regex.IsMatch(fullText, emailPattern))
                AddFail(result, "no-pii", "Email address detected in output");
            else
                AddPass(result, "no-pii");

            result.IsValid = result.Passed == result.Total;
            return result;
        }

        private static void CheckArrayQuality(
            ValidationResultDto result, JsonElement root,
            string propertyName, string assertionName,
            int exactCount, int minLength)
        {
            if (!root.TryGetProperty(propertyName, out var arr) ||
                arr.ValueKind != JsonValueKind.Array)
            {
                AddFail(result, assertionName, $"{propertyName} is missing or not an array");
                return;
            }

            // Extract text from each array element — supports both plain strings
            // and objects (uses "text" property, or serializes the whole object)
            var items = arr.EnumerateArray()
                .Select(x => x.ValueKind == JsonValueKind.String
                    ? x.GetString() ?? ""
                    : x.ValueKind == JsonValueKind.Object && x.TryGetProperty("text", out var txt)
                        ? txt.GetString() ?? ""
                        : x.GetRawText())
                .ToList();

            if (items.Count != exactCount)
            {
                AddFail(result, assertionName, $"Expected exactly {exactCount} items, got {items.Count}");
                return;
            }

            var tooShort = items.Where(x => x.Length < minLength).ToList();
            if (tooShort.Count > 0)
            {
                AddFail(result, assertionName, $"{tooShort.Count} item(s) shorter than {minLength} chars");
                return;
            }

            AddPass(result, assertionName);
        }

        private static void AddPass(ValidationResultDto result, string name)
        {
            result.Passed++;
            result.Assertions.Add(new AssertionResultDto { Name = name, Passed = true });
        }

        private static void AddFail(ValidationResultDto result, string name, string reason)
        {
            result.Assertions.Add(new AssertionResultDto { Name = name, Passed = false, FailReason = reason });
        }

        /// <summary>
        /// Extracts ratio from the SystemPrompt column of PromptTemplates.
        /// Looks for patterns like "exactly 4 themes", "3 recommendations",
        /// "3 action steps", "recommendation_count default 2".
        /// Returns defaults (4/3/3) when no ratio is found.
        /// </summary>
        public static (int Themes, int Recommendations, int ActionSteps) ParseRatioFromSystemPrompt(string? systemPrompt)
        {
            if (string.IsNullOrWhiteSpace(systemPrompt))
                return (DefaultThemes, DefaultRecommendations, DefaultActionSteps);

            int themes = DefaultThemes;
            int recommendations = DefaultRecommendations;
            int actionSteps = DefaultActionSteps;

            var themesMatch = Regex.Match(systemPrompt, @"(\d+)\s*themes", RegexOptions.IgnoreCase);
            if (themesMatch.Success)
                themes = int.Parse(themesMatch.Groups[1].Value);

            var recsMatch = Regex.Match(systemPrompt, @"(\d+)\s*recommendations", RegexOptions.IgnoreCase);
            if (recsMatch.Success)
                recommendations = int.Parse(recsMatch.Groups[1].Value);

            // Also check for "default N" pattern near recommendation_count
            if (!recsMatch.Success)
            {
                var defaultRecsMatch = Regex.Match(systemPrompt, @"recommendation_count.*?default\s+(\d+)", RegexOptions.IgnoreCase);
                if (defaultRecsMatch.Success)
                    recommendations = int.Parse(defaultRecsMatch.Groups[1].Value);
            }

            var actionsMatch = Regex.Match(systemPrompt, @"(\d+)\s*(?:action\s*steps|actions|top\s*actions)", RegexOptions.IgnoreCase);
            if (actionsMatch.Success)
                actionSteps = int.Parse(actionsMatch.Groups[1].Value);

            return (themes, recommendations, actionSteps);
        }
    }
}
