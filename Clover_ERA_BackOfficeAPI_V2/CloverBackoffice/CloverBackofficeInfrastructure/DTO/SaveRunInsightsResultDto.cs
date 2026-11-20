namespace CloverBackoffice.Infrastructure.DTO
{
    // What SaveRunInsightsAsync did, for callers that need more than the
    // insert/update code the controller returns to the workbench UI.
    //
    // The scheduler needs the ReportId (to log it and to hand it to the review
    // queue) and needs to be able to save WITHOUT publishing, because a
    // scheduled report waits for an admin before it reaches its manager.
    public class SaveRunInsightsResultDto
    {
        // 0 = run or report not found, 1 = report created, 2 = existing report updated.
        public int Outcome { get; set; }

        public long ReportId { get; set; }

        // False when the caller asked for a save without release: no
        // InsightPublishLog row was written, so the report sits Pending in the
        // review queue.
        public bool Published { get; set; }

        // True only when this call was the report's first release. Editing and
        // re-saving an already published report leaves this false, which is what
        // keeps the manager from being emailed twice about one report.
        public bool FirstPublish { get; set; }

        // The saved report's unit and window, so a caller that has to notify the
        // manager doesn't re-read the row it just wrote.
        public uint UnitId { get; set; }
        public System.DateTime FromDate { get; set; }
        public System.DateTime ToDate { get; set; }
    }
}
