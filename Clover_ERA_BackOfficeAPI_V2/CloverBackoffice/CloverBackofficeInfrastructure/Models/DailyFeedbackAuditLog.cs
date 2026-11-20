using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    [Table("DailyFeedbackAuditLogs", Schema = "public")]
    public class DailyFeedbackAuditLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AuditId { get; set; }

        public long Id { get; set; }

        public DateTime SubmissionDateTime { get; set; }

        public long BusinessUnitId { get; set; }

        public int SentimentType { get; set; }

        public long DailyQuestionId { get; set; }

        public long DailyReasonId { get; set; }

        [MaxLength(896)]
        public string? FreeText { get; set; }

        public DateTime LoggedAt { get; set; } = DateTime.UtcNow;

        public int VersionNumber { get; set; } = 1;

        [MaxLength(20)]
        public string OperationType { get; set; } = string.Empty;

        public int? RecordCountSnapshot { get; set; }
    }
}
