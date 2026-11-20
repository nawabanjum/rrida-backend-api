using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class DailyFeedbackDto
    {
        public long FeedbackId { get; set; }
        public DateTimeOffset SubmissionDateTime { get; set; }
        public int SentimentType { get; set; }
        public string FreeText { get; set; }

        public long QuestionId { get; set; }
        public string QuestionText { get; set; }

        public long ReasonId { get; set; }
        public string ReasonText { get; set; }
        public bool RequiresFreeText { get; set; }
        public long SpecificationId { get; set; }

        public int SpecificationOrdinal { get; set; }
        public long SpecificationGroupId { get; set; }
        public int SpecificationCloverCategory { get; set; }
    }
}
