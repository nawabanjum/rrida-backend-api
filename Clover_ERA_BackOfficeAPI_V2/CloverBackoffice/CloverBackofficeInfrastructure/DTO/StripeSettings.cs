namespace CloverBackoffice.Infrastructure.DTO
{
    public class StripeSettings
    {
        // Stripe keys + webhook secret. Loaded PER-ENVIRONMENT from Azure Key
        // Vault at startup (Program.cs), keyed by "Environment:Name". Never
        // stored in appsettings.json. The rest of the codebase only reads these
        // three fields and stays oblivious to which environment they came from.
        public string SecretKey { get; set; } = string.Empty;
        public string PublishableKey { get; set; } = string.Empty;
        public string WebhookSecret { get; set; } = string.Empty;

        public string SuccessUrl { get; set; } = string.Empty;
        public string CancelUrl { get; set; } = string.Empty;

        // Flat monthly price charged per license/team at checkout. The licensing
        // model is fixed-price-per-license: total = licenseCount × this value,
        // with NO bulk/plan tiers. Annual billing charges this × AnnualPriceMultiplier
        // (handled in CreateDynamicCheckoutSessionAsync and the add-on flow).
        // Defaults to 295 when the config key is absent so existing single-license
        // pricing is preserved.
        public decimal PricePerLicenseMonthly { get; set; } = 295m;

        // Months of the monthly price charged for one ANNUAL license: yearly
        // price = PricePerLicenseMonthly × this. Single source of truth for the
        // annual multiplier, shared by the primary subscription (StripeService)
        // and the additional-license add-on flow so a first yearly purchase and a
        // later yearly add-on always cost the same. 12 = full twelve months (no
        // annual discount); set to 10 for a "2 months free" annual discount.
        public int AnnualPriceMultiplier { get; set; } = 12;

        // Stripe Price ids ("price_...") of the RRIDA Team Manager products, one
        // per billing cycle. When set, checkout and seat-count updates bill these
        // catalog prices (quantity = licence count) instead of an inline price
        // built from PricePerLicenseMonthly, so invoices name the real product.
        // Empty → the inline-price behaviour above. Not secret; set per
        // environment (appsettings.<Env>.json or Stripe__MonthlyPriceId).
        // Keep PricePerLicenseMonthly / AnnualPriceMultiplier equal to these
        // prices: the app still uses them for displayed totals.
        public string MonthlyPriceId { get; set; } = string.Empty;
        public string AnnualPriceId { get; set; } = string.Empty;

        // Marks which environment a Stripe object belongs to, for when several
        // environments (or another product) share one Stripe account. Written as
        // metadata "rridaEnv" on customers, checkout sessions and subscriptions;
        // when set, the webhook ignores any event not carrying this same value.
        // Empty → no tagging and no filtering (the original behaviour).
        public string EnvironmentTag { get; set; } = string.Empty;
    }
}
