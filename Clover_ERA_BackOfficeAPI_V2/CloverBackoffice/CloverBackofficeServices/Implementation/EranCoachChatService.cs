using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// EranCoach chat orchestration. Loads the coach system prompt (with the CLOVER catalog
    /// baked in), selects the active Search-model by priority, and runs the agentic retrieval
    /// loop: stream a turn, and if it ends in tool_use, run the tools, feed the results back,
    /// and continue (up to a cap). Text is buffered until the first retrieval, then streamed
    /// live, so a pre-retrieval draft never reaches the client. Ported from the reference
    /// /api/chat. Stateless: the client sends the full history each turn.
    /// </summary>
    public class EranCoachChatService : IEranCoachChatService
    {
        private const int SearchModelTypeId = 1;   // ModelConfiguration.ModelConfigTypeId for Search-models
        private const int MaxTurns = 24;           // cap history sent to the model
        private const int MaxIterations = 6;       // cap tool-loop round trips

        private readonly IDocumentEmbeddingRepository _modelRepo;
        private readonly IEranCoachAnthropicClient _client;
        private readonly IEranCoachTools _tools;
        private readonly IEranCoachTeamContextService _teamContext;
        private readonly IEranCoachMemoryService _memory;
        private readonly IEngineChatContextService _engineChat;
        private readonly ITokenReaderService _tokenReader;
        private readonly IUnitAccessService _unitAccess;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<EranCoachChatService> _logger;
        private readonly IManagerActionService? _managerActions;

        // Plan: Clover Resources (worksheets) for this manager. Null = not limited.
        private readonly IPlanFeatureGate? _planGate;

        private const string WorksheetsNotInPlanBlock =
            "## PLAN: NO CLOVER RESOURCES\n\n" +
            "This manager's plan (Light) does not include Clover Resources. Never name, quote, describe or " +
            "recommend a worksheet (IDs such as C1, V8, OP3) or its session, even when one appears in the " +
            "context above. Coach with micro-actions and practical steps instead. If they ask for a worksheet " +
            "or its materials, say Clover Resources come with the Pro plan.";

        private static string? _cachedPrompt;

        public EranCoachChatService(
            IDocumentEmbeddingRepository modelRepo,
            IEranCoachAnthropicClient client,
            IEranCoachTools tools,
            IEranCoachTeamContextService teamContext,
            IEranCoachMemoryService memory,
            IEngineChatContextService engineChat,
            ITokenReaderService tokenReader,
            IUnitAccessService unitAccess,
            IWebHostEnvironment env,
            ILogger<EranCoachChatService> logger,
            IManagerActionService? managerActions = null,
            IPlanFeatureGate? planGate = null)
        {
            _planGate = planGate;
            _managerActions = managerActions;
            _modelRepo = modelRepo;
            _client = client;
            _tools = tools;
            _teamContext = teamContext;
            _memory = memory;
            _engineChat = engineChat;
            _tokenReader = tokenReader;
            _unitAccess = unitAccess;
            _env = env;
            _logger = logger;
        }

        public async IAsyncEnumerable<string> StreamReplyAsync(
            EranCoachChatRequestDto request,
            int? managerUserId = null,
            string? portfolioContext = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            // Who is asking and which units their role entitles them to. Resolved fresh from the
            // database on every turn, so a role or team-assignment change takes effect on the very
            // next message — no session restart, cache clear, or memory reset required.
            var caller = await ResolveCallerAsync(ct);

            // Portfolio View: grounded in the selected manager's teams (or all of the portfolio),
            // never in one team, so no unit, no engine team and no team report.
            var portfolio = request?.Portfolio == true;

            // The team this turn is scoped to. UnitId is client-supplied, so it is only honoured
            // when the caller's role actually covers it. The report service re-checks it as well;
            // dropping it here means an out-of-scope id never becomes the tools' default team in
            // the first place.
            long? requestedUnit = !portfolio && request?.UnitId is uint u && u > 0 ? u : (long?)null;
            long? unitScope = requestedUnit is long r && caller.Units.Contains(r) ? r : null;

            if (requestedUnit is long rejected && unitScope is null)
                _logger.LogWarning(
                    "[EranCoach] Ignoring out-of-scope unit {Unit} supplied by the client; caller's role does not cover it.",
                    rejected);

            _tools.CurrentUnitId = unitScope;

            // Plan: a manager without Clover Resources (Light) gets no worksheets from Eran.
            var worksheetsAllowed = _planGate == null || managerUserId is not int planUser
                || await _planGate.IsIncludedAsync(planUser, Infrastructure.DTO.PlanManagement.PlanFeatureKeys.CloverResources, ct);
            _tools.WorksheetsAllowed = worksheetsAllowed;

            var cleaned = CleanMessages(request);
            var lastUserMessage = cleaned.LastOrDefault(m => m.Role == "user")?.Content ?? "";

            // A team on the deterministic layer is described by the engine: its reading, the move
            // in front of the manager and its history, with the MVP's chat rules. The older
            // engagement report is then left out, so the coach has one account of the team.
            EngineChatContext? engine = null;
            if (unitScope is long engineUnit)
                engine = await _engineChat.BuildAsync((uint)engineUnit, managerUserId, lastUserMessage, ct);

            // Ground on the team's report when one is in scope. The access check above runs before
            // this read, so the shared per-unit context cache is never reached by a caller who
            // failed it.
            string? teamContext = null;
            if (portfolio)
                teamContext = portfolioContext;
            else if (unitScope is long unit && engine is null)
                teamContext = await _teamContext.GetTeamContextTextAsync((uint)unit, ct);

            // Read this manager's rolling memory for THIS team only, so nothing said about one
            // team reaches the coach while another is selected. The Portfolio View neither reads
            // nor writes it: one manager's teams must not surface while another is selected.
            string? memory = null;
            if (!portfolio && managerUserId is int mid && mid > 0)
                memory = await _memory.GetAsync(mid, unitScope, ct);

            // The team's open manual actions (api/actions). An engine team has them in its engine
            // block already; any other team gets them here.
            string? ownActions = null;
            if (!portfolio && engine is null && _managerActions is not null && unitScope is long scoped)
                ownActions = await _managerActions.EranChatBlockAsync((uint)scoped, ct);

            var systemBlocks = BuildSystemBlocks(teamContext, memory, caller, engine?.SystemBlock, ownActions);
            if (!worksheetsAllowed)
                systemBlocks = systemBlocks.Append(new CoachSystemBlock(WorksheetsNotInPlanBlock, null)).ToList();
            var model = await SelectModelByPriorityAsync();
            var tools = _tools.Definitions;

            var messages = new List<CoachMessage>();
            if (engine?.History is { } history)
            {
                // The team's thread from the database, its last turns stamped with the day they
                // were asked (MVP), then the question being answered.
                foreach (var turn in history)
                    messages.Add(new CoachMessage(turn.Role, new List<CoachBlock> { new CoachTextBlock(turn.Content) }));
                messages.Add(new CoachMessage("user", new List<CoachBlock> { new CoachTextBlock(lastUserMessage) }));
            }
            else
            {
                foreach (var m in cleaned)
                    messages.Add(new CoachMessage(m.Role, new List<CoachBlock> { new CoachTextBlock(m.Content) }));
            }

            _logger.LogInformation(
                "[EranCoach] Streaming reply via model '{Model}' (provider={Provider}), {Turns} turn(s).",
                model.ModelName, model.Provider, messages.Count);

            // A team on the deterministic layer: the reply is checked against Eran's rules before
            // the manager sees any of it, so it is written in full first and sent in one piece.
            // Every other team streams exactly as before.
            if (engine is not null)
            {
                var guarded = await GuardedEngineReplyAsync(engine, (uint)unitScope!.Value, systemBlocks, messages, tools, model, lastUserMessage, ct);
                if (guarded.Delivered.Length > 0) yield return guarded.Delivered;
                await TrySaveMemoryAsync(managerUserId, unitScope, lastUserMessage, guarded.ForMemory, model, ct);
                yield break;
            }

            // Stream text tokens to the client live as they arrive (ChatGPT-style), across every
            // turn — any pre-tool preamble and the final answer alike — so nothing is buffered.
            for (var iteration = 0; iteration < MaxIterations; iteration++)
            {
                var turnText = new StringBuilder();
                var toolUses = new List<CoachToolUseBlock>();
                IReadOnlyList<CoachBlock>? turnContent = null;
                string? stopReason = null;
                const bool live = true;

                await foreach (var ev in _client.StreamTurnAsync(systemBlocks, messages, tools, model, ct))
                {
                    switch (ev)
                    {
                        case CoachTextDelta d:
                            turnText.Append(d.Text);
                            if (live && d.Text.Length > 0) yield return d.Text;
                            break;
                        case CoachToolUseReady t:
                            toolUses.Add(new CoachToolUseBlock(t.Id, t.Name, t.InputJson));
                            break;
                        case CoachTurnEnded e:
                            stopReason = e.StopReason;
                            break;
                        case CoachAssistantContent a:
                            turnContent = a.Blocks;
                            break;
                    }
                }

                if (stopReason == "tool_use" && toolUses.Count > 0)
                {
                    // Record the assistant turn (any text + the tool_use blocks) for context,
                    // then run each tool and feed the results back as a user turn.
                    // The turn exactly as the model wrote it, thinking blocks included, when the client
                    // has it (Anthropic): newer Claude models require thinking sent back unchanged.
                    var assistantBlocks = turnContent is { Count: > 0 } ? new List<CoachBlock>(turnContent) : new List<CoachBlock>();
                    if (assistantBlocks.Count == 0)
                    {
                        if (turnText.Length > 0) assistantBlocks.Add(new CoachTextBlock(turnText.ToString()));
                        assistantBlocks.AddRange(toolUses);
                    }
                    messages.Add(new CoachMessage("assistant", assistantBlocks));

                    var resultBlocks = new List<CoachBlock>(toolUses.Count);
                    foreach (var tu in toolUses)
                    {
                        var result = await _tools.RunAsync(tu.Name, tu.InputJson, ct);
                        resultBlocks.Add(new CoachToolResultBlock(tu.Id, result));
                    }
                    messages.Add(new CoachMessage("user", resultBlocks));
                    continue; // let the model read the results
                }

                // Final turn (no tool call) — all text has already been streamed live above.
                // Post-conversation: fold this exchange into the manager's rolling memory (best-effort).
                if (!portfolio)
                    await TrySaveMemoryAsync(managerUserId, unitScope, lastUserMessage, turnText.ToString(), model, ct);
                yield break;
            }
        }

        /// <summary>What goes to the manager, and what the rolling memory keeps (the final turn).</summary>
        private sealed record GuardedReply(string Delivered, string ForMemory);

        /// <summary>
        /// The engine team's reply through Eran's guard (the brief, section 3.5): write it, check
        /// it against the situation the chat was given; on a breach, one retry with an extra user
        /// turn naming the rules (labels only); on a second breach, the chat's fixed card.
        /// </summary>
        private async Task<GuardedReply> GuardedEngineReplyAsync(
            EngineChatContext engine, uint unit, IReadOnlyList<CoachSystemBlock> systemBlocks, List<CoachMessage> messages,
            IReadOnlyList<object> tools, ModelConfiguration model, string question, CancellationToken ct)
        {
            // The manager's own actions were given in their words, so quoting them is not inventing.
            var given = string.IsNullOrWhiteSpace(engine.ManagerActionsText)
                ? engine.Situation
                : $"{engine.Situation}\n{engine.ManagerActionsText}";
            var situation = GuardSource.FromText(given, engine.ActionText);

            var (text, final, toolText) = await CollectReplyAsync(systemBlocks, messages, tools, model, ct);
            // A worksheet the coach's own tools returned this turn was handed over, not invented.
            var source = situation.WithCodesFrom(toolText);
            var verdict = EranGuard.CheckChat(text, source);
            if (verdict.Passed && text.Trim().Length > 0)
            {
                if (verdict.Warned)
                    EranGuardLog.Catch(_logger, "chat", unit, engine.Day, 1, EranGuardLog.Passed, verdict.RulesText, model.ModelName);
                return new GuardedReply(text, final);
            }

            var rules = verdict.Passed ? "empty reply" : verdict.RulesText;
            EranGuardLog.Catch(_logger, "chat", unit, engine.Day, 1, EranGuardLog.Retried, rules, model.ModelName);
            // The rejected reply is not sent back: an extra user turn names the rules (labels only),
            // so the model is not primed with the words it must not use.
            messages.Add(new CoachMessage("user", new List<CoachBlock>
            {
                new CoachTextBlock(verdict.Passed
                    ? $"Your last reply was empty. Answer the manager's question.\n\nThe manager's question was: {question}"
                    : $"{EranGuard.RetryFeedback(verdict)}\n\nThe manager's question was: {question}")
            }));

            var (retryText, retryFinal, retryTools) = await CollectReplyAsync(systemBlocks, messages, tools, model, ct);
            var retry = EranGuard.CheckChat(retryText, source.WithCodesFrom(retryTools));
            if (retry.Passed && retryText.Trim().Length > 0)
            {
                EranGuardLog.Catch(_logger, "chat", unit, engine.Day, 2, EranGuardLog.PassedOnRetry, retry.RulesText, model.ModelName);
                return new GuardedReply(retryText, retryFinal);
            }

            EranGuardLog.Catch(_logger, "chat", unit, engine.Day, 2, EranGuardLog.Fallback,
                retry.Passed ? "empty reply" : retry.RulesText, model.ModelName);
            // The card is checked too; one whose action text breaks a rule falls back to the plain card.
            var card = EranGuardFallback.ForChat(engine.ActionText, question);
            if (!EranGuard.CheckChat(card, situation).Passed)
            {
                EranGuardLog.FallbackAlert(_logger, "chat", unit, engine.Day, EranGuard.CheckChat(card, situation).RulesText);
                card = EranGuardFallback.ForChat(null, question);
            }
            return new GuardedReply(card, card);
        }

        /// <summary>
        /// The same tool loop as the streamed reply, run to the end without sending anything:
        /// every turn's text (what the stream would have sent) and the final turn's text.
        /// </summary>
        private async Task<(string Text, string Final, string ToolText)> CollectReplyAsync(
            IReadOnlyList<CoachSystemBlock> systemBlocks, List<CoachMessage> messages,
            IReadOnlyList<object> tools, ModelConfiguration model, CancellationToken ct)
        {
            var all = new StringBuilder();
            var toolText = new StringBuilder();
            for (var iteration = 0; iteration < MaxIterations; iteration++)
            {
                var turnText = new StringBuilder();
                var toolUses = new List<CoachToolUseBlock>();
                IReadOnlyList<CoachBlock>? turnContent = null;
                string? stopReason = null;

                await foreach (var ev in _client.StreamTurnAsync(systemBlocks, messages, tools, model, ct))
                {
                    switch (ev)
                    {
                        case CoachTextDelta d:
                            turnText.Append(d.Text);
                            break;
                        case CoachToolUseReady t:
                            toolUses.Add(new CoachToolUseBlock(t.Id, t.Name, t.InputJson));
                            break;
                        case CoachTurnEnded e:
                            stopReason = e.StopReason;
                            break;
                        case CoachAssistantContent a:
                            turnContent = a.Blocks;
                            break;
                    }
                }
                all.Append(turnText);

                if (stopReason == "tool_use" && toolUses.Count > 0)
                {
                    // The turn exactly as the model wrote it, thinking blocks included, when the client
                    // has it (Anthropic): newer Claude models require thinking sent back unchanged.
                    var assistantBlocks = turnContent is { Count: > 0 } ? new List<CoachBlock>(turnContent) : new List<CoachBlock>();
                    if (assistantBlocks.Count == 0)
                    {
                        if (turnText.Length > 0) assistantBlocks.Add(new CoachTextBlock(turnText.ToString()));
                        assistantBlocks.AddRange(toolUses);
                    }
                    messages.Add(new CoachMessage("assistant", assistantBlocks));

                    var resultBlocks = new List<CoachBlock>(toolUses.Count);
                    foreach (var tu in toolUses)
                    {
                        var result = await _tools.RunAsync(tu.Name, tu.InputJson, ct);
                        toolText.AppendLine(result);
                        resultBlocks.Add(new CoachToolResultBlock(tu.Id, result));
                    }
                    messages.Add(new CoachMessage("user", resultBlocks));
                    continue;
                }

                return (all.ToString(), turnText.ToString(), toolText.ToString());
            }
            return (all.ToString(), string.Empty, toolText.ToString());
        }

        // The signed-in caller and the units their role entitles them to. Best-effort: any failure
        // yields an empty scope, which denies grounding and omits the role block rather than
        // widening access.
        private sealed record Caller(int? UserGroupId, IReadOnlyList<long> Units)
        {
            public static readonly Caller None = new(null, Array.Empty<long>());
        }

        private async Task<Caller> ResolveCallerAsync(CancellationToken ct)
        {
            try
            {
                var user = await _tokenReader.GetCurrentUserAsync();
                if (user == null) return Caller.None;
                var units = await _unitAccess.GetAccessibleUnitIdsAsync(user, ct);
                return new Caller(user.UserGroupId, units);
            }
            catch (UnauthorizedAccessException)
            {
                return Caller.None;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[EranCoach] caller scope resolution failed.");
                return Caller.None;
            }
        }

        // Tells the coach what the signed-in role is allowed to ask about. Without this the model
        // only ever sees "THIS MANAGER'S TEAM" and reads every other team as somebody else's data —
        // which is wrong for an admin, whose whole company (or the whole system) is legitimately
        // theirs to ask about. Access itself is enforced server-side in UnitAccessService; this
        // block only stops the coach from inventing a permission rule the server never applied.
        private static CoachSystemBlock? BuildAccessScopeBlock(Caller caller)
        {
            if (caller.UserGroupId is not int groupId) return null;

            var entitlement = groupId switch
            {
                (int)UserGroups.RridaAdmin =>
                    "a **RRIDA administrator**. They oversee every profile and every team in the system, "
                    + "and may ask about any of them.",
                (int)UserGroups.ProfileAdmin =>
                    "a **profile administrator**. They oversee every team in their profile — not only teams they "
                    + "personally manage — and may ask about any of those teams.",
                (int)UserGroups.TeamManager =>
                    "a **team manager**. They oversee the teams assigned to them and may ask about any of those.",
                (int)UserGroups.PortfolioManager =>
                    "a **portfolio manager**. They oversee the teams of the managers who report to them, directly or "
                    + "through other portfolio managers, and may ask about any of those teams.",
                _ => null
            };

            if (entitlement == null) return null;

            return new CoachSystemBlock(
                "## WHO YOU ARE TALKING TO\n\n"
                + "The person in this conversation is signed in as " + entitlement + "\n\n"
                + "Every team the report tools return for them is a team they are entitled to ask about. When they "
                + "ask about one, answer about that team directly. Do not treat it as someone else's data, do not "
                + "question why they are asking about it, do not redirect them back to another team, and never tell "
                + "them they must be that team's manager to see it. Asking about a team they oversee is their job, "
                + "not avoidance.\n\n"
                + "Access is enforced by the server, not by you: if a tool returns a team, they are allowed to see "
                + "it, and if they are not, the tool says so in its result. Never apply a stricter rule than the "
                + "tool did, and never apply a looser one.\n\n"
                + "**Your memory and this conversation's history are not access grants.** They are a record of what "
                + "was discussed, and a person's role can be narrowed after the fact. Never state a team's themes, "
                + "recommendations, actions, scores, or dates from memory or from earlier in the conversation. Every "
                + "team fact you state must come from a tool result in the current turn, because only the tools are "
                + "role-checked. If the tools will not return a team, you may not discuss that team's data, even if "
                + "your memory contains it — say plainly that you cannot pull that team's report.\n\n"
                + "A \"THIS MANAGER'S TEAM\" block, when present, is only the team currently selected on their "
                + "dashboard. It is the default team for this conversation, not a limit on what they may ask about. "
                + "If a team genuinely has no report yet, say so plainly and leave it there.",
                "1h");
        }

        private async Task TrySaveMemoryAsync(int? managerUserId, long? unitScope, string lastUserMessage, string answer, ModelConfiguration model, CancellationToken ct)
        {
            if (managerUserId is not int mid || mid <= 0) return;
            if (string.IsNullOrWhiteSpace(answer)) return;
            try
            {
                await _memory.SaveAsync(mid, unitScope, lastUserMessage, answer, model, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[EranCoach] memory save failed for manager {Id}.", mid);
            }
        }

        // Assemble the system prompt as cache-aware blocks: the stable persona + catalog
        // (1-hour cache so it isn't re-billed every turn) and the live date (uncached).
        // Team context and manager memory slot in here as further cached blocks in later phases.
        private IReadOnlyList<CoachSystemBlock> BuildSystemBlocks(string? teamContext = null, string? memory = null, Caller? caller = null, string? engineBlock = null, string? managerActions = null)
        {
            var blocks = new List<CoachSystemBlock>
            {
                new CoachSystemBlock(LoadSystemPrompt(), "1h"),
            };

            // What this role may ask about. Sits before the team block so the coach reads the team
            // it is given as the default, not as the only team it is allowed to discuss.
            if (caller != null && BuildAccessScopeBlock(caller) is CoachSystemBlock scope)
                blocks.Add(scope);

            // The manager's team report, if supplied. Stable within a session, so cache it too.
            if (!string.IsNullOrWhiteSpace(teamContext))
                blocks.Add(new CoachSystemBlock(teamContext, "1h"));

            // The manager's rolling memory from past sessions, if any.
            if (!string.IsNullOrWhiteSpace(memory))
                blocks.Add(new CoachSystemBlock(
                    "## ONGOING MEMORY FOR THIS MANAGER\n\n" +
                    "This is your rolling memory of past coaching conversations with this manager: the people " +
                    "and situations discussed, advice you gave, what they committed to, and open follow-ups. Use " +
                    "it for continuity, do not recite it back, and if it conflicts with what they say now, trust " +
                    "the current conversation.\n\n" +
                    "It is continuity only, never authority. It may mention teams this person's role no longer " +
                    "covers, and it is not re-checked against their access. Do not repeat any team's report detail " +
                    "from here — re-fetch it with the report tools, which are role-checked, and answer from that.\n\n"
                    + memory, "1h"));

            // The manager's own open actions. They change as the manager works, so not cached.
            if (!string.IsNullOrWhiteSpace(managerActions))
                blocks.Add(new CoachSystemBlock(managerActions, null));

            // The deterministic layer's account of this team, last of the cached blocks so that
            // where it overrides anything above, it is also what the coach read most recently.
            if (!string.IsNullOrWhiteSpace(engineBlock))
                blocks.Add(new CoachSystemBlock(engineBlock, "1h"));

            var dateStr = DateTime.UtcNow.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);
            blocks.Add(new CoachSystemBlock(
                $"## Current date\nToday is {dateStr} (UTC), injected live by the server.", null));

            return blocks;
        }

        // Persona + coaching rules + CLOVER catalog. Cached after first read.
        private string LoadSystemPrompt()
        {
            if (_cachedPrompt != null) return _cachedPrompt;
            var path = Path.Combine(_env.ContentRootPath, "CloverEra", "Prompts", "eran-coach.md");
            if (!File.Exists(path))
                throw new FileNotFoundException($"EranCoach system prompt not found at '{path}'.");
            var raw = File.ReadAllText(path, Encoding.UTF8);
            var corpus = EranCoachCorpusLoader.Load(_env.ContentRootPath);
            _cachedPrompt = raw.Replace("{{CATALOG}}", EranCoachCorpusLoader.BuildCatalog(corpus));
            return _cachedPrompt;
        }

        // Priority among active Search-models (type 1): Claude Sonnet -> GPT-4o -> default.
        private async Task<ModelConfiguration> SelectModelByPriorityAsync() =>
            EranModelPriority.Pick(await _modelRepo.GetAllModelsAsync(SearchModelTypeId));

        // Keep only well-formed user/assistant turns, trim, and cap the history length.
        private static IReadOnlyList<EranCoachChatMessageDto> CleanMessages(EranCoachChatRequestDto request)
        {
            var incoming = request?.Messages ?? new List<EranCoachChatMessageDto>();
            var clean = incoming
                .Where(m => m != null
                            && (string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                            && !string.IsNullOrWhiteSpace(m.Content))
                .Select(m => new EranCoachChatMessageDto { Role = m.Role.ToLowerInvariant(), Content = m.Content.Trim() })
                .ToList();

            if (clean.Count > MaxTurns)
                clean = clean.Skip(clean.Count - MaxTurns).ToList();

            return clean;
        }
    }
}
