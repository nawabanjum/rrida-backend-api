using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    public class CompanySubscription
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public uint ProfileId { get; set; }

        public int? PlanId { get; set; }

        // The plan catalogue plan (ServicePlans) this subscription was bought for. Null on
        // subscriptions created before plans existed. Independent of the legacy PlanId.
        public int? ServicePlanId { get; set; }

        // The plan version (terms) bought; stamped on the licences minted for it.
        public int? ServicePlanVersionId { get; set; }

        // Price per manager the plan checkout charged (the tier for the quantity bought), for
        // BillingCycle. Null for subscriptions bought before plans or without one.
        [System.ComponentModel.DataAnnotations.Schema.Column(TypeName = "numeric(10,2)")]
        public decimal? ServicePlanUnitPrice { get; set; }

        public BillingCycle BillingCycle { get; set; }

        public SubscriptionStatus Status { get; set; }

        [MaxLength(100)]
        public string? StripeCustomerId { get; set; }

        [MaxLength(100)]
        public string? StripeSubscriptionId { get; set; }

        // Stripe Product ID (e.g. prod_UbutUvwjA3w8cB) for the product this
        // subscription maps to in the Stripe Product catalog.
        [MaxLength(100)]
        public string? StripeProductId { get; set; }

        public int TeamCount { get; set; } = 1;

        public int MaxTeamsAllowed { get; set; }

        public int MaxPeopleAllowed { get; set; }              // MaxTeamsAllowed × 20

        public DateTime? TrialStartDate { get; set; }

        public DateTime? TrialEndDate { get; set; }

        public DateTime CurrentPeriodStart { get; set; }

        public DateTime CurrentPeriodEnd { get; set; }

        public bool IsTrialUsed { get; set; } = false;

        public DateTime? CanceledAt { get; set; }

        public bool CancelAtPeriodEnd { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public int? PromoCodeId { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal? LifetimeDiscountPercent { get; set; }  // persisted from lifetime promo code

        // Manual / admin-applied discount percent. Separate column from
        // LifetimeDiscountPercent because that one is reserved for promo-code
        // flows and gets cleared/re-set by Stripe handlers.
        [Column(TypeName = "decimal(5,2)")]
        public decimal? Discount { get; set; }

        // Extra days appended to CurrentPeriodEnd / TrialEndDate when computing
        // expiry. ComputeExpiry adds this on top of the canonical period-end,
        // so it propagates to every read path (status, detail, days-remaining).
        public int? GracePeriod { get; set; }

        // Idempotency gate for SubscriptionLifecycleJob warning notifications.
        // Stores the daysRemaining bucket of the last warning that was sent
        // ({3,2,1}). The job only sends a new warning when daysRemaining is
        // strictly less than this value, so each stage fires once even though
        // the job ticks hourly. Reset to NULL on reactivation.
        public int? LastWarningDaysSent { get; set; }

        public bool IsDel { get; set; } = false;

        // Navigation properties
        [ForeignKey(nameof(ProfileId))]
        public virtual Profile? Profile { get; set; }

        [ForeignKey("PlanId")]
        public virtual Plan? Plan { get; set; }

        [ForeignKey("PromoCodeId")]
        public virtual PromoCode? PromoCode { get; set; }

        public virtual ICollection<Payment>? Payments { get; set; }
    }
}
