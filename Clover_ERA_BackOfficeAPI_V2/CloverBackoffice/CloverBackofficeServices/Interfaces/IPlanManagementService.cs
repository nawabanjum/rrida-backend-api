using CloverBackoffice.Infrastructure.DTO.PlanManagement;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// The plan catalogue a RRIDA Admin manages (api/admin/plans, api/admin/plan-features):
    /// plans, their per-manager price tiers, the feature rows and each plan's value for them.
    /// Every call is RRIDA Admin only (Forbidden otherwise).
    /// </summary>
    public interface IPlanManagementService
    {
        // ── Plans ──
        Task<PlanManagementResult<ServicePlanListDto>> ListPlansAsync(ServicePlanListQueryDto? query, int actorUserId, CancellationToken ct = default);
        Task<PlanManagementResult<ServicePlanDetailDto>> GetPlanAsync(int planId, int actorUserId, CancellationToken ct = default);
        Task<PlanManagementResult<ServicePlanDetailDto>> CreatePlanAsync(CreateServicePlanDto? dto, int actorUserId, CancellationToken ct = default);
        Task<PlanManagementResult<ServicePlanDetailDto>> UpdatePlanAsync(int planId, UpdateServicePlanDto? dto, int actorUserId, CancellationToken ct = default);
        Task<PlanManagementResult<ServicePlanDetailDto>> SetPlanStatusAsync(int planId, SetServicePlanStatusDto? dto, int actorUserId, CancellationToken ct = default);
        Task<PlanManagementResult<ServicePlanDetailDto>> ReplacePriceTiersAsync(int planId, ReplacePriceTiersDto? dto, int actorUserId, CancellationToken ct = default);
        Task<PlanManagementResult<ServicePlanDetailDto>> SetPlanFeaturesAsync(int planId, SetPlanFeaturesDto? dto, int actorUserId, CancellationToken ct = default);
        Task<PlanManagementResult<ServicePlanDetailDto>> DuplicatePlanAsync(int planId, DuplicateServicePlanDto? dto, int actorUserId, CancellationToken ct = default);
        Task<PlanManagementResult<ServicePlanListDto>> ReorderPlansAsync(ReorderDto? dto, int actorUserId, CancellationToken ct = default);
        Task<PlanManagementResult<PlanAuditListDto>> GetPlanHistoryAsync(int planId, int? take, int actorUserId, CancellationToken ct = default);
        Task<PlanManagementResult<PlanComparisonDto>> GetComparisonAsync(bool includeInactive, int actorUserId, CancellationToken ct = default);

        // ── Versions (existing customers keep the terms they bought) ──
        Task<PlanManagementResult<PlanVersionListDto>> GetPlanVersionsAsync(int planId, int actorUserId, CancellationToken ct = default);
        Task<PlanManagementResult<PlanMigrationResultDto>> MigrateLicencesAsync(int planId, MigratePlanLicencesDto? dto, int actorUserId, CancellationToken ct = default);

        // ── Feature catalogue ──
        Task<PlanManagementResult<PlanFeatureListDto>> ListFeaturesAsync(string? status, int actorUserId, CancellationToken ct = default);
        Task<PlanManagementResult<PlanFeatureDto>> GetFeatureAsync(int featureId, int actorUserId, CancellationToken ct = default);
        Task<PlanManagementResult<PlanFeatureDto>> CreateFeatureAsync(CreatePlanFeatureDto? dto, int actorUserId, CancellationToken ct = default);
        Task<PlanManagementResult<PlanFeatureDto>> UpdateFeatureAsync(int featureId, UpdatePlanFeatureDto? dto, int actorUserId, CancellationToken ct = default);
        Task<PlanManagementResult<PlanFeatureDto>> SetFeatureStatusAsync(int featureId, SetPlanFeatureStatusDto? dto, int actorUserId, CancellationToken ct = default);
        Task<PlanManagementResult<PlanFeatureListDto>> ReorderFeaturesAsync(ReorderDto? dto, int actorUserId, CancellationToken ct = default);
        Task<PlanManagementResult<PlanAuditListDto>> GetFeatureHistoryAsync(int featureId, int? take, int actorUserId, CancellationToken ct = default);
    }
}
