using CloverBackoffice.Services.Interfaces;
using Rrida.Engine;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// The two clocks, in one place.
    ///
    /// A DAY is a date index: days since the team's start date, 1-based. An ASKED DAY is
    /// how many days the team has actually been asked anything, which skips the days it
    /// does not work. Every window in the layer counts the second, and both are plain
    /// integers, which is how the reference implementation confused them five times.
    ///
    /// The working week comes from the kit's own calendar.json, so a team on sun-thu for
    /// the Gulf or wed-sun for a restaurant needs no code change. Sunday is 0.
    /// </summary>
    public sealed class EngineTeamClock : IEngineTeamClock
    {
        private readonly int[] _schedule;

        public EngineTeamClock(DateOnly startDate, string workingWeek, CalendarConfig calendar)
        {
            StartDate = startDate;
            WorkingWeek = workingWeek;
            // The kit refuses an unknown name with its own exception type. Caught here so a
            // bad stored setting reads as a team configuration problem rather than as a
            // failure from inside the engine.
            try
            {
                _schedule = calendar.Resolve(workingWeek);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"'{workingWeek}' is not a working week the kit knows. Use mon-fri, sun-thu, "
                    + "wed-sun, sat-wed, tue-sat or every-day.", ex);
            }
        }

        public DateOnly StartDate { get; }

        public string WorkingWeek { get; }

        /// <summary>The weekdays this team works, Sunday = 0.</summary>
        public IReadOnlyList<int> Schedule => _schedule;

        public bool IsAskedOn(DateOnly date) => _schedule.Contains((int)date.DayOfWeek);

        /// <summary>Days since the start date, 1-based. Day 1 is the start date itself.</summary>
        public int DayOf(DateOnly date) => date.DayNumber - StartDate.DayNumber + 1;

        public DateOnly DateOfDay(int day) => StartDate.AddDays(day - 1);

        /// <summary>
        /// How many days this team has been asked anything, up to and including the date.
        /// Zero when the date is before the start or the team does not work that day.
        /// </summary>
        public int AskedDayIndexOf(DateOnly date)
        {
            if (date < StartDate || !IsAskedOn(date)) return 0;

            // Whole weeks, then the tail. Counting day by day would be correct and would
            // also walk a year of dates on every submit.
            var totalDays = date.DayNumber - StartDate.DayNumber;   // exclusive of the date itself
            var wholeWeeks = totalDays / 7;
            var remainder = totalDays % 7;

            var count = wholeWeeks * _schedule.Length;
            for (var i = 0; i <= remainder; i += 1)
            {
                if (_schedule.Contains((int)StartDate.AddDays(wholeWeeks * 7 + i).DayOfWeek))
                    count += 1;
            }

            return count;
        }
    }
}
