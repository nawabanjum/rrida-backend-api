using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    /// <summary>
    /// One generated "Snapshot Report" for a business unit.
    /// Holds both the run lifecycle (Status / timestamps / error) and the parsed
    /// Claude output, so a single row is the run record and the result store.
    /// Mirrors the WorkbenchRunStatus + LLMReportSchedule conventions.
    /// </summary>
    [Table("CloveraPersonalSnapshots", Schema = "public")]
    public class CloveraPersonalSnapshot
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        // ── Run lifecycle ──
        [MaxLength(20)]
        public string Status { get; set; } = "Queued"; // Queued, Running, Completed, Failed

        // ── Source scope (the unit + window the snapshot was built from) ──
        public uint ProfileId { get; set; }
        public uint UnitId { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }

        [MaxLength(120)]
        public string? ModelUsed { get; set; }

        // ── Parsed snapshot output ──
        [Column(TypeName = "text")] public string? Headline { get; set; }
        [Column(TypeName = "text")] public string? SummaryBody { get; set; }
        [Column(TypeName = "text")] public string? ProductivityObservation { get; set; }
        [Column(TypeName = "text")] public string? PillarFraming { get; set; }

        [MaxLength(1)]  public string? HighlightedPillarCode { get; set; }   // C/L/O/V/E/R
        [MaxLength(40)] public string? HighlightedPillarLabel { get; set; }
        public int? HighlightedSmileyScore { get; set; }                     // 1..4
        [MaxLength(40)] public string? HighlightReason { get; set; }         // lowest/highest/...

        [Column(TypeName = "text")] public string? NudgeTitle { get; set; }
        [Column(TypeName = "text")] public string? NudgeRationale { get; set; }
        [Column(TypeName = "text")] public string? NudgeAction { get; set; }
        [MaxLength(20)] public string? NudgeIntent { get; set; }             // challenge/momentum/support

        [Column(TypeName = "text")] public string? DrawingFromJson { get; set; }
        [Column(TypeName = "text")] public string? RawResponseJson { get; set; } // verbatim Claude body

        // ── Telemetry ──
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens { get; set; }
        public double GenerationSeconds { get; set; }

        [Column(TypeName = "text")] public string? ErrorMessage { get; set; }

        // ── Audit / soft-delete (house convention) ──
        public bool IsDel { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public int? CreatedBy { get; set; }
    }
}
