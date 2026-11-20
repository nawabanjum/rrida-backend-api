using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class DailyFeedbackReportRowDto
    {
        public long FeedbackId { get; set; }
        public DateTimeOffset SubmissionDateTime { get; set; }
        public int SentimentType { get; set; }
        public string? FreeText { get; set; }

        [NotMapped]
        public string? SentimentTypeName { get; set; }
        public uint? QuestionId { get; set; }
        public string? QuestionText { get; set; }

        public uint? ReasonId { get; set; }
        public string? ReasonText { get; set; }
        public bool? RequiresFreeText { get; set; }
        public uint? SpecificationId { get; set; }

        public int? SpecificationOrdinal { get; set; }
        public long? SpecificationGroupId { get; set; }
        public int? SpecificationCloverCategory { get; set; }
        [NotMapped]
        public string? SpecificationCloverCategoryName { get; set; }
        public uint? BusinessUnitId { get; set; }
        [NotMapped]
        public string? CompanyName { get; set; }

        [NotMapped]
        public double? Score { get; set; }

        [NotMapped]
        public string? Answer { get; set; }
    }
}
