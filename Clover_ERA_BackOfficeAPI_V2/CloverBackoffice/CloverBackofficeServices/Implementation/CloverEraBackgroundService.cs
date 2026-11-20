using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    public class CloverEraWorkItem
    {
        public int RunId { get; set; }
        public WorkbenchRunRequestDto Request { get; set; } = null!;
        public byte[]? CsvFileBytes { get; set; }
        public string? CsvFileName { get; set; }
        public string? CsvContentType { get; set; }

        // What raised this run. Ends up in the run's ResultJson, which is how the
        // workbench screens tell an operator's run from a scheduled one without
        // a column for it.
        public string Source { get; set; } = WorkbenchRunSources.Workbench;
    }

    public class CloverEraWorkQueue
    {
        private readonly Channel<CloverEraWorkItem> _channel =
            Channel.CreateBounded<CloverEraWorkItem>(new BoundedChannelOptions(20)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

        public ChannelWriter<CloverEraWorkItem> Writer => _channel.Writer;
        public ChannelReader<CloverEraWorkItem> Reader => _channel.Reader;
    }

    public class CloverEraBackgroundService : BackgroundService
    {
        private readonly CloverEraWorkQueue _queue;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CloverEraBackgroundService> _logger;

        public CloverEraBackgroundService(
            CloverEraWorkQueue queue,
            IServiceProvider serviceProvider,
            ILogger<CloverEraBackgroundService> logger)
        {
            _queue = queue;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[CloverEraBackground] Service started.");

            await foreach (var workItem in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessWorkItemAsync(workItem, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[CloverEraBackground] Unhandled error for RunId={RunId}", workItem.RunId);
                }
            }
        }

        // The run itself lives in WorkbenchRunExecutor, which the report
        // scheduler also calls. This service is now only the channel loop: take
        // a queued item, open a scope, hand it over.
        private async Task ProcessWorkItemAsync(CloverEraWorkItem workItem, CancellationToken ct)
        {
            using var scope = _serviceProvider.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<IWorkbenchRunExecutor>();

            await executor.ExecuteAsync(workItem, ct);
        }
    }
}
