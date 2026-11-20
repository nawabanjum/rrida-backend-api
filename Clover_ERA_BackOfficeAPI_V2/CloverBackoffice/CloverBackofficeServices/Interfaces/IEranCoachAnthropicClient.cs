using System.Collections.Generic;
using System.Threading;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Low-level streaming call to an LLM Messages/Chat-Completions endpoint for EranCoach.
    /// Streams a single assistant turn as <see cref="CoachStreamEvent"/>s (text deltas,
    /// completed tool_use blocks, and the turn's stop reason). Its own client so the coach
    /// stays separate from the existing AI services; credentials still come from the shared
    /// ModelConfiguration row (Anthropic vs Azure-OpenAI is decided by the endpoint URL).
    /// The chat service owns the multi-turn tool loop and calls this once per turn.
    /// </summary>
    public interface IEranCoachAnthropicClient
    {
        IAsyncEnumerable<CoachStreamEvent> StreamTurnAsync(
            IReadOnlyList<CoachSystemBlock> systemBlocks,
            IReadOnlyList<CoachMessage> messages,
            IReadOnlyList<object>? tools,
            ModelConfiguration model,
            CancellationToken ct = default);

        /// <summary>
        /// One-shot, non-streaming completion. Used for the cheap post-conversation memory
        /// summary pass. Returns the plain text answer.
        /// </summary>
        /// <param name="failWhenCutOff">
        /// Throw when the answer did not finish — it hit the token limit, or the model
        /// declined — rather than returning the part that came back. The engine narrator
        /// sets it, as the MVP's Eran client does (src/eran/client.js callModel).
        /// </param>
        Task<string> CompleteAsync(
            string systemPrompt,
            string userContent,
            ModelConfiguration model,
            int maxTokens,
            CancellationToken ct = default,
            bool failWhenCutOff = false);
    }
}
