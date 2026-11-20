using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    public class InsightPublishLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public long ReportId { get; set; }

        public int RunId { get; set; }

        public int? UserId { get; set; }

        public DateTime DateFrom { get; set; }

        public DateTime DateTo { get; set; }

        [MaxLength(10)]
        public string OperationType { get; set; } = "Insert";

        [Column(TypeName = "text")]
        public string TopThemes { get; set; } = "[]";

        [Column(TypeName = "text")]
        public string TopActions { get; set; } = "[]";

        [Column(TypeName = "text")]
        public string TopRecommendations { get; set; } = "[]";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
