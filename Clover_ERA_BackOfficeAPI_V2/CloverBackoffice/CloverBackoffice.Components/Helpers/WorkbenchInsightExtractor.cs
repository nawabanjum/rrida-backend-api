using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Components.Helpers
{
    // Turns a workbench run's test responses into the three insight lists the
    // product stores (TopThemes / TopRecommendations / TopActions).
    //
    // This lived as a private static in PromptManagementController, which meant
    // the scheduler could not use it and grew its own flattening instead — the
    // two paths then produced subtly different text for the same engine output.
    // It is shared from here so a report reads the same however its run was
    // triggered; the controller and GuardShortageJob both call it.
    public static class WorkbenchInsightExtractor
    {
        // Picks the single best model group out of an eval result — highest
        // average score, pass rate as tie-break — the same choice the workbench
        // output page presents as "the" result.
        public static List<PromptfooTestResultDto> SelectBestModelTests(PromptfooEvalResultDto? evalResult)
        {
            var allTests = (evalResult?.TestResults ?? new List<PromptfooTestResultDto>())
                .Where(t => t != null)
                .ToList();

            if (allTests.Count == 0)
                return new List<PromptfooTestResultDto>();

            return allTests
                .GroupBy(t => $"{t.ProviderName}|{t.ModelName}")
                .Select(g =>
                {
                    var tests = g.ToList();
                    return new
                    {
                        Tests = tests,
                        AvgScore = tests.Average(t => t.Score),
                        PassRate = tests.Average(t => t.Pass ? 1.0 : 0.0)
                    };
                })
                .OrderByDescending(g => g.AvgScore)
                .ThenByDescending(g => g.PassRate)
                .First()
                .Tests;
        }

        // Convenience for callers holding the raw WorkbenchRunStatus.ResultJson
        // wrapper: parse it, pick the best model, extract.
        public static (List<string> TopThemes, List<string> TopRecommendations, List<string> TopActions)
            ExtractFromResultJson(string? resultJson)
        {
            if (string.IsNullOrWhiteSpace(resultJson))
                return (new List<string>(), new List<string>(), new List<string>());

            var evalResult = WorksheetMatcher.ExtractEvalResultFromResultJson(resultJson);
            return Extract(SelectBestModelTests(evalResult));
        }

        // Uses the FIRST successfully-parsed test response only, and stops there.
        // Aggregating across all test cases produces inflated counts (e.g. 12
        // themes instead of 4), because each test case is a separate evaluation
        // of the same prompt over different rows — every response already
        // carries the full insight set. This is the rule the workbench output
        // page has always used; keep the two in step.
        public static (List<string> TopThemes, List<string> TopRecommendations, List<string> TopActions) Extract(
            List<PromptfooTestResultDto> tests)
        {
            foreach (var test in tests ?? new List<PromptfooTestResultDto>())
            {
                if (string.IsNullOrWhiteSpace(test?.Response)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(test.Response);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) continue;

                    var themes          = new List<string>();
                    var recommendations = new List<string>();
                    var actions         = new List<string>();

                    // top_themes — supports [{ "text": "..." }] and ["string"] formats
                    if (TryGetPropertyCaseInsensitive(root, "top_themes", out var themesEl) &&
                        themesEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var t in themesEl.EnumerateArray())
                        {
                            string? text = t.ValueKind == JsonValueKind.String
                                ? t.GetString()
                                : TryGetPropertyCaseInsensitive(t, "text", out var tx) ? tx.GetString() : null;
                            if (!string.IsNullOrWhiteSpace(text)) themes.Add(text);
                        }
                    }

                    // top_recommendations — nested (area_title + action_steps) or flat ({ text })
                    if (TryGetPropertyCaseInsensitive(root, "top_recommendations", out var recsEl) &&
                        recsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var r in recsEl.EnumerateArray())
                        {
                            string? recText = r.ValueKind == JsonValueKind.String
                                ? r.GetString()
                                : TryGetPropertyCaseInsensitive(r, "area_title", out var at) && !string.IsNullOrWhiteSpace(at.GetString())
                                    ? at.GetString()
                                    : TryGetPropertyCaseInsensitive(r, "text", out var rt) ? rt.GetString() : null;

                            if (!string.IsNullOrWhiteSpace(recText)) recommendations.Add(recText);

                            if (r.ValueKind == JsonValueKind.Object &&
                                TryGetPropertyCaseInsensitive(r, "action_steps", out var stepsEl) &&
                                stepsEl.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var step in stepsEl.EnumerateArray())
                                {
                                    var title       = TryGetPropertyCaseInsensitive(step, "title",       out var ti) ? ti.GetString() : null;
                                    var description = TryGetPropertyCaseInsensitive(step, "description", out var d)  ? d.GetString()  : null;
                                    var actionText  = string.IsNullOrWhiteSpace(title)       ? description
                                                    : string.IsNullOrWhiteSpace(description) ? title
                                                    : $"{title}: {description}";
                                    if (!string.IsNullOrWhiteSpace(actionText)) actions.Add(actionText);
                                }
                            }
                        }
                    }

                    // action_steps at root level (SystemPrompt schema)
                    if (TryGetPropertyCaseInsensitive(root, "action_steps", out var rootActionsEl) &&
                        rootActionsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var action in rootActionsEl.EnumerateArray())
                        {
                            string? actionText = action.ValueKind == JsonValueKind.String
                                ? action.GetString()
                                : TryGetPropertyCaseInsensitive(action, "text", out var atx) ? atx.GetString() : null;
                            if (!string.IsNullOrWhiteSpace(actionText)) actions.Add(actionText);
                        }
                    }

                    // First parseable response wins, content or not.
                    return (themes, recommendations, actions);
                }
                catch { }
            }

            return (new List<string>(), new List<string>(), new List<string>());
        }

        public static bool TryGetPropertyCaseInsensitive(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = prop.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }
    }
}
