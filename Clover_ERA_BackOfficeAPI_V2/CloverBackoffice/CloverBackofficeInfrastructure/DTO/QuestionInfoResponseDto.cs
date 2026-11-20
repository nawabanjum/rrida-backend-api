using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class QuestionInfoResponseDto
    {
        public long QuestionId { get; set; }
        public string QuestionText { get; set; }
        public QuestionSpecificationInfoDto Specification { get; set; }
    }

    public class QuestionSpecificationInfoDto
    {
        public long SpecificationId { get; set; }
        public string CloverCategory { get; set; }
        public List<ReasonInfoDto> Reasons { get; set; }
    }

    public class ReasonInfoDto
    {
        public long ReasonId { get; set; }
        public string Text { get; set; }
        public string SentimentType { get; set; }
        public bool? IsDel { get; set; }
        public List<ActionInfoDto> Actions { get; set; }
    }

    public class ActionInfoDto
    {
        public long ActionId { get; set; }
        public string Text { get; set; }
        public bool? IsDel { get; set; }
    }
}

