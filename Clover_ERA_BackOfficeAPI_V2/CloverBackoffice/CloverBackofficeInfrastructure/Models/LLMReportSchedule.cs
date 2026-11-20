using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    [Table("LLMReportSchedules", Schema = "public")]
    public class LLMReportSchedule
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public uint ProfileId { get; set; }

        public uint UnitId { get; set; }

        public bool IsScheduleEnabled { get; set; } = false;

        // 0 means "no schedule" — only used when IsScheduleEnabled = false.
        // When IsScheduleEnabled = true the API enforces >= 1.
        public int ReportGenerationDays { get; set; } = 0;

        public bool IsDel { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public int? CreatedBy { get; set; }

        public int? UpdatedBy { get; set; }

        [ForeignKey(nameof(ProfileId))]
        public virtual Profile? Profile { get; set; }

        [ForeignKey(nameof(UnitId))]
        public virtual BusinessUnit? BusinessUnit { get; set; }
    }
}
