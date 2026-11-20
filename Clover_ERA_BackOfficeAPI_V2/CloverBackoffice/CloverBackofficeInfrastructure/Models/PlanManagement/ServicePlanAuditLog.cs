using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models.PlanManagement
{
    /// <summary>
    /// Who changed what in the plan catalogue and when, with the state before and after as JSON.
    /// Plan changes carry <see cref="ServicePlanId"/>; feature-catalogue changes carry
    /// <see cref="PlanFeatureId"/>. Append-only.
    /// </summary>
    [Table("ServicePlanAuditLogs", Schema = "public")]
    public class ServicePlanAuditLog
    {
        public const int ReasonMaxLength = 500;

        public long Id { get; init; }

        public int? ServicePlanId { get; set; }

        public int? PlanFeatureId { get; set; }

        /// <summary><see cref="ServicePlanAuditActions"/>.</summary>
        [MaxLength(40)]
        public required string Action { get; set; }

        public int ActorUserId { get; set; }

        [MaxLength(ReasonMaxLength)]
        public string? Reason { get; set; }

        [Column(TypeName = "jsonb")]
        public string? Before { get; set; }

        [Column(TypeName = "jsonb")]
        public string? After { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    public static class ServicePlanAuditActions
    {
        public const string PlanCreated = "PLAN_CREATED";
        public const string PlanUpdated = "PLAN_UPDATED";
        public const string PlanActivated = "PLAN_ACTIVATED";
        public const string PlanDeactivated = "PLAN_DEACTIVATED";
        public const string PriceTiersReplaced = "PRICE_TIERS_REPLACED";
        public const string FeaturesUpdated = "FEATURES_UPDATED";
        public const string PlansReordered = "PLANS_REORDERED";
        public const string FeatureCreated = "FEATURE_CREATED";
        public const string FeatureUpdated = "FEATURE_UPDATED";
        public const string FeatureActivated = "FEATURE_ACTIVATED";
        public const string FeatureDeactivated = "FEATURE_DEACTIVATED";
        public const string FeaturesReordered = "FEATURES_REORDERED";
        public const string LicencesMigrated = "LICENCES_MIGRATED";
    }
}
