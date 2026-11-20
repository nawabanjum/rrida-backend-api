using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>Queue item for a corpus seeding run. Carries who asked, for the log trail.</summary>
    public class EranCoachSeedWorkItem
    {
        public string? RequestedBy { get; set; }
    }

    /// <summary>
    /// Hand-off channel between POST /seed-corpus and the seeding worker. Capacity 1 with
    /// DropWrite — <see cref="EranCoachSeedRunState"/> already rejects a second run, so the
    /// channel never needs to block the request thread.
    /// </summary>
    public class EranCoachSeedWorkQueue
    {
        private readonly Channel<EranCoachSeedWorkItem> _channel =
            Channel.CreateBounded<EranCoachSeedWorkItem>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropWrite
            });

        public ChannelWriter<EranCoachSeedWorkItem> Writer => _channel.Writer;
        public ChannelReader<EranCoachSeedWorkItem> Reader => _channel.Reader;
    }

    /// <summary>
    /// Live state of the corpus seeding run. Singleton so POST /seed-corpus can return
    /// immediately and GET /corpus-status can report progress after that request has ended.
    /// Also the concurrency guard: only one run may be in flight at a time.
    /// </summary>
    public class EranCoachSeedRunState
    {
        private readonly object _gate = new();

        private bool _active;
        private string _status = "Idle";
        private DateTime? _queuedAt;
        private DateTime? _startedAt;
        private DateTime? _completedAt;
        private int? _seededCount;
        private string? _error;

        /// <summary>
        /// Claims the single run slot and marks it Queued. False when a run is already queued
        /// or running — the caller should reject the request instead of enqueuing another.
        /// </summary>
        public bool TryClaim()
        {
            lock (_gate)
            {
                if (_active) return false;

                _active = true;
                _status = "Queued";
                _queuedAt = DateTime.UtcNow;
                _startedAt = null;
                _completedAt = null;
                _seededCount = null;
                _error = null;
                return true;
            }
        }

        /// <summary>Gives the slot back when the claim could not be handed to the worker.</summary>
        public void Abandon(string reason)
        {
            lock (_gate)
            {
                _active = false;
                _status = "Failed";
                _error = reason;
                _completedAt = DateTime.UtcNow;
            }
        }

        public void MarkRunning()
        {
            lock (_gate)
            {
                _active = true;
                _status = "Running";
                _startedAt = DateTime.UtcNow;
            }
        }

        public void MarkCompleted(int seededCount)
        {
            lock (_gate)
            {
                _active = false;
                _status = "Completed";
                _seededCount = seededCount;
                _error = null;
                _completedAt = DateTime.UtcNow;
            }
        }

        public void MarkFailed(string error)
        {
            lock (_gate)
            {
                _active = false;
                _status = "Failed";
                _error = error;
                _completedAt = DateTime.UtcNow;
            }
        }

        /// <summary>Consistent point-in-time copy for the status endpoint.</summary>
        public EranCoachSeedStatusDto Snapshot()
        {
            lock (_gate)
            {
                return new EranCoachSeedStatusDto
                {
                    Status = _status,
                    IsRunning = _active,
                    QueuedAt = _queuedAt,
                    StartedAt = _startedAt,
                    CompletedAt = _completedAt,
                    SeededCount = _seededCount,
                    Error = _error
                };
            }
        }
    }

    /// <summary>
    /// Runs corpus seeding off the request thread. Embedding the whole CLOVER bundle takes far
    /// longer than a request may stay open (the gateway returns 500 "request timed out" first),
    /// so POST /seed-corpus queues the work here and returns straight away; the caller polls
    /// GET /corpus-status. The run is bound to the application lifetime, not the request.
    /// </summary>
    public class EranCoachSeedBackgroundService : BackgroundService
    {
        private readonly EranCoachSeedWorkQueue _queue;
        private readonly EranCoachSeedRunState _state;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<EranCoachSeedBackgroundService> _logger;

        public EranCoachSeedBackgroundService(
            EranCoachSeedWorkQueue queue,
            EranCoachSeedRunState state,
            IServiceProvider serviceProvider,
            ILogger<EranCoachSeedBackgroundService> logger)
        {
            _queue = queue;
            _state = state;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[EranCoach] Corpus seeding worker started.");

            try
            {
                await foreach (var workItem in _queue.Reader.ReadAllAsync(stoppingToken))
                {
                    try
                    {
                        await ProcessWorkItemAsync(workItem, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[EranCoach] Unhandled error during corpus seeding.");
                        _state.MarkFailed(ex.Message);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Application shutting down — nothing to report.
            }
        }

        private async Task ProcessWorkItemAsync(EranCoachSeedWorkItem workItem, CancellationToken ct)
        {
            _state.MarkRunning();
            _logger.LogInformation("[EranCoach] Corpus seeding started (requested by {RequestedBy}).",
                workItem.RequestedBy ?? "unknown");

            using var scope = _serviceProvider.CreateScope();
            var seed = scope.ServiceProvider.GetRequiredService<IEranCoachSeedService>();

            try
            {
                var count = await seed.SeedAsync(ct);
                _state.MarkCompleted(count);
                _logger.LogInformation("[EranCoach] Corpus seeding COMPLETED — {Count} items.", count);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _state.MarkFailed("Seeding was cancelled because the application is shutting down.");
                _logger.LogWarning("[EranCoach] Corpus seeding cancelled — application stopping.");
            }
            catch (Exception ex)
            {
                _state.MarkFailed(ex.Message);
                _logger.LogError(ex, "[EranCoach] Corpus seeding FAILED: {Message}", ex.Message);
            }
        }
    }
}
