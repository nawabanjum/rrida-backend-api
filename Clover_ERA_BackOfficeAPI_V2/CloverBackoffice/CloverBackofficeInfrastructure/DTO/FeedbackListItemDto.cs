using System;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class FeedbackListItemDto
    {
        public long Id { get; set; }
        public string Feedback { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }
    }
}
