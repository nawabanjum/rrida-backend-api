using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    public class Payment
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public uint ProfileId { get; set; }

        public int CompanySubscriptionId { get; set; }

        [MaxLength(100)]
        public string? StripePaymentIntentId { get; set; }

        [MaxLength(100)]
        public string? StripeInvoiceId { get; set; }

        // What was actually charged (Stripe amount_total) — already net of any
        // discount. Kept as the single authoritative figure; the two columns
        // below only explain how it was reached.
        [Column(TypeName = "decimal(10,2)")]
        public decimal Amount { get; set; }

        // Pre-discount total (Stripe amount_subtotal) and the discount Stripe
        // applied (total_details.amount_discount). Both null on payments with no
        // discount and on rows written before these columns existed, so the UI
        // must treat null as "no discount" rather than zero.
        //
        // Stripe computes both — we never derive them locally. Invariant when
        // present: AmountSubtotal - DiscountAmount == Amount.
        [Column(TypeName = "decimal(10,2)")]
        public decimal? AmountSubtotal { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal? DiscountAmount { get; set; }

        // Which code caused DiscountAmount, as text. Two sources, one column:
        // our own admin-created code (PromoCodes.Code), or a promotion code the
        // customer typed on the Stripe checkout page (Stripe's promo_… resolved
        // to its code). Null when the payment carried no discount, when the code
        // could not be resolved, and on rows written before this column existed
        // — so a non-null DiscountAmount with a null code is valid, not a bug.
        //
        // Deliberately NOT a PromoCodeId FK: a Stripe Dashboard code has no row
        // in PromoCodes, and forcing one would fabricate local records for codes
        // this system doesn't own.
        [MaxLength(100)]
        public string? DiscountCode { get; set; }

        [MaxLength(3)]
        public string Currency { get; set; } = "usd";

        public PaymentMethodType PaymentMethod { get; set; }

        // Name on the card, from Stripe billing_details.name. Nullable — may be
        // absent when the payer didn't supply it at checkout.
        [MaxLength(255)]
        public string? CardholderName { get; set; }

        [MaxLength(4)]
        public string? CardLast4 { get; set; }

        [MaxLength(20)]
        public string? CardBrand { get; set; }

        // Card expiry (from the payment method used). Nullable — non-card
        // payments (invoice) and unresolved lookups leave these null.
        public int? CardExpMonth { get; set; }              // 1-12

        public int? CardExpYear { get; set; }               // 4-digit year

        // Stripe payment-method id (pm_…) actually charged — the "associated
        // payment method" the ticket asks for. Lets us tie a stored card back
        // to Stripe without re-deriving it.
        [MaxLength(100)]
        public string? StripePaymentMethodId { get; set; }

        [MaxLength(20)]
        public string? CardFunding { get; set; }            // credit | debit | prepaid | unknown

        [MaxLength(2)]
        public string? CardCountry { get; set; }            // ISO-3166-1 alpha-2

        public PaymentStatus Status { get; set; }

        public DateTime? InvoiceDueDate { get; set; }          // creation + 30 days (invoice only)

        public int? VerifiedByUserId { get; set; }             // Clover ERA Admin who verified

        public DateTime? VerifiedAt { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        [MaxLength(500)]
        public string? FailureReason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey(nameof(ProfileId))]
        public virtual Profile? Profile { get; set; }

        [ForeignKey("CompanySubscriptionId")]
        public virtual CompanySubscription? CompanySubscription { get; set; }
    }
}
