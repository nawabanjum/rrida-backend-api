using System.Collections.Generic;

namespace CloverBackoffice.Services.Interfaces
{
    // Provider-neutral message/content model for the EranCoach tool loop. The chat
    // service builds these; the client translates them to the Anthropic wire shape.
    // Mirrors ERAn's messages[] + content-block model (text / tool_use / tool_result).

    public abstract record CoachBlock;
    public sealed record CoachTextBlock(string Text) : CoachBlock;
    public sealed record CoachToolUseBlock(string Id, string Name, string InputJson) : CoachBlock;
    public sealed record CoachToolResultBlock(string ToolUseId, string Content) : CoachBlock;

    /// <summary>
    /// A thinking block exactly as the model produced it (text, possibly empty, and its signature).
    /// Newer Claude models (Opus 5.5 always thinks) need it sent back unchanged in a tool loop.
    /// </summary>
    public sealed record CoachThinkingBlock(string Thinking, string Signature) : CoachBlock;

    /// <summary>A redacted thinking block, sent back as it came.</summary>
    public sealed record CoachRedactedThinkingBlock(string Data) : CoachBlock;

    /// <summary>One conversation turn. Role is "user" or "assistant".</summary>
    public sealed record CoachMessage(string Role, IReadOnlyList<CoachBlock> Content);

    /// <summary>
    /// One system-prompt block with an optional cache TTL ("1h", "5m", or null for
    /// uncached). Stable blocks (persona+catalog, team context) are cached so they
    /// aren't re-billed every turn; the per-request date block stays uncached.
    /// </summary>
    public sealed record CoachSystemBlock(string Text, string? CacheTtl);

    // Events streamed out of a single assistant turn.
    public abstract record CoachStreamEvent;
    public sealed record CoachTextDelta(string Text) : CoachStreamEvent;
    public sealed record CoachToolUseReady(string Id, string Name, string InputJson) : CoachStreamEvent;
    public sealed record CoachTurnEnded(string? StopReason) : CoachStreamEvent;

    /// <summary>
    /// The whole assistant turn, every block in the order the model wrote it (thinking, text,
    /// tool_use). Sent just before <see cref="CoachTurnEnded"/> by the Anthropic path, so a tool loop
    /// can send the turn back unchanged, as the API requires of thinking blocks.
    /// </summary>
    public sealed record CoachAssistantContent(IReadOnlyList<CoachBlock> Blocks) : CoachStreamEvent;
}
