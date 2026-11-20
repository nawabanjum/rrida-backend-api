using System;

namespace CloverBackoffice.Infrastructure.DTO
{
    // A contiguous slice of the requested date range flagged licensed or not,
    // so the frontend can tell which sub-periods of the range the selected unit
    // held an active license for. Appended to the dashboard summary response as
    // licensePeriods; it does NOT change the health data itself.
    public class LicensePeriodSegmentDto
    {
        public DateTimeOffset FromDate { get; set; }
        public DateTimeOffset ToDate { get; set; }

        // Effective Team Health license state for this segment:
        //   true  = license active (data valid)
        //   false = license expired / never held
        //   null  = TRANSITION — a license assign/unassign happened within the
        //           grace period, so the team is not yet Team-Health-active;
        //           the frontend shows the lock screen for this window.
        public bool? IsLicensed { get; set; }
    }
}
