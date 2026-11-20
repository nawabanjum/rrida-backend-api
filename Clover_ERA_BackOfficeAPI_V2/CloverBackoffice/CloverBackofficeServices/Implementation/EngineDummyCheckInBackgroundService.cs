using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Helpers;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// TEST DATA: wakes on "JobSchedules:EngineDummyCheckIn" and answers today's check-in
    /// for the teams in "EngineDummyCheckIn". Both Enabled flags must be true. A second
    /// run on the same day finds everyone already answered and writes nothing.
    /// </summary>
    public class EngineDummyCheckInBackgroundService : BackgroundService
    {
        private const string JobName = JobScheduleHelper.JobNames.EngineDummyCheckIn;

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<EngineDummyCheckInBackgroundService> _logger;
        private readonly IOptionsMonitor<JobScheduleOptions> _scheduleOptions;
        private readonly IOptionsMonitor<EngineDummyCheckInSettings> _settings;

        public EngineDummyCheckInBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<EngineDummyCheckInBackgroundService> logger,
            IOptionsMonitor<JobScheduleOptions> scheduleOptions,
            IOptionsMonitor<EngineDummyCheckInSettings> settings)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _scheduleOptions = scheduleOptions;
            _settings = settings;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var schedule = _scheduleOptions.Get(JobName);
            if (!_settings.CurrentValue.Enabled || !schedule.Enabled)
            {
                _logger.LogInformation(
                    "EngineDummyCheckIn disabled (settings.Enabled={SE}, schedule.Enabled={ScE}). Service exiting.",
                    _settings.CurrentValue.Enabled, schedule.Enabled);
                return;
            }

            _logger.LogInformation("EngineDummyCheckIn started. Schedule = {Schedule}", JobScheduleHelper.Describe(schedule));

            try { await Task.Delay(TimeSpan.FromSeconds(schedule.StartupDelaySeconds), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IEngineDummyCheckInService>();
                    var result = await service.RunAsync(stoppingToken);
                    _logger.LogInformation("[EngineDummyCheckIn] {Message}", result.Message);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in EngineDummyCheckInBackgroundService");
                }

                var nextDelay = JobScheduleHelper.ComputeNextDelay(_scheduleOptions.Get(JobName), DateTime.UtcNow);
                _logger.LogInformation("[EngineDummyCheckIn] Next run in {Delay} (at {RunAt:O} UTC)",
                    nextDelay, DateTime.UtcNow.Add(nextDelay));

                try { await Task.Delay(nextDelay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
