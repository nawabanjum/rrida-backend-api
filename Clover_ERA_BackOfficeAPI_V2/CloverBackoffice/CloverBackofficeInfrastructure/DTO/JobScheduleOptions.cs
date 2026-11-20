namespace CloverBackoffice.Infrastructure.DTO
{
    // Shared schedule shape for every background job in this project. Bound to
    // a per-job section under "JobSchedules" via named IOptionsMonitor.
    //
    // Mode = "Daily"    → service fires once at each entry in DailyTimes (UTC
    //                     "HH:mm" strings). Default is 00:00 + 12:00, i.e.
    //                     twice a day at midnight and noon UTC.
    // Mode = "Interval" → service fires every IntervalMinutes. Use for local
    //                     testing or short cadences (1 min, 5 min, 4 h, etc.).
    //                     Fractional minutes are allowed (e.g. 0.5 = 30 s).
    public class JobScheduleOptions
    {
        // Master toggle. Setting false skips the job entirely (the hosted
        // service exits cleanly during startup) without commenting code out.
        public bool Enabled { get; set; } = true;

        // "Daily" or "Interval". Case-insensitive.
        public string Mode { get; set; } = "Daily";

        // UTC clock times in "HH:mm" or "HH:mm:ss" format. Order doesn't
        // matter — the helper sorts and picks the next future entry.
        public string[] DailyTimes { get; set; } = new[] { "00:00", "12:00" };

        // How often the job fires when Mode=Interval. Minutes (double, so
        // 0.0167 = 1 second is allowed). The helper clamps to a tiny minimum
        // so a misconfigured zero can't hot-loop the thread.
        public double IntervalMinutes { get; set; } = 720;

        // Delay before the FIRST run after process start. Lets the app finish
        // wiring DI / DB pool / queues before the job touches anything.
        public int StartupDelaySeconds { get; set; } = 20;
    }
}
