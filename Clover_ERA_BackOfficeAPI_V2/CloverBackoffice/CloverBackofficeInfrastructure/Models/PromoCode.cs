using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    public class PromoCode
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [MaxLength(50)]
        public required string Code { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal DiscountPercent { get; set; }

        public uint? ProfileId { get; set; }                    // null = global (any company)

        public BillingCycle? ApplicableBillingCycle { get; set; } // null = both Monthly & Annual

        public int? MaxUsageCount { get; set; }                 // null = unlimited

        public int CurrentUsageCount { get; set; } = 0;

        public DateTime ExpiryDate { get; set; }

        public bool IsLifetime { get; set; } = false;        // true = discount applies on every renewal forever

        public bool IsActive { get; set; } = true;

        public int CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public bool IsDel { get; set; } = false;

        // Navigation properties
        [ForeignKey(nameof(ProfileId))]
        public virtual Profile? Profile { get; set; }

        public virtual ICollection<PromoCodeUsage>? Usages { get; set; }
    }
}
