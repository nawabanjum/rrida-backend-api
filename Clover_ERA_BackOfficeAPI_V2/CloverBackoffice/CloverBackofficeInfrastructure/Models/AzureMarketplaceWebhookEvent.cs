using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    public class AzureMarketplaceWebhookEvent
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public Guid OperationId { get; set; }

        public Guid? ActivityId { get; set; }

        public Guid MarketplaceSubscriptionId { get; set; }

        [MaxLength(50)]
        public string Action { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? IncomingStatus { get; set; }

        [MaxLength(200)]
        public string? PlanId { get; set; }

        public int? Quantity { get; set; }

        [Column(TypeName = "jsonb")]
        public string RawPayload { get; set; } = "{}";

        [MaxLength(50)]
        public string ProcessingStatus { get; set; } = "Received";

        public string? ErrorMessage { get; set; }

        public bool VerifiedWithMicrosoft { get; set; }

        public bool AckSentToMicrosoft { get; set; }

        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ProcessedAt { get; set; }
    }

    public static class WebhookProcessingStatuses
    {
        public const string Received = "Received";
        public const string Processing = "Processing";
        public const string Success = "Success";
        public const string Failure = "Failure";
        public const string DuplicateIgnored = "DuplicateIgnored";
        public const string VerificationFailed = "VerificationFailed";
    }
}
