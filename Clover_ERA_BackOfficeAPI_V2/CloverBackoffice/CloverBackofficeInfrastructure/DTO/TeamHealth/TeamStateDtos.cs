namespace CloverBackoffice.Infrastructure.DTO.TeamHealth
{
    /// <summary>
    /// GET api/teams/{teamId}/health/state — the Team State card on Team Health.
    ///
    /// <c>state</c> is the team's latest resolved day. The four segments are the share of
    /// resolved days in the last 30 calendar days spent in each state, whole percentages
    /// summing to 100 (all zero when there are no resolved days in the window). The
    /// frontend draws them as given and derives nothing.
    /// </summary>
    public class TeamStateDto
    {
        public string TeamId { get; set; } = string.Empty;
        public string TeamName { get; set; } = string.Empty;

        /// <summary>CRUISE, DRIFT or STALL — the vertical, as the MVP shows it. Headwinds is a flag, not a state.</summary>
        public string State { get; set; } = TeamStates.Cruise;

        /// <summary>Raised on the latest day: the causes read as outside the team. Says nothing about the state.</summary>
        public bool HeadwindsFlag { get; set; }

        /// <summary>Share of counted days in each state; these three sum to 100.</summary>
        public int Cruise { get; set; }
        public int Drift { get; set; }
        public int Stall { get; set; }

        /// <summary>Share of the same days the Headwinds flag was raised on, 0–100, independent of the three above.</summary>
        public int Headwinds { get; set; }

        /// <summary>True until more than six asked days are resolved (MVP: no state before day seven).</summary>
        public bool IsCalibrating { get; set; }

        /// <summary>Resolved days the segments were computed from.</summary>
        public int DaysCounted { get; set; }

        /// <summary>"YYYY-MM-DD" date of the day <c>state</c> was read from; null while calibrating.</summary>
        public string? AsOfDate { get; set; }

        /// <summary>
        /// The compass's dashed trail, oldest first: where the reading stood on each of the
        /// team's last three weekly readings before <c>asOfDate</c> (the client's "last three
        /// reporting periods"; a report week ends on the reading, D25). Empty while
        /// calibrating and before the first reading.
        /// </summary>
        public List<TeamStateTrailPointDto> Trail { get; set; } = new();

        /// <summary>
        /// The trend chart: one row per calculated day in the last 30, 60 or 90 days
        /// (<c>?days=</c>, default 60), oldest first. Empty while calibrating.
        /// </summary>
        public TeamStateHistoryDto History { get; set; } = new();
    }

    /// <summary>The trend chart's window. fromDate and toDate are inclusive; toDate is the NOW date once calibrated.</summary>
    public class TeamStateHistoryDto
    {
        /// <summary>The window asked for: 30, 60 or 90.</summary>
        public int PeriodDays { get; set; }

        /// <summary>"YYYY-MM-DD", inclusive.</summary>
        public string FromDate { get; set; } = string.Empty;

        /// <summary>"YYYY-MM-DD", inclusive.</summary>
        public string ToDate { get; set; } = string.Empty;

        /// <summary>One row per calculated day, oldest first. A day that was not calculated has no row.</summary>
        public List<TeamStateHistoryPointDto> DataPoints { get; set; } = new();

        /// <summary>The dates Headwinds was raised on, one per date, each a date in <see cref="DataPoints"/>.</summary>
        public List<TeamStateHeadwindsEventDto> HeadwindsEvents { get; set; } = new();
    }

    /// <summary>One calculated day on the trend chart.</summary>
    public class TeamStateHistoryPointDto
    {
        /// <summary>"YYYY-MM-DD".</summary>
        public string AsOfDate { get; set; } = string.Empty;

        /// <summary>CRUISE, DRIFT or STALL — that day's definitive state. Never derived from the shares.</summary>
        public string State { get; set; } = TeamStates.Cruise;

        /// <summary>Shares as of that day (the same 30-day reading as the summary); these three sum to 100.</summary>
        public int Cruise { get; set; }
        public int Drift { get; set; }
        public int Stall { get; set; }

        /// <summary>0–100, separate from the three shares.</summary>
        public int Headwinds { get; set; }

        /// <summary>Headwinds was raised on this day.</summary>
        public bool HeadwindsFlag { get; set; }

        public int DaysCounted { get; set; }
    }

    /// <summary>A date Headwinds was raised on.</summary>
    public class TeamStateHeadwindsEventDto
    {
        /// <summary>"YYYY-MM-DD".</summary>
        public string Date { get; set; } = string.Empty;
    }

    /// <summary>One trail point: the same four shares as the current reading, as of a weekly reading day.</summary>
    public class TeamStateTrailPointDto
    {
        /// <summary>"YYYY-MM-DD" date of the weekly reading.</summary>
        public string AsOfDate { get; set; } = string.Empty;

        public int Cruise { get; set; }
        public int Drift { get; set; }
        public int Stall { get; set; }
        public int Headwinds { get; set; }
        public int DaysCounted { get; set; }
    }

    public static class TeamStates
    {
        public const string Cruise = "CRUISE";
        public const string Drift = "DRIFT";
        public const string Headwinds = "HEADWINDS";
        public const string Stall = "STALL";
    }
}
