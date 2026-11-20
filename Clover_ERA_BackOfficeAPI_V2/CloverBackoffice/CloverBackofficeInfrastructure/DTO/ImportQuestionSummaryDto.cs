using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class ImportQuestionSummaryDto
    {
        public int TotalRows { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<ImportErrorDetailDto> Errors { get; set; } = new List<ImportErrorDetailDto>();
    }

    public class ImportErrorDetailDto
    {
        public int RowNumber { get; set; }
        public string Message { get; set; }
        public string QuestionText { get; set; }
    }
}

