using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO.PlanManagement
{
    /// <summary>GET api/plans/price: what buying N managers on a plan costs.</summary>
    public class PlanPriceQuoteDto
    {
        public int PlanId { get; set; }
        public string PlanCode { get; set; } = string.Empty;
        public string PlanName { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public string PricingUnit { get; set; } = string.Empty;

        /// <summary>"monthly" or "annual".</summary>
        public string BillingCycle { get; set; } = string.Empty;

        public int Quantity { get; set; }

        /// <summary>The tier the quantity falls in.</summary>
        public PriceTierDto Tier { get; set; } = new();

        /// <summary>Per manager, for the billing cycle.</summary>
        public decimal UnitPrice { get; set; }

        /// <summary>UnitPrice × Quantity, for the billing cycle.</summary>
        public decimal TotalPrice { get; set; }

        /// <summary>Annual only: 12 × monthly − annual, for the whole quantity (0 for monthly).</summary>
        public decimal AnnualSavings { get; set; }

        /// <summary>"3 managers × $695 per month = $2,085 per month".</summary>
        public string Summary { get; set; } = string.Empty;
    }

    /// <summary>What a user gets from their plan right now (GET api/entitlements/me).</summary>
    public class PlanEntitlementsDto
    {
        public int UserId { get; set; }
        public int? ProfileId { get; set; }

        /// <summary>
        /// True when the plan decides what the user may use: they act as a Team Manager.
        /// RRIDA Admin, Profile Admin and Portfolio Manager without the Team Manager role are
        /// never limited by a plan (false, every feature allowed).
        /// </summary>
        public bool IsPlanGated { get; set; }

        /// <summary>The live licence the plan comes from; null when the manager has none.</summary>
        public int? LicenseId { get; set; }
        public string? LicenseKey { get; set; }
        public string? LicenseType { get; set; }
        public DateTime? LicenseExpiryDate { get; set; }

        /// <summary>HELD (assigned to the user) or TEAM (covers one of their teams).</summary>
        public string? LicenseSource { get; set; }

        public int? PlanId { get; set; }
        public string? PlanCode { get; set; }
        public string? PlanName { get; set; }

        /// <summary>The plan version (terms) the licence was bought on.</summary>
        public int PlanVersionNumber { get; set; }

        /// <summary>True when the licence predates plans and is read as the legacy plan.</summary>
        public bool IsLegacyPlanDefault { get; set; }

        public TeamCapacityDto TeamCapacity { get; set; } = new();

        public List<FeatureEntitlementDto> Features { get; set; } = new();

        /// <summary>UTC start of the month the monthly usage counts from.</summary>
        public DateTime UsagePeriodStart { get; set; }
    }

    public class FeatureEntitlementDto
    {
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ValueType { get; set; } = string.Empty;

        /// <summary>Whether the user may use the feature at all.</summary>
        public bool IsEnabled { get; set; }

        /// <summary>LIMIT features: the limit; null = Unlimited (when enabled).</summary>
        public int? Limit { get; set; }
        public bool IsUnlimited { get; set; }
        public string? LimitUnit { get; set; }
        public string? LimitPeriod { get; set; }

        /// <summary>LIMIT features with a usage counter (Eran chats / conversations, teams).</summary>
        public int? Used { get; set; }

        /// <summary>Limit − Used, never below 0; null when unlimited or not counted.</summary>
        public int? Remaining { get; set; }

        /// <summary>Plan's display text, e.g. "First reading walk-through".</summary>
        public string? Display { get; set; }
        public string? SubText { get; set; }
    }

    public class TeamCapacityDto
    {
        /// <summary>The plan's team limit (MANAGERS_AND_TEAMS); null = unlimited.</summary>
        public int? DefaultCapacity { get; set; }

        /// <summary>Extra teams a RRIDA Admin added (profile-wide + this licence).</summary>
        public int ExtendedCapacity { get; set; }

        /// <summary>Default + extended; null = unlimited.</summary>
        public int? TotalCapacity { get; set; }

        public int TeamsUsed { get; set; }

        /// <summary>Null = unlimited.</summary>
        public int? Remaining { get; set; }

        public bool CanBeExtended { get; set; }

        /// <summary>
        /// The manager runs more teams than the plan allows (e.g. moved to Light with 3 teams).
        /// Existing teams are kept; no new team can be added until under the limit.
        /// </summary>
        public bool IsOverLimit { get; set; }
        public string? Message { get; set; }
    }
}
