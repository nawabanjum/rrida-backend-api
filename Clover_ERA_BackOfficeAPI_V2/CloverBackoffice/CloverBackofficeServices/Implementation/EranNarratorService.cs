using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rrida.Engine;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Eran, on the deterministic layer's feed.
    ///
    /// <b>Eran phrases and never selects.</b> The action, the rung, the route and the
    /// condition were all decided before this service is reached; every prompt here is
    /// "say this well", never "choose".
    ///
    /// The prompts are the MVP's own (src/eran/prompts.js): the preamble, a context block
    /// saying who is being spoken to, the payload as INPUT, and the task for its kind. They
    /// are kept as files copied from it word for word, so the wording rules have one source.
    /// What differs is only the plumbing: our configured model rather than the MVP's, and
    /// the daily message asked for as JSON in the task rather than through a schema.
    ///
    /// Each feed item is phrased once. The prose is stored on the item, so re-running a day
    /// neither re-bills nor changes the wording under a manager who has already read it.
    /// The store clears it when the engine's payload for that item changes.
    ///
    /// Each message is stamped with the prompt that wrote it (PromptFingerprint, the MVP's
    /// 0729864 item 5). When the prompt changes, the team's last PromptRefreshDays feed days are
    /// written again; older history keeps the wording the manager already read.
    /// </summary>
    public sealed class EranNarratorService : IEranNarratorService
    {
        private const string PreambleFile = "CloverEra/Prompts/rrida-eran.md";
        private const string TasksFile = "CloverEra/Prompts/rrida-eran-tasks.json";
        /// <summary>
        /// The MVP's budget (src/eran/client.js callModel). The outputs are under 400 words;
        /// the rest is headroom, so an answer is never cut short, and one that is anyway is a
        /// failure rather than a message (failWhenCutOff), as in the MVP.
        /// </summary>
        private const int MaxTokens = 8000;

        /// <summary>The MVP's batch size: the newest pending messages first, fourteen at a time.</summary>
        public const int DefaultBatch = 14;

        /// <summary>
        /// The MVP retries a failed message on later passes, three attempts in all; after that the
        /// manager gets Eran's fixed card (EranGuardFallback) instead of a message that never comes.
        /// </summary>
        public const int MaxAttempts = 3;

        /// <summary>
        /// The daily message comes back in parts so the chat can render them separately. The
        /// MVP asks for this through a JSON schema; our client takes plain text, so the shape
        /// is asked for in words and parsed with the MVP's fallback.
        /// </summary>
        private const string DailyJsonInstruction = """


            Return only a JSON object, with no other text and no code fence, with exactly these
            string fields: "headline", "body", "outward", "one_thing", "say_this", "what_tells_you".
            Where the task says a field is empty, use an empty string.
            """;

        /// <summary>
        /// Not MVP wording: added only to a weekly or monthly message whose INPUT carries the
        /// manager's own actions (ManagerActionEngineActivity), so every other message's prompt
        /// is exactly the MVP's.
        /// </summary>
        public const string ManagerActivityInstruction = """


            The INPUT also lists what the manager did with actions they set themselves in this period
            (manager_actions_this_week or manager_actions_this_month). Count it as part of the period's
            progress: mention it briefly, using the action names as written. It is the manager's own
            activity, not a result of the reading: do not say it caused, fixed or explains anything in
            the reading, and never present it as the move.
            """;

        private static readonly Lazy<string> Preamble = new(() => ReadFile(PreambleFile));
        private static readonly Lazy<Dictionary<string, string>> Tasks = new(() =>
            JsonSerializer.Deserialize<Dictionary<string, string>>(ReadFile(TasksFile))
            ?? throw new InvalidOperationException($"'{TasksFile}' holds no tasks."));

        /// <summary>
        /// How many of the team's most recent feed days are written again when the prompt changes.
        /// Feed items exist only on days the team is asked (D35), so these are working days. Older
        /// history keeps the wording the manager already read, and is not billed again.
        /// </summary>
        public const int PromptRefreshDays = 5;

        private static readonly Lazy<string> Fingerprint = new(() => FingerprintOf(
            Preamble.Value, ReadFile(TasksFile), DailyJsonInstruction, ManagerActivityInstruction));

        /// <summary>
        /// The prompt that writes today's messages, as the MVP's PROMPT_FINGERPRINT (0729864, item 5):
        /// a hash of the text rather than a version bumped by hand. Covers every instruction Eran is
        /// handed here: the preamble, the tasks, and the two instructions this service appends.
        /// </summary>
        public static string PromptFingerprint => Fingerprint.Value;

        /// <summary>
        /// First 12 hex of a hash over the parts, the MVP's length. SHA-256 rather than the MVP's
        /// SHA-1: it only detects a change, so the value need not match the MVP's.
        /// </summary>
        public static string FingerprintOf(params string[] parts)
        {
            var bytes = Encoding.UTF8.GetBytes(string.Join("\n\u0000\n", parts));
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..12].ToLowerInvariant();
        }

        /// <summary>
        /// Written under an older prompt and recent enough to write again. Never a team note
        /// (writing it again re-issues it to the team, and a note that arrives again before the
        /// team answers changes the answer) and never a null stamp (written before the column).
        /// </summary>
        private static bool WrittenUnderOlderPrompt(EngineFeedItem item, string current, HashSet<int> recentDays) =>
            item.Kind != "note"
            && item.PromptFingerprint is not null
            && item.PromptFingerprint != current
            && recentDays.Contains(item.Day);

        private readonly ApplicationDbContext _context;
        private readonly IEranCoachAnthropicClient _client;
        private readonly IDocumentEmbeddingRepository _models;
        private readonly EranEngineContextBuilder _contextBuilder;
        private readonly ILogger<EranNarratorService> _logger;

        public EranNarratorService(
            ApplicationDbContext context,
            IEranCoachAnthropicClient client,
            IDocumentEmbeddingRepository models,
            EranEngineContextBuilder contextBuilder,
            ILogger<EranNarratorService> logger)
        {
            _context = context;
            _client = client;
            _models = models;
            _contextBuilder = contextBuilder;
            _logger = logger;
        }

        public async Task<int> NarrateAsync(uint businessUnitId, int take, CancellationToken ct = default, int? throughDay = null)
        {
            // Stored wording is checked again before anything else, so a message written before
            // the guard (or under older rules) that breaks a rule is cleared and rewritten.
            await SweepStoredAsync(businessUnitId, throughDay, ct);

            // Newest first, as the MVP does, so today's message is always in the batch.
            var candidates = await _context.EngineFeedItems
                .Where(f => f.BusinessUnitId == businessUnitId && f.Prose == null
                            && (throughDay == null || f.Day <= throughDay))
                .OrderByDescending(f => f.Day).ThenByDescending(f => f.Id)
                .Take(Math.Clamp(take, 1, 50) * 2)
                .ToListAsync(ct);
            if (candidates.Count == 0) return 0;

            var pending = new List<EngineFeedItem>();
            var written = 0;
            foreach (var item in candidates)
            {
                if (pending.Count >= Math.Clamp(take, 1, 50)) break;
                if (await AttemptsSinceWrittenAsync(businessUnitId, item, ct) < MaxAttempts)
                {
                    pending.Add(item);
                    continue;
                }

                // The MVP's three attempts are spent: the manager gets the fixed card rather than
                // a message that never arrives, and the nightly run tries it once more.
                await WriteFallbackAsync(businessUnitId, item, EranGuardLog.Fallback, attempt: MaxAttempts, model: null, ct);
                written += 1;
            }
            if (pending.Count == 0) return written;

            var model = EranModelPriority.Pick(await _models.GetAllModelsAsync(EranModelPriority.SearchModelTypeId));
            var system = $"{Preamble.Value}\n\n{EranEngineContextBuilder.Render(await _contextBuilder.BuildAsync(businessUnitId, ct))}";

            foreach (var item in pending)
            {
                if (await PhraseAsync(businessUnitId, item, system, model, retryingFallback: false, ct)) written += 1;
            }

            _logger.LogInformation("[Eran] Unit {Unit}: {Written} of {Pending} feed items phrased.",
                businessUnitId, written, pending.Count);
            return written;
        }

        public async Task<int> RetryFallbacksAsync(uint businessUnitId, CancellationToken ct = default)
        {
            var cards = await _context.EngineFeedItems
                .Where(f => f.BusinessUnitId == businessUnitId && f.GuardOutcome == EranGuardLog.Fallback)
                .OrderByDescending(f => f.Day).ThenByDescending(f => f.Id)
                .Take(50)
                .ToListAsync(ct);
            if (cards.Count == 0) return 0;

            var model = EranModelPriority.Pick(await _models.GetAllModelsAsync(EranModelPriority.SearchModelTypeId));
            var system = $"{Preamble.Value}\n\n{EranEngineContextBuilder.Render(await _contextBuilder.BuildAsync(businessUnitId, ct))}";
            var replaced = 0;
            foreach (var item in cards)
            {
                if (await PhraseAsync(businessUnitId, item, system, model, retryingFallback: true, ct)
                    && item.GuardOutcome is EranGuardLog.Passed or EranGuardLog.PassedOnRetry)
                    replaced += 1;
            }

            _logger.LogInformation("[Eran] Unit {Unit}: {Replaced} of {Cards} fallback cards rewritten on the nightly retry.",
                businessUnitId, replaced, cards.Count);
            return replaced;
        }

        /// <summary>
        /// One message through the guard (the brief, section 2): generate, check; on a breach one
        /// retry carrying the rule labels only; on a second breach the fixed card. True when the
        /// item now has prose. A transport failure leaves it for a later pass, except on the nightly
        /// fallback retry, where the card simply stays.
        /// </summary>
        private async Task<bool> PhraseAsync(
            uint businessUnitId, EngineFeedItem item, string system, ModelConfiguration model, bool retryingFallback, CancellationToken ct)
        {
            if (!Tasks.Value.TryGetValue(item.Kind, out var task))
            {
                _logger.LogWarning("[Eran] No prompt for feed kind '{Kind}'. Left unphrased.", item.Kind);
                return false;
            }

            var user = $"{RenderInput(item.PayloadJson)}\n\n{task}";
            if (item.Kind == "daily") user += DailyJsonInstruction;
            if (HasManagerActivity(item.PayloadJson)) user += ManagerActivityInstruction;
            var finalFallback = retryingFallback ? EranGuardLog.FallbackFinal : EranGuardLog.Fallback;

            var first = await CallAsync(businessUnitId, item, system, user, model, ct);
            if (first.Prose is null)
            {
                if (!retryingFallback) return false;
                item.GuardOutcome = EranGuardLog.FallbackFinal;
                await _context.SaveChangesAsync(ct);
                return true;
            }

            if (first.Verdict!.Passed)
            {
                await StoreAsync(businessUnitId, item, first.Prose, EranGuardLog.Passed, first.Call, first.Verdict, attempt: 1, model, ct);
                return true;
            }

            first.Call.GuardOutcome = EranGuardLog.Retried;
            first.Call.GuardRules = Clip(first.Verdict.RulesText, 500);
            await _context.SaveChangesAsync(ct);
            EranGuardLog.Catch(_logger, item.Kind, businessUnitId, item.Day, 1, EranGuardLog.Retried, first.Verdict.RulesText, model.ModelName);

            var second = await CallAsync(businessUnitId, item, system, $"{user}\n\n{EranGuard.RetryFeedback(first.Verdict)}", model, ct);
            if (second.Prose is not null && second.Verdict!.Passed)
            {
                await StoreAsync(businessUnitId, item, second.Prose, EranGuardLog.PassedOnRetry, second.Call, second.Verdict, attempt: 2, model, ct);
                return true;
            }

            if (second.Verdict is null)
            {
                // The retry could not be made (a transport fault, not a breach). Nothing Eran wrote
                // is kept; a later pass writes the item afresh, and three faults give the card. On
                // the nightly retry the card simply stays.
                if (!retryingFallback) return false;
                item.GuardOutcome = EranGuardLog.FallbackFinal;
                await _context.SaveChangesAsync(ct);
                return true;
            }

            second.Call.GuardOutcome = EranGuardLog.Fallback;
            second.Call.GuardRules = Clip(second.Verdict.RulesText, 500);
            await WriteFallbackAsync(businessUnitId, item, finalFallback, attempt: 2, model.ModelName, ct,
                rules: second.Verdict.RulesText);
            return true;
        }

        private sealed record CallResult(EngineLlmCall Call, string? Prose, GuardVerdict? Verdict);

        /// <summary>One model call, logged in EngineLlmCalls, its reply normalised and checked.</summary>
        private async Task<CallResult> CallAsync(
            uint businessUnitId, EngineFeedItem item, string system, string user, ModelConfiguration model, CancellationToken ct)
        {
            var call = new EngineLlmCall
            {
                BusinessUnitId = businessUnitId,
                FeedKey = item.FeedKey,
                PromptType = item.Kind,
                Day = item.Day,
                SystemPrompt = system,
                UserContent = user,
                Model = model.ModelName,
                CreatedAt = DateTime.UtcNow
            };
            _context.EngineLlmCalls.Add(call);

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var response = (await _client.CompleteAsync(system, user, model, MaxTokens, ct, failWhenCutOff: true)).Trim();
                stopwatch.Stop();
                call.Response = response;
                call.DurationMs = (int)stopwatch.ElapsedMilliseconds;

                // An empty response is a failure, not a result (MVP client.run).
                if (response.Length == 0) throw new InvalidOperationException("the model returned an empty response");

                var wasJson = true;
                var prose = item.Kind == "daily" ? NormaliseDaily(response, out wasJson) : response;
                prose = Clip(prose, 4000);
                var verdict = EranGuard.CheckFeedItem(item.Kind, item.Situation, item.PayloadJson, prose, wasJson);
                await _context.SaveChangesAsync(ct);
                return new CallResult(call, prose, verdict);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One failure must not stop the rest; the item keeps no prose and is tried
                // again on a later pass, up to the MVP's three attempts.
                stopwatch.Stop();
                call.Error = Clip(ex.GetBaseException().Message, 1000);
                call.DurationMs = (int)stopwatch.ElapsedMilliseconds;
                _logger.LogError(ex, "[Eran] {Kind} for unit {Unit} day {Day} failed.", item.Kind, businessUnitId, item.Day);
                await _context.SaveChangesAsync(ct);
                return new CallResult(call, null, null);
            }
        }

        private async Task StoreAsync(
            uint businessUnitId, EngineFeedItem item, string prose, string outcome, EngineLlmCall call, GuardVerdict verdict,
            int attempt, ModelConfiguration model, CancellationToken ct)
        {
            item.Prose = prose;
            item.ProseGeneratedAt = DateTime.UtcNow;
            item.GuardOutcome = outcome;
            item.PromptFingerprint = PromptFingerprint;
            call.GuardOutcome = outcome;
            if (verdict.Breaches.Count > 0) call.GuardRules = Clip(verdict.RulesText, 500);

            // A note is the only feed item the TEAM sees.
            if (item.Kind is "note" && item.CycleKey is not null)
                await FillTeamNoteAsync(businessUnitId, item, ct);

            await _context.SaveChangesAsync(ct);
            if (outcome == EranGuardLog.PassedOnRetry || verdict.Warned)
                EranGuardLog.Catch(_logger, item.Kind, businessUnitId, item.Day, attempt, outcome, verdict.RulesText, model.ModelName);
        }

        /// <summary>
        /// The fixed card, from the payload's authored text only. It is checked too; if even the
        /// card breaks a rule, the daily headline (or a fixed line) goes out and an alert is raised.
        /// </summary>
        private async Task WriteFallbackAsync(
            uint businessUnitId, EngineFeedItem item, string outcome, int attempt, string? model, CancellationToken ct, string? rules = null)
        {
            var card = EranGuardFallback.ForFeedItem(item.Kind, item.Situation, item.PayloadJson);
            var verdict = EranGuard.CheckFeedItem(item.Kind, item.Situation, item.PayloadJson, card);
            if (!verdict.Passed)
            {
                EranGuardLog.FallbackAlert(_logger, item.Kind, businessUnitId, item.Day, verdict.RulesText);
                card = item.Kind == "daily"
                    ? EranGuardFallback.DailyHeadlineOnly(item.PayloadJson)
                    : EranGuardFallback.ForFeedItem("unknown", null, null);
            }

            item.Prose = Clip(card, 4000);
            item.ProseGeneratedAt = DateTime.UtcNow;
            item.GuardOutcome = outcome;
            // The card is built from the payload, not written by the prompt.
            item.PromptFingerprint = null;
            if (item.Kind is "note" && item.CycleKey is not null)
                await FillTeamNoteAsync(businessUnitId, item, ct);
            await _context.SaveChangesAsync(ct);

            EranGuardLog.Catch(_logger, item.Kind, businessUnitId, item.Day, attempt, outcome, rules ?? "unwritten", model);
        }

        /// <summary>
        /// Every stored message for the team, checked again (the brief: cache hits go through the
        /// guard too). One that breaks a rule is cleared so this pass writes it again; one written
        /// before the guard that passes is marked checked.
        /// </summary>
        private async Task SweepStoredAsync(uint businessUnitId, int? throughDay, CancellationToken ct)
        {
            var stored = await _context.EngineFeedItems
                .Where(f => f.BusinessUnitId == businessUnitId && f.Prose != null
                            && (throughDay == null || f.Day <= throughDay))
                .ToListAsync(ct);

            // MVP 0729864, item 5: an answer written to one prompt is not an answer to another.
            // Only the team's most recent feed days, and only messages stamped with a different
            // prompt: a null stamp (written before the column existed) is never retired.
            var current = PromptFingerprint;
            var recentDays = (await _context.EngineFeedItems.AsNoTracking()
                    .Where(f => f.BusinessUnitId == businessUnitId && (throughDay == null || f.Day <= throughDay))
                    .Select(f => f.Day)
                    .Distinct()
                    .OrderByDescending(d => d)
                    .Take(PromptRefreshDays)
                    .ToListAsync(ct))
                .ToHashSet();

            var changed = false;
            foreach (var item in stored)
            {
                // The fixed card was built from the payload and checked when written (and, if it
                // failed, replaced by the headline alone with an alert). Evicting it would only
                // write it again, every pass.
                if (EranGuardLog.IsCard(item.GuardOutcome)) continue;

                if (WrittenUnderOlderPrompt(item, current, recentDays))
                {
                    item.Prose = null;
                    item.ProseGeneratedAt = null;
                    item.GuardOutcome = null;
                    item.PromptFingerprint = null;
                    changed = true;
                    EranGuardLog.Catch(_logger, item.Kind, businessUnitId, item.Day, 1, EranGuardLog.PromptRetired, "prompt changed", null);
                    continue;
                }

                var verdict = EranGuard.CheckFeedItem(item.Kind, item.Situation, item.PayloadJson, item.Prose);
                if (verdict.Passed)
                {
                    if (item.GuardOutcome is null)
                    {
                        item.GuardOutcome = EranGuardLog.Passed;
                        changed = true;
                    }
                    continue;
                }

                item.Prose = null;
                item.ProseGeneratedAt = null;
                item.GuardOutcome = null;
                item.PromptFingerprint = null;
                changed = true;
                EranGuardLog.Catch(_logger, item.Kind, businessUnitId, item.Day, 1, EranGuardLog.CacheEvicted, verdict.RulesText, null);
            }

            if (changed) await _context.SaveChangesAsync(ct);
        }

        private static string Clip(string text, int max) => text.Length > max ? text[..max] : text;

        /// <summary>The payload as the MVP renders it: one "key: value" line per field, nulls left out.</summary>
        /// <summary>Whether a payload carries the manager's own actions for its period.</summary>
        public static bool HasManagerActivity(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return false;
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                return doc.RootElement.ValueKind == JsonValueKind.Object
                    && (doc.RootElement.TryGetProperty(ManagerActionEngineActivity.WeeklyKey, out _)
                        || doc.RootElement.TryGetProperty(ManagerActionEngineActivity.MonthlyKey, out _));
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public static string RenderInput(string payloadJson)
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson);
            var lines = new StringBuilder("INPUT");
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
                lines.Append($"\n  {property.Name}: {Format(property.Value)}");
            }
            return lines.ToString();
        }

        private static string Format(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.Array => string.Join("; ", value.EnumerateArray().Select(Format)),
            JsonValueKind.Object => string.Join(", ", value.EnumerateObject().Select(p => $"{p.Name}={Format(p.Value)}")),
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => value.GetRawText()
        };

        /// <summary>
        /// The daily message in its five parts, stored as JSON. If the model answered in prose
        /// instead, the whole answer becomes the body (MVP parseDaily).
        /// </summary>
        public static string NormaliseDaily(string response) => NormaliseDaily(response, out _);

        /// <param name="wasJson">False when the model answered in prose; the guard fails that (brief §4.6).</param>
        public static string NormaliseDaily(string response, out bool wasJson)
        {
            wasJson = false;
            var text = response.Trim();
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                var firstLine = text.IndexOf('\n');
                var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
                if (firstLine > 0 && lastFence > firstLine) text = text[(firstLine + 1)..lastFence].Trim();
            }

            string headline = "", body = text, outward = "", oneThing = "", sayThis = "", whatTellsYou = "";
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String)
                {
                    wasJson = true;
                    headline = Field(doc.RootElement, "headline");
                    body = b.GetString()!.Trim();
                    // D36 (MVP c36935d): the outward route's own block, under the body.
                    outward = Field(doc.RootElement, "outward");
                    oneThing = Field(doc.RootElement, "one_thing");
                    sayThis = Field(doc.RootElement, "say_this");
                    whatTellsYou = Field(doc.RootElement, "what_tells_you");
                }
            }
            catch (JsonException)
            {
                // Not JSON. Treated as prose.
            }

            return JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["headline"] = headline,
                ["body"] = body,
                ["outward"] = outward,
                ["one_thing"] = oneThing,
                ["say_this"] = sayThis,
                ["what_tells_you"] = whatTellsYou
            });

            static string Field(JsonElement root, string name) =>
                root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private async Task<int> AttemptsSinceWrittenAsync(uint businessUnitId, EngineFeedItem item, CancellationToken ct) =>
            await _context.EngineLlmCalls.AsNoTracking()
                .CountAsync(c => c.BusinessUnitId == businessUnitId && c.FeedKey == item.FeedKey
                                 && c.Error != null && c.CreatedAt >= item.WrittenAt, ct);

        private async Task FillTeamNoteAsync(uint businessUnitId, EngineFeedItem item, CancellationToken ct)
        {
            var note = await _context.EngineTeamNotes
                .FirstOrDefaultAsync(n => n.BusinessUnitId == businessUnitId
                                          && n.CycleKey == item.CycleKey
                                          && n.Day == item.Day, ct);

            if (note is null)
            {
                note = new EngineTeamNote
                {
                    BusinessUnitId = businessUnitId,
                    CycleKey = item.CycleKey,
                    Day = item.Day,
                    WrittenAt = DateTime.UtcNow
                };
                _context.EngineTeamNotes.Add(note);
            }

            note.NoteType = item.NoteType;
            note.Text = item.Prose;
            note.IssuedAt = DateTime.UtcNow;
        }

        private static string ReadFile(string relative)
        {
            var path = Path.Combine(AppContext.BaseDirectory, relative);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Eran's prompt file was not found at '{path}'.");
            return File.ReadAllText(path);
        }
    }
}
