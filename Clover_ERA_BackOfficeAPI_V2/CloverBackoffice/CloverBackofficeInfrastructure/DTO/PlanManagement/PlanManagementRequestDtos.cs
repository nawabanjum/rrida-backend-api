using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO.PlanManagement
{
    // Request bodies for api/admin/plans and api/admin/plan-features. Every field is nullable so
    // a missing value is reported as a 400 with a clear message instead of a binder error.

    /// <summary>One manager-count band. MaxManagers null = no upper limit (last tier only).</summary>
    public class PriceTierInputDto
    {
        public int? MinManagers { get; set; }
        public int? MaxManagers { get; set; }
        public decimal? MonthlyPrice { get; set; }
        public decimal? AnnualPrice { get; set; }
    }

    /// <summary>
    /// What a plan gives for one feature. Identify the feature by featureId or featureKey.
    /// isIncluded false = "–". LIMIT features: limitValue null = Unlimited.
    /// </summary>
    public class PlanFeatureValueInputDto
    {
        public int? FeatureId { get; set; }
        public string? FeatureKey { get; set; }
        public bool? IsIncluded { get; set; }
        public int? LimitValue { get; set; }
        public string? DisplayText { get; set; }
        public string? SubText { get; set; }
    }

    public class CreateServicePlanDto
    {
        public string? Code { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? InternalNotes { get; set; }

        /// <summary>Default "manager".</summary>
        public string? PricingUnit { get; set; }

        /// <summary>Default "USD".</summary>
        public string? Currency { get; set; }

        public bool? IsHighlighted { get; set; }
        public bool? AllowsTeamLimitExtension { get; set; }

        /// <summary>Default: after the last plan.</summary>
        public int? SortOrder { get; set; }

        /// <summary>Default false (draft). Active needs at least one price tier.</summary>
        public bool? IsActive { get; set; }

        public List<PriceTierInputDto>? PriceTiers { get; set; }
        public List<PlanFeatureValueInputDto>? Features { get; set; }
    }

    /// <summary>Only the fields sent change; "" clears description / internalNotes.</summary>
    public class UpdateServicePlanDto
    {
        /// <summary>The version last read (required): an older one gets 409.</summary>
        public int? Version { get; set; }

        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? InternalNotes { get; set; }
        public string? PricingUnit { get; set; }
        public string? Currency { get; set; }
        public bool? IsHighlighted { get; set; }
        public bool? AllowsTeamLimitExtension { get; set; }
        public int? SortOrder { get; set; }
    }

    public class SetServicePlanStatusDto
    {
        public bool? IsActive { get; set; }
        public int? Version { get; set; }
        public string? Reason { get; set; }
    }

    public class ReplacePriceTiersDto
    {
        public int? Version { get; set; }
        public List<PriceTierInputDto>? PriceTiers { get; set; }
    }

    /// <summary>Sets the cells sent; features not sent keep their current value.</summary>
    public class SetPlanFeaturesDto
    {
        public int? Version { get; set; }
        public List<PlanFeatureValueInputDto>? Features { get; set; }
    }

    public class DuplicateServicePlanDto
    {
        public string? Code { get; set; }
        public string? Name { get; set; }
    }

    /// <summary>Ids in the new display order; every id must be listed exactly once.</summary>
    public class ReorderDto
    {
        public List<int>? Ids { get; set; }
    }

    public class CreatePlanFeatureDto
    {
        public string? Key { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }

        /// <summary>INCLUDED, LIMIT or TEXT.</summary>
        public string? ValueType { get; set; }

        public string? LimitUnit { get; set; }

        /// <summary>LIMIT only: NONE (default) or MONTH.</summary>
        public string? LimitPeriod { get; set; }

        public string? Footnote { get; set; }
        public int? SortOrder { get; set; }

        /// <summary>Default true: a row of the comparison table.</summary>
        public bool? ShowInComparison { get; set; }
    }

    /// <summary>Only the fields sent change; "" clears description / limitUnit / footnote. Key never changes.</summary>
    public class UpdatePlanFeatureDto
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? ValueType { get; set; }
        public string? LimitUnit { get; set; }
        public string? LimitPeriod { get; set; }
        public string? Footnote { get; set; }
        public int? SortOrder { get; set; }
        public bool? ShowInComparison { get; set; }
    }

    public class SetPlanFeatureStatusDto
    {
        public bool? IsActive { get; set; }
        public string? Reason { get; set; }
    }

    public class ServicePlanListQueryDto
    {
        /// <summary>all (default), active or inactive.</summary>
        public string? Status { get; set; }
    }
}
