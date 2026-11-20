using Pgvector;
using System.ComponentModel.DataAnnotations;

namespace CloverBackoffice.Infrastructure.Models
{
    public class DocumentEmbedding
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(500)]
        public string Title { get; set; }

        public string Content { get; set; }

        public Vector Embedding { get; set; }

        [MaxLength(100)]
        public string SourceType { get; set; }

        [MaxLength(500)]
        public string? FileName { get; set; }

        public int? ChunkIndex { get; set; }

        [MaxLength(200)]
        public string? EmbeddingType { get; set; }

        public int ProcessId { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }
}
