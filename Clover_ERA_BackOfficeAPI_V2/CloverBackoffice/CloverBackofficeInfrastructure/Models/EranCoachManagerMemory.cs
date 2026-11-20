using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    /// <summary>
    /// Rolling, per-manager coaching memory for EranCoach: a distilled summary of the people
    /// and situations discussed, advice given, and open follow-ups. Written after a conversation
    /// (a cheap summary pass), read back at the start of the next session and injected into the
    /// system prompt. One row per manager. Stores a summary, not raw transcripts.
    /// </summary>
    [Table("EranCoachManagerMemories", Schema = "public")]
    public class EranCoachManagerMemory
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public int ManagerUserId { get; set; }

        /// <summary>
        /// The team this memory is about. One row per manager per team, so what was said about
        /// one team never reaches the coach while another is selected. Null = no team selected.
        /// </summary>
        public long? BusinessUnitId { get; set; }

        [Column(TypeName = "text")]
        public string? SummaryText { get; set; }

        public int TurnCount { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDel { get; set; }
    }
}
