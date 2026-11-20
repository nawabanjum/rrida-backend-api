using System;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    public class EranCoachMemoryService : IEranCoachMemoryService
    {
        private const int SummaryMaxTokens = 700;

        private const string SummarySystemPrompt =
            "You maintain a rolling coaching memory for a single manager. Given the prior memory and " +
            "the latest exchange, output an updated memory: a concise running summary of the people and " +
            "situations discussed, advice given, what the manager committed to, and open follow-ups. " +
            "Keep the whole thing under 250 words. Merge new information into the prior memory rather than " +
            "appending; drop stale detail. Output only the memory text, with no preamble or headings.";

        private readonly IEranCoachMemoryRepository _repo;
        private readonly IEranCoachAnthropicClient _client;
        private readonly ILogger<EranCoachMemoryService> _logger;

        public EranCoachMemoryService(
            IEranCoachMemoryRepository repo,
            IEranCoachAnthropicClient client,
            ILogger<EranCoachMemoryService> logger)
        {
            _repo = repo;
            _client = client;
            _logger = logger;
        }

        public async Task<string?> GetAsync(int managerUserId, long? businessUnitId, CancellationToken ct = default)
        {
            if (managerUserId <= 0) return null;
            var row = await _repo.GetByManagerAsync(managerUserId, businessUnitId, ct);
            return string.IsNullOrWhiteSpace(row?.SummaryText) ? null : row!.SummaryText;
        }

        public async Task SaveAsync(int managerUserId, long? businessUnitId, string latestUserMessage, string latestAnswer, ModelConfiguration model, CancellationToken ct = default)
        {
            if (managerUserId <= 0) return;
            if (string.IsNullOrWhiteSpace(latestAnswer)) return;

            var prior = (await _repo.GetByManagerAsync(managerUserId, businessUnitId, ct))?.SummaryText;

            var userContent =
                $"PRIOR MEMORY:\n{(string.IsNullOrWhiteSpace(prior) ? "(none yet)" : prior)}\n\n" +
                $"LATEST EXCHANGE:\nManager: {latestUserMessage}\nCoach: {latestAnswer}\n\n" +
                "Updated memory:";

            var summary = (await _client.CompleteAsync(SummarySystemPrompt, userContent, model, SummaryMaxTokens, ct))?.Trim();
            if (string.IsNullOrWhiteSpace(summary)) return;

            await _repo.UpsertAsync(managerUserId, businessUnitId, summary, ct);
            _logger.LogInformation("[EranCoach] Updated rolling memory for manager {Id}, team {Unit}.", managerUserId, businessUnitId);
        }
    }
}
