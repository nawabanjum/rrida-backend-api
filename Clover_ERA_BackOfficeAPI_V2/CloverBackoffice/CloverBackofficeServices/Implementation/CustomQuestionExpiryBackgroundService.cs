using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Helpers;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloverBackoffice.Services.Implementation
{
    // Moves custom questions to Expired once their validity period has run out, on the
    // schedule under "JobSchedules:CustomQuestionExpiry". Same shape as
    // TeamDailyFeedbackAuditBackgroundService; default = daily at 00:05 UTC, just after
    // the previous day's questions stop being valid.
    //
    // A custom question carries one ScheduledAt rather than a from/to pair, so its
    // validity period is the UTC day that stamp falls in. The sweep is a status
    // correction, not the gate: GET custom-questions checks the period against the clock
    // as well, so a late or disabled sweep never leaks an out-of-date question.
    public class CustomQuestionExpiryBackgroundService : BackgroundService
    {
        private const string JobName = JobScheduleHelper.JobNames.CustomQuestionExpiry;

        private readonly IServiceProvider _serviceProvider;
        private readonly IOptionsMonitor<JobScheduleOptions> _scheduleOptions;
        private readonly ILogger<CustomQuestionExpiryBackgroundService> _logger;

        public CustomQuestionExpiryBackgroundService(
            IServiceProvider serviceProvider,
            IOptionsMonitor<JobScheduleOptions> scheduleOptions,
            ILogger<CustomQuestionExpiryBackgroundService> logger)
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
                _logger.LogInformation("[CustomQuestionExpiry] Disabled via appsettings. Service exiting.");
                return;
            }

            _logger.LogInformation(
                "[CustomQuestionExpiry] Service started. Schedule = {Schedule}",
                JobScheduleHelper.Describe(opts));

            try { await Task.Delay(TimeSpan.FromSeconds(opts.StartupDelaySeconds), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var customQuestionService = scope.ServiceProvider.GetRequiredService<ICustomQuestionService>();

                    // No user id: the sweep is the system acting, so UpdatedBy is left as
                    // whoever last touched the row rather than being overwritten.
                    var result = await customQuestionService.ExpireDueCustomQuestionsAsync();

                    if (result.ExpiredCount > 0)
                    {
                        _logger.LogInformation(
                            "[CustomQuestionExpiry] Expired {Count} custom question(s): {Ids}",
                            result.ExpiredCount,
                            string.Join(", ", result.ExpiredQuestionIds));
                    }
                    else
                    {
                        _logger.LogInformation("[CustomQuestionExpiry] No custom questions were due to expire.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in CustomQuestionExpiryBackgroundService");
                }

                var nextDelay = JobScheduleHelper.ComputeNextDelay(
                    _scheduleOptions.Get(JobName), DateTime.UtcNow);

                _logger.LogInformation(
                    "[CustomQuestionExpiry] Next run in {Delay} (at {RunAt:O} UTC)",
                    nextDelay, DateTime.UtcNow.Add(nextDelay));

                try { await Task.Delay(nextDelay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
