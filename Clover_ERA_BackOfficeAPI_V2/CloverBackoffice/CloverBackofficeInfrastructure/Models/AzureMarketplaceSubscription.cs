using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    public class AzureMarketplaceSubscription
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public Guid MarketplaceSubscriptionId { get; set; }

        [MaxLength(200)]
        public string OfferId { get; set; } = string.Empty;

        [MaxLength(200)]
        public string PlanId { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? PublisherId { get; set; }

        [MaxLength(300)]
        public string? SubscriptionName { get; set; }

        public int Quantity { get; set; }

        [MaxLength(50)]
        public string Status { get; set; } = string.Empty;

        [MaxLength(300)]
        public string? PurchaserEmail { get; set; }

        public Guid? PurchaserTenantId { get; set; }

        [MaxLength(200)]
        public string? PurchaserObjectId { get; set; }

        [MaxLength(300)]
        public string? BeneficiaryEmail { get; set; }

        public Guid? BeneficiaryTenantId { get; set; }

        [MaxLength(200)]
        public string? BeneficiaryObjectId { get; set; }

        [MaxLength(20)]
        public string? TermUnit { get; set; }

        public DateTime? TermStartDate { get; set; }

        public DateTime? TermEndDate { get; set; }

        public bool AutoRenew { get; set; }

        public bool IsTest { get; set; }

        public bool IsFreeTrial { get; set; }

        public uint? ProfileId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ActivatedAt { get; set; }

        public DateTime? UnsubscribedAt { get; set; }

        public bool IsDel { get; set; } = false;

        [ForeignKey(nameof(ProfileId))]
        public virtual Profile? Profile { get; set; }
    }
}
