namespace CloverBackoffice.Infrastructure.DTO
{
    // Fallback cadence for units the admin never configured.
    //
    // The per-unit schedule in LLMReportSchedules stays authoritative: any unit
    // with IsScheduleEnabled = true keeps running on its own
    // ReportGenerationDays. This section only covers the units that have no
    // enabled schedule row at all — they generate a report every IntervalDays
    // instead of being ignored by the scheduler.
    //
    // Bound to "ReportGenerationFallback" in appsettings.json. The wake-up
    // cadence is separate and still lives under "JobSchedules:ReportScheduler";
    // the fallback can only be evaluated as often as the job wakes up.
    public class ReportGenerationFallbackOptions
    {
        // false = only explicitly scheduled units generate reports (behaviour
        // before this fallback existed).
        public bool Enabled { get; set; } = true;

        // Days between automatic reports for unscheduled units. Two weeks.
        // Values <= 0 are treated as "misconfigured" and disable the fallback
        // rather than generating a report on every tick.
        public int IntervalDays { get; set; } = 14;
    }
}
