using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Owner-scoped orchestration for EranCoach persistent chat: conversation CRUD, transcript
    /// reads, and the two hooks POST /chat uses to persist a turn (begin = create-or-resolve +
    /// store the user message; complete = store the assistant reply).
    /// </summary>
    public interface IEranCoachConversationService
    {
        /// <summary>The user's conversations; with a team, only that team's.</summary>
        Task<List<EranCoachConversationDto>> ListAsync(int userId, long? businessUnitId, CancellationToken ct = default);

        Task<EranCoachConversationDto> CreateAsync(int userId, int? profileId, int? userGroupId, long? businessUnitId, string? title, CancellationToken ct = default);

        /// <summary>
        /// The conversation's transcript, or null if it is not found / not owned by the user, or
        /// (when a team is given) belongs to a different team.
        /// </summary>
        Task<List<EranCoachMessageDto>?> GetMessagesAsync(int userId, int conversationId, long? businessUnitId, CancellationToken ct = default);

        /// <summary>Renames / pins an owned conversation; null if not found / not owned.</summary>
        Task<EranCoachConversationDto?> UpdateAsync(int userId, int conversationId, string? title, bool? isPinned, CancellationToken ct = default);

        /// <summary>Soft-deletes an owned conversation. False if not found / not owned.</summary>
        Task<bool> DeleteAsync(int userId, int conversationId, CancellationToken ct = default);

        /// <summary>
        /// Opens a chat turn: resolves the conversation (creating one when conversationId is null)
        /// and persists the incoming user message. Returns null when a supplied conversationId is
        /// not found / not owned by the user. When the conversation belongs to a different team than
        /// <paramref name="businessUnitId"/>, nothing is stored and TeamMismatch is set.
        /// </summary>
        Task<EranCoachTurnContextDto?> BeginTurnAsync(int userId, int? profileId, int? userGroupId, long? businessUnitId, int? conversationId, string userMessage, CancellationToken ct = default);

        /// <summary>Persists the assistant reply for a turn and bumps the conversation's recency. Returns the message id.</summary>
        Task<long> CompleteTurnAsync(int conversationId, string assistantContent, string? modelUsed, int? promptTokens, int? completionTokens, CancellationToken ct = default);
    }
}
