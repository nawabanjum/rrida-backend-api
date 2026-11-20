using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    [Table("PaymentAuditLogs", Schema = "public")]
    public class PaymentAuditLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AuditId { get; set; }

        public int Id { get; set; }

        public uint ProfileId { get; set; }

        public int CompanySubscriptionId { get; set; }

        [MaxLength(100)]
        public string? StripePaymentIntentId { get; set; }

        [MaxLength(100)]
        public string? StripeInvoiceId { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal Amount { get; set; }

        // Mirrors Payment.AmountSubtotal / Payment.DiscountAmount / Payment.DiscountCode.
        [Column(TypeName = "decimal(10,2)")]
        public decimal? AmountSubtotal { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal? DiscountAmount { get; set; }

        [MaxLength(100)]
        public string? DiscountCode { get; set; }

        [MaxLength(3)]
        public string Currency { get; set; } = "usd";

        public PaymentMethodType PaymentMethod { get; set; }

        [MaxLength(255)]
        public string? CardholderName { get; set; }

        [MaxLength(4)]
        public string? CardLast4 { get; set; }

        [MaxLength(20)]
        public string? CardBrand { get; set; }

        public int? CardExpMonth { get; set; }

        public int? CardExpYear { get; set; }

        [MaxLength(100)]
        public string? StripePaymentMethodId { get; set; }

        [MaxLength(20)]
        public string? CardFunding { get; set; }

        [MaxLength(2)]
        public string? CardCountry { get; set; }

        public PaymentStatus Status { get; set; }

        public DateTime? InvoiceDueDate { get; set; }

        public int? VerifiedByUserId { get; set; }

        public DateTime? VerifiedAt { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        [MaxLength(500)]
        public string? FailureReason { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        // Audit fields
        public DateTime LoggedAt { get; set; } = DateTime.UtcNow;

        public int VersionNumber { get; set; } = 1;

        [MaxLength(20)]
        public string OperationType { get; set; } = string.Empty; // INSERT, UPDATE, DELETE
    }
}
