using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    /// <summary>
    /// A persistent EranCoach chat thread owned by a single user. Holds the metadata for the
    /// conversation (title, pin state, and a scope snapshot); the turns live in EranCoachMessage.
    /// Owner-scoped: every read/write is filtered by UserId. Soft-deleted via IsDel.
    /// </summary>
    [Table("EranCoachConversations", Schema = "public")]
    public class EranCoachConversation
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>Owner. Maps to Users.UserId. Every query is scoped by this.</summary>
        public int UserId { get; set; }

        [MaxLength(200)]
        public string? Title { get; set; }

        public bool IsPinned { get; set; }

        // Scope snapshot captured at creation time (best-effort; for later grouping/filtering).
        public long? ProfileId { get; set; }
        public long? BusinessUnitId { get; set; }
        public int? UserGroupId { get; set; }

        /// <summary>Timestamp of the most recent turn; drives the "recent first" ordering.</summary>
        public DateTime? LastMessageAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDel { get; set; }
    }
}
