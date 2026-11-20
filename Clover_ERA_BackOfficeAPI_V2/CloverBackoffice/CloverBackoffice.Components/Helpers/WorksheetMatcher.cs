using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Components.Helpers
{
    public static class WorksheetMatcher
    {
        // SINGLE SOURCE OF TRUTH for the worksheet "pillars". Each pillar owns the
        // canonical code prefix used in worksheet_library.json and the real file
        // names (e.g. "OP1"), its display name, and the aliases by which callers
        // may reference it — the single-letter initial and the full pillar name(s).
        // Both filename matching and the anchor-label builder derive from this list,
        // so adding a pillar or alias here updates every consumer automatically.
        public sealed record WorksheetPillar(string CanonicalCode, string DisplayName, string[] Aliases);

        public static readonly IReadOnlyList<WorksheetPillar> Pillars = new[]
        {
            new WorksheetPillar("C",  "Communication", new[] { "C",  "Communication" }),
            new WorksheetPillar("L",  "Learning",      new[] { "L",  "Learning" }),
            new WorksheetPillar("OP", "Opportunity",   new[] { "O",  "OP", "Opportunity", "Opportunities" }),
            new WorksheetPillar("V",  "Vulnerability", new[] { "V",  "Vulnerability" }),
            new WorksheetPillar("E",  "Enablement",    new[] { "E",  "Enablement" }),
            new WorksheetPillar("R",  "Reflection",    new[] { "R",  "Reflection" }),
        };

        // alias (case-insensitive) -> pillar. Built once from Pillars above.
        private static readonly IReadOnlyDictionary<string, WorksheetPillar> PillarByAlias =
            Pillars
                .SelectMany(p => p.Aliases.Select(a => (Alias: a, Pillar: p)))
                .ToDictionary(x => x.Alias, x => x.Pillar, StringComparer.OrdinalIgnoreCase);

        // Resolves a requested worksheet filename to an actual file in folderPath by
        // comparing the CANONICAL worksheet code of each side. The stored files are
        // named by bare code ("OP6.pdf"), while requests may carry a "Worksheet "
        // prefix and any pillar alias ("Worksheet O6.pdf", "Worksheet OP6.pdf",
        // "O6.pdf") — all canonicalize to "op6" and resolve to "OP6.pdf". Matching
        // is case-insensitive. Returns the matched file's full path, or null when
        // nothing matches.
        public static string? ResolveByPillarAlias(string folderPath, string requestedFileName)
        {
            if (string.IsNullOrWhiteSpace(folderPath) ||
                string.IsNullOrWhiteSpace(requestedFileName) ||
                !Directory.Exists(folderPath))
                return null;

            var requestedExt = Path.GetExtension(requestedFileName);   // may be empty
            var requestedCode = ExtractCanonicalCode(Path.GetFileNameWithoutExtension(requestedFileName));
            if (string.IsNullOrEmpty(requestedCode))
                return null;

            foreach (var candidate in Directory.GetFiles(folderPath))
            {
                // When the request carried an extension, only consider candidates
                // with the same one; otherwise accept any extension.
                if (!string.IsNullOrEmpty(requestedExt) &&
                    !string.Equals(Path.GetExtension(candidate), requestedExt, StringComparison.OrdinalIgnoreCase))
                    continue;

                var candidateCode = ExtractCanonicalCode(Path.GetFileNameWithoutExtension(candidate));

                if (!string.IsNullOrEmpty(candidateCode) &&
                    string.Equals(requestedCode, candidateCode, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }

            return null;
        }

        // Extracts the canonical worksheet code from a filename base. Tokenizes on
        // space/underscore/hyphen and scans from the LAST token for one shaped like a
        // pillar code (letters + optional digits) whose letter prefix is a known
        // pillar alias, then maps that alias to the pillar's canonical code, keeping
        // any trailing number. So "Worksheet O6" -> "op6", "O6" -> "op6",
        // "Worksheet OP6" -> "op6", "OP6" -> "op6". Returns null when no token is a
        // recognizable pillar code.
        public static string? ExtractCanonicalCode(string? baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName)) return null;

            var tokens = baseName.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = tokens.Length - 1; i >= 0; i--)
            {
                // Split leading letters from trailing digits: "OP12" -> ("OP","12").
                var m = Regex.Match(tokens[i], "^([A-Za-z]+)([0-9]*)$");
                if (m.Success && PillarByAlias.TryGetValue(m.Groups[1].Value, out var pillar))
                    return (pillar.CanonicalCode + m.Groups[2].Value).ToLowerInvariant();
            }

            return null;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Prose linkification — turns worksheet references written inside free text
        // ("Worksheet E3, Empowerment Through Delegation") into download anchors.
        // ─────────────────────────────────────────────────────────────────────────────

        // The coach prompt mandates "Worksheet" + code ("Worksheet C9", never bare "C9"),
        // so the reference always carries the literal word. Letters are validated against
        // PillarByAlias afterwards, which is what keeps "Worksheet Ab12" from matching.
        private static readonly Regex WorksheetReferenceRegex = new(
            @"\bWorksheets?\s+([A-Za-z]{1,2})[\s\-]?([0-9]{1,3})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Longest text a single reference can occupy ("Worksheet" + separators + code).
        // Used by the streaming linkifier to decide how much tail to hold back.
        internal const int MaxReferenceLength = 24;

        private static readonly ConcurrentDictionary<string, (DateTime LoadedAt, HashSet<string> Codes)> CodeCache = new();
        private static readonly TimeSpan CodeCacheTtl = TimeSpan.FromMinutes(5);

        // Canonical codes ("e3", "op1") that actually have a file on disk. References to
        // codes present in the catalog but missing a PDF are left as plain text rather
        // than rendered as a link that would download nothing.
        public static HashSet<string> GetAvailableCodes(string? folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (CodeCache.TryGetValue(folderPath, out var cached) &&
                DateTime.UtcNow - cached.LoadedAt < CodeCacheTtl)
                return cached.Codes;

            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var file in Directory.GetFiles(folderPath))
                {
                    var code = ExtractCanonicalCode(Path.GetFileNameWithoutExtension(file));
                    if (!string.IsNullOrEmpty(code)) codes.Add(code!);
                }
            }
            catch
            {
                // Unreadable folder — fall through with whatever we managed to collect.
            }

            CodeCache[folderPath] = (DateTime.UtcNow, codes);
            return codes;
        }

        /// <summary>
        /// Wraps every worksheet reference in <paramref name="text"/> in a download anchor.
        /// The anchor label keeps the author's own wording ("Worksheet E3") so the prose is
        /// unchanged; only the href is added. When <paramref name="worksheetFolderPath"/> is
        /// supplied, references without a matching PDF are left untouched.
        /// </summary>
        public static string? LinkifyWorksheetReferences(string? text, string? worksheetFolderPath = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            if (text.IndexOf("Worksheet", StringComparison.OrdinalIgnoreCase) < 0) return text;

            var available = worksheetFolderPath == null ? null : GetAvailableCodes(worksheetFolderPath);

            return WorksheetReferenceRegex.Replace(text, match =>
            {
                if (!PillarByAlias.TryGetValue(match.Groups[1].Value, out var pillar))
                    return match.Value; // not a real pillar — leave the text alone

                var code = $"{pillar.CanonicalCode}{match.Groups[2].Value}";

                if (available != null && !available.Contains(code))
                    return match.Value; // no file behind it; a link would download nothing

                // Files are stored by bare canonical code ("E3.pdf", "OP1.pdf").
                var apiUrl = $"/api/rag/get-worksheet-file?filename={Uri.EscapeDataString($"{code}.pdf")}";
                return $"<a href=\"{apiUrl}\" target=\"_blank\">{match.Value}</a>";
            });
        }

        public static string? BuildWorksheetAnchor(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return fileName;

            var apiUrl = $"/api/rag/get-worksheet-file?filename={Uri.EscapeDataString(fileName)}";

            // Capture the pillar alias (letters) + number, e.g. "OP1", "O1", "C10".
            // The alias is resolved against the same canonical pillar map used for
            // file matching, so single-letter ("O") and full-code ("OP") prefixes
            // both render the right category label.
            var match = Regex.Match(
                fileName,
                @"Worksheet\s+([A-Za-z]+)(\d+)",
                RegexOptions.IgnoreCase);

            string label = fileName;
            if (match.Success && PillarByAlias.TryGetValue(match.Groups[1].Value, out var pillar))
            {
                var number = match.Groups[2].Value;
                label = $"{pillar.DisplayName} {number}";
            }

            return $"<a href=\"{apiUrl}\" target=\"_blank\">{label}</a>";
        }

        public static string? MatchWorksheetFromDb(
            List<PromptfooTestResultDto> tests,
            List<(string FileName, string Content)> worksheetCorpus)
        {
            if (worksheetCorpus == null || worksheetCorpus.Count == 0) return null;

            var sb = new StringBuilder();
            if (tests != null)
            {
                foreach (var t in tests)
                {
                    if (!string.IsNullOrWhiteSpace(t?.Response))
                        sb.Append(' ').Append(t.Response);
                }
            }
            var predictionText = sb.ToString().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(predictionText))
                return worksheetCorpus[0].FileName;

            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "the","and","for","with","this","that","your","from","are","was","were","has","have","had",
                "will","would","should","could","their","there","which","when","what","about","into","over",
                "complete","element","number","title","worksheet","action","step","steps","title","text",
                "area","urgency","top","high","critical","standard","true","false","null","string","score",
                "status","json","parse","parsed","assertion","passed"
            };

            var predictionTokens = Regex
                .Split(predictionText, "[^a-z0-9]+")
                .Where(tk => tk.Length >= 4 && !stopWords.Contains(tk))
                .Distinct()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            string? bestFile = null;
            int bestScore = -1;

            foreach (var (file, content) in worksheetCorpus)
            {
                if (string.IsNullOrWhiteSpace(file)) continue;

                var baseName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                var blob = $"{baseName} {content ?? string.Empty}".ToLowerInvariant();

                int score = 0;
                if (!string.IsNullOrWhiteSpace(baseName) && predictionText.Contains(baseName))
                    score += 500;

                var codeTokens = Regex
                    .Split(baseName, "[^a-z0-9]+")
                    .Where(tk => tk.Length >= 2);
                foreach (var code in codeTokens)
                {
                    if (predictionText.Contains(code)) score += 5;
                }

                foreach (var tk in predictionTokens)
                {
                    if (blob.Contains(tk)) score++;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestFile = file;
                }
            }

            return FormatWorksheetName(bestFile ?? worksheetCorpus[0].FileName);
        }

        public static string FormatWorksheetName(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            var normalized = path.Replace('\\', '/');
            var fileName = normalized.Substring(normalized.LastIndexOf('/') + 1).Trim();
            if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                fileName = $"{fileName}.pdf";
            if (fileName.StartsWith("Worksheet ", StringComparison.OrdinalIgnoreCase))
                return fileName;
            return $"Worksheet {fileName}";
        }

        public static PromptfooEvalResultDto? ExtractEvalResultFromResultJson(string? resultJson)
        {
            if (string.IsNullOrWhiteSpace(resultJson)) return null;

            try
            {
                using var doc = JsonDocument.Parse(resultJson);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (TryGetPropertyCaseInsensitive(root, "Result", out var resultProp) &&
                        resultProp.ValueKind == JsonValueKind.Object)
                    {
                        return resultProp.Deserialize<PromptfooEvalResultDto>(new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    }

                    if (TryGetPropertyCaseInsensitive(root, "PromptId", out _))
                    {
                        return JsonSerializer.Deserialize<PromptfooEvalResultDto>(resultJson, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    }
                }
            }
            catch { }

            try
            {
                return JsonSerializer.Deserialize<PromptfooEvalResultDto>(resultJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGetPropertyCaseInsensitive(JsonElement element, string name, out JsonElement value)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }
    }
}
