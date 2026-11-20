using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Repository.Interfaces
{
    /// <summary>
    /// Owner-scoped persistence for EranCoach conversations and their message transcripts.
    /// Conversation reads/writes are filtered by UserId; messages are reached only through a
    /// conversation the caller owns. Soft-delete via IsDel.
    /// </summary>
    public interface IEranCoachConversationRepository
    {
        Task<EranCoachConversation> CreateConversationAsync(EranCoachConversation conversation, CancellationToken ct = default);

        /// <summary>The conversation if it exists, is not deleted, and belongs to <paramref name="userId"/>; else null.</summary>
        Task<EranCoachConversation?> GetConversationAsync(int id, int userId, CancellationToken ct = default);

        /// <summary>
        /// The owner's non-deleted conversations, pinned first, then most-recent first. With a
        /// team, only that team's conversations.
        /// </summary>
        Task<List<EranCoachConversation>> ListConversationsAsync(int userId, long? businessUnitId, CancellationToken ct = default);

        /// <summary>Applies title/pin changes to an owned conversation; returns the updated row or null if not found.</summary>
        Task<EranCoachConversation?> UpdateConversationAsync(int id, int userId, string? title, bool? isPinned, CancellationToken ct = default);

        /// <summary>Soft-deletes an owned conversation. Returns false if not found.</summary>
        Task<bool> SoftDeleteConversationAsync(int id, int userId, CancellationToken ct = default);

        /// <summary>Bumps LastMessageAt/UpdatedAt after a turn is persisted (no ownership check — internal).</summary>
        Task TouchConversationAsync(int id, DateTime whenUtc, CancellationToken ct = default);

        Task<EranCoachMessage> AddMessageAsync(EranCoachMessage message, CancellationToken ct = default);

        /// <summary>A conversation's non-deleted messages in chronological order.</summary>
        Task<List<EranCoachMessage>> ListMessagesAsync(int conversationId, CancellationToken ct = default);
    }
}
