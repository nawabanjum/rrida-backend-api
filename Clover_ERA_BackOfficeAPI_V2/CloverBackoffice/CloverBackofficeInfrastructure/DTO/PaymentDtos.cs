namespace CloverBackoffice.Infrastructure.DTO
{
    public class PaymentHistoryDto
    {
        public int Id { get; set; }                                 // Transaction ID

        // The subscription this payment belongs to (every payment carries one —
        // a Subscription + Payment are created together at purchase). Lets the
        // client tie a payment straight back to its subscription.
        public int CompanySubscriptionId { get; set; }

        // True when that subscription is currently LIVE — it exists (not
        // soft-deleted), is Active, and is NOT scheduled to cancel. False once it
        // has been canceled (cancel-at-period-end), expired, or removed. Note a
        // canceled subscription keeps Status=Active during its wind-down, so this
        // flag also checks the cancel flag — not just Status.
        public bool HasSubscription { get; set; }

        public decimal Amount { get; set; }

        // Discount breakdown, both null when the payment carried no discount.
        // When present, AmountSubtotal is the pre-discount total and
        // Amount == AmountSubtotal - DiscountAmount. Figures come from Stripe,
        // never recomputed here.
        public decimal? AmountSubtotal { get; set; }
        public decimal? DiscountAmount { get; set; }

        // The code that produced DiscountAmount — either an admin-created promo
        // code or a Stripe Dashboard promotion code the customer typed at
        // checkout. Null when there was no discount or the code was unresolvable.
        public string? DiscountCode { get; set; }

        public string Currency { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;   // "Card" or "Invoice"
        public string Status { get; set; } = string.Empty;          // Payment status
        public string? Description { get; set; }

        // Number of licenses this transaction minted (its license batch). Split
        // out of the description so the UI can show label + count separately.
        // Populated only by the transaction/details composition; null on rows
        // with no resolvable license batch and on other endpoints.
        public int? Quantity { get; set; }

        public DateTime? InvoiceDueDate { get; set; }
        public DateTime CreatedAt { get; set; }                     // Transaction date & time (= card "last used" for this txn)

        // Card used for THIS transaction. Null for non-card (invoice) payments
        // or when Stripe didn't surface the value. Lets the License tab show the
        // card per transaction and derive "last used" from CreatedAt.
        public string? CardholderName { get; set; }
        public string? CardBrand { get; set; }                      // Visa, Mastercard, …
        public string? CardLast4 { get; set; }                      // masked number tail
        public int? CardExpMonth { get; set; }                      // 1-12
        public int? CardExpYear { get; set; }                       // 4-digit year

        // Expiry date of the license(s) this transaction paid for. Every license
        // minted in a single purchase shares one expiry, so one date represents
        // the whole transaction. Resolved at read time from the licenses on this
        // payment's subscription (no direct payment→license FK exists). Null when
        // the subscription has no dated license (e.g. legacy/invoice-only rows).
        public DateTime? LicenseExpiryDate { get; set; }
    }

    public class RequestInvoiceDto
    {
        public int CompanySubscriptionId { get; set; }
    }

    // Combined response for GET /api/subscription/transaction/details.
    // Scoped to a Company (all units + company license pool) or a single Unit,
    // carrying the subscription details, the scope's card-on-file, and the
    // paged payment history (each row already carries its own card info).
    public class TransactionDetailsDto
    {
        public uint ProfileId { get; set; }
        public uint? UnitId { get; set; }
        public string Scope { get; set; } = string.Empty;   // "Company" | "Unit"

        // Subscription details for the scope. Exactly one is populated:
        //   * Unit scope    → UnitSubscription
        //   * Company scope → CompanySubscription (all units + license pool)
        public SubscriptionDetailDto? UnitSubscription { get; set; }
        public CompanySubscriptionDetailDto? CompanySubscription { get; set; }

        // Most-recent card on file for the scope (last4, brand, expiry, …).
        public PaymentCardDto? Card { get; set; }

        // Paged payment history for the scope.
        public List<PaymentHistoryDto> Payments { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}
