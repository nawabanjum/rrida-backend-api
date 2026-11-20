using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class ExcelFeedbackDto
    {
        public DateTimeOffset SubmissionDateTime { get; set; }
        public string BusinessUnitName { get; set; }
        public string CompanyName { get; set; }
        public string QuestionText { get; set; }
        public string Category { get; set; }
        public int SentimentType { get; set; }
        public string ReasonText { get; set; }
        public string FreeText { get; set; }
    }
}
