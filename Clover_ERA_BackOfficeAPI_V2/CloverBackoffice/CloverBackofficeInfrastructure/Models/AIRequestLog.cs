using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    public class AIRequestLog
    {
        [Key]
        public long Id { get; set; }

        public string PromptName { get; set; } = "";
        public string PromptVersion { get; set; } = "";
        public string ModelRequested { get; set; } = "";
        public string ModelUsed { get; set; } = "";
        public bool FallbackUsed { get; set; }

        public long LatencyMs { get; set; }
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public decimal EstimatedCost { get; set; }

        public bool IsJsonValid { get; set; }
        public int AssertionsPassed { get; set; }
        public int AssertionsTotal { get; set; }

        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }

        [Column(TypeName = "text")]
        public string? ResponsePayload { get; set; }

        [MaxLength(200)]
        public string? EvalId { get; set; }

        public uint? BusinessUnitId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
