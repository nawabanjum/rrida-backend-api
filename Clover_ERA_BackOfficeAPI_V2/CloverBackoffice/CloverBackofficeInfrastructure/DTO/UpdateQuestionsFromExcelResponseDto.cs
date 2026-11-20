using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class UpdateQuestionsFromExcelResponseDto
    {
        public int TotalRowsInExcel { get; set; }
        public int MatchedQuestions { get; set; }
        public int UpdatedQuestions { get; set; }
        public int UpdatedPositiveReasons { get; set; }
        public int UpdatedNegativeReasons { get; set; }
        public int UpdatedPositiveActions { get; set; }
        public int UpdatedNegativeActions { get; set; }
        public int SkippedRows { get; set; }
        public int TotalUpdatedReasons => UpdatedPositiveReasons + UpdatedNegativeReasons;
        public int TotalUpdatedActions => UpdatedPositiveActions + UpdatedNegativeActions;
        public List<ExcelUpdateRowResultDto> Results { get; set; } = new List<ExcelUpdateRowResultDto>();
    }

    public class ExcelUpdateRowResultDto
    {
        public int RowNumber { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public bool IsMatched { get; set; }
        public bool HasChanges { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<string> ChangeDetails { get; set; } = new List<string>();
    }
}
