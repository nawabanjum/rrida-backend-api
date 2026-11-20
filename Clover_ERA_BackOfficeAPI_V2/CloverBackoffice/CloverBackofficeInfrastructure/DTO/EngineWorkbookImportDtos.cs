using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    /// <summary>
    /// Options for POST api/rrida/engine/import-workbook. The file itself travels as the
    /// multipart field "file"; these are the other form fields.
    /// </summary>
    public class EngineWorkbookImportOptionsDto
    {
        /// <summary>The company the teams belong to. A team is matched by name inside it.</summary>
        public uint ProfileId { get; set; }

        /// <summary>
        /// Moves each team's Day 1 forward or back by whole weeks so its last asked day lands
        /// on or just before today. Off keeps the sheet's own "First day of testing" date.
        /// The weekday never changes, so the engine sees exactly the same check-ins.
        /// </summary>
        public bool AlignToToday { get; set; }

        /// <summary>
        /// A team that already holds engine data is skipped unless this is set. Set, its
        /// engine data and the rows an earlier import wrote are cleared and imported again.
        /// </summary>
        public bool ReplaceExisting { get; set; }

        /// <summary>Plays the manager's confirmations so cycles ladder and close (seeder defaults).</summary>
        public bool AutoConfirm { get; set; } = true;
    }

    /// <summary>What one upload did, summed over every team sheet.</summary>
    public class EngineWorkbookImportResultDto
    {
        public bool Success { get; set; }
        public string FileName { get; set; } = string.Empty;

        public int SheetsRead { get; set; }
        public int TeamsCreated { get; set; }
        public int TeamsUpdated { get; set; }
        public int TeamsSkipped { get; set; }
        public int TeamsFailed { get; set; }

        public EngineWorkbookTotalsDto Totals { get; set; } = new();

        public List<EngineWorkbookTeamResultDto> Teams { get; set; } = new();
    }

    public class EngineWorkbookTotalsDto
    {
        public int RowsRead { get; set; }
        public int Members { get; set; }
        public int IndividualAnswers { get; set; }
        public int TeamAnswers { get; set; }
        public int Chips { get; set; }
        public int Confirmations { get; set; }
        public int DailyFeedback { get; set; }
        public int TeamDailyFeedback { get; set; }
        public int FeedbackReasons { get; set; }
        public int CustomQuestions { get; set; }
        public int CustomAnswers { get; set; }
        public int EngineStateDays { get; set; }
        public int Cycles { get; set; }
        public int Warnings { get; set; }
    }

    /// <summary>One team sheet: what was read, what was written, what the engine made of it.</summary>
    public class EngineWorkbookTeamResultDto
    {
        public string Sheet { get; set; } = string.Empty;
        public string TeamName { get; set; } = string.Empty;
        public uint? BusinessUnitId { get; set; }

        /// <summary>created, updated, skipped or failed.</summary>
        public string Status { get; set; } = string.Empty;
        public string? Message { get; set; }

        public string WorkingWeek { get; set; } = string.Empty;
        public DateOnly? SheetStartDate { get; set; }
        public DateOnly? StartDate { get; set; }
        public DateOnly? ThroughDate { get; set; }
        public int AskedDays { get; set; }

        public int RowsRead { get; set; }
        public int Members { get; set; }
        public int IndividualAnswers { get; set; }
        public int TeamAnswers { get; set; }
        public int Chips { get; set; }
        public int Confirmations { get; set; }

        /// <summary>Team Health's own tables (screen 1 and screen 2 of the check-in).</summary>
        public int DailyFeedback { get; set; }
        public int TeamDailyFeedback { get; set; }
        public int FeedbackReasons { get; set; }

        public int CustomQuestions { get; set; }
        public int CustomAnswers { get; set; }

        public int EngineStateDays { get; set; }
        public Dictionary<string, int> CycleOutcomes { get; set; } = new();

        public int WarningCount { get; set; }

        /// <summary>The first warnings, by row. The count above is the full number.</summary>
        public List<string> Warnings { get; set; } = new();
    }
}
