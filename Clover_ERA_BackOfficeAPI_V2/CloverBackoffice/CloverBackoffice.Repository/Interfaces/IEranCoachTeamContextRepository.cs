using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Repository.Interfaces
{
    /// <summary>
    /// Reads a business unit's latest CLOVER-EQ engagement report for EranCoach grounding.
    /// Own repository so the coach stays separate from the existing insights code.
    /// </summary>
    public interface IEranCoachTeamContextRepository
    {
        Task<EngagementInsights?> GetLatestByUnitAsync(uint unitId, CancellationToken ct = default);
    }
}
