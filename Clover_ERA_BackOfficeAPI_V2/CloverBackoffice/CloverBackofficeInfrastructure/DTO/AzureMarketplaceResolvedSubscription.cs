using System.Text.Json.Serialization;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class AzureMarketplaceResolvedSubscription
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("subscriptionName")]
        public string? SubscriptionName { get; set; }

        [JsonPropertyName("offerId")]
        public string OfferId { get; set; } = string.Empty;

        [JsonPropertyName("planId")]
        public string PlanId { get; set; } = string.Empty;

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }

        [JsonPropertyName("subscription")]
        public AzureMarketplaceSubscriptionDetails? Subscription { get; set; }
    }

    public class AzureMarketplaceSubscriptionDetails
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("publisherId")]
        public string? PublisherId { get; set; }

        [JsonPropertyName("offerId")]
        public string? OfferId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("saasSubscriptionStatus")]
        public string? SaasSubscriptionStatus { get; set; }

        [JsonPropertyName("beneficiary")]
        public AzureMarketplacePartyInfo? Beneficiary { get; set; }

        [JsonPropertyName("purchaser")]
        public AzureMarketplacePartyInfo? Purchaser { get; set; }

        [JsonPropertyName("planId")]
        public string? PlanId { get; set; }

        [JsonPropertyName("term")]
        public AzureMarketplaceTerm? Term { get; set; }

        [JsonPropertyName("isTest")]
        public bool IsTest { get; set; }

        [JsonPropertyName("isFreeTrial")]
        public bool IsFreeTrial { get; set; }

        [JsonPropertyName("autoRenew")]
        public bool AutoRenew { get; set; }

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }

        [JsonPropertyName("created")]
        public DateTime? Created { get; set; }

        [JsonPropertyName("lastModified")]
        public DateTime? LastModified { get; set; }
    }

    public class AzureMarketplacePartyInfo
    {
        [JsonPropertyName("emailId")]
        public string? EmailId { get; set; }

        [JsonPropertyName("objectId")]
        public string? ObjectId { get; set; }

        [JsonPropertyName("tenantId")]
        public string? TenantId { get; set; }

        [JsonPropertyName("puid")]
        public string? Puid { get; set; }
    }

    public class AzureMarketplaceTerm
    {
        [JsonPropertyName("termUnit")]
        public string? TermUnit { get; set; }

        [JsonPropertyName("startDate")]
        public DateTime? StartDate { get; set; }

        [JsonPropertyName("endDate")]
        public DateTime? EndDate { get; set; }
    }

    public class AzureMarketplaceOperation
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("activityId")]
        public Guid ActivityId { get; set; }

        [JsonPropertyName("subscriptionId")]
        public Guid SubscriptionId { get; set; }

        [JsonPropertyName("offerId")]
        public string? OfferId { get; set; }

        [JsonPropertyName("publisherId")]
        public string? PublisherId { get; set; }

        [JsonPropertyName("planId")]
        public string? PlanId { get; set; }

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }

        [JsonPropertyName("action")]
        public string? Action { get; set; }

        [JsonPropertyName("timeStamp")]
        public DateTime TimeStamp { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }
}
