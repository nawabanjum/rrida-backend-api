using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    [Table("CompanySubscriptionAuditLogs", Schema = "public")]
    public class CompanySubscriptionAuditLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AuditId { get; set; }

        public int Id { get; set; }

        public uint ProfileId { get; set; }

        public int? PlanId { get; set; }

        public BillingCycle BillingCycle { get; set; }

        public SubscriptionStatus Status { get; set; }

        [MaxLength(100)]
        public string? StripeCustomerId { get; set; }

        [MaxLength(100)]
        public string? StripeSubscriptionId { get; set; }

        public int TeamCount { get; set; }

        public int MaxTeamsAllowed { get; set; }

        public int MaxPeopleAllowed { get; set; }

        public DateTime? TrialStartDate { get; set; }

        public DateTime? TrialEndDate { get; set; }

        public DateTime CurrentPeriodStart { get; set; }

        public DateTime CurrentPeriodEnd { get; set; }

        public bool IsTrialUsed { get; set; }

        public DateTime? CanceledAt { get; set; }

        public bool CancelAtPeriodEnd { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public int? PromoCodeId { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal? LifetimeDiscountPercent { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal? Discount { get; set; }

        public int? GracePeriod { get; set; }

        public int? LastWarningDaysSent { get; set; }

        public bool IsDel { get; set; }

        // Audit fields
        public DateTime LoggedAt { get; set; } = DateTime.UtcNow;

        public int VersionNumber { get; set; } = 1;

        [MaxLength(20)]
        public string OperationType { get; set; } = string.Empty; // INSERT, UPDATE, DELETE
    }
}
