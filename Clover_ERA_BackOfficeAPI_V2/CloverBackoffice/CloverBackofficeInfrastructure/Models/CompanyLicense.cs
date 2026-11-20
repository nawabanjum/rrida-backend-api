using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    // One row per license slot owned by a Company. Created at company-creation
    // time (count = NumberOfTeams the plan grants). A license is assignable to
    // exactly one BusinessUnit at a time via AssignedCompanyLicenses; that link
    // table also preserves the reassignment history.
    public class CompanyLicense
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public uint ProfileId { get; set; }

        // Format: CLD-{TRI|PRO}{NNN}-{ProfileId}. Unique across the table.
        [MaxLength(64)]
        public required string LicenseKey { get; set; }

        // "Trial" or "Paid" — drives the prefix used when the key was minted.
        [MaxLength(20)]
        public required string LicenseType { get; set; }

        // Optional links to subscription/plan so a license can be traced to the
        // pricing tier that created it. Both nullable to allow imports / manual rows.
        public int? CompanySubscriptionId { get; set; }
        public int? PlanId { get; set; }

        // The plan catalogue plan (ServicePlans: Light, Pro, ...) this licence gives its
        // Team Manager: features, limits, team capacity. Null on licences minted before
        // plans existed; PlanEntitlementService reads those as the legacy plan (Pro).
        // Independent of the legacy PlanId above.
        public int? ServicePlanId { get; set; }

        // The plan version (terms) this licence was bought on; later plan edits don't change it
        // until an admin migrates it. Null = version 1 of its plan (licences from before versions).
        public int? ServicePlanVersionId { get; set; }

        // Each license owns its individual lifecycle. ExpiryDate drives whether
        // the license is live — it is INDEPENDENT of the parent subscription's
        // CurrentPeriodEnd. A license keeps working until its own ExpiryDate is
        // reached even if the company subscription has lapsed. Trial licenses
        // get the trial-window end; paid licenses get now + billing cycle
        // (monthly → +1 month, annual → +1 year) at the time they are minted.
        // Nullable so legacy/imported rows without a date never auto-expire.
        public DateTime? ExpiryDate { get; set; }

        // Optional free-text remark attached when a Clovera admin extends the
        // license expiry (see /api/subscription/extend-license). Null for
        // licenses that have never been manually extended.
        [MaxLength(1000)]
        public string? Notes { get; set; }

        // Notification bookkeeping for SubscriptionLifecycleJob. The license row
        // itself records whether the expiry / reactivation notification pair has
        // already gone out, so notifications follow the LICENSE's state rather
        // than "who flipped IsDel first". Read-path reconciliation
        // (SubscriptionService.ReconcileLicenseExpiryAsync) mutates IsDel on
        // every subscription page load; before these columns existed that race
        // permanently suppressed the job's notification, because the job only
        // considered licenses it had expired itself.
        //
        // Stamped when the expiry notification is dispatched; cleared on
        // reactivation so a later expiry notifies again.
        public DateTime? ExpiryNotifiedAt { get; set; }

        // Stamped when the reactivation notification is dispatched; cleared when
        // the license expires again. Only ever set for a license that was
        // previously announced as expired.
        public DateTime? ReactivationNotifiedAt { get; set; }

        // Per-licence end-of-period cancellation (POST
        // /api/license-assignments/cancel). True once this seat has been taken
        // out of its Stripe subscription's quantity: it stays live until its own
        // ExpiryDate, but renewals no longer extend it, so it lapses then. A
        // cancellation that ends the WHOLE subscription is recorded on
        // CompanySubscription.CancelAtPeriodEnd instead.
        public bool CancelAtPeriodEnd { get; set; } = false;

        // When the per-licence cancellation was requested. Null while renewing.
        public DateTime? CanceledAt { get; set; }

        // The Team Manager holding this licence (1 licence = 1 Team Manager),
        // with or without a team yet: the registering Profile Admin's trial
        // licence, a licence auto-assigned when a Team Manager is created, or one
        // assigned by managerId. Counts as consumed only while that user is still
        // a live Team Manager of the profile; otherwise the licence is free again.
        public int? AssignedUserId { get; set; }

        // When the licence was given to AssignedUserId.
        public DateTime? AssignedUserAt { get; set; }

        // Set when this TRIAL licence was ended early because its Team Manager
        // was given a purchased licence (POST /api/license-assignments Assign by
        // managerId). A terminated row is also expired (ExpiryDate = TerminatedAt,
        // IsDel = true) and is never revived: expiry reconciliation, renewal,
        // trial extension, extend-license and the post-expiry trial conversion
        // all skip it.
        public DateTime? TerminatedAt { get; set; }

        // The purchased licence that replaced this terminated trial licence.
        public int? ReplacedByLicenseId { get; set; }

        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDel { get; set; } = false;

        [ForeignKey(nameof(ProfileId))]
        public virtual Profile? Profile { get; set; }

        [ForeignKey("CompanySubscriptionId")]
        public virtual CompanySubscription? CompanySubscription { get; set; }

        [ForeignKey("PlanId")]
        public virtual Plan? Plan { get; set; }

        public virtual ICollection<AssignedCompanyLicense>? Assignments { get; set; }
    }
}
