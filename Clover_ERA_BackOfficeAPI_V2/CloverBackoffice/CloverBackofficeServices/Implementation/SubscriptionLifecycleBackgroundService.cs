using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloverBackoffice.Services.Implementation
{
    // Hosted background service that runs SubscriptionLifecycleJob on the
    // schedule configured under "JobSchedules:SubscriptionLifecycle" in
    // appsettings.json. Default = twice a day at 00:00 + 12:00 UTC. Switch
    // Mode to "Interval" with IntervalMinutes for local testing.
    public class SubscriptionLifecycleBackgroundService : BackgroundService
    {
        private const string JobName = JobScheduleHelper.JobNames.SubscriptionLifecycle;

        private readonly IServiceProvider _services;
        private readonly IOptionsMonitor<JobScheduleOptions> _scheduleOptions;
        private readonly ILogger<SubscriptionLifecycleBackgroundService> _logger;

        public SubscriptionLifecycleBackgroundService(
            IServiceProvider services,
            IOptionsMonitor<JobScheduleOptions> scheduleOptions,
            ILogger<SubscriptionLifecycleBackgroundService> logger)
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
                _logger.LogInformation("[SubscriptionLifecycle] Disabled via appsettings. Service exiting.");
                return;
            }

            _logger.LogInformation(
                "[SubscriptionLifecycle] Service started. Schedule = {Schedule}",
                JobScheduleHelper.Describe(opts));

            try { await Task.Delay(TimeSpan.FromSeconds(opts.StartupDelaySeconds), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var job = scope.ServiceProvider.GetRequiredService<SubscriptionLifecycleJob>();
                    await job.RunAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[SubscriptionLifecycle] RunAsync threw");
                }

                // Re-read options every loop so an appsettings change is
                // picked up on the next iteration. The job itself owns all
                // day-based "is this due yet?" logic — this service only
                // controls when the wake-up tick happens.
                var nextDelay = JobScheduleHelper.ComputeNextDelay(
                    _scheduleOptions.Get(JobName), DateTime.UtcNow);

                _logger.LogInformation(
                    "[SubscriptionLifecycle] Next run in {Delay} (at {RunAt:O} UTC)",
                    nextDelay, DateTime.UtcNow.Add(nextDelay));

                try { await Task.Delay(nextDelay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }

            _logger.LogInformation("[SubscriptionLifecycle] Service stopping.");
        }
    }
}
