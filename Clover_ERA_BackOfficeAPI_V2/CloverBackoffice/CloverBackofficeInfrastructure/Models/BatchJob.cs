using System;

namespace CloverBackoffice.Infrastructure.Models
{
    public class BatchJob
    {
        public Guid Id { get; set; }
        public string OpenAIJobId { get; set; }
        public string Status { get; set; }
        public string RequestType { get; set; }
        public DateTime SubmittedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? InputFileUrl { get; set; }
        public string? OutputFileUrl { get; set; }
        public string? ErrorMessage { get; set; }
        public string? MetadataJson { get; set; }
        public uint BusinessUnitId { get; set; }
        public ICollection<EngagementInsights> EngagementInsights { get; set; } = new List<EngagementInsights>();

    }
}


