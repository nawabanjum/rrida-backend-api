using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// The manager-of-managers roll-up: each child team's state and cycle status, never its
    /// action, and the cross-team patterns that are the senior manager's own to act on.
    /// </summary>
    public interface IEngineRollUpService
    {
        Task<EngineRollUpDto> GetRollUpAsync(User actor, CancellationToken ct = default);
    }
}
