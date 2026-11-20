using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.API.Controllers
{
    /// <summary>
    /// EranCoach — a chat coach for managers, ported from the ERAn reference app.
    /// Persistent chat: turns are stored per owner in EranCoachConversation / EranCoachMessage,
    /// the reply still streams live as the model produces it, and the coach's model is resolved
    /// from central configuration (ModelConfiguration, Search type). Admins manage that model
    /// via the model-config endpoints. All routes are owner-scoped by the caller's token.
    /// </summary>
    [Authorize]
    [Route("api/eran-coach")]
    [ApiController]
    public class EranCoachController : ControllerBase
    {
        private readonly IEranCoachChatService _chat;
        private readonly IEranCoachSeedService _seed;
        private readonly IEranCoachConversationService _conversations;
        private readonly IEranCoachTimelineService _timeline;
        private readonly IEranCoachModelConfigService _modelConfig;
        private readonly IPortfolioViewService _portfolio;
        private readonly ITokenReaderService _tokenReader;
        private readonly EranCoachSeedWorkQueue _seedQueue;
        private readonly EranCoachSeedRunState _seedState;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<EranCoachController> _logger;
        private readonly IPlanFeatureGate _planGate;

        public EranCoachController(
            IEranCoachChatService chat,
            IEranCoachSeedService seed,
            IEranCoachConversationService conversations,
            IEranCoachTimelineService timeline,
            IEranCoachModelConfigService modelConfig,
            IPortfolioViewService portfolio,
            ITokenReaderService tokenReader,
            EranCoachSeedWorkQueue seedQueue,
            EranCoachSeedRunState seedState,
            IWebHostEnvironment env,
            ILogger<EranCoachController> logger,
            IPlanFeatureGate planGate)
        {
            _planGate = planGate;
            _env = env;
            _chat = chat;
            _seed = seed;
            _conversations = conversations;
            _timeline = timeline;
            _modelConfig = modelConfig;
            _portfolio = portfolio;
            _tokenReader = tokenReader;
            _seedQueue = seedQueue;
            _seedState = seedState;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Chat
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Run a coach turn and persist it. Body: { messages: [{ role, content }], unitId?,
        /// conversationId? } — the full conversation, last turn must be the user. When
        /// conversationId is null a new conversation is created. The new/target conversation id
        /// and the persisted user-message id come back as X-Conversation-Id / X-User-Message-Id
        /// headers (before the body), then the reply streams as text/plain. The assistant turn is
        /// persisted once the stream completes.
        /// </summary>
        [HttpPost("chat")]
        public async Task Chat([FromBody] EranCoachChatRequestDto request, CancellationToken ct)
        {
            var last = request?.Messages?.LastOrDefault();
            var valid = last != null
                        && string.Equals(last.Role, "user", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(last.Content);

            if (!valid)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                await Response.WriteAsJsonAsync(
                    ApiResponseHandler.Fail<string>("Provide at least one message; the last message must be from the user."),
                    ct);
                return;
            }

            // Owner from the token (the TokenAuthentication middleware has already validated it).
            var managerId = _tokenReader.GetUserIdFromToken();
            if (managerId is not int uid || uid <= 0)
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                await Response.WriteAsJsonAsync(ApiResponseHandler.Fail<string>("Not authenticated."), ct);
                return;
            }

            // Plan: Eran chats per month, a new conversation when none is sent, and Portfolio
            // View for a portfolio turn. 403 with the reason before anything is stored.
            var gateKeys = new List<string> { PlanFeatureKeys.EranCoach };
            if (request!.ConversationId is not > 0) gateKeys.Add(PlanFeatureKeys.EranConversations);
            if (request.Portfolio) gateKeys.Add(PlanFeatureKeys.PortfolioView);
            foreach (var key in gateKeys)
            {
                var gate = await _planGate.CheckAsync(uid, key, ct);
                if (gate.Allowed) continue;
                Response.StatusCode = StatusCodes.Status403Forbidden;
                await Response.WriteAsJsonAsync(ApiResponseHandler.Fail<string>(gate.Message!), ct);
                return;
            }

            // Scope snapshot used only when a brand-new conversation is created.
            var currentUser = await _tokenReader.GetCurrentUserAsync();
            var profileId = currentUser?.ProfileId;
            var userGroupId = currentUser?.UserGroupId;
            // The selected team. A conversation belongs to one team, and 0 means none.
            var businessUnitId = TeamOf(request!.UnitId);

            // Portfolio View: grounded in the selected manager's teams (or All Managers). The
            // scope is checked before anything is stored, so a manager outside the caller's
            // portfolio is refused and Eran never sees their teams. No team on these turns.
            string? portfolioContext = null;
            if (request.Portfolio)
            {
                var scope = await _portfolio.BuildEranContextTextAsync(uid, request.ManagerId, request.ProfileId, ct);
                if (!scope.IsSuccess)
                {
                    Response.StatusCode = scope.Error switch
                    {
                        Infrastructure.DTO.TeamHealth.TeamMilestoneError.Forbidden => StatusCodes.Status403Forbidden,
                        Infrastructure.DTO.TeamHealth.TeamMilestoneError.NotFound => StatusCodes.Status404NotFound,
                        _ => StatusCodes.Status400BadRequest
                    };
                    await Response.WriteAsJsonAsync(ApiResponseHandler.Fail<string>(scope.Message), ct);
                    return;
                }
                portfolioContext = scope.Data;
                businessUnitId = 0;
            }

            // Create-or-resolve the conversation and store the incoming user turn.
            var turn = await _conversations.BeginTurnAsync(
                uid, profileId, userGroupId, businessUnitId, request.ConversationId, last!.Content.Trim(), ct);

            if (turn == null)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                await Response.WriteAsJsonAsync(ApiResponseHandler.Fail<string>("Conversation not found."), ct);
                return;
            }

            if (turn.TeamMismatch)
            {
                Response.StatusCode = StatusCodes.Status409Conflict;
                await Response.WriteAsJsonAsync(
                    ApiResponseHandler.Fail<string>("This conversation belongs to another team. Start a new conversation for the selected team."),
                    ct);
                return;
            }

            // Ids the client needs to keep the thread going. Exposed so the cross-origin browser
            // client can actually read them off the response.
            Response.Headers["X-Conversation-Id"] = turn.ConversationId.ToString(CultureInfo.InvariantCulture);
            Response.Headers["X-User-Message-Id"] = turn.UserMessageId.ToString(CultureInfo.InvariantCulture);
            Response.Headers["Access-Control-Expose-Headers"] = "X-Conversation-Id, X-User-Message-Id";

            Response.ContentType = "text/plain; charset=utf-8";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no"; // defeat proxy-level buffering (nginx etc.)
            // Flush tokens to the client as they arrive rather than buffering the whole reply.
            HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            // Worksheet names the coach mentions ("Worksheet E3") go out as download anchors, the
            // same treatment the report/action-hub responses get. Done through a streamer because
            // a reference regularly straddles two chunks, which a per-chunk replace would miss.
            var linker = new WorksheetLinkStreamer(WorksheetFolderPath());

            var answer = new StringBuilder();
            var wroteAny = false;
            try
            {
                await foreach (var chunk in _chat.StreamReplyAsync(request!, uid, portfolioContext, ct))
                {
                    if (string.IsNullOrEmpty(chunk)) continue;
                    wroteAny = true;
                    answer.Append(chunk); // raw text — what gets persisted
                    await WriteChunkAsync(linker.Push(chunk), ct);
                }

                // Release whatever the linker was still holding back.
                await WriteChunkAsync(linker.Flush(), ct);

                if (!wroteAny)
                {
                    const string fallback =
                        "I wasn't able to put a response together. Try asking again, or rephrase what's happening on your team.";
                    answer.Append(fallback);
                    await Response.WriteAsync(fallback, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected mid-stream — still persist whatever we produced below.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EranCoach] chat stream failed.");
                // Headers/status may already be sent once streaming has begun, so we can only
                // append a friendly tail to whatever the client already received. Push the held-back
                // text out first so the reply does not lose its last few words.
                try { await WriteChunkAsync(linker.Flush(), ct); } catch { /* connection gone */ }

                var friendly = $"Something went wrong on my side. ({ex.Message})";
                var text = wroteAny ? $"\n\n---\n\n{friendly}" : friendly;
                try { await Response.WriteAsync(text, ct); } catch { /* connection gone */ }
            }
            finally
            {
                // Persist the assistant turn. Best-effort and detached from the client's token so a
                // disconnect still keeps the transcript; never fail the request over persistence.
                await PersistAssistantTurnSafeAsync(turn.ConversationId, answer.ToString());
            }
        }

        // Writes one already-linkified slice and pushes it to the client immediately. No-ops on the
        // empty string the linker returns while it is holding text back.
        private async Task WriteChunkAsync(string text, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(text)) return;
            await Response.WriteAsync(text, ct);
            await Response.Body.FlushAsync(ct);
        }

        // Where the worksheet PDFs live — the same folder api/rag/get-worksheet-file serves from.
        // Passing it lets the linker skip references whose PDF is missing (the catalog lists more
        // codes than there are files) so we never render a link that downloads nothing.
        private string? WorksheetFolderPath()
        {
            var webRoot = _env.WebRootPath;
            return string.IsNullOrWhiteSpace(webRoot) ? null : Path.Combine(webRoot, "WorkSheetFile");
        }

        private async Task PersistAssistantTurnSafeAsync(int conversationId, string answer)
        {
            if (string.IsNullOrWhiteSpace(answer)) return;
            try
            {
                await _conversations.CompleteTurnAsync(conversationId, answer, null, null, null, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[EranCoach] failed to persist assistant turn for conversation {Id}.", conversationId);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Conversations (owner-scoped)
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// GET api/eran-coach/timeline?unitId=&amp;before=&amp;take=&amp;cycleId= — the team's Eran thread: the
        /// engine's daily, weekly, outcome, team-note, escalation and monthly messages and my
        /// chat about the team, as one continuous timeline, oldest first. Page back with
        /// <c>before</c> = the previous page's nextBefore. Also returns the team's reading and
        /// the move in front of the manager, and the conversationId new chat turns go into.
        /// With cycleId (as returned by weekly-flags): only that cycle's engine messages — its
        /// daily messages, signal read and team note — and no chat, and activeAction is that
        /// cycle's action (open or resolved) rather than today's.
        /// </summary>
        [HttpGet("timeline")]
        public async Task<IActionResult> Timeline(
            [FromQuery] uint unitId, [FromQuery] DateTime? before, [FromQuery] int take, [FromQuery] long? cycleId, CancellationToken ct)
        {
            if (unitId == 0)
                return Ok(ApiResponseHandler.Fail<string>("unitId is required."));

            var user = await _tokenReader.GetCurrentUserAsync();
            if (user is null)
                return Unauthorized(ApiResponseHandler.Fail<string>("Not authenticated."));

            try
            {
                var timeline = await _timeline.GetAsync(user, unitId,
                    before is null ? null : DateTime.SpecifyKind(before.Value.ToUniversalTime(), DateTimeKind.Utc), take, cycleId, ct);
                return Ok(ApiResponseHandler.Success(timeline, "Timeline."));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                // No such cycle for this team.
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
        }

        /// <summary>
        /// List my conversations (pinned first, then most recent). With unitId, only the
        /// conversations of that team — what Team Health shows for the selected team.
        /// </summary>
        [HttpGet("conversations")]
        public async Task<IActionResult> ListConversations([FromQuery] uint? unitId, CancellationToken ct)
        {
            if (!TryGetUserId(out var uid))
                return Ok(ApiResponseHandler.Fail<string>("Not authenticated."));

            var list = await _conversations.ListAsync(uid, TeamOf(unitId), ct);
            return Ok(ApiResponseHandler.Success(list, "Conversations."));
        }

        /// <summary>Start a new (empty) conversation.</summary>
        [HttpPost("conversations")]
        public async Task<IActionResult> CreateConversation([FromBody] CreateEranCoachConversationDto? body, CancellationToken ct)
        {
            if (!TryGetUserId(out var uid))
                return Ok(ApiResponseHandler.Fail<string>("Not authenticated."));

            // Plan: Eran conversations per month.
            var gate = await _planGate.CheckAsync(uid, PlanFeatureKeys.EranConversations, ct);
            if (!gate.Allowed)
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponseHandler.Fail<string>(gate.Message!));

            var user = await _tokenReader.GetCurrentUserAsync();
            var dto = await _conversations.CreateAsync(
                uid, user?.ProfileId, user?.UserGroupId, TeamOf(body?.UnitId), body?.Title, ct);

            return Ok(ApiResponseHandler.Success(dto, "Conversation created."));
        }

        /// <summary>
        /// Load a conversation's transcript. With unitId, a conversation of another team is
        /// reported as not found.
        /// </summary>
        [HttpGet("conversations/{id:int}/messages")]
        public async Task<IActionResult> GetMessages(int id, [FromQuery] uint? unitId, CancellationToken ct)
        {
            if (!TryGetUserId(out var uid))
                return Ok(ApiResponseHandler.Fail<string>("Not authenticated."));

            var messages = await _conversations.GetMessagesAsync(uid, id, TeamOf(unitId), ct);
            if (messages == null)
                return Ok(ApiResponseHandler.Fail<string>("Conversation not found."));

            return Ok(ApiResponseHandler.Success(messages, "Messages."));
        }

        /// <summary>Rename and/or pin a conversation.</summary>
        [HttpPatch("conversations/{id:int}")]
        public async Task<IActionResult> UpdateConversation(int id, [FromBody] UpdateEranCoachConversationDto? body, CancellationToken ct)
        {
            if (!TryGetUserId(out var uid))
                return Ok(ApiResponseHandler.Fail<string>("Not authenticated."));

            if (body == null || (body.Title == null && body.IsPinned == null))
                return Ok(ApiResponseHandler.Fail<string>("Nothing to update; provide title and/or isPinned."));

            var dto = await _conversations.UpdateAsync(uid, id, body.Title, body.IsPinned, ct);
            if (dto == null)
                return Ok(ApiResponseHandler.Fail<string>("Conversation not found."));

            return Ok(ApiResponseHandler.Success(dto, "Conversation updated."));
        }

        /// <summary>Soft-delete a conversation.</summary>
        [HttpDelete("conversations/{id:int}")]
        public async Task<IActionResult> DeleteConversation(int id, CancellationToken ct)
        {
            if (!TryGetUserId(out var uid))
                return Ok(ApiResponseHandler.Fail<string>("Not authenticated."));

            var ok = await _conversations.DeleteAsync(uid, id, ct);
            if (!ok)
                return Ok(ApiResponseHandler.Fail<string>("Conversation not found."));

            return Ok(ApiResponseHandler.Success(new { id }, "Conversation deleted."));
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Model configuration (admin) — Req 1, Option A
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>View the coach's active model + generation settings. Admin only.</summary>
        [ApiExplorerSettings(IgnoreApi = true)] // hidden from Swagger for now; still callable
        [HttpGet("model-config")]
        public async Task<IActionResult> GetModelConfig(CancellationToken ct)
        {
            if (!await _tokenReader.IsAdminAsync())
                return Ok(ApiResponseHandler.Fail<string>("Admin access required."));

            var cfg = await _modelConfig.GetActiveAsync(ct);
            if (cfg == null)
                return Ok(ApiResponseHandler.Fail<string>("No coach model is configured."));

            return Ok(ApiResponseHandler.Success(cfg, "Active coach model configuration."));
        }

        /// <summary>Set the coach's active model and optional settings. Admin only.</summary>
        [ApiExplorerSettings(IgnoreApi = true)] // hidden from Swagger for now; still callable
        [HttpPut("model-config")]
        public async Task<IActionResult> UpdateModelConfig([FromBody] UpdateEranCoachModelConfigDto? body, CancellationToken ct)
        {
            if (!await _tokenReader.IsAdminAsync())
                return Ok(ApiResponseHandler.Fail<string>("Admin access required."));

            if (body == null || body.ModelConfigurationId <= 0)
                return Ok(ApiResponseHandler.Fail<string>("Provide a valid modelConfigurationId."));

            var user = await _tokenReader.GetCurrentUserAsync();
            var modifiedBy = user?.UserId.ToString(CultureInfo.InvariantCulture);

            var cfg = await _modelConfig.SetActiveAsync(body, modifiedBy, ct);
            if (cfg == null)
                return Ok(ApiResponseHandler.Fail<string>("Model not found or not a selectable coach model."));

            return Ok(ApiResponseHandler.Success(cfg, "Coach model configuration updated."));
        }

        /// <summary>List the models the coach can be pointed at. Admin only.</summary>
        [ApiExplorerSettings(IgnoreApi = true)] // hidden from Swagger for now; still callable
        [HttpGet("models")]
        public async Task<IActionResult> GetModels(CancellationToken ct)
        {
            if (!await _tokenReader.IsAdminAsync())
                return Ok(ApiResponseHandler.Fail<string>("Admin access required."));

            var models = await _modelConfig.GetModelsAsync(ct);
            return Ok(ApiResponseHandler.Success(models, "Selectable coach models."));
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Knowledge base (existing)
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Queue a seed (or re-seed) of the CLOVER corpus into the pgvector store so the retrieval
        /// tools have content to return. Admin only. Idempotent — replaces the coach namespace.
        /// Run once after deploy (and again whenever the corpus bundle changes).
        /// Returns as soon as the run is queued: embedding the whole bundle takes longer than the
        /// gateway keeps a request open (it answers 500 "request timed out" first), so the work
        /// runs on <see cref="EranCoachSeedBackgroundService"/> and survives the response.
        /// Poll GET corpus-status for progress. A second call while a run is in flight is rejected.
        /// </summary>
        [ApiExplorerSettings(IgnoreApi = true)] // hidden from Swagger for now; still callable
        [HttpPost("seed-corpus")]
        public async Task<IActionResult> SeedCorpus()
        {
            if (!await _tokenReader.IsAdminAsync())
                return Ok(ApiResponseHandler.Fail<string>("Admin access required."));

            if (!_seedState.TryClaim())
            {
                var running = _seedState.Snapshot();
                return Ok(ApiResponseHandler.Fail(
                    running, "Corpus seeding is already in progress. Poll api/eran-coach/corpus-status."));
            }

            var requestedBy = _tokenReader.GetUserIdFromToken()?.ToString(CultureInfo.InvariantCulture);

            // Capacity-1 channel; the claim above means the slot is free, so this cannot block.
            if (!_seedQueue.Writer.TryWrite(new EranCoachSeedWorkItem { RequestedBy = requestedBy }))
            {
                _seedState.Abandon("Could not queue the seeding run.");
                _logger.LogError("[EranCoach] Failed to queue corpus seeding — work queue rejected the item.");
                return Ok(ApiResponseHandler.Fail<string>("Could not queue the seeding run. Try again."));
            }

            _logger.LogInformation("[EranCoach] Corpus seeding queued by user {UserId}.", requestedBy ?? "unknown");

            return Ok(ApiResponseHandler.Success(
                _seedState.Snapshot(),
                "Corpus seeding started in the background. Poll api/eran-coach/corpus-status for progress."));
        }

        /// <summary>
        /// How many CLOVER corpus items are currently seeded for the coach, plus the state of the
        /// background seeding run (Idle | Queued | Running | Completed | Failed).
        /// </summary>
        [ApiExplorerSettings(IgnoreApi = true)] // hidden from Swagger for now; still callable
        [HttpGet("corpus-status")]
        public async Task<IActionResult> CorpusStatus(CancellationToken ct)
        {
            var status = _seedState.Snapshot();
            status.Seeded = await _seed.CountAsync(ct);
            return Ok(ApiResponseHandler.Success(status, "Corpus status."));
        }

        // Resolves the owner from the token; false when unauthenticated (unreachable behind the
        // TokenAuthentication middleware, but guarded so the endpoints are self-contained).
        private bool TryGetUserId(out int userId)
        {
            var id = _tokenReader.GetUserIdFromToken();
            userId = id ?? 0;
            return id is int value && value > 0;
        }

        // The team a request is about; null for none (missing or 0).
        private static long? TeamOf(uint? unitId) => unitId is uint u && u > 0 ? u : null;
    }
}
