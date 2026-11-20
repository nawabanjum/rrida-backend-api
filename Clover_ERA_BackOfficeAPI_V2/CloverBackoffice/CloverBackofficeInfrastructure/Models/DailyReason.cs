using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class DailyReason
    {
        public uint Id { get; init; }

        public required DailyReasonSentimentType DailyReasonSentimentType { get; set; }

        [MaxLength(1000)]
        public required string Text { get; set; }

        public required bool RequiresFreeTextFeedback { get; set; }
        public uint SpecificationId { get; set; }

        public virtual required DailyQuestionSpecification Specification { get; set; }

        public virtual required ContentCulture ContentCulture { get; set; }
        
        //  Soft delete flag
       // public bool? IsDel { get; set; } = false;
    }
}
