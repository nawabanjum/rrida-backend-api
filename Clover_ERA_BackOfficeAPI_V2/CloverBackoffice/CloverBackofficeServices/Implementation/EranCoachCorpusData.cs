using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloverBackoffice.Services.Implementation
{
    // ── EranCoach knowledge base ──────────────────────────────────────────────
    // Loads the committed CLOVER content bundle (ported from the ERAn reference's
    // corpus.json: 56 worksheets, 117 micro-actions, book chunks) and produces the
    // exact text the retrieval tools return + the catalog baked into the system prompt.
    // No embeddings here — that happens at seed time; this is the source of truth for
    // what gets embedded (search_clover) and what get_worksheet / get_micro_action return.

    public sealed class EranCoachWorksheet
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("pillar")] public string Pillar { get; set; } = "";
        [JsonPropertyName("difficulty")] public int? Difficulty { get; set; }
        [JsonPropertyName("triggers")] public List<string> Triggers { get; set; } = new();
        [JsonPropertyName("keywords")] public List<string> Keywords { get; set; } = new();
        [JsonPropertyName("tags")] public List<string> Tags { get; set; } = new();
        [JsonPropertyName("neuroscience_anchor")] public string NeuroscienceAnchor { get; set; } = "";
        [JsonPropertyName("body")] public string Body { get; set; } = "";
    }

    public sealed class EranCoachMicroAction
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("issue")] public string Issue { get; set; } = "";
        [JsonPropertyName("pillar")] public string Pillar { get; set; } = "";
        [JsonPropertyName("actions")] public List<string> Actions { get; set; } = new();
    }

    public sealed class EranCoachBookChunk
    {
        [JsonPropertyName("book")] public string Book { get; set; } = "";
        [JsonPropertyName("chunkId")] public string ChunkId { get; set; } = "";
        [JsonPropertyName("text")] public string Text { get; set; } = "";
    }

    public sealed class EranCoachCorpus
    {
        [JsonPropertyName("worksheets")] public List<EranCoachWorksheet> Worksheets { get; set; } = new();
        [JsonPropertyName("microActions")] public List<EranCoachMicroAction> MicroActions { get; set; } = new();
        [JsonPropertyName("bookChunks")] public List<EranCoachBookChunk> BookChunks { get; set; } = new();
    }

    /// <summary>
    /// Loads + caches the corpus bundle from disk and renders catalog / tool text.
    /// Content-shaping mirrors the reference tools.ts / corpus.ts so the coach reads
    /// the same material it was authored against.
    /// </summary>
    public static class EranCoachCorpusLoader
    {
        public const string SourceWorksheet = "worksheet";
        public const string SourceMicroAction = "micro_action";
        public const string SourceBook = "book";

        // Grouping order for the catalog, matching the reference CLOVER_PILLARS.
        public static readonly string[] Pillars =
            { "Communication", "Learning", "Opportunity", "Vulnerability", "Enablement", "Reflection" };

        private static EranCoachCorpus? _cache;
        private static string? _catalogCache;

        public static EranCoachCorpus Load(string contentRootPath)
        {
            if (_cache != null) return _cache;
            var path = Path.Combine(contentRootPath, "CloverEra", "Data", "eran-coach-corpus.json");
            if (!File.Exists(path))
                throw new FileNotFoundException($"EranCoach corpus not found at '{path}'.");
            var json = File.ReadAllText(path, Encoding.UTF8);
            _cache = JsonSerializer.Deserialize<EranCoachCorpus>(json)
                     ?? throw new InvalidOperationException("EranCoach corpus failed to parse.");
            return _cache;
        }

        // ── Catalog for the system prompt (replaces {{CATALOG}}). ──
        public static string BuildCatalog(EranCoachCorpus corpus)
        {
            if (_catalogCache != null) return _catalogCache;
            var sb = new StringBuilder();

            sb.AppendLine("## WORKSHEET LIBRARY (guided team sessions)");
            sb.AppendLine("Each worksheet is a facilitated session. Use get_worksheet(id) to pull the full playbook.");
            sb.AppendLine();
            foreach (var pillar in Pillars)
            {
                var items = corpus.Worksheets.Where(w => w.Pillar == pillar)
                    .OrderBy(w => w.Id, StringComparer.OrdinalIgnoreCase).ToList();
                if (items.Count == 0) continue;
                sb.AppendLine($"### {pillar}");
                foreach (var w in items)
                {
                    var trig = string.Join(", ", w.Triggers.Take(4)).Replace('_', ' ');
                    sb.AppendLine($"- {w.Id} — {w.Title}{(string.IsNullOrWhiteSpace(trig) ? "" : $" — for: {trig}")}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("## MICRO-ACTION LIBRARY (immediate moves)");
            sb.AppendLine("Each micro-action targets one issue with quick moves. Use get_micro_action(id) for the full list.");
            sb.AppendLine();
            foreach (var pillar in Pillars)
            {
                var items = corpus.MicroActions.Where(m => m.Pillar == pillar)
                    .OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToList();
                if (items.Count == 0) continue;
                sb.AppendLine($"### {pillar}");
                foreach (var m in items)
                    sb.AppendLine($"- {m.Id} — {m.Issue}");
                sb.AppendLine();
            }

            _catalogCache = sb.ToString();
            return _catalogCache;
        }

        // ── What get_worksheet / get_micro_action return (stored as DocumentEmbedding.Content). ──
        public static string FormatWorksheet(EranCoachWorksheet w)
        {
            var lines = new List<string>
            {
                $"WORKSHEET {w.Id}: {w.Title}",
                $"Pillar: {w.Pillar}{(w.Difficulty.HasValue ? $" | Difficulty: {w.Difficulty}" : "")}",
            };
            if (w.Triggers.Count > 0) lines.Add($"Use when: {string.Join(", ", w.Triggers).Replace('_', ' ')}");
            if (!string.IsNullOrWhiteSpace(w.NeuroscienceAnchor))
                lines.Add($"Neuroscience anchor: {w.NeuroscienceAnchor.Replace('_', ' ')}");
            lines.Add("");
            lines.Add(w.Body);
            return string.Join("\n", lines.Where(l => l != null));
        }

        public static string FormatMicroAction(EranCoachMicroAction m)
        {
            var lines = new List<string>
            {
                $"MICRO-ACTION {m.Id}",
                $"Issue: {m.Issue}",
                $"Pillar: {m.Pillar}",
                "",
                "Immediate moves:",
            };
            for (int i = 0; i < m.Actions.Count; i++)
                lines.Add($"{i + 1}. {m.Actions[i]}");
            return string.Join("\n", lines);
        }

        // ── Short label used in search snippets (stored as DocumentEmbedding.Title). ──
        public static string WorksheetSnippet(EranCoachWorksheet w)
            => Cap($"{w.Id} — {w.Title} ({w.Pillar}). Triggers: {(w.Triggers.Count > 0 ? string.Join(", ", w.Triggers.Take(4)).Replace('_', ' ') : "n/a")}.", 480);

        public static string MicroActionSnippet(EranCoachMicroAction m)
            => Cap($"{m.Id} — {m.Issue} ({m.Pillar}). {m.Actions.Count} quick actions.", 480);

        public static string BookSnippet(EranCoachBookChunk b)
            => Cap($"[{b.Book}] {b.ChunkId}", 480);

        // ── Text that gets embedded for search_clover (high-signal fields + body). ──
        public static string WorksheetEmbedText(EranCoachWorksheet w)
            => $"{w.Id} {w.Title} {w.Pillar} {string.Join(" ", w.Keywords)} {string.Join(" ", w.Triggers)} {string.Join(" ", w.Tags)} {w.NeuroscienceAnchor} {w.Body}".Replace('_', ' ');

        public static string MicroActionEmbedText(EranCoachMicroAction m)
            => $"{m.Id} {m.Issue} {m.Pillar} {string.Join(" ", m.Actions)}";

        public static string BookEmbedText(EranCoachBookChunk b) => b.Text;

        private static string Cap(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
    }
}
