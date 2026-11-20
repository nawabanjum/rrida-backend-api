using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Repository.Interfaces
{
    /// <summary>Reads/writes a manager's rolling EranCoach memory (one row per manager per team).</summary>
    public interface IEranCoachMemoryRepository
    {
        Task<EranCoachManagerMemory?> GetByManagerAsync(int managerUserId, long? businessUnitId, CancellationToken ct = default);
        Task UpsertAsync(int managerUserId, long? businessUnitId, string summary, CancellationToken ct = default);
    }
}
