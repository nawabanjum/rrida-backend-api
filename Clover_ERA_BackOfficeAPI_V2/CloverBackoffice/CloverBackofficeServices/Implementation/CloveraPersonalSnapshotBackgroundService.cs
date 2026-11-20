using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    public class CloveraPersonalSnapshotWorkItem
    {
        public int SnapshotId { get; set; }
    }

    public class CloveraPersonalSnapshotWorkQueue
    {
        private readonly Channel<CloveraPersonalSnapshotWorkItem> _channel =
            Channel.CreateBounded<CloveraPersonalSnapshotWorkItem>(new BoundedChannelOptions(20)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

        public ChannelWriter<CloveraPersonalSnapshotWorkItem> Writer => _channel.Writer;
        public ChannelReader<CloveraPersonalSnapshotWorkItem> Reader => _channel.Reader;
    }

    public class CloveraPersonalSnapshotBackgroundService : BackgroundService
    {
        private readonly CloveraPersonalSnapshotWorkQueue _queue;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CloveraPersonalSnapshotBackgroundService> _logger;

        public CloveraPersonalSnapshotBackgroundService(
            CloveraPersonalSnapshotWorkQueue queue,
            IServiceProvider serviceProvider,
            ILogger<CloveraPersonalSnapshotBackgroundService> logger)
        {
            _queue = queue;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[CloveraPersonalSnapshot] Service started.");

            await foreach (var workItem in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessWorkItemAsync(workItem, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[CloveraPersonalSnapshot] Unhandled error for SnapshotId={SnapshotId}", workItem.SnapshotId);
                }
            }
        }

        private async Task ProcessWorkItemAsync(CloveraPersonalSnapshotWorkItem workItem, CancellationToken ct)
        {
            _logger.LogInformation("[CloveraPersonalSnapshot] Processing SnapshotId={SnapshotId}", workItem.SnapshotId);

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var service = scope.ServiceProvider.GetRequiredService<ICloveraPersonalSnapshotService>();

            var snapshot = await db.CloveraPersonalSnapshots.FindAsync(new object[] { workItem.SnapshotId }, ct);
            if (snapshot == null)
            {
                _logger.LogWarning("[CloveraPersonalSnapshot] SnapshotId={SnapshotId} not found in DB, skipping.", workItem.SnapshotId);
                return;
            }

            snapshot.Status = "Running";
            await db.SaveChangesAsync(ct);

            try
            {
                await service.GenerateAsync(snapshot, ct);
                snapshot.Status = "Completed";
                snapshot.CompletedAt = DateTime.UtcNow;
                _logger.LogInformation("[CloveraPersonalSnapshot] SnapshotId={SnapshotId} COMPLETED.", workItem.SnapshotId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CloveraPersonalSnapshot] SnapshotId={SnapshotId} FAILED: {Message}", workItem.SnapshotId, ex.Message);
                snapshot.Status = "Failed";
                snapshot.ErrorMessage = ex.Message;
                snapshot.CompletedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
