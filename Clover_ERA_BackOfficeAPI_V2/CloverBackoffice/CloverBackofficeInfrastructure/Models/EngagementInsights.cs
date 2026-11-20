using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.Models
{
    public class EngagementInsights
    {
        public int Id { get; set; }
        public uint BusinessUnitId { get; set; }
        public DateTime GeneratedAt { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public decimal? OverallScore { get; set; }
        public string CategoryScoresJson { get; set; }
        public string TopThemesJson { get; set; }
        public string RecommendationsJson { get; set; }
        public string StatisticalSummaryJson { get; set; }
        public string CreatedBy { get; set; }
        public Guid BatchJobsId { get; set; }
        public string TopActionsJson { get; set; }
        public BatchJob BatchJob { get; set; }
    }
}


