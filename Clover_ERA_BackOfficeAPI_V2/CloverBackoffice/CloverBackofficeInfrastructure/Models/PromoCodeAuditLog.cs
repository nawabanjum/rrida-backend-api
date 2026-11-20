using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    [Table("PromoCodeAuditLogs", Schema = "public")]
    public class PromoCodeAuditLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AuditId { get; set; }

        public int Id { get; set; }

        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [Column(TypeName = "decimal(5,2)")]
        public decimal DiscountPercent { get; set; }

        public uint? ProfileId { get; set; }

        public BillingCycle? ApplicableBillingCycle { get; set; }

        public int? MaxUsageCount { get; set; }

        public int CurrentUsageCount { get; set; }

        public DateTime ExpiryDate { get; set; }

        public bool IsLifetime { get; set; }

        public bool IsActive { get; set; }

        public int CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public bool IsDel { get; set; }

        // Audit fields
        public DateTime LoggedAt { get; set; } = DateTime.UtcNow;

        public int VersionNumber { get; set; } = 1;

        [MaxLength(20)]
        public string OperationType { get; set; } = string.Empty; // INSERT, UPDATE, DELETE
    }
}
