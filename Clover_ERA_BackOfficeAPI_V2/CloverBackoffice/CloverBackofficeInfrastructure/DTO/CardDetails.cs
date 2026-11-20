namespace CloverBackoffice.Infrastructure.DTO
{
    // Card details resolved from Stripe for a single payment. Every field is
    // nullable — any individual Stripe lookup may fail to yield a value, and
    // callers persist only the fields that came back non-null (so a partial
    // resolve never wipes previously-stored data).
    public class CardDetails
    {
        public string? CardholderName { get; set; }  // name on card (Stripe billing_details.name)
        public string? Last4 { get; set; }          // last four digits (masked-number tail)
        public string? Brand { get; set; }          // visa, mastercard, amex, …
        public int? ExpMonth { get; set; }          // 1-12
        public int? ExpYear { get; set; }            // 4-digit year
        public string? PaymentMethodId { get; set; } // Stripe pm_… id (associated payment method)
        public string? Funding { get; set; }         // credit | debit | prepaid | unknown
        public string? Country { get; set; }         // ISO-3166-1 alpha-2 issuing country

        // True once we have at least the last four — the minimum that makes a
        // stored card meaningful. Used by the resolver to decide whether a
        // source "hit" and by the upsert to skip empty results.
        public bool HasCard => !string.IsNullOrEmpty(Last4);

        public static CardDetails Empty => new();
    }
}
