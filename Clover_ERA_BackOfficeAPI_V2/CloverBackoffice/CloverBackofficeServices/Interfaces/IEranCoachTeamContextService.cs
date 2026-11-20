using System.Threading;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Assembles the "THIS MANAGER'S TEAM" grounding block from a unit's latest CLOVER-EQ
    /// engagement report. Cached briefly per unit. Returns null when the unit has no report.
    /// The full report stays server-side (injected straight into the model).
    /// </summary>
    public interface IEranCoachTeamContextService
    {
        Task<string?> GetTeamContextTextAsync(uint unitId, CancellationToken ct = default);
    }
}
