using System;

namespace CloverBackoffice.Infrastructure.DTO
{
    // ── EranCoach — persistent conversations, messages, and central model config (Req 1 & 2). ──

    /// <summary>A conversation as returned to the owner (list rows / detail).</summary>
    public class EranCoachConversationDto
    {
        public int Id { get; set; }
        public string? Title { get; set; }

        /// <summary>The team this conversation belongs to; null for one started with no team.</summary>
        public long? UnitId { get; set; }

        public bool IsPinned { get; set; }
        public DateTime? LastMessageAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>One persisted turn returned to the client.</summary>
    public class EranCoachMessageDto
    {
        public long Id { get; set; }
        public int ConversationId { get; set; }
        public string Role { get; set; } = "user";
        public string Content { get; set; } = "";
        public string? ModelUsed { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>Body for POST /conversations. Title optional (defaults from the first message).</summary>
    public class CreateEranCoachConversationDto
    {
        public string? Title { get; set; }

        /// <summary>Optional business-unit id, captured on the conversation for later grounding.</summary>
        public uint? UnitId { get; set; }
    }

    /// <summary>Body for PATCH /conversations/{id}. A null field means "leave unchanged".</summary>
    public class UpdateEranCoachConversationDto
    {
        public string? Title { get; set; }
        public bool? IsPinned { get; set; }
    }

    /// <summary>
    /// Result of opening a chat turn: the conversation the turn belongs to (created if the
    /// request had no id) and the id of the just-persisted user message.
    /// </summary>
    public class EranCoachTurnContextDto
    {
        public int ConversationId { get; set; }
        public long UserMessageId { get; set; }
        public bool Created { get; set; }

        /// <summary>True when the conversation belongs to another team; the turn was not stored.</summary>
        public bool TeamMismatch { get; set; }
    }

    // ── Req 1 — central model config (Option A: reuse ModelConfiguration, type 1). ──

    /// <summary>The coach's active model plus its generation settings (GET /model-config).</summary>
    public class EranCoachModelConfigDto
    {
        public int Id { get; set; }
        public string ModelName { get; set; } = "";
        public string? FriendlyName { get; set; }
        public string? Provider { get; set; }
        public double Temperature { get; set; }
        public double TopP { get; set; }
        public int MaxTokens { get; set; }
        public bool IsSelected { get; set; }
        public bool IsDefault { get; set; }
    }

    /// <summary>A selectable coach model option (GET /models).</summary>
    public class EranCoachModelOptionDto
    {
        public int Id { get; set; }
        public string ModelName { get; set; } = "";
        public string? FriendlyName { get; set; }
        public string? Provider { get; set; }
        public bool IsSelected { get; set; }
        public bool IsDefault { get; set; }
    }

    /// <summary>Body for PUT /model-config. Selects the active coach model + optional settings.</summary>
    public class UpdateEranCoachModelConfigDto
    {
        public int ModelConfigurationId { get; set; }
        public double? Temperature { get; set; }
        public double? TopP { get; set; }
        public int? MaxTokens { get; set; }
    }
}
