using System.ComponentModel.DataAnnotations;

namespace CloverBackoffice.Infrastructure.DTO
{
    // GET response — one row per unit (or one row total if UnitId filter supplied).
    public class LLMReportScheduleDto
    {
        public uint ProfileId { get; set; }
        public string CompanyName { get; set; } = string.Empty;

        public uint UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;

        public bool IsScheduleEnabled { get; set; }

        public int ReportGenerationDays { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    // PUT/UPDATE payload — caller targets one (ProfileId, UnitId) pair per request.
    public class UpdateLLMReportScheduleDto
    {
        [Required]
        public uint ProfileId { get; set; }

        [Required]
        public uint UnitId { get; set; }

        // Defaults to false at the DTO and DB level. When false, scheduler skips this unit.
        public bool IsScheduleEnabled { get; set; } = false;

        // Per spec:
        //   - IsScheduleEnabled = false → server forces this to 0; any value sent is ignored.
        //   - IsScheduleEnabled = true  → must be >= 1; 0/missing is rejected.
        // No silent default-to-14 anywhere on the stack.
        [Range(0, int.MaxValue)]
        public int ReportGenerationDays { get; set; } = 0;
    }
}
