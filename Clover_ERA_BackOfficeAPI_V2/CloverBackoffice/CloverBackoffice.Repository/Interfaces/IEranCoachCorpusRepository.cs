using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;
using Pgvector;

namespace CloverBackoffice.Repository.Interfaces
{
    /// <summary>
    /// Retrieval over the EranCoach CLOVER corpus. Reuses the shared DocumentEmbedding
    /// (pgvector) table but scopes every query to the coach's own namespace, so the
    /// corpus never mixes with other embeddings. Own repository to keep the coach separate.
    /// </summary>
    public interface IEranCoachCorpusRepository
    {
        /// <summary>Exact lookup by CLOVER id (e.g. "C1", "MA-C-061") within one source type.</summary>
        Task<DocumentEmbedding?> GetByCloverIdAsync(string sourceType, string cloverId, CancellationToken ct = default);

        /// <summary>Cosine-nearest corpus rows to the query vector, optionally limited to one source type.</summary>
        Task<List<DocumentEmbedding>> SearchAsync(Vector queryEmbedding, int topK, string? sourceType = null, CancellationToken ct = default);

        /// <summary>Rows currently seeded in the coach namespace.</summary>
        Task<int> CountAsync(CancellationToken ct = default);

        /// <summary>Idempotent reseed: delete the whole coach namespace, then insert the given rows.</summary>
        Task<int> ReplaceCorpusAsync(List<DocumentEmbedding> rows, CancellationToken ct = default);
    }
}
