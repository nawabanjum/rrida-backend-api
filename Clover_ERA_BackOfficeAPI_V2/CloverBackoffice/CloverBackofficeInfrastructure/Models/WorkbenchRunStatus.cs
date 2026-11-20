using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    // Who triggered a run. Carried inside WorkbenchRunStatus.ResultJson as a
    // "Source" field rather than as a column of its own — the run table needs no
    // schema change for this, and the JSON wrapper is written by the same code
    // for both triggers.
    //
    // Absent (or null ResultJson) means Workbench: that is what every run was
    // before the scheduler started raising them, and it is what an operator's
    // run still is while it sits Queued.
    public static class WorkbenchRunSources
    {
        public const string Workbench = "Workbench";
        public const string Scheduler = "Scheduler";

        // Substring test used by the workbench list endpoints to leave scheduled
        // runs out. Matches the serialized "Source":"Scheduler" pair written by
        // WorkbenchRunExecutor and by the scheduler's placeholder.
        public const string SchedulerJsonMarker = "\"Source\":\"Scheduler\"";

        // Written to ResultJson when the scheduler creates the run row, so the
        // run is identifiable before the engine has produced anything — a
        // scheduled run that fails or never completes stays out of the
        // workbench's screens too.
        public const string SchedulerPlaceholderJson = "{\"Source\":\"Scheduler\"}";
    }

    public class WorkbenchRunStatus
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int RunId { get; set; }

        [MaxLength(20)]
        public string Status { get; set; } = "Queued";

        [Column(TypeName = "text")]
        public string? ResultJson { get; set; }

        [Column(TypeName = "text")]
        public string? ErrorMessage { get; set; }

        [Column(TypeName = "text")]
        public string? DetailedLogs { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
    }
}
