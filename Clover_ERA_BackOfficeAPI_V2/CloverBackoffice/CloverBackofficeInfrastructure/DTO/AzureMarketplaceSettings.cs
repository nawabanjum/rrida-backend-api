namespace CloverBackoffice.Infrastructure.DTO
{
    public class AzureMarketplaceSettings
    {
        public string TenantId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string ResourceId { get; set; } = "20e940b3-4c77-4b0b-9a53-9e16a1b010a7";
        public string ApiVersion { get; set; } = "2018-08-31";
        public string BaseUrl { get; set; } = "https://marketplaceapi.microsoft.com";
        public string LandingPageRedirectUrl { get; set; } = string.Empty;

        // true  = auto-activate sub + return simple HTML (no frontend needed)
        // false = redirect customer to LandingPageRedirectUrl (frontend handles activate)
        public bool AutoActivateOnLanding { get; set; } = true;

        // DEV ONLY: skip Microsoft verification + ack so fake payloads can be tested locally.
        // MUST be false in production.
        public bool SkipVerificationForTesting { get; set; } = false;
    }
}
