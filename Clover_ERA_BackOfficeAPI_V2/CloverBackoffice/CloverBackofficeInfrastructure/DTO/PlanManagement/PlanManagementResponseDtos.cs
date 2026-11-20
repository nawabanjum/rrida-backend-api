using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO.PlanManagement
{
    public enum PlanManagementError
    {
        None = 0,
        BadRequest,
        NotFound,
        Forbidden,

        /// <summary>The plan changed since the caller read it, or a code/key is taken (409).</summary>
        Conflict
    }

    public class PlanManagementResult<T>
    {
        public T? Data { get; init; }
        public PlanManagementError Error { get; init; }
        public string Message { get; init; } = string.Empty;

        public bool IsSuccess => Error == PlanManagementError.None;

        public static PlanManagementResult<T> Ok(T data) => new() { Data = data };

        public static PlanManagementResult<T> Fail(PlanManagementError error, string message) =>
            new() { Error = error, Message = message };
    }

    public class PriceTierDto
    {
        public int Id { get; set; }
        public int MinManagers { get; set; }

        /// <summary>Null = no upper limit.</summary>
        public int? MaxManagers { get; set; }

        public decimal MonthlyPrice { get; set; }
        public decimal AnnualPrice { get; set; }

        /// <summary>"1 to 4 managers", "10+ managers".</summary>
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>A feature row with this plan's value for it.</summary>
    public class PlanFeatureValueDto
    {
        public int FeatureId { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string ValueType { get; set; } = string.Empty;
        public string? LimitUnit { get; set; }
        public string? LimitPeriod { get; set; }
        public bool FeatureIsActive { get; set; }

        /// <summary>False when the plan has no value stored for the feature yet (shown as "–").</summary>
        public bool IsConfigured { get; set; }

        public bool IsIncluded { get; set; }

        /// <summary>LIMIT only; null with isIncluded = Unlimited.</summary>
        public int? LimitValue { get; set; }

        public bool IsUnlimited { get; set; }
        public string? DisplayText { get; set; }
        public string? SubText { get; set; }

        /// <summary>What the table shows: displayText, else "10" / "Unlimited" for limits, null for ✓ / –.</summary>
        public string? Display { get; set; }
    }

    public class ServicePlanSummaryDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string PricingUnit { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public bool IsHighlighted { get; set; }
        public bool AllowsTeamLimitExtension { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }

        /// <summary>First tier's price (the "from" price); null when no tiers yet.</summary>
        public decimal? StartingMonthlyPrice { get; set; }
        public decimal? StartingAnnualPrice { get; set; }

        public int PriceTierCount { get; set; }
        public int IncludedFeatureCount { get; set; }
        public int Version { get; set; }

        /// <summary>Newest terms version (what new purchases get); 0 before the first.</summary>
        public int CurrentVersionNumber { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ServicePlanDetailDto : ServicePlanSummaryDto
    {
        public string? InternalNotes { get; set; }

        /// <summary>"$97 per manager per month, or $970 a year".</summary>
        public string? PriceSummary { get; set; }

        public List<PriceTierDto> PriceTiers { get; set; } = new();

        /// <summary>Every active feature row (plus inactive ones this plan still has a value for).</summary>
        public List<PlanFeatureValueDto> Features { get; set; } = new();

        public int CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ServicePlanListDto
    {
        public int Total { get; set; }
        public List<ServicePlanSummaryDto> Plans { get; set; } = new();
    }

    public class PlanFeatureDto
    {
        public int Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string ValueType { get; set; } = string.Empty;
        public string? LimitUnit { get; set; }
        public string? LimitPeriod { get; set; }
        public string? Footnote { get; set; }
        public bool IsSystem { get; set; }
        public bool IsActive { get; set; }
        public bool ShowInComparison { get; set; }
        public int SortOrder { get; set; }

        /// <summary>Plans that include this feature.</summary>
        public int IncludedInPlans { get; set; }

        public DateTime UpdatedAt { get; set; }
    }

    public class PlanFeatureListDto
    {
        public int Total { get; set; }
        public List<PlanFeatureDto> Features { get; set; } = new();
    }

    /// <summary>The plan comparison table ("What you get on each plan").</summary>
    public class PlanComparisonDto
    {
        public List<PlanComparisonColumnDto> Plans { get; set; } = new();
        public List<PlanComparisonRowDto> Rows { get; set; } = new();
        public List<string> Footnotes { get; set; } = new();
    }

    public class PlanComparisonColumnDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsHighlighted { get; set; }
        public bool IsActive { get; set; }
        public string PricingUnit { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public string? PriceSummary { get; set; }
        public List<PriceTierDto> PriceTiers { get; set; } = new();
    }

    public class PlanComparisonRowDto
    {
        public int FeatureId { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string ValueType { get; set; } = string.Empty;

        /// <summary>One cell per plan, in the same order as plans[].</summary>
        public List<PlanComparisonCellDto> Cells { get; set; } = new();
    }

    public class PlanComparisonCellDto
    {
        public int PlanId { get; set; }
        public bool IsIncluded { get; set; }
        public int? LimitValue { get; set; }
        public bool IsUnlimited { get; set; }
        public string? Display { get; set; }
        public string? SubText { get; set; }
    }

    public class PlanAuditEntryDto
    {
        public long Id { get; set; }
        public int? PlanId { get; set; }
        public int? FeatureId { get; set; }
        public string Action { get; set; } = string.Empty;
        public int ActorUserId { get; set; }
        public string? ActorName { get; set; }
        public string? Reason { get; set; }
        public string? Before { get; set; }
        public string? After { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class PlanAuditListDto
    {
        public int Total { get; set; }
        public List<PlanAuditEntryDto> Entries { get; set; } = new();
    }
}
