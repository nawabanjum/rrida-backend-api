using Rrida.Engine;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// How a manager-facing surface says time on the deterministic layer (MVP 90fe06d): a count of
    /// the team's own working days, "3 working days ago" or "in 2 working days", never the run-day
    /// index ("day 98" counts days since the team started and means nothing to the reader).
    ///
    /// The engine's <see cref="Team.WorkingDaysBack"/> and <see cref="Team.WorkingDaysAhead"/> are
    /// the only two things permitted to phrase one, so this only builds the team's clock for them:
    /// an engine <see cref="Team"/> with the team's start date and working week and nothing else.
    /// </summary>
    public sealed class EngineWorkingDays
    {
        private readonly Team _team;

        public EngineWorkingDays(EngineContext ctx, DateOnly startDate, string workingWeek)
        {
            _team = new Team(ctx, new TeamConfig
            {
                Id = "clock",
                Roster = [],
                WorkingWeek = workingWeek,
                StartDate = startDate.ToString("yyyy-MM-dd")
            });
        }

        /// <summary>"today", "1 working day ago", "N working days ago"; null when there is no day.</summary>
        public string? Back(int day, int? then) => Team.WorkingDaysBack(_team, day, then);

        /// <summary>"today", "in 1 working day", "in N working days"; null when there is no day.</summary>
        public string? Ahead(int day, int? then) => Team.WorkingDaysAhead(_team, day, then);
    }
}
