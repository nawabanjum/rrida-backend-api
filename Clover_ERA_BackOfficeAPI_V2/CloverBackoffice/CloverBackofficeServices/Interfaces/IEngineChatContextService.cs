namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>One turn of the chat's history as the model is given it.</summary>
    public sealed record EngineChatTurn(string Role, string Content);

    /// <summary>
    /// The engine block for the chat's system prompt, today's engine day, and the thread's
    /// recent turns stamped with their day (null when the user is not known).
    /// </summary>
    public sealed record EngineChatContext(string SystemBlock, int Day, List<EngineChatTurn>? History)
    {
        /// <summary>
        /// "Where the team is today": the delivered action, worksheet and worked-if the chat was
        /// given. Eran's guard lets a reply echo this wording, and nothing else of the library.
        /// </summary>
        public string? Situation { get; init; }

        /// <summary>This week's action as delivered, for the chat's fallback card. Null when nothing runs.</summary>
        public string? ActionText { get; init; }

        /// <summary>
        /// The titles and descriptions of the manager's own open actions (api/actions) as written,
        /// which the chat was given. Eran's guard lets a reply echo this wording too.
        /// </summary>
        public string? ManagerActionsText { get; init; }
    }

    public interface IEngineChatContextService
    {
        /// <summary>
        /// What Eran Chat is told about a team on the deterministic layer. Null when the team is
        /// not on it, in which case the chat runs exactly as it always has.
        /// </summary>
        Task<EngineChatContext?> BuildAsync(uint unitId, int? userId, string currentQuestion, CancellationToken ct = default);
    }
}
