using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Report retrieval + role scoping for the EranCoach report tools. Reads the caller from the
    /// request token, resolves the units they may see, then returns stored report insights (or
    /// resolves a team by name) for the model. Text-only output: whatever it returns is handed
    /// straight back to the model as a tool_result.
    /// </summary>
    public class EranCoachReportService : IEranCoachReportService
    {
        // How many teams we list back to the model before asking it to narrow by name.
        private const int TeamListCap = 40;
        // How many name matches we hand back for an ambiguous name.
        private const int MatchCap = 25;

        private readonly ITokenReaderService _tokenReader;
        private readonly IUnitAccessService _unitAccess;
        private readonly IEranCoachReportRepository _repo;
        private readonly ILogger<EranCoachReportService> _logger;

        public EranCoachReportService(
            ITokenReaderService tokenReader,
            IUnitAccessService unitAccess,
            IEranCoachReportRepository repo,
            ILogger<EranCoachReportService> logger)
        {
            _tokenReader = tokenReader;
            _unitAccess = unitAccess;
            _repo = repo;
            _logger = logger;
        }

        public async Task<string> GetReportInsightsAsync(long? requestedUnitId, long? reportId, string? teamName, long? fallbackUnitId, CancellationToken ct = default)
        {
            var scope = await ResolveScopeAsync(ct);
            if (scope.Error != null) return scope.Error;

            // Resolve which team, in priority order: explicit id, then a name the manager gave,
            // then the team this chat is scoped to, then their only team, else ask.
            long? target = null;

            if (requestedUnitId is long rid && rid > 0)
            {
                if (!scope.Units.Contains(rid))
                    return "That team is not one this manager has access to. Ask them which of their own teams they mean; do not ask for a numeric id.";
                target = rid;
            }

            if (target is null && !string.IsNullOrWhiteSpace(teamName))
            {
                var matches = FindTeamsByName(scope.Names, teamName!);
                if (matches.Count == 1) target = matches[0].Id;
                else if (matches.Count > 1) return FormatMatches(teamName!, matches, forReport: true);
                else return NoNameMatch(teamName!);
            }

            if (target is null && fallbackUnitId is long fid && fid > 0 && scope.Units.Contains(fid))
                target = fid;

            if (target is null && scope.Units.Count == 1)
                target = scope.Units[0];

            if (target is null)
                return BuildTeamPickList(scope);

            var unitId = target.Value;

            var report = reportId is long r && r > 0
                ? await _repo.GetReportAsync(unitId, r, ct)
                : await _repo.GetLatestReportAsync(unitId, ct);

            var name = NameOf(scope, unitId);

            if (report == null)
            {
                return reportId is long rr && rr > 0
                    ? $"There is no report #{rr} for {name} (team #{unitId})."
                    : $"{name} (team #{unitId}) doesn't have any generated reports yet.";
            }

            var themes = await _repo.GetTopThemesAsync(unitId, report.Id, ct);
            var recommendations = await _repo.GetTopRecommendationsAsync(unitId, report.Id, ct);
            var actions = await _repo.GetTopActionsAsync(unitId, report.Id, ct);

            if (themes.Count == 0 && recommendations.Count == 0 && actions.Count == 0)
            {
                // The latest report is empty — point the model at earlier ones if any exist.
                var all = await _repo.GetReportsAsync(unitId, ct);
                var others = all.Where(x => x.Id != report.Id).ToList();
                var tail = others.Count > 0
                    ? $" Earlier reports exist ({others.Count}); call list_team_reports for their dates, or get_team_report with a reportId to check one."
                    : "";
                return $"The latest report for {name} (team #{unitId}, {DateRange(report)}) has no saved themes, recommendations, or actions.{tail}";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"## TEAM REPORT — {name} (team #{unitId})");
            sb.AppendLine($"Report period: {DateRange(report)}. Generated {Date(report.CreatedAt)}. Report id {report.Id}.");
            sb.AppendLine(
                "This is the manager's own stored CLOVER feedback report. Use only what is listed here; " +
                "never invent themes, recommendations, actions, numbers, or dates beyond it. Tell the manager " +
                "which report period this is from.");
            sb.AppendLine();
            AppendList(sb, "Top Themes", themes);
            AppendList(sb, "Top Recommendations", recommendations);
            AppendList(sb, "Top Actions", actions);

            return sb.ToString().TrimEnd();
        }

        public async Task<string> ListReportsAsync(long? requestedUnitId, string? teamName, long? fallbackUnitId, CancellationToken ct = default)
        {
            var scope = await ResolveScopeAsync(ct);
            if (scope.Error != null) return scope.Error;

            long? target = requestedUnitId is long rid && rid > 0 ? rid : null;

            if (target is long t && !scope.Units.Contains(t))
                return "That team is not one this manager has access to.";

            if (target is null && !string.IsNullOrWhiteSpace(teamName))
            {
                var matches = FindTeamsByName(scope.Names, teamName!);
                if (matches.Count == 1) target = matches[0].Id;
                else if (matches.Count > 1) return FormatMatches(teamName!, matches, forReport: false);
                else return NoNameMatch(teamName!);
            }

            if (target is null && fallbackUnitId is long fid && fid > 0 && scope.Units.Contains(fid))
                target = fid;

            if (target is null && scope.Units.Count == 1)
                target = scope.Units[0];

            if (target is long unit)
            {
                var name = NameOf(scope, unit);
                var reports = await _repo.GetReportsAsync(unit, ct);
                if (reports.Count == 0)
                    return $"{name} (team #{unit}) has no reports yet.";

                var sb = new StringBuilder();
                sb.AppendLine($"Reports available for {name} (team #{unit}), most recent first:");
                foreach (var r in reports)
                    sb.AppendLine($"- report #{r.Id}: {DateRange(r)}");
                sb.AppendLine();
                sb.AppendLine("Pull one with get_team_report(unitId, reportId), or omit reportId for the latest.");
                return sb.ToString().TrimEnd();
            }

            // Many teams and no name — ask which one.
            return BuildTeamPickList(scope);
        }

        public async Task<string> FindTeamAsync(string teamName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(teamName))
                return "find_team needs a team name to look for.";

            var scope = await ResolveScopeAsync(ct);
            if (scope.Error != null) return scope.Error;

            var matches = FindTeamsByName(scope.Names, teamName);
            if (matches.Count == 0) return NoNameMatch(teamName);
            if (matches.Count == 1)
            {
                var m = matches[0];
                return $"Found one team matching \"{teamName.Trim()}\": {m.Name} (team #{m.Id}). Pull its report with get_team_report(unitId: {m.Id}).";
            }
            return FormatMatches(teamName, matches, forReport: true);
        }

        // ── scope + caller ───────────────────────────────────────────────────────────────────

        private sealed class Scope
        {
            public IReadOnlyList<long> Units { get; init; } = Array.Empty<long>();
            public IReadOnlyDictionary<long, string> Names { get; init; } = new Dictionary<long, string>();
            public string? Error { get; init; }   // non-null => return this text straight to the model
        }

        private async Task<Scope> ResolveScopeAsync(CancellationToken ct)
        {
            var user = await _tokenReader.GetCurrentUserAsync();
            if (user == null)
                return new Scope { Error = "I can't tell who you are from this session, so I can't pull your team reports." };

            List<long> units;
            try
            {
                units = await _unitAccess.GetAccessibleUnitIdsAsync(user, ct);
            }
            catch (UnauthorizedAccessException)
            {
                units = new List<long>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[EranCoach] failed to resolve accessible units for user {UserId}.", user.UserId);
                return new Scope { Error = "I hit a problem looking up which teams you can see. Try again in a moment." };
            }

            if (units.Count == 0)
                return new Scope { Units = units, Error = "This manager's account doesn't have any teams with reports attached to it." };

            // Names are needed for name-matching and for every message that mentions a team, so
            // fetch them once per tool call.
            var names = await _repo.GetUnitNamesAsync(units, ct);
            return new Scope { Units = units, Names = names };
        }

        // ── name matching ────────────────────────────────────────────────────────────────────

        // Match a manager-typed name against the teams they can access. Whitespace and case are
        // ignored so "Demo team 1.1", "DemoTeam 1.1" and "demoteam1.1" all match the same unit.
        // Exact matches win outright; otherwise starts-with beats contains.
        private static List<(long Id, string Name)> FindTeamsByName(IReadOnlyDictionary<long, string> names, string query)
        {
            var q = Normalize(query);
            if (q.Length == 0) return new List<(long, string)>();

            var scored = new List<(long Id, string Name, int Rank)>();
            foreach (var kv in names)
            {
                var n = Normalize(kv.Value);
                if (n.Length == 0) continue;

                int rank;
                if (n == q) rank = 0;
                else if (n.StartsWith(q, StringComparison.Ordinal)) rank = 1;
                else if (n.Contains(q, StringComparison.Ordinal)) rank = 2;
                else continue;

                scored.Add((kv.Key, kv.Value ?? "(unnamed)", rank));
            }

            // If there's an exact match, that's the answer — don't dilute it with partials.
            var exact = scored.Where(s => s.Rank == 0).Select(s => (s.Id, s.Name)).ToList();
            if (exact.Count > 0) return exact;

            return scored
                .OrderBy(s => s.Rank)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .Select(s => (s.Id, s.Name))
                .ToList();
        }

        private static string Normalize(string? s)
            => Regex.Replace((s ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", "");

        private static string FormatMatches(string query, List<(long Id, string Name)> matches, bool forReport)
        {
            var shown = matches.Take(MatchCap).ToList();
            var sb = new StringBuilder();
            sb.AppendLine($"More than one of this manager's teams matches \"{query.Trim()}\":");
            foreach (var m in shown)
                sb.AppendLine($"- {m.Name} (team #{m.Id})");
            if (matches.Count > shown.Count)
                sb.AppendLine($"...and {matches.Count - shown.Count} more. Ask the manager for the fuller name.");
            sb.AppendLine();
            sb.AppendLine(forReport
                ? "Ask the manager which one they mean, then call get_team_report with that team's unitId."
                : "Ask the manager which one they mean, then call list_team_reports with that team's unitId.");
            return sb.ToString().TrimEnd();
        }

        private static string NoNameMatch(string query)
            => $"None of this manager's teams match \"{query.Trim()}\". Ask them to confirm the exact team name as it appears on their dashboard, then try again. Do not ask them for a numeric team id, and do not use search_clover to find a team.";

        private string BuildTeamPickList(Scope scope)
        {
            if (scope.Units.Count > TeamListCap)
                return $"This manager can access {scope.Units.Count} teams — too many to list. Ask them which team they mean by name, then call get_team_report with that teamName (or find_team to look it up). Never ask them for a numeric team id.";

            var sb = new StringBuilder();
            sb.AppendLine("This manager can see more than one team. Ask which one they mean, then call get_team_report with its teamName or unitId. Teams:");
            foreach (var id in scope.Units)
                sb.AppendLine($"- {NameOf(scope, id)} (team #{id})");
            return sb.ToString().TrimEnd();
        }

        private static string NameOf(Scope scope, long unitId)
            => scope.Names.TryGetValue(unitId, out var n) && !string.IsNullOrWhiteSpace(n) ? n : "this team";

        // ── formatting ───────────────────────────────────────────────────────────────────────

        private static void AppendList(StringBuilder sb, string heading, IReadOnlyList<string> items)
        {
            if (items.Count == 0) return;
            sb.AppendLine($"### {heading}");
            foreach (var item in items)
                sb.AppendLine($"- {item}");
            sb.AppendLine();
        }

        private static string DateRange(LLMReport r) => $"{Date(r.FromDate)} to {Date(r.ToDate)}";
        private static string Date(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
