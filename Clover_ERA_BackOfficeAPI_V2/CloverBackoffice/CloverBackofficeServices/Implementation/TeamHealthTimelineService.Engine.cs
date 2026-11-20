using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.TeamQuestions;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // The timeline for a team on the deterministic engine, from the engine's own answers
    // (EngineAnswers — what the engine check-in writes and the engine is replayed from),
    // calculated as the MVP calculates its board (RRIDA-Engine src/engine/state.js
    // dailyLines / lineSeries, src/server.js statTiles):
    //
    //   level          LEVEL_SCALE as a percentage: red 0, orange 33.3, yellow 66.7, green 100
    //   line 1         a day's individual answers, averaged — Individual Engagement
    //   line 2         a day's team answers, averaged — Team Conditions; like line 1, a day
    //                  with no team answer has no point, so the line stops (client's call:
    //                  the MVP carries the last reading forward, the chart here does not)
    //   tile           the mean of the last seven days' points that had answers, and its
    //                  change against the seven before (statTiles)
    //   response rate  screens answered against team size x 2 (screen 1 and screen 2 counted
    //                  separately, so one screen is a partial), over the team's last seven
    //                  asked days — complete days, so up to yesterday, as the engine itself
    //                  is run. The team size is the headcount; members enrolled only where
    //                  no size is set (TeamHeadcount)
    //
    // So the chart and the engine's state read the same answers on the same scale, which is
    // why the MVP built its chart from the engine series. The CLOVER and Team Conditions
    // drill-downs keep their shape and rules, fed from the same answers on the same scale.
    // A team not on the engine keeps the back-office tables (TeamHealthTimelineService.cs).
    public partial class TeamHealthTimelineService
    {
        private const int TileDays = 7;

        private sealed record EnginePoint(DateOnly Date, double? Line1, double? Line2, int Responses, int TeamResponses);

        private sealed record EngineReading(
            ScoreSeriesDto IndividualEngagement,
            ScoreSeriesDto TeamConditions,
            List<Answer> CurrentIndividual,
            List<Answer> PreviousIndividual,
            List<Answer> CurrentTeam,
            ResponseRateDetailsDto ResponseRate,
            ScreenCompletionDto ScreenCompletion);

        /// <summary>MVP LEVEL_SCALE, as the percentage the board shows. Null for a level the kit does not have.</summary>
        internal static double? EngineLevelScore(string? level) => level?.Trim().ToLowerInvariant() switch
        {
            "red" => 0,
            "orange" => 100.0 / 3,
            "yellow" => 200.0 / 3,
            "green" => 100,
            _ => null
        };

        /// <summary>Null when the team is not on the engine, or no engine is configured.</summary>
        private async Task<EngineReading?> LoadEngineReadingAsync(
            uint unitId, int? teamSize, DateOnly today, DateOnly periodStart, DateOnly previousStart, CancellationToken ct)
        {
            if (_engine is null) return null;
            var settings = await _context.EngineTeamSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.BusinessUnitId == unitId, ct);
            if (settings is null) return null;

            var clock = new EngineTeamClock(settings.StartDate, settings.WorkingWeek, _engine.Calendar);
            var data = _engine.Context.Data;

            var from = previousStart;
            var rows = await _context.EngineAnswers.AsNoTracking()
                .Where(a => a.BusinessUnitId == unitId && a.AnswerDate >= from && a.AnswerDate <= today)
                .Select(a => new { a.AnswerDate, a.Track, a.QuestionKey, a.Level, a.MemberId })
                .ToListAsync(ct);

            // ── The two lines, day by day (lineSeries) ──
            var byDate = rows.GroupBy(r => r.AnswerDate).ToDictionary(g => g.Key, g => g.ToList());
            var points = new List<EnginePoint>();
            for (var date = from; date <= today; date = date.AddDays(1))
            {
                var day = byDate.GetValueOrDefault(date) ?? [];
                var individual = day.Where(r => r.Track == "individual")
                    .Select(r => EngineLevelScore(r.Level)).OfType<double>().ToList();
                var teamTrack = day.Where(r => r.Track == "team")
                    .Select(r => EngineLevelScore(r.Level)).OfType<double>().ToList();

                points.Add(new EnginePoint(
                    date,
                    individual.Count > 0 ? individual.Average() : null,
                    teamTrack.Count > 0 ? teamTrack.Average() : null,
                    individual.Count,
                    teamTrack.Count));
            }

            var inPeriod = points.Where(p => p.Date >= periodStart).ToList();

            // ── The drill-downs, from the same answers on the same scale ──
            var answers = new List<Answer>();
            foreach (var row in rows)
            {
                var weight = EngineLevelScore(row.Level);
                var element = data.QuestionOf(row.QuestionKey)?.Element;
                if (weight is null || element is null) continue;

                if (row.Track == "individual"
                    && EngineWorkbookTeamHealthWriter.CloverByElement.TryGetValue(element, out var category))
                {
                    answers.Add(new Answer(row.AnswerDate, weight.Value, category, default, default));
                }
                else if (row.Track == "team"
                         && EngineWorkbookTeamHealthWriter.ConditionByElement.TryGetValue(element, out var condition))
                {
                    answers.Add(new Answer(row.AnswerDate, weight.Value, 0, GroupOf(condition), condition));
                }
            }

            var individualAnswers = answers.Where(a => a.Category != 0).ToList();
            var teamAnswers = answers.Where(a => a.Category == 0).ToList();

            var (responseRate, screens) = await EngineResponseRateAsync(
                unitId, teamSize, clock, today, rows.Select(r => (r.AnswerDate, r.MemberId, r.Track)).ToList(), ct);

            return new EngineReading(
                EngineSeries(points, inPeriod, today, p => p.Line1),
                EngineSeries(points, inPeriod, today, p => p.Line2),
                individualAnswers.Where(a => a.Day >= periodStart).ToList(),
                individualAnswers.Where(a => a.Day < periodStart).ToList(),
                teamAnswers.Where(a => a.Day >= periodStart).ToList(),
                responseRate,
                screens);
        }

        /// <summary>A line for the chart, and its tile (statTiles): last seven days against the seven before.</summary>
        private static ScoreSeriesDto EngineSeries(
            List<EnginePoint> all, List<EnginePoint> inPeriod, DateOnly today, Func<EnginePoint, double?> line)
        {
            double? Mean(DateOnly from, DateOnly to)
            {
                // Days with no answers have no point, so a silent day does not pull the
                // tile toward nothing.
                var values = all
                    .Where(p => p.Date >= from && p.Date <= to && (p.Responses > 0 || p.TeamResponses > 0))
                    .Select(line).OfType<double>().ToList();
                return values.Count > 0 ? values.Average() : null;
            }

            var current = Round(Mean(today.AddDays(-(TileDays - 1)), today));
            var prior = Round(Mean(today.AddDays(-(2 * TileDays - 1)), today.AddDays(-TileDays)));

            return new ScoreSeriesDto
            {
                Current = current,
                Change = Change(current, prior),
                Data = inPeriod
                    .Where(p => line(p) is not null)
                    .Select(p => new DailyScoreDto { Date = Iso(p.Date), Score = Round(line(p))!.Value })
                    .ToList()
            };
        }

        /// <summary>
        /// Screens answered against screens possible, per asked day, over the team's last seven
        /// asked days up to yesterday. Screen 1 (the individual question) and screen 2 (the
        /// team question) are counted separately, as on a back-office team: possible = headcount
        /// x 2 a day, and a member who answers only one screen is a partial response (1 of 2).
        /// The headcount is the team size the manager set (TeamHeadcount.DisplaySizeOn), and
        /// members enrolled only where none is set: an enrolment is a device, so a reinstall
        /// would otherwise count one person twice. `answered` covers the chart's two periods
        /// (sixty days), which always holds the last seven asked days. Each screen's completion
        /// is that screen alone against the headcount.
        /// </summary>
        private async Task<(ResponseRateDetailsDto Details, ScreenCompletionDto Screens)> EngineResponseRateAsync(
            uint unitId, int? teamSize, EngineTeamClock clock, DateOnly today,
            List<(DateOnly Date, long MemberId, string Track)> answered, CancellationToken ct)
        {
            var sizes = await TeamHeadcount.LoadHistoryAsync(_context, unitId, ct);

            var days = new List<DateOnly>(ResponseRateWorkingDays);
            for (var d = today.AddDays(-1); days.Count < ResponseRateWorkingDays && d >= clock.StartDate; d = d.AddDays(-1))
            {
                if (clock.IsAskedOn(d)) days.Add(d);
            }
            days.Reverse();

            var memberships = await _context.EngineTeamMemberships.AsNoTracking()
                .Where(m => m.BusinessUnitId == unitId)
                .Select(m => new { m.MemberId, m.EffectiveFrom, m.EffectiveTo })
                .ToListAsync(ct);

            var data = new List<DailyRateDto>();
            var totalAnswered = 0;
            var totalPossible = 0;
            var peopleDays = 0;
            var screen1 = 0;
            var screen2 = 0;
            var exceeded = false;
            foreach (var day in days)
            {
                var onDay = answered.Where(a => a.Date == day).ToList();
                // Each screen counts on its own: one member answering both is 2, one screen is 1.
                var individual = onDay.Where(a => a.Track == "individual").Select(a => a.MemberId).Distinct().Count();
                var team = onDay.Where(a => a.Track == "team").Select(a => a.MemberId).Distinct().Count();
                var responded = onDay.Select(a => a.MemberId).Distinct().Count();
                var enrolled = memberships
                    .Where(m => m.EffectiveFrom <= day && (m.EffectiveTo == null || m.EffectiveTo >= day))
                    .Select(m => m.MemberId).Distinct().Count();
                var headcount = TeamHeadcount.DisplaySizeOn(sizes, teamSize, day, enrolled);
                var possibleToday = headcount * AnswersPerMemberPerDay;

                // More people answered than the team size says: the size is out of date. The
                // rate is capped at 100 and the flag lets the page ask for the size to be updated.
                if (responded > headcount) exceeded = true;

                totalAnswered += individual + team;
                totalPossible += possibleToday;
                peopleDays += headcount;
                screen1 += individual;
                screen2 += team;
                data.Add(new DailyRateDto { Date = Iso(day), Rate = Rate(individual + team, possibleToday > 0 ? possibleToday : null) });
            }

            int? possible = totalPossible > 0 ? totalPossible : null;
            int? possiblePerScreen = peopleDays > 0 ? peopleDays : null;
            return (new ResponseRateDetailsDto
            {
                Current = Rate(totalAnswered, possible),
                Period = ResponseRatePeriods.LastSevenWorkingDays,
                Data = data,
                TeamSizeExceeded = exceeded,
                Summary = new ResponseRateSummaryDto { Answers = totalAnswered, Possible = possible }
            }, new ScreenCompletionDto
            {
                Screen1 = Rate(screen1, possiblePerScreen),
                Screen2 = Rate(screen2, possiblePerScreen)
            });
        }

        private static TeamElement GroupOf(TeamSubCategory condition) => condition switch
        {
            TeamSubCategory.GoalAffinity or TeamSubCategory.Results or TeamSubCategory.ImprovementLoop => TeamElement.DirectionAndLearning,
            TeamSubCategory.Structure or TeamSubCategory.CoordinationQuality or TeamSubCategory.Accountability => TeamElement.DesignAndMechanics,
            _ => TeamElement.WhatTheTeamBelieves
        };
    }
}
