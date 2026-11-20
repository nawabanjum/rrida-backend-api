using System.Text.Json.Serialization;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class AzureMarketplaceWebhookPayload
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("activityId")]
        public Guid ActivityId { get; set; }

        [JsonPropertyName("subscriptionId")]
        public Guid SubscriptionId { get; set; }

        [JsonPropertyName("publisherId")]
        public string PublisherId { get; set; } = string.Empty;

        [JsonPropertyName("offerId")]
        public string OfferId { get; set; } = string.Empty;

        [JsonPropertyName("planId")]
        public string PlanId { get; set; } = string.Empty;

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }

        [JsonPropertyName("action")]
        public string Action { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("operationRequestSource")]
        public string? OperationRequestSource { get; set; }

        [JsonPropertyName("timeStamp")]
        public DateTime TimeStamp { get; set; }

        [JsonPropertyName("subscription")]
        public AzureMarketplaceResolvedSubscription? Subscription { get; set; }

        [JsonPropertyName("purchaseToken")]
        public string? PurchaseToken { get; set; }
    }

    public static class AzureMarketplaceActions
    {
        public const string Subscribe = "Subscribe";
        public const string Unsubscribe = "Unsubscribe";
        public const string ChangePlan = "ChangePlan";
        public const string ChangeQuantity = "ChangeQuantity";
        public const string Suspend = "Suspend";
        public const string Reinstate = "Reinstate";
        public const string Renew = "Renew";
        public const string Transfer = "Transfer";
    }

    public static class AzureMarketplaceStatuses
    {
        public const string PendingFulfillmentStart = "PendingFulfillmentStart";
        public const string Subscribed = "Subscribed";
        public const string Suspended = "Suspended";
        public const string Unsubscribed = "Unsubscribed";
        public const string NotStarted = "NotStarted";
    }
}
