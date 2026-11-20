using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Per-manager, per-team rolling memory for EranCoach. Read at the start of a session and injected
    /// into the system prompt; written after a conversation with a cheap summary pass. Mirrors
    /// the reference getManagerMemory / saveManagerMemory hooks.
    /// </summary>
    public interface IEranCoachMemoryService
    {
        /// <summary>The manager's rolling memory for this team (null team = no team selected), or null if none yet.</summary>
        Task<string?> GetAsync(int managerUserId, long? businessUnitId, CancellationToken ct = default);

        /// <summary>Fold the latest exchange into the manager's rolling memory (LLM summary + upsert).</summary>
        Task SaveAsync(int managerUserId, long? businessUnitId, string latestUserMessage, string latestAnswer, ModelConfiguration model, CancellationToken ct = default);
    }
}
