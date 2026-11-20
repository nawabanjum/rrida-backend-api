using System;
using System.Linq;
using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Infrastructure.Helpers
{
    // Computes the wait time before a background service's next iteration.
    // Centralizes the Daily / Interval decision so every hosted service has
    // identical scheduling semantics and the same defensive parsing.
    public static class JobScheduleHelper
    {
        // Names match the keys under "JobSchedules" in appsettings.json. Used
        // as the named-options key so multiple services can share the
        // JobScheduleOptions class without colliding.
        public static class JobNames
        {
            public const string SubscriptionLifecycle = "SubscriptionLifecycle";
            public const string BatchProcessing       = "BatchProcessing";

            // Azure OpenAI batch-job polling. Was driven by the external
            // CloverBackoffice.Jobs timer trigger (every 5 minutes) before that
            // Function App was removed; it now runs in-process like every other
            // job here. Distinct from BatchProcessing, which polls a different
            // set of jobs through IBatchProcessingService.
            public const string BatchPolling         = "BatchPolling";
            public const string DailyFeedbackAudit    = "DailyFeedbackAudit";

            // Same snapshot-diff audit as DailyFeedbackAudit, against TeamDailyFeedback.
            public const string TeamDailyFeedbackAudit = "TeamDailyFeedbackAudit";

            // Moves custom questions to Expired once their scheduled validity
            // period (the UTC day their ScheduledAt falls in) has ended.
            public const string CustomQuestionExpiry  = "CustomQuestionExpiry";
            public const string ReportScheduler       = "ReportScheduler";
            public const string FeedbackAutoImport    = "FeedbackAutoImport";

            // New-team onboarding nudges (day 2 / day 5 after team creation).
            public const string TeamOnboardingNotifications = "TeamOnboardingNotifications";

            // The deterministic layer's daily tick: replays every enabled team through
            // the engine and stores the state, cycles and feed. Idempotent, so the
            // cadence is a floor rather than a promise.
            public const string EngineDailyRun = "EngineDailyRun";

            // TEST DATA: answers today's engine check-in for the test teams listed under
            // "EngineDummyCheckIn", from the test-data workbook.
            public const string EngineDummyCheckIn = "EngineDummyCheckIn";
        }

        // Returns the delay from `now` until the next scheduled run. For
        // Interval mode the result is always positive (clamped to 1 second
        // minimum). For Daily mode the result rolls over to the first time
        // tomorrow when every entry for today has already passed.
        public static TimeSpan ComputeNextDelay(JobScheduleOptions options, DateTime nowUtc)
        {
            options ??= new JobScheduleOptions();

            if (string.Equals(options.Mode, "Interval", StringComparison.OrdinalIgnoreCase))
            {
                // Clamp so a misconfigured zero/negative doesn't hot-loop.
                var minutes = options.IntervalMinutes > 0 ? options.IntervalMinutes : 1;
                var span = TimeSpan.FromMinutes(minutes);
                return span < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : span;
            }

            // Daily mode: parse "HH:mm[:ss]" entries, sort ascending, pick
            // the first one strictly in the future. Defensive: an empty or
            // unparseable list falls back to noon UTC so the service still
            // runs at a sane cadence instead of busy-looping.
            var times = (options.DailyTimes ?? Array.Empty<string>())
                .Select(s => TimeSpan.TryParse(s, out var t) ? (TimeSpan?)t : null)
                .Where(t => t.HasValue)
                .Select(t => t!.Value)
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            if (times.Count == 0)
                return TimeSpan.FromHours(12);

            var today = nowUtc.Date;
            foreach (var t in times)
            {
                var candidate = today.Add(t);
                if (candidate > nowUtc)
                    return candidate - nowUtc;
            }

            // Everything today already fired → first slot tomorrow.
            return today.AddDays(1).Add(times[0]) - nowUtc;
        }

        // Human-readable description for the startup log line. Saves callers
        // from string-formatting the options themselves and keeps logs
        // consistent across all the background services.
        public static string Describe(JobScheduleOptions options)
        {
            options ??= new JobScheduleOptions();
            if (string.Equals(options.Mode, "Interval", StringComparison.OrdinalIgnoreCase))
                return $"Interval every {options.IntervalMinutes} min";

            var times = options.DailyTimes ?? Array.Empty<string>();
            return $"Daily at [{string.Join(", ", times)}] UTC";
        }
    }
}
