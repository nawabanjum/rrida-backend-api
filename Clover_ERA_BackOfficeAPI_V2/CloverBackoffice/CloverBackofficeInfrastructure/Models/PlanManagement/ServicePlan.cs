using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models.PlanManagement
{
    /// <summary>
    /// A plan a customer can buy, e.g. Light or Pro: its details, its per-manager price tiers
    /// (<see cref="ServicePlanPriceTier"/>) and what it includes (<see cref="ServicePlanFeature"/>).
    /// Managed by a RRIDA Admin through api/admin/plans.
    ///
    /// Separate from the legacy <c>Plans</c> table, which registration and the Stripe flows still
    /// read; that table is untouched. Licences will point at this table in a later phase.
    ///
    /// Plans are never deleted, only deactivated: an inactive plan cannot be bought, but licences
    /// already on it keep working.
    /// </summary>
    [Table("ServicePlans", Schema = "public")]
    public class ServicePlan
    {
        public const int CodeMaxLength = 30;
        public const int NameMaxLength = 100;
        public const int DescriptionMaxLength = 1000;
        public const int InternalNotesMaxLength = 4000;
        public const int PricingUnitMaxLength = 50;

        public int Id { get; init; }

        /// <summary>Stable lowercase id, e.g. "light", "pro". Unique; cannot change once created.</summary>
        [MaxLength(CodeMaxLength)]
        public required string Code { get; set; }

        [MaxLength(NameMaxLength)]
        public required string Name { get; set; }

        /// <summary>Customer-facing description of the plan.</summary>
        [MaxLength(DescriptionMaxLength)]
        public string? Description { get; set; }

        /// <summary>Admin-only comments about the plan; never shown to customers.</summary>
        [MaxLength(InternalNotesMaxLength)]
        public string? InternalNotes { get; set; }

        /// <summary>What one unit of the price buys, e.g. "manager" → "$97 per manager per month".</summary>
        [MaxLength(PricingUnitMaxLength)]
        public required string PricingUnit { get; set; }

        /// <summary>ISO 4217, e.g. "USD".</summary>
        [MaxLength(3)]
        public required string Currency { get; set; }

        /// <summary>Shown as the emphasised column on the comparison table (Pro).</summary>
        public bool IsHighlighted { get; set; }

        /// <summary>
        /// Whether a RRIDA Admin may raise a holder's team capacity above the plan's limit
        /// (Pro yes, Light no). Read by the licence phase.
        /// </summary>
        public bool AllowsTeamLimitExtension { get; set; }

        public int SortOrder { get; set; }

        public bool IsActive { get; set; }

        /// <summary>Bumped on every change; a write carrying an older value gets 409.</summary>
        public int Version { get; set; }

        public int CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public virtual ICollection<ServicePlanPriceTier> PriceTiers { get; set; } = new List<ServicePlanPriceTier>();
        public virtual ICollection<ServicePlanFeature> Features { get; set; } = new List<ServicePlanFeature>();
    }
}
