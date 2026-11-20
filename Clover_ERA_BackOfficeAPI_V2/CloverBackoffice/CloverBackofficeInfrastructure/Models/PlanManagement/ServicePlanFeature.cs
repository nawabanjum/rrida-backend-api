using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models.PlanManagement
{
    /// <summary>
    /// One cell of the comparison table: what a plan gives for a feature. One row per plan and
    /// feature; a plan with no row for a feature does not include it (–).
    ///
    ///   INCLUDED  IsIncluded only                        ✓ / –
    ///   LIMIT     IsIncluded + LimitValue (null=Unlimited) "10 chats" / "Unlimited"
    ///   TEXT      IsIncluded + DisplayText               "First reading walk-through"
    ///
    /// DisplayText / SubText override what is shown ("Up to 3", "per month", "*with Clover
    /// Resources"); <see cref="LimitValue"/> is what code will enforce.
    /// </summary>
    [Table("ServicePlanFeatures", Schema = "public")]
    public class ServicePlanFeature
    {
        public const int DisplayTextMaxLength = 150;
        public const int SubTextMaxLength = 200;

        public int Id { get; init; }

        [ForeignKey(nameof(ServicePlan))]
        public int ServicePlanId { get; set; }

        public virtual ServicePlan? ServicePlan { get; set; }

        [ForeignKey(nameof(PlanFeature))]
        public int PlanFeatureId { get; set; }

        public virtual PlanFeature? PlanFeature { get; set; }

        public bool IsIncluded { get; set; }

        /// <summary>LIMIT features only; null = Unlimited.</summary>
        public int? LimitValue { get; set; }

        [MaxLength(DisplayTextMaxLength)]
        public string? DisplayText { get; set; }

        [MaxLength(SubTextMaxLength)]
        public string? SubText { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
