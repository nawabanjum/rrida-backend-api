using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Wakes the deterministic layer's daily tick on the schedule in appsettings, the same
    /// way every other job here is driven.
    ///
    /// Disabled by default: the engine must not start running teams because a deployment
    /// happened. Switching it on is a deliberate act, after the tables exist and at least
    /// one team has been set up.
    /// </summary>
    public class EngineDailyRunBackgroundService : BackgroundService
    {
        private const string JobName = JobScheduleHelper.JobNames.EngineDailyRun;

        private readonly IServiceProvider _services;
        private readonly IOptionsMonitor<JobScheduleOptions> _scheduleOptions;
        private readonly ILogger<EngineDailyRunBackgroundService> _logger;

        public EngineDailyRunBackgroundService(
            IServiceProvider services,
            IOptionsMonitor<JobScheduleOptions> scheduleOptions,
            ILogger<EngineDailyRunBackgroundService> logger)
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
                _logger.LogInformation("[EngineDailyRun] Disabled via appsettings. Service exiting.");
                return;
            }

            _logger.LogInformation(
                "[EngineDailyRun] Service started. Schedule = {Schedule}", JobScheduleHelper.Describe(opts));

            try { await Task.Delay(TimeSpan.FromSeconds(opts.StartupDelaySeconds), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var job = scope.ServiceProvider.GetRequiredService<EngineDailyRunJob>();
                    await job.RunJobAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    // The job already handles a single team failing; anything reaching here
                    // is a setup or DI failure, which must not kill the loop.
                    _logger.LogError(ex, "[EngineDailyRun] RunJobAsync threw");
                }

                var nextDelay = JobScheduleHelper.ComputeNextDelay(_scheduleOptions.Get(JobName), DateTime.UtcNow);

                _logger.LogInformation(
                    "[EngineDailyRun] Next run in {Delay} (at {RunAt:O} UTC)", nextDelay, DateTime.UtcNow.Add(nextDelay));

                try { await Task.Delay(nextDelay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }

            _logger.LogInformation("[EngineDailyRun] Service stopping.");
        }
    }
}
