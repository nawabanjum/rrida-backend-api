namespace CloverBackoffice.Infrastructure.DTO.TeamHealth
{
    /// <summary>GET api/portfolio/managers — the manager dropdown on the Portfolio View.</summary>
    public class PortfolioManagersDto
    {
        /// <summary>The signed-in Portfolio Manager or RRIDA Admin.</summary>
        public int PortfolioManagerId { get; set; }

        public uint ProfileId { get; set; }

        /// <summary>
        /// Every manager below the Portfolio Manager, directly or through other Portfolio
        /// Managers, or every manager in the selected profile for a RRIDA Admin, by name.
        /// </summary>
        public List<PortfolioManagerDto> Managers { get; set; } = new();
    }

    public class PortfolioManagerDto
    {
        public int ManagerId { get; set; }

        public string ManagerName { get; set; } = string.Empty;

        public uint ProfileId { get; set; }

        /// <summary>
        /// Who this manager reports to: the signed-in Portfolio Manager, or a Portfolio Manager
        /// below them. For a RRIDA Admin, their Portfolio Manager, or 0 when they have none.
        /// </summary>
        public int ReportsToManagerId { get; set; }

        /// <summary>Teams this manager holds now (active assignments).</summary>
        public int TeamCount { get; set; }

        /// <summary>True when the manager is a Portfolio Manager with managers of their own.</summary>
        public bool IsPortfolioManager { get; set; }
    }

    /// <summary>GET api/portfolio/teams?managerId= — the Teams table.</summary>
    public class PortfolioTeamsDto
    {
        /// <summary>The selected manager; null = All Managers.</summary>
        public int? ManagerId { get; set; }

        public string? ManagerName { get; set; }

        public int TeamCount { get; set; }

        /// <summary>Managers the listed teams belong to.</summary>
        public int ManagerCount { get; set; }

        /// <summary>Headwinds, Stall, Drift, Cruise, then calibrating; by name within each.</summary>
        public List<PortfolioTeamDto> Teams { get; set; } = new();
    }

    public class PortfolioTeamDto
    {
        public uint TeamId { get; set; }

        public string TeamName { get; set; } = string.Empty;

        public int TeamManagerId { get; set; }

        public string TeamManagerName { get; set; } = string.Empty;

        /// <summary>Members enrolled today; the team's own size when nobody is enrolled yet.</summary>
        public int MemberCount { get; set; }

        /// <summary>HEADWINDS, STALL, DRIFT or CRUISE; null while calibrating.</summary>
        public string? HealthState { get; set; }

        /// <summary>The latest day's vertical (CRUISE, DRIFT, STALL); null while calibrating.</summary>
        public string? Vertical { get; set; }

        public bool HeadwindsFlag { get; set; }

        /// <summary>
        /// No state yet (six asked days or fewer), or fewer enrolled members than the roll-up
        /// visibility floor, which reads as calibrating whatever the real state.
        /// </summary>
        public bool IsCalibrating { get; set; }

        /// <summary>Weeks the team has been in its current health state; 0 while calibrating.</summary>
        public int WeeksCount { get; set; }

        /// <summary>The latest resolved day (yyyy-MM-dd).</summary>
        public string? AsOfDate { get; set; }

        /// <summary>The trend graph: one reading per week over the last quarter, oldest first, ending on <see cref="AsOfDate"/>.</summary>
        public List<PortfolioTrendPointDto> Trend { get; set; } = new();

        // The team's actions, the same set GET api/actions lists: the manager's own (MANUAL)
        // and the engine actions taken up with "I'll do it" (ENGINE).

        /// <summary>Last Action Update: when one of the team's actions was last created, edited or closed (UTC); null when the team has none.</summary>
        public DateTime? LastActionUpdate { get; set; }

        /// <summary>No. of Actions (Last 90 Days): actions created or taken up in the last 90 days, today included; 0 when none.</summary>
        public int ActionsCountLast90Days { get; set; }

        /// <summary>The Action column: "1 in flight", "2 in flight"; null when nothing is in flight.</summary>
        public string? Action { get; set; }

        /// <summary>Actions still to do: OPEN / IN_PROGRESS manual ones and engine ones in flight.</summary>
        public int ActionsInFlight { get; set; }

        /// <summary>The most recently updated action (the one behind <see cref="LastActionUpdate"/>); null when none.</summary>
        public PortfolioActionDto? LatestAction { get; set; }
    }

    /// <summary>One action, in GET api/actions terms (actionId opens GET api/actions/{actionId}).</summary>
    public class PortfolioActionDto
    {
        /// <summary>The api/actions id: "12" for a manual action, "engine-34" for an engine one.</summary>
        public string ActionId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        /// <summary>MANUAL, REPORT_GENERATED, SYSTEM or ENGINE.</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>OPEN, IN_PROGRESS, DONE or CANCELLED.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>"YYYY-MM-DD" or null.</summary>
        public string? DueDate { get; set; }

        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>The team's reading as of one week: the same 30-day shares the Team State card shows.</summary>
    public class PortfolioTrendPointDto
    {
        public string AsOfDate { get; set; } = string.Empty;

        /// <summary>The health state on that day.</summary>
        public string State { get; set; } = string.Empty;

        /// <summary>The value to plot: the share of the 30 days in Cruise (0-100, higher is healthier).</summary>
        public int Score { get; set; }

        public int Cruise { get; set; }

        public int Drift { get; set; }

        public int Stall { get; set; }

        public int Headwinds { get; set; }

        public int DaysCounted { get; set; }
    }

    /// <summary>GET api/portfolio/alerts?managerId= — the "needs you" banner and its Review action.</summary>
    public class PortfolioAlertsDto
    {
        public int? ManagerId { get; set; }

        public string? ManagerName { get; set; }

        /// <summary>Teams in HEADWINDS or STALL; always equals Teams.Count.</summary>
        public int AttentionCount { get; set; }

        /// <summary>"2 teams need you, not the manager."; null when none.</summary>
        public string? Title { get; set; }

        /// <summary>One sentence per team; null when none.</summary>
        public string? Message { get; set; }

        /// <summary>The teams the Review action opens, in banner order.</summary>
        public List<uint> ReviewTeamIds { get; set; } = new();

        public List<PortfolioAlertTeamDto> Teams { get; set; } = new();
    }

    public class PortfolioAlertTeamDto
    {
        public uint TeamId { get; set; }

        public string TeamName { get; set; } = string.Empty;

        public string HealthState { get; set; } = string.Empty;

        public int WeeksCount { get; set; }

        public int ManagerId { get; set; }

        public string ManagerName { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>GET api/portfolio/eran-context?managerId= — what Eran is told about this scope, and the column's brief.</summary>
    public class PortfolioEranContextDto
    {
        /// <summary>"manager" when one is selected, "portfolio" for All Managers.</summary>
        public string Scope { get; set; } = string.Empty;

        public int? ManagerId { get; set; }

        public string? ManagerName { get; set; }

        public int TeamCount { get; set; }

        public int ManagerCount { get; set; }

        public PortfolioStateCountsDto StateCounts { get; set; } = new();

        /// <summary>The brief's opening line.</summary>
        public string Lead { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;

        public List<string> Prompts { get; set; } = new();

        public PortfolioAlertsDto Alerts { get; set; } = new();

        public List<PortfolioTeamDto> Teams { get; set; } = new();
    }

    public class PortfolioStateCountsDto
    {
        public int Headwinds { get; set; }

        public int Stall { get; set; }

        public int Drift { get; set; }

        public int Cruise { get; set; }

        public int Calibrating { get; set; }
    }
}
