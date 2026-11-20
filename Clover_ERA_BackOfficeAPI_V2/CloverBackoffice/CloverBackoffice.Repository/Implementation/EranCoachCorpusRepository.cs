using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Implementation
{
    public class EranCoachCorpusRepository : IEranCoachCorpusRepository
    {
        // Namespaces the coach corpus inside the shared DocumentEmbeddings table.
        // Single source of truth lives in EmbeddingTypes so every scoped query agrees.
        public const string Namespace = EmbeddingTypes.EranCoachCorpus;

        private readonly ApplicationDbContext _context;

        public EranCoachCorpusRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<DocumentEmbedding?> GetByCloverIdAsync(string sourceType, string cloverId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(cloverId)) return null;
            var id = cloverId.Trim().ToUpperInvariant();
            return await _context.DocumentEmbeddings
                .Where(d => d.EmbeddingType == Namespace
                            && d.SourceType == sourceType
                            && d.FileName != null
                            && d.FileName.ToUpper() == id)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<List<DocumentEmbedding>> SearchAsync(Vector queryEmbedding, int topK, string? sourceType = null, CancellationToken ct = default)
        {
            var query = _context.DocumentEmbeddings.Where(d => d.EmbeddingType == Namespace);
            if (!string.IsNullOrWhiteSpace(sourceType))
                query = query.Where(d => d.SourceType == sourceType);

            var results = await query
                .Select(d => new { Document = d, Distance = d.Embedding.CosineDistance(queryEmbedding) })
                .OrderBy(x => x.Distance)
                .Take(topK)
                .ToListAsync(ct);

            return results.Select(r => r.Document).ToList();
        }

        public async Task<int> CountAsync(CancellationToken ct = default)
        {
            return await _context.DocumentEmbeddings.CountAsync(d => d.EmbeddingType == Namespace, ct);
        }

        public async Task<int> ReplaceCorpusAsync(List<DocumentEmbedding> rows, CancellationToken ct = default)
        {
            var existing = await _context.DocumentEmbeddings
                .Where(d => d.EmbeddingType == Namespace)
                .ToListAsync(ct);
            if (existing.Count > 0)
                _context.DocumentEmbeddings.RemoveRange(existing);

            if (rows.Count > 0)
                await _context.DocumentEmbeddings.AddRangeAsync(rows, ct);

            await _context.SaveChangesAsync(ct);
            return rows.Count;
        }
    }
}
