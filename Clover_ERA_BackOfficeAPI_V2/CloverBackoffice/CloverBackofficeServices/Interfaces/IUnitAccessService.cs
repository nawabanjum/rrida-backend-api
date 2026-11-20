using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Single source of truth for "which business units may this user see", by role.
    /// RridaAdmin = every unit; ProfileAdmin = their profile's units;
    /// TeamManager = assigned units plus all descendants;
    /// PortfolioManager = none yet, pending its permission spec.
    /// Shared by the aggregate dashboard and the EranCoach report tools so scoping stays
    /// consistent. Extracted verbatim from AggregateDashboardService.GetAccessibleUnitIds.
    /// </summary>
    public interface IUnitAccessService
    {
        /// <summary>
        /// The unit ids this user can access. Throws <see cref="System.UnauthorizedAccessException"/>
        /// for roles with no unit scope (e.g. Customer), matching the original dashboard behaviour;
        /// callers that want a soft result should catch it.
        /// </summary>
        Task<List<long>> GetAccessibleUnitIdsAsync(User user, CancellationToken ct = default);
    }
}
