using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    /// <summary>
    /// One persisted turn in an EranCoach conversation. Role is "user" or "assistant".
    /// PromptTokens / CompletionTokens / ModelUsed are optional telemetry. Soft-deleted via IsDel.
    /// </summary>
    [Table("EranCoachMessages", Schema = "public")]
    public class EranCoachMessage
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        /// <summary>Owning conversation. Maps to EranCoachConversations.Id.</summary>
        public int ConversationId { get; set; }

        [MaxLength(20)]
        public string Role { get; set; } = "user";

        [Column(TypeName = "text")]
        public string Content { get; set; } = "";

        public int? PromptTokens { get; set; }
        public int? CompletionTokens { get; set; }

        [MaxLength(120)]
        public string? ModelUsed { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDel { get; set; }
    }
}
