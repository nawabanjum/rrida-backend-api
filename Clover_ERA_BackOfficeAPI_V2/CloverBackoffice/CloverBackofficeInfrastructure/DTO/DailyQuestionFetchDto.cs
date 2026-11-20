using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    /// <summary>
    /// The daily question served for a business unit, with the reasons that can be
    /// given when feedback is submitted against it. Mirrors the Clover ERA mobile API
    /// response contract.
    /// </summary>
    public class DailyQuestionFetchResponseDto
    {
        public string CompanyName { get; set; } = string.Empty;

        public string BusinessUnitName { get; set; } = string.Empty;

        public uint QuestionId { get; set; }

        public string QuestionText { get; set; } = string.Empty;

        public DailyReasonResponseDto[] Reasons { get; set; } = Array.Empty<DailyReasonResponseDto>();

        /// <summary>
        /// CLOVER category of the question specification.
        /// </summary>
        public CloverCategory CloverCategory { get; set; }
    }

    /// <summary>
    /// A reason that can be selected when submitting feedback for a daily question.
    /// </summary>
    public class DailyReasonResponseDto
    {
        public uint ReasonId { get; set; }

        public string ReasonText { get; set; } = string.Empty;

        public bool RequiresFreeTextFeedback { get; set; }

        public DailyReasonSentimentType Sentiment { get; set; }
    }
}
