using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Implementation
{
    public class EranCoachConversationRepository : IEranCoachConversationRepository
    {
        private readonly ApplicationDbContext _context;

        public EranCoachConversationRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<EranCoachConversation> CreateConversationAsync(EranCoachConversation conversation, CancellationToken ct = default)
        {
            await _context.EranCoachConversations.AddAsync(conversation, ct);
            await _context.SaveChangesAsync(ct);
            return conversation;
        }

        public async Task<EranCoachConversation?> GetConversationAsync(int id, int userId, CancellationToken ct = default)
        {
            return await _context.EranCoachConversations
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId && !c.IsDel, ct);
        }

        public async Task<List<EranCoachConversation>> ListConversationsAsync(int userId, long? businessUnitId, CancellationToken ct = default)
        {
            return await _context.EranCoachConversations
                .Where(c => c.UserId == userId && !c.IsDel)
                .Where(c => businessUnitId == null || c.BusinessUnitId == businessUnitId)
                .OrderByDescending(c => c.IsPinned)
                .ThenByDescending(c => c.LastMessageAt ?? c.CreatedAt)
                .ToListAsync(ct);
        }

        public async Task<EranCoachConversation?> UpdateConversationAsync(int id, int userId, string? title, bool? isPinned, CancellationToken ct = default)
        {
            var conversation = await GetConversationAsync(id, userId, ct);
            if (conversation == null) return null;

            if (title != null) conversation.Title = title;
            if (isPinned.HasValue) conversation.IsPinned = isPinned.Value;
            conversation.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);
            return conversation;
        }

        public async Task<bool> SoftDeleteConversationAsync(int id, int userId, CancellationToken ct = default)
        {
            var conversation = await GetConversationAsync(id, userId, ct);
            if (conversation == null) return false;

            conversation.IsDel = true;
            conversation.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
            return true;
        }

        public async Task TouchConversationAsync(int id, DateTime whenUtc, CancellationToken ct = default)
        {
            var conversation = await _context.EranCoachConversations
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsDel, ct);
            if (conversation == null) return;

            conversation.LastMessageAt = whenUtc;
            conversation.UpdatedAt = whenUtc;
            await _context.SaveChangesAsync(ct);
        }

        public async Task<EranCoachMessage> AddMessageAsync(EranCoachMessage message, CancellationToken ct = default)
        {
            await _context.EranCoachMessages.AddAsync(message, ct);
            await _context.SaveChangesAsync(ct);
            return message;
        }

        public async Task<List<EranCoachMessage>> ListMessagesAsync(int conversationId, CancellationToken ct = default)
        {
            return await _context.EranCoachMessages
                .Where(m => m.ConversationId == conversationId && !m.IsDel)
                .OrderBy(m => m.CreatedAt)
                .ThenBy(m => m.Id)
                .ToListAsync(ct);
        }
    }
}
