using System.Text.Json;

namespace Rrida.Engine;

/// <summary>
/// The calendar, and the days a team is asked on. D24, D25, D26.
/// </summary>
/// <remarks>
/// <para>
/// Two clocks, and keeping them apart is the point of this file.
/// </para>
/// <para>
/// The <b>calendar</b> runs seven days a week and belongs to everyone. A team's
/// <b>working week</b> is weekdays its manager chooses: Monday to Friday in an office,
/// Sunday to Thursday in the Gulf, Wednesday to Sunday in a restaurant. The team is asked
/// on those days and on no others.
/// </para>
/// <para>
/// <b>Every window in the layer counts the team's own asked days, never calendar days.</b>
/// That is the load-bearing sentence. Sections M and N measured that a window counting
/// calendar days holds five sevenths of the answers it was calibrated for, and that this,
/// rather than the missing days themselves, is what cost the detection: the quiet fade fell
/// from about 91 per cent found to 76. Counting asked days brings it back to 89.8 at a
/// lower false-alarm rate, and leaves h at 31.0 and g at 17.5.
/// </para>
/// <para>
/// <b>Weekdays are numbered with Sunday as 0</b>, which is what JavaScript's
/// <c>getUTCDay()</c> gives and what <c>kit/data/calendar.json</c> carries. .NET's
/// <see cref="DayOfWeek"/> happens to agree. A port that numbered from Monday would read
/// every schedule one day out and nothing would crash.
/// </para>
/// </remarks>
public sealed class CalendarConfig
{
    public const int Sun = 0, Mon = 1, Tue = 2, Wed = 3, Thu = 4, Fri = 5, Sat = 6;

    public required IReadOnlyDictionary<string, int[]> Schedules { get; init; }
    public required string DefaultSchedule { get; init; }

    /// <summary>Day 1 of a run, read as UTC midnight. A Monday, so the default week starts where a week starts.</summary>
    public required string DefaultStartDate { get; init; }

    /// <summary>
    /// D26. Friday, and only until a portfolio says otherwise. Where the week ends is a
    /// portfolio's decision, the same kind of decision as which days a team works.
    /// </summary>
    public required int DefaultRollUpWeekEnds { get; init; }

    public static CalendarConfig Load(string file)
    {
        var raw = JsonDocument.Parse(File.ReadAllText(file)).RootElement;
        var schedules = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var entry in raw.GetProperty("schedules").EnumerateObject())
        {
            schedules[entry.Name] = entry.Value.EnumerateArray().Select(x => x.GetInt32()).ToArray();
        }
        return new CalendarConfig
        {
            Schedules = schedules,
            DefaultSchedule = raw.GetProperty("defaultSchedule").GetString()!,
            DefaultStartDate = raw.GetProperty("defaultStartDate").GetString()!,
            DefaultRollUpWeekEnds = raw.GetProperty("defaultRollUpWeekEnds").GetInt32(),
        };
    }

    /// <summary>
    /// A schedule, from a name or a list of weekdays.
    /// </summary>
    /// <remarks>
    /// Five days is what the product offers and what sections M and N measured. Four is a
    /// supported configuration and is allowed; three is not refused either, because the
    /// arithmetic holds and refusing it would be this file deciding a product question.
    /// What is refused is nothing at all, which is a team that can never be asked.
    /// </remarks>
    public int[] Resolve(string? name)
    {
        if (name is null || !Schedules.TryGetValue(name, out var days) || days.Length == 0)
        {
            throw new DataException(
                $"unknown working week \"{name}\". Name one of {string.Join(", ", Schedules.Keys)} "
                + "or give a list of weekdays.");
        }
        return Normalise(days);
    }

    public static int[] Normalise(IReadOnlyList<int> days)
    {
        if (days.Count == 0)
        {
            throw new DataException("a working week with no days in it is a team that can never be asked");
        }
        var bad = days.FirstOrDefault(d => d is < 0 or > 6, -1);
        if (days.Any(d => d is < 0 or > 6))
        {
            throw new DataException($"weekday {days.First(d => d is < 0 or > 6)} is not a day of the week");
        }
        _ = bad;
        return days.Distinct().OrderBy(d => d).ToArray();
    }
}

/// <summary>Date arithmetic and the two day spaces. Nothing here holds state.</summary>
public static class Calendar
{
    private static DateTime Utc(string dateString) =>
        DateTime.SpecifyKind(DateTime.ParseExact(dateString, "yyyy-MM-dd", null), DateTimeKind.Utc);

    /// <summary>Calendar day <paramref name="day"/> (1-based) as a date. Seven days a week, no gaps.</summary>
    public static DateTime DateForDay(string startDate, int day) => Utc(startDate).AddDays(day - 1);

    public static int WeekdayOf(string startDate, int day) => (int)DateForDay(startDate, day).DayOfWeek;

    public static bool IsMonthEnd(string startDate, int day) =>
        day >= 1 && DateForDay(startDate, day).Month != DateForDay(startDate, day + 1).Month;

    /// <summary>The first calendar day of the month this day falls in, clamped to the start of the run.</summary>
    public static int FirstDayOfMonth(string startDate, int day) =>
        Math.Max(1, day - DateForDay(startDate, day).Day + 1);

    /// <summary>English month names, so the monthly label does not depend on the machine's culture.</summary>
    private static readonly string[] MonthNames =
    [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
    ];

    /// <summary>The label the monthly summary carries, e.g. "January 2026".</summary>
    public static string MonthLabel(string startDate, int day)
    {
        var date = DateForDay(startDate, day);
        return $"{MonthNames[date.Month - 1]} {date.Year}";
    }

    public sealed record Week(int From, int To, int Ends);

    /// <summary>
    /// The roll-up week containing <paramref name="day"/>, inclusive, clamped to day 1. D26.
    /// </summary>
    /// <remarks>
    /// A manager of managers looking at teams on different working weeks needs one boundary
    /// for "this week", and their child rows are read against it whatever each team's own
    /// week does. A weekday outside 0 to 6 is refused rather than silently clamped.
    /// </remarks>
    public static Week RollUpWeek(string startDate, int day, int weekEnds)
    {
        if (weekEnds is < 0 or > 6) throw new DataException($"the roll-up week cannot end on \"{weekEnds}\"");
        var end = Math.Max(1, day);
        // Walk forward to the boundary this day belongs to, at most six days away.
        while (WeekdayOf(startDate, end) != weekEnds) end += 1;
        return new Week(Math.Max(1, end - 6), end, end);
    }

    /// <summary>
    /// The last day of the team's own week, which is where D25 puts the weekly reading.
    /// </summary>
    /// <remarks>
    /// "Last" is relative to the schedule rather than to the calendar: a Wednesday-to-Sunday
    /// restaurant ends its week on Sunday and a Sunday-to-Thursday team ends on Thursday,
    /// even though Sunday is weekday 0 and would sort first. Found by walking the schedule
    /// forward from each day and taking the one with no successor inside seven days.
    /// </remarks>
    public static int LastDayOfWeek(IReadOnlyList<int> schedule)
    {
        foreach (var day in schedule)
        {
            if (!schedule.Contains((day + 1) % 7)) return day;
        }
        // Every day of the week is worked, so the week ends when the calendar week does.
        return CalendarConfig.Sun;
    }

    /// <summary>
    /// The one conversion from a calendar day into the rotation's own space. D24 amended.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rotation answers in a team's asked-day index: its nth entry is the nth question
    /// that team is offered. Everything else in the layer that carries a day carries a
    /// calendar day. Both are plain integers, so passing one where the other is wanted is
    /// silent, and it was silent five times in the reference: the occurrences window, the
    /// scripted confirmation offset, the tally walking its window by date, the day's slots,
    /// and the classifier's question.
    /// </para>
    /// <para>
    /// That last one is the one to remember. It produced a plausible answer rather than a
    /// crash, and it reached the manager as a missing Headwinds flag.
    /// </para>
    /// <para>
    /// <b>So there is one way to cross, and this is it.</b> A test scans both libraries and
    /// asserts nothing else does the arithmetic itself.
    /// </para>
    /// </remarks>
    public static int RotationDay(Team team, int calendarDay)
    {
        if (calendarDay < 1)
        {
            throw new DataException($"a calendar day is a whole number from one, not {calendarDay}");
        }
        var asked = team.AskedDaysTo(calendarDay);
        if (asked < 1)
        {
            throw new DataException(
                $"{team.Id} has not been asked anything by calendar day {calendarDay}, "
                + "so it has no rotation day yet");
        }
        return asked;
    }

    /// <summary>
    /// The calendar day on which a schedule reaches its nth asked day.
    /// </summary>
    /// <remarks>
    /// A scenario's script is the team's own sequence of answering days, so ninety scripted
    /// days are ninety asked days. A run's horizon is calendar days. On a five-day week
    /// those ninety need a hundred and twenty-six, and a horizon left at ninety would cut
    /// the last twenty-six days of every script off without saying so.
    /// </remarks>
    public static int CalendarDayForAskedDay(string startDate, IReadOnlyList<int> schedule, int n)
    {
        if (n < 1) return 1;
        var seen = 0;
        for (var day = 1; day <= n * 7 + 14; day += 1)
        {
            if (!schedule.Contains(WeekdayOf(startDate, day))) continue;
            seen += 1;
            if (seen == n) return day;
        }
        throw new DataException($"a {schedule.Count}-day week never reaches asked day {n}");
    }
}
