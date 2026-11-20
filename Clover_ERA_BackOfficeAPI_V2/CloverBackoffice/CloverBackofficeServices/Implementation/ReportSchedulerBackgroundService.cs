using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloverBackoffice.Services.Implementation
{
    // Hosted background service that runs GuardShortageJob.RunJobListAsync on
    // the schedule under "JobSchedules:ReportScheduler". Default = twice a
    // day at 00:00 + 12:00 UTC.
    public class ReportSchedulerBackgroundService : BackgroundService
    {
        private const string JobName = JobScheduleHelper.JobNames.ReportScheduler;

        private readonly IServiceProvider _services;
        private readonly IOptionsMonitor<JobScheduleOptions> _scheduleOptions;
        private readonly ILogger<ReportSchedulerBackgroundService> _logger;

        public ReportSchedulerBackgroundService(
            IServiceProvider services,
            IOptionsMonitor<JobScheduleOptions> scheduleOptions,
            ILogger<ReportSchedulerBackgroundService> logger)
        {
            _services = services;
            _scheduleOptions = scheduleOptions;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var opts = _scheduleOptions.Get(JobName);

            if (!opts.Enabled)
            {
                _logger.LogInformation("[ReportScheduler] Disabled via appsettings. Service exiting.");
                return;
            }

            _logger.LogInformation(
                "[ReportScheduler] Service started. Schedule = {Schedule}",
                JobScheduleHelper.Describe(opts));

            try { await Task.Delay(TimeSpan.FromSeconds(opts.StartupDelaySeconds), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var job = scope.ServiceProvider.GetRequiredService<GuardShortageJob>();
                    await job.RunJobListAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ReportScheduler] RunJobListAsync threw");
                }

                var nextDelay = JobScheduleHelper.ComputeNextDelay(
                    _scheduleOptions.Get(JobName), DateTime.UtcNow);

                _logger.LogInformation(
                    "[ReportScheduler] Next run in {Delay} (at {RunAt:O} UTC)",
                    nextDelay, DateTime.UtcNow.Add(nextDelay));

                try { await Task.Delay(nextDelay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }

            _logger.LogInformation("[ReportScheduler] Service stopping.");
        }
    }
}
