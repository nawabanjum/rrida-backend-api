using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    public class CloverEraEngineService : ICloverEraEngineService
    {
        private readonly IDocumentEmbeddingRepository _embeddingRepo;
        private readonly IPromptTemplateRepository _promptRepo;
        private readonly IHttpClientFactory _httpFactory;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<CloverEraEngineService> _logger;

        // Per-run state (safe: service is scoped, one instance per request)
        private string _runSystemPrompt = "";
        private double? _runTempOverride;
        private int? _runMaxTokensOverride;
        private double? _runTopPOverride;
        private readonly List<string> _runQualityWarnings = new();

        private double T(double d) => _runTempOverride ?? d;
        private int MT(int d) => _runMaxTokensOverride ?? d;
        private double TP() => _runTopPOverride ?? 1.0;

        private static int Stage2ThinkingBudget =>
            int.TryParse(Environment.GetEnvironmentVariable("CLOVER_ERA_STAGE2_THINKING_BUDGET"), out var v) ? v : 4000;
        private static int Stage3ThinkingBudget =>
            int.TryParse(Environment.GetEnvironmentVariable("CLOVER_ERA_STAGE3_THINKING_BUDGET"), out var v) ? v : 6000;
        private static bool EnableSynthesis =>
            (Environment.GetEnvironmentVariable("CLOVER_ERA_ENABLE_SYNTHESIS") ?? "1") != "0";

        private static readonly string[] Pillars =
            ["Communication", "Learning", "Opportunities", "Vulnerability", "Enablement", "Reflection"];

        private static readonly Dictionary<string, int> SentimentValues = new()
        {
            ["Happy"] = 100,
            ["Indifferent"] = 66,
            ["Sad"] = 33,
            ["Angry"] = 1
        };

        private const double TargetScore = 70.0;
        private const double BiasCorrection = 10.0;

        // ── Escalation patterns ──
        private record EscalationPattern(
            string Category,
            string[] Keywords,
            string[] Phrases,
            string Route,
            int TimeframeDays,
            int Priority,
            bool RequiresSystemicContext = false);

        private static readonly EscalationPattern[] EscalationPatterns =
        [
            new("self_harm",
                ["suicide", "suicidal", "self-harm", "self harm"],
                ["kill myself", "end it all", "don't want to be here anymore",
                 "do not want to be here anymore", "can't go on", "cannot go on", "want to disappear"],
                "EAP", 7, 5),
            new("safeguarding",
                ["unsafe", "vulnerable adult", "minor", "child protection"],
                ["safety concern", "raised concerns about safety", "safeguarding issue",
                 "not physically safe", "not feel safe", "do not feel safe", "don't feel safe",
                 "not feel physically safe"],
                "Safeguarding lead", 7, 4),
            new("harassment",
                ["harassed", "harassment", "bullied", "bullying", "hostile", "threatening"],
                ["targeted me", "singled out", "makes me afraid", "intimidated by", "made threats"],
                "HR", 14, 3),
            new("discrimination",
                ["religion", "religious", "race", "racial", "racism", "ethnic",
                 "sexuality", "sexual orientation", "homophobic", "transphobic",
                 "disability", "ableism", "nationality", "xenophobic"],
                ["gender discrimination", "age discrimination", "culture only", "only accepting",
                 "only hire", "have quit because", "left because of", "not welcome here", "pushed out because"],
                "HR", 14, 2),
            new("attrition",
                ["leaving", "quit", "quitting", "resign", "resigning"],
                ["already left", "have all gone", "people are leaving", "everyone is leaving",
                 "mass exodus", "revolving door"],
                "Leadership", 14, 1, RequiresSystemicContext: true),
        ];

        private static readonly HashSet<string> SystemicContextWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "culture", "environment", "leadership", "treatment", "management",
            "manager", "toxic", "unreasonable", "burnout", "overworked", "overwork",
            "pay", "compensation", "ignored", "unsupported", "morale", "politics"
        };

        // ── PII redaction safelists ──
        private static readonly HashSet<string> SafeCapitalised = new(StringComparer.Ordinal)
        {
            "I", "Im", "Ill", "Ive", "Id",
            "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday",
            "January", "February", "March", "April", "May", "June", "July",
            "August", "September", "October", "November", "December",
            "Q1", "Q2", "Q3", "Q4",
            "WFH", "HR", "IT", "OK", "OKR", "KPI", "CEO", "CFO", "COO", "CTO",
            "PTO", "FTE", "AI"
        };

        private static readonly HashSet<string> SentenceStarterLower = new(StringComparer.OrdinalIgnoreCase)
        {
            "the","a","an","this","that","these","those","it","he","she","they","we","you",
            "his","her","their","our","your","its","my","there","here","where","who","whom",
            "whose","what","when","why","how","which","if","because","since","while","although",
            "though","so","but","and","or","nor","yet","also","then","thus","hence","however",
            "still","some","most","many","few","all","any","every","each","both","either",
            "neither","no","none","other","another","now","soon","later","already","just",
            "maybe","perhaps","really","truly","probably","actually","basically","sometimes",
            "often","always","never","usually","rarely","occasionally","frequently","typically",
            "generally","can","could","would","should","may","might","will","shall","must",
            "do","does","did","doing","done","have","has","had","having","is","are","was",
            "were","be","been","being","am","let","lets","please","thanks","thank","yes",
            "yep","yeah","no","not","nope","nah","well","ok","okay","sure","right","only",
            "even","first","second","third","last","next","finally","otherwise","again",
            "overall","initially","similarly","instead","meanwhile","eventually","unfortunately",
            "fortunately","hopefully","up","down","in","on","off","out","over","under","about","around"
        };

        private static readonly Regex EmailRe = new(@"[\w.+-]+@[\w-]+\.[\w.-]+", RegexOptions.Compiled);
        private static readonly Regex UrlRe = new(@"https?://\S+|www\.\S+", RegexOptions.Compiled);
        private static readonly Regex PhoneRe = new(@"\b\d{3}[-.\s]?\d{3}[-.\s]?\d{4}\b", RegexOptions.Compiled);
        private static readonly Regex NameRunRe = new(@"\[name\](\s+\[name\])+", RegexOptions.Compiled);
        private static readonly Regex JsonFenceRe = new(@"^```[a-zA-Z]*\n", RegexOptions.Multiline);
        private static readonly Regex JsonBodyRe = new(@"\{.*\}", RegexOptions.Singleline);
        private static readonly Regex SentenceSplitRe = new(@"(?<!\d)[.!?]+(?!\d)", RegexOptions.Compiled);
        private static readonly Regex WordRe = new(@"[A-Za-z']+", RegexOptions.Compiled);

        private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
        {
            "a","an","the","and","or","of","in","to","is","are","be","been","it","its",
            "this","that","we","our","i","my","at","on","for","as","by","from","with",
            "not","no","so","up","out","then","can","could","would","should","may","might",
            "has","have","had","do","does","did"
        };

        public CloverEraEngineService(
            IDocumentEmbeddingRepository embeddingRepo,
            IPromptTemplateRepository promptRepo,
            IHttpClientFactory httpFactory,
            IWebHostEnvironment env,
            ILogger<CloverEraEngineService> logger)
        {
            _embeddingRepo = embeddingRepo;
            _promptRepo = promptRepo;
            _httpFactory = httpFactory;
            _env = env;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Public entry point
        // ─────────────────────────────────────────────────────────────────────

        public async Task<CloverEraCompleteReportDto> RunEngineAsync(
            WorkbenchRunRequestDto request,
            IFormFile csvFile,
            CancellationToken ct = default)
        {
            if (request.ModelIds == null || request.ModelIds.Count == 0)
                throw new InvalidOperationException("At least one model ID is required.");

            var model = await _embeddingRepo.GetModelByIdAsync(request.ModelIds[0])
                ?? throw new InvalidOperationException($"Model {request.ModelIds[0]} not found.");

            var prompt = await _promptRepo.GetByIdAsync(request.PromptId, ct)
                ?? throw new InvalidOperationException($"Prompt {request.PromptId} not found.");

            // Set per-run overrides (instance fields, safe since service is scoped)
            _runSystemPrompt = prompt.SystemPrompt ?? "";
            _runTempOverride = request.Temperature;
            _runMaxTokensOverride = request.MaxTokens;
            _runTopPOverride = request.TopP;

            var businessUnit = request.UnitId?.ToString() ?? "";
            var company = request.ProfileId?.ToString() ?? "";

            _runQualityWarnings.Clear();

            // Read CSV rows
            var rows = await ParseCsvAsync(csvFile, ct);
            if (rows.Count == 0)
                throw new InvalidOperationException("CSV file contained no data rows.");

            // Stage 1: deterministic
            var dataset = ProcessDataset(rows, businessUnit, company);

            // Escalation: deterministic (must run on raw text before PII redaction)
            var escalation = DetectEscalation(dataset.RawFreeTexts);

            // Stage 2-5 are LLM-bound. If the configured model is misconfigured
            // (e.g. Azure endpoint missing /openai/deployments/.../chat/completions
            // path → returns 200 + empty body), each stage call will throw.
            // We catch per stage so a single bad call no longer kills the whole
            // pipeline: deterministic data + any successful stages still produce
            // a saved LLMReport row, and the failure is recorded as a quality
            // warning so it surfaces in audit.
            List<WrittenTheme> themes;
            try
            {
                themes = await WriteThemesAsync(dataset, model, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[CloverEra] Stage 2 (themes) failed; continuing with empty themes.");
                _runQualityWarnings.Add($"Stage 2 (themes): {ex.GetType().Name}: {ex.Message}");
                themes = new List<WrittenTheme>();
            }

            List<CloverEraRecommendationDto> recommendations;
            CloverEraWorksheetActionDto? worksheetAction;
            try
            {
                var recTask = WriteRecommendationsAsync(dataset, themes, 2, model, ct);
                var wsTask = SelectWorksheetAsync(dataset, themes, new List<string>(), model, ct);
                await Task.WhenAll(recTask, wsTask);
                recommendations = await recTask;
                worksheetAction = await wsTask;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[CloverEra] Stage 3/4 (recs/worksheet) failed; continuing with empty values.");
                _runQualityWarnings.Add($"Stage 3/4 (recs/worksheet): {ex.GetType().Name}: {ex.Message}");
                recommendations = new List<CloverEraRecommendationDto>();
                worksheetAction = null;
            }

            List<CloverEraMicroActionDto> microActions;
            if (worksheetAction == null)
            {
                _runQualityWarnings.Add("Stage 5 (micro actions): skipped — no worksheet available from Stage 4.");
                microActions = new List<CloverEraMicroActionDto>();
            }
            else
            {
                try
                {
                    microActions = await WriteMicroActionsAsync(dataset, themes, worksheetAction, escalation != null, model, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[CloverEra] Stage 5 (micro actions) failed; continuing with empty list.");
                    _runQualityWarnings.Add($"Stage 5 (micro actions): {ex.GetType().Name}: {ex.Message}");
                    microActions = new List<CloverEraMicroActionDto>();
                }
            }

            // Stage 5.5: synthesis pass (cross-piece deduplication)
            if (EnableSynthesis && themes.Count >= 3 && recommendations.Count >= 2 && microActions.Count >= 1)
            {
                try
                {
                    (themes, recommendations, worksheetAction, microActions, escalation) =
                        await RunSynthesisPassAsync(themes, recommendations, worksheetAction, microActions, escalation, model, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[CloverEra] Synthesis pass failed, continuing with original pieces.");
                    _runQualityWarnings.Add($"Stage 5.5 (synthesis): failed with {ex.Message}; original pieces used.");
                }
            }

            // Merge quality warnings into dataset before assembly
            dataset.DataQualityIssues.AddRange(_runQualityWarnings);

            // Stage 6: assemble
            return Assemble(dataset, themes, recommendations, worksheetAction, microActions, escalation);
        }

        // ─────────────────────────────────────────────────────────────────────
        // CSV Parsing
        // ─────────────────────────────────────────────────────────────────────

        private async Task<List<Dictionary<string, string>>> ParseCsvAsync(IFormFile csvFile, CancellationToken ct)
        {
            using var stream = csvFile.OpenReadStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var content = await reader.ReadToEndAsync(ct);

            var lines = content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            if (lines.Length == 0)
                return new();

            // Parse header
            var headers = SplitCsvLine(lines[0]);
            var rows = new List<Dictionary<string, string>>();

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;
                var fields = SplitCsvLine(line);
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int j = 0; j < headers.Count && j < fields.Count; j++)
                    row[headers[j]] = fields[j];
                rows.Add(row);
            }
            return rows;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
            fields.Add(sb.ToString());
            return fields;
        }

        // ─────────────────────────────────────────────────────────────────────
        // PII Redaction
        // ─────────────────────────────────────────────────────────────────────

        private static string RedactPii(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            text = EmailRe.Replace(text, "[email]");
            text = UrlRe.Replace(text, "[url]");
            text = PhoneRe.Replace(text, "[phone]");

            var sb = new StringBuilder();
            bool sentenceStart = true;
            int i = 0;
            while (i < text.Length)
            {
                char ch = text[i];
                if (char.IsLetter(ch))
                {
                    int j = i;
                    while (j < text.Length && (char.IsLetter(text[j]) || text[j] == '\''))
                        j++;
                    var word = text[i..j];
                    var bare = word.Replace("'", "");
                    bool isCapitalised = char.IsUpper(word[0]) && word.Length >= 2;
                    bool isSafelisted = SafeCapitalised.Contains(bare) || SafeCapitalised.Contains(word);
                    bool isCommonStarter = sentenceStart && SentenceStarterLower.Contains(word.ToLower());

                    if (isCapitalised && !isSafelisted && !isCommonStarter)
                        sb.Append("[name]");
                    else
                        sb.Append(word);

                    sentenceStart = false;
                    i = j;
                }
                else
                {
                    sb.Append(ch);
                    if (ch == '.' || ch == '!' || ch == '?')
                        sentenceStart = true;
                    i++;
                }
            }

            var result = sb.ToString();
            result = NameRunRe.Replace(result, "[name]");
            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Stage 1: Data Processor (internal types)
        // ─────────────────────────────────────────────────────────────────────

        private sealed class PillarStats
        {
            public string Pillar { get; set; } = "";
            public int ResponseCount { get; set; }
            public int HappyCount { get; set; }
            public int IndifferentCount { get; set; }
            public int SadCount { get; set; }
            public int AngryCount { get; set; }
            public double RawScore { get; set; }
            public double CorrectedScore { get; set; }
            public double GapFromTarget { get; set; }
            public string Status { get; set; } = "";
            public double ConfidenceWeight { get; set; }
            public double FocusPriorityScore { get; set; }
            public Dictionary<string, int> ReasonTextCounts { get; set; } = new();
            public List<Dictionary<string, string>> FreeTextResponses { get; set; } = new();
            public List<string> NegativeReasonTexts { get; set; } = new();
        }

        private sealed class FocusRecommendation
        {
            public string Pillar { get; set; } = "";
            public double CorrectedScore { get; set; }
            public double GapFromTarget { get; set; }
            public double ConfidenceScore { get; set; }
            public List<string> WhySelected { get; set; } = new();
            public string? SecondaryPillar { get; set; }
            public string? SecondaryRationale { get; set; }
        }

        private sealed class ThemeAssignment
        {
            public int ThemeIndex { get; set; }
            public string Sentiment { get; set; } = "";
            public string Pillar { get; set; } = "";
            public string Rationale { get; set; } = "";
        }

        private sealed class ProcessedDataset
        {
            public string BusinessUnit { get; set; } = "";
            public string Profile { get; set; } = "";
            public string PeriodStart { get; set; } = "";
            public string PeriodEnd { get; set; } = "";
            public int TotalResponses { get; set; }
            public double OverallRawScore { get; set; }
            public double OverallCorrectedScore { get; set; }
            public Dictionary<string, PillarStats> PillarStats { get; set; } = new();
            public FocusRecommendation FocusRecommendation { get; set; } = new();
            public List<ThemeAssignment> ThemeAssignments { get; set; } = new();
            public List<string> DataQualityIssues { get; set; } = new();
            public double FreeTextCompletionRate { get; set; }
            public List<string> LowSamplePillars { get; set; } = new();
            public List<string> RawFreeTexts { get; set; } = new();
        }

        private sealed class WrittenTheme
        {
            public int ThemeIndex { get; set; }
            public string Sentiment { get; set; } = "";
            public string Pillar { get; set; } = "";
            public string Headline { get; set; } = "";
            public string Text { get; set; } = "";
        }

        private ProcessedDataset ProcessDataset(List<Dictionary<string, string>> rows, string businessUnit, string company)
        {
            // Period bounds from SubmissionDateTime
            var dates = rows
                .Select(r => r.TryGetValue("SubmissionDateTime", out var d) ? ParseDate(d) : null)
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .ToList();

            string periodStart = dates.Count > 0 ? dates.Min().ToString("yyyy-MM-dd") : "";
            string periodEnd = dates.Count > 0 ? dates.Max().ToString("yyyy-MM-dd") : "";

            int total = rows.Count;
            var validSentimentRows = rows.Where(r => SentimentValues.ContainsKey(r.GetValueOrDefault("SentimentType", ""))).ToList();
            double overallRaw = validSentimentRows.Count > 0
                ? (double)validSentimentRows.Sum(r => SentimentValues[r["SentimentType"]]) / validSentimentRows.Count
                : 0.0;
            double overallCorrected = Math.Max(0.0, overallRaw - BiasCorrection);

            // Pillar stats
            var pillarStats = ComputePillarStats(rows);

            // Focus
            var issues = new List<string>();
            var focus = SelectFocus(pillarStats);

            // Low sample warnings
            var lowSample = pillarStats.Where(p => p.Value.ResponseCount > 0 && p.Value.ResponseCount < 4)
                .Select(p => p.Key).ToList();
            foreach (var p in lowSample)
                issues.Add($"{p} has only {pillarStats[p].ResponseCount} responses; confidence weighted down.");

            var zeroPillars = pillarStats.Where(p => p.Value.ResponseCount == 0).Select(p => p.Key).ToList();
            foreach (var p in zeroPillars)
                issues.Add($"{p} received 0 responses this period; cannot be a theme.");

            int freeTextRows = rows.Count(r => !string.IsNullOrWhiteSpace(r.GetValueOrDefault("FreeText", "")));
            double freeTextRate = total > 0 ? (double)freeTextRows / total : 0.0;
            if (freeTextRate < 0.2)
                issues.Add($"Free-text completion rate {freeTextRate:P0} is below 20%; qualitative evidence is thin this period.");

            // Theme assignment
            var themeAssignments = AssignThemes(pillarStats, focus.Pillar, issues);

            // Raw free texts for escalation (pre-redaction)
            var rawFreeTexts = rows
                .Where(r => !string.IsNullOrWhiteSpace(r.GetValueOrDefault("FreeText", "")))
                .Select(r => r["FreeText"].Trim())
                .ToList();

            return new ProcessedDataset
            {
                BusinessUnit = businessUnit,
                Profile = company,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                TotalResponses = total,
                OverallRawScore = Math.Round(overallRaw, 2),
                OverallCorrectedScore = Math.Round(overallCorrected, 2),
                PillarStats = pillarStats,
                FocusRecommendation = focus,
                ThemeAssignments = themeAssignments,
                DataQualityIssues = issues,
                FreeTextCompletionRate = Math.Round(freeTextRate, 3),
                LowSamplePillars = lowSample,
                RawFreeTexts = rawFreeTexts
            };
        }

        private static DateTime? ParseDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var cleaned = s.Trim().Replace("Z", "+00:00");
            if (DateTimeOffset.TryParse(cleaned, out var dto))
                return dto.UtcDateTime;
            return null;
        }

        private static Dictionary<string, PillarStats> ComputePillarStats(List<Dictionary<string, string>> rows)
        {
            var byPillar = Pillars.ToDictionary(p => p, _ => new List<Dictionary<string, string>>());
            foreach (var r in rows)
            {
                var pillar = r.GetValueOrDefault("Category", "").Trim();
                if (byPillar.ContainsKey(pillar))
                    byPillar[pillar].Add(r);
            }

            var stats = new Dictionary<string, PillarStats>();
            foreach (var pillar in Pillars)
            {
                var pr = byPillar[pillar];
                int n = pr.Count;
                int happy = pr.Count(r => r.GetValueOrDefault("SentimentType") == "Happy");
                int indiff = pr.Count(r => r.GetValueOrDefault("SentimentType") == "Indifferent");
                int sad = pr.Count(r => r.GetValueOrDefault("SentimentType") == "Sad");
                int angry = pr.Count(r => r.GetValueOrDefault("SentimentType") == "Angry");

                double raw = 0, corrected = 0, gap = 0;
                if (n > 0)
                {
                    raw = (double)pr.Sum(r => SentimentValues.TryGetValue(r.GetValueOrDefault("SentimentType", ""), out var v) ? v : 0) / n;
                    corrected = Math.Max(0.0, raw - BiasCorrection);
                    gap = Math.Max(0.0, TargetScore - corrected);
                }

                double conf = n >= 8 ? 1.0 : n >= 4 ? 0.8 : n >= 1 ? 0.5 : 0.0;
                double focusPriority = gap * conf;

                // Reason text counts
                var reasonCounts = pr
                    .Where(r => !string.IsNullOrWhiteSpace(r.GetValueOrDefault("ReasonText", "")))
                    .GroupBy(r => r["ReasonText"].Trim())
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key)
                    .ToDictionary(g => g.Key, g => g.Count());

                // Free text responses (PII-redacted)
                var freeTextResponses = pr
                    .Where(r => !string.IsNullOrWhiteSpace(r.GetValueOrDefault("FreeText", "")))
                    .Select(r => new Dictionary<string, string>
                    {
                        ["sentiment"] = r.GetValueOrDefault("SentimentType", ""),
                        ["reason_text"] = r.GetValueOrDefault("ReasonText", "").Trim(),
                        ["free_text"] = RedactPii(r["FreeText"].Trim())
                    })
                    .ToList();

                // Negative reason texts (Sad or Angry, deduped)
                var seen = new HashSet<string>();
                var negativeReasons = new List<string>();
                foreach (var r in pr)
                {
                    var st = r.GetValueOrDefault("SentimentType", "");
                    if (st == "Sad" || st == "Angry")
                    {
                        var rt = r.GetValueOrDefault("ReasonText", "").Trim();
                        if (!string.IsNullOrEmpty(rt) && seen.Add(rt))
                            negativeReasons.Add(rt);
                    }
                }

                stats[pillar] = new PillarStats
                {
                    Pillar = pillar,
                    ResponseCount = n,
                    HappyCount = happy,
                    IndifferentCount = indiff,
                    SadCount = sad,
                    AngryCount = angry,
                    RawScore = Math.Round(raw, 2),
                    CorrectedScore = Math.Round(corrected, 2),
                    GapFromTarget = Math.Round(gap, 2),
                    Status = StatusFor(corrected, n),
                    ConfidenceWeight = conf,
                    FocusPriorityScore = Math.Round(focusPriority, 2),
                    ReasonTextCounts = reasonCounts,
                    FreeTextResponses = freeTextResponses,
                    NegativeReasonTexts = negativeReasons
                };
            }
            return stats;
        }

        private static string StatusFor(double corrected, int n)
        {
            if (n == 0) return "no_data";
            if (corrected >= 85) return "Excellent";
            if (corrected >= 70) return "Healthy";
            if (corrected >= 60) return "Monitor";
            if (corrected >= 50) return "Needs Work";
            if (corrected >= 40) return "Critical";
            return "Severe Crisis";
        }

        private static FocusRecommendation SelectFocus(Dictionary<string, PillarStats> pillarStats)
        {
            var eligible = pillarStats.Where(p => p.Value.ResponseCount > 0).ToList();
            if (eligible.Count == 0)
            {
                return new FocusRecommendation
                {
                    WhySelected = ["No responses received. No focus pillar can be selected."]
                };
            }

            var ranked = eligible
                .OrderByDescending(p => p.Value.FocusPriorityScore)
                .ThenByDescending(p => p.Value.ResponseCount)
                .ToList();

            var focus = ranked[0];
            string? secondary = null;
            string? secondaryRationale = null;

            if (ranked.Count > 1)
            {
                var runner = ranked[1];
                if (Math.Abs(focus.Value.FocusPriorityScore - runner.Value.FocusPriorityScore) <= 1.0)
                {
                    // Swap if runner has higher response count
                    if (runner.Value.ResponseCount > focus.Value.ResponseCount)
                        (focus, runner) = (runner, focus);

                    secondary = runner.Key;
                    secondaryRationale = $"Within 1.0 of focus on priority score " +
                        $"({runner.Value.FocusPriorityScore} vs {focus.Value.FocusPriorityScore}); " +
                        $"kept as secondary based on response count.";
                }
            }

            var why = BuildWhySelected(focus.Value, pillarStats);

            return new FocusRecommendation
            {
                Pillar = focus.Key,
                CorrectedScore = focus.Value.CorrectedScore,
                GapFromTarget = focus.Value.GapFromTarget,
                ConfidenceScore = focus.Value.FocusPriorityScore,
                WhySelected = why,
                SecondaryPillar = secondary,
                SecondaryRationale = secondaryRationale
            };
        }

        private static List<string> BuildWhySelected(PillarStats focusStats, Dictionary<string, PillarStats> all)
        {
            var reasons = new List<string>
            {
                $"Widest gap from target at {focusStats.GapFromTarget:F1} percentage points"
            };

            if (focusStats.ResponseCount > 0 && focusStats.ReasonTextCounts.Count > 0)
            {
                var top = focusStats.ReasonTextCounts.First();
                double share = (double)top.Value / focusStats.ResponseCount;
                if (share >= 0.30)
                    reasons.Add($"'{top.Key}' selected by {top.Value} of {focusStats.ResponseCount} respondents ({share * 100:F0}%)");
            }

            int ftCount = focusStats.FreeTextResponses.Count;
            if (ftCount > 0)
                reasons.Add($"Free-text comments from {ftCount} {(ftCount == 1 ? "respondent" : "respondents")} reinforce the pattern (content redacted from this summary).");

            int maxCount = all.Values.Max(s => s.ResponseCount);
            if (focusStats.ResponseCount == maxCount && maxCount > 0)
                reasons.Add("Highest response volume in the dataset, giving strongest statistical confidence");

            return reasons.Take(4).ToList();
        }

        private static List<ThemeAssignment> AssignThemes(
            Dictionary<string, PillarStats> pillarStats,
            string focusPillar,
            List<string> issues)
        {
            // Positive candidates
            var positiveCandidates = pillarStats
                .Where(p => p.Value.ResponseCount > 0 &&
                    (p.Value.CorrectedScore > 65 ||
                     (p.Value.ResponseCount >= 2 && p.Value.SadCount == 0 && p.Value.AngryCount == 0)))
                .OrderByDescending(p => p.Value.CorrectedScore)
                .ThenByDescending(p => p.Value.ResponseCount)
                .Select(p => p.Key)
                .ToList();

            // Negative candidates
            var negativeCandidates = pillarStats
                .Where(p => IsNegativeCandidate(p.Value))
                .OrderByDescending(p => p.Value.GapFromTarget)
                .ThenByDescending(p => p.Value.ResponseCount)
                .Select(p => p.Key)
                .ToList();

            var assignments = new List<ThemeAssignment>();
            var usedPillars = new HashSet<string>();

            // Theme 0: top positive
            if (positiveCandidates.Count > 0)
            {
                var p0 = positiveCandidates[0];
                assignments.Add(new ThemeAssignment { ThemeIndex = 0, Sentiment = "positive", Pillar = p0, Rationale = RationalePositive(pillarStats[p0]) });
                usedPillars.Add(p0);
            }

            // Theme 1: next positive
            var p1 = positiveCandidates.FirstOrDefault(p => !usedPillars.Contains(p));
            if (p1 != null)
            {
                assignments.Add(new ThemeAssignment { ThemeIndex = 1, Sentiment = "positive", Pillar = p1, Rationale = RationalePositive(pillarStats[p1]) });
                usedPillars.Add(p1);
            }
            else
            {
                var fallback = pillarStats
                    .Where(p => p.Value.ResponseCount > 0 && !usedPillars.Contains(p.Key))
                    .OrderByDescending(p => p.Value.CorrectedScore)
                    .ThenByDescending(p => p.Value.ResponseCount)
                    .Select(p => p.Key)
                    .FirstOrDefault();

                if (fallback != null)
                {
                    issues.Add($"Only {positiveCandidates.Count} genuinely positive signal(s) available; second positive theme uses {fallback} as best available.");
                    assignments.Add(new ThemeAssignment
                    {
                        ThemeIndex = 1, Sentiment = "positive", Pillar = fallback,
                        Rationale = RationalePositive(pillarStats[fallback]) + " No stronger positive candidate available this period."
                    });
                    usedPillars.Add(fallback);
                }
            }

            // Theme 2: focus pillar (negative) — always
            if (!string.IsNullOrEmpty(focusPillar) && pillarStats.TryGetValue(focusPillar, out var fs) && fs.ResponseCount > 0)
            {
                assignments.Add(new ThemeAssignment { ThemeIndex = 2, Sentiment = "negative", Pillar = focusPillar, Rationale = RationaleNegative(fs, true) });
                usedPillars.Add(focusPillar);
            }

            // Theme 3: next negative
            var p3 = negativeCandidates.FirstOrDefault(p => !usedPillars.Contains(p));
            if (p3 != null)
            {
                assignments.Add(new ThemeAssignment { ThemeIndex = 3, Sentiment = "negative", Pillar = p3, Rationale = RationaleNegative(pillarStats[p3], false) });
                usedPillars.Add(p3);
            }

            // Diversity check
            var distinct = assignments.Select(a => a.Pillar).ToHashSet();
            if (distinct.Count < 3 && assignments.Count >= 3)
            {
                issues.Add($"Theme assignments cover only {distinct.Count} distinct pillar(s); sample depth limits diversity this period.");
                var swapPool = pillarStats
                    .Where(p => p.Value.ResponseCount > 0 && !distinct.Contains(p.Key))
                    .OrderByDescending(p => p.Value.GapFromTarget)
                    .ThenByDescending(p => p.Value.ResponseCount)
                    .Select(p => p.Key)
                    .ToList();

                if (swapPool.Count > 0 && assignments.Count == 4)
                {
                    var newPillar = swapPool[0];
                    var old = assignments[3];
                    assignments[3] = new ThemeAssignment
                    {
                        ThemeIndex = 3, Sentiment = old.Sentiment, Pillar = newPillar,
                        Rationale = RationaleNegative(pillarStats[newPillar], false) + " Selected to maximise pillar diversity across themes."
                    };
                }
            }

            if (assignments.Count < 4)
                issues.Add($"Only {assignments.Count} of 4 theme slots could be filled from available data.");

            return assignments;
        }

        private static bool IsNegativeCandidate(PillarStats s)
        {
            if (s.ResponseCount >= 2 && s.SadCount + s.AngryCount >= 1) return true;
            if (s.ResponseCount >= 3 && s.IndifferentCount > Math.Max(s.HappyCount, s.SadCount + s.AngryCount)) return true;
            return false;
        }

        private static string RationalePositive(PillarStats s)
        {
            var bits = new List<string> { $"Corrected score {s.CorrectedScore:F1} with {s.ResponseCount} responses" };
            if (s.SadCount == 0 && s.AngryCount == 0) bits.Add("zero negative responses");
            if (s.CorrectedScore > 65) bits.Add("above the 65 positive threshold");
            return string.Join("; ", bits) + ".";
        }

        private static string RationaleNegative(PillarStats s, bool isFocus)
        {
            var label = isFocus ? "Focus pillar: " : "";
            return $"{label}corrected score {s.CorrectedScore:F1}, {s.GapFromTarget:F1} points below target, " +
                   $"with {s.SadCount} Sad and {s.AngryCount} Angry responses out of {s.ResponseCount}.";
        }

        // ─────────────────────────────────────────────────────────────────────
        // Escalation Detection
        // ─────────────────────────────────────────────────────────────────────

        private static CloverEraEscalationActionDto? DetectEscalation(List<string> rawTexts)
        {
            var matches = new List<EscalationPattern>();

            foreach (var raw in rawTexts)
            {
                var lower = raw.ToLower();
                if (string.IsNullOrWhiteSpace(lower)) continue;

                foreach (var spec in EscalationPatterns)
                {
                    if (matches.Contains(spec)) continue;
                    bool triggered = TextContainsAny(lower, spec.Keywords) || TextContainsAny(lower, spec.Phrases);
                    if (!triggered) continue;
                    if (spec.RequiresSystemicContext && !HasSystemicContext(lower)) continue;
                    matches.Add(spec);
                }
            }

            if (matches.Count == 0) return null;

            var winner = matches.OrderByDescending(s => s.Priority).First();
            return new CloverEraEscalationActionDto
            {
                SignalCategory = winner.Category,
                Route = winner.Route,
                TimeframeDays = winner.TimeframeDays,
                Text = $"A FreeText response in this period contained signals consistent with {winner.Category}. " +
                       $"Route this to {winner.Route} within {winner.TimeframeDays} days. Frame the conversation as a " +
                       $"survey-sourced insight requiring investigation, not as an accusation. Do not quote the respondent directly, " +
                       $"particularly if doing so would identify them."
            };
        }

        private static bool TextContainsAny(string lowerText, string[] needles)
        {
            foreach (var needle in needles)
            {
                var needleLower = needle.ToLower();
                if (needleLower.Contains(' ') || needleLower.Contains('-'))
                {
                    if (lowerText.Contains(needleLower)) return true;
                }
                else
                {
                    if (Regex.IsMatch(lowerText, @"\b" + Regex.Escape(needleLower) + @"\b"))
                        return true;
                }
            }
            return false;
        }

        private static bool HasSystemicContext(string lowerText)
        {
            var words = Regex.Matches(lowerText, @"\b[\w']+\b")
                .Select(m => m.Value.ToLower())
                .ToHashSet();
            return words.Overlaps(SystemicContextWords);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Stage 2: Theme Writing
        // ─────────────────────────────────────────────────────────────────────

        private async Task<List<WrittenTheme>> WriteThemesAsync(ProcessedDataset dataset, ModelConfiguration model, CancellationToken ct)
        {
            var tasks = dataset.ThemeAssignments.Select(a => WriteOneThemeAsync(a, dataset, model, ct));
            var results = await Task.WhenAll(tasks);
            return results.OrderBy(t => t.ThemeIndex).ToList();
        }

        private async Task<WrittenTheme> WriteOneThemeAsync(ThemeAssignment assignment, ProcessedDataset dataset, ModelConfiguration model, CancellationToken ct)
        {
            var prompt = BuildThemePrompt(assignment, dataset);
            var isAnthropic = (model.Endpoint ?? "").Contains("/anthropic/", StringComparison.OrdinalIgnoreCase);
            var thinkingBudget = isAnthropic ? Stage2ThinkingBudget : 0;

            var (raw, _, _, _) = await CallLlmAsync(_runSystemPrompt, prompt, model, T(0.8), MT(400), TP(), ct, thinkingBudget);

            var parsed = TryParseJson(raw);
            if (parsed == null)
            {
                var retryPrompt = prompt + "\n\n## Retry instruction\n\nYour previous response was not valid JSON. Return only the JSON object with 'headline' and 'text' keys. No prose, no fences, no commentary.";
                (raw, _, _, _) = await CallLlmAsync(_runSystemPrompt, retryPrompt, model, T(0.8), MT(400), TP(), ct, thinkingBudget);
                parsed = TryParseJson(raw);
            }

            if (parsed == null)
            {
                _logger.LogWarning("[CloverEra] Theme {Index} ({Pillar}): parse failure, passing raw text.", assignment.ThemeIndex, assignment.Pillar);
                return new WrittenTheme { ThemeIndex = assignment.ThemeIndex, Sentiment = assignment.Sentiment, Pillar = assignment.Pillar, Headline = "(parse failure)", Text = raw.Trim()[..Math.Min(500, raw.Length)] };
            }

            var failures = ValidateTheme(parsed.Value, assignment.Pillar);
            if (failures.Count > 0)
            {
                var retryInstruction = "Your previous response failed validation. Fix the following and return the same JSON structure:\n- " + string.Join("\n- ", failures);
                var retryPrompt = prompt + "\n\n## Retry instruction\n\n" + retryInstruction;
                (raw, _, _, _) = await CallLlmAsync(_runSystemPrompt, retryPrompt, model, T(0.8), MT(400), TP(), ct, thinkingBudget);
                var retryParsed = TryParseJson(raw);
                if (retryParsed != null)
                {
                    parsed = retryParsed;
                    _runQualityWarnings.Add($"Stage 2 (theme writer): Theme {assignment.ThemeIndex} ({assignment.Pillar}) required 1 retry: {string.Join("; ", failures)}");
                }
                else
                {
                    _runQualityWarnings.Add($"Stage 2 (theme writer): Theme {assignment.ThemeIndex} ({assignment.Pillar}) validation failed after retry: {string.Join("; ", failures)}; accepted as-is.");
                }
            }

            var el = parsed!.Value;
            return new WrittenTheme
            {
                ThemeIndex = assignment.ThemeIndex,
                Sentiment = assignment.Sentiment,
                Pillar = assignment.Pillar,
                Headline = el.TryGetProperty("headline", out var h) ? h.GetString() ?? "" : "",
                Text = el.TryGetProperty("text", out var t) ? t.GetString() ?? "" : ""
            };
        }

        private string BuildThemePrompt(ThemeAssignment assignment, ProcessedDataset dataset)
        {
            var stats = dataset.PillarStats[assignment.Pillar];
            var templateName = assignment.Sentiment == "positive" ? "theme_positive.md" : "theme_negative.md";
            var template = LoadPromptTemplate(templateName);

            var signals = CollectSignalStrings(stats);
            var exemplars = SelectThemeExemplars(assignment.Sentiment, assignment.Pillar, signals);

            var values = new Dictionary<string, string>
            {
                ["pillar"] = assignment.Pillar,
                ["rationale"] = assignment.Rationale,
                ["corrected_score"] = $"{stats.CorrectedScore:F2}",
                ["response_count"] = stats.ResponseCount.ToString(),
                ["happy_count"] = stats.HappyCount.ToString(),
                ["indifferent_count"] = stats.IndifferentCount.ToString(),
                ["sad_count"] = stats.SadCount.ToString(),
                ["angry_count"] = stats.AngryCount.ToString(),
                ["status"] = stats.Status,
                ["gap_from_target"] = $"{stats.GapFromTarget:F2}",
                ["reason_text_block"] = FormatReasonTextBlock(stats),
                ["negative_reason_block"] = FormatNegativeReasonBlock(stats),
                ["free_text_block"] = FormatFreeTextBlock(stats),
                ["prior_period_context"] = "No prior period context provided.",
                ["framework_context_block"] = FormatPillarContextBlock(assignment.Pillar),
                ["examples_block"] = FormatThemeExemplarsBlock(exemplars)
            };

            return FillTemplate(template, values);
        }

        private static List<string> ValidateTheme(JsonElement parsed, string pillar)
        {
            var failures = new List<string>();
            if (!parsed.TryGetProperty("headline", out var headlineEl) || !parsed.TryGetProperty("text", out var textEl))
            {
                failures.Add("Response JSON is missing 'headline' or 'text'. Return both keys.");
                return failures;
            }

            var headline = headlineEl.GetString() ?? "";
            var text = textEl.GetString() ?? "";
            int wordCount = headline.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount < 5 || wordCount > 8)
                failures.Add($"Headline has {wordCount} words. Rewrite the headline so it is between 5 and 8 words.");

            // Reject descriptive headlines like "Pillar scores strongly" or "Pillar gap widens"
            var headlineDescriptive = $@"^\s*{Regex.Escape(pillar)}\s+(score|scored|scores|gap|climbs|falls|sits|rises|drops|declines|returns)";
            if (Regex.IsMatch(headline, headlineDescriptive, RegexOptions.IgnoreCase))
                failures.Add($"Headline opens with '{pillar} [verb]' which is descriptive, not a claim. Rewrite to make a specific claim about what is happening (e.g. 'A core of the team has internalised safety', not 'Vulnerability scores low').");

            var sentCount = CountSentences(text);
            if (sentCount < 3 || sentCount > 5)
                failures.Add($"Body has {sentCount} sentences. Rewrite the body as 3 to 5 complete sentences.");

            var parts = SentenceSplitRe.Split(text)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToArray();

            var firstTwoSents = string.Join(" ", parts.Take(2));
            var firstThreeSents = string.Join(" ", parts.Take(3));
            var firstSent = parts.FirstOrDefault() ?? "";

            if (!firstTwoSents.Contains(pillar, StringComparison.OrdinalIgnoreCase))
                failures.Add($"Pillar '{pillar}' must appear in the first two sentences. Rewrite so one of the first two sentences names '{pillar}' explicitly.");

            if (!Regex.IsMatch(firstThreeSents, @"\b\d"))
                failures.Add("No specific number found in the first three sentences. Include a corrected score, response count, or percentage in sentences 1-3.");

            // Reject score-readout openings: "Pillar scored X", "Pillar sits at X", etc.
            var scoreReadout = $@"^\s*{Regex.Escape(pillar)}\s+(scored|sits\s+at|registered|dropped|fell|declined|rose|climbed|returned|comes\s+in|sits\b)";
            if (Regex.IsMatch(firstSent, scoreReadout, RegexOptions.IgnoreCase))
                failures.Add("First sentence opens with a score readout. Lead with the finding or pattern, not the statistic. Put the number in sentence 2 or 3.");

            if (headline.Contains('—') || text.Contains('—'))
                failures.Add("Remove all em-dashes. Use colons or shorter sentences.");

            return failures;
        }

        private static int CountSentences(string s)
            => SentenceSplitRe.Split(s).Count(p => !string.IsNullOrWhiteSpace(p));

        // ─────────────────────────────────────────────────────────────────────
        // Stage 3: Recommendations
        // ─────────────────────────────────────────────────────────────────────

        private async Task<List<CloverEraRecommendationDto>> WriteRecommendationsAsync(
            ProcessedDataset dataset, List<WrittenTheme> themes, int count, ModelConfiguration model, CancellationToken ct)
        {
            var isAnthropic = (model.Endpoint ?? "").Contains("/anthropic/", StringComparison.OrdinalIgnoreCase);
            var thinkingBudget = isAnthropic ? Stage3ThinkingBudget : 0;

            var prompt = BuildRecommendationsPrompt(dataset, themes, count);
            var (raw, _, _, _) = await CallLlmAsync(_runSystemPrompt, prompt, model, T(0.8), MT(500), TP(), ct, thinkingBudget);

            var parsed = TryParseJson(raw);
            if (parsed == null)
            {
                var retryPrompt = prompt + "\n\n## Retry instruction\n\nYour previous response was not valid JSON. Return only the JSON object with a 'recommendations' array. No prose, no fences.";
                (raw, _, _, _) = await CallLlmAsync(_runSystemPrompt, retryPrompt, model, T(0.8), MT(500), TP(), ct, thinkingBudget);
                parsed = TryParseJson(raw);
            }

            if (parsed == null || !parsed.Value.TryGetProperty("recommendations", out var recsEl))
                return new List<CloverEraRecommendationDto>();

            var failures = ValidateRecommendations(parsed.Value, count);
            if (failures.Count > 0)
            {
                var retryPrompt = prompt + "\n\n## Retry instruction\n\nYour previous response failed validation. Fix these and return the same JSON structure:\n- " + string.Join("\n- ", failures);
                (raw, _, _, _) = await CallLlmAsync(_runSystemPrompt, retryPrompt, model, T(0.8), MT(500), TP(), ct, thinkingBudget);
                var retryParsed = TryParseJson(raw);
                if (retryParsed != null && retryParsed.Value.TryGetProperty("recommendations", out _))
                {
                    parsed = retryParsed;
                    _runQualityWarnings.Add($"Stage 3 (recommendations): required 1 retry: {string.Join("; ", failures)}");
                }
            }

            if (!parsed!.Value.TryGetProperty("recommendations", out recsEl))
                return new List<CloverEraRecommendationDto>();

            return recsEl.EnumerateArray()
                .Where(r => r.TryGetProperty("text", out _))
                .Select(r => new CloverEraRecommendationDto { Text = r.GetProperty("text").GetString() ?? "" })
                .ToList();
        }

        private string BuildRecommendationsPrompt(ProcessedDataset dataset, List<WrittenTheme> themes, int count)
        {
            var template = LoadPromptTemplate("recommendations.md");
            var focusRationale = string.Join("; ", dataset.FocusRecommendation.WhySelected);
            int pillarsWithData = dataset.PillarStats.Values.Count(s => s.ResponseCount > 0);

            var allSignals = themes.SelectMany(t => new[] { t.Headline, t.Text }).ToList();
            var recExemplars = SelectRecommendationExemplars(dataset.FocusRecommendation.Pillar, allSignals);

            var values = new Dictionary<string, string>
            {
                ["recommendation_count"] = count.ToString(),
                ["themes_block"] = FormatThemesBlock(themes),
                ["focus_element"] = dataset.FocusRecommendation.Pillar,
                ["focus_score"] = $"{dataset.FocusRecommendation.CorrectedScore:F2}",
                ["focus_gap"] = $"{dataset.FocusRecommendation.GapFromTarget:F2}",
                ["focus_rationale"] = focusRationale,
                ["total_responses"] = dataset.TotalResponses.ToString(),
                ["pillars_with_data"] = pillarsWithData.ToString(),
                ["overall_score"] = $"{dataset.OverallCorrectedScore:F2}",
                ["prior_period_context"] = "No prior period context provided.",
                ["framework_context_block"] = FormatPillarContextBlock(dataset.FocusRecommendation.Pillar),
                ["principles_block"] = FormatPrinciplesBlock(),
                ["examples_block"] = FormatRecommendationExemplarsBlock(recExemplars)
            };

            return FillTemplate(template, values);
        }

        private static List<string> ValidateRecommendations(JsonElement parsed, int expectedCount)
        {
            var failures = new List<string>();
            if (!parsed.TryGetProperty("recommendations", out var recs) || recs.ValueKind != JsonValueKind.Array)
                return ["Response must contain a 'recommendations' array."];

            var items = recs.EnumerateArray().ToList();
            if (items.Count != expectedCount)
                failures.Add($"Expected exactly {expectedCount} recommendations, got {items.Count}.");

            for (int i = 0; i < items.Count; i++)
            {
                if (!items[i].TryGetProperty("text", out var textEl)) { failures.Add($"Recommendation {i} missing 'text' field."); continue; }
                var text = textEl.GetString() ?? "";
                int words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                if (words < 30 || words > 100)
                    failures.Add($"Recommendation {i} is {words} words; rewrite to 30-100 words (roughly 2-3 sentences).");
                if (text.Contains('—'))
                    failures.Add($"Recommendation {i} contains em-dash; remove it.");
            }
            return failures;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Stage 4: Worksheet Selector
        // ─────────────────────────────────────────────────────────────────────

        private async Task<CloverEraWorksheetActionDto> SelectWorksheetAsync(
            ProcessedDataset dataset, List<WrittenTheme> themes, List<string> previousUsed, ModelConfiguration model, CancellationToken ct)
        {
            var focusPillar = dataset.FocusRecommendation.Pillar;
            if (string.IsNullOrEmpty(focusPillar) || !dataset.PillarStats.ContainsKey(focusPillar))
                return NoneFoundWorksheet(focusPillar ?? "(unknown)", previousUsed);

            var library = LoadWorksheetLibrary();
            var focusStats = dataset.PillarStats[focusPillar];
            var signals = CollectSignalStrings(focusStats);
            var previousSet = previousUsed.ToHashSet();

            var pillarMatch = library
                .Where(w => w.TryGetValue("pillar", out var p) && p?.ToString() == focusPillar)
                .OrderByDescending(w => ScoreCandidate(w, signals))
                .ThenBy(w => w.TryGetValue("code", out var c) ? c?.ToString() : "")
                .ToList();

            // Detect skipped primary
            var preExclusionTop = pillarMatch.Take(5).ToList();
            string? skippedCode = null, skippedTitle = null;
            foreach (var w in preExclusionTop)
            {
                if (w.TryGetValue("code", out var c) && previousSet.Contains(c?.ToString() ?? ""))
                {
                    skippedCode = c?.ToString();
                    skippedTitle = w.TryGetValue("title", out var t) ? t?.ToString() : "";
                    break;
                }
            }

            var candidates = pillarMatch
                .Where(w => !w.TryGetValue("code", out var c) || !previousSet.Contains(c?.ToString() ?? ""))
                .Take(5)
                .ToList();

            if (candidates.Count == 0)
            {
                var action = NoneFoundWorksheet(focusPillar, previousUsed);
                if (skippedCode != null)
                    action.SwapNote = $"{skippedCode} was a primary match for the {focusPillar} pattern but is in previous_worksheets_used. No other candidates available.";
                return action;
            }

            if (candidates.Count == 1)
            {
                var first = candidates[0];
                var firstCode = first.TryGetValue("code", out var c1) ? c1?.ToString() : "";
                var firstTitle = first.TryGetValue("title", out var t1) ? t1?.ToString() : "";
                var firstDuration = first.TryGetValue("duration_minutes", out var d1) ? Convert.ToInt32(d1) : 0;
                return new CloverEraWorksheetActionDto
                {
                    WorksheetCode = firstCode ?? "",
                    WorksheetTitle = firstTitle ?? "",
                    DurationMinutes = firstDuration,
                    Text = $"Facilitate {firstTitle} with the team this period. Only one eligible worksheet remained in the library for {focusPillar} after exclusions, so selection was deterministic. Review the worksheet's facilitation notes before running the session.",
                    SuccessMetric = "Watch for movement in the focus pillar's corrected score next period.",
                    SwapNote = skippedCode != null ? $"{skippedCode} was a primary match for the {focusPillar} pattern but is in previous_worksheets_used. Selected {firstCode} instead." : null
                };
            }

            // 2+ candidates → LLM
            var prompt = BuildWorksheetSelectionPrompt(dataset, themes, candidates);
            var (raw, _, _, _) = await CallLlmAsync(_runSystemPrompt, prompt, model, T(0.4), MT(600), TP(), ct);
            var parsed = TryParseJson(raw);

            if (parsed == null)
            {
                var retryPrompt = prompt + "\n\n## Retry instruction\n\nYour previous response was not valid JSON. Return only the JSON object. No prose, no fences.";
                (raw, _, _, _) = await CallLlmAsync(_runSystemPrompt, retryPrompt, model, T(0.4), MT(600), TP(), ct);
                parsed = TryParseJson(raw);
            }

            if (parsed == null)
            {
                var first = candidates[0];
                var fc = first.TryGetValue("code", out var c2) ? c2?.ToString() : "";
                return new CloverEraWorksheetActionDto { WorksheetCode = fc ?? "", WorksheetTitle = first.TryGetValue("title", out var t2) ? t2?.ToString() ?? "" : "", DurationMinutes = first.TryGetValue("duration_minutes", out var d2) ? Convert.ToInt32(d2) : 0, Text = $"Facilitate {(first.TryGetValue("title", out var t3) ? t3?.ToString() : "")} with the team this period.", SuccessMetric = "Watch for movement in the focus pillar next period." };
            }

            var candidateCodes = candidates.Select(c => c.TryGetValue("code", out var code) ? code?.ToString() : "").ToHashSet();
            var selFailures = ValidateWorksheetSelection(parsed.Value, candidateCodes!);
            if (selFailures.Count > 0)
            {
                var retryPrompt = prompt + "\n\n## Retry instruction\n\nYour previous response failed validation. Fix these and return the same JSON structure:\n- " + string.Join("\n- ", selFailures);
                (raw, _, _, _) = await CallLlmAsync(_runSystemPrompt, retryPrompt, model, T(0.4), MT(600), TP(), ct);
                var retryParsed = TryParseJson(raw);
                if (retryParsed != null)
                {
                    var retryCode = retryParsed.Value.TryGetProperty("worksheet_code", out var rc) ? rc.GetString() : "";
                    if (candidateCodes.Contains(retryCode ?? ""))
                        parsed = retryParsed;
                    else
                    {
                        // Hard fallback to first candidate
                        var first = candidates[0];
                        return new CloverEraWorksheetActionDto
                        {
                            WorksheetCode = first.TryGetValue("code", out var c3) ? c3?.ToString() ?? "" : "",
                            WorksheetTitle = first.TryGetValue("title", out var t4) ? t4?.ToString() ?? "" : "",
                            DurationMinutes = first.TryGetValue("duration_minutes", out var d3) ? Convert.ToInt32(d3) : 0,
                            Text = retryParsed.Value.TryGetProperty("text", out var rt) ? rt.GetString() ?? "" : $"Facilitate {(first.TryGetValue("title", out var t5) ? t5?.ToString() : "")} with the team this period.",
                            SuccessMetric = retryParsed.Value.TryGetProperty("success_metric", out var rsm) ? rsm.GetString() ?? "" : "Watch for movement in the focus pillar next period."
                        };
                    }
                }
            }

            var code2 = parsed!.Value.TryGetProperty("worksheet_code", out var wc) ? wc.GetString() ?? "" : "";
            var title2 = parsed!.Value.TryGetProperty("worksheet_title", out var wt) ? wt.GetString() ?? "" : "";
            var duration2 = parsed!.Value.TryGetProperty("duration_minutes", out var wd) ? wd.GetInt32() : 0;
            var text2 = parsed!.Value.TryGetProperty("text", out var wtxt) ? wtxt.GetString() ?? "" : "";
            var metric2 = parsed!.Value.TryGetProperty("success_metric", out var wsm) ? wsm.GetString() ?? "" : "";

            return new CloverEraWorksheetActionDto
            {
                WorksheetCode = code2,
                WorksheetTitle = title2,
                DurationMinutes = duration2,
                Text = text2,
                SuccessMetric = metric2,
                SwapNote = skippedCode != null ? $"{skippedCode} was a primary match for the {focusPillar} pattern but is in previous_worksheets_used. Selected {code2} instead." : null
            };
        }

        private static CloverEraWorksheetActionDto NoneFoundWorksheet(string focusPillar, List<string> previousUsed)
        {
            var previousStr = previousUsed.Count > 0 ? string.Join(", ", previousUsed) : "(none)";
            return new CloverEraWorksheetActionDto
            {
                WorksheetCode = "NONE_FOUND",
                WorksheetTitle = "No match in library",
                DurationMinutes = 0,
                Text = $"No worksheet in the library matched the focus element {focusPillar} after excluding previously used worksheets [{previousStr}]. Recommend a custom session or reuse an older worksheet with care.",
                SuccessMetric = "N/A"
            };
        }

        private static List<string> CollectSignalStrings(PillarStats stats)
        {
            var signals = new List<string>();
            signals.AddRange(stats.NegativeReasonTexts);
            signals.AddRange(stats.ReasonTextCounts.Keys);
            foreach (var ft in stats.FreeTextResponses)
            {
                if (ft.TryGetValue("free_text", out var t) && !string.IsNullOrWhiteSpace(t))
                    signals.Add(t);
            }
            return signals;
        }

        private static int ScoreCandidate(Dictionary<string, object?> candidate, List<string> signals)
        {
            var candTokens = new HashSet<string>();
            if (candidate.TryGetValue("best_for_patterns", out var bfp) && bfp is JsonElement bfpEl)
            {
                foreach (var p in bfpEl.EnumerateArray())
                {
                    if (p.ValueKind == JsonValueKind.String)
                        foreach (var w in WordRe.Matches(p.GetString() ?? "").Select(m => m.Value.ToLower()))
                            if (!Stopwords.Contains(w)) candTokens.Add(w);
                }
            }
            var signalTokens = new HashSet<string>();
            foreach (var s in signals)
                foreach (var w in WordRe.Matches(s).Select(m => m.Value.ToLower()))
                    if (!Stopwords.Contains(w)) signalTokens.Add(w);
            return candTokens.Intersect(signalTokens).Count();
        }

        private string BuildWorksheetSelectionPrompt(ProcessedDataset dataset, List<WrittenTheme> themes, List<Dictionary<string, object?>> candidates)
        {
            var template = LoadPromptTemplate("worksheet_selection.md");
            var focusPillar = dataset.FocusRecommendation.Pillar;
            var focusStats = dataset.PillarStats[focusPillar];
            var focusTheme = themes.FirstOrDefault(t => t.Sentiment == "negative" && t.Pillar == focusPillar);

            var values = new Dictionary<string, string>
            {
                ["candidate_count"] = candidates.Count.ToString(),
                ["focus_pillar"] = focusPillar,
                ["focus_score"] = $"{dataset.FocusRecommendation.CorrectedScore:F2}",
                ["focus_gap"] = $"{dataset.FocusRecommendation.GapFromTarget:F2}",
                ["reason_text_block"] = FormatReasonTextBlockWithNeg(focusStats),
                ["free_text_block"] = FormatFreeTextBlock(focusStats),
                ["focus_pillar_headline"] = focusTheme?.Headline ?? "(no focus theme written)",
                ["focus_pillar_theme"] = focusTheme?.Text ?? "(no focus theme body)",
                ["candidate_block"] = FormatCandidateBlock(candidates)
            };

            return FillTemplate(template, values);
        }

        private static List<string> ValidateWorksheetSelection(JsonElement parsed, HashSet<string> candidateCodes)
        {
            var failures = new List<string>();
            var required = new[] { "worksheet_code", "worksheet_title", "duration_minutes", "text", "success_metric" };
            foreach (var k in required)
                if (!parsed.TryGetProperty(k, out _))
                    failures.Add($"Missing '{k}' field.");

            if (failures.Count > 0) return failures;

            var code = parsed.TryGetProperty("worksheet_code", out var c) ? c.GetString() ?? "" : "";
            if (!candidateCodes.Contains(code))
                failures.Add($"Chosen worksheet_code '{code}' is not in the candidate list. Pick one of the provided codes.");

            var text = parsed.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            if (CountSentences(text) < 3 || CountSentences(text) > 5)
                failures.Add("Framing text must be 3-5 sentences.");
            if (text.Contains('—'))
                failures.Add("Remove em-dashes.");

            return failures;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Stage 5: Micro Actions
        // ─────────────────────────────────────────────────────────────────────

        private async Task<List<CloverEraMicroActionDto>> WriteMicroActionsAsync(
            ProcessedDataset dataset, List<WrittenTheme> themes, CloverEraWorksheetActionDto worksheetAction, bool escalationPresent, ModelConfiguration model, CancellationToken ct)
        {
            int actionCount = escalationPresent ? 1 : 2;
            var library = LoadMicroActionsLibrary();
            var inspiration = SelectLibraryInspiration(dataset, library, 3);

            var prompt = BuildMicroActionsPrompt(dataset, themes, worksheetAction, actionCount, inspiration);
            var (raw, _, _, _) = await CallLlmAsync(_runSystemPrompt, prompt, model, T(0.8), MT(500), TP(), ct);

            var parsed = TryParseJson(raw);
            if (parsed == null)
            {
                var retryPrompt = prompt + "\n\n## Retry instruction\n\nYour previous response was not valid JSON. Return only the JSON object with 'micro_actions' array. No prose, no fences.";
                (raw, _, _, _) = await CallLlmAsync(_runSystemPrompt, retryPrompt, model, T(0.8), MT(500), TP(), ct);
                parsed = TryParseJson(raw);
            }

            if (parsed == null || !parsed.Value.TryGetProperty("micro_actions", out var actionsEl))
                return new List<CloverEraMicroActionDto>();

            var failures = ValidateMicroActions(parsed.Value, actionCount);
            if (failures.Count > 0)
            {
                var retryPrompt = prompt + "\n\n## Retry instruction\n\nYour previous response failed validation. Fix these and return the same JSON structure:\n- " + string.Join("\n- ", failures);
                (raw, _, _, _) = await CallLlmAsync(_runSystemPrompt, retryPrompt, model, T(0.8), MT(500), TP(), ct);
                var retryParsed = TryParseJson(raw);
                if (retryParsed != null && retryParsed.Value.TryGetProperty("micro_actions", out _))
                {
                    parsed = retryParsed;
                    _runQualityWarnings.Add($"Stage 5 (micro-actions): required 1 retry: {string.Join("; ", failures)}");
                }
            }

            if (!parsed!.Value.TryGetProperty("micro_actions", out actionsEl))
                return new List<CloverEraMicroActionDto>();

            return actionsEl.EnumerateArray()
                .Select(m => new CloverEraMicroActionDto
                {
                    DurationMinutes = m.TryGetProperty("duration_minutes", out var d) ? d.GetInt32() : 0,
                    Text = m.TryGetProperty("text", out var t) ? t.GetString() ?? "" : ""
                })
                .ToList();
        }

        private string BuildMicroActionsPrompt(ProcessedDataset dataset, List<WrittenTheme> themes, CloverEraWorksheetActionDto worksheetAction, int actionCount, List<Dictionary<string, object?>> inspiration)
        {
            var template = LoadPromptTemplate("micro_actions.md");
            var values = new Dictionary<string, string>
            {
                ["action_count"] = actionCount.ToString(),
                ["worksheet_summary"] = $"{worksheetAction.WorksheetCode}: {worksheetAction.WorksheetTitle} ({worksheetAction.DurationMinutes} min). {worksheetAction.Text}",
                ["themes_block"] = FormatThemesBlock(themes),
                ["negative_signals_block"] = FormatNegativeSignalsBlock(dataset),
                ["library_inspiration_block"] = FormatLibraryInspiration(inspiration)
            };
            return FillTemplate(template, values);
        }

        private static List<string> ValidateMicroActions(JsonElement parsed, int expectedCount)
        {
            var failures = new List<string>();
            if (!parsed.TryGetProperty("micro_actions", out var items) || items.ValueKind != JsonValueKind.Array)
                return ["Response must contain a 'micro_actions' array."];

            var list = items.EnumerateArray().ToList();
            if (list.Count != expectedCount)
                failures.Add($"Expected exactly {expectedCount} micro-action(s), got {list.Count}.");

            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].TryGetProperty("duration_minutes", out var dur) || !list[i].TryGetProperty("text", out var txt))
                { failures.Add($"Micro-action {i} missing required fields."); continue; }
                int duration = dur.GetInt32();
                if (duration < 2 || duration > 10)
                    failures.Add($"Micro-action {i} duration is {duration}; must be 2 to 10 minutes.");
                var text = txt.GetString() ?? "";
                var sc = CountSentences(text);
                if (sc < 2 || sc > 4)
                    failures.Add($"Micro-action {i} has {sc} sentences; rewrite to 2-4 sentences.");
                if (text.Contains('—'))
                    failures.Add($"Micro-action {i} contains an em-dash; remove it.");
            }
            return failures;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Library loading and scoring
        // ─────────────────────────────────────────────────────────────────────

        private List<Dictionary<string, object?>> LoadWorksheetLibrary()
        {
            var path = Path.Combine(_env.ContentRootPath, "CloverEra", "Data", "worksheet_library.json");
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var worksheets = root.TryGetProperty("worksheets", out var ws) ? ws : root;

            return worksheets.EnumerateArray()
                .Select(w => {
                    var dict = new Dictionary<string, object?>();
                    foreach (var p in w.EnumerateObject())
                        dict[p.Name] = p.Value.Clone();
                    return dict;
                })
                .ToList();
        }

        private Dictionary<string, List<Dictionary<string, object?>>> LoadMicroActionsLibrary()
        {
            var path = Path.Combine(_env.ContentRootPath, "CloverEra", "Data", "micro_actions_library.json");
            if (!File.Exists(path)) return new();
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);

            var result = new Dictionary<string, List<Dictionary<string, object?>>>();
            foreach (var pillarProp in doc.RootElement.EnumerateObject())
            {
                var entries = pillarProp.Value.EnumerateArray()
                    .Select(e => {
                        var dict = new Dictionary<string, object?>();
                        foreach (var p in e.EnumerateObject())
                            dict[p.Name] = p.Value.Clone();
                        return dict;
                    })
                    .ToList();
                result[pillarProp.Name] = entries;
            }
            return result;
        }

        private List<Dictionary<string, object?>> SelectLibraryInspiration(ProcessedDataset dataset, Dictionary<string, List<Dictionary<string, object?>>> library, int limit)
        {
            var focusPillar = dataset.FocusRecommendation.Pillar;
            var focusStats = dataset.PillarStats.TryGetValue(focusPillar, out var fs) ? fs : null;
            var focusSignals = focusStats != null ? CollectSignalStrings(focusStats) : new List<string>();

            var primary = RankLibraryEntries(library.GetValueOrDefault(focusPillar, new()), focusSignals, limit);
            if (primary.Count >= limit) return primary;

            var allSignals = new List<string>();
            foreach (var (pillar, stats) in dataset.PillarStats)
                if (IsNegativeCandidate(stats))
                    allSignals.AddRange(CollectSignalStrings(stats));

            var excludeIds = primary.Select(e => e.TryGetValue("id", out var id) ? id?.ToString() : null).ToHashSet();
            var pool = library
                .Where(kv => kv.Key != focusPillar)
                .SelectMany(kv => kv.Value)
                .Where(e => !excludeIds.Contains(e.TryGetValue("id", out var id) ? id?.ToString() : null))
                .ToList();
            var topUp = RankLibraryEntries(pool, allSignals, limit - primary.Count);
            return primary.Concat(topUp).ToList();
        }

        private static List<Dictionary<string, object?>> RankLibraryEntries(List<Dictionary<string, object?>> entries, List<string> signals, int limit)
        {
            if (entries.Count == 0) return new();
            var signalTokens = new HashSet<string>();
            foreach (var s in signals)
                foreach (var w in WordRe.Matches(s).Select(m => m.Value.ToLower()))
                    if (!Stopwords.Contains(w)) signalTokens.Add(w);

            return entries
                .Select(e => {
                    var issueTokens = new HashSet<string>();
                    if (e.TryGetValue("issue", out var issue) && issue is JsonElement issueEl && issueEl.ValueKind == JsonValueKind.String)
                        foreach (var w in WordRe.Matches(issueEl.GetString() ?? "").Select(m => m.Value.ToLower()))
                            if (!Stopwords.Contains(w)) issueTokens.Add(w);
                    return (Score: issueTokens.Intersect(signalTokens).Count(), Entry: e);
                })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Entry.TryGetValue("id", out var id) ? id?.ToString() : "")
                .Take(limit)
                .Select(x => x.Entry)
                .ToList();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Stage 6: Assemble
        // ─────────────────────────────────────────────────────────────────────

        private static CloverEraCompleteReportDto Assemble(
            ProcessedDataset dataset,
            List<WrittenTheme> themes,
            List<CloverEraRecommendationDto> recommendations,
            CloverEraWorksheetActionDto worksheetAction,
            List<CloverEraMicroActionDto> microActions,
            CloverEraEscalationActionDto? escalation)
        {
            var pillarStatsDto = dataset.PillarStats.ToDictionary(
                kv => kv.Key,
                kv => new CloverEraPillarStatsDto
                {
                    Pillar = kv.Value.Pillar,
                    ResponseCount = kv.Value.ResponseCount,
                    HappyCount = kv.Value.HappyCount,
                    IndifferentCount = kv.Value.IndifferentCount,
                    SadCount = kv.Value.SadCount,
                    AngryCount = kv.Value.AngryCount,
                    RawScore = kv.Value.RawScore,
                    CorrectedScore = kv.Value.CorrectedScore,
                    GapFromTarget = kv.Value.GapFromTarget,
                    Status = kv.Value.Status,
                    ConfidenceWeight = kv.Value.ConfidenceWeight,
                    FocusPriorityScore = kv.Value.FocusPriorityScore,
                    ReasonTextCounts = kv.Value.ReasonTextCounts,
                    FreeTextResponses = kv.Value.FreeTextResponses,
                    NegativeReasonTexts = kv.Value.NegativeReasonTexts
                });

            return new CloverEraCompleteReportDto
            {
                BusinessUnit = dataset.BusinessUnit,
                Company = dataset.Profile,
                PeriodStart = dataset.PeriodStart,
                PeriodEnd = dataset.PeriodEnd,
                TotalResponses = dataset.TotalResponses,
                OverallRawScore = dataset.OverallRawScore,
                OverallCorrectedScore = dataset.OverallCorrectedScore,
                PillarStats = pillarStatsDto,
                FocusRecommendation = new CloverEraFocusRecommendationDto
                {
                    Pillar = dataset.FocusRecommendation.Pillar,
                    CorrectedScore = dataset.FocusRecommendation.CorrectedScore,
                    GapFromTarget = dataset.FocusRecommendation.GapFromTarget,
                    ConfidenceScore = dataset.FocusRecommendation.ConfidenceScore,
                    WhySelected = dataset.FocusRecommendation.WhySelected,
                    SecondaryPillar = dataset.FocusRecommendation.SecondaryPillar,
                    SecondaryRationale = dataset.FocusRecommendation.SecondaryRationale
                },
                Themes = themes.Select(t => new CloverEraWrittenThemeDto
                {
                    ThemeIndex = t.ThemeIndex,
                    Sentiment = t.Sentiment,
                    Pillar = t.Pillar,
                    Headline = t.Headline,
                    Text = t.Text
                }).ToList(),
                Recommendations = recommendations,
                WorksheetAction = worksheetAction,
                MicroActions = microActions,
                EscalationAction = escalation,
                DataQualityIssues = dataset.DataQualityIssues,
                FreeTextCompletionRate = dataset.FreeTextCompletionRate,
                LowSamplePillars = dataset.LowSamplePillars
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        // Formatting helpers
        // ─────────────────────────────────────────────────────────────────────

        private static string FormatReasonTextBlock(PillarStats stats)
        {
            if (stats.ReasonTextCounts.Count == 0) return "(none)";
            return string.Join("\n", stats.ReasonTextCounts.Select(kv => $"- {kv.Value}x: {kv.Key}"));
        }

        private static string FormatReasonTextBlockWithNeg(PillarStats stats)
        {
            if (stats.ReasonTextCounts.Count == 0) return "(none)";
            return string.Join("\n", stats.ReasonTextCounts.Select(kv => {
                var neg = stats.NegativeReasonTexts.Contains(kv.Key) ? " (negative)" : "";
                return $"- {kv.Value}x: {kv.Key}{neg}";
            }));
        }

        private static string FormatNegativeReasonBlock(PillarStats stats)
        {
            if (stats.NegativeReasonTexts.Count == 0) return "(none)";
            return string.Join("\n", stats.NegativeReasonTexts.Select(t => $"- {t}"));
        }

        private static string FormatFreeTextBlock(PillarStats stats)
        {
            if (stats.FreeTextResponses.Count == 0) return "(none provided this period)";
            return string.Join("\n", stats.FreeTextResponses.Select(ft =>
                $"- [{ft.GetValueOrDefault("sentiment", "")}] ({ft.GetValueOrDefault("reason_text", "")}) {ft.GetValueOrDefault("free_text", "")}"));
        }

        private static string FormatThemesBlock(List<WrittenTheme> themes)
        {
            if (themes.Count == 0) return "(no themes generated)";
            var lines = new List<string>();
            foreach (var t in themes)
            {
                lines.Add($"- [{t.Sentiment.ToUpper()}] {t.Pillar} | {t.Headline}");
                lines.Add($"  {t.Text}");
            }
            return string.Join("\n", lines);
        }

        private static string FormatNegativeSignalsBlock(ProcessedDataset dataset)
        {
            var rows = new List<string>();
            foreach (var (pillar, stats) in dataset.PillarStats)
            {
                if (stats.ResponseCount == 0) continue;
                if (stats.SadCount == 0 && stats.AngryCount == 0 && stats.IndifferentCount < stats.ResponseCount) continue;
                var signals = CollectSignalStrings(stats);
                if (signals.Count == 0) continue;
                rows.Add($"- {pillar} ({stats.CorrectedScore:F1} corrected, gap {stats.GapFromTarget:F1}, Happy/Indiff/Sad/Angry = {stats.HappyCount}/{stats.IndifferentCount}/{stats.SadCount}/{stats.AngryCount}):");
                foreach (var s in signals.Take(4))
                    rows.Add($"    - {s}");
            }
            return rows.Count > 0 ? string.Join("\n", rows) : "(no negative signals surfaced this period)";
        }

        private static string FormatCandidateBlock(List<Dictionary<string, object?>> candidates)
        {
            if (candidates.Count == 0) return "(none)";
            var blocks = new List<string>();
            foreach (var c in candidates)
            {
                var code = c.TryGetValue("code", out var co) && co is JsonElement coEl ? coEl.GetString() : "";
                var title = c.TryGetValue("title", out var ti) && ti is JsonElement tiEl ? tiEl.GetString() : "";
                var duration = c.TryGetValue("duration_minutes", out var du) && du is JsonElement duEl ? duEl.GetInt32() : 0;
                var summary = c.TryGetValue("summary", out var su) && su is JsonElement suEl ? suEl.GetString() : "";
                var patterns = "";
                if (c.TryGetValue("best_for_patterns", out var bfp) && bfp is JsonElement bfpEl)
                    patterns = string.Join(", ", bfpEl.EnumerateArray().Take(6).Select(p => p.GetString() ?? ""));
                blocks.Add($"- {code} | {title} ({duration} min)\n  summary: {summary}\n  best for: {patterns}");
            }
            return string.Join("\n", blocks);
        }

        private static string FormatLibraryInspiration(List<Dictionary<string, object?>> entries)
        {
            if (entries.Count == 0) return "(library had no close matches for this period's signals)";
            var lines = new List<string>();
            foreach (var e in entries)
            {
                var issue = e.TryGetValue("issue", out var iss) && iss is JsonElement issEl ? issEl.GetString() : "";
                var id = e.TryGetValue("id", out var idV) && idV is JsonElement idEl ? idEl.GetString() : "";
                lines.Add($"- Issue: {issue} (from {id})");
                if (e.TryGetValue("actions", out var acts) && acts is JsonElement actsEl)
                    foreach (var a in actsEl.EnumerateArray().Take(3))
                        lines.Add($"    * {a.GetString()}");
            }
            return string.Join("\n", lines);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Template loading and filling
        // ─────────────────────────────────────────────────────────────────────

        private string LoadPromptTemplate(string fileName)
        {
            var path = Path.Combine(_env.ContentRootPath, "CloverEra", "Prompts", fileName);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string FillTemplate(string template, Dictionary<string, string> values)
        {
            var result = template;
            foreach (var (key, value) in values)
                result = result.Replace("{" + key + "}", value);
            result = result.Replace("{{", "{").Replace("}}", "}");
            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Knowledge base: pillar context
        // ─────────────────────────────────────────────────────────────────────

        private JsonDocument? _pillarContextDoc;

        private JsonDocument LoadPillarContextDoc()
        {
            if (_pillarContextDoc != null) return _pillarContextDoc;
            var path = Path.Combine(_env.ContentRootPath, "CloverEra", "Data", "knowledge", "pillar_context.json");
            if (!File.Exists(path)) return null!;
            _pillarContextDoc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            return _pillarContextDoc;
        }

        private string FormatPillarContextBlock(string pillar)
        {
            try
            {
                var doc = LoadPillarContextDoc();
                if (doc == null || !doc.RootElement.TryGetProperty("pillars", out var pillarsEl)
                    || !pillarsEl.TryGetProperty(pillar, out var p)) return "";

                var sb = new StringBuilder();
                sb.AppendLine($"## Framework context for {pillar}");
                sb.AppendLine();
                if (p.TryGetProperty("framework_definition", out var def))
                    sb.AppendLine($"**Definition.** {def.GetString()}");
                sb.AppendLine();
                if (p.TryGetProperty("clover_principle", out var pr))
                    sb.AppendLine($"**CLOVER principle.** {pr.GetString()}");
                sb.AppendLine();
                if (p.TryGetProperty("neuroscience_mechanism", out var ns))
                    sb.AppendLine($"**Neuroscience.** {ns.GetString()}");
                sb.AppendLine();
                if (p.TryGetProperty("what_goes_wrong", out var wgw))
                {
                    sb.AppendLine($"**What goes wrong when {pillar} is low:**");
                    foreach (var item in wgw.EnumerateArray())
                        sb.AppendLine($"- {item.GetString()}");
                }
                sb.AppendLine();
                if (p.TryGetProperty("what_good_looks_like", out var wgl))
                {
                    sb.AppendLine("**What good looks like:**");
                    foreach (var item in wgl.EnumerateArray())
                        sb.AppendLine($"- {item.GetString()}");
                }
                sb.AppendLine();
                sb.AppendLine("Use this context to ground your analysis and vocabulary. Do NOT copy sentences from this section verbatim.");
                return sb.ToString().TrimEnd();
            }
            catch { return ""; }
        }

        private string FormatPrinciplesBlock()
        {
            try
            {
                var doc = LoadPillarContextDoc();
                if (doc == null || !doc.RootElement.TryGetProperty("framework_principles", out var fp)) return "";

                var sb = new StringBuilder();
                sb.AppendLine("## Cross-pillar principles");
                foreach (var prop in fp.EnumerateObject())
                {
                    var title = System.Globalization.CultureInfo.CurrentCulture.TextInfo
                        .ToTitleCase(prop.Name.Replace("_", " "));
                    sb.AppendLine();
                    sb.AppendLine($"**{title}.** {prop.Value.GetString()}");
                }
                return sb.ToString().TrimEnd();
            }
            catch { return ""; }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Exemplar selection
        // ─────────────────────────────────────────────────────────────────────

        private record ExemplarEntry(string Id, string Pillar, string Headline, string Body, string[] Pillars);

        private List<ExemplarEntry>? _positiveExemplars;
        private List<ExemplarEntry>? _negativeExemplars;
        private List<ExemplarEntry>? _recExemplars;

        private List<ExemplarEntry> LoadExemplars(string type)
        {
            var file = type == "positive" ? "themes_positive.json"
                     : type == "negative" ? "themes_negative.json"
                     : "recommendations.json";
            var path = Path.Combine(_env.ContentRootPath, "CloverEra", "Data", "exemplars", file);
            if (!File.Exists(path)) return new();
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var result = new List<ExemplarEntry>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var id = el.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
                var pillar = el.TryGetProperty("pillar", out var p) ? p.GetString() ?? "" : "";
                var headline = el.TryGetProperty("headline", out var h) ? h.GetString() ?? "" : "";
                var body = el.TryGetProperty("body", out var b) ? b.GetString() ?? ""
                         : el.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                var pillars = el.TryGetProperty("pillars", out var pa) && pa.ValueKind == JsonValueKind.Array
                    ? pa.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                    : new[] { pillar };
                result.Add(new ExemplarEntry(id, pillar, headline, body, pillars));
            }
            return result;
        }

        private static HashSet<string> Tokenise(string text) =>
            new HashSet<string>(
                WordRe.Matches(text).Select(m => m.Value.ToLower()).Where(w => !Stopwords.Contains(w)),
                StringComparer.OrdinalIgnoreCase);

        private static bool PairIsRedundant(ExemplarEntry a, ExemplarEntry b)
        {
            var ta = Tokenise(a.Body + " " + a.Headline);
            var tb = Tokenise(b.Body + " " + b.Headline);
            if (ta.Count == 0 || tb.Count == 0) return false;
            var intersect = ta.Intersect(tb).Count();
            var jaccard = (double)intersect / (ta.Count + tb.Count - intersect);
            var sameOpener = a.Body.Split(' ').FirstOrDefault()?.ToLower() ==
                             b.Body.Split(' ').FirstOrDefault()?.ToLower();
            return jaccard > 0.70 || sameOpener;
        }

        private List<ExemplarEntry> SelectExemplars(List<ExemplarEntry> pool, string pillar, List<string> signals, int k = 2)
        {
            if (pool.Count == 0) return new();
            var signalTokens = Tokenise(string.Join(" ", signals));

            var ranked = pool
                .Select(e => new
                {
                    Entry = e,
                    PillarMatch = e.Pillars.Contains(pillar, StringComparer.OrdinalIgnoreCase) ? 1 : 0,
                    Overlap = Tokenise(e.Body + " " + e.Headline).Intersect(signalTokens).Count()
                })
                .OrderByDescending(x => x.PillarMatch)
                .ThenByDescending(x => x.Overlap)
                .Select(x => x.Entry)
                .ToList();

            var selected = new List<ExemplarEntry>();
            foreach (var candidate in ranked)
            {
                if (selected.Count >= k) break;
                if (selected.All(s => !PairIsRedundant(s, candidate)))
                    selected.Add(candidate);
            }
            return selected;
        }

        private List<ExemplarEntry> SelectThemeExemplars(string sentiment, string pillar, List<string> signals)
        {
            if (sentiment == "positive")
            {
                _positiveExemplars ??= LoadExemplars("positive");
                return SelectExemplars(_positiveExemplars, pillar, signals, 2);
            }
            _negativeExemplars ??= LoadExemplars("negative");
            return SelectExemplars(_negativeExemplars, pillar, signals, 2);
        }

        private List<ExemplarEntry> SelectRecommendationExemplars(string focusPillar, List<string> signals)
        {
            _recExemplars ??= LoadExemplars("recommendations");
            return SelectExemplars(_recExemplars, focusPillar, signals, 3);
        }

        private static string FormatThemeExemplarsBlock(List<ExemplarEntry> exemplars)
        {
            if (exemplars.Count == 0) return "(no exemplars available)";
            var sb = new StringBuilder();
            foreach (var ex in exemplars)
            {
                sb.AppendLine($"Headline: {ex.Headline}");
                sb.AppendLine();
                sb.AppendLine($"Body: {ex.Body}");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        private static string FormatRecommendationExemplarsBlock(List<ExemplarEntry> exemplars)
        {
            if (exemplars.Count == 0) return "(no exemplars available)";
            var sb = new StringBuilder();
            for (int i = 0; i < exemplars.Count; i++)
                sb.AppendLine($"{i + 1}. {exemplars[i].Body}").AppendLine();
            return sb.ToString().TrimEnd();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Stage 5.5: Synthesis pass
        // ─────────────────────────────────────────────────────────────────────

        private async Task<(List<WrittenTheme>, List<CloverEraRecommendationDto>, CloverEraWorksheetActionDto,
            List<CloverEraMicroActionDto>, CloverEraEscalationActionDto?)> RunSynthesisPassAsync(
            List<WrittenTheme> themes,
            List<CloverEraRecommendationDto> recommendations,
            CloverEraWorksheetActionDto worksheet,
            List<CloverEraMicroActionDto> microActions,
            CloverEraEscalationActionDto? escalation,
            ModelConfiguration model,
            CancellationToken ct)
        {
            var prompt = BuildSynthesisPrompt(themes, recommendations, worksheet, microActions, escalation);
            var (raw, _, _, _) = await CallLlmAsync(_runSystemPrompt, prompt, model, T(0.7), MT(2000), TP(), ct);

            var parsed = TryParseJson(raw);
            if (parsed == null)
            {
                _runQualityWarnings.Add("Stage 5.5 (synthesis): LLM did not return valid JSON; original pieces used.");
                return (themes, recommendations, worksheet, microActions, escalation);
            }

            var changes = new List<string>();

            // Apply theme rewrites
            if (parsed.Value.TryGetProperty("themes", out var themesEl))
            {
                foreach (var t in themesEl.EnumerateArray())
                {
                    if (!t.TryGetProperty("index", out var idxEl)) continue;
                    int idx = idxEl.GetInt32();
                    var match = themes.FirstOrDefault(th => th.ThemeIndex == idx);
                    if (match == null) continue;

                    var newHeadline = t.TryGetProperty("headline", out var nh) ? nh.GetString() ?? match.Headline : match.Headline;
                    var newText = t.TryGetProperty("text", out var nt) ? nt.GetString() ?? match.Text : match.Text;

                    var headlineViolations = ValidateSynthesisRewrite(match.Headline, newHeadline);
                    var textViolations = ValidateSynthesisRewrite(match.Text, newText);

                    if (headlineViolations.Count == 0 && textViolations.Count == 0 &&
                        (newHeadline != match.Headline || newText != match.Text))
                    {
                        match.Headline = newHeadline;
                        match.Text = newText;
                        changes.Add($"Theme {idx} ({match.Pillar}): rewritten to reduce repetition.");
                    }
                    else if (headlineViolations.Count > 0 || textViolations.Count > 0)
                    {
                        _runQualityWarnings.Add($"Stage 5.5 (synthesis): Theme {idx} rewrite dropped due to fact violations: {string.Join(", ", headlineViolations.Concat(textViolations))}");
                    }
                }
            }

            // Apply recommendation rewrites
            if (parsed.Value.TryGetProperty("recommendations", out var recsEl))
            {
                var recList = recommendations.ToList();
                foreach (var r in recsEl.EnumerateArray())
                {
                    if (!r.TryGetProperty("index", out var idxEl)) continue;
                    int idx = idxEl.GetInt32();
                    if (idx >= recList.Count) continue;

                    var newText = r.TryGetProperty("text", out var nt) ? nt.GetString() ?? recList[idx].Text : recList[idx].Text;
                    var violations = ValidateSynthesisRewrite(recList[idx].Text, newText);
                    if (violations.Count == 0 && newText != recList[idx].Text)
                    {
                        recList[idx] = new CloverEraRecommendationDto { Text = newText };
                        changes.Add($"Recommendation {idx + 1}: rewritten to reduce repetition.");
                    }
                    else if (violations.Count > 0)
                    {
                        _runQualityWarnings.Add($"Stage 5.5 (synthesis): Rec {idx + 1} rewrite dropped: {string.Join(", ", violations)}");
                    }
                }
                recommendations = recList;
            }

            // Apply worksheet text rewrite (never touch success_metric)
            if (parsed.Value.TryGetProperty("worksheet_text", out var wtEl))
            {
                var newText = wtEl.GetString() ?? worksheet.Text;
                var violations = ValidateSynthesisRewrite(worksheet.Text, newText);
                if (violations.Count == 0 && newText != worksheet.Text)
                {
                    worksheet = new CloverEraWorksheetActionDto
                    {
                        WorksheetCode = worksheet.WorksheetCode,
                        WorksheetTitle = worksheet.WorksheetTitle,
                        DurationMinutes = worksheet.DurationMinutes,
                        Text = newText,
                        SuccessMetric = worksheet.SuccessMetric,
                        SwapNote = worksheet.SwapNote
                    };
                    changes.Add("Worksheet framing: rewritten to reduce repetition.");
                }
            }

            // Apply micro-action rewrites
            if (parsed.Value.TryGetProperty("micro_action_texts", out var maEl))
            {
                var maList = microActions.ToList();
                foreach (var m in maEl.EnumerateArray())
                {
                    if (!m.TryGetProperty("index", out var idxEl)) continue;
                    int idx = idxEl.GetInt32();
                    if (idx >= maList.Count) continue;

                    var newText = m.TryGetProperty("text", out var nt) ? nt.GetString() ?? maList[idx].Text : maList[idx].Text;
                    var violations = ValidateSynthesisRewrite(maList[idx].Text, newText);
                    if (violations.Count == 0 && newText != maList[idx].Text)
                    {
                        maList[idx] = new CloverEraMicroActionDto { DurationMinutes = maList[idx].DurationMinutes, Text = newText };
                        changes.Add($"Micro-action {idx + 1}: rewritten to reduce repetition.");
                    }
                    else if (violations.Count > 0)
                    {
                        _runQualityWarnings.Add($"Stage 5.5 (synthesis): Micro-action {idx + 1} rewrite dropped: {string.Join(", ", violations)}");
                    }
                }
                microActions = maList;
            }

            if (changes.Count == 0)
                _runQualityWarnings.Add("Stage 5.5 (synthesis): no cross-piece repetition detected; no rewrites applied.");
            else
                _runQualityWarnings.AddRange(changes.Select(c => $"Stage 5.5 (synthesis): {c}"));

            return (themes, recommendations, worksheet, microActions, escalation);
        }

        private string BuildSynthesisPrompt(
            List<WrittenTheme> themes,
            List<CloverEraRecommendationDto> recommendations,
            CloverEraWorksheetActionDto worksheet,
            List<CloverEraMicroActionDto> microActions,
            CloverEraEscalationActionDto? escalation)
        {
            var template = LoadPromptTemplate("synthesis.md");

            // Pad themes to 4
            var t0 = themes.FirstOrDefault(t => t.ThemeIndex == 0) ?? new WrittenTheme { Pillar = "(none)", Headline = "(none)", Text = "(none)" };
            var t1 = themes.FirstOrDefault(t => t.ThemeIndex == 1) ?? new WrittenTheme { Pillar = "(none)", Headline = "(none)", Text = "(none)" };
            var t2 = themes.FirstOrDefault(t => t.ThemeIndex == 2) ?? new WrittenTheme { Pillar = "(none)", Headline = "(none)", Text = "(none)" };
            var t3 = themes.FirstOrDefault(t => t.ThemeIndex == 3) ?? new WrittenTheme { Pillar = "(none)", Headline = "(none)", Text = "(none)" };

            var rec0 = recommendations.Count > 0 ? recommendations[0].Text : "(none)";
            var rec1 = recommendations.Count > 1 ? recommendations[1].Text : "(none)";

            var micro0 = microActions.Count > 0 ? microActions[0] : new CloverEraMicroActionDto { DurationMinutes = 5, Text = "(none)" };
            var micro1 = microActions.Count > 1 ? microActions[1] : null;

            var micro1Block = micro1 != null
                ? $"### Micro-action 2 ({micro1.DurationMinutes} min)\nText: {micro1.Text}"
                : "(only one micro-action this period)";

            var escalationBlock = escalation != null
                ? $"### Escalation ({escalation.SignalCategory})\nRoute: {escalation.Route} within {escalation.TimeframeDays} days.\nText: {escalation.Text}"
                : "(no escalation this period)";

            var micro1JsonComma = micro1 != null ? "," : "";
            var micro1JsonEntry = micro1 != null ? "{\"index\": 1, \"text\": \"string\"}" : "";
            var escalationTextJson = escalation != null ? "\"string\"" : "null";

            var values = new Dictionary<string, string>
            {
                ["theme0_pillar"] = t0.Pillar, ["theme0_headline"] = t0.Headline, ["theme0_text"] = t0.Text,
                ["theme1_pillar"] = t1.Pillar, ["theme1_headline"] = t1.Headline, ["theme1_text"] = t1.Text,
                ["theme2_pillar"] = t2.Pillar, ["theme2_headline"] = t2.Headline, ["theme2_text"] = t2.Text,
                ["theme3_pillar"] = t3.Pillar, ["theme3_headline"] = t3.Headline, ["theme3_text"] = t3.Text,
                ["rec0_text"] = rec0, ["rec1_text"] = rec1,
                ["worksheet_code"] = worksheet.WorksheetCode,
                ["worksheet_title"] = worksheet.WorksheetTitle,
                ["worksheet_duration"] = worksheet.DurationMinutes.ToString(),
                ["worksheet_text"] = worksheet.Text,
                ["worksheet_success_metric"] = worksheet.SuccessMetric,
                ["micro0_duration"] = micro0.DurationMinutes.ToString(), ["micro0_text"] = micro0.Text,
                ["micro1_block"] = micro1Block,
                ["escalation_block"] = escalationBlock,
                ["micro1_json_comma"] = micro1JsonComma,
                ["micro1_json_entry"] = micro1JsonEntry,
                ["escalation_text_json"] = escalationTextJson
            };

            return FillTemplate(template, values);
        }

        private static List<string> ValidateSynthesisRewrite(string original, string rewritten)
        {
            if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(rewritten)) return new();
            var violations = new List<string>();

            // Numbers must not drop
            var origNums = new HashSet<string>(Regex.Matches(original, @"\b\d+(?:\.\d+)?\b").Select(m => m.Value));
            var newNums = new HashSet<string>(Regex.Matches(rewritten, @"\b\d+(?:\.\d+)?\b").Select(m => m.Value));
            var missingNums = origNums.Except(newNums).ToList();
            if (missingNums.Count > 0)
                violations.Add($"Missing numbers: {string.Join(", ", missingNums)}");

            // PII token counts must match
            var origPii = Regex.Matches(original, @"\[(?:name|email|url|phone)\]").Select(m => m.Value).OrderBy(x => x).ToList();
            var newPii = Regex.Matches(rewritten, @"\[(?:name|email|url|phone)\]").Select(m => m.Value).OrderBy(x => x).ToList();
            if (!origPii.SequenceEqual(newPii))
                violations.Add("PII token counts changed");

            // No em-dashes introduced
            if (rewritten.Contains('—') && !original.Contains('—'))
                violations.Add("Em-dash introduced in rewrite");

            return violations;
        }

        // ─────────────────────────────────────────────────────────────────────
        // JSON helpers
        // ─────────────────────────────────────────────────────────────────────

        private static JsonElement? TryParseJson(string raw)
        {
            var stripped = raw.Trim();
            if (stripped.StartsWith("```"))
            {
                stripped = Regex.Replace(stripped, @"^```[a-zA-Z]*\n", "");
                stripped = Regex.Replace(stripped, @"\n```\s*$", "");
                stripped = stripped.Trim();
            }
            try { return JsonDocument.Parse(stripped).RootElement; }
            catch { }

            var m = JsonBodyRe.Match(stripped);
            if (!m.Success) return null;
            try { return JsonDocument.Parse(m.Value).RootElement; }
            catch { return null; }
        }

        // ─────────────────────────────────────────────────────────────────────
        // LLM calls (mirrors PromptManagementService pattern)
        // ─────────────────────────────────────────────────────────────────────

        private async Task<(string Content, int PromptTokens, int CompletionTokens, int TotalTokens)> CallLlmAsync(
            string systemPrompt, string userContent, ModelConfiguration model,
            double temperature, int maxTokens, double topP, CancellationToken ct, int thinkingBudget = 0)
        {
            var endpoint = (model.Endpoint ?? "").Trim();
            var isAnthropic = endpoint.Contains("/anthropic/", StringComparison.OrdinalIgnoreCase);

            if (isAnthropic)
                return await CallAnthropicAsync(systemPrompt, userContent, model, endpoint, temperature, maxTokens, topP, ct, thinkingBudget);

            var messages = new List<object>
            {
                new { role = "system", content = string.IsNullOrWhiteSpace(systemPrompt) ? "You must always respond in English only." : $"{systemPrompt}\n\nIMPORTANT: You must always respond in English only." },
                new { role = "user", content = userContent }
            };

            bool isServerless = endpoint.Contains("/models/chat/completions", StringComparison.OrdinalIgnoreCase)
                || (endpoint.Contains(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase) && endpoint.Contains("/models/", StringComparison.OrdinalIgnoreCase));

            var bodyObj = isServerless
                ? (object)new { model = model.ModelName?.Trim(), messages, max_tokens = maxTokens, temperature, top_p = topP }
                : new { messages, max_tokens = maxTokens, temperature, top_p = topP };

            var body = JsonSerializer.Serialize(bodyObj);
            using var client = _httpFactory.CreateClient();
            client.DefaultRequestHeaders.Add("api-key", model.ApiKey);
            using var res = await client.PostAsync(endpoint, new StringContent(body, Encoding.UTF8, "application/json"), ct);
            var responseText = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"LLM call failed ({res.StatusCode}): {responseText}");

            // Surface empty / non-JSON bodies with the endpoint and a preview so the
            // audit row actually says WHY parsing failed instead of leaking a raw
            // JsonReaderException at byte 0.
            if (string.IsNullOrWhiteSpace(responseText))
                throw new InvalidOperationException(
                    $"LLM returned status {(int)res.StatusCode} but an empty body. endpoint={endpoint} model={model.ModelName}");
            var trimmedAz = responseText.TrimStart();
            if (trimmedAz.Length == 0 || (trimmedAz[0] != '{' && trimmedAz[0] != '['))
                throw new InvalidOperationException(
                    $"LLM returned non-JSON body (status {(int)res.StatusCode}). endpoint={endpoint} model={model.ModelName} bodyPreview={Truncate(responseText, 500)}");

            using var doc = JsonDocument.Parse(responseText);
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            int pt = 0, ct2 = 0, tt = 0;
            if (doc.RootElement.TryGetProperty("usage", out var usage))
            {
                pt = usage.GetProperty("prompt_tokens").GetInt32();
                ct2 = usage.GetProperty("completion_tokens").GetInt32();
                tt = usage.GetProperty("total_tokens").GetInt32();
            }
            return (content, pt, ct2, tt);
        }

        private async Task<(string Content, int PromptTokens, int CompletionTokens, int TotalTokens)> CallAnthropicAsync(
            string systemPrompt, string userContent, ModelConfiguration model, string endpoint,
            double temperature, int maxTokens, double topP, CancellationToken ct, int thinkingBudget = 0)
        {
            var effectiveSystem = string.IsNullOrWhiteSpace(systemPrompt)
                ? "You must always respond in English only."
                : $"{systemPrompt}\n\nIMPORTANT: You must always respond in English only.";

            var messages = new List<object> { new { role = "user", content = userContent } };
            var bodyDict = new Dictionary<string, object?>
            {
                ["model"] = model.ModelName?.Trim(),
                ["system"] = effectiveSystem,
                ["messages"] = messages,
            };

            if (thinkingBudget > 0 && AnthropicModelRules.UsesThinkingBudget(model.ModelName))
            {
                // Extended thinking on a model that still takes a budget (4.5 and older): force
                // temp=1.0, ensure max_tokens > budget.
                bodyDict["thinking"] = new { type = "enabled", budget_tokens = thinkingBudget };
                bodyDict["temperature"] = 1.0;
                bodyDict["max_tokens"] = Math.Max(maxTokens, thinkingBudget + 1000);
            }
            else if (thinkingBudget > 0)
            {
                // From 4.6 on thinking is adaptive, and on Claude Opus 5.5 budget_tokens is a 400.
                // The budget still sizes max_tokens, which covers thinking plus the answer.
                bodyDict["thinking"] = new { type = "adaptive" };
                bodyDict["max_tokens"] = Math.Max(maxTokens, thinkingBudget + 1000);
            }
            else
            {
                bodyDict["max_tokens"] = maxTokens;
                // Newer Claude models (Opus 4.7+, Sonnet 5+, Opus 5.5) reject temperature/top_p with a 400.
                AnthropicModelRules.AddSampling(bodyDict, model.ModelName, temperature, topP);
            }

            var body = JsonSerializer.Serialize(bodyDict);
            using var client = _httpFactory.CreateClient();
            client.DefaultRequestHeaders.Add("x-api-key", model.ApiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            client.Timeout = TimeSpan.FromMinutes(5); // extended thinking can be slow
            using var res = await client.PostAsync(endpoint, new StringContent(body, Encoding.UTF8, "application/json"), ct);
            var responseText = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"LLM call failed ({res.StatusCode}): {responseText}");

            // Surface empty / non-JSON bodies with the endpoint and a preview.
            if (string.IsNullOrWhiteSpace(responseText))
                throw new InvalidOperationException(
                    $"LLM (Anthropic) returned status {(int)res.StatusCode} but an empty body. endpoint={endpoint} model={model.ModelName}");
            var trimmedAn = responseText.TrimStart();
            if (trimmedAn.Length == 0 || (trimmedAn[0] != '{' && trimmedAn[0] != '['))
                throw new InvalidOperationException(
                    $"LLM (Anthropic) returned non-JSON body (status {(int)res.StatusCode}). endpoint={endpoint} model={model.ModelName} bodyPreview={Truncate(responseText, 500)}");

            using var doc = JsonDocument.Parse(responseText);
            // Find the text block — with extended thinking there may also be a "thinking" block
            var content = "";
            if (doc.RootElement.TryGetProperty("content", out var contentArr))
            {
                foreach (var block in contentArr.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "text"
                        && block.TryGetProperty("text", out var textEl))
                    {
                        content = textEl.GetString() ?? "";
                        break;
                    }
                }
            }

            int pt = 0, ct2 = 0, tt = 0;
            if (doc.RootElement.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("input_tokens", out var it)) pt = it.GetInt32();
                if (usage.TryGetProperty("output_tokens", out var ot)) ct2 = ot.GetInt32();
                tt = pt + ct2;
            }
            return (content, pt, ct2, tt);
        }

        private static string Truncate(string? s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "(empty)";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
