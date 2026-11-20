using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Helpers;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloverBackoffice.Services.Implementation
{
    // Runs the team-daily-feedback audit on the schedule under
    // "JobSchedules:TeamDailyFeedbackAudit". Mirrors
    // DailyFeedbackAuditBackgroundService; default = twice a day at 00:00 +
    // 12:00 UTC.
    public class TeamDailyFeedbackAuditBackgroundService : BackgroundService
    {
        private const string JobName = JobScheduleHelper.JobNames.TeamDailyFeedbackAudit;

        private readonly IServiceProvider _serviceProvider;
        private readonly IOptionsMonitor<JobScheduleOptions> _scheduleOptions;
        private readonly ILogger<TeamDailyFeedbackAuditBackgroundService> _logger;

        public TeamDailyFeedbackAuditBackgroundService(
            IServiceProvider serviceProvider,
            IOptionsMonitor<JobScheduleOptions> scheduleOptions,
            ILogger<TeamDailyFeedbackAuditBackgroundService> logger)
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
                _logger.LogInformation("[TeamDailyFeedbackAudit] Disabled via appsettings. Service exiting.");
                return;
            }

            _logger.LogInformation(
                "[TeamDailyFeedbackAudit] Service started. Schedule = {Schedule}",
                JobScheduleHelper.Describe(opts));

            try { await Task.Delay(TimeSpan.FromSeconds(opts.StartupDelaySeconds), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var auditService = scope.ServiceProvider.GetRequiredService<ITeamDailyFeedbackAuditService>();
                    await auditService.PerformAuditAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in TeamDailyFeedbackAuditBackgroundService");
                }

                var nextDelay = JobScheduleHelper.ComputeNextDelay(
                    _scheduleOptions.Get(JobName), DateTime.UtcNow);

                _logger.LogInformation(
                    "[TeamDailyFeedbackAudit] Next run in {Delay} (at {RunAt:O} UTC)",
                    nextDelay, DateTime.UtcNow.Add(nextDelay));

                try { await Task.Delay(nextDelay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
