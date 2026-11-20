using System.Text;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Rrida.Engine;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// What Eran Chat is told about a team that is on the deterministic layer, so a manager can
    /// ask about the engine's readings and moves in the same thread. The MVP's chat
    /// (src/eran/prompts.js buildChatPrompt, src/engine/chat.js chatSituation) is the reference:
    /// the preamble's rules, the context block, the chat instructions, where the team is today,
    /// every area the product covers without codes, and earlier turns stamped with their day.
    ///
    /// Added on top of the coach's own prompt rather than replacing it, so the coaching, the
    /// worksheet library and the tools stay. On this team's readings, moves and formatting the
    /// engine's rules win, which the block says in so many words.
    /// </summary>
    public sealed class EngineChatContextService : IEngineChatContextService
    {
        private const string PreambleFile = "CloverEra/Prompts/rrida-eran.md";
        private const string ChatFile = "CloverEra/Prompts/rrida-eran-chat.md";

        /// <summary>The MVP hands the chat the last eight turns.</summary>
        public const int HistoryTurns = 8;

        private static readonly Lazy<string> Preamble = new(() => ReadFile(PreambleFile));
        private static readonly Lazy<string> ChatInstructions = new(() => ReadFile(ChatFile));

        private readonly ApplicationDbContext _context;
        private readonly IRridaEngineContextFactory _engine;
        private readonly EranEngineContextBuilder _contextBuilder;

        public EngineChatContextService(
            ApplicationDbContext context,
            IRridaEngineContextFactory engine,
            EranEngineContextBuilder contextBuilder)
        {
            _context = context;
            _engine = engine;
            _contextBuilder = contextBuilder;
        }

        public async Task<EngineChatContext?> BuildAsync(uint unitId, int? userId, string currentQuestion, CancellationToken ct = default)
        {
            var settings = await _context.EngineTeamSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.BusinessUnitId == unitId, ct);
            if (settings is null || !settings.IsEnabled) return null;

            var clock = new EngineTeamClock(settings.StartDate, settings.WorkingWeek, _engine.Calendar);
            var day = Math.Max(1, clock.DayOf(DateOnly.FromDateTime(DateTime.UtcNow)));
            var (situation, actionText) = await SituationPartsAsync(unitId, ct);

            var block = new StringBuilder()
                .Append("## THIS TEAM IS ON THE DETERMINISTIC LAYER\n\n")
                .Append("Everything in this block is how you talk about this team's readings, its moves and their ")
                .Append("history. Where anything earlier in this prompt, a tool result, your memory or an earlier ")
                .Append("answer says otherwise about this team, this block wins. An older engagement report is not ")
                .Append("this team's reading. The rules on writing below also replace any formatting guidance above.\n\n")
                .Append(Preamble.Value).Append("\n\n")
                .Append(EranEngineContextBuilder.Render(await _contextBuilder.BuildAsync(unitId, ct))).Append("\n\n")
                .Append(ChatInstructions.Value).Append("\n\n")
                .Append($"Where the team is today, day {day}:\n")
                .Append(situation).Append("\n\n")
                .Append("The areas this product covers:\n")
                .Append(Themes(_engine.Context))
                .ToString();

            var history = userId is int uid
                ? await HistoryAsync(unitId, uid, currentQuestion, clock, day, ct)
                : null;

            // The manager's own open actions are in the context block above; the guard may let a
            // reply quote them as the manager wrote them.
            var ownActions = await ManagerActionEranContext.OpenAsync(
                _context, unitId, DateOnly.FromDateTime(DateTime.UtcNow), ct);

            return new EngineChatContext(block, day, history)
            {
                Situation = situation,
                ActionText = actionText,
                ManagerActionsText = ownActions.Count == 0 ? null : ManagerActionEranContext.GuardText(ownActions)
            };
        }

        /// <summary>
        /// Where the team is today, in the manager's language (MVP chatSituation). Everything in
        /// it is true of what was actually delivered: the stored delivery, not the library.
        /// </summary>
        public async Task<string> SituationAsync(uint unitId, CancellationToken ct = default) =>
            (await SituationPartsAsync(unitId, ct)).Text;

        /// <summary>The situation, and the running action's text when there is one.</summary>
        private async Task<(string Text, string? ActionText)> SituationPartsAsync(uint unitId, CancellationToken ct)
        {
            string? runningAction = null;
            // MVP 90fe06d: time in the team's own working days, never the run-day index.
            var settings = await _context.EngineTeamSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.BusinessUnitId == unitId, ct);
            var startDate = settings?.StartDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var day = Math.Max(1, new EngineTeamClock(startDate, settings?.WorkingWeek ?? "mon-fri", _engine.Calendar)
                .DayOf(DateOnly.FromDateTime(DateTime.UtcNow)));
            var daysBack = new EngineWorkingDays(_engine.Context, startDate, settings?.WorkingWeek ?? "mon-fri");
            var state = await _contextBuilder.TeamStateAsync(unitId, ct);
            if (state == "calibrating") state = null;
            var lines = new List<string> { $"The reading on this team is {state ?? "still calibrating"}." };

            var open = await _context.EngineCycles.AsNoTracking()
                .Include(c => c.Deliveries)
                .Where(c => c.BusinessUnitId == unitId && c.Outcome == null && c.DeliveredDay != null)
                .ToListAsync(ct);
            var cycle = open
                .OrderByDescending(c => c.Deliveries.Count == 0 ? c.DeliveredDay : c.Deliveries.Max(d => d.DeliveredDay))
                .ThenByDescending(c => c.CycleIndex)
                .FirstOrDefault();

            if (cycle is not null)
            {
                var delivery = cycle.Deliveries.OrderByDescending(d => d.Rung).FirstOrDefault();
                var rung = delivery?.Rung ?? 1;
                var move = rung == 2
                    ? "a change to how something works, after the conversation did not shift it"
                    : "a conversation, the first move on this";
                var actionText = delivery is null ? null
                    : _engine.Context.Data.Action(delivery.ActionKey)?.Text ?? delivery.DeliveredText;
                runningAction = actionText;
                var confirmed = delivery?.ConfirmedDay is int confirmedDay
                    ? $"They confirmed it {daysBack.Back(day, confirmedDay)}."
                    : "They have not confirmed it yet.";

                lines.Add(
                    $"Delivered {daysBack.Back(day, delivery?.DeliveredDay ?? cycle.DeliveredDay)}: {(cycle.Issue ?? "").ToLowerInvariant()}. "
                    + $"What was asked of the manager is {move}. The action reads: \"{actionText}\" "
                    + $"{confirmed} "
                    + "The next check-back on it comes when a question on it comes round again.");

                if (delivery?.DeliveredWorksheetCode is { Length: > 0 } code)
                {
                    var title = _engine.Context.Data.Worksheet(code)?.Title;
                    // The action text names the worksheet its file wanted, which after a
                    // prerequisite substitution is not the one handed over (MVP chatSituation).
                    var sub = EngineWorksheetCardReader.Read(delivery.DeliveredWorksheetCardJson)?.Substitution;
                    // The chat's fallback card names what was handed over, as the daily card does.
                    if (sub is not null) runningAction = $"Run {code} {title}".TrimEnd() + " with the team.";
                    lines.Add($"The worksheet they were actually handed is {code} {title}".TrimEnd() + "."
                        + (sub is null ? "" :
                            $" The action text names {sub.WantedTitle}, which is not what went out: it "
                            + $"assumes {string.Join(" and ", sub.Unmet)} has been run and closed first and this team "
                            + "has not done that. Talk about the worksheet they were given, not the one the "
                            + "action names, and say where the named one sits in the sequence if it comes up."));
                }

                lines.Add($"What they are asked to confirm is: {delivery?.DeliveredWorkedIf}");
            }
            else
            {
                lines.Add("Nothing is running today. " + (state is not null
                    ? EngineState.NoFloorPosture.GetValueOrDefault(state, "")
                    : "Nothing is asked before the first reading on day seven."));
            }

            var last = await _context.EngineCycles.AsNoTracking()
                .Where(c => c.BusinessUnitId == unitId && c.Outcome != null && c.ResolvedDay != null)
                .OrderByDescending(c => c.ResolvedDay).ThenByDescending(c => c.CycleIndex)
                .Select(c => new { c.Issue, c.Outcome, c.ResolvedDay })
                .FirstOrDefaultAsync(ct);
            if (last is not null)
            {
                var how = last.Outcome switch
                {
                    "closed" => "closed, and the reports fell away",
                    "unread" => "ended without a check-back, the question went unanswered",
                    "failed" => "ran out of moves without shifting",
                    _ => "ended without being confirmed, so nothing could be credited to it"
                };
                lines.Add($"Most recently: {(last.Issue ?? "").ToLowerInvariant()} {how}, {daysBack.Back(day, last.ResolvedDay)}.");
            }

            return (string.Join(" ", lines.Select(l => l.Trim())), runningAction);
        }

        /// <summary>
        /// Every area the product covers, grouped by dimension, with no identifiers: the manager
        /// is never to read a catalogue of codes back (MVP buildChatPrompt).
        /// </summary>
        public static string Themes(EngineContext ctx)
        {
            var byPillar = new List<(string Pillar, List<string> Issues)>();
            foreach (var file in ctx.Data.IssueFiles.Values)
            {
                if (string.IsNullOrWhiteSpace(file.Issue)) continue;
                var pillar = file.Pillar ?? "";
                var group = byPillar.FirstOrDefault(p => p.Pillar == pillar);
                if (group.Issues is null)
                {
                    group = (pillar, new List<string>());
                    byPillar.Add(group);
                }
                group.Issues.Add(file.Issue);
            }

            return string.Join("\n", byPillar.Select(p =>
                $"  {p.Pillar}: {string.Join("; ", p.Issues.Distinct(StringComparer.Ordinal).OrderBy(i => i, StringComparer.Ordinal))}"));
        }

        /// <summary>
        /// The thread's last turns, the user's earlier questions stamped with the day they were
        /// asked when that is not today. Without the stamp, an answer given while an older move
        /// was running reads as a competing account of today (MVP buildChatPrompt).
        /// </summary>
        private async Task<List<EngineChatTurn>> HistoryAsync(
            uint unitId, int userId, string currentQuestion, EngineTeamClock clock, int today, CancellationToken ct)
        {
            var conversations = _context.EranCoachConversations
                .Where(c => c.UserId == userId && c.BusinessUnitId == unitId && !c.IsDel)
                .Select(c => c.Id);

            var recent = await _context.EranCoachMessages.AsNoTracking()
                .Where(m => !m.IsDel && conversations.Contains(m.ConversationId))
                .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
                .Take(HistoryTurns + 1)
                .Select(m => new { m.Role, m.Content, m.CreatedAt })
                .ToListAsync(ct);

            // The question being answered is already stored by the time the reply runs; it goes
            // in as the current turn, not as history.
            if (recent.Count > 0 && recent[0].Role == "user"
                && string.Equals(recent[0].Content.Trim(), currentQuestion.Trim(), StringComparison.Ordinal))
                recent.RemoveAt(0);

            var ordered = recent.Take(HistoryTurns).Reverse().ToList();

            // A question whose reply never got stored (the turn failed) is dropped, otherwise the
            // model reads it as still open and answers it instead of the current question.
            var turns = ordered
                .Where((m, i) => m.Role != "user" || (i + 1 < ordered.Count && ordered[i + 1].Role == "assistant"))
                .Select(m => StampTurn(m.Role, m.Content, clock.DayOf(DateOnly.FromDateTime(m.CreatedAt)), today))
                .ToList();

            // A model conversation starts with the user.
            while (turns.Count > 0 && turns[0].Role != "user") turns.RemoveAt(0);
            return turns;
        }

        public static EngineChatTurn StampTurn(string role, string content, int askedDay, int today) =>
            role == "user" && askedDay != today
                ? new EngineChatTurn("user", $"[asked on day {askedDay}] {content}")
                : new EngineChatTurn(role, content);

        private static string ReadFile(string relative)
        {
            var path = Path.Combine(AppContext.BaseDirectory, relative);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Eran's prompt file was not found at '{path}'.");
            return File.ReadAllText(path);
        }
    }
}
