using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IStripeService
    {
        Task<string> GetOrCreateCustomerAsync(uint profileId, string email, string name);
        Task<string> CreateCheckoutSessionAsync(string customerId, string priceId, int trialDays, string successUrl, string cancelUrl, uint profileId, int planId);
        // extraMetadata is added to the session AND to the Stripe subscription it
        // creates, so subscription events can recognise the purchase too.
        Task<string> CreateDynamicCheckoutSessionAsync(string customerId, int teamCount, decimal pricePerTeamCents, BillingCycle billingCycle, int trialDays, string successUrl, string cancelUrl, uint profileId, decimal? discountPercent = null, bool isLifetimeDiscount = false, string? promoCodeText = null, int? promoCodeId = null, IDictionary<string, string>? extraMetadata = null, CloverBackoffice.Infrastructure.DTO.PlanManagement.StripePlanPrice? planPrice = null);
        Task<string> CreateOneTimePaymentSessionAsync(string customerId, long unitAmountCents, string productName, string successUrl, string cancelUrl, IDictionary<string, string> metadata);
        Task UpdateSubscriptionPriceAsync(string stripeSubscriptionId, int newTeamCount, decimal pricePerTeamCents, BillingCycle billingCycle);
        Task UpdateSubscriptionPriceNoProrateAsync(string stripeSubscriptionId, int newTeamCount, decimal pricePerTeamCents, BillingCycle billingCycle);
        Task UpdateSubscriptionAsync(string stripeSubscriptionId, string newPriceId);
        Task CancelSubscriptionAsync(string stripeSubscriptionId, bool atPeriodEnd = true);
        Task ReactivateSubscriptionAsync(string stripeSubscriptionId, long? quantity = null);

        // Seat quantity of the subscription's first item.
        Task<long> GetSubscriptionQuantityAsync(string stripeSubscriptionId);

        // Sets the first item's quantity. prorate=false: nothing is credited or
        // charged now, the next renewal invoice bills the new quantity.
        // prorate=true: the unused time of removed seats is credited to the
        // subscription's next invoice.
        Task SetSubscriptionQuantityAsync(string stripeSubscriptionId, long quantity, bool prorate = false);

        // Ends the subscription NOW. prorate=true credits the unused time to the
        // customer's balance (applied to their future invoices).
        Task CancelSubscriptionNowAsync(string stripeSubscriptionId, bool prorate);

        // Open (not yet completed or expired) checkout sessions of a customer.
        Task<List<StripeCheckoutSessionInfo>> ListOpenCheckoutSessionsAsync(string customerId);

        // Expires an open checkout session so it can no longer be completed.
        Task ExpireCheckoutSessionAsync(string sessionId);
        Task<CardDetails> GetCardDetailsAsync(string? paymentIntentId);

        // Authoritative "which card was used" resolver. Walks the most precise
        // source to the least: PaymentIntent's charge → the customer's actual
        // charge (matched to the invoice) → subscription default PM → customer
        // default / saved card. Robust to the Basil API dropping the deprecated
        // Invoice.payment_intent / Invoice.charge fields. Returns an empty
        // CardDetails (HasCard=false) only when no source yields a card.
        Task<CardDetails> GetCardUsedAsync(
            string? paymentIntentId, string? invoiceId, string? subscriptionId, string? customerId);
        Task<string?> GetProductIdForSubscriptionAsync(string? stripeSubscriptionId);

        // The customer-facing promotion code text (e.g. "SUMMER20") redeemed on a
        // Checkout Session, or null if none was. Best-effort: null on any failure.
        Task<string?> GetSessionPromotionCodeAsync(string? sessionId);

        // Reads the recurring interval of a Stripe subscription's first line
        // item and maps it to our BillingCycle (interval "year" → Annual,
        // "month" → Monthly). The authoritative "what was actually bought"
        // signal for the checkout webhook when metadata is absent. Returns null
        // when the id is empty, the interval is neither month nor year, or the
        // lookup fails.
        Task<BillingCycle?> GetSubscriptionBillingCycleAsync(string? stripeSubscriptionId);

        object ConstructWebhookEvent(string json, string signature, string secret);

        // Lists a customer's PAID one-time "addLicenses" checkout sessions
        // (metadata op=addLicenses, payment_status=paid). Used by the
        // reconcile/backfill path to recover top-ups whose
        // checkout.session.completed webhook never reached the mint. Capped at
        // Stripe's max page size of 100.
        Task<List<StripeCheckoutSessionInfo>> ListPaidAddLicenseSessionsAsync(string customerId);

        // Reads the profileId stored in a Stripe customer's metadata
        // (GetOrCreateCustomerAsync stamps it). The authoritative link back to
        // our company when a subscription/invoice event carries no metadata —
        // e.g. the subscription-updated resilience fallback. Returns null when
        // the customer is missing, has no profileId, or the lookup fails.
        Task<uint?> GetProfileIdForCustomerAsync(string? customerId);

        // The customer's "rridaEnv" metadata (null when untagged or not found).
        // The webhook uses it to drop events from another environment that
        // shares this Stripe account.
        Task<string?> GetCustomerEnvironmentTagAsync(string? customerId);

        // Customer id of an invoice (null when not found). For events such as
        // invoice_payment.paid whose object names only the invoice.
        Task<string?> GetInvoiceCustomerIdAsync(string? invoiceId);
    }
}
