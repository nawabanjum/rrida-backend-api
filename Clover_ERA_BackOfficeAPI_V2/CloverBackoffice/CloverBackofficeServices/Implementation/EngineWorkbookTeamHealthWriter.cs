using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Infrastructure.Models.TeamQuestions;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using static CloverBackoffice.Services.Implementation.EngineWorkbookReader;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Writes an imported workbook's check-ins to Team Health's own tables, which are the
    /// back-office question banks rather than the engine's kit:
    ///   engine question → DailyFeedback (+ DailyFeedbackReasons), by CLOVER element
    ///   team question   → TeamDailyFeedback (+ TeamDailyFeedbackReasons), by team condition
    ///   custom question → CustomQuestions + CustomQuestionDailyFeedback (+ audits)
    ///
    /// The two banks do not share ids, so each answer is placed on a back-office question
    /// of the same CLOVER element (or team condition) and given that question's first
    /// positive reason (Green, Yellow) or negative reason (Orange, Red), as the Team Health
    /// demo seed does. The kit's team "Leadership" element has no back-office condition and
    /// is left out of TeamDailyFeedback (the engine still has it).
    ///
    /// Every row written here carries a marker in its timestamp, seconds = 42.424242, which
    /// is the only thing a re-import uses to find and clear them. Real answers never match.
    /// </summary>
    public sealed class EngineWorkbookTeamHealthWriter
    {
        // 42.424242 seconds past the minute, as Postgres extract(microseconds) reports it.
        private const long MarkerMicroseconds = 42_424_242;
        private static readonly TimeSpan Marker = TimeSpan.FromTicks(MarkerMicroseconds * 10);
        private const uint ContentCultureId = 1;

        internal static readonly Dictionary<string, int> CloverByElement = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Communication"] = 1, ["Learning"] = 2, ["Opportunity"] = 3, ["Opportunities"] = 3,
            ["Vulnerability"] = 4, ["Enablement"] = 5, ["Reflection"] = 6
        };

        internal static readonly Dictionary<string, TeamSubCategory> ConditionByElement = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Goal Affinity"] = TeamSubCategory.GoalAffinity,
            ["Results"] = TeamSubCategory.Results,
            ["Learning"] = TeamSubCategory.ImprovementLoop,
            ["Structure"] = TeamSubCategory.Structure,
            ["Coordination Quality"] = TeamSubCategory.CoordinationQuality,
            ["Accountability"] = TeamSubCategory.Accountability,
            ["Potency"] = TeamSubCategory.Potency,
            ["Belonging"] = TeamSubCategory.Belonging
        };

        private readonly ApplicationDbContext _context;
        private readonly IRridaEngineContextFactory _engine;

        public EngineWorkbookTeamHealthWriter(ApplicationDbContext context, IRridaEngineContextFactory engine)
        {
            _context = context;
            _engine = engine;
        }

        public sealed record Written(int DailyFeedback, int TeamDailyFeedback, int Reasons, int CustomQuestions, int CustomAnswers);

        // A back-office question with one reason of each polarity.
        private sealed record Slot(uint QuestionId, uint PositiveReason, uint NegativeReason);

        public async Task<bool> HasImportedRowsAsync(uint unitId, CancellationToken ct)
        {
            var count = await _context.Database.SqlQuery<int>($"""
                SELECT ((SELECT count(*) FROM public."DailyFeedback"
                         WHERE "BusinessUnitId" = {unitId} AND extract(microseconds FROM "SubmissionDateTime") = {MarkerMicroseconds})
                      + (SELECT count(*) FROM public."TeamDailyFeedback"
                         WHERE "BusinessUnitId" = {unitId} AND extract(microseconds FROM "SubmissionDateTime") = {MarkerMicroseconds})
                      + (SELECT count(*) FROM public."CustomQuestions"
                         WHERE "UnitId" = {unitId} AND extract(microseconds FROM "CreatedAt") = {MarkerMicroseconds}))::int AS "Value"
                """).SingleAsync(ct);
            return count > 0;
        }

        /// <summary>Removes only the rows an earlier import wrote for this team. Reasons go by cascade.</summary>
        public async Task ClearImportedRowsAsync(uint unitId, CancellationToken ct)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);

            await _context.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM public."DailyFeedback"
                WHERE "BusinessUnitId" = {unitId} AND extract(microseconds FROM "SubmissionDateTime") = {MarkerMicroseconds}
                """, ct);
            await _context.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM public."TeamDailyFeedback"
                WHERE "BusinessUnitId" = {unitId} AND extract(microseconds FROM "SubmissionDateTime") = {MarkerMicroseconds}
                """, ct);
            await _context.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM public."CustomQuestionDailyFeedback"
                WHERE "CustomQuestionId" IN (SELECT "Id" FROM public."CustomQuestions"
                    WHERE "UnitId" = {unitId} AND extract(microseconds FROM "CreatedAt") = {MarkerMicroseconds})
                """, ct);
            await _context.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM public."CustomQuestions"
                WHERE "UnitId" = {unitId} AND extract(microseconds FROM "CreatedAt") = {MarkerMicroseconds}
                """, ct);

            await transaction.CommitAsync(ct);
        }

        public async Task<Written> WriteAsync(
            uint unitId, uint profileId, EngineTeamClock clock, List<Row> rows, int actorUserId,
            List<string> warnings, CancellationToken ct)
        {
            var individualSlots = await IndividualSlotsAsync(ct);
            var teamSlots = await TeamSlotsAsync(ct);
            var dates = AskedDates(clock, rows.Max(r => r.Day));
            var memberIndex = rows.Select(r => r.Member.ToLowerInvariant()).Distinct().Select((m, i) => (m, i))
                .ToDictionary(x => x.m, x => x.i);

            var rotation = _engine.Context.Rotation;
            var data = _engine.Context.Data;
            int daily = 0, team = 0, reasons = 0, leadershipSkipped = 0;

            await using var transaction = await _context.Database.BeginTransactionAsync(ct);

            foreach (var row in rows)
            {
                var at = Stamp(dates[row.Day], memberIndex[row.Member.ToLowerInvariant()]);

                if (row.Individual is not null)
                {
                    var element = data.QuestionOf(rotation.QuestionIdForDay(row.Day, "individual") ?? 0)?.Element;
                    if (element is not null && CloverByElement.TryGetValue(element, out var category)
                        && individualSlots.TryGetValue(category, out var options))
                    {
                        var slot = options[(row.Day - 1) % options.Count];
                        var reasonId = IsPositive(row.Individual) ? slot.PositiveReason : slot.NegativeReason;
                        var feedback = new DailyFeedback
                        {
                            SubmissionDateTime = at,
                            BusinessUnitId = unitId,
                            SentimentType = (DailyFeedbackSentimentType)SentimentType(row.Individual),
                            DailyQuestionId = slot.QuestionId,
                            DailyReasonId = reasonId,
                            FreeText = row.Individual.Text
                        };
                        _context.DailyFeedbacks.Add(feedback);
                        _context.DailyFeedbackReasons.Add(new DailyFeedbackReason { DailyFeedback = feedback, DailyReasonId = reasonId });
                        daily += 1;
                        reasons += 1;
                    }
                }

                if (row.Team is not null)
                {
                    var element = data.QuestionOf(rotation.QuestionIdForDay(row.Day, "team") ?? 0)?.Element;
                    if (element is not null && ConditionByElement.TryGetValue(element, out var condition)
                        && teamSlots.TryGetValue(condition, out var options))
                    {
                        var slot = options[(row.Day - 1) % options.Count];
                        var reasonId = IsPositive(row.Team) ? slot.PositiveReason : slot.NegativeReason;
                        var feedback = new TeamDailyFeedback
                        {
                            SubmissionDateTime = at,
                            BusinessUnitId = unitId,
                            SentimentType = (TeamDailyFeedbackSentimentType)SentimentType(row.Team),
                            TeamDailyQuestionId = slot.QuestionId,
                            TeamDailyReasonId = reasonId,
                            FreeText = row.Team.Text
                        };
                        _context.TeamDailyFeedback.Add(feedback);
                        _context.TeamDailyFeedbackReasons.Add(new TeamDailyFeedbackReason { TeamDailyFeedback = feedback, TeamDailyReasonId = reasonId });
                        team += 1;
                        reasons += 1;
                    }
                    else
                    {
                        leadershipSkipped += 1;
                    }
                }
            }

            await _context.SaveChangesAsync(ct);
            var (customQuestions, customAnswers) = await WriteCustomAsync(unitId, profileId, rows, dates, memberIndex, actorUserId, ct);
            await transaction.CommitAsync(ct);
            _context.ChangeTracker.Clear();

            if (leadershipSkipped > 0)
                warnings.Add($"{leadershipSkipped} team answers are on a condition Team Health has no bank for "
                             + "(e.g. Leadership); they are in the engine only.");
            if (individualSlots.Count < 6 || teamSlots.Count < 8)
                warnings.Add($"Team Health's question bank is incomplete ({individualSlots.Count}/6 CLOVER elements, "
                             + $"{teamSlots.Count}/8 conditions); answers on the missing ones are in the engine only.");

            return new Written(daily, team, reasons, customQuestions, customAnswers);
        }

        /// <summary>One question per distinct (day, text); every member's answer on that day goes under it.</summary>
        private async Task<(int Questions, int Answers)> WriteCustomAsync(
            uint unitId, uint profileId, List<Row> rows, Dictionary<int, DateOnly> dates,
            Dictionary<string, int> memberIndex, int actorUserId, CancellationToken ct)
        {
            var groups = rows.Where(r => r.CustomQuestion is not null)
                .GroupBy(r => (r.Day, Text: r.CustomQuestion!))
                .ToList();
            if (groups.Count == 0) return (0, 0);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var answers = 0;

            foreach (var group in groups)
            {
                var date = dates[group.Key.Day];
                var stamp = Stamp(date, 0).Date.AddHours(8).Add(Marker);
                var status = date < today ? CustomQuestionStatus.Expired
                    : date == today ? CustomQuestionStatus.Active
                    : CustomQuestionStatus.Scheduled;

                var question = new CustomQuestion
                {
                    Text = group.Key.Text,
                    ContentCultureId = ContentCultureId,
                    ProfileId = profileId,
                    UnitId = unitId,
                    ScheduledAt = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue).AddHours(8), DateTimeKind.Utc),
                    Status = status,
                    CreatedBy = actorUserId,
                    UpdatedBy = actorUserId,
                    CreatedAt = stamp,
                    UpdatedAt = stamp
                };
                _context.CustomQuestions.Add(question);
                await _context.SaveChangesAsync(ct);

                _context.CustomQuestionAudits.Add(new CustomQuestionAudit
                {
                    CustomQuestionId = question.Id,
                    Text = question.Text,
                    ContentCultureId = question.ContentCultureId,
                    ProfileId = profileId,
                    UnitId = unitId,
                    ScheduledAt = question.ScheduledAt,
                    Status = question.Status,
                    ChangeSummary = "Imported from test-data workbook.",
                    CreatedBy = actorUserId,
                    UpdatedBy = actorUserId,
                    CreatedAt = stamp,
                    UpdatedAt = stamp,
                    LoggedAt = DateTime.UtcNow,
                    VersionNumber = 1,
                    OperationType = "INSERT"
                });

                var feedback = group.Select(r => new CustomQuestionDailyFeedback
                {
                    CustomQuestionId = question.Id,
                    UnitId = unitId,
                    FreeText = r.CustomAnswer,
                    CreatedAt = Stamp(date, memberIndex[r.Member.ToLowerInvariant()]),
                    UpdatedAt = Stamp(date, memberIndex[r.Member.ToLowerInvariant()])
                }).ToList();
                _context.CustomQuestionDailyFeedback.AddRange(feedback);
                await _context.SaveChangesAsync(ct);

                _context.CustomQuestionDailyFeedbackAudits.AddRange(feedback.Select(f => new CustomQuestionDailyFeedbackAudit
                {
                    CustomQuestionDailyFeedbackId = f.Id,
                    CustomQuestionId = f.CustomQuestionId,
                    UnitId = unitId,
                    FreeText = f.FreeText,
                    ChangeSummary = f.FreeText is null ? "Answer skipped." : "Answer recorded.",
                    CreatedAt = f.CreatedAt,
                    UpdatedAt = f.UpdatedAt,
                    LoggedAt = DateTime.UtcNow,
                    VersionNumber = 1,
                    OperationType = "INSERT"
                }));
                await _context.SaveChangesAsync(ct);
                answers += feedback.Count;
            }

            return (groups.Count, answers);
        }

        // ── banks ────────────────────────────────────────────────────────────

        private async Task<Dictionary<int, List<Slot>>> IndividualSlotsAsync(CancellationToken ct)
        {
            var specs = await _context.DailyQuestionSpecifications.AsNoTracking()
                .Where(s => s.DailyQuestionSetGroupId == 1)
                .OrderBy(s => s.Id)
                .Select(s => new
                {
                    Category = (int)s.CloverCategory,
                    QuestionId = s.Question.Id,
                    Positive = s.Reasons.Where(r => r.DailyReasonSentimentType == DailyReasonSentimentType.Positive).Min(r => (uint?)r.Id),
                    Negative = s.Reasons.Where(r => r.DailyReasonSentimentType == DailyReasonSentimentType.Negative).Min(r => (uint?)r.Id)
                })
                .ToListAsync(ct);

            return specs.Where(s => s.Positive != null && s.Negative != null)
                .GroupBy(s => s.Category)
                .ToDictionary(g => g.Key, g => g.Select(s => new Slot(s.QuestionId, s.Positive!.Value, s.Negative!.Value)).ToList());
        }

        private async Task<Dictionary<TeamSubCategory, List<Slot>>> TeamSlotsAsync(CancellationToken ct)
        {
            var specs = await _context.TeamDailyQuestionSpecifications.AsNoTracking()
                .OrderBy(s => s.Id)
                .Select(s => new
                {
                    s.SubCategory,
                    s.TeamDailyQuestionId,
                    Positive = s.Reasons.Where(r => r.TeamDailyReasonSentimentType == TeamDailyReasonSentimentType.Positive).Min(r => (uint?)r.Id),
                    Negative = s.Reasons.Where(r => r.TeamDailyReasonSentimentType == TeamDailyReasonSentimentType.Negative).Min(r => (uint?)r.Id)
                })
                .ToListAsync(ct);

            return specs.Where(s => s.Positive != null && s.Negative != null)
                .GroupBy(s => s.SubCategory)
                .ToDictionary(g => g.Key, g => g.Select(s => new Slot(s.TeamDailyQuestionId, s.Positive!.Value, s.Negative!.Value)).ToList());
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static Dictionary<int, DateOnly> AskedDates(EngineTeamClock clock, int askedDays)
        {
            var dates = new Dictionary<int, DateOnly>();
            for (var day = 1; dates.Count < askedDays; day += 1)
            {
                var date = clock.DateOfDay(day);
                if (clock.IsAskedOn(date)) dates[dates.Count + 1] = date;
            }

            return dates;
        }

        // 16:00 UTC plus a minute per member, so a team's answers spread through the
        // afternoon, and the marker seconds that identify an imported row.
        private static DateTime Stamp(DateOnly date, int member) =>
            DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(16, 0)), DateTimeKind.Utc)
                .AddMinutes(member % 600)
                .Add(Marker);

        private static bool IsPositive(Section section) => section.Sentiment is "green" or "yellow";

        // Red→Angry 1, Orange→Sad 2, Yellow→Indifferent 3, Green→Happy 4 (same numbers in both enums).
        private static int SentimentType(Section section) => section.Sentiment switch
        {
            "red" => 1,
            "orange" => 2,
            "yellow" => 3,
            _ => 4
        };
    }
}
