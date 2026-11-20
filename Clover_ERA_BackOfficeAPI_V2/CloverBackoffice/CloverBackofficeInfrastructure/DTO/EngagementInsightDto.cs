using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class EngagementInsightDto
    {
        public int Id { get; set; }
        public DateTime GeneratedAt { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public decimal? OverallScore { get; set; }
        public string CategoryScoresJson { get; set; }
        public string TopThemesJson { get; set; }
        public string RecommendationsJson { get; set; }
        public object StatisticalSummaryJson { get; set; }
        public string CreatedBy { get; set; }
        public string TopActionsJson { get; set; }
    }

    public class BatchJobDto
    {
        public Guid BatchJobId { get; set; }
        public string OpenAIJobId { get; set; }
        public string Status { get; set; }
        public string RequestType { get; set; }
        public DateTime SubmittedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string InputFileUrl { get; set; }
        public string OutputFileUrl { get; set; }
        public string ErrorMessage { get; set; }
        public string MetadataJson { get; set; }
        public List<EngagementInsightDto> EngagementInsights { get; set; } = new();
    }

    public class BusinessUnitBatchJobsDto
    {
        public int BusinessUnitId { get; set; }
        public List<BatchJobDto> BatchJobs { get; set; } = new();
    }
}
