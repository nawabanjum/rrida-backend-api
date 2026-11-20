namespace CloverBackoffice.Infrastructure.DTO.PlanManagement
{
    /// <summary>
    /// The plan catalogue feature keys the product code enforces (seeded system features).
    /// Titles, descriptions and each plan's values stay editable in api/admin/plan-features.
    /// </summary>
    public static class PlanFeatureKeys
    {
        public const string ManagersAndTeams = "MANAGERS_AND_TEAMS";
        public const string TeamHealthTimeline = "TEAM_HEALTH_TIMELINE";
        public const string DailyNudgesMonthlyReporting = "DAILY_NUDGES_MONTHLY_REPORTING";
        public const string PortfolioView = "PORTFOLIO_VIEW";
        public const string EranCoach = "ERAN_COACH";
        public const string EranConversations = "ERAN_CONVERSATIONS";

        /// <summary>Clover Resources (worksheets) with the weekly action. Not a comparison row of its own.</summary>
        public const string CloverResources = "CLOVER_RESOURCES";
    }
}
