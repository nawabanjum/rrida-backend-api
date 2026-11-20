using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Pgvector;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Tool definitions + executor for EranCoach retrieval. Exact-id lookups
    /// (get_worksheet / get_micro_action) hit the corpus namespace directly;
    /// search_clover embeds the query and does a cosine search over the same namespace.
    /// </summary>
    public class EranCoachTools : IEranCoachTools
    {
        private const int SearchLimit = 8;
        private const int BookExcerptChars = 1400;

        private readonly IEranCoachCorpusRepository _corpus;
        private readonly IEmbeddingRepository _embedding;
        private readonly IEranCoachReportService _reports;
        private readonly ILogger<EranCoachTools> _logger;

        public EranCoachTools(
            IEranCoachCorpusRepository corpus,
            IEmbeddingRepository embedding,
            IEranCoachReportService reports,
            ILogger<EranCoachTools> logger)
        {
            _corpus = corpus;
            _embedding = embedding;
            _reports = reports;
            _logger = logger;
        }

        // Set per request from EranCoachChatRequestDto.UnitId; the default team for the report tools.
        public long? CurrentUnitId { get; set; }

        // Plan: Clover Resources (worksheets) are Pro; false for a Light manager's request.
        public bool WorksheetsAllowed { get; set; } = true;

        private const string WorksheetsNotInPlan =
            "Worksheets (Clover Resources) are not part of this manager's plan. Do not name, quote or describe any " +
            "worksheet. Coach with micro-actions and practical steps instead; if they ask for a worksheet, say " +
            "Clover Resources come with the Pro plan.";

        public IReadOnlyList<object> Definitions { get; } = new List<object>
        {
            new
            {
                name = "get_worksheet",
                description = "Retrieve the full guided worksheet (facilitation playbook) by its ID, e.g. 'C1', 'V8', 'R3'. Returns the complete session: what to say, phases, facilitation notes, and cross-pillar links. Use this whenever you want to coach a manager through a specific worksheet or quote its exact language.",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string", description = "The worksheet ID, e.g. 'C1', 'V8', 'OP3', 'R14'." }
                    },
                    required = new[] { "id" }
                }
            },
            new
            {
                name = "get_micro_action",
                description = "Retrieve a full micro-action card by its ID, e.g. 'MA-C-061'. Returns the issue it targets and the immediate, low-effort moves a manager can make today. Use this for quick, in-the-moment interventions rather than a full session.",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string", description = "The micro-action ID, e.g. 'MA-C-061', 'MA-V-012'." }
                    },
                    required = new[] { "id" }
                }
            },
            new
            {
                name = "search_clover",
                description = "Keyword/semantic search across the entire Clover library (worksheets, micro-actions, and the books). Use this to find the right worksheet/micro-action for a described situation, or to pull supporting ideas from the books. Returns ranked snippets with their IDs; follow up with get_worksheet / get_micro_action to read the full content.",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string", description = "What you're looking for, in the manager's own words, e.g. 'high performer gone quiet and disengaged'." },
                        source = new { type = "string", @enum = new[] { "worksheet", "micro_action", "book" }, description = "Optional. Limit the search to one source type. Omit to search everything." }
                    },
                    required = new[] { "query" }
                }
            },
            new
            {
                name = "get_team_report",
                description = "Retrieve a team engagement report from the RRIDA dashboard: the Top Themes, Top Recommendations, and Top Actions saved from that team's feedback. Works for any team this manager's role gives them access to — for an admin that is every team in their company (or the whole system), not only teams they personally manage. Use this whenever they ask about a report, top themes/recommendations/actions, or a question a team's own data would answer. To choose the team, prefer teamName (what they call it) — the server matches it against the teams they can access. Omit unitId and teamName to use the team this chat is about, or their only team. Omit reportId for the latest report. If the team is ambiguous, this returns the matching teams to choose from instead.",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        teamName = new { type = "string", description = "Optional. The team name as the manager refers to it, e.g. 'DemoTeam 1.3'. The server matches it (case/space-insensitive) against the teams this manager can access. Use this instead of asking the manager for a numeric id." },
                        unitId = new { type = "integer", description = "Optional. The team (business unit) id, if you already have it (e.g. from find_team or list_team_reports). Only teams the manager can see are accepted." },
                        reportId = new { type = "integer", description = "Optional. A specific report id (from list_team_reports). Omit for the most recent report." }
                    }
                }
            },
            new
            {
                name = "find_team",
                description = "Look up which of the manager's teams match a name they gave, returning each match with its team id. Use this to resolve a team when the manager names one and you don't have its id, especially when they oversee many teams. Never use search_clover to find a team; that searches the coaching library, not the manager's teams.",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        teamName = new { type = "string", description = "The team name to look for, as the manager said it, e.g. 'DemoTeam 1.3'." }
                    },
                    required = new[] { "teamName" }
                }
            },
            new
            {
                name = "list_team_reports",
                description = "List what this manager can pull: the teams they have access to, or a single team's available reports (id and date range). Use this when the manager asks which reports exist, or to find an earlier report's id before calling get_team_report. Pass teamName or unitId to list that team's reports; omit both to list the teams the manager can access.",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        teamName = new { type = "string", description = "Optional. A team name to resolve and list reports for." },
                        unitId = new { type = "integer", description = "Optional. A team (business unit) id to list reports for. Omit both this and teamName to list the teams the manager can access." }
                    }
                }
            }
        };

        public async Task<string> RunAsync(string toolName, string inputJson, CancellationToken ct = default)
        {
            try
            {
                using var doc = string.IsNullOrWhiteSpace(inputJson)
                    ? JsonDocument.Parse("{}")
                    : JsonDocument.Parse(inputJson);
                var root = doc.RootElement;

                switch (toolName)
                {
                    case "get_worksheet":
                        if (!WorksheetsAllowed) return WorksheetsNotInPlan;
                        return await GetWorksheetAsync(Str(root, "id"), ct);
                    case "get_micro_action":
                        return await GetMicroActionAsync(Str(root, "id"), ct);
                    case "search_clover":
                        return await SearchAsync(Str(root, "query"), Str(root, "source"), ct);
                    case "get_team_report":
                        return await _reports.GetReportInsightsAsync(Long(root, "unitId"), Long(root, "reportId"), Str(root, "teamName"), CurrentUnitId, ct);
                    case "find_team":
                        return await _reports.FindTeamAsync(Str(root, "teamName"), ct);
                    case "list_team_reports":
                        return await _reports.ListReportsAsync(Long(root, "unitId"), Str(root, "teamName"), CurrentUnitId, ct);
                    default:
                        return $"Unknown tool: {toolName}";
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "[EranCoach] tool '{Tool}' failed.", toolName);
                return $"Tool {toolName} error: {e.Message}";
            }
        }

        private async Task<string> GetWorksheetAsync(string id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(id)) return "get_worksheet needs an 'id'.";
            var row = await _corpus.GetByCloverIdAsync(EranCoachCorpusLoader.SourceWorksheet, id, ct);
            if (row == null)
                return $"No worksheet found with ID \"{id}\". Worksheet IDs look like C1, L2, OP3, V8, E5, R14. Use search_clover to find the right one.";
            return row.Content ?? "";
        }

        private async Task<string> GetMicroActionAsync(string id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(id)) return "get_micro_action needs an 'id'.";
            var row = await _corpus.GetByCloverIdAsync(EranCoachCorpusLoader.SourceMicroAction, id, ct);
            if (row == null)
                return $"No micro-action found with ID \"{id}\". IDs look like MA-C-061. Use search_clover to find one.";
            return row.Content ?? "";
        }

        private async Task<string> SearchAsync(string query, string? source, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(query)) return "search_clover needs a 'query'.";

            var sourceType = source switch
            {
                "worksheet" => EranCoachCorpusLoader.SourceWorksheet,
                "micro_action" => EranCoachCorpusLoader.SourceMicroAction,
                "book" => EranCoachCorpusLoader.SourceBook,
                _ => null
            };

            var vec = await _embedding.GetEmbeddingAsync(query, ct);
            if (!WorksheetsAllowed && sourceType == EranCoachCorpusLoader.SourceWorksheet)
                return WorksheetsNotInPlan;

            var hits = await _corpus.SearchAsync(new Vector(vec), SearchLimit, sourceType, ct);
            // Plan without Clover Resources: worksheets never come back from the library search.
            if (!WorksheetsAllowed)
                hits = hits.Where(h => h.SourceType != EranCoachCorpusLoader.SourceWorksheet).ToList();

            if (hits.Count == 0)
                return $"No matches in the Clover library for \"{query}\". Try different words, or tell the manager this specific topic isn't covered in the library yet.";

            var sb = new StringBuilder();
            sb.AppendLine($"Top matches for \"{query}\":");
            sb.AppendLine();
            foreach (var h in hits)
            {
                if (h.SourceType == EranCoachCorpusLoader.SourceBook)
                {
                    var excerpt = h.Content ?? "";
                    if (excerpt.Length > BookExcerptChars) excerpt = excerpt.Substring(0, BookExcerptChars).Trim() + "…";
                    sb.AppendLine($"<<VERBATIM EXCERPT — {h.FileName} — you may quote ONLY text found between these markers, word for word>>");
                    sb.AppendLine(excerpt);
                    sb.AppendLine("<<END EXCERPT>>");
                }
                else
                {
                    sb.AppendLine($"[{h.SourceType}] {h.Title}");
                }
                sb.AppendLine();
            }
            sb.AppendLine("Rules for using these results: quote a book only from inside the VERBATIM EXCERPT markers, word for word. Do not add numbers, dates, or specifics that are not in the excerpt. Retrieve full worksheet/micro-action content with get_worksheet(id) or get_micro_action(id).");
            return sb.ToString();
        }

        private static string Str(JsonElement root, string name)
            => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? (el.GetString() ?? "") : "";

        // Reads an optional numeric tool arg. Accepts a JSON number or a numeric string; null otherwise.
        private static long? Long(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var el)) return null;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var n)) return n;
            if (el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), out var s)) return s;
            return null;
        }
    }
}
