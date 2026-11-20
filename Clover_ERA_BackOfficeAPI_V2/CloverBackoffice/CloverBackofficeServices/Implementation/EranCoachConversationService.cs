using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;

namespace CloverBackoffice.Services.Implementation
{
    public class EranCoachConversationService : IEranCoachConversationService
    {
        private const int TitleMaxLength = 200;
        private const int TitleFromMessageMaxLength = 60;
        private const int ModelUsedMaxLength = 120;

        private readonly IEranCoachConversationRepository _repo;
        private readonly IWebHostEnvironment? _env;

        public EranCoachConversationService(IEranCoachConversationRepository repo, IWebHostEnvironment? env = null)
        {
            _repo = repo;
            _env = env;
        }

        public async Task<List<EranCoachConversationDto>> ListAsync(int userId, long? businessUnitId, CancellationToken ct = default)
        {
            var rows = await _repo.ListConversationsAsync(userId, businessUnitId, ct);
            return rows.Select(ToDto).ToList();
        }

        public async Task<EranCoachConversationDto> CreateAsync(int userId, int? profileId, int? userGroupId, long? businessUnitId, string? title, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var entity = new EranCoachConversation
            {
                UserId = userId,
                Title = NormalizeTitle(title),
                IsPinned = false,
                ProfileId = profileId,
                BusinessUnitId = businessUnitId,
                UserGroupId = userGroupId,
                CreatedAt = now,
                UpdatedAt = now
            };

            var saved = await _repo.CreateConversationAsync(entity, ct);
            return ToDto(saved);
        }

        public async Task<List<EranCoachMessageDto>?> GetMessagesAsync(int userId, int conversationId, long? businessUnitId, CancellationToken ct = default)
        {
            var conversation = await _repo.GetConversationAsync(conversationId, userId, ct);
            if (conversation == null) return null;

            // Another team's conversation is reported as not found, never shown.
            if (businessUnitId != null && conversation.BusinessUnitId != businessUnitId) return null;

            var rows = await _repo.ListMessagesAsync(conversationId, ct);
            var messages = rows.Select(ToDto).ToList();

            // Linkify on the way out, not on the way in: the stored transcript stays plain text so
            // replaying it as model context never feeds anchor markup back to the coach, while a
            // reloaded conversation still shows the same clickable worksheets as the live stream.
            var worksheetFolder = WorksheetFolderPath();
            foreach (var message in messages)
            {
                if (string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                    message.Content = WorksheetMatcher.LinkifyWorksheetReferences(message.Content, worksheetFolder);
            }

            return messages;
        }

        // Folder api/rag/get-worksheet-file serves from; null when there is no web root.
        private string? WorksheetFolderPath()
        {
            var webRoot = _env?.WebRootPath;
            return string.IsNullOrWhiteSpace(webRoot) ? null : Path.Combine(webRoot, "WorkSheetFile");
        }

        public async Task<EranCoachConversationDto?> UpdateAsync(int userId, int conversationId, string? title, bool? isPinned, CancellationToken ct = default)
        {
            var updated = await _repo.UpdateConversationAsync(conversationId, userId, NormalizeTitle(title), isPinned, ct);
            return updated == null ? null : ToDto(updated);
        }

        public Task<bool> DeleteAsync(int userId, int conversationId, CancellationToken ct = default)
            => _repo.SoftDeleteConversationAsync(conversationId, userId, ct);

        public async Task<EranCoachTurnContextDto?> BeginTurnAsync(int userId, int? profileId, int? userGroupId, long? businessUnitId, int? conversationId, string userMessage, CancellationToken ct = default)
        {
            int resolvedId;
            var created = false;

            if (conversationId is int existingId && existingId > 0)
            {
                var conversation = await _repo.GetConversationAsync(existingId, userId, ct);
                if (conversation == null) return null; // not found or not owned

                // A conversation stays on the team it was started for. Continuing it with another
                // team selected would mix the two teams' chats, so nothing is stored.
                if (conversation.BusinessUnitId != businessUnitId)
                    return new EranCoachTurnContextDto { ConversationId = conversation.Id, TeamMismatch = true };

                resolvedId = conversation.Id;
            }
            else
            {
                var dto = await CreateAsync(userId, profileId, userGroupId, businessUnitId, TitleFromMessage(userMessage), ct);
                resolvedId = dto.Id;
                created = true;
            }

            var now = DateTime.UtcNow;
            var message = await _repo.AddMessageAsync(new EranCoachMessage
            {
                ConversationId = resolvedId,
                Role = "user",
                Content = userMessage,
                CreatedAt = now
            }, ct);

            await _repo.TouchConversationAsync(resolvedId, now, ct);

            return new EranCoachTurnContextDto
            {
                ConversationId = resolvedId,
                UserMessageId = message.Id,
                Created = created
            };
        }

        public async Task<long> CompleteTurnAsync(int conversationId, string assistantContent, string? modelUsed, int? promptTokens, int? completionTokens, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var message = await _repo.AddMessageAsync(new EranCoachMessage
            {
                ConversationId = conversationId,
                Role = "assistant",
                Content = assistantContent,
                ModelUsed = Truncate(modelUsed, ModelUsedMaxLength),
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                CreatedAt = now
            }, ct);

            await _repo.TouchConversationAsync(conversationId, now, ct);
            return message.Id;
        }

        // ── mapping + helpers ──

        private static EranCoachConversationDto ToDto(EranCoachConversation c) => new()
        {
            Id = c.Id,
            Title = c.Title,
            UnitId = c.BusinessUnitId,
            IsPinned = c.IsPinned,
            LastMessageAt = c.LastMessageAt,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
        };

        private static EranCoachMessageDto ToDto(EranCoachMessage m) => new()
        {
            Id = m.Id,
            ConversationId = m.ConversationId,
            Role = m.Role,
            Content = m.Content,
            ModelUsed = m.ModelUsed,
            CreatedAt = m.CreatedAt
        };

        private static string? NormalizeTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;
            return Truncate(title.Trim(), TitleMaxLength);
        }

        // A first-message-derived title: first line, trimmed, capped, with an ellipsis if cut.
        private static string TitleFromMessage(string message)
        {
            var text = (message ?? "").Trim();
            var newline = text.IndexOfAny(new[] { '\r', '\n' });
            if (newline >= 0) text = text.Substring(0, newline).Trim();

            if (text.Length == 0) return "New conversation";
            return text.Length <= TitleFromMessageMaxLength
                ? text
                : text.Substring(0, TitleFromMessageMaxLength).TrimEnd() + "…";
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }
}
