using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Builds the team-grounding block for EranCoach from a unit's latest engagement report.
    /// Cached 5 minutes per unit (the report is stable within a cycle). Ported from the
    /// reference team-context.ts: what goes in is exactly what the coach may cite — it never
    /// invents figures beyond it.
    /// </summary>
    public class EranCoachTeamContextService : IEranCoachTeamContextService
    {
        private const int TeamContextCap = 40000; // chars, matching the reference cap
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

        private readonly IEranCoachTeamContextRepository _repo;
        private readonly IMemoryCache _cache;
        private readonly ILogger<EranCoachTeamContextService> _logger;

        public EranCoachTeamContextService(
            IEranCoachTeamContextRepository repo,
            IMemoryCache cache,
            ILogger<EranCoachTeamContextService> logger)
        {
            _repo = repo;
            _cache = cache;
            _logger = logger;
        }

        public async Task<string?> GetTeamContextTextAsync(uint unitId, CancellationToken ct = default)
        {
            if (unitId == 0) return null;

            var key = $"erancoach:team:{unitId}";
            var text = await _cache.GetOrCreateAsync(key, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheTtl;
                var insights = await _repo.GetLatestByUnitAsync(unitId, ct);
                return insights == null ? "" : BuildTeamBlock(unitId, insights);
            });

            return string.IsNullOrEmpty(text) ? null : text;
        }

        private string BuildTeamBlock(uint unitId, EngagementInsights e)
        {
            var report = BuildReport(unitId, e);
            if (report.Length > TeamContextCap)
                report = report.Substring(0, TeamContextCap) + "\n\n[team context truncated]";

            return
                "## THIS MANAGER'S TEAM (live from the RRIDA dashboard)\n\n" +
                "This is the team currently selected on the dashboard: its real, current CLOVER-EQ engagement " +
                "report, pulled from the RRIDA system for this session. It is the default team for this " +
                "conversation — use it per the \"When you have the manager's team data\" instructions. It is not " +
                "the only team they may ask about: see \"WHO YOU ARE TALKING TO\" for what their role covers, and " +
                "use the report tools for any other team they name. Never invent scores, quotes, or figures " +
                "beyond what is here.\n\n" + report;
        }

        private string BuildReport(uint unitId, EngagementInsights e)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# CLOVER-EQ Engagement Report — Business Unit {unitId}");
            sb.AppendLine($"Reporting period: {Date(e.PeriodStart)} to {Date(e.PeriodEnd)}. Generated {Date(e.GeneratedAt)}.");
            if (e.OverallScore.HasValue)
                sb.AppendLine($"Overall score: {e.OverallScore.Value.ToString("0.#", CultureInfo.InvariantCulture)}%");
            sb.AppendLine();

            AppendSection(sb, "Element scores", e.CategoryScoresJson);
            AppendSection(sb, "Themes", e.TopThemesJson);
            AppendSection(sb, "Recommendations", e.RecommendationsJson);
            AppendSection(sb, "Recommended actions", e.TopActionsJson);
            AppendSection(sb, "Statistical summary", e.StatisticalSummaryJson);

            return sb.ToString().TrimEnd();
        }

        private static void AppendSection(StringBuilder sb, string heading, string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            sb.AppendLine($"## {heading}");
            sb.AppendLine(Pretty(json));
            sb.AppendLine();
        }

        // Re-indent stored JSON so the model reads it as structured data; fall back to raw.
        private static string Pretty(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (JsonException)
            {
                return json.Trim();
            }
        }

        private static string Date(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
