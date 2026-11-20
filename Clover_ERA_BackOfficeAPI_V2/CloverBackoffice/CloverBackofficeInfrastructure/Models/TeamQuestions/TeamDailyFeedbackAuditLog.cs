using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models.TeamQuestions
{
    /// <summary>
    /// Column-for-column mirror of DailyFeedbackAuditLog, for TeamDailyFeedback.
    /// The only differences are the two content columns, which name the team
    /// question and team reason instead of the legacy ones.
    ///
    /// Like the legacy table this is a row snapshot with no foreign key back to
    /// the source row, so the history outlives a hard delete.
    /// </summary>
    [Table("TeamDailyFeedbackAuditLogs", Schema = "public")]
    public class TeamDailyFeedbackAuditLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AuditId { get; set; }

        /// <summary>The TeamDailyFeedback row this snapshot describes.</summary>
        public long Id { get; set; }

        public DateTime SubmissionDateTime { get; set; }

        public long BusinessUnitId { get; set; }

        public int SentimentType { get; set; }

        public long TeamDailyQuestionId { get; set; }

        public long TeamDailyReasonId { get; set; }

        [MaxLength(896)]
        public string? FreeText { get; set; }

        public DateTime LoggedAt { get; set; } = DateTime.UtcNow;

        public int VersionNumber { get; set; } = 1;

        [MaxLength(20)]
        public string OperationType { get; set; } = string.Empty;

        public int? RecordCountSnapshot { get; set; }
    }
}
