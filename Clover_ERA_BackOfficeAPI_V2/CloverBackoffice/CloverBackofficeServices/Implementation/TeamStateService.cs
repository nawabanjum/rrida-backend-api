using System.Globalization;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // Team State card on the Team Health page. Reads EngineStateDaily, which the
    // deterministic layer's daily job writes, and never writes or replays.
    //
    // One state per day, as the MVP shows it: the vertical — cruise, drift or stall.
    // Headwinds is not a state (D19/D21): it is a flag on the second axis, raised when the
    // causes read as outside the team, and it changes nothing about the vertical. It comes
    // back as `headwindsFlag` (today), and `headwinds` is the share of the counted days it
    // was raised on, alongside the three state shares, which alone sum to 100.
    //
    // Calibrating, as the MVP: until the team has more than six asked days resolved
    // (CALIBRATION_DAYS, "no state before day seven"). The engine resolves a state from day
    // one, but the first six are its warm-up and are neither shown nor counted. The current
    // state is the latest resolved day up to today; the shares cover the resolved days in
    // the last 30 calendar days.
    //
    // A day is the team's own index (StartDate = day 1), the same clock the engine
    // writes with, so a row for a future day is never counted.
    //
    // The trail is the client's dashed line on the compass, "the last three reporting
    // periods". The MVP's reporting period is the report week, the seven days ending on the
    // weekly reading (D25: the last working day of the team's own week), and a reading day
    // is a day the engine wrote its weekly message on — the same days the weekly flag lists.
    // Each point is the reading above taken as of one of the last three of those days
    // before the current one, so the trail shows where the dot stood, by the same rule.
    //
    // The history is the trend chart: one row per calculated day in the last 30, 60 or 90
    // days (?days=, default 60), oldest first. The window ends on the NOW date (asOfDate), so
    // the NOW row is always in it. Each row carries the engine's own state for that day (never
    // the largest share) and the same 30-day reading as above, taken as of that day. A day the
    // engine did not calculate has no row, and the warm-up days are never shown. The Headwinds
    // events are the days the flag was raised on, one per date, all of them rows.
    //
    // Access is ITeamHealthAccessService, as for every other Team Health endpoint.
    public class TeamStateService : ITeamStateService
    {
        private const int WindowDays = 30;
        private const int TrailPoints = 3;

        /// <summary>The trend chart windows; anything else, or none, is the default.</summary>
        public static readonly int[] HistoryPeriods = [30, 60, 90];
        public const int DefaultHistoryDays = 60;

        /// <summary>30, 60 or 90; anything else, or nothing, is 60.</summary>
        public static int HistoryDaysOf(int? days) =>
            days is int d && HistoryPeriods.Contains(d) ? d : DefaultHistoryDays;

        /// <summary>No state before day seven (MVP state.js CALIBRATION_DAYS, spec 7.3).</summary>
        public const int CalibrationDays = 6;

        private readonly ApplicationDbContext _context;
        private readonly ITeamHealthAccessService _access;

        public TeamStateService(ApplicationDbContext context, ITeamHealthAccessService access)
        {
            _context = context;
            _access = access;
        }

        public async Task<TeamMilestoneResult<TeamStateDto>> GetAsync(string? teamId, int actorUserId, CancellationToken ct = default, int? days = null)
        {
            var periodDays = HistoryDaysOf(days);

            if (!uint.TryParse(teamId?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var unitId) || unitId == 0)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.TimelineTeamIdInvalid);

            var unit = await _context.BusinessUnits
                .AsNoTracking()
                .Where(u => u.Id == unitId && u.IsDel != true)
                .Select(u => new { u.ProfileId, u.Name })
                .FirstOrDefaultAsync(ct);
            if (unit == null)
                return Fail(TeamMilestoneError.NotFound, ResponseMessages.Errors.MilestoneTeamNotFound);

            if (!await _access.CanAccessTeamAsync(unitId, unit.ProfileId, actorUserId, ct))
                return Fail(TeamMilestoneError.Forbidden, ResponseMessages.Errors.TimelineAccessDenied);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var dto = new TeamStateDto
            {
                TeamId = unitId.ToString(CultureInfo.InvariantCulture),
                TeamName = unit.Name,
                IsCalibrating = true,
                // Calibrating (or no engine yet): the window up to today, with nothing in it.
                History = new TeamStateHistoryDto
                {
                    PeriodDays = periodDays,
                    FromDate = Iso(today.AddDays(1 - periodDays)),
                    ToDate = Iso(today)
                }
            };

            var startDate = await _context.EngineTeamSettings
                .AsNoTracking()
                .Where(s => s.BusinessUnitId == unitId)
                .Select(s => (DateOnly?)s.StartDate)
                .FirstOrDefaultAsync(ct);
            if (startDate == null)
                return TeamMilestoneResult<TeamStateDto>.Ok(dto);

            var todayDay = today.DayNumber - startDate.Value.DayNumber + 1;

            // The engine resolves one state per asked day, so the rows are the team's asked
            // days, in order; the first CalibrationDays of them are the warm-up.
            var resolved = await _context.EngineStateDaily
                .AsNoTracking()
                .Where(s => s.BusinessUnitId == unitId && s.Day <= todayDay)
                .OrderBy(s => s.Day)
                .Select(s => new StateRow(s.Day, s.Vertical, s.HeadwindsFlag))
                .ToListAsync(ct);
            if (resolved.Count <= CalibrationDays)
                return TeamMilestoneResult<TeamStateDto>.Ok(dto);

            var latest = resolved[^1];
            dto.IsCalibrating = false;
            dto.State = StateOf(latest.Vertical);
            dto.HeadwindsFlag = latest.HeadwindsFlag;
            dto.AsOfDate = DateOf(startDate.Value, latest.Day);

            var counted = resolved.Skip(CalibrationDays).ToList();

            var now = ReadingAsOf(counted, todayDay);
            dto.Cruise = now.Cruise;
            dto.Drift = now.Drift;
            dto.Stall = now.Stall;
            dto.Headwinds = now.Headwinds;
            dto.DaysCounted = now.DaysCounted;

            var readingDays = await _context.EngineFeedItems
                .AsNoTracking()
                .Where(f => f.BusinessUnitId == unitId && f.Kind == "weekly" && f.Day < latest.Day)
                .Select(f => f.Day)
                .Distinct()
                .OrderByDescending(d => d)
                .Take(TrailPoints)
                .ToListAsync(ct);
            foreach (var day in readingDays.OrderBy(d => d))
            {
                var point = ReadingAsOf(counted, day);
                if (point.DaysCounted == 0) continue;   // still calibrating that day: no position to draw
                point.AsOfDate = DateOf(startDate.Value, day);
                dto.Trail.Add(point);
            }

            dto.History = History(counted, startDate.Value, latest.Day, periodDays);

            return TeamMilestoneResult<TeamStateDto>.Ok(dto);
        }

        internal sealed record StateRow(int Day, string? Vertical, bool HeadwindsFlag);

        /// <summary>
        /// The trend chart: the counted days in the <paramref name="periodDays"/> calendar days
        /// ending on <paramref name="nowDay"/> (the NOW date), oldest first, one row per day.
        /// </summary>
        internal static TeamStateHistoryDto History(List<StateRow> counted, DateOnly startDate, int nowDay, int periodDays)
        {
            var fromDay = nowDay - periodDays + 1;
            var history = new TeamStateHistoryDto
            {
                PeriodDays = periodDays,
                FromDate = DateOf(startDate, fromDay),
                ToDate = DateOf(startDate, nowDay)
            };

            // The engine writes one row per (team, day), so each day appears once.
            foreach (var row in counted.Where(s => s.Day >= fromDay && s.Day <= nowDay).OrderBy(s => s.Day))
            {
                // Shares as of that day; the day itself is always counted, so they sum to 100.
                var reading = ReadingAsOf(counted, row.Day);
                var date = DateOf(startDate, row.Day);
                history.DataPoints.Add(new TeamStateHistoryPointDto
                {
                    AsOfDate = date,
                    State = StateOf(row.Vertical),
                    Cruise = reading.Cruise,
                    Drift = reading.Drift,
                    Stall = reading.Stall,
                    Headwinds = reading.Headwinds,
                    HeadwindsFlag = row.HeadwindsFlag,
                    DaysCounted = reading.DaysCounted
                });
                if (row.HeadwindsFlag)
                    history.HeadwindsEvents.Add(new TeamStateHeadwindsEventDto { Date = date });
            }

            return history;
        }

        private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>
        /// The reading as of <paramref name="endDay"/>: the shares of the counted days in the
        /// 30 calendar days ending on it.
        /// </summary>
        internal static TeamStateTrailPointDto ReadingAsOf(List<StateRow> counted, int endDay)
        {
            var firstDay = endDay - WindowDays + 1;
            var days = counted.Where(s => s.Day >= firstDay && s.Day <= endDay).ToList();

            var counts = new[] { 0, 0, 0 };   // cruise, drift, stall
            foreach (var d in days)
                counts[Index(StateOf(d.Vertical))]++;

            var pct = Percentages(counts);
            return new TeamStateTrailPointDto
            {
                Cruise = pct[0],
                Drift = pct[1],
                Stall = pct[2],
                Headwinds = days.Count == 0
                    ? 0
                    : (int)Math.Round(days.Count(d => d.HeadwindsFlag) * 100.0 / days.Count, MidpointRounding.AwayFromZero),
                DaysCounted = days.Count
            };
        }

        private static string DateOf(DateOnly startDate, int day) =>
            startDate.AddDays(day - 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        internal static string StateOf(string? vertical)
        {
            return vertical?.Trim().ToLowerInvariant() switch
            {
                "drift" => TeamStates.Drift,
                "stall" => TeamStates.Stall,
                _ => TeamStates.Cruise
            };
        }

        private static int Index(string state) => state switch
        {
            TeamStates.Drift => 1,
            TeamStates.Stall => 2,
            _ => 0
        };

        // Whole percentages that always sum to 100: floor each share, then hand the
        // remaining points to the largest remainders (ties to the earlier state).
        private static int[] Percentages(int[] counts)
        {
            var total = counts.Sum();
            var result = new int[counts.Length];
            if (total == 0) return result;

            var remainders = new double[counts.Length];
            for (var i = 0; i < counts.Length; i++)
            {
                var exact = counts[i] * 100.0 / total;
                result[i] = (int)Math.Floor(exact);
                remainders[i] = exact - result[i];
            }

            var left = 100 - result.Sum();
            foreach (var i in Enumerable.Range(0, counts.Length).OrderByDescending(i => remainders[i]).ThenBy(i => i).Take(left))
                result[i]++;

            return result;
        }

        private static TeamMilestoneResult<TeamStateDto> Fail(TeamMilestoneError error, string message) =>
            TeamMilestoneResult<TeamStateDto>.Fail(error, message);
    }
}
