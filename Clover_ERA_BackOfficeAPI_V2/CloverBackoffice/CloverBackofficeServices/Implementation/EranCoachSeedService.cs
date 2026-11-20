using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Pgvector;

namespace CloverBackoffice.Services.Implementation
{
    public class EranCoachSeedService : IEranCoachSeedService
    {
        private readonly IEranCoachCorpusRepository _corpus;
        private readonly IEmbeddingRepository _embedding;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<EranCoachSeedService> _logger;

        public EranCoachSeedService(
            IEranCoachCorpusRepository corpus,
            IEmbeddingRepository embedding,
            IWebHostEnvironment env,
            ILogger<EranCoachSeedService> logger)
        {
            _corpus = corpus;
            _embedding = embedding;
            _env = env;
            _logger = logger;
        }

        public Task<int> CountAsync(CancellationToken ct = default) => _corpus.CountAsync(ct);

        public async Task<int> SeedAsync(CancellationToken ct = default)
        {
            var corpus = EranCoachCorpusLoader.Load(_env.ContentRootPath);
            var rows = new List<DocumentEmbedding>(
                corpus.Worksheets.Count + corpus.MicroActions.Count + corpus.BookChunks.Count);

            var now = DateTime.UtcNow;
            var done = 0;

            foreach (var w in corpus.Worksheets)
            {
                ct.ThrowIfCancellationRequested();
                var vec = await _embedding.GetEmbeddingAsync(EranCoachCorpusLoader.WorksheetEmbedText(w), ct);
                rows.Add(NewRow(
                    EranCoachCorpusLoader.SourceWorksheet, w.Id,
                    EranCoachCorpusLoader.WorksheetSnippet(w),
                    EranCoachCorpusLoader.FormatWorksheet(w), vec, now));
                LogProgress(ref done, rows.Count);
            }

            foreach (var m in corpus.MicroActions)
            {
                ct.ThrowIfCancellationRequested();
                var vec = await _embedding.GetEmbeddingAsync(EranCoachCorpusLoader.MicroActionEmbedText(m), ct);
                rows.Add(NewRow(
                    EranCoachCorpusLoader.SourceMicroAction, m.Id,
                    EranCoachCorpusLoader.MicroActionSnippet(m),
                    EranCoachCorpusLoader.FormatMicroAction(m), vec, now));
                LogProgress(ref done, rows.Count);
            }

            foreach (var b in corpus.BookChunks)
            {
                ct.ThrowIfCancellationRequested();
                var vec = await _embedding.GetEmbeddingAsync(EranCoachCorpusLoader.BookEmbedText(b), ct);
                rows.Add(NewRow(
                    EranCoachCorpusLoader.SourceBook, b.ChunkId,
                    EranCoachCorpusLoader.BookSnippet(b),
                    b.Text, vec, now));
                LogProgress(ref done, rows.Count);
            }

            var count = await _corpus.ReplaceCorpusAsync(rows, ct);
            _logger.LogInformation("[EranCoach] Seeded {Count} corpus rows into namespace '{Ns}'.",
                count, EranCoachCorpusRepository.Namespace);
            return count;
        }

        private static DocumentEmbedding NewRow(string sourceType, string cloverId, string title, string content, float[] vec, DateTime now)
            => new()
            {
                Title = title,
                Content = content,
                Embedding = new Vector(vec),
                SourceType = sourceType,
                FileName = cloverId,
                EmbeddingType = EranCoachCorpusRepository.Namespace,
                ProcessId = 0,
                CreatedAt = now
            };

        private void LogProgress(ref int done, int total)
        {
            done++;
            if (done % 50 == 0)
                _logger.LogInformation("[EranCoach] Embedded {Done} corpus items…", done);
        }
    }
}
