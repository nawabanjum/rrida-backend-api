namespace CloverBackoffice.Infrastructure.DTO
{
    public class FeedbackAutoImportSettings
    {
        public bool Enabled { get; set; }
        public string BusinessUnitIds { get; set; }
        public double IntervalHours { get; set; }
        public DateTime SubmissionDateTime { get; set; }
        public string ExcelFilePath { get; set; }
        /// <summary>
        /// Number of feedback records to import per day per business unit.
        /// Default is 1. Can be configured to 20, 30, 50, etc.
        /// </summary>
        public int RecordsPerDay { get; set; } = 1;
    }
}
