using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class FeedbackDataResultDto
    {
        [Key]
        public long FeedbackId { get; set; }
        public DateTime SubmissionDateTime { get; set; }

        public int BusinessUnitId { get; set; }
        public string? BusinessUnitName { get; set; }
        public string? CompanyName { get; set; }

        public int? QuestionId { get; set; }
        public string? QuestionText { get; set; }

        public int? CategoryId { get; set; }
        public string? Category { get; set; }

        public int? ReasonId { get; set; }
        public string? ReasonText { get; set; }

        public int SentimentTypeId { get; set; }
        public string? SentimentType { get; set; }

        public string? FreeText { get; set; }
    }
}
