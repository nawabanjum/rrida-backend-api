using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class UpdateQuestionInfoDto
    {
        public long QuestionId { get; set; }
        public string QuestionText { get; set; }
        public UpdateQuestionSpecificationInfoDto Specification { get; set; }
    }

    public class UpdateQuestionSpecificationInfoDto
    {
        public long SpecificationId { get; set; }
        public string CloverCategory { get; set; }
        public List<UpdateReasonInfoDto> Reasons { get; set; }
    }

    public class UpdateReasonInfoDto
    {
        public long? ReasonId { get; set; } // Nullable for new reasons
        public string Text { get; set; }
        public string SentimentType { get; set; }
        public bool RequiresFreeTextFeedback { get; set; }

        public bool? IsDel { get; set; }
        public List<UpdateActionInfoDto> Actions { get; set; }
    }

    public class UpdateActionInfoDto
    {
        public long? ActionId { get; set; } // Nullable for new actions
        public string Text { get; set; }
        public bool? IsDel { get; set; }
    }
}

