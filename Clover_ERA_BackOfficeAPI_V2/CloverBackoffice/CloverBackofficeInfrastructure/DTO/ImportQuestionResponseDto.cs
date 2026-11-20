using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class ImportQuestionResponseDto
    {
        public int TotalRows { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<ImportQuestionRowResultDto> Results { get; set; } = new List<ImportQuestionRowResultDto>();
    }

    public class ImportQuestionRowResultDto
    {
        public int RowNumber { get; set; }
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public string QuestionText { get; set; }
        public CustomQuestionResponseDto QuestionData { get; set; }
    }
}

