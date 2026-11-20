using CloverBackoffice.Infrastructure.DTO.PlanManagement;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>Customer-facing plan reads (api/plans): active plans and price quotes.</summary>
    public interface IPlanCatalogService
    {
        Task<PlanComparisonDto> GetActivePlansAsync(CancellationToken ct = default);

        Task<PlanManagementResult<PlanPriceQuoteDto>> QuoteAsync(int? planId, string? planCode, int? quantity, string? billingCycle, CancellationToken ct = default);
    }
}
