namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// A team's two clocks: dates, and the days the team is actually asked. Windows count
    /// the second. Built per team from its start date and working week.
    /// </summary>
    public interface IEngineTeamClock
    {
        DateOnly StartDate { get; }
        string WorkingWeek { get; }

        /// <summary>The weekdays this team works, Sunday = 0.</summary>
        IReadOnlyList<int> Schedule { get; }

        bool IsAskedOn(DateOnly date);

        /// <summary>Days since the start date, 1-based.</summary>
        int DayOf(DateOnly date);

        DateOnly DateOfDay(int day);

        /// <summary>Asked days up to and including this date; 0 if the team is not asked then.</summary>
        int AskedDayIndexOf(DateOnly date);
    }
}
