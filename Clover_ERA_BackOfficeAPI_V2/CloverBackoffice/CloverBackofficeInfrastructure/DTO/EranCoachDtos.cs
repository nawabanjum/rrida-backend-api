using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    // ── EranCoach — manager-coach chat (ported from the ERAn Next.js reference). ──
    // Phase 1: a stateless streaming chat. The client sends the full conversation
    // each turn (mirrors ERAn's /api/chat), the server streams the coach's reply.

    /// <summary>One chat turn, as sent from the client. Role is "user" or "assistant".</summary>
    public class EranCoachChatMessageDto
    {
        public string Role { get; set; } = "user";
        public string Content { get; set; } = "";
    }

    /// <summary>Request body for POST api/eran-coach/chat. Full history, last turn must be the user.</summary>
    public class EranCoachChatRequestDto
    {
        public List<EranCoachChatMessageDto> Messages { get; set; } = new();

        /// <summary>
        /// Optional business-unit id. When supplied, the coach is grounded in that unit's
        /// latest CLOVER-EQ engagement report (server-side; never sent to the browser).
        /// </summary>
        public uint? UnitId { get; set; }

        /// <summary>
        /// Existing conversation to append this turn to. When null, POST /chat creates a new
        /// conversation and returns its id in the X-Conversation-Id response header.
        /// </summary>
        public int? ConversationId { get; set; }

        /// <summary>
        /// True on the Portfolio View: the coach is grounded in the Portfolio Manager's teams
        /// (IPortfolioViewService) instead of one team, and UnitId is ignored.
        /// </summary>
        public bool Portfolio { get; set; }

        /// <summary>
        /// Portfolio View only: the selected manager; null or 0 = All Managers. Must be in the
        /// caller's portfolio, or the selected profile for a RRIDA Admin, otherwise the turn
        /// is refused (403).
        /// </summary>
        public int? ManagerId { get; set; }

        /// <summary>
        /// Portfolio View only: the profile picked in the Profile dropdown. Required for a
        /// RRIDA Admin; a Portfolio Manager leaves it out.
        /// </summary>
        public uint? ProfileId { get; set; }
    }

    /// <summary>
    /// Progress of the corpus seeding run. Seeding embeds the whole CLOVER bundle, which takes
    /// far longer than a request may stay open, so POST /seed-corpus only queues the run and the
    /// caller polls GET /corpus-status for this.
    /// </summary>
    public class EranCoachSeedStatusDto
    {
        /// <summary>Idle | Queued | Running | Completed | Failed.</summary>
        public string Status { get; set; } = "Idle";

        /// <summary>True while a run is queued or in flight — a second POST is rejected.</summary>
        public bool IsRunning { get; set; }

        public System.DateTime? QueuedAt { get; set; }
        public System.DateTime? StartedAt { get; set; }
        public System.DateTime? CompletedAt { get; set; }

        /// <summary>Rows written by the last completed run.</summary>
        public int? SeededCount { get; set; }

        /// <summary>Failure message from the last run, when Status is Failed.</summary>
        public string? Error { get; set; }

        /// <summary>Rows currently in the coach namespace (live count, not run state).</summary>
        public int Seeded { get; set; }
    }
}
