using System.Threading;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// One-time (idempotent) seeding of the CLOVER corpus into the pgvector store for
    /// EranCoach retrieval. Reads the committed bundle, embeds each worksheet /
    /// micro-action / book chunk, and replaces the coach namespace wholesale.
    /// </summary>
    public interface IEranCoachSeedService
    {
        Task<int> SeedAsync(CancellationToken ct = default);
        Task<int> CountAsync(CancellationToken ct = default);
    }
}
