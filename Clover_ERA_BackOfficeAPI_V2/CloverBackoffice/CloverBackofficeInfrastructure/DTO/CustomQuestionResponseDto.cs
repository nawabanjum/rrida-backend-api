using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class CustomQuestionResponseDto
    {
        public long DailyQuestionId { get; set; }
        public string QuestionText { get; set; }

        public long SpecificationId { get; set; }
        public string CloverCategory { get; set; }
        public int Ordinal { get; set; }

        // Add these to return actual text values
        public IEnumerable<string> PositiveReasons { get; set; }
        public IEnumerable<string> NegativeReasons { get; set; }
        public IEnumerable<string> PositiveActions { get; set; }
        public IEnumerable<string> NegativeActions { get; set; }
    }
}
