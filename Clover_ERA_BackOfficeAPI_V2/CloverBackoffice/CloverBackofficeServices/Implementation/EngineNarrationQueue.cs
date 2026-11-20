using System.Collections.Concurrent;
using System.Threading.Channels;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Teams whose feed has just been rebuilt and may hold messages Eran has not phrased.
    ///
    /// The MVP phrases the new messages straight after every step. Here a run happens in the
    /// daily job, in an admin call, or inside a manager's confirm or decline, and none of
    /// those should wait on a model, so the run only queues the team and the worker below
    /// does the phrasing. A team already queued is not queued twice.
    /// </summary>
    public sealed class EngineNarrationQueue : IEngineNarrationQueue
    {
        private readonly Channel<(uint Unit, int? ThroughDay, bool RetryFallbacks)> _channel =
            Channel.CreateUnbounded<(uint, int?, bool)>();
        private readonly ConcurrentDictionary<(uint, int?, bool), byte> _queued = new();

        public void Enqueue(uint businessUnitId, int? throughDay = null) => Write((businessUnitId, throughDay, false));

        public void EnqueueFallbackRetry(uint businessUnitId) => Write((businessUnitId, null, true));

        private void Write((uint, int?, bool) work)
        {
            if (_queued.TryAdd(work, 0)) _channel.Writer.TryWrite(work);
        }

        internal ChannelReader<(uint Unit, int? ThroughDay, bool RetryFallbacks)> Reader => _channel.Reader;

        internal void Done((uint, int?, bool) work) => _queued.TryRemove(work, out _);
    }

    /// <summary>
    /// Phrases each queued team's newest pending messages, the MVP's batch of fourteen.
    /// Switched off with <c>Rrida:Narration:Enabled = false</c>, which leaves the messages
    /// unphrased until the admin narrate endpoint is called.
    /// </summary>
    public sealed class EngineNarrationBackgroundService : BackgroundService
    {
        private readonly EngineNarrationQueue _queue;
        private readonly IServiceScopeFactory _scopes;
        private readonly IConfiguration _configuration;
        private readonly ILogger<EngineNarrationBackgroundService> _logger;

        public EngineNarrationBackgroundService(
            EngineNarrationQueue queue,
            IServiceScopeFactory scopes,
            IConfiguration configuration,
            ILogger<EngineNarrationBackgroundService> logger)
        {
            _queue = queue;
            _scopes = scopes;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var work in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                var unit = work.Unit;
                // Released before phrasing, so a run that lands mid-phrase queues the team again.
                _queue.Done(work);
                if (!_configuration.GetValue("Rrida:Narration:Enabled", true)) continue;

                try
                {
                    using var scope = _scopes.CreateScope();
                    var narrator = scope.ServiceProvider.GetRequiredService<IEranNarratorService>();
                    if (work.RetryFallbacks)
                        await narrator.RetryFallbacksAsync(unit, stoppingToken);
                    else
                        await narrator.NarrateAsync(unit, EranNarratorService.DefaultBatch, stoppingToken, work.ThroughDay);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // The engine's results stand without the wording; the next run queues it again.
                    _logger.LogError(ex, "[Eran] Narration for unit {Unit} failed.", unit);
                }
            }
        }
    }
}
