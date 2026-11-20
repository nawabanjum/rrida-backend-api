using System.Security.Cryptography;
using System.Text;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.DTO.Mobile;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Infrastructure.Models.TeamQuestions;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Capture: enrolling a member, and recording what they answered.
    ///
    /// Everything written here is a FACT. The engine's state is replayed from these rows
    /// and never stored as a running total, so an answer is appended and never edited.
    ///
    /// Validation follows spec 4.3 and is done against the engine's own data bank rather
    /// than our copy of it: the chip must belong to the question the rotation actually
    /// asked, on the right track, in the set that matches the level the member picked.
    /// A reason is compulsory — zero chips is valid only with free text.
    /// </summary>
    public sealed class EngineCheckInService : IEngineCheckInService
    {
        private static readonly Dictionary<string, string> PolarityOfLevel =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // Red and Orange share one chip set: they differ in intensity, not in
                // cause, and the face already carries the intensity.
                ["red"] = "negative",
                ["orange"] = "negative",
                ["yellow"] = "indifferent",
                ["green"] = "positive"
            };

        private const int MaxChips = 3;

        // How many asked days engine/next returns — what the old fetch-next API gave the app
        // to prefetch questions and fill reminders.
        private const int NextAskedDayCount = 5;

        // How far ahead those days are looked for: five weeks, so a team asked once a week
        // still gets all five.
        private const int MaxDaysToNextAskedDays = 35;

        // TeamDayAsync reads a team's day with no member behind it. No member has this id,
        // so nothing reads as already answered.
        private const long NoMember = 0;

        private readonly ApplicationDbContext _context;
        private readonly IRridaEngineContextFactory _engine;
        private readonly IEngineTeamProvisioner _provisioner;
        private readonly ILogger<EngineCheckInService> _logger;

        public EngineCheckInService(
            ApplicationDbContext context,
            IRridaEngineContextFactory engine,
            IEngineTeamProvisioner provisioner,
            ILogger<EngineCheckInService> logger)
        {
            _context = context;
            _engine = engine;
            _provisioner = provisioner;
            _logger = logger;
        }

        public async Task<EngineLeaveResultDto> LeaveAsync(EngineLeaveRequestDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.MemberToken))
                throw new InvalidOperationException("A member token is required.");
            if (dto.BusinessUnitId == 0)
                throw new InvalidOperationException("A team is required.");

            var hash = HashToken(dto.MemberToken);
            var member = await _context.EngineMembers.AsNoTracking()
                .FirstOrDefaultAsync(m => m.TokenHash == hash, ct)
                ?? throw new InvalidOperationException("This member is not on that team.");

            var periods = await _context.EngineTeamMemberships
                .Where(m => m.MemberId == member.Id && m.BusinessUnitId == dto.BusinessUnitId)
                .ToListAsync(ct);
            if (periods.Count == 0)
                throw new InvalidOperationException("This member is not on that team.");

            // Membership is a period: closing it keeps who was enrolled on every past day
            // answerable, which the floor needs, and drops the member from tomorrow. Today
            // is kept, so an answer already given today still belongs to an enrolled member.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var open = periods.Where(p => p.EffectiveTo is null).ToList();
            foreach (var period in open)
                period.EffectiveTo = today < period.EffectiveFrom ? period.EffectiveFrom : today;

            if (open.Count > 0)
            {
                await _context.SaveChangesAsync(ct);
                _logger.LogInformation("[Engine] Member {Member} left unit {Unit} on {Day}.",
                    member.EngineMemberKey, dto.BusinessUnitId, today);
            }

            return new EngineLeaveResultDto
            {
                BusinessUnitId = dto.BusinessUnitId,
                EffectiveTo = periods.Max(p => p.EffectiveTo!.Value)
            };
        }

        public async Task<EngineEnrolResultDto> EnrolAsync(EngineEnrolRequestDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.MemberToken))
                throw new InvalidOperationException("A member token is required.");
            if (string.IsNullOrWhiteSpace(dto.TeamCode))
                throw new InvalidOperationException("A team code is required.");

            var code = dto.TeamCode.Trim();
            var unit = await _context.BusinessUnits.AsNoTracking()
                .Where(b => b.BusinessUnitShortCode == code && b.IsDel != true)
                .Select(b => new { b.Id, b.Name, b.ProfileId })
                .FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException("No team was found for that code.");

            // The engine is on by default, so a team nobody has configured is set up here
            // rather than refused. An admin's own settings are never overwritten.
            await _provisioner.EnsureAsync(unit.Id, ct);

            var settings = await _context.EngineTeamSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.BusinessUnitId == unit.Id, ct)
                ?? throw new InvalidOperationException(
                    "This team could not be set up for the deterministic layer.");

            var hash = HashToken(dto.MemberToken);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var member = await _context.EngineMembers.FirstOrDefaultAsync(m => m.TokenHash == hash, ct);
            if (member is null)
            {
                member = new EngineMember
                {
                    ProfileId = unit.ProfileId,
                    TokenHash = hash,
                    // Filled in below, once the row has an id. The key is opaque on
                    // purpose: a payload carrying it names nobody.
                    EngineMemberKey = "pending",
                    CreatedAt = DateTime.UtcNow
                };
                _context.EngineMembers.Add(member);
                await _context.SaveChangesAsync(ct);

                member.EngineMemberKey = $"m-{member.Id:D6}";
                await _context.SaveChangesAsync(ct);
            }

            var open = await _context.EngineTeamMemberships
                .FirstOrDefaultAsync(m => m.MemberId == member.Id
                                          && m.BusinessUnitId == unit.Id
                                          && m.EffectiveTo == null, ct);

            if (open is null)
            {
                // Membership is a period, not a flag: the floor's denominator has to be
                // answerable for a past date, not only for today.
                _context.EngineTeamMemberships.Add(new EngineTeamMembership
                {
                    MemberId = member.Id,
                    BusinessUnitId = unit.Id,
                    EffectiveFrom = today < settings.StartDate ? settings.StartDate : today,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync(ct);
            }

            return new EngineEnrolResultDto
            {
                EngineMemberKey = member.EngineMemberKey,
                BusinessUnitId = unit.Id,
                TeamName = unit.Name,
                EffectiveFrom = open?.EffectiveFrom ?? today,
                AlreadyEnrolled = open is not null
            };
        }

        public async Task<EngineTodayDto> TodayAsync(string memberToken, uint businessUnitId, CancellationToken ct = default)
        {
            var (member, settings, clock) = await ResolveMemberAsync(memberToken, businessUnitId, ct);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return await BuildDayAsync(member.Id, businessUnitId, settings, clock, today, ct);
        }

        public async Task<EngineTodayDto> TodayByCodeAsync(string memberToken, string businessUnitShortCode, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(businessUnitShortCode))
                throw new InvalidOperationException("A team code is required.");

            // Same lookup as enrol, so a code that enrols a member also fetches their day.
            // Nothing is provisioned here: a member can only be enrolled on a set-up team.
            var code = businessUnitShortCode.Trim();
            var businessUnitId = await _context.BusinessUnits.AsNoTracking()
                .Where(b => b.BusinessUnitShortCode == code && b.IsDel != true)
                .Select(b => (uint?)b.Id)
                .FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException("No team was found for that code.");

            return await TodayAsync(memberToken, businessUnitId, ct);
        }

        public async Task<EngineNextDto> NextAsync(string memberToken, uint businessUnitId, CancellationToken ct = default)
        {
            var (member, settings, clock) = await ResolveMemberAsync(memberToken, businessUnitId, ct);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var result = new EngineNextDto { BusinessUnitId = businessUnitId };
            foreach (var day in NextAskedDays(today, settings, clock))
                result.Days.Add(await BuildDayAsync(member.Id, businessUnitId, settings, clock, day, ct));
            return result;
        }

        public async Task<EngineTodayDto?> TeamDayAsync(uint businessUnitId, CancellationToken ct = default)
        {
            // As enrol does: an invite is usually what goes out before anyone has joined.
            await _provisioner.EnsureAsync(businessUnitId, ct);

            var settings = await _context.EngineTeamSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.BusinessUnitId == businessUnitId, ct);
            if (settings is null || !settings.IsEnabled) return null;

            var clock = new EngineTeamClock(settings.StartDate, settings.WorkingWeek, _engine.Calendar);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            if (clock.IsAskedOn(today) && today >= settings.StartDate)
                return await BuildDayAsync(NoMember, businessUnitId, settings, clock, today, ct);

            var next = NextAskedDays(today, settings, clock);
            return next.Count == 0
                ? null
                : await BuildDayAsync(NoMember, businessUnitId, settings, clock, next[0], ct);
        }

        // The next asked days after today, in date order, never before the team's start
        // date (Friday, then Monday, Tuesday … on a Mon-Fri week). Days the team is not
        // asked are skipped. Fewer than NextAskedDayCount when the window holds fewer.
        private static List<DateOnly> NextAskedDays(DateOnly today, EngineTeamSettings settings, IEngineTeamClock clock)
        {
            var days = new List<DateOnly>(NextAskedDayCount);
            var day = today.AddDays(1);
            if (day < settings.StartDate) day = settings.StartDate;

            for (var i = 0; i < MaxDaysToNextAskedDays && days.Count < NextAskedDayCount; i += 1, day = day.AddDays(1))
            {
                if (clock.IsAskedOn(day)) days.Add(day);
            }
            return days;
        }

        /// <summary>
        /// One day's check-in: the rotation's questions with their chip sets, and the custom
        /// question scheduled for that day. Shared by today and next, so both read the same.
        /// </summary>
        private async Task<EngineTodayDto> BuildDayAsync(
            long memberId, uint businessUnitId, EngineTeamSettings settings, IEngineTeamClock clock, DateOnly date, CancellationToken ct)
        {
            // D34. The production rotation: all ninety questions, one of each track per asked day.
            var ctx = _engine.Context;

            var result = new EngineTodayDto
            {
                BusinessUnitId = businessUnitId,
                Date = date,
                IsAskedToday = clock.IsAskedOn(date) && date >= settings.StartDate,
                Day = clock.DayOf(date),
                AskedDayIndex = clock.AskedDayIndexOf(date)
            };

            // Same lookup as fetch-daily-question: one question scheduled for this
            // unit on that day, or null. It follows its own schedule, not the rotation.
            var customDay = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            result.CustomQuestion = await _context.CustomQuestions.AsNoTracking()
                .Where(MobileApiRepository.CustomQuestionLiveOn(businessUnitId, customDay))
                .OrderBy(q => q.ScheduledAt)
                .ThenBy(q => q.Id)
                .Select(q => new CustomQuestionFetchResponse
                {
                    CustomDailyQuestionId = q.Id,
                    QuestionText = q.Text
                })
                .FirstOrDefaultAsync(ct);

            if (!result.IsAskedToday) return result;

            var answered = await _context.EngineAnswers.AsNoTracking()
                .Where(a => a.BusinessUnitId == businessUnitId && a.MemberId == memberId && a.AnswerDate == date)
                .Select(a => a.Track)
                .ToListAsync(ct);

            // The rotation is keyed on the team's ASKED-DAY index, never on the date. Using
            // the date here is the defect that reached a manager as a missing flag.
            foreach (var slot in ctx.Rotation.SlotsForDay(result.AskedDayIndex))
            {
                var q = slot.Question;
                var dto = new EngineTodayQuestionDto
                {
                    Track = slot.Track,
                    QuestionKey = q.Id,
                    Text = q.Text,
                    Element = q.Element,
                    AlreadyAnswered = answered.Contains(slot.Track, StringComparer.Ordinal)
                };

                foreach (var (polarity, chipIds) in q.Chips)
                {
                    dto.Chips[polarity] = chipIds
                        .Select(id => ctx.Data.Chip(id))
                        .Where(c => c is not null)
                        .Select((c, i) => new RefChipDto { ChipKey = c!.Id, Ordinal = i + 1, Text = c.Label })
                        .ToList();
                }

                result.Questions.Add(dto);
            }

            return result;
        }

        public async Task<EngineAnswerResultDto> SubmitAsync(EngineAnswerRequestDto dto, CancellationToken ct = default)
        {
            var (member, settings, clock) = await ResolveMemberAsync(dto.MemberToken, dto.BusinessUnitId, ct);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (today < settings.StartDate)
                throw new InvalidOperationException("This team's engine clock has not started yet.");
            if (!clock.IsAskedOn(today))
                throw new InvalidOperationException("This team is not asked on this weekday.");

            var track = (dto.Track ?? "individual").Trim().ToLowerInvariant();
            if (dto.Team is not null && track == "team")
                throw new InvalidOperationException("The team answer was sent twice: as the track and as 'team'.");

            // Everything is validated before anything is written, and saved in one
            // transaction, like submit-daily-feedback: a check-in is recorded whole or
            // not at all.
            var answer = BuildAnswer(member.Id, dto.BusinessUnitId, clock, today, track, dto.Level, dto.Chips, dto.FreeText);
            var teamAnswer = dto.Team is null
                ? null
                : BuildAnswer(member.Id, dto.BusinessUnitId, clock, today, "team", dto.Team.Level, dto.Team.Chips, dto.Team.FreeText);

            var customFeedback = dto.CustomQuestion is null
                ? null
                : await BuildCustomAnswerAsync(dto.BusinessUnitId, dto.CustomQuestion, ct);

            _context.EngineAnswers.Add(answer);
            if (teamAnswer is not null) _context.EngineAnswers.Add(teamAnswer);
            if (customFeedback is not null) _context.CustomQuestionDailyFeedback.Add(customFeedback);

            await using var transaction = await _context.Database.BeginTransactionAsync(ct);
            try
            {
                await _context.SaveChangesAsync(ct);

                // The audit snapshot needs the generated id, so it takes a second save.
                if (customFeedback is not null)
                {
                    _context.CustomQuestionDailyFeedbackAudits.Add(new CustomQuestionDailyFeedbackAudit
                    {
                        CustomQuestionDailyFeedbackId = customFeedback.Id,
                        CustomQuestionId = customFeedback.CustomQuestionId,
                        UnitId = customFeedback.UnitId,
                        FreeText = customFeedback.FreeText,
                        ChangeSummary = dto.CustomQuestion!.Skipped ? "Answer skipped." : "Answer recorded.",
                        CreatedAt = customFeedback.CreatedAt,
                        UpdatedAt = customFeedback.UpdatedAt,
                        LoggedAt = customFeedback.CreatedAt,
                        VersionNumber = 1,
                        OperationType = "INSERT"
                    });
                    await _context.SaveChangesAsync(ct);
                }

                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);

                // The unique index is the rule, not a race guard: one contribution per
                // member per track per day (spec 4.3).
                _logger.LogWarning(ex, "Duplicate answer refused for unit {Unit} track {Track}.",
                    dto.BusinessUnitId, track);
                throw new InvalidOperationException("You have already answered this question today.");
            }

            var result = ToResult(answer);
            result.Team = teamAnswer is null ? null : ToResult(teamAnswer);
            result.CustomQuestionFeedbackId = customFeedback?.Id;
            return result;
        }

        /// <summary>
        /// One track's answer, validated against today's rotation (spec 4.3). Not added
        /// to the context, so a later failure leaves nothing half written.
        /// </summary>
        private EngineAnswer BuildAnswer(
            long memberId, uint businessUnitId, IEngineTeamClock clock, DateOnly today,
            string track, string? rawLevel, List<string>? rawChips, string? rawFreeText)
        {
            var ctx = _engine.Context;

            if (!ctx.Rotation.TracksAsked.Contains(track, StringComparer.Ordinal))
                throw new InvalidOperationException($"The {track} track is not being asked.");

            var level = (rawLevel ?? string.Empty).Trim().ToLowerInvariant();
            if (!PolarityOfLevel.TryGetValue(level, out var polarity))
                throw new InvalidOperationException($"The {track} level must be red, orange, yellow or green.");

            var askedDayIndex = clock.AskedDayIndexOf(today);
            var questionKey = ctx.Rotation.QuestionIdForDay(askedDayIndex, track)
                ?? throw new InvalidOperationException($"The rotation has no {track} question for today.");

            // Keep the member's order, drop a repeat: two chips sharing an action is one
            // vote from that member, and the same chip twice is not two answers.
            var chips = (rawChips ?? new List<string>())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (chips.Count > MaxChips)
                throw new InvalidOperationException($"At most {MaxChips} chips may be chosen for the {track} question.");

            var freeText = string.IsNullOrWhiteSpace(rawFreeText) ? null : rawFreeText.Trim();

            // A reason is compulsory. An answer never leaves the screen carrying a level
            // and no reason at all.
            if (chips.Count == 0 && freeText is null)
                throw new InvalidOperationException($"A reason is required for the {track} question: choose a chip or write your own.");

            foreach (var chipId in chips)
            {
                var chip = ctx.Data.Chip(chipId)
                    ?? throw new InvalidOperationException($"Chip '{chipId}' is not in the reference data.");

                if (chip.QuestionId != questionKey)
                    throw new InvalidOperationException($"Chip '{chipId}' does not belong to today's {track} question.");
                if (!string.Equals(chip.Track, track, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Chip '{chipId}' is not on the {track} track.");
                if (!string.Equals(chip.Polarity, polarity, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Chip '{chipId}' is not in the {polarity} set for that level.");
            }

            var answer = new EngineAnswer
            {
                BusinessUnitId = businessUnitId,
                MemberId = memberId,
                AnswerDate = today,
                Day = clock.DayOf(today),
                AskedDayIndex = askedDayIndex,
                Track = track,
                QuestionKey = questionKey,
                Level = level,
                FreeText = freeText,
                CreatedAt = DateTime.UtcNow
            };

            for (var i = 0; i < chips.Count; i += 1)
                answer.Chips.Add(new EngineAnswerChip { ChipKey = chips[i], Position = i + 1 });

            return answer;
        }

        /// <summary>
        /// The custom question's answer, checked the way submit-daily-feedback checks it:
        /// the question must belong to this unit and be live today. A skip, or no text,
        /// is still recorded, as a row with no free text.
        /// </summary>
        private async Task<CustomQuestionDailyFeedback> BuildCustomAnswerAsync(
            uint businessUnitId, EngineCustomAnswerDto custom, CancellationToken ct)
        {
            var text = custom.Skipped || string.IsNullOrWhiteSpace(custom.FreeText)
                ? null
                : custom.FreeText.Trim();
            if (text?.Length > MobileApiConstants.CustomFeedbackFreeTextLength)
                throw new InvalidOperationException(
                    $"The custom answer can be at most {MobileApiConstants.CustomFeedbackFreeTextLength} characters.");

            var answerable = await _context.CustomQuestions.AsNoTracking()
                .Where(MobileApiRepository.CustomQuestionLiveOn(businessUnitId, DateTime.UtcNow))
                .AnyAsync(q => q.Id == custom.CustomDailyQuestionId, ct);
            if (!answerable)
                throw new InvalidOperationException(MobileApiMessages.CustomDailyQuestion_NotAvailable);

            var now = DateTime.UtcNow;
            return new CustomQuestionDailyFeedback
            {
                CustomQuestionId = custom.CustomDailyQuestionId,
                UnitId = businessUnitId,
                FreeText = text,
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        private static EngineAnswerResultDto ToResult(EngineAnswer answer) => new()
        {
            AnswerId = answer.Id,
            Day = answer.Day,
            AskedDayIndex = answer.AskedDayIndex,
            QuestionKey = answer.QuestionKey,
            Track = answer.Track,
            Level = answer.Level,
            ChipsRecorded = answer.Chips.Count,
            FreeTextRecorded = answer.FreeText is not null
        };

        // ── helpers ──────────────────────────────────────────────────────────

        private async Task<(EngineMember Member, EngineTeamSettings Settings, IEngineTeamClock Clock)>
            ResolveMemberAsync(string memberToken, uint businessUnitId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(memberToken))
                throw new InvalidOperationException("A member token is required.");

            var hash = HashToken(memberToken);
            var member = await _context.EngineMembers.AsNoTracking()
                .FirstOrDefaultAsync(m => m.TokenHash == hash && !m.IsDel, ct)
                ?? throw new InvalidOperationException("This device is not enrolled.");

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var enrolled = await _context.EngineTeamMemberships.AsNoTracking()
                .AnyAsync(m => m.MemberId == member.Id
                               && m.BusinessUnitId == businessUnitId
                               && m.EffectiveFrom <= today
                               && (m.EffectiveTo == null || m.EffectiveTo >= today), ct);

            if (!enrolled)
                throw new InvalidOperationException("This member is not enrolled on that team.");

            var settings = await _context.EngineTeamSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.BusinessUnitId == businessUnitId, ct)
                ?? throw new InvalidOperationException("This team is not set up for the deterministic layer yet.");

            if (!settings.IsEnabled)
                throw new InvalidOperationException("The deterministic layer is not switched on for this team.");

            var clock = new EngineTeamClock(settings.StartDate, settings.WorkingWeek, _engine.Calendar);
            return (member, settings, clock);
        }

        /// <summary>
        /// SHA-256, hex. The token itself is never stored, so this table cannot be read
        /// back into a device identifier.
        /// </summary>
        private static string HashToken(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()))).ToLowerInvariant();
    }
}
