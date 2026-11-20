using CloverBackoffice.API.Filters;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CloverBackoffice.API.Controllers
{
    /// <summary>
    /// Snapshot Report endpoint. Originally limited to Clovera Personal App units;
    /// that flag is gone, so it now serves any live business unit.
    /// Same request shape as the workbench run, but loads the unit's engagement
    /// data from the DB, runs the hardcoded snapshot prompt against the same Claude
    /// configuration, and persists the result to CloveraPersonalSnapshots.
    /// Async like the workbench: returns a SnapshotId, processed in the background,
    /// polled via the status endpoint.
    /// </summary>
    // Mobile-facing, no user login. JWT is bypassed ([AllowAnonymous] + the route is
    // in TokenAuthentication.skipPaths), and access is instead gated by a static API
    // token (config "CloveraPersonalApp:ApiToken") that the mobile app sends in the
    // X-Api-Key or Authorization: Bearer header — enforced by PersonalAppTokenAuthorize.
    [AllowAnonymous]
    [PersonalAppTokenAuthorize]
    [Route("api/clovera-personal-snapshot")]
    [ApiController]
    public class CloveraPersonalSnapshotController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly CloveraPersonalSnapshotWorkQueue _queue;
        private readonly IFeedbackAnalysisService _feedback;
        private readonly ILogger<CloveraPersonalSnapshotController> _logger;
        private readonly ISnapshotEmailLogService _emailLog;

        public CloveraPersonalSnapshotController(
            ApplicationDbContext db,
            CloveraPersonalSnapshotWorkQueue queue,
            IFeedbackAnalysisService feedback,
            ILogger<CloveraPersonalSnapshotController> logger,
            ISnapshotEmailLogService emailLog)
        {
            _db = db;
            _queue = queue;
            _feedback = feedback;
            _logger = logger;
            _emailLog = emailLog;
        }

        // ── Run (queues a snapshot) ──
        [HttpPost("run")]
        public async Task<IActionResult> Run([FromBody] CloveraPersonalSnapshotRequestDto request, CancellationToken ct)
        {
            // Troubleshooting email: capture the outcome of EVERY Run call (success
            // or failure) and mail the full request/response in the finally below.
            object? responseForLog = null;
            bool success = false;
            string? error = null;

            try
            {
                // profileId is OPTIONAL for the mobile app — it only knows its unitId, so we
                // derive profileId from the unit when it is omitted. unit + date range stay required.
                var hasUnit = request.UnitId.HasValue && request.UnitId.Value > 0;
                var hasDateRange = request.FromDate.HasValue && request.ToDate.HasValue;

                if (!hasUnit || !hasDateRange)
                {
                    var missing = new List<string>();
                    if (!hasUnit) missing.Add("Unit ID is required.");
                    if (!request.FromDate.HasValue) missing.Add("From Date is required.");
                    if (!request.ToDate.HasValue) missing.Add("To Date is required.");
                    error = string.Join(" ", missing);
                    responseForLog = new { error };
                    return Ok(ApiResponseHandler.Fail<string>(error));
                }

                if (request.FromDate!.Value > request.ToDate!.Value)
                {
                    error = "FromDate must be earlier than or equal to ToDate.";
                    responseForLog = new { error };
                    return Ok(ApiResponseHandler.Fail<string>(error));
                }

                // The owning profile used to be a shadow FK named ProfileId; it is now the
                // explicit BusinessUnit.ProfileId column, still surfaced as profileId here
                // so the response shape does not change.
                var unitId = (uint)request.UnitId!.Value;
                var unit = await _db.BusinessUnits
                    .Where(u => u.Id == unitId && (u.IsDel == false || u.IsDel == null))
                    .Select(u => new { u.Id, ProfileId = u.ProfileId })
                    .FirstOrDefaultAsync(ct);

                if (unit == null)
                {
                    error = "Business unit not found.";
                    responseForLog = new { error };
                    return Ok(ApiResponseHandler.Fail<string>(error));
                }

                // An explicit, valid profileId in the request wins; otherwise derive it from the unit.
                var profileId = request.ProfileId.HasValue && request.ProfileId.Value > 0
                    ? (uint)request.ProfileId.Value
                    : unit.ProfileId;
                // There is no personal-app gate any more: the IsCloveraPersonalApp flag
                // was removed, and the check that read it had already been disabled. Any
                // live unit may have a snapshot generated for it.

                var snapshot = new CloveraPersonalSnapshot
                {
                    Status = "Queued",
                    ProfileId = profileId,
                    UnitId = unitId,
                    FromDate = request.FromDate!.Value.UtcDateTime,
                    ToDate = request.ToDate!.Value.UtcDateTime,
                    CreatedAt = DateTime.UtcNow
                };
                _db.CloveraPersonalSnapshots.Add(snapshot);
                await _db.SaveChangesAsync(ct);

                await _queue.Writer.WriteAsync(new CloveraPersonalSnapshotWorkItem
                {
                    SnapshotId = snapshot.Id
                }, ct);

                _logger.LogInformation("[CloveraPersonalSnapshot] Queued SnapshotId={SnapshotId} for UnitId={UnitId}", snapshot.Id, unitId);

                success = true;
                responseForLog = new { SnapshotId = snapshot.Id, Status = "Queued", ProfileId = profileId, UnitId = unitId };
                return Accepted(ApiResponseHandler.Success(
                    new { SnapshotId = snapshot.Id, Status = "Queued" },
                    "Snapshot generation queued."));
            }
            catch (Exception ex)
            {
                error = ex.GetBaseException().Message;
                responseForLog = new { error };
                _logger.LogError(ex, "[CloveraPersonalSnapshot] Run failed for UnitId={UnitId}", request.UnitId);
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
            finally
            {
                // Best-effort (never throws) — see SnapshotEmailLogService.
                await _emailLog.SendRunLogAsync(request, responseForLog, success, error);
            }
        }

        // ── Status (polling) ──
        [HttpGet("{id}/status")]
        public async Task<IActionResult> GetStatus(int id, CancellationToken ct)
        {
            var s = await _db.CloveraPersonalSnapshots
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDel, ct);
            if (s == null)
                return Ok(ApiResponseHandler.Fail<string>("Snapshot not found."));

            object payload = s.Status == "Completed" ? await BuildCompletedPayloadAsync(s, ct) : BuildPendingPayload(s);

            // Troubleshooting email: send the FINAL result once per snapshot. This is
            // a no-op while the snapshot is still Queued/Running, and de-dupes across
            // the repeated polls so it only fires on the first terminal state.
            await _emailLog.SendFinalStatusLogOnceAsync(
                s.Id,
                s.Status,
                request: new { Id = id },
                response: payload,
                success: s.Status == "Completed",
                error: string.Equals(s.Status, "Failed", StringComparison.OrdinalIgnoreCase) ? s.ErrorMessage : null);

            return Ok(ApiResponseHandler.Success(payload, $"Snapshot status: {s.Status}"));
        }

        // ── Personal History (last 15 days, full result per row) ──
        // Personal-app scope: a unit only ever sees its own snapshots, so unitId
        // (the business unit id) is REQUIRED — this is not the admin/global workbench view.
        [HttpGet("personal-history")]
        public async Task<IActionResult> GetPersonalHistory([FromQuery] long? unitId, CancellationToken ct)
        {
            if (!unitId.HasValue || unitId.Value <= 0)
                return Ok(ApiResponseHandler.Fail<string>("Unit ID is required."));
            var u = (uint)unitId.Value;

            var todayUtc = DateTime.UtcNow.Date;
            var fromUtc = todayUtc.AddDays(-15);
            var toUtc = todayUtc.AddDays(1).AddTicks(-1);

            var rows = await _db.CloveraPersonalSnapshots
                .Where(s => !s.IsDel && s.UnitId == u && s.CreatedAt >= fromUtc && s.CreatedAt <= toUtc)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync(ct);

            var history = new List<object>();
            foreach (var s in rows)
            {
                history.Add(new
                {
                    s.Id,
                    s.Status,
                    s.ProfileId,
                    s.UnitId,
                    s.FromDate,
                    s.ToDate,
                    s.ModelUsed,
                    s.CreatedAt,
                    s.CompletedAt,
                    s.ErrorMessage,
                    Result = s.Status == "Completed" ? await BuildResultAsync(s, ct) : null
                });
            }

            return Ok(ApiResponseHandler.Success(history, $"Personal snapshot history for the last 15 days ({history.Count} record(s))."));
        }

        // ── Run List (last 15 days, lightweight) ──
        // Personal-app scope: unitId (business unit id) is REQUIRED.
        [HttpGet("run-list")]
        public async Task<IActionResult> GetRunList([FromQuery] long? unitId, CancellationToken ct)
        {
            if (!unitId.HasValue || unitId.Value <= 0)
                return Ok(ApiResponseHandler.Fail<string>("Unit ID is required."));
            var u = (uint)unitId.Value;

            var todayUtc = DateTime.UtcNow.Date;
            var fromUtc = todayUtc.AddDays(-15);
            var toUtc = todayUtc.AddDays(1).AddTicks(-1);

            var rows = await _db.CloveraPersonalSnapshots
                .Where(s => !s.IsDel && s.UnitId == u && s.CreatedAt >= fromUtc && s.CreatedAt <= toUtc)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new { s.Id, s.Status, s.ProfileId, s.UnitId, s.CreatedAt, s.CompletedAt })
                .ToListAsync(ct);

            return Ok(ApiResponseHandler.Success(rows, $"Personal snapshot run list for the last 15 days ({rows.Count} record(s))."));
        }

        // ── Latest Run Insights (most recent completed snapshot) ──
        // Personal-app scope: unitId (business unit id) is REQUIRED.
        [HttpGet("latest-run-insights")]
        public async Task<IActionResult> GetLatestRunInsights([FromQuery] long? unitId, CancellationToken ct)
        {
            try
            {
                if (!unitId.HasValue || unitId.Value <= 0)
                    return Ok(ApiResponseHandler.Fail<string>("Unit ID is required."));
                var u = (uint)unitId.Value;

                var latest = await _db.CloveraPersonalSnapshots
                    .Where(s => !s.IsDel && s.Status == "Completed" && s.UnitId == u)
                    .OrderByDescending(s => s.Id)
                    .FirstOrDefaultAsync(ct);
                if (latest == null)
                    return Ok(ApiResponseHandler.Fail<string>("No completed snapshots found."));

                return Ok(ApiResponseHandler.Success(await BuildCompletedPayloadAsync(latest, ct), "Latest snapshot insights."));
            }
            catch
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        // ── Snapshot Range List (paginated, lightweight — drives the mobile range dropdown) ──
        // Personal-app scope: unitId (business unit id) is REQUIRED. Returns COMPLETED,
        // non-deleted snapshots only, newest first. The app lists these in the "Snapshot
        // range" dropdown and then fetches a chosen one by id via {id}/status.
        [HttpGet("ranges")]
        public async Task<IActionResult> GetRanges([FromQuery] long? unitId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        {
            if (!unitId.HasValue || unitId.Value <= 0)
                return Ok(ApiResponseHandler.Fail<string>("Unit ID is required."));
            var u = (uint)unitId.Value;

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            // Newest-first across all completed runs for this unit (personal-app scope is
            // small, so we page after de-duping in memory).
            var all = await _db.CloveraPersonalSnapshots
                .Where(s => !s.IsDel && s.Status == "Completed" && s.UnitId == u)
                .OrderByDescending(s => s.CompletedAt ?? s.CreatedAt)
                .ThenByDescending(s => s.Id)
                .Select(s => new { s.Id, s.Status, s.ProfileId, s.UnitId, s.FromDate, s.ToDate, s.CreatedAt, s.CompletedAt })
                .ToListAsync(ct);

            // The same range is often re-run several times; the dropdown should list each
            // range once. De-dup by DAY range (the label is day-based) and keep the newest
            // run per range — the list is already newest-first, so First() wins.
            var distinct = all
                .GroupBy(s => new { From = s.FromDate.Date, To = s.ToDate.Date })
                .Select(g => g.First())
                // Order by the snapshot date range (newest period first), regardless of
                // when the run was generated. FromDate desc, then ToDate desc as tiebreak.
                .OrderByDescending(s => s.ToDate)
                .ThenByDescending(s => s.FromDate)
                .ToList();

            var totalCount = distinct.Count;

            var rows = distinct
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var items = rows.Select(s => new
            {
                s.Id,
                s.Status,
                s.ProfileId,
                s.UnitId,
                s.FromDate,
                s.ToDate,
                Label = BuildRangeLabel(s.FromDate, s.ToDate),
                s.CreatedAt,
                s.CompletedAt
            }).ToList();

            var hasMore = page * pageSize < totalCount;

            var content = new { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount, HasMore = hasMore };
            return Ok(ApiResponseHandler.Success(content, "Snapshot ranges."));
        }

        // ── Shared payload shapes (status / history / latest-insights) ──

        // The full completed contract the mobile UI binds (run metadata + the four UI
        // blocks: overview / output / spotlight / nudge + telemetry). Async because the
        // spotlight bar chart pulls prior cycles for the same pillar.
        private async Task<object> BuildCompletedPayloadAsync(CloveraPersonalSnapshot s, CancellationToken ct)
        {
            var prod = await GetProductivityStatsAsync(s);
            return new
            {
                s.Id,
                s.Status,
                s.ProfileId,
                s.UnitId,
                s.FromDate,
                s.ToDate,
                s.ModelUsed,
                Overview = BuildOverview(s, prod),
                Productivity = BuildProductivity(s, prod),
                Spotlight = await BuildSpotlightAsync(s, ct),
                Nudge = BuildNudge(s),
                s.PromptTokens,
                s.CompletionTokens,
                s.TotalTokens,
                s.GenerationSeconds,
                s.CreatedAt,
                s.CompletedAt
            };
        }

        private static object BuildPendingPayload(CloveraPersonalSnapshot s) => new
        {
            s.Id,
            s.Status,
            s.ProfileId,
            s.UnitId,
            s.FromDate,
            s.ToDate,
            s.ErrorMessage,
            s.CreatedAt,
            s.CompletedAt
        };

        // The insight block only (no run metadata) — embedded under each history row's
        // `result` key. Same four UI blocks as the completed payload, plus telemetry.
        private async Task<object> BuildResultAsync(CloveraPersonalSnapshot s, CancellationToken ct)
        {
            var prod = await GetProductivityStatsAsync(s);
            return new
            {
                Overview = BuildOverview(s, prod),
                Productivity = BuildProductivity(s, prod),
                Spotlight = await BuildSpotlightAsync(s, ct),
                Nudge = BuildNudge(s),
                s.PromptTokens,
                s.CompletionTokens,
                s.TotalTokens,
                s.GenerationSeconds
            };
        }

        // ── UI block builders ──

        // Overview tab: headline + body + the three stat cards (v1.1). Blockers / wins are
        // the deduped productivity-list lengths computed at read time from the unit's
        // DailyReflections lines for this cycle's window (see GetProductivityStatsAsync).
        private static object BuildOverview(CloveraPersonalSnapshot s, ProductivityStats prod) => new
        {
            Headline = CleanText(s.Headline),
            Body = CleanText(s.SummaryBody),
            Stats = new
            {
                Blockers = prod.Blockers.Count,        // = productivity.blockers.length
                WinsLogged = prod.Achievements.Count,  // replaces threadsLanded/threadsTotal — no denominator
                TopPillarCode = s.HighlightedPillarCode,
                TopPillarLabel = s.HighlightedPillarLabel
            }
        };

        // Output tab (productivity, v1.1): three independent deterministic lists aggregated
        // from the unit's DailyReflections lines for this cycle (deduped, sentence-cased —
        // see GetProductivityStatsAsync), plus the LLM-written observation sentence beneath
        // them. No thread counts / denominators. observation is the persisted LLM free-text.
        // Shown by the UI in place of the productivity block when the cycle
        // produced no reflection lines at all. Empty string whenever there IS
        // data, so the client can branch on `message` alone without inspecting
        // the three lists.
        private const string NoProductivityDataMessage =
            "No Productivity reflection data was captured for this cycle.";

        private static object BuildProductivity(CloveraPersonalSnapshot s, ProductivityStats prod) => new
        {
            Achievements = prod.Achievements,                    // string[]
            NotDone = prod.NotDone,                              // serialized "notDone"
            Blockers = prod.Blockers,                            // string[] (was a count pre-v1.1)
            Observation = CleanText(s.ProductivityObservation) ?? "",
            Message = prod.HasData ? "" : NoProductivityDataMessage
        };

        // Read-time productivity lists for a snapshot, derived from the unit's
        // DailyReflections lines over the snapshot's own window. Deterministic (no LLM, no
        // persistence): each list = the matching reflection lines, deduped case-insensitively
        // (first-seen order) and sentence-cased. The repository already dedups to one
        // reflection per local date so re-submissions aren't double-counted.
        private sealed class ProductivityStats
        {
            public List<string> Achievements { get; init; } = new();
            public List<string> NotDone { get; init; } = new();
            public List<string> Blockers { get; init; } = new();
            public bool HasData => Achievements.Count + NotDone.Count + Blockers.Count > 0;
        }

        private async Task<ProductivityStats> GetProductivityStatsAsync(CloveraPersonalSnapshot s)
        {
            // Stored window DateTimes are UTC; bind as UTC offsets for the timestamptz/date query.
            var from = new DateTimeOffset(DateTime.SpecifyKind(s.FromDate, DateTimeKind.Utc));
            var to = new DateTimeOffset(DateTime.SpecifyKind(s.ToDate, DateTimeKind.Utc));
            var rows = await _feedback.DailyReflectionLinesAsync((long)s.UnitId, from, to);

            List<string> Pick(Func<DailyReflectionLinesDto, string[]?> pick) =>
                Normalize(rows.SelectMany(r => pick(r) ?? Array.Empty<string>()));

            return new ProductivityStats
            {
                Achievements = Pick(r => r.AchievementsLines),
                NotDone = Pick(r => r.NotDoneLines),
                Blockers = Pick(r => r.BlockersLines)
            };
        }

        // Aggregate a productivity list per the v1.1 contract: trim, drop empties, dedup
        // case-insensitively keeping first-seen order, then sentence-case the first char only.
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

        // Spotlight tab: the highlighted pillar with display-ready score/label/badge,
        // the framing paragraph, and a small bar chart of this pillar's score across the
        // last few cycles.
        private async Task<object> BuildSpotlightAsync(CloveraPersonalSnapshot s, CancellationToken ct) => new
        {
            PillarCode = s.HighlightedPillarCode,
            PillarName = s.HighlightedPillarLabel,
            Score = (double?)s.HighlightedSmileyScore,
            MaxScore = 4.0, // smiley scale is 1..4 (see CloveraPersonalSnapshot.HighlightedSmileyScore)
            ScoreLabel = ScoreLabel(s.HighlightedSmileyScore),
            HighlightReason = s.HighlightReason,
            BadgeText = BadgeText(s.HighlightReason),
            Body = CleanText(s.PillarFraming),
            History = await BuildSpotlightHistoryAsync(s, ct)
        };

        // Nudge tab: theme/intent + title + flagged observation + action + sources.
        private static object BuildNudge(CloveraPersonalSnapshot s) => new
        {
            Intent = s.NudgeIntent,
            ThemeName = ThemeName(s.NudgeIntent),
            Title = CleanText(s.NudgeTitle),
            Flagged = CleanText(s.NudgeRationale),
            Action = CleanText(s.NudgeAction),
            DrawingFrom = DrawingFromArray(s.DrawingFromJson)
        };

        // Bar chart data: the highlighted pillar's smiley score across the last up-to-4
        // completed cycles for this unit (oldest → newest), current cycle flagged.
        // Only cycles where THIS pillar was the highlighted one carry a stored score, so
        // that is what we can show today.
        // TODO: to chart every pillar's per-cycle score, persist all six pillar scores per run.
        private async Task<List<object>> BuildSpotlightHistoryAsync(CloveraPersonalSnapshot s, CancellationToken ct)
        {
            var pillar = s.HighlightedPillarCode;
            if (string.IsNullOrEmpty(pillar))
                return new List<object>();

            // The same cycle is often re-run several times a day (identical or same-day
            // ToDate), so pulling raw priors would surface the same date as multiple bars
            // (e.g. "8 Jul" three times). Fetch the candidates, then collapse to ONE per
            // calendar day (newest run wins), excluding the current cycle's own day — it
            // is appended below as IsCurrent so must not also appear as a prior bar.
            var currentDay = s.ToDate.Date;
            var candidates = await _db.CloveraPersonalSnapshots
                .Where(x => !x.IsDel && x.Status == "Completed" && x.UnitId == s.UnitId
                            && x.HighlightedPillarCode == pillar && x.Id != s.Id
                            && x.ToDate < s.ToDate)
                .OrderByDescending(x => x.ToDate)
                .ThenByDescending(x => x.Id)
                .Select(x => new { x.Id, x.ToDate, x.HighlightedSmileyScore })
                .ToListAsync(ct);

            var history = candidates
                .Where(x => x.ToDate.Date != currentDay)              // never duplicate today's bar
                .GroupBy(x => x.ToDate.Date)                          // one bar per calendar day
                .Select(g => g.OrderByDescending(x => x.Id).First())  // newest run that day wins
                .OrderByDescending(x => x.ToDate)
                .Take(3)                                              // last up-to-3 distinct days
                .OrderBy(x => x.ToDate)                               // oldest → newest
                .Select(x => (object)new { Date = x.ToDate, Value = x.HighlightedSmileyScore ?? 0, MaxValue = 4, IsCurrent = false })
                .ToList();

            history.Add(new { Date = s.ToDate, Value = s.HighlightedSmileyScore ?? 0, MaxValue = 4, IsCurrent = true });
            return history;
        }

        // ── Display helpers (derive strings the UI shows from stored codes) ──

        // The LLM laces narrative text with em/en dashes (— –) as clause separators,
        // which surface as a stray dash in the app's description fields. Swap any unicode
        // dash used as punctuation for a comma so the text reads cleanly. ASCII
        // hyphen-minus is left alone, and this is NOT applied to the date-range labels
        // (which use en dashes intentionally).
        // Matches ANY Unicode dash (\p{Pd}) used as punctuation — em/en/figure/two-em/
        // horizontal-bar/etc., whatever exact codepoint the LLM emits — plus a spaced
        // ASCII hyphen acting as a separator. ASCII hyphens inside words/paths
        // ("Worksheets-PDFs", "3-7") have no surrounding space and are kept.
        private static string? CleanText(string? s) =>
            string.IsNullOrEmpty(s) ? s : System.Text.RegularExpressions.Regex.Replace(s, @"\s*[\p{Pd}−➖⁒-[-]]\s*|\s+-\s+", ", ");

        private static string? ScoreLabel(int? score) => score switch
        {
            4 => "Great",
            3 => "Good",
            2 => "Okay",
            1 => "Low",
            _ => null
        };

        private static string? BadgeText(string? highlightReason) => highlightReason switch
        {
            "highest" => "↑ Highest this cycle",
            "lowest" => "↓ Lowest this cycle",
            "biggest_mover_up" => "↑ Biggest improvement",
            "biggest_mover_down" => "↓ Biggest drop",
            _ => null
        };

        private static string? ThemeName(string? intent) => intent switch
        {
            "momentum" => "Momentum",
            "challenge" => "Challenge",
            "support" => "Support",
            _ => null
        };

        // Stored as a JSON string (array of { source, what }); emit a real JSON array so
        // the app does not have to re-parse a stringified payload. Empty array on absence/parse error.
        // Dash cleaning runs here on the DECODED string values (source/what), NOT on the raw
        // JSON text: older records stored the dash as a \uXXXX escape, which a regex over the
        // raw JSON string can't see — GetString() decodes it to the real char first, so CleanText
        // strips it reliably for both old and new records (mirrors how the Action field is cleaned).
        private static object DrawingFromArray(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<object>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return Array.Empty<object>();

                var items = new List<Dictionary<string, string?>>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    var obj = new Dictionary<string, string?>();
                    foreach (var prop in el.EnumerateObject())
                        obj[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                            ? CleanText(prop.Value.GetString())
                            : prop.Value.GetRawText();
                    items.Add(obj);
                }
                return items;
            }
            catch
            {
                return Array.Empty<object>();
            }
        }

        // Human label for the range dropdown, e.g. "1–27 May 2026" (same month),
        // "27 Apr – 3 May 2026" (cross-month), "28 Dec 2025 – 3 Jan 2026" (cross-year).
        private static string BuildRangeLabel(DateTime fromDate, DateTime toDate)
        {
            if (fromDate.Year == toDate.Year && fromDate.Month == toDate.Month)
                return $"{fromDate.Day}–{toDate.Day} {toDate:MMM yyyy}";
            if (fromDate.Year == toDate.Year)
                return $"{fromDate:d MMM} – {toDate:d MMM yyyy}";
            return $"{fromDate:d MMM yyyy} – {toDate:d MMM yyyy}";
        }
    }
}
