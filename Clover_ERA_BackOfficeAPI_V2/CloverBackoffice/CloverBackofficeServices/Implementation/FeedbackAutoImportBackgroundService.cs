using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Helpers;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloverBackoffice.Services.Implementation
{
    public class FeedbackAutoImportBackgroundService : BackgroundService
    {
        private const string JobName = JobScheduleHelper.JobNames.FeedbackAutoImport;

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<FeedbackAutoImportBackgroundService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _hostEnvironment;
        private readonly IOptionsMonitor<JobScheduleOptions> _scheduleOptions;
        private readonly FeedbackAutoImportSettings _settings;

        public FeedbackAutoImportBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<FeedbackAutoImportBackgroundService> logger,
            IConfiguration configuration,
            IHostEnvironment hostEnvironment,
            IOptionsMonitor<JobScheduleOptions> scheduleOptions)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _configuration = configuration;
            _hostEnvironment = hostEnvironment;
            _scheduleOptions = scheduleOptions;
            _settings = _configuration.GetSection("FeedbackAutoImport").Get<FeedbackAutoImportSettings>();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // FeedbackAutoImport has two toggles: its own Enabled flag (for
            // ExcelFilePath / BusinessUnitIds wiring) and the schedule's
            // Enabled flag under JobSchedules:FeedbackAutoImport. Both must
            // be true for the job to actually fire.
            var schedule = _scheduleOptions.Get(JobName);

            if (_settings == null || !_settings.Enabled || !schedule.Enabled)
            {
                _logger.LogInformation(
                    "FeedbackAutoImport disabled (settings.Enabled={SE}, schedule.Enabled={ScE}). Service exiting.",
                    _settings?.Enabled, schedule.Enabled);
                return;
            }

            _logger.LogInformation(
                "FeedbackAutoImport started. Schedule = {Schedule}",
                JobScheduleHelper.Describe(schedule));

            try { await Task.Delay(TimeSpan.FromSeconds(schedule.StartupDelaySeconds), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessFeedbackImportAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in FeedbackAutoImportBackgroundService");
                }

                var nextDelay = JobScheduleHelper.ComputeNextDelay(
                    _scheduleOptions.Get(JobName), DateTime.UtcNow);

                _logger.LogInformation(
                    "[FeedbackAutoImport] Next run in {Delay} (at {RunAt:O} UTC)",
                    nextDelay, DateTime.UtcNow.Add(nextDelay));

                try { await Task.Delay(nextDelay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task ProcessFeedbackImportAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var feedbackService = scope.ServiceProvider.GetRequiredService<IFeedbackAnalysisService>();

            // Resolve Excel file path
            string excelFilePath = _settings.ExcelFilePath;
            if (!System.IO.Path.IsPathRooted(excelFilePath))
            {
                excelFilePath = System.IO.Path.Combine(_hostEnvironment.ContentRootPath, excelFilePath);
            }

            _logger.LogInformation($"Excel path: {excelFilePath}");
            _logger.LogInformation($"File exists: {File.Exists(excelFilePath)}");

            // Parse BusinessUnitIds from comma-separated string
            if (string.IsNullOrWhiteSpace(_settings.BusinessUnitIds))
            {
                // Nothing to process, exit safely
                return;
            }

            var businessUnitIds = _settings.BusinessUnitIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim())
                .ToList();


            _logger.LogInformation($"Processing feedback import for {businessUnitIds.Count} business units using file: {excelFilePath}");

            foreach (var businessUnitId in businessUnitIds)
            {
                try
                {
                    _logger.LogInformation($"Importing feedback for BusinessUnitId: {businessUnitId}");

                    // Call the import method
                    await feedbackService.ImportFeedbackFromExcelAsync(excelFilePath, uint.Parse(businessUnitId));

                    _logger.LogInformation($"Successfully imported feedback for BusinessUnitId: {businessUnitId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to import feedback for BusinessUnitId: {businessUnitId}");
                }
            }
        }
    }
}
