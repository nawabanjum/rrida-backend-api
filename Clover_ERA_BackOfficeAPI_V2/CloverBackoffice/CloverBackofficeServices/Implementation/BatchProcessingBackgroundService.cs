using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Helpers;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloverBackoffice.Services.Implementation
{
    // Polls Stripe/LLM batch jobs on the schedule under
    // "JobSchedules:BatchProcessing". Default = twice a day at 00:00 + 12:00
    // UTC; flip Mode to "Interval" with IntervalMinutes for testing.
    public class BatchProcessingBackgroundService : BackgroundService
    {
        private const string JobName = JobScheduleHelper.JobNames.BatchProcessing;

        private readonly IServiceProvider _serviceProvider;
        private readonly IOptionsMonitor<JobScheduleOptions> _scheduleOptions;
        private readonly ILogger<BatchProcessingBackgroundService> _logger;

        public BatchProcessingBackgroundService(
            IServiceProvider serviceProvider,
            IOptionsMonitor<JobScheduleOptions> scheduleOptions,
            ILogger<BatchProcessingBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _scheduleOptions = scheduleOptions;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var opts = _scheduleOptions.Get(JobName);

            if (!opts.Enabled)
            {
                _logger.LogInformation("[BatchProcessing] Disabled via appsettings. Service exiting.");
                return;
            }

            _logger.LogInformation(
                "[BatchProcessing] Service started. Schedule = {Schedule}",
                JobScheduleHelper.Describe(opts));

            try { await Task.Delay(TimeSpan.FromSeconds(opts.StartupDelaySeconds), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var batchService = scope.ServiceProvider.GetRequiredService<IBatchProcessingService>();
                    await batchService.ProcessCompletedJobsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in BatchProcessingBackgroundService");
                }

                var nextDelay = JobScheduleHelper.ComputeNextDelay(
                    _scheduleOptions.Get(JobName), DateTime.UtcNow);

                _logger.LogInformation(
                    "[BatchProcessing] Next run in {Delay} (at {RunAt:O} UTC)",
                    nextDelay, DateTime.UtcNow.Add(nextDelay));

                try { await Task.Delay(nextDelay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
