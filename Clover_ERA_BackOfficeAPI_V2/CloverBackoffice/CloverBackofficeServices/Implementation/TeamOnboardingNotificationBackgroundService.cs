using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Helpers;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloverBackoffice.Services.Implementation
{
    // Hosted service that runs the new-team onboarding nudges (day 2 and day 5
    // after a team is created) on the schedule under
    // "JobSchedules:TeamOnboardingNotifications". Default = once a day at 08:00
    // UTC — a nudge about "yesterday" is only useful in the morning.
    //
    // Same shape as ReportSchedulerBackgroundService: the schedule here only
    // controls how often we WAKE UP; which teams are actually due is decided in
    // TeamOnboardingNotificationService against each team's registration date.
    public class TeamOnboardingNotificationBackgroundService : BackgroundService
    {
        private const string JobName = JobScheduleHelper.JobNames.TeamOnboardingNotifications;

        private readonly IServiceProvider _services;
        private readonly IOptionsMonitor<JobScheduleOptions> _scheduleOptions;
        private readonly ILogger<TeamOnboardingNotificationBackgroundService> _logger;

        public TeamOnboardingNotificationBackgroundService(
            IServiceProvider services,
            IOptionsMonitor<JobScheduleOptions> scheduleOptions,
            ILogger<TeamOnboardingNotificationBackgroundService> logger)
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
                _logger.LogInformation("[TeamOnboarding] Disabled via appsettings. Service exiting.");
                return;
            }

            _logger.LogInformation(
                "[TeamOnboarding] Service started. Schedule = {Schedule}",
                JobScheduleHelper.Describe(opts));

            try { await Task.Delay(TimeSpan.FromSeconds(opts.StartupDelaySeconds), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<ITeamOnboardingNotificationService>();
                    var sent = await service.RunAsync(stoppingToken);

                    if (sent > 0)
                        _logger.LogInformation("[TeamOnboarding] Sent {Count} onboarding notification(s).", sent);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[TeamOnboarding] RunAsync threw");
                }

                var nextDelay = JobScheduleHelper.ComputeNextDelay(
                    _scheduleOptions.Get(JobName), DateTime.UtcNow);

                _logger.LogInformation(
                    "[TeamOnboarding] Next run in {Delay} (at {RunAt:O} UTC)",
                    nextDelay, DateTime.UtcNow.Add(nextDelay));

                try { await Task.Delay(nextDelay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }

            _logger.LogInformation("[TeamOnboarding] Service stopping.");
        }
    }
}
