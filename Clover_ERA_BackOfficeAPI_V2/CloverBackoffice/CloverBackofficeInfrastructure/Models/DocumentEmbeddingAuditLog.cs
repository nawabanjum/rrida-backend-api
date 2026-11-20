using System.ComponentModel.DataAnnotations;

namespace CloverBackoffice.Infrastructure.Models
{
    public class DocumentEmbeddingAuditLog
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(50)]
        public string Action { get; set; }

        public int? ProcessId { get; set; }

        [MaxLength(500)]
        public string? FileName { get; set; }

        [MaxLength(200)]
        public string? EmbeddingType { get; set; }

        [MaxLength(100)]
        public string? SourceType { get; set; }

        public int? TotalChunks { get; set; }

        [MaxLength(1000)]
        public string? Details { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
