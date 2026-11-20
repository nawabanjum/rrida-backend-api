using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Repository.Interfaces
{
    /// <summary>
    /// Reads/updates the coach's central model configuration (Req 1, Option A). The coach's
    /// selectable models are the Search-type ModelConfiguration rows (ModelConfigTypeId == 1);
    /// the active one is flagged via IsSelected. No new table — reuses ModelConfiguration.
    /// </summary>
    public interface IEranCoachModelConfigRepository
    {
        /// <summary>All non-deleted Search-type models the coach can be pointed at.</summary>
        Task<List<ModelConfiguration>> GetSelectableAsync(CancellationToken ct = default);

        /// <summary>The active coach model: IsSelected, else IsDefault, else the most recent.</summary>
        Task<ModelConfiguration?> GetActiveAsync(CancellationToken ct = default);

        /// <summary>
        /// Makes <paramref name="modelId"/> the active coach model (clearing IsSelected on the
        /// other Search models) and applies any supplied generation settings. Returns the model,
        /// or null if it is not a selectable Search-type model.
        /// </summary>
        Task<ModelConfiguration?> SetActiveAsync(int modelId, double? temperature, double? topP, int? maxTokens, string? modifiedBy, CancellationToken ct = default);
    }
}
