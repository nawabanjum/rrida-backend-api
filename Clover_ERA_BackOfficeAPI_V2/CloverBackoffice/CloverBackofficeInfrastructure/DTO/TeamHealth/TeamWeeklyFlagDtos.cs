namespace CloverBackoffice.Infrastructure.DTO.TeamHealth
{
    /// <summary>
    /// GET api/teams/{teamId}/weekly-flags. Without a week: every flag for the team,
    /// latest week first (fills the week dropdown). With <c>weekStartDate</c>: the flag
    /// for that week only — one item, or an empty list when the week has none.
    /// </summary>
    public class TeamWeeklyFlagsDto
    {
        public string TeamId { get; set; } = string.Empty;
        public List<WeeklyFlagDto> WeeklyFlags { get; set; } = new();
    }

    public class WeeklyFlagDto
    {
        public uint Id { get; set; }

        /// <summary>"YYYY-MM-DD".</summary>
        public string WeekStartDate { get; set; } = string.Empty;

        /// <summary>"YYYY-MM-DD", inclusive.</summary>
        public string WeekEndDate { get; set; } = string.Empty;

        /// <summary>Dropdown text, e.g. "Week of 25 Aug".</summary>
        public string Label { get; set; } = string.Empty;

        public string WhatCameUp { get; set; } = string.Empty;
        public string SuggestedAction { get; set; } = string.Empty;

        /// <summary>
        /// The engine cycle behind the week's action (EngineCycles.Id). Pass it to
        /// GET api/eran-coach/timeline?cycleId= for that cycle's messages. Null when the
        /// week has no delivered action.
        /// </summary>
        public long? CycleId { get; set; }
    }
}
