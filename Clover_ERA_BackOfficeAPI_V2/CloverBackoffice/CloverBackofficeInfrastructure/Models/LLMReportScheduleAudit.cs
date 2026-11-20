using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    [Table("LLMReportScheduleAudits", Schema = "public")]
    public class LLMReportScheduleAudit
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AuditId { get; set; }

        // FK back to the schedule row this audit row describes.
        public int ScheduleId { get; set; }

        public uint ProfileId { get; set; }

        public uint UnitId { get; set; }

        public bool IsScheduleEnabled { get; set; }

        public int ReportGenerationDays { get; set; }

        public bool IsDel { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public int? CreatedBy { get; set; }

        public int? UpdatedBy { get; set; }

        public DateTime LoggedAt { get; set; } = DateTime.UtcNow;

        public int VersionNumber { get; set; } = 1;

        [MaxLength(20)]
        public string OperationType { get; set; } = string.Empty;
    }
}
