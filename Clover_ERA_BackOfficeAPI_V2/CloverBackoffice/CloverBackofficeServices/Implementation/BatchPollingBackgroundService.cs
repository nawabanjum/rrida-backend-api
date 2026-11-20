using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloverBackoffice.Services.Implementation
{
    // Polls in-flight Azure OpenAI batch jobs and persists their results, on the
    // schedule under "JobSchedules:BatchPolling".
    //
    // This work used to reach the API from outside: the CloverBackoffice.Jobs
    // Function App fired a 5-minute timer trigger that POSTed to
    // /api/rag/azure-backgroundjob-function, which called
    // FeedbackAnalysisService.AzureFunctionCall() -> RunJobAsync(). The Function
    // App and that endpoint are gone; this service is the in-process
    // replacement, and the default cadence below matches the timer it replaces
    // so batch turnaround is unchanged.
    //
    // It runs GuardShortageJob.RunJobAsync — the canonical copy of the polling
    // loop, and the same one the removed Hangfire "guard-shortage-check"
    // recurring job used to call. The duplicate that lived in
    // FeedbackAnalysisService went with the endpoint.
    public class BatchPollingBackgroundService : BackgroundService
    {
        private const string JobName = JobScheduleHelper.JobNames.BatchPolling;

        private readonly IServiceProvider _services;
        private readonly IOptionsMonitor<JobScheduleOptions> _scheduleOptions;
        private readonly ILogger<BatchPollingBackgroundService> _logger;

        public BatchPollingBackgroundService(
            IServiceProvider services,
            IOptionsMonitor<JobScheduleOptions> scheduleOptions,
            ILogger<BatchPollingBackgroundService> logger)
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
                _logger.LogInformation("[BatchPolling] Disabled via appsettings. Service exiting.");
                return;
            }

            _logger.LogInformation(
                "[BatchPolling] Service started. Schedule = {Schedule}",
                JobScheduleHelper.Describe(opts));

            try { await Task.Delay(TimeSpan.FromSeconds(opts.StartupDelaySeconds), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var job = scope.ServiceProvider.GetRequiredService<GuardShortageJob>();
                    await job.RunJobAsync();
                }
                catch (Exception ex)
                {
                    // RunJobAsync already swallows and logs per-batch failures to
                    // LLMLogs; anything reaching here is a setup/DI failure, which
                    // must not kill the loop.
                    _logger.LogError(ex, "[BatchPolling] RunJobAsync threw");
                }

                // Re-read options every loop so an appsettings change is picked
                // up on the next iteration.
                var nextDelay = JobScheduleHelper.ComputeNextDelay(
                    _scheduleOptions.Get(JobName), DateTime.UtcNow);

                _logger.LogInformation(
                    "[BatchPolling] Next run in {Delay} (at {RunAt:O} UTC)",
                    nextDelay, DateTime.UtcNow.Add(nextDelay));

                try { await Task.Delay(nextDelay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }

            _logger.LogInformation("[BatchPolling] Service stopping.");
        }
    }
}
