using CloverBackoffice.Infrastructure.BusinessModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class DailyFeedback
    {
        public uint Id { get; init; }

        public required DateTime SubmissionDateTime { get; init; }

        [ForeignKey(nameof(BusinessUnit))]
        public required uint BusinessUnitId { get; set; }

        public virtual BusinessUnit? BusinessUnit { get; set; }

        public DailyFeedbackSentimentType SentimentType { get; init; }

        [ForeignKey(nameof(Question))]
        public uint DailyQuestionId { get; init; }

        public virtual DailyQuestion? Question { get; set; }

        /// <summary>
        /// The first selected reason — the full selection is in DailyFeedbackReasons.
        /// Null when the member answered with their own free text and no predefined
        /// reason.
        /// </summary>
        [ForeignKey(nameof(Reason))]
        public uint? DailyReasonId { get; init; }

        public virtual DailyReason? Reason { get; set; }

        [MaxLength(DataConstants.FeedbackFreeTextLength)]
        public required string? FreeText { get; init; }

    }
}
