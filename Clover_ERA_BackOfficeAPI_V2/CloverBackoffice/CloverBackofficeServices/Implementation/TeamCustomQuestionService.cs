using System.Globalization;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.TeamQuestions;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // "Schedule a custom question" on the Team Health page.
    //
    // A custom question is a CustomQuestions row: Status Scheduled, ScheduledAt =
    // the chosen day at 16:00 UTC (the check-in time). The mobile feed
    // (api/team-daily-question/custom-questions) serves it on that UTC day and the
    // expiry job moves it to Expired after it, so nothing else has to change.
    //
    // Rules (from the mockup, enforced here rather than trusted to the client):
    //   question   required, at most 140 characters
    //   askOn      a working day (Mon–Fri), today or later; today only before 16:00 UTC
    //   slots      at most 3 scheduled at once per team
    //   one a day  never two questions on the same day for a team
    //   duplicate  the same wording cannot be scheduled twice at once
    //   remove     only while still scheduled and before anyone has answered;
    //              the row is kept as Cancelled so its history survives
    //
    // Every write is snapshotted into CustomQuestionAudits, as the existing
    // repository does. Access is ITeamHealthAccessService, as for the timeline.
    public class TeamCustomQuestionService : ITeamCustomQuestionService
    {
        public const int MaxSlots = 3;
        public const int QuestionMaxLength = 140;
        public const int PreviouslyAskedLimit = 20;
        public static readonly TimeOnly CheckInTimeUtc = new(16, 0);

        // First key of pg_advisory_xact_lock(int, int); the second is the unit id.
        // Two schedules for one team queue here, so they can never both take the
        // last slot or the same day.
        private const int ScheduleLockNamespace = 16022;

        private readonly ApplicationDbContext _context;
        private readonly ITeamHealthAccessService _access;
        private readonly TimeProvider _clock;

        public TeamCustomQuestionService(ApplicationDbContext context, ITeamHealthAccessService access, TimeProvider? clock = null)
        {
            _context = context;
            _access = access;
            _clock = clock ?? TimeProvider.System;
        }

        // ── Read ─────────────────────────────────────────────────────────────

        public async Task<TeamMilestoneResult<TeamCustomQuestionsDto>> GetAsync(string? teamId, int actorUserId, string? from = null, string? to = null, CancellationToken ct = default)
        {
            if (!uint.TryParse(teamId?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var unitId) || unitId == 0)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.TimelineTeamIdInvalid);

            // Same rules as GET milestones: both optional, inclusive, YYYY-MM-DD or DD-MM-YYYY.
            DateOnly? fromDate = null, toDate = null;
            if (!string.IsNullOrWhiteSpace(from))
            {
                fromDate = TeamHealthDates.Parse(from);
                if (fromDate == null)
                    return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.MilestoneDateInvalid);
            }
            if (!string.IsNullOrWhiteSpace(to))
            {
                toDate = TeamHealthDates.Parse(to);
                if (toDate == null)
                    return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.MilestoneDateInvalid);
            }
            if (fromDate > toDate)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.MilestoneDateRangeInvalid);

            var team = await CheckTeamAsync(unitId, actorUserId, ct);
            if (team.Denied != null)
                return team.Denied;

            return TeamMilestoneResult<TeamCustomQuestionsDto>.Ok(await BuildAsync(unitId, team.TeamSize, ct, fromDate, toDate));
        }

        // ── Schedule ─────────────────────────────────────────────────────────

        public async Task<TeamMilestoneResult<TeamCustomQuestionsDto>> ScheduleAsync(ScheduleCustomQuestionDto? dto, int actorUserId, CancellationToken ct = default)
        {
            var unitId = dto?.TeamId ?? 0;
            if (unitId == 0)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.TimelineTeamIdInvalid);

            var team = await CheckTeamAsync(unitId, actorUserId, ct);
            if (team.Denied != null)
                return team.Denied;

            var text = dto!.Question?.Trim();
            if (string.IsNullOrEmpty(text))
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.CustomQuestionTextRequired);
            if (text.Length > QuestionMaxLength)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.CustomQuestionTextTooLong);

            if (string.IsNullOrWhiteSpace(dto.AskOn))
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.CustomQuestionDateRequired);
            var askOn = TeamHealthDates.Parse(dto.AskOn);
            if (askOn == null)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.MilestoneDateInvalid);

            if (askOn.Value.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.CustomQuestionWeekend);

            var now = _clock.GetUtcNow().UtcDateTime;
            var today = DateOnly.FromDateTime(now);
            if (askOn.Value < today)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.CustomQuestionDatePassed);
            if (askOn.Value == today && TimeOnly.FromDateTime(now) >= CheckInTimeUtc)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.CustomQuestionTodayTooLate);

            await using var transaction = await _context.Database.BeginTransactionAsync(ct);

            if (IsPostgres)
                await _context.Database.ExecuteSqlRawAsync(
                    $"SELECT pg_advisory_xact_lock({ScheduleLockNamespace}, {unitId})", ct);

            // Read under the lock, so the checks below see every committed schedule.
            var upcoming = await UpcomingQuery(unitId, today).ToListAsync(ct);

            if (upcoming.Count >= MaxSlots)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.CustomQuestionSlotsFull);

            if (upcoming.Any(q => DateOnly.FromDateTime(q.ScheduledAt!.Value.ToUniversalTime()) == askOn.Value))
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.CustomQuestionDayTaken);

            if (upcoming.Any(q => string.Equals(q.Text.Trim(), text, StringComparison.OrdinalIgnoreCase)))
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.CustomQuestionDuplicate);

            var cultureId = await _context.ContentCultures
                .Where(c => c.CultureName == DataConstants.DefaultContentCultureName)
                .Select(c => (uint?)c.Id)
                .FirstOrDefaultAsync(ct)
                ?? await _context.ContentCultures.Select(c => (uint?)c.Id).FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException("No ContentCulture entries exist.");

            var question = new CustomQuestion
            {
                Text = text,
                ContentCultureId = cultureId,
                ProfileId = team.ProfileId,
                UnitId = unitId,
                ScheduledAt = askOn.Value.ToDateTime(CheckInTimeUtc, DateTimeKind.Utc),
                Status = CustomQuestionStatus.Scheduled,
                CreatedBy = actorUserId,
                UpdatedBy = actorUserId,
                CreatedAt = now,
                UpdatedAt = now,
                IsDel = false
            };
            _context.CustomQuestions.Add(question);
            await _context.SaveChangesAsync(ct);

            _context.CustomQuestionAudits.Add(Audit(question, now, 1, "INSERT",
                $"Scheduled from Team Health for {Iso(askOn.Value)}."));
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return TeamMilestoneResult<TeamCustomQuestionsDto>.Ok(await BuildAsync(unitId, team.TeamSize, ct));
        }

        // ── Remove ───────────────────────────────────────────────────────────

        public async Task<TeamMilestoneResult<TeamCustomQuestionsDto>> RemoveAsync(uint customQuestionId, int actorUserId, CancellationToken ct = default)
        {
            if (customQuestionId == 0)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.CustomQuestionNotFound);

            var question = await _context.CustomQuestions
                .FirstOrDefaultAsync(q => q.Id == customQuestionId && !q.IsDel, ct);
            if (question == null)
                return Fail(TeamMilestoneError.NotFound, ResponseMessages.Errors.CustomQuestionNotFound);

            var team = await CheckTeamAsync(question.UnitId, actorUserId, ct);
            if (team.Denied != null)
                return team.Denied;

            var now = _clock.GetUtcNow().UtcDateTime;
            var today = DateOnly.FromDateTime(now);

            var stillScheduled = question.ScheduledAt.HasValue
                                 && DateOnly.FromDateTime(question.ScheduledAt.Value.ToUniversalTime()) >= today
                                 && question.Status is CustomQuestionStatus.Scheduled or CustomQuestionStatus.Active;
            if (!stillScheduled)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.CustomQuestionNotRemovable);

            if (await _context.CustomQuestionDailyFeedback.AnyAsync(f => f.CustomQuestionId == question.Id, ct))
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.CustomQuestionAlreadyAnswered);

            var lastVersion = await _context.CustomQuestionAudits
                .Where(a => a.CustomQuestionId == question.Id)
                .Select(a => (int?)a.VersionNumber)
                .MaxAsync(ct) ?? 0;

            var previous = question.Status;
            question.Status = CustomQuestionStatus.Cancelled;
            question.UpdatedBy = actorUserId;
            question.UpdatedAt = now;

            _context.CustomQuestionAudits.Add(Audit(question, now, lastVersion + 1, "CANCEL",
                $"Moved from {previous} to Cancelled: removed from the Team Health schedule."));
            await _context.SaveChangesAsync(ct);

            return TeamMilestoneResult<TeamCustomQuestionsDto>.Ok(await BuildAsync(question.UnitId, team.TeamSize, ct));
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private bool IsPostgres => _context.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true;

        // Live questions on or after today: they hold the slots.
        private IQueryable<CustomQuestion> UpcomingQuery(uint unitId, DateOnly today)
        {
            var todayStart = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            return _context.CustomQuestions
                .Where(q => q.UnitId == unitId
                            && !q.IsDel
                            && q.ScheduledAt != null
                            && q.ScheduledAt >= todayStart
                            && (q.Status == CustomQuestionStatus.Scheduled || q.Status == CustomQuestionStatus.Active));
        }

        // from / to narrow only the previously asked list (by the day asked) and lift
        // its PreviouslyAskedLimit, so a range returns every question asked in it. The
        // scheduled list is never narrowed: it backs the slot counter.
        private async Task<TeamCustomQuestionsDto> BuildAsync(uint unitId, int? teamSize, CancellationToken ct, DateOnly? from = null, DateOnly? to = null)
        {
            var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
            var todayStart = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

            var upcoming = await UpcomingQuery(unitId, today)
                .AsNoTracking()
                .OrderBy(q => q.ScheduledAt)
                .ThenBy(q => q.Id)
                .Select(q => new { q.Id, q.Text, q.ScheduledAt })
                .ToListAsync(ct);

            // Asked = its day is over and it was published (not Draft, not Cancelled).
            var askedQuery = _context.CustomQuestions
                .AsNoTracking()
                .Where(q => q.UnitId == unitId
                            && !q.IsDel
                            && q.ScheduledAt != null
                            && q.ScheduledAt < todayStart
                            && (q.Status == CustomQuestionStatus.Scheduled
                                || q.Status == CustomQuestionStatus.Active
                                || q.Status == CustomQuestionStatus.Expired));
            if (from.HasValue)
            {
                var fromUtc = from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                askedQuery = askedQuery.Where(q => q.ScheduledAt >= fromUtc);
            }
            if (to.HasValue)
            {
                var toExclusiveUtc = to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                askedQuery = askedQuery.Where(q => q.ScheduledAt < toExclusiveUtc);
            }

            askedQuery = askedQuery
                .OrderByDescending(q => q.ScheduledAt)
                .ThenByDescending(q => q.Id);
            if (!from.HasValue && !to.HasValue)
                askedQuery = askedQuery.Take(PreviouslyAskedLimit);

            var asked = await askedQuery
                .Select(q => new { q.Id, q.Text, q.ScheduledAt })
                .ToListAsync(ct);

            var ids = upcoming.Select(q => q.Id).Concat(asked.Select(q => q.Id)).ToList();
            var answerCounts = await _context.CustomQuestionDailyFeedback
                .AsNoTracking()
                .Where(f => ids.Contains(f.CustomQuestionId))
                .GroupBy(f => f.CustomQuestionId)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

            // The answer texts for the asked questions, same as the timeline's custom
            // question card: only answers with text, only the text and the answer's id.
            var askedIds = asked.Select(q => q.Id).ToList();
            var answerTexts = (await _context.CustomQuestionDailyFeedback
                    .AsNoTracking()
                    .Where(f => askedIds.Contains(f.CustomQuestionId) && f.UnitId == unitId && f.FreeText != null)
                    .OrderBy(f => f.Id)
                    .Select(f => new { f.Id, f.CustomQuestionId, f.FreeText })
                    .ToListAsync(ct))
                .Where(f => !string.IsNullOrWhiteSpace(f.FreeText))
                .ToLookup(f => f.CustomQuestionId);

            int? possible = teamSize is > 0 ? teamSize : null;

            return new TeamCustomQuestionsDto
            {
                TeamId = unitId.ToString(CultureInfo.InvariantCulture),
                MaxSlots = MaxSlots,
                SlotsUsed = upcoming.Count,
                Scheduled = upcoming.Select(q => new ScheduledCustomQuestionDto
                {
                    Id = q.Id,
                    Question = q.Text,
                    AskOn = Iso(DateOnly.FromDateTime(q.ScheduledAt!.Value.ToUniversalTime())),
                    CanRemove = !answerCounts.ContainsKey(q.Id)
                }).ToList(),
                PreviouslyAsked = asked.Select(q =>
                {
                    var answers = answerCounts.GetValueOrDefault(q.Id);
                    return new AskedCustomQuestionDto
                    {
                        Id = q.Id,
                        Question = q.Text,
                        AskedOn = Iso(DateOnly.FromDateTime(q.ScheduledAt!.Value.ToUniversalTime())),
                        Answers = answers,
                        Possible = possible,
                        ResponseRate = possible.HasValue
                            ? Math.Min(100, (int)Math.Round(answers * 100.0 / possible.Value, MidpointRounding.AwayFromZero))
                            : null,
                        Responses = answerTexts[q.Id]
                            .Select(f => new TimelineCustomQuestionAnswerDto { ResponseId = f.Id, ResponseText = f.FreeText!.Trim() })
                            .ToList()
                    };
                }).ToList()
            };
        }

        private sealed record TeamCheck(TeamMilestoneResult<TeamCustomQuestionsDto>? Denied, uint ProfileId, int? TeamSize);

        private async Task<TeamCheck> CheckTeamAsync(uint unitId, int actorUserId, CancellationToken ct)
        {
            var unit = await _context.BusinessUnits
                .AsNoTracking()
                .Where(u => u.Id == unitId && u.IsDel != true)
                .Select(u => new { u.ProfileId, u.TeamSize })
                .FirstOrDefaultAsync(ct);
            if (unit == null)
                return new(Fail(TeamMilestoneError.NotFound, ResponseMessages.Errors.MilestoneTeamNotFound), 0, null);

            if (!await _access.CanAccessTeamAsync(unitId, unit.ProfileId, actorUserId, ct))
                return new(Fail(TeamMilestoneError.Forbidden, ResponseMessages.Errors.CustomQuestionAccessDenied), 0, null);

            return new(null, unit.ProfileId, unit.TeamSize);
        }

        private static CustomQuestionAudit Audit(CustomQuestion q, DateTime now, int version, string operation, string summary) => new()
        {
            CustomQuestionId = q.Id,
            Text = q.Text,
            ContentCultureId = q.ContentCultureId,
            ProfileId = q.ProfileId,
            UnitId = q.UnitId,
            ScheduledAt = q.ScheduledAt,
            Status = q.Status,
            ChangeSummary = summary,
            CreatedBy = q.CreatedBy,
            UpdatedBy = q.UpdatedBy,
            CreatedAt = q.CreatedAt,
            UpdatedAt = q.UpdatedAt,
            IsDel = q.IsDel,
            LoggedAt = now,
            VersionNumber = version,
            OperationType = operation
        };

        private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static TeamMilestoneResult<TeamCustomQuestionsDto> Fail(TeamMilestoneError error, string message) =>
            TeamMilestoneResult<TeamCustomQuestionsDto>.Fail(error, message);
    }
}
