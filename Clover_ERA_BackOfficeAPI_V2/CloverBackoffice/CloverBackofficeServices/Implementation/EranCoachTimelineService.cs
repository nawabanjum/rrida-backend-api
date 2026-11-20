using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using System.Text.Json;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// The team's Eran thread: the engine's messages and this user's chat about the team, in
    /// one continuous timeline. The MVP's Eran column (public/app.js renderThread) is the
    /// reference for what is in it and in what order.
    ///
    /// Engine messages belong to the team, so every manager with access sees the same ones.
    /// Chat belongs to the user. Neither is copied into the other's tables; they are merged
    /// here, when read.
    /// </summary>
    public sealed class EranCoachTimelineService : IEranCoachTimelineService
    {
        private const int DefaultTake = 50;
        private const int MaxTake = 100;

        /// <summary>The MVP's labels in the thread (app.js KIND_IN_THREAD). A daily message has none.</summary>
        private static readonly Dictionary<string, string> LabelByKind = new(StringComparer.Ordinal)
        {
            ["weekly"] = "weekly reading",
            ["monthly"] = "monthly summary",
            ["outcome"] = "signal read",
            ["note"] = "note to the team",
            ["escalation"] = "repeated draw",
            ["cross_team"] = "cross-team pattern",
            ["cross_team_note"] = "note to the managers",
        };

        /// <summary>
        /// The order the engine writes a day's messages in (Simulation.TickTeam): the reading
        /// and its note, the weekly reading, an escalation with a new selection, the daily
        /// message, then the month's summary after the day.
        /// </summary>
        private static readonly Dictionary<string, int> RankByKind = new(StringComparer.Ordinal)
        {
            ["outcome"] = 0, ["note"] = 1, ["weekly"] = 2, ["escalation"] = 3, ["daily"] = 4, ["monthly"] = 5,
        };

        private readonly ApplicationDbContext _context;
        private readonly IUnitAccessService _access;
        private readonly IEngineManagerService _manager;
        private readonly IRridaEngineContextFactory _engine;
        private readonly IEngineNarrationQueue _narration;
        private readonly IPlanFeatureGate? _planGate;

        // planGate: the plan catalogue's content filter (daily nudges / monthly report). Null =
        // nothing filtered. The action card is filtered by IEngineManagerService itself.
        public EranCoachTimelineService(
            ApplicationDbContext context,
            IUnitAccessService access,
            IEngineManagerService manager,
            IRridaEngineContextFactory engine,
            IEngineNarrationQueue narration,
            IPlanFeatureGate? planGate = null)
        {
            _planGate = planGate;
            _context = context;
            _access = access;
            _manager = manager;
            _engine = engine;
            _narration = narration;
        }

        public async Task<EranCoachTimelineDto> GetAsync(
            User actor, uint unitId, DateTime? before, int take, long? cycleId = null, CancellationToken ct = default)
        {
            // The same rule the dashboards use: Team Managers, Profile Admins and Portfolio
            // Managers see the teams they have access to.
            var accessible = await _access.GetAccessibleUnitIdsAsync(actor, ct);
            if (!accessible.Contains(unitId))
                throw new UnauthorizedAccessException("You do not have access to this team.");

            take = take <= 0 ? DefaultTake : Math.Min(take, MaxTake);
            var cutoff = before ?? DateTime.MaxValue;

            // One cycle's thread: the engine's messages about that action and nothing else.
            // Chat is not tied to a cycle, so it stays out rather than being guessed at.
            // Feed items name their cycle by key, so the id is turned into this team's key.
            string? cycleKey = null;
            if (cycleId is not null)
            {
                cycleKey = await _context.EngineCycles.AsNoTracking()
                    .Where(c => c.Id == cycleId && c.BusinessUnitId == unitId)
                    .Select(c => c.CycleKey)
                    .FirstOrDefaultAsync(ct)
                    ?? throw new InvalidOperationException($"No cycle {cycleId} for this team.");
            }

            var chat = cycleKey is null ? await ChatAsync(actor.UserId, unitId, cutoff, take, ct) : [];
            var settings = await _context.EngineTeamSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.BusinessUnitId == unitId, ct);
            var engine = settings is null ? [] : await EngineAsync(unitId, settings, cutoff, take, cycleKey, ct);
            // Plan: daily quick-action nudges and the monthly report are Pro; left out otherwise.
            if (_planGate != null && engine.Count > 0
                && !await _planGate.IsIncludedAsync(actor.UserId, PlanFeatureKeys.DailyNudgesMonthlyReporting, ct))
                engine = engine.Where(i => !PlanFeatureGate.IsDailyOrMonthly(i.Kind)).ToList();
            if (settings is not null) WithdrawUncheckedReplies(chat);

            var page = chat.Concat(engine)
                .OrderByDescending(i => i.At)
                .ThenByDescending(i => i.MessageId ?? 0)
                .Take(take)
                .ToList();

            // Messages Eran has not written yet are queued, the way the MVP writes a team's
            // pending messages when that team is opened. Up to the newest one on this page, so
            // scrolling back through an imported history phrases what is being looked at.
            var pendingDays = page.Where(i => i.Status == "pending" && i.Day is not null).Select(i => i.Day!.Value).ToList();
            if (pendingDays.Count > 0) _narration.Enqueue(unitId, pendingDays.Max());

            var conversationId = await _context.EranCoachConversations.AsNoTracking()
                .Where(c => c.UserId == actor.UserId && c.BusinessUnitId == unitId && !c.IsDel)
                .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt).ThenByDescending(c => c.Id)
                .Select(c => (int?)c.Id)
                .FirstOrDefaultAsync(ct);

            var timeline = new EranCoachTimelineDto
            {
                UnitId = unitId,
                ConversationId = conversationId,
                Items = page.OrderBy(i => i.At).ThenBy(i => i.MessageId ?? 0).ToList(),
                NextBefore = page.Count == take ? page[^1].At : null
            };

            if (settings is not null)
            {
                timeline.State = await _manager.GetStateAsync(actor, unitId, ct);
                // With a cycle selected, that cycle's action, so it matches the week's messages
                // and its weekly flag. Otherwise the action in front of the manager today.
                timeline.ActiveAction = cycleId is long selected
                    ? await _manager.GetCycleActionAsync(actor, unitId, selected, ct)
                    : await _manager.GetActiveActionAsync(actor, unitId, ct);
                timeline.Prompts = await PromptsAsync(actor, unitId, timeline.State, timeline.ActiveAction, ct);
            }

            return timeline;
        }

        /// <summary>The suggested questions, from what the team's data shows today (<see cref="EranPromptChips"/>).</summary>
        private async Task<List<string>> PromptsAsync(
            User actor, uint unitId, EngineTeamStateDto? state, EngineActiveActionDto? action, CancellationToken ct)
        {
            if (state is null) return [];
            if (state.IsCalibrating) return EranPromptChips.For(new EranPromptChips.Facts(state, action));

            var outward = await _manager.GetOutwardConditionAsync(actor, unitId, ct);

            // The state on the last weekly reading before the current day, the same reading the
            // compass trail starts from.
            var latestDay = await _context.EngineStateDaily.AsNoTracking()
                .Where(s => s.BusinessUnitId == unitId && s.Day <= state.Day)
                .MaxAsync(s => (int?)s.Day, ct);
            var readingDay = latestDay is null ? null : await _context.EngineFeedItems.AsNoTracking()
                .Where(f => f.BusinessUnitId == unitId && f.Kind == "weekly" && f.Day < latestDay)
                .MaxAsync(f => (int?)f.Day, ct);
            var verticalThen = readingDay is null ? null : await _context.EngineStateDaily.AsNoTracking()
                .Where(s => s.BusinessUnitId == unitId && s.Day == readingDay)
                .Select(s => s.Vertical)
                .FirstOrDefaultAsync(ct);

            // Between cycles, as the newest daily message says it (the MVP's between_cycles):
            // the move that just resolved.
            string? finished = null;
            if (action is null)
            {
                var situation = await _context.EngineFeedItems.AsNoTracking()
                    .Where(f => f.BusinessUnitId == unitId && f.Kind == "daily" && f.Day <= state.Day)
                    .OrderByDescending(f => f.Day).ThenByDescending(f => f.Id)
                    .Select(f => f.Situation)
                    .FirstOrDefaultAsync(ct);
                if (situation == "between_cycles")
                {
                    finished = await _context.EngineCycles.AsNoTracking()
                        .Where(c => c.BusinessUnitId == unitId && c.ResolvedDay != null && c.ResolvedDay <= state.Day)
                        .OrderByDescending(c => c.ResolvedDay).ThenByDescending(c => c.CycleIndex)
                        .Select(c => c.Issue)
                        .FirstOrDefaultAsync(ct);
                }
            }

            // The manager's own most pressing open action (api/actions), for a follow-up question.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var own = ManagerActionEranContext.MostPressing(
                await ManagerActionEranContext.OpenAsync(_context, unitId, today, ct));

            return EranPromptChips.For(new EranPromptChips.Facts(state, action, outward?.Condition, verticalThen, finished,
                own?.Title, own?.IsOverdue(today) ?? false));
        }

        public const string WithdrawnReply = "This earlier reply is hidden because it did not meet Eran's rules.";

        /// <summary>
        /// Eran's chat replies on an engine team, stored before the guard existed, are checked when
        /// read; one that breaks a rule is shown as withdrawn rather than repeated. Worksheet names
        /// are not judged here, because what the chat was handed at the time is not kept.
        /// </summary>
        public static void WithdrawUncheckedReplies(List<EranCoachTimelineItemDto> chat)
        {
            foreach (var item in chat)
            {
                if (!string.Equals(item.Role, "assistant", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(item.Content))
                    continue;
                var breaks = EranGuard.CheckChat(item.Content, GuardSource.Empty).Breaches
                    .Any(b => b.Severity == GuardSeverity.Hard && b.Label != EranGuard.InventedAction);
                if (breaks) item.Content = WithdrawnReply;
            }
        }

        /// <summary>This user's chat about this team, across every conversation they have had about it.</summary>
        private async Task<List<EranCoachTimelineItemDto>> ChatAsync(
            int userId, uint unitId, DateTime cutoff, int take, CancellationToken ct)
        {
            var conversations = _context.EranCoachConversations
                .Where(c => c.UserId == userId && c.BusinessUnitId == unitId && !c.IsDel)
                .Select(c => c.Id);

            return await _context.EranCoachMessages.AsNoTracking()
                .Where(m => !m.IsDel && conversations.Contains(m.ConversationId) && m.CreatedAt < cutoff)
                .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
                .Take(take)
                .Select(m => new EranCoachTimelineItemDto
                {
                    Type = "chat",
                    At = m.CreatedAt,
                    MessageId = m.Id,
                    ConversationId = m.ConversationId,
                    Role = m.Role,
                    Content = m.Content
                })
                .ToListAsync(ct);
        }

        /// <summary>The engine's messages to this team's managers, up to today.</summary>
        private async Task<List<EranCoachTimelineItemDto>> EngineAsync(
            uint unitId, Infrastructure.Models.DeterministicLayer.EngineTeamSettings settings,
            DateTime cutoff, int take, string? cycleKey, CancellationToken ct)
        {
            var clock = new EngineTeamClock(settings.StartDate, settings.WorkingWeek, _engine.Calendar);
            var lastDay = clock.DayOf(DateOnly.FromDateTime(DateTime.UtcNow));
            if (cutoff != DateTime.MaxValue)
                lastDay = Math.Min(lastDay, clock.DayOf(DateOnly.FromDateTime(cutoff)));

            var items = new List<EranCoachTimelineItemDto>();
            var skip = 0;
            // Over-fetched and filtered here, because whether a day is shown depends on the
            // team's working week. A batch can be mostly hidden weekend days, so keep reading.
            while (items.Count < take)
            {
                var rows = await _context.EngineFeedItems.AsNoTracking()
                    .Where(f => f.BusinessUnitId == unitId && f.Day <= lastDay)
                    .Where(f => cycleKey == null || f.CycleKey == cycleKey)
                    .OrderByDescending(f => f.Day).ThenByDescending(f => f.Id)
                    .Skip(skip).Take(take * 2)
                    .ToListAsync(ct);
                if (rows.Count == 0) break;
                skip += rows.Count;

                foreach (var row in rows)
                {
                    var date = clock.DateOfDay(row.Day);
                    if (!IsShown(row.Kind, row.Situation, clock.IsAskedOn(date))) continue;

                    var at = new DateTime(date, TimeOnly.MinValue, DateTimeKind.Utc)
                        .AddMilliseconds(RankByKind.GetValueOrDefault(row.Kind, 6));
                    if (at >= cutoff) continue;

                    // Nothing Eran wrote reaches a manager unchecked: stored wording that breaks
                    // a rule reads as still being written, and the narrator rewrites it.
                    var prose = GuardedProse(row);

                    items.Add(new EranCoachTimelineItemDto
                    {
                        Type = "engine",
                        At = at,
                        FeedKey = row.FeedKey,
                        Kind = row.Kind,
                        Label = LabelByKind.GetValueOrDefault(row.Kind),
                        Day = row.Day,
                        Date = date,
                        Situation = row.Situation,
                        CycleKey = row.CycleKey,
                        Rung = row.Rung,
                        NoteType = row.NoteType,
                        Status = prose is null ? "pending" : "ready",
                        Text = row.Kind == "daily" ? null : prose,
                        Daily = row.Kind == "daily" && prose is not null ? DailyParts(prose) : null
                    });
                }
            }

            await MarkFailedAsync(unitId, items, ct);
            return items;
        }

        /// <summary>
        /// A message Eran could not write after <see cref="EranNarratorService.MaxAttempts"/>
        /// tries is given up on (the MVP's status 'error'). It is said so, rather than shown as
        /// being written for ever; a later run that rewrites the item starts it again.
        /// </summary>
        private async Task MarkFailedAsync(uint unitId, List<EranCoachTimelineItemDto> items, CancellationToken ct)
        {
            var pending = items.Where(i => i.Status == "pending" && i.FeedKey is not null).ToList();
            if (pending.Count == 0) return;

            var keys = pending.Select(i => i.FeedKey!).Distinct().ToList();
            var writtenAt = await _context.EngineFeedItems.AsNoTracking()
                .Where(f => f.BusinessUnitId == unitId && keys.Contains(f.FeedKey))
                .ToDictionaryAsync(f => f.FeedKey, f => f.WrittenAt, ct);
            var errors = await _context.EngineLlmCalls.AsNoTracking()
                .Where(c => c.BusinessUnitId == unitId && c.Error != null && c.FeedKey != null && keys.Contains(c.FeedKey))
                .Select(c => new { c.FeedKey, c.CreatedAt })
                .ToListAsync(ct);

            foreach (var item in pending)
            {
                var since = writtenAt.GetValueOrDefault(item.FeedKey!);
                var failures = errors.Count(e => e.FeedKey == item.FeedKey && e.CreatedAt >= since);
                if (failures >= EranNarratorService.MaxAttempts) item.Status = "failed";
            }
        }

        /// <summary>
        /// D35: there is no daily message on a day the team was not asked anything. The engine no
        /// longer writes one (since upstream 06361d9), so this only keeps rows stored by an older
        /// run out of view until the team's next run deletes them. Every other kind is always
        /// shown. Nothing here changes what the engine decides or stores.
        /// </summary>
        public static bool IsShown(string kind, string? situation, bool isWorkingDay) =>
            kind != "daily" || isWorkingDay;

        /// <summary>The stored wording if it passes Eran's guard, otherwise null (treated as not yet written).</summary>
        public static string? GuardedProse(Infrastructure.Models.DeterministicLayer.EngineFeedItem row) =>
            row.Prose is not null
            && (EranGuardLog.IsCard(row.GuardOutcome) // the fixed card: checked when it was written
                || EranGuard.CheckFeedItem(row.Kind, row.Situation, row.PayloadJson, row.Prose).Passed)
                ? row.Prose
                : null;

        /// <summary>A daily message's six parts, from its stored prose (EranNarratorService.NormaliseDaily).</summary>
        internal static EranCoachDailyMessageDto DailyParts(string prose)
        {
            try
            {
                using var doc = JsonDocument.Parse(prose);
                var root = doc.RootElement;
                string Field(string name) =>
                    root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                return new EranCoachDailyMessageDto
                {
                    Headline = Field("headline"),
                    Body = Field("body"),
                    Outward = Field("outward"),
                    OneThing = Field("one_thing"),
                    SayThis = Field("say_this"),
                    WhatTellsYou = Field("what_tells_you")
                };
            }
            catch (JsonException)
            {
                // Written before the daily message was stored in parts.
                return new EranCoachDailyMessageDto { Body = prose };
            }
        }
    }
}
