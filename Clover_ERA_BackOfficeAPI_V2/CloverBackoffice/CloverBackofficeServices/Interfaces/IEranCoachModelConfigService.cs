using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Central model configuration for the coach (Req 1, Option A). Views/sets the active
    /// Search-type ModelConfiguration the coach runs on. Admin-gated at the controller.
    /// </summary>
    public interface IEranCoachModelConfigService
    {
        /// <summary>The active coach model + settings, or null if none is configured.</summary>
        Task<EranCoachModelConfigDto?> GetActiveAsync(CancellationToken ct = default);

        /// <summary>All models the coach can be pointed at.</summary>
        Task<List<EranCoachModelOptionDto>> GetModelsAsync(CancellationToken ct = default);

        /// <summary>Sets the active coach model + optional settings; null if the model is invalid.</summary>
        Task<EranCoachModelConfigDto?> SetActiveAsync(UpdateEranCoachModelConfigDto request, string? modifiedBy, CancellationToken ct = default);
    }
}
