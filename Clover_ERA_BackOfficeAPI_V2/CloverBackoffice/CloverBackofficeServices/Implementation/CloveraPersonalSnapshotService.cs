using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Microsoft.Extensions.Logging;
using Pgvector;

namespace CloverBackoffice.Services.Implementation
{
    public class CloveraPersonalSnapshotService : ICloveraPersonalSnapshotService
    {
        private readonly IFeedbackAnalysisService _feedbackService;
        private readonly IDocumentEmbeddingRepository _embeddingRepo;
        private readonly IEmbeddingRepository _emb;
        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<CloveraPersonalSnapshotService> _logger;

        // Fixed generation settings — mirror the reference snapshot engine (route.ts):
        // synthesis, not extraction. The caller does not pass these.
        private const double Temperature = 0.65;
        private const int MaxTokens = 1200;
        private const double TopP = 1.0;

        // ModelConfigurationType.Id for the "Search-model" type (same id RAG/feedback use).
        private const int SearchModelTypeId = 1;

        // RAG retrieval knobs — mirror the reference engine (route.ts): two queries
        // (tight spotlight + broad cycle), dedup, take top N. Reuses the existing
        // pgvector KB (DocumentEmbeddings) via IEmbeddingRepository + IDocumentEmbeddingRepository.
        private const int KbPerQuery = 8;
        private const int KbMaxChunks = 14;
        private const int KbChunkCharCap = 1500;

        public CloveraPersonalSnapshotService(
            IFeedbackAnalysisService feedbackService,
            IDocumentEmbeddingRepository embeddingRepo,
            IEmbeddingRepository emb,
            IHttpClientFactory httpFactory,
            ILogger<CloveraPersonalSnapshotService> logger)
        {
            _feedbackService = feedbackService;
            _embeddingRepo = embeddingRepo;
            _emb = emb;
            _httpFactory = httpFactory;
            _logger = logger;
        }

        public async Task GenerateAsync(CloveraPersonalSnapshot snapshot, CancellationToken ct = default)
        {
            // Select the Search-model by priority: Claude Sonnet → GPT-4o → any other.
            var model = await SelectSearchModelByPriorityAsync();

            // 1. Load the unit's engagement feedback for the window (scope comes from the entity).
            var feedback = await _feedbackService.FeedbackDataAsync(
                (long)snapshot.ProfileId, (long)snapshot.UnitId,
                ToUtcOffset(snapshot.FromDate), ToUtcOffset(snapshot.ToDate));
            if (feedback == null || feedback.Count == 0)
                throw new InvalidOperationException("No feedback data found for the selected unit and date range.");

            // 2. Reduce to one entry per pillar (latest submission wins) and pick the spotlight.
            var entries = BuildEngagementEntries(feedback);
            var (highlight, highlightReason) = PickHighlight(entries);

            // 2b. Load the unit's productivity reflection lines (achievements / not-done /
            //     blockers) for the same window — the exact three lists the mobile Output
            //     tab renders. The observation is written as a PATTERN across THESE lists
            //     only (per the v1.1 UI contract), not from time-allocation hours.
            var reflectionLines = await _feedbackService.DailyReflectionLinesAsync(
                (long)snapshot.UnitId,
                ToUtcOffset(snapshot.FromDate), ToUtcOffset(snapshot.ToDate));

            // 2c. RAG: retrieve Clover KB passages for this cycle's themes from the
            //     existing pgvector store (DocumentEmbeddings). Best-effort — if the
            //     KB / embedding model isn't available, fall back to framework-only.
            var kbChunks = await RetrieveKbAsync(entries, highlight, ct);

            // 3. Build the user prompt (engagement + productivity + retrieved KB passages).
            var userPrompt = BuildUserPrompt(snapshot, entries, highlight, highlightReason, reflectionLines, kbChunks);

            // 4. Call Claude with the default Search-model's configuration (fixed gen settings).
            var sw = Stopwatch.StartNew();
            var (content, pt, completion, total) =
                await CallLlmAsync(SystemPrompt, userPrompt, model, Temperature, MaxTokens, TopP, ct);
            sw.Stop();

            // 5. Parse and map onto the entity.
            var parsed = ParseSnapshotJson(content, model.ModelName);

            // 5b. Citation discipline: when KB passages were supplied, drop any
            //     nudge_drawing_from entries whose source isn't a retrieved passage
            //     (defends against the model inventing source paths). Mirrors route.ts.
            if (kbChunks.Count > 0)
                parsed.DrawingFromJson = FilterDrawingFromToKb(parsed.DrawingFromJson, kbChunks);

            snapshot.ModelUsed = model.ModelName;
            snapshot.Headline = parsed.Headline;
            snapshot.SummaryBody = parsed.SummaryBody;
            // Contract hard cap: observation must be <= 30 words. The LLM occasionally
            // drifts a few words over, so clamp deterministically before persisting.
            snapshot.ProductivityObservation = TrimToWords(parsed.ProductivityObservation, 30);
            snapshot.PillarFraming = parsed.PillarFraming;
            snapshot.HighlightedPillarCode = PillarCode(highlight.Category);
            snapshot.HighlightedPillarLabel = highlight.Category.ToString();
            snapshot.HighlightedSmileyScore = highlight.SmileyScore;
            snapshot.HighlightReason = highlightReason;
            snapshot.NudgeTitle = parsed.NudgeTitle;
            snapshot.NudgeRationale = parsed.NudgeRationale;
            snapshot.NudgeAction = parsed.NudgeAction;
            snapshot.NudgeIntent = IntentFor(highlightReason, highlight.SmileyScore);
            snapshot.DrawingFromJson = parsed.DrawingFromJson;
            snapshot.RawResponseJson = content;
            snapshot.PromptTokens = pt;
            snapshot.CompletionTokens = completion;
            snapshot.TotalTokens = total;
            snapshot.GenerationSeconds = Math.Round(sw.Elapsed.TotalSeconds, 1);
        }

        // ──────────────────────────────────────────────────────────────────
        // Model selection — priority order among active Search-models (type 1):
        //   1. Claude Sonnet (Anthropic)   2. GPT-4o   3. any other (DeepSeek / Grok / …)
        // Looks across ALL active search models, not just the one flagged default,
        // so the priority always wins when a higher-ranked model exists.
        // ──────────────────────────────────────────────────────────────────
        private async Task<ModelConfiguration> SelectSearchModelByPriorityAsync()
        {
            var models = await _embeddingRepo.GetAllModelsAsync(SearchModelTypeId);
            if (models == null || models.Count == 0)
                throw new InvalidOperationException("No active Search-model found in ModelConfiguration.");

            // Match on model name + friendly name + provider (case-insensitive).
            static bool Has(ModelConfiguration m, params string[] tokens)
            {
                var hay = $"{m.ModelName} {m.FriendlyName} {m.Provider}".ToLowerInvariant();
                return tokens.All(t => hay.Contains(t));
            }

            // 1. Claude Sonnet (Anthropic).
            var selected = models.FirstOrDefault(m => Has(m, "claude", "sonnet"))
                           ?? models.FirstOrDefault(m => Has(m, "sonnet"));

            // 2. GPT-4o fallback.
            selected ??= models.FirstOrDefault(m => Has(m, "gpt-4o"))
                         ?? models.FirstOrDefault(m => Has(m, "gpt4o"));

            // 3. Any other available — prefer the flagged default, else the newest.
            selected ??= models.FirstOrDefault(m => m.IsDefault) ?? models.First();

            _logger.LogInformation(
                "[CloveraPersonalSnapshot] Selected Search-model '{Model}' (provider={Provider}) by priority.",
                selected.ModelName, selected.Provider);
            return selected;
        }

        // ──────────────────────────────────────────────────────────────────
        // Input shaping
        // ──────────────────────────────────────────────────────────────────

        private sealed class EngagementEntry
        {
            public CloverCategory Category { get; set; }
            public DateTime Date { get; set; }
            public int SmileyScore { get; set; }            // 1..4 (Angry..Happy)
            public string QuestionText { get; set; } = "";
            public string ReasonText { get; set; } = "";
            public string FreeText { get; set; } = "";
        }

        private static List<EngagementEntry> BuildEngagementEntries(List<FeedbackDataResultDto> feedback)
        {
            // Keep rows that carry a recognisable pillar, then take the most recent
            // submission per pillar so we feed Claude one entry per CLOVER element.
            return feedback
                .Where(f => f.CategoryId.HasValue && f.CategoryId.Value >= 1 && f.CategoryId.Value <= 6)
                .GroupBy(f => f.CategoryId!.Value)
                .Select(g => g.OrderByDescending(f => f.SubmissionDateTime).First())
                .Select(f => new EngagementEntry
                {
                    Category = (CloverCategory)f.CategoryId!.Value,
                    Date = f.SubmissionDateTime,
                    SmileyScore = f.SentimentTypeId, // already 1..4
                    QuestionText = f.QuestionText ?? "",
                    ReasonText = f.ReasonText ?? "",
                    FreeText = f.FreeText ?? ""
                })
                .OrderBy(e => (int)e.Category)
                .ToList();
        }

        // Port of pickHighlight() from route.ts, minus the previous-cycle "biggest mover"
        // branch (no prior-cycle data is wired in v1).
        private static (EngagementEntry Entry, string Reason) PickHighlight(List<EngagementEntry> entries)
        {
            var lowest = entries.OrderBy(e => e.SmileyScore).First();
            if (lowest.SmileyScore <= 2) return (lowest, "lowest");

            var highest = entries.OrderByDescending(e => e.SmileyScore).First();
            if (highest.SmileyScore >= 4) return (highest, "highest");

            return (lowest, "lowest");
        }

        private static string IntentFor(string highlightReason, int score)
        {
            if (highlightReason == "highest" || highlightReason == "biggest_mover_up") return "momentum";
            if (score <= 2) return "challenge";
            return "support";
        }

        private static string PillarCode(CloverCategory c) => c switch
        {
            CloverCategory.Communication => "C",
            CloverCategory.Learning => "L",
            CloverCategory.Opportunities => "O",
            CloverCategory.Vulnerability => "V",
            CloverCategory.Enablement => "E",
            CloverCategory.Reflection => "R",
            _ => "?"
        };

        // Stored DateTimes are UTC; bind them as UTC offsets for the timestamptz query.
        private static DateTimeOffset ToUtcOffset(DateTime dt) =>
            new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));

        private static string BuildUserPrompt(
            CloveraPersonalSnapshot snapshot,
            List<EngagementEntry> entries, EngagementEntry highlight, string highlightReason,
            List<DailyReflectionLinesDto> reflectionLines, List<KbChunk> kbChunks)
        {
            var engagementBlock = string.Join("\n", entries.Select(e =>
            {
                var line = $"[{e.Date:yyyy-MM-dd}] {PillarCode(e.Category)} ({e.Category}) — smiley {e.SmileyScore}/4 — " +
                           $"Q: \"{e.QuestionText}\" — reason: \"{e.ReasonText}\"";
                if (!string.IsNullOrWhiteSpace(e.FreeText)) line += $" — free: \"{e.FreeText}\"";
                return line;
            }));

            var sb = new StringBuilder();
            sb.AppendLine($"## Cycle: {snapshot.FromDate:yyyy-MM-dd} → {snapshot.ToDate:yyyy-MM-dd}");
            sb.AppendLine();
            sb.AppendLine("## Engagement (one entry per CLOVER pillar)");
            sb.AppendLine();
            sb.AppendLine(engagementBlock);
            sb.AppendLine();
            sb.AppendLine("## Server picked this pillar to highlight");
            sb.AppendLine();
            sb.AppendLine($"Pillar: {PillarCode(highlight.Category)} ({highlight.Category})");
            sb.AppendLine($"Smiley this cycle: {highlight.SmileyScore}/4");
            sb.AppendLine($"Reason for highlight: {highlightReason}");
            var reasonLine = $"User's reason for that smiley: \"{highlight.ReasonText}\"";
            if (!string.IsNullOrWhiteSpace(highlight.FreeText)) reasonLine += $" (free: \"{highlight.FreeText}\")";
            sb.AppendLine(reasonLine);
            sb.AppendLine();
            AppendProductivitySection(sb, reflectionLines);
            AppendKbSection(sb, kbChunks);
            sb.AppendLine("## Output");
            sb.AppendLine();
            sb.AppendLine("Strict JSON only.");
            return sb.ToString();
        }

        // Renders retrieved Clover KB passages as [clover-content: SOURCE] blocks —
        // the source material the nudge is synthesised from. Empty when retrieval
        // returned nothing (then the nudge falls back to framework knowledge).
        private static void AppendKbSection(StringBuilder sb, List<KbChunk> kbChunks)
        {
            if (kbChunks == null || kbChunks.Count == 0) return;

            sb.AppendLine("## Knowledge base passages");
            sb.AppendLine();
            sb.AppendLine("These are passages retrieved from the Clover ERA knowledge base for this cycle's themes. Treat them as the source material for the nudge — borrow concepts, methods, and framings from them. Cite the exact source labels in nudge_drawing_from when you draw from them.");
            sb.AppendLine();
            foreach (var c in kbChunks)
            {
                sb.AppendLine($"[clover-content: {c.Source}]");
                sb.AppendLine(c.Content);
                sb.AppendLine("[/clover-content]");
                sb.AppendLine();
            }
        }

        // Renders the three productivity lists (achievements / not-done / blockers) EXACTLY
        // as the mobile Output tab shows them — deduped case-insensitively (first-seen order)
        // and sentence-cased. The observation must read a PATTERN ACROSS these lists only:
        // no hours, no time-allocation, no invented numbers. Empty input → empty observation.
        private static void AppendProductivitySection(StringBuilder sb, List<DailyReflectionLinesDto> reflectionLines)
        {
            List<string> Pick(Func<DailyReflectionLinesDto, string[]?> pick) =>
                Normalize((reflectionLines ?? new List<DailyReflectionLinesDto>())
                    .SelectMany(r => pick(r) ?? Array.Empty<string>()));

            var achievements = Pick(r => r.AchievementsLines);
            var notDone = Pick(r => r.NotDoneLines);
            var blockers = Pick(r => r.BlockersLines);

            if (achievements.Count + notDone.Count + blockers.Count == 0)
            {
                sb.AppendLine("Note: no productivity entries are available for this cycle. Return an empty string for productivity_observation.");
                sb.AppendLine();
                return;
            }

            void Render(string label, List<string> items)
            {
                sb.AppendLine($"{label}:");
                if (items.Count == 0) sb.AppendLine("- (none)");
                else foreach (var i in items) sb.AppendLine($"- {i}");
                sb.AppendLine();
            }

            sb.AppendLine("## Productivity (the user's logged lists this cycle)");
            sb.AppendLine();
            Render("Achievements", achievements);
            Render("Not done", notDone);
            Render("Blockers", blockers);
            sb.AppendLine("Use ONLY these three lists to write productivity_observation. Describe the PATTERN across them — what the shape of achievements vs. not-done vs. blockers reveals. Do NOT list, quote, or restate individual items. Do NOT mention hours, time spent, durations, categories, or any number not shown above.");
            sb.AppendLine();
        }

        // Enforce the observation's <= 30-word contract cap. Returns the text unchanged
        // when already within budget; otherwise keeps the first `maxWords` words, drops a
        // trailing comma/conjunction left dangling by the cut, and ends with a period.
        private static string? TrimToWords(string? text, int maxWords)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var words = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= maxWords) return text;

            var kept = words.Take(maxWords).ToList();
            // Drop a dangling connector so the truncated sentence doesn't end mid-clause.
            while (kept.Count > 0 &&
                   (kept[^1].TrimEnd(',', ';', ':', '-').Length == 0 ||
                    kept[^1] is "and" or "but" or "or" or "the" or "a" or "to" or "of" or "that"))
                kept.RemoveAt(kept.Count - 1);

            var result = string.Join(' ', kept).TrimEnd(',', ';', ':', '-', ' ');
            if (result.Length > 0 && result[^1] is not ('.' or '!' or '?'))
                result += ".";
            return result;
        }

        // Aggregate a productivity list the same way the API read-model does (so the prompt
        // sees exactly what the UI renders): trim, drop empties, dedup case-insensitively
        // keeping first-seen order, then sentence-case the first character only.
        private static List<string> Normalize(IEnumerable<string?> raw)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var outp = new List<string>();
            foreach (var x in raw)
            {
                if (string.IsNullOrWhiteSpace(x)) continue;
                var t = x.Trim();
                if (!seen.Add(t)) continue;
                outp.Add(char.ToUpper(t[0]) + t.Substring(1));
            }
            return outp;
        }

        // ──────────────────────────────────────────────────────────────────
        // RAG retrieval — reuses the existing pgvector KB (DocumentEmbeddings)
        // via IEmbeddingRepository (embed) + IDocumentEmbeddingRepository (search).
        // Mirrors the reference engine's two-query (spotlight + cycle) retrieval.
        // ──────────────────────────────────────────────────────────────────

        private sealed class KbChunk
        {
            public string Source { get; set; } = "";
            public string Content { get; set; } = "";
        }

        private async Task<List<KbChunk>> RetrieveKbAsync(
            List<EngagementEntry> entries, EngagementEntry highlight, CancellationToken ct)
        {
            try
            {
                // Tight query on the spotlight pillar's situation + broad query on the
                // whole cycle — biases retrieval toward the highlight while still
                // surfacing cross-cycle context. (Reference route.ts does the same.)
                var spotlightText = $"{highlight.Category} {highlight.ReasonText} {highlight.FreeText}".Trim();
                var cycleText = string.Join(" | ",
                    entries.Select(e => $"{e.Category} {e.ReasonText} {e.FreeText}".Trim()));

                var spotlightVec = await _emb.GetEmbeddingAsync(spotlightText, ct);
                var cycleVec = await _emb.GetEmbeddingAsync(cycleText, ct);

                var spotlightDocs = await _embeddingRepo.SearchSimilarAsync(new Vector(spotlightVec), KbPerQuery);
                var cycleDocs = await _embeddingRepo.SearchSimilarAsync(new Vector(cycleVec), KbPerQuery);

                // Dedup by document id (spotlight first), cap the total.
                var seen = new HashSet<int>();
                var chunks = new List<KbChunk>();
                foreach (var (doc, _) in spotlightDocs.Concat(cycleDocs))
                {
                    if (doc == null || string.IsNullOrWhiteSpace(doc.Content)) continue;
                    if (!seen.Add(doc.Id)) continue;
                    chunks.Add(new KbChunk
                    {
                        Source = KbSourceLabel(doc),
                        Content = Truncate(doc.Content, KbChunkCharCap)
                    });
                    if (chunks.Count >= KbMaxChunks) break;
                }

                _logger.LogInformation("[CloveraPersonalSnapshot] KB retrieval returned {Count} chunk(s).", chunks.Count);
                return chunks;
            }
            catch (Exception ex)
            {
                // Best-effort: a missing embedding model / empty KB must not fail the run.
                _logger.LogWarning(ex, "[CloveraPersonalSnapshot] KB retrieval failed; proceeding without KB grounding.");
                return new List<KbChunk>();
            }
        }

        // Stable citation label for a KB row — the string the model must echo in
        // nudge_drawing_from.source and that we validate against.
        private static string KbSourceLabel(DocumentEmbedding d)
        {
            if (!string.IsNullOrWhiteSpace(d.FileName)) return d.FileName!.Trim();
            if (!string.IsNullOrWhiteSpace(d.Title)) return d.Title!.Trim();
            var type = string.IsNullOrWhiteSpace(d.SourceType) ? "kb" : d.SourceType!.Trim();
            return $"{type}#{d.Id}";
        }

        // Drop nudge_drawing_from entries whose source isn't a retrieved KB label.
        private static string? FilterDrawingFromToKb(string? drawingFromJson, List<KbChunk> kbChunks)
        {
            if (string.IsNullOrWhiteSpace(drawingFromJson)) return drawingFromJson;
            var valid = new HashSet<string>(kbChunks.Select(c => c.Source), StringComparer.OrdinalIgnoreCase);
            try
            {
                using var doc = JsonDocument.Parse(drawingFromJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return drawingFromJson;

                var kept = new List<Dictionary<string, string?>>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    var src = el.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                    if (string.IsNullOrWhiteSpace(src) || !valid.Contains(src!)) continue;
                    var what = el.TryGetProperty("what", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : "";
                    kept.Add(new Dictionary<string, string?> { ["source"] = src, ["what"] = what });
                }
                return JsonSerializer.Serialize(kept);
            }
            catch
            {
                return drawingFromJson;
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // Response parsing
        // ──────────────────────────────────────────────────────────────────

        private sealed class ParsedSnapshot
        {
            public string? Headline { get; set; }
            public string? SummaryBody { get; set; }
            public string? ProductivityObservation { get; set; }
            public string? PillarFraming { get; set; }
            public string? NudgeTitle { get; set; }
            public string? NudgeRationale { get; set; }
            public string? NudgeAction { get; set; }
            public string? DrawingFromJson { get; set; }
        }

        // The LLM peppers narrative text with em/en dashes (— –) as clause separators.
        // The UI shows these as a stray dash in the description, so swap any unicode
        // dash used as punctuation for a comma and collapse the surrounding spaces.
        // ASCII hyphen-minus (e.g. "3-7 words") is left untouched.
        private static string? StripDashes(string? s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            // Any Unicode dash (\p{Pd}) used as punctuation, plus spaced ASCII hyphens.
            // Bare ASCII hyphens in words/paths ("3-7", "Worksheets-PDFs") are kept.
            return Regex.Replace(s, @"\s*[\p{Pd}−➖⁒-[-]]\s*|\s+-\s+", ", ");
        }

        // Clean the dash on the DECODED string values of each { source, what } entry, then
        // re-serialise. StripDashes over GetRawText() misses dashes the model emitted as
        // \uXXXX escapes — GetString() decodes them to the real char first (same approach the
        // Action/headline fields already use), so the stored JSON is dash-free.
        private static string CleanDrawingFromJson(JsonElement arr)
        {
            var items = new List<Dictionary<string, string?>>();
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var obj = new Dictionary<string, string?>();
                foreach (var prop in el.EnumerateObject())
                    obj[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? StripDashes(prop.Value.GetString())
                        : prop.Value.GetRawText();
                items.Add(obj);
            }
            return JsonSerializer.Serialize(items);
        }

        private static ParsedSnapshot ParseSnapshotJson(string raw, string? modelName)
        {
            // Claude occasionally wraps JSON in ```json fences despite "strict JSON only".
            var cleaned = raw?.Trim() ?? "";
            if (cleaned.StartsWith("```"))
            {
                var firstNewline = cleaned.IndexOf('\n');
                if (firstNewline >= 0) cleaned = cleaned.Substring(firstNewline + 1);
                if (cleaned.EndsWith("```")) cleaned = cleaned.Substring(0, cleaned.Length - 3);
                cleaned = cleaned.Trim();
            }

            JsonDocument doc;
            try { doc = JsonDocument.Parse(cleaned); }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"{modelName} returned non-JSON. {ex.Message}. bodyPreview={Truncate(raw, 500)}");
            }

            using (doc)
            {
                var root = doc.RootElement;
                string? Str(string name) =>
                    root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? StripDashes(el.GetString()) : null;

                string? drawingFromJson = null;
                if (root.TryGetProperty("nudge_drawing_from", out var df) && df.ValueKind == JsonValueKind.Array)
                    drawingFromJson = CleanDrawingFromJson(df);

                return new ParsedSnapshot
                {
                    Headline = Str("headline"),
                    SummaryBody = Str("summary_body"),
                    ProductivityObservation = Str("productivity_observation"),
                    PillarFraming = Str("pillar_framing"),
                    NudgeTitle = Str("nudge_title"),
                    NudgeRationale = Str("nudge_rationale"),
                    NudgeAction = Str("nudge_action"),
                    DrawingFromJson = drawingFromJson
                };
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // LLM dispatch — same configuration/headers as the workbench engine.
        // ──────────────────────────────────────────────────────────────────

        private async Task<(string Content, int PromptTokens, int CompletionTokens, int TotalTokens)> CallLlmAsync(
            string systemPrompt, string userContent, ModelConfiguration model,
            double temperature, int maxTokens, double topP, CancellationToken ct)
        {
            var endpoint = (model.Endpoint ?? "").Trim();
            var isAnthropic = endpoint.Contains("/anthropic/", StringComparison.OrdinalIgnoreCase);

            if (isAnthropic)
                return await CallAnthropicAsync(systemPrompt, userContent, model, endpoint, temperature, maxTokens, topP, ct);

            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent }
            };

            // Azure AI Foundry serverless models (.../models/chat/completions) take the full
            // endpoint URL with the model name in the body. Azure OpenAI deployments (a bare
            // cognitiveservices base URL, e.g. the default gpt-4o Search-model) need the
            // /openai/deployments/{model}/chat/completions path built off a base address.
            // Same routing RagService uses for the Search-model.
            bool isServerless = endpoint.Contains("/models/chat/completions", StringComparison.OrdinalIgnoreCase)
                || (endpoint.Contains(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase) && endpoint.Contains("/models/", StringComparison.OrdinalIgnoreCase));

            using var client = _httpFactory.CreateClient();
            client.DefaultRequestHeaders.Add("api-key", model.ApiKey);
            client.Timeout = TimeSpan.FromMinutes(5);

            string url;
            string body;
            if (isServerless)
            {
                url = endpoint;
                body = JsonSerializer.Serialize(new { model = model.ModelName?.Trim(), messages, max_tokens = maxTokens, temperature, top_p = topP });
            }
            else
            {
                client.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
                url = $"/openai/deployments/{model.ModelName?.Trim()}/chat/completions?api-version={model.ApiVersion}";
                body = JsonSerializer.Serialize(new { messages, max_tokens = maxTokens, temperature, top_p = topP });
            }

            using var res = await client.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"), ct);
            var responseText = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"LLM call failed ({res.StatusCode}): {responseText}");
            if (string.IsNullOrWhiteSpace(responseText))
                throw new InvalidOperationException($"LLM returned an empty body. endpoint={endpoint} model={model.ModelName}");

            using var d = JsonDocument.Parse(responseText);
            var content = d.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            int pt = 0, ctk = 0, tt = 0;
            if (d.RootElement.TryGetProperty("usage", out var usage))
            {
                pt = usage.GetProperty("prompt_tokens").GetInt32();
                ctk = usage.GetProperty("completion_tokens").GetInt32();
                tt = usage.GetProperty("total_tokens").GetInt32();
            }
            return (content, pt, ctk, tt);
        }

        private async Task<(string Content, int PromptTokens, int CompletionTokens, int TotalTokens)> CallAnthropicAsync(
            string systemPrompt, string userContent, ModelConfiguration model, string endpoint,
            double temperature, int maxTokens, double topP, CancellationToken ct)
        {
            var messages = new List<object> { new { role = "user", content = userContent } };
            var bodyDict = new Dictionary<string, object?>
            {
                ["model"] = model.ModelName?.Trim(),
                ["system"] = systemPrompt,
                ["messages"] = messages,
                ["max_tokens"] = maxTokens,
            };
            // Newer Claude models (Opus 4.7+, Sonnet 5+, Opus 5.5) reject temperature/top_p with a 400.
            AnthropicModelRules.AddSampling(bodyDict, model.ModelName, temperature, topP);

            var body = JsonSerializer.Serialize(bodyDict);
            using var client = _httpFactory.CreateClient();
            client.DefaultRequestHeaders.Add("x-api-key", model.ApiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            client.Timeout = TimeSpan.FromMinutes(5);
            using var res = await client.PostAsync(endpoint, new StringContent(body, Encoding.UTF8, "application/json"), ct);
            var responseText = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"LLM call failed ({res.StatusCode}): {responseText}");
            if (string.IsNullOrWhiteSpace(responseText))
                throw new InvalidOperationException($"LLM (Anthropic) returned an empty body. endpoint={endpoint} model={model.ModelName}");

            using var d = JsonDocument.Parse(responseText);
            var content = "";
            if (d.RootElement.TryGetProperty("content", out var contentArr))
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

            int pt = 0, ctk = 0, tt = 0;
            if (d.RootElement.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("input_tokens", out var it)) pt = it.GetInt32();
                if (usage.TryGetProperty("output_tokens", out var ot)) ctk = ot.GetInt32();
                tt = pt + ctk;
            }
            return (content, pt, ctk, tt);
        }

        private static string Truncate(string? s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "(empty)";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        // ──────────────────────────────────────────────────────────────────
        // System prompt — adapted from clover-snapshot-handoff route.ts SYSTEM_PROMPT.
        // All discipline rules kept verbatim in intent. KB/[clover-content] grounding
        // removed (no RAG in v1): the nudge is synthesised from CLOVER-framework
        // knowledge instead, and nudge_drawing_from may be an empty array.
        // ──────────────────────────────────────────────────────────────────
        private const string SystemPrompt = @"You are an analysis engine for the Clover ERA platform. You know the Clover ERA framework — its six pillars (Communication, Learning, Opportunities, Vulnerability, Enablement, Reflection), its worksheets, micro-actions, and action library — and you draw on that knowledge to synthesise advice.

When [clover-content: SOURCE] blocks are provided in the input, they are authoritative passages retrieved from the Clover ERA knowledge base for this cycle. PREFER them as the source material for the nudge — borrow their concepts, methods, and framings — and cite the exact SOURCE labels in nudge_drawing_from. When no such blocks are provided, draw on your own Clover ERA framework knowledge instead.

Your job: synthesise one user's CLOVER cycle into a snapshot. The snapshot is the DEEP synthesis — it sits above the user's daily nudges and must offer more than they'd see in a routine daily check-in.

CRITICAL — MOBILE READING:
The user will read this on a phone, almost always. Every prose block needs to be tight enough to scan in a few seconds. Rules:
- Write short, direct sentences. Prefer fragments and clauses where they work.
- No setup phrases (""It is worth noting that…"", ""This suggests that…""). Get to the point.
- One idea per sentence. If you find yourself joining clauses with semicolons or ""and… and…"", split or cut.
- Word budgets are HARD CAPS, not aspirational targets. Going over by 20% defeats the purpose.

How to think about the input:
- Each engagement check-in has a smiley score (1=bad..4=good) + a dropdown reason (the bucketed signal) + sometimes free text.
- The DROPDOWN REASON is the user's chosen bucket — what category their situation falls into.
- The FREE TEXT is enhancement: the user's own words about what specifically is going on. It is NOT a separate signal — it sharpens the dropdown choice. Use it to write more specifically, but never present it as a separate channel.

Outputs to produce:

1. HEADLINE — 2-4 words, evocative, and SPECIFIC TO THIS CYCLE. Forbidden defaults: ""Tough days"", ""Quiet erosion"", ""Mixed signals"", ""Holding the line"" are CATEGORIES, not headlines. A strong headline names something concrete from THIS user's week (the 11pm message, the customer telling them, the lead-role going to someone else, the eleven-week silence). The headline test: read it alone — can a reader guess what's specifically happening to this person THIS week? Generic shape is allowed ONLY when the cycle genuinely lacks a singular event to name. Never use corporate words like ""engagement"", ""alignment"", ""talent"".

2. SUMMARY_BODY — maximum 40 words. 2 sentences. Name the trajectory of the week and reference at least one concrete thing the user wrote. Tone: thoughtful peer who has been paying attention, not a corporate report. DO NOT end with a motivational closer (""keep going"", ""stay positive""). Empty validation reads as a system that isn't listening.

3. PRODUCTIVITY_OBSERVATION — maximum 30 words (HARD cap). If a Productivity section with the user's three lists (achievements / not-done / blockers) is provided, write ONE sentence naming the PATTERN across those lists — what the relationship between what got done, what didn't, and what blocked reveals. STRICT: do NOT list, quote, or restate individual items from the lists. Do NOT mention hours, durations, time allocation, work categories, or ANY number. Use only the three lists shown — invent nothing. If no productivity data is provided, return an empty string.

4. PILLAR_FRAMING — maximum 25 words. One sentence about the spotlight pillar this cycle. If the user wrote free text for that pillar, work that detail in.

5. NUDGE — synthesise a single concrete move grounded in the Clover ERA framework and sound people-leadership practice, tailored to this specific user. Higher bar than a daily nudge: it should leave the user thinking ""I wouldn't have thought of doing that.""

   a) NUDGE_TITLE — 3-7 words. Names THIS SPECIFIC action, not its general theme. FORBIDDEN reflexes: ""Audit the X"", ""Identify the X"", ""Manage your X"" / ""Build your X"", ""X-Minute"" framings, anything starting with a corporate verb (Optimise, Strategise, Leverage). A strong title names a concrete artefact, move, or question. Title test: read it alone — can a reader guess what the move IS? If it could fit ten different actions, write a sharper one.

   b) NUDGE_RATIONALE — maximum 25 words. One sentence on why this move fits THIS user this week. Reference something specific they wrote.

   c) NUDGE_ACTION — maximum 90 words total (or 60 if no caveat is required). The concrete move. Must be: specific enough to do this week; counterintuitive or non-obvious (not the most predictable next step); grounded in the Clover ERA framework; personal to their situation; concrete behaviour, not platitude. Lead with the move; explain only what's needed.

   CRITICAL — self-contained writing: the user only ever sees this snapshot. They have NO access to worksheets, books, or any library asset. Do NOT write ""use the framework from worksheet V8"" or ""apply the Vulnerability Circle method"". Instead, EXPLAIN the concept inline in your own words within the action.

   IMPORTANT — locate the agency correctly: when the user's situation is shaped primarily by an organisational or leadership failure (a manager going silent, a leader withholding information, a CEO labelling pushback as negativity, a workload culture punishing honesty), the nudge MUST focus on the conversation, boundary, or specific move the user needs to make IN RESPONSE. Do NOT reframe the user's response as the problem (""manage your own reactivity"", ""build your own discipline""). That transfers the burden of fixing a system failure onto the person the system failed. When the situation IS something the user has agency over (their own time, learning, reflection), personal-discipline moves are fair game.

   CRITICAL — respect stated constraints: if the user has said something is confidential, contractually restricted, or off-limits, any example phrasing you suggest MUST NOT, if spoken verbatim, violate that constraint. Keep example sentences abstract enough to be safe to speak.

   CRITICAL — safety caveat for upward-challenge moves: when the action involves challenging a manager, partner, executive, or other higher-status person (pushing back, raising a risk, asking a pointed question, naming a pattern, surfacing a cost), you MUST add a brief caveat AND a softer alternative. Shape: [main action under 50 words] This may not be the right move if [risk clause under 15 words]. A lower-stakes version: [softer alternative under 25 words]. The softer alternative must be a real move (a shared doc, a peer first, written-not-spoken), not ""just think about it."" Mandatory whenever the primary verb is challenge / push back / raise / confront / surface / name / tell / ask the partner/manager/CEO/board. Not required for purely internal or peer-level actions.

   d) NUDGE_DRAWING_FROM — an array (may be EMPTY) of 0-3 Clover ERA concepts or methods you drew on. Each: { ""source"": ""label"", ""what"": ""the concept/method you drew from"" }. If [clover-content: SOURCE] blocks were provided above, each ""source"" MUST be the exact SOURCE label from one of those blocks — copy it verbatim; never invent a label or cite a passage that wasn't shown. If no blocks were provided, use a short concept label and do not invent worksheet codes or book titles.

Output strict JSON only:
{
  ""headline"": ""..."",
  ""summary_body"": ""2 sentences"",
  ""productivity_observation"": """",
  ""pillar_framing"": ""1 sentence"",
  ""nudge_title"": ""3-7 word memorable name"",
  ""nudge_rationale"": ""1 sentence — why this fits THIS user"",
  ""nudge_action"": ""the specific move"",
  ""nudge_drawing_from"": [ { ""source"": ""..."", ""what"": ""..."" } ]
}

Constraints:
- All prose should sound like a thoughtful peer who knows the framework well, not a coach or a corporate report.
- No motivational language anywhere (""you've got this"", ""keep going"", ""stay strong"").
- Reference specifics from the user's actual words.
- The ""I wouldn't have thought of that"" quality matters in the nudge: if the action is the most predictable next step, you haven't done the work.

POINT OF VIEW (critical — applies to ALL prose):
Write in the SECOND PERSON throughout. Address the user as ""you"" / ""your"". Never refer to the user by name in the third person, and never write about them as ""the user"". This covers headline, summary, pillar_framing, nudge_rationale, and nudge_action.

QUOTATION DISCIPLINE (critical):
Quotation marks are reserved for verbatim text the user actually wrote. If a phrase is YOUR synthesis or a framework concept, do NOT put it in quote marks — embed it inline as your own observation. Putting concepts in quote marks is misattribution and breaks trust.

CONTEXT-INVENTION DISCIPLINE (critical):
Do NOT invent specific events, people, meetings, or artefacts the user did NOT mention. Every specific noun referring to an event/meeting/person in your output must be traceable to the user's input. If it isn't there, use a generic placeholder (""your next 1:1 if you have one scheduled"") or drop the reference.";
    }
}
