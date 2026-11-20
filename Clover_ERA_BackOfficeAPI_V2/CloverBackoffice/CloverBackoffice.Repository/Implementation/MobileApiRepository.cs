using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.DTO.Mobile;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.Mobile;
using TQ = CloverBackoffice.Infrastructure.Models.TeamQuestions;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    /// <summary>
    /// Data access for the mobile app endpoints.
    ///
    /// This is a re-implementation of the old Functions API's unit-of-work chain
    /// against this project's DbContext, not a port of it. The behaviour that the app
    /// depends on is preserved exactly; the plumbing around it is this project's.
    ///
    /// The one piece worth reading closely is <see cref="ResolveOrdinal" />, which is
    /// the whole of "how a question is chosen".
    /// </summary>
    public class MobileApiRepository : IMobileApiRepository
    {
        private readonly ApplicationDbContext _context;

        public MobileApiRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        // ── Daily questions ─────────────────────────────────────────────────

        public async Task<MobileQuestionResult> GetDailyQuestionAsync(uint businessUnitId)
        {
            var unit = await LoadUnitWithCompanyAsync(businessUnitId);
            if (unit == null)
            {
                return new MobileQuestionResult { BusinessUnitFound = false };
            }

            var cultureId = await GetDefaultContentCultureIdAsync();

            var rangeSize = await GetRotationRangeSizeAsync(unit.ActiveDailyQuestionSetGroupId, cultureId);
            if (rangeSize <= 0)
            {
                return new MobileQuestionResult { BusinessUnitFound = true, QuestionAvailable = false };
            }

            var today = DateTime.UtcNow.Date;
            var ordinal = ResolveOrdinal(today, rangeSize);

            var specification = await LoadSpecificationsAsync(
                unit.ActiveDailyQuestionSetGroupId,
                cultureId,
                new[] { ordinal });

            var match = specification.FirstOrDefault();
            if (match == null)
            {
                return new MobileQuestionResult { BusinessUnitFound = true, QuestionAvailable = false };
            }

            // The team question rides along with the daily one. It is resolved after the
            // daily question and attached to it, so a team bank that has nothing to serve
            // costs the daily answer nothing — the response is the same as before, with
            // teamQuestion null.
            var team = await BuildTeamQuestionsAsync(unit, cultureId, new[] { today });

            // And the company's own one-off question, on the same terms: resolved last,
            // attached if it is there, and never able to cost the caller the daily
            // question it asked for.
            var custom = await BuildCustomQuestionAsync(unit.Id, today);

            return new MobileQuestionResult
            {
                BusinessUnitFound = true,
                QuestionAvailable = true,
                Question = MapQuestion(
                    unit,
                    match,
                    team.TryGetValue(today, out var teamQuestion) ? teamQuestion : null,
                    custom)
            };
        }

        public async Task<MobileQuestionListResult> GetNextDailyQuestionsAsync(uint businessUnitId)
        {
            var unit = await LoadUnitWithCompanyAsync(businessUnitId);
            if (unit == null)
            {
                return new MobileQuestionListResult { BusinessUnitFound = false };
            }

            var cultureId = await GetDefaultContentCultureIdAsync();

            var rangeSize = await GetRotationRangeSizeAsync(unit.ActiveDailyQuestionSetGroupId, cultureId);
            if (rangeSize <= 0)
            {
                return new MobileQuestionListResult { BusinessUnitFound = true, QuestionAvailable = false };
            }

            // Starts at tomorrow, not today: the screen this feeds is "what is coming
            // up", shown alongside today's question from the other endpoint.
            var firstDay = DateTime.UtcNow.Date.AddDays(1);

            // Built as an ordered list rather than a set because the rotation wraps:
            // with a short bank the same ordinal legitimately appears twice in five
            // days, and the app expects five entries in day order.
            var days = Enumerable.Range(0, 5).Select(offset => firstDay.AddDays(offset)).ToList();

            var ordinals = days
                .Select(day => ResolveOrdinal(day, rangeSize))
                .ToList();

            var specifications = await LoadSpecificationsAsync(
                unit.ActiveDailyQuestionSetGroupId,
                cultureId,
                ordinals.Distinct().ToArray());

            var byOrdinal = specifications.ToDictionary(x => (int)x.Ordinal);

            // One team question per day, on the same days the daily rotation is being
            // asked for, so each entry in the array carries the pair the app will show
            // together on that date.
            var teamByDay = await BuildTeamQuestionsAsync(unit, cultureId, days);

            // No custom question here, and that is deliberate. The daily and team banks
            // are fixed rotations — what they will serve on a future day is already
            // decided, so pre-loading them is safe. A custom question is authored content
            // that can be rescheduled or cancelled right up to its day, so pre-loading it
            // would let the app show a question that has since been pulled. It is served
            // by the single-day endpoint only, which is also where the backend owns the
            // day (see the read notes on CustomQuestionDtos).

            var questions = days
                .Select((day, index) => (Day: day, Ordinal: ordinals[index]))
                .Where(x => byOrdinal.ContainsKey(x.Ordinal))
                .Select(x => MapQuestion(
                    unit,
                    byOrdinal[x.Ordinal],
                    teamByDay.TryGetValue(x.Day, out var teamQuestion) ? teamQuestion : null))
                .ToList();

            return new MobileQuestionListResult
            {
                BusinessUnitFound = true,
                QuestionAvailable = true,
                Questions = questions
            };
        }

        /// <summary>
        /// Which question the whole platform sees on a given day.
        ///
        /// The rotation is a pure function of the date: it walks the bank from
        /// <see cref="MobileApiConstants.StartOrdinal" /> upwards, one step per day
        /// since <see cref="MobileApiConstants.RotationStartDateUtc" />, and wraps at
        /// the end. It takes no account of the member, the team, or what has already
        /// been answered — every team on the same question set group is shown the
        /// same question on the same day, by design.
        /// </summary>
        private static int ResolveOrdinal(DateTime utcDate, int rangeSize)
        {
            var dayIndex = (utcDate.Date - MobileApiConstants.RotationStartDateUtc.Date).Days;

            // Modulo of a negative dayIndex is negative in C#, which would index below
            // the start of the bank. Dates before the rotation epoch are not expected,
            // but a clock skew or a back-dated request must not produce a wild ordinal.
            var offset = ((dayIndex % rangeSize) + rangeSize) % rangeSize;

            return MobileApiConstants.StartOrdinal + offset;
        }

        /// <summary>
        /// How many ordinals the rotation has to play with. Zero or less means the
        /// bank has nothing at or above the start ordinal, and no question can be served.
        /// </summary>
        private async Task<int> GetRotationRangeSizeAsync(uint setGroupId, uint cultureId)
        {
            // Max over an empty set would throw on a non-nullable projection, so it is
            // lifted to int? and the empty case handled as "nothing to serve".
            var maxOrdinal = await _context.DailyQuestionSpecifications
                .Where(x => x.DailyQuestionSetGroupId == setGroupId
                            && x.ContentCultureId == cultureId
                            && x.Ordinal >= MobileApiConstants.StartOrdinal)
                .MaxAsync(x => (int?)x.Ordinal);

            if (maxOrdinal == null)
            {
                return 0;
            }

            return maxOrdinal.Value - MobileApiConstants.StartOrdinal + 1;
        }

        private async Task<List<DailyQuestionSpecification>> LoadSpecificationsAsync(
            uint setGroupId,
            uint cultureId,
            int[] ordinals)
        {
            return await _context.DailyQuestionSpecifications
                .AsNoTracking()
                .Include(x => x.Question)
                .Include(x => x.Reasons)
                .Where(x => x.DailyQuestionSetGroupId == setGroupId
                            && x.ContentCultureId == cultureId
                            && ordinals.Contains((int)x.Ordinal))
                .ToListAsync();
        }

        /// <summary>
        /// Shapes one specification into the app's response.
        ///
        /// Reason order is not incidental: reasons needing free text are moved to the
        /// end so the app can render them as the last option in the list. Preserved
        /// exactly, because the app relies on the position.
        /// </summary>
        private static DailyQuestionFetchResponse MapQuestion(
            BusinessUnit unit,
            DailyQuestionSpecification spec,
            TeamQuestionFetchResponse? teamQuestion,
            CustomQuestionFetchResponse? customQuestion = null)
        {
            var reasons = spec.Reasons ?? new List<DailyReason>();

            return new DailyQuestionFetchResponse
            {
                ProfileName = unit.Profile?.ProfileName ?? string.Empty,
                BusinessUnitName = unit.Name,
                QuestionId = spec.Question.Id,
                QuestionText = spec.Question.Text,
                CloverCategory = spec.CloverCategory,
                TeamQuestion = teamQuestion,
                CustomQuestion = customQuestion,
                Reasons = reasons
                    .Where(x => !x.RequiresFreeTextFeedback)
                    .Concat(reasons.Where(x => x.RequiresFreeTextFeedback))
                    .Select(x => new DailyReasonResponse
                    {
                        ReasonId = x.Id,
                        ReasonText = x.Text,
                        RequiresFreeTextFeedback = x.RequiresFreeTextFeedback,
                        Sentiment = x.DailyReasonSentimentType
                    })
                    .ToArray()
            };
        }

        // ── Team questions ──────────────────────────────────────────────────
        //
        // A second rotation, independent of the legacy one and served beside it. The
        // differences from the daily bank, all of them forced by the schema:
        //
        //   * There is no set group. Team specifications are not organised into
        //     versioned sets, so the rotation is scoped by category and culture.
        //   * The bank is numbered from 1, not 121.
        //   * An ordinal can hold two specifications — a generic wording and a
        //     goal-anchored restatement of the same condition. Which one is served
        //     depends on whether the team has a goal to anchor it to.

        /// <summary>
        /// The team question for each of the given days, keyed by day.
        ///
        /// Returns an empty map rather than failing when the team bank is empty or the
        /// unit has no setup: the team question is additional to the daily one, and its
        /// absence must never cost the caller the daily question it asked for.
        /// </summary>
        private async Task<Dictionary<DateTime, TeamQuestionFetchResponse>> BuildTeamQuestionsAsync(
            BusinessUnit unit,
            uint cultureId,
            IReadOnlyList<DateTime> days)
        {
            var result = new Dictionary<DateTime, TeamQuestionFetchResponse>();

            var rangeSize = await GetTeamRotationRangeSizeAsync(cultureId);
            if (rangeSize <= 0)
            {
                return result;
            }

            var goals = await LoadTeamGoalsAsync(unit.Id);

            var ordinals = days
                .Select(day => ResolveTeamOrdinal(day, rangeSize))
                .ToList();

            var specifications = await LoadTeamSpecificationsAsync(cultureId, ordinals.Distinct().ToArray());

            // Grouped, not keyed: an ordinal can legitimately hold both variants.
            var byOrdinal = specifications
                .GroupBy(x => (int)x.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList());

            for (var index = 0; index < days.Count; index++)
            {
                var day = days[index];

                if (!byOrdinal.TryGetValue(ordinals[index], out var candidates))
                {
                    continue;
                }

                // Goal-anchored wording only when there is a goal to put in front of it.
                // The question reads "This is one of your team's goals…", so serving it to
                // a team with no goals would be a question about nothing.
                var anchoredGoal = goals.Count == 0 ? null : PickGoalForDay(goals, day);

                var spec = anchoredGoal == null
                    ? candidates.FirstOrDefault(x => x.Variant == TQ.TeamQuestionVariant.Generic)
                    : candidates.FirstOrDefault(x => x.Variant == TQ.TeamQuestionVariant.GoalAnchored)
                      ?? candidates.FirstOrDefault(x => x.Variant == TQ.TeamQuestionVariant.Generic);

                // An ordinal holding only a goal-anchored specification, for a team with no
                // goals, has nothing to serve that day. Skipped rather than substituted:
                // the rotation is a fixed calendar and pulling a different condition
                // forward would put the team out of step with every other team.
                if (spec == null)
                {
                    continue;
                }

                result[day] = MapTeamQuestion(
                    spec,
                    spec.Variant == TQ.TeamQuestionVariant.GoalAnchored ? anchoredGoal : null,
                    goals);
            }

            return result;
        }

        /// <summary>
        /// The team rotation, on the same terms as the daily one: a pure function of the
        /// date, taking no account of the member or what has been answered. It shares the
        /// daily rotation's epoch so both banks turn over on the same day boundary.
        /// </summary>
        private static int ResolveTeamOrdinal(DateTime utcDate, int rangeSize)
        {
            var dayIndex = (utcDate.Date - MobileApiConstants.RotationStartDateUtc.Date).Days;

            var offset = ((dayIndex % rangeSize) + rangeSize) % rangeSize;

            return MobileApiConstants.TeamStartOrdinal + offset;
        }

        /// <summary>
        /// Which goal a goal-anchored question is put against on a given day. Rotates
        /// through the team's goals by date so a team with three goals is asked about
        /// each of them in turn, and every member of that team sees the same one.
        /// </summary>
        private static TeamGoalResponse PickGoalForDay(List<TeamGoalResponse> goals, DateTime utcDate)
        {
            var dayIndex = (utcDate.Date - MobileApiConstants.RotationStartDateUtc.Date).Days;

            var offset = ((dayIndex % goals.Count) + goals.Count) % goals.Count;

            return goals[offset];
        }

        private async Task<int> GetTeamRotationRangeSizeAsync(uint cultureId)
        {
            var maxOrdinal = await _context.TeamDailyQuestionSpecifications
                .Where(x => x.CategoryId == MobileApiConstants.DefaultTeamQuestionCategoryId
                            && x.ContentCultureId == cultureId
                            && x.Ordinal >= MobileApiConstants.TeamStartOrdinal)
                .MaxAsync(x => (int?)x.Ordinal);

            if (maxOrdinal == null)
            {
                return 0;
            }

            return maxOrdinal.Value - MobileApiConstants.TeamStartOrdinal + 1;
        }

        private async Task<List<TQ.TeamDailyQuestionSpecification>> LoadTeamSpecificationsAsync(
            uint cultureId,
            int[] ordinals)
        {
            return await _context.TeamDailyQuestionSpecifications
                .AsNoTracking()
                .Include(x => x.Question)
                .Include(x => x.Reasons)
                .Where(x => x.CategoryId == MobileApiConstants.DefaultTeamQuestionCategoryId
                            && x.ContentCultureId == cultureId
                            && ordinals.Contains((int)x.Ordinal))
                .ToListAsync();
        }

        /// <summary>
        /// The team's live, active goals in display order. Empty for a team that has
        /// never been through setup, which is most of them today.
        /// </summary>
        private async Task<List<TeamGoalResponse>> LoadTeamGoalsAsync(uint unitId)
        {
            return await _context.TeamGoals
                .AsNoTracking()
                .Where(x => x.UnitId == unitId && x.IsActive && !x.IsDel)
                .OrderBy(x => x.DisplayOrder)
                .Select(x => new TeamGoalResponse
                {
                    TeamGoalId = x.Id,
                    GoalText = x.GoalText,
                    DisplayOrder = x.DisplayOrder,
                    TargetDate = x.TargetDate
                })
                .ToListAsync();
        }

        /// <summary>
        /// Shapes one team specification into the app's response.
        ///
        /// Reason ordering repeats the daily rule exactly — those needing free text are
        /// moved to the end — so the two screens put their "something else" option in
        /// the same place.
        /// </summary>
        private static TeamQuestionFetchResponse MapTeamQuestion(
            TQ.TeamDailyQuestionSpecification spec,
            TeamGoalResponse? anchoredGoal,
            List<TeamGoalResponse> goals)
        {
            var reasons = spec.Reasons ?? new List<TQ.TeamDailyReason>();

            return new TeamQuestionFetchResponse
            {
                TeamQuestionId = spec.Question?.Id ?? spec.TeamDailyQuestionId,
                QuestionText = spec.Question?.Text ?? string.Empty,

                // Value and wording together — see TeamQuestionDisplay for why the
                // backend owns the wording rather than leaving the app to map numbers.
                TeamElement = spec.TeamElement,
                TeamElementName = spec.TeamElement.ToString(),
                TeamElementLabel = TQ.TeamQuestionDisplay.Label(spec.TeamElement),

                SubCategory = spec.SubCategory,
                SubCategoryName = spec.SubCategory.ToString(),
                SubCategoryLabel = TQ.TeamQuestionDisplay.Label(spec.SubCategory),
                SubCategoryShortLabel = TQ.TeamQuestionDisplay.ShortLabel(spec.SubCategory),

                Variant = spec.Variant,
                VariantName = spec.Variant.ToString(),

                AnchoredGoal = anchoredGoal,
                TeamGoals = goals.ToArray(),
                Reasons = reasons
                    .Where(x => !x.RequiresFreeTextFeedback)
                    .Concat(reasons.Where(x => x.RequiresFreeTextFeedback))
                    .Select(x => new TeamReasonResponse
                    {
                        ReasonId = x.Id,
                        ReasonText = x.Text,
                        RequiresFreeTextFeedback = x.RequiresFreeTextFeedback,
                        Sentiment = x.TeamDailyReasonSentimentType,
                        TeamState = x.TeamState,
                        TeamStateLabel = x.TeamState.HasValue
                            ? TQ.TeamQuestionDisplay.Label(x.TeamState.Value)
                            : null
                    })
                    .ToArray()
            };
        }

        // ── Custom questions ────────────────────────────────────────────────
        //
        // Not a rotation at all. A custom question is a one-off a company authors for
        // one of its own units and stamps with a single ScheduledAt, so there is no
        // ordinal to resolve and no bank to walk — it is simply looked up by unit and
        // by day. It has no reasons and no sentiment scale, so the answer to it is
        // free text and nothing else.

        /// <summary>
        /// The custom question a unit should be answering on the given day, or null when
        /// it has none.
        ///
        /// Both the fetch and the submit go through <see cref="CustomQuestionLiveOn" />,
        /// so a question can never be answerable on a day it would not have been served
        /// on, or the other way about.
        /// </summary>
        private async Task<CustomQuestionFetchResponse?> BuildCustomQuestionAsync(uint unitId, DateTime day)
        {
            // Ordered the same way the back-office read endpoint orders its list. A unit
            // is expected to have at most one question a day, but nothing in the schema
            // enforces that, so the tie is broken deterministically rather than left to
            // the provider — the app shows one question and must be shown the same one
            // on a re-fetch.
            return await _context.CustomQuestions
                .AsNoTracking()
                .Where(CustomQuestionLiveOn(unitId, day))
                .OrderBy(q => q.ScheduledAt)
                .ThenBy(q => q.Id)
                .Select(q => new CustomQuestionFetchResponse
                {
                    CustomDailyQuestionId = q.Id,
                    QuestionText = q.Text
                })
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// What makes a custom question live for a unit on a given day, in one place.
        ///
        /// The rule is the one the back-office read endpoint already applies: a question
        /// carries a single ScheduledAt and no end stamp, so its validity period is the
        /// UTC calendar day that stamp falls in. Selecting on that window is the whole of
        /// "due today and not past its validity period" — Status is filtered too but is
        /// not what enforces it, because the expiry sweep runs on a schedule and a row
        /// can still read Scheduled after its day has ended.
        ///
        /// ScheduledAt is timestamptz, so the bounds have to be UTC or Npgsql refuses the
        /// comparison.
        ///
        /// Public so the engine's today endpoint serves the same question.
        /// </summary>
        public static Expression<Func<TQ.CustomQuestion, bool>> CustomQuestionLiveOn(uint unitId, DateTime day)
        {
            var dayStart = DateTime.SpecifyKind(day.Date, DateTimeKind.Utc);
            var dayEnd = dayStart.AddDays(1);

            return q => !q.IsDel
                        && q.UnitId == unitId
                        && q.ScheduledAt != null
                        && q.ScheduledAt >= dayStart
                        && q.ScheduledAt < dayEnd
                        // Draft was never published and Cancelled was pulled; Expired has
                        // already been swept.
                        && (q.Status == TQ.CustomQuestionStatus.Scheduled
                            || q.Status == TQ.CustomQuestionStatus.Active);
        }

        // ── Daily feedback ──────────────────────────────────────────────────

        public async Task<MobileFeedbackResult> SubmitDailyFeedbackAsync(DailyFeedbackSubmissionRequest request)
        {
            var submittedAt = DateTime.UtcNow;

            // Guarded on the question id rather than written unconditionally, so a
            // team-only submission does not try to insert a DailyFeedback row pointing at
            // question 0. A request from the shipped app always carries one, so its
            // behaviour is unchanged.
            // Multi-select: every selected reason, first selection first. The legacy
            // single-reason columns keep the first one.
            var dailyReasonIds = request.SelectedDailyReasonIds();
            var teamReasonIds = request.SelectedTeamDailyReasonIds();

            var hasDaily = request.DailyQuestionId > 0;
            var hasTeam = request.TeamDailyQuestionId is > 0 && teamReasonIds.Count > 0;
            var hasCustom = request.CustomDailyQuestionId is > 0;

            // An unknown reason id is refused before anything is written, instead of
            // failing on the foreign key.
            if (hasDaily && dailyReasonIds.Count > 0)
            {
                var found = await _context.DailyReasons
                    .AsNoTracking()
                    .CountAsync(r => dailyReasonIds.Contains(r.Id));

                if (found != dailyReasonIds.Count)
                {
                    return new MobileFeedbackResult
                    {
                        ReasonError = MobileApiMessages.DailyReason_NotFound,
                        ReasonErrorField = nameof(request.DailyReasonIds)
                    };
                }
            }

            if (hasTeam)
            {
                var found = await _context.TeamDailyReasons
                    .AsNoTracking()
                    .CountAsync(r => teamReasonIds.Contains(r.Id));

                if (found != teamReasonIds.Count)
                {
                    return new MobileFeedbackResult
                    {
                        ReasonError = MobileApiMessages.TeamDailyReason_NotFound,
                        ReasonErrorField = nameof(request.TeamDailyReasonIds)
                    };
                }
            }

            // Checked before anything is written, and the whole submission is refused if
            // it fails. The alternative — save the daily and team halves and drop the
            // custom one — would tell the member their check-in went through while a
            // third of it silently did not.
            if (hasCustom)
            {
                var customQuestionId = request.CustomDailyQuestionId!.Value;

                // Ownership and liveness in one test: the predicate is scoped to the
                // submitting unit, so a question belonging to another team fails it just
                // as an expired one does. That is also what keeps the stored UnitId and
                // the question's own UnitId from ever disagreeing.
                var answerable = await _context.CustomQuestions
                    .AsNoTracking()
                    .Where(CustomQuestionLiveOn(request.BusinessUnitId, submittedAt))
                    .AnyAsync(q => q.Id == customQuestionId);

                if (!answerable)
                {
                    return new MobileFeedbackResult
                    {
                        CustomQuestionError = MobileApiMessages.CustomDailyQuestion_NotAvailable
                    };
                }
            }

            if (hasDaily)
            {
                var dailyFeedback = new DailyFeedback
                {
                    SubmissionDateTime = submittedAt,
                    BusinessUnitId = request.BusinessUnitId,
                    SentimentType = request.DailyFeedbackSentimentType,
                    DailyQuestionId = request.DailyQuestionId,

                    // Null when the member gave only their own reason text.
                    DailyReasonId = dailyReasonIds.Count > 0 ? dailyReasonIds[0] : null,
                    FreeText = request.FeedbackFreeText
                };

                _context.DailyFeedbacks.Add(dailyFeedback);

                // The full selection, saved in the same SaveChanges; EF fills the id.
                foreach (var reasonId in dailyReasonIds)
                {
                    _context.DailyFeedbackReasons.Add(new DailyFeedbackReason
                    {
                        DailyFeedback = dailyFeedback,
                        DailyReasonId = reasonId
                    });
                }
            }

            if (hasTeam)
            {
                var teamFeedback = new TQ.TeamDailyFeedback
                {
                    SubmissionDateTime = submittedAt,
                    BusinessUnitId = request.BusinessUnitId,
                    SentimentType = request.TeamFeedbackSentimentType
                                    ?? TQ.TeamDailyFeedbackSentimentType.Indifferent,
                    TeamDailyQuestionId = request.TeamDailyQuestionId!.Value,
                    TeamDailyReasonId = teamReasonIds[0],
                    FreeText = request.TeamFeedbackFreeText
                };

                _context.TeamDailyFeedback.Add(teamFeedback);

                foreach (var reasonId in teamReasonIds)
                {
                    _context.TeamDailyFeedbackReasons.Add(new TeamDailyFeedbackReason
                    {
                        TeamDailyFeedback = teamFeedback,
                        TeamDailyReasonId = reasonId
                    });
                }
            }

            TQ.CustomQuestionDailyFeedback? customFeedback = null;

            if (hasCustom)
            {
                // A skip is recorded, not discarded: the row is what says the member was
                // shown the question and chose not to answer, which is the difference
                // between "nobody answered" and "nobody was asked". Since an empty answer
                // is stored the same way, a row with no free text always means a skip.
                var customFreeText = request.CustomFeedbackSkipped
                                     || string.IsNullOrWhiteSpace(request.CustomFeedbackFreeText)
                    ? null
                    : request.CustomFeedbackFreeText.Trim();

                customFeedback = new TQ.CustomQuestionDailyFeedback
                {
                    CustomQuestionId = request.CustomDailyQuestionId!.Value,
                    UnitId = request.BusinessUnitId,
                    FreeText = customFreeText,
                    CreatedAt = submittedAt,
                    UpdatedAt = submittedAt
                };

                _context.CustomQuestionDailyFeedback.Add(customFeedback);
            }

            if (customFeedback == null)
            {
                // One SaveChanges for both rows, so a member answering both questions
                // either has both answers recorded or neither — never a daily answer with
                // the team half silently missing.
                await _context.SaveChangesAsync();
            }
            else
            {
                // The custom answer has to be snapshotted into its audit table like every
                // other write to it (see CustomQuestionEntities), and the snapshot needs
                // the generated id, so it takes a second SaveChanges. The transaction puts
                // all three answers and the audit row under one commit, keeping the
                // all-or-nothing property the single save gave the other two.
                await using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    await _context.SaveChangesAsync();

                    _context.CustomQuestionDailyFeedbackAudits.Add(new TQ.CustomQuestionDailyFeedbackAudit
                    {
                        CustomQuestionDailyFeedbackId = customFeedback.Id,
                        CustomQuestionId = customFeedback.CustomQuestionId,
                        UnitId = customFeedback.UnitId,
                        FreeText = customFeedback.FreeText,
                        ChangeSummary = request.CustomFeedbackSkipped
                            ? "Answer skipped."
                            : "Answer recorded.",
                        CreatedBy = customFeedback.CreatedBy,
                        UpdatedBy = customFeedback.UpdatedBy,
                        CreatedAt = customFeedback.CreatedAt,
                        UpdatedAt = customFeedback.UpdatedAt,
                        LoggedAt = submittedAt,
                        VersionNumber = 1,
                        OperationType = "INSERT"
                    });

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }

            // The nudge is a courtesy, not part of the submission: the answers are
            // already saved and committed above. A reason with no micro-action behind
            // it returns an empty string, exactly as before — never an error, because
            // failing here would tell the member their answer was lost when it was not.
            // Based on the first selected reason, as the single reason was before.
            var firstDailyReasonId = dailyReasonIds.Count > 0 ? dailyReasonIds[0] : 0;

            var microAction = hasDaily && firstDailyReasonId > 0
                ? await _context.DailyMicroActions
                    .AsNoTracking()
                    .Where(x => x.ReasonId == firstDailyReasonId)
                    .OrderBy(x => x.Id)
                    .Select(x => x.Text)
                    .FirstOrDefaultAsync()
                : null;

            string? teamMicroAction = null;

            if (hasTeam)
            {
                var teamReasonId = teamReasonIds[0];

                teamMicroAction = await _context.TeamDailyMicroActions
                    .AsNoTracking()
                    .Where(x => x.TeamDailyReasonId == teamReasonId)
                    .OrderBy(x => x.Id)
                    .Select(x => x.Text)
                    .FirstOrDefaultAsync() ?? string.Empty;
            }

            return new MobileFeedbackResult
            {
                // Unchanged for a daily submission: empty string when the reason has no
                // micro-action, which is what the app has always been given.
                SuggestedMicroAction = hasDaily ? microAction ?? string.Empty : string.Empty,

                // Null, not empty, when no team answer was submitted — the app can tell
                // "no team answer" from "team answer with no nudge".
                TeamSuggestedMicroAction = teamMicroAction
            };
        }

        // ── Member enrolment ────────────────────────────────────────────────

        public async Task<MemberEnrolmentResponse?> EnrollMemberAsync(string businessUnitShortCode)
        {
            var shortCode = businessUnitShortCode.Trim();

            var unit = await _context.BusinessUnits
                .AsNoTracking()
                .Where(x => x.BusinessUnitShortCode == shortCode)
                .Select(x => new MemberEnrolmentResponse
                {
                    BusinessUnitId = x.Id,
                    BusinessUnitRegistrationDate = x.RegistrationDate
                })
                .FirstOrDefaultAsync();

            return unit;
        }

        // ── Productivity ────────────────────────────────────────────────────

        public async Task SubmitProductivityAsync(ProductivitySubmissionRequest request)
        {
            var reflection = new DailyReflection
            {
                BusinessUnitId = request.BusinessUnitId,

                // The column is integer, so the app's uint is narrowed here rather
                // than at the contract, which must keep accepting what it always has.
                PersonalBusinessUnitId = (int)request.PersonalBusinessUnitId,

                ReflectionLocalDate = request.ReflectionLocalDate,
                AchievementsLines = request.AchievementsLines,
                NotDoneLines = request.NotDoneLines,
                BlockersLines = request.BlockersLines,
                TomorrowReminderText = request.TomorrowReminderText,
                TomorrowReminderLocalTime = request.TomorrowReminderLocalTime,
                ClientSubmittedAtUtc = ToUtc(request.ClientSubmittedAtUtc),
                TimeAllocations = request.TimeAllocations?
                    .Select(x => new TimeAllocation
                    {
                        OptionId = x.OptionId,
                        Hours = x.Hours
                    })
                    .ToList()
            };

            _context.DailyReflections.Add(reflection);

            // One SaveChanges for the reflection and its allocations together: EF
            // orders the inserts and fills in the child foreign keys, so a failure
            // cannot leave allocations pointing at a reflection that was not written.
            await _context.SaveChangesAsync();
        }

        // ── Suggestions ─────────────────────────────────────────────────────

        public async Task SubmitSuggestionAsync(FeedbackSubmissionRequest request)
        {
            _context.FeedBack.Add(new FeedBack
            {
                BusinessUnitId = request.BusinessUnitId,
                // Non-null past the endpoint validator; the column is NOT NULL.
                FeedbackText = request.FeedbackText ?? string.Empty,

                // This column alone is timestamp WITHOUT time zone. Npgsql refuses a
                // UTC-kind value for it, so the kind is stripped after converting.
                UserSubmittedAtUtc = DateTime.SpecifyKind(
                    ToUtc(request.ClientSubmittedAtUtc) ?? DateTime.UtcNow,
                    DateTimeKind.Unspecified)
            });

            await _context.SaveChangesAsync();
        }

        // ── Team feedback ───────────────────────────────────────────────────

        public async Task<bool> SubmitTeamFeedbackAsync(TeamFeedbackSubmissionRequest request)
        {
            var unitExists = await _context.BusinessUnits
                .AsNoTracking()
                .AnyAsync(x => x.Id == request.BusinessUnitId);

            if (!unitExists)
            {
                return false;
            }

            var submittedAt = DateTime.UtcNow;

            // Anonymous by design: no member identity is stored, only the team.
            _context.TeamFeedback.Add(new TeamFeedback
            {
                BusinessUnitId = request.BusinessUnitId,
                FeedbackText = request.FeedbackText!.Trim(),
                ClientSubmittedAtUtc = ToUtc(request.ClientSubmittedAtUtc!.Value) ?? submittedAt,
                SubmissionDateTime = submittedAt
            });

            await _context.SaveChangesAsync();

            return true;
        }

        // ── Company registration ────────────────────────────────────────────

        public async Task<MobileRegistrationResult> RegisterProfileAsync(
            ProfileRegistrationRequest request,
            ushort trialPeriodDays)
        {
            var businessUnitCodes = GenerateCodes();

            var domainName = Text(request.RegistrantEmailAddress).ToLowerInvariant().Split('@').Last();
            var companyCodes = GenerateCodes();

            // Companies.NameInvariant backed the duplicate-name check and is gone. A
            // case-insensitive compare on ProfileName replaces it, which also closes a
            // hole in the old check: this endpoint wrote NameInvariant upper-cased but
            // the back-office paths wrote it verbatim, so a company registered through
            // the back office never matched here.
            var profileNameNormalized = Text(request.ProfileName).ToLowerInvariant();

            var existing = await _context.Profiles
                .Where(c => c.ProfileShortCode == companyCodes.ShortCode
                            || c.DomainName == domainName
                            || c.ProfileName.ToLower() == profileNameNormalized)
                .ToListAsync();

            // Nothing matches: brand new company, contact, team and trial.
            if (existing.Count == 0)
            {
                var unit = await CreateCompanyAsync(request, companyCodes, businessUnitCodes, domainName, trialPeriodDays);
                return Success(unit);
            }

            // The email domain is already registered, so this is a colleague joining.
            // They get a team under the existing company rather than a second company.
            var sameDomain = existing.FirstOrDefault(c => c.DomainName == domainName);
            if (sameDomain != null)
            {
                var unit = await AttachBusinessUnitAsync(sameDomain, request, businessUnitCodes, trialPeriodDays);
                return Success(unit);
            }

            // The company name is taken, but by a different email domain. Refused:
            // letting it through would silently split one company across two records.
            if (existing.Any(c => c.DomainName != domainName
                                  && c.ProfileName.ToLowerInvariant() == profileNameNormalized))
            {
                return new MobileRegistrationResult { IsSuccess = false, Conflict = true };
            }

            // Only a generated-code clash is left. Regenerating is enough — the codes
            // are fresh GUIDs, so a second attempt collides with probability ~0.
            var retryUnit = await CreateCompanyAsync(
                request, GenerateCodes(), GenerateCodes(), domainName, trialPeriodDays);

            return Success(retryUnit);
        }

        private static MobileRegistrationResult Success(BusinessUnit unit) => new()
        {
            IsSuccess = true,
            Registration = new ProfileRegistrationResponse
            {
                BusinessUnitId = unit.Id,
                BusinessUnitShortCode = unit.BusinessUnitShortCode,
                BusinessUnitRegistrationDate = unit.RegistrationDate
            }
        };

        private async Task<BusinessUnit> CreateCompanyAsync(
            ProfileRegistrationRequest request,
            (string Code, string ShortCode) companyCodes,
            (string Code, string ShortCode) businessUnitCodes,
            string domainName,
            ushort trialPeriodDays)
        {
            var contact = new ContactInfoDto
            {
                EmailAddress = Text(request.RegistrantEmailAddress).ToLowerInvariant(),
                FirstName = Text(request.RegistrantFirstName),
                LastName = Text(request.RegistrantLastName)
            };

            var profileName = Text(request.ProfileName);

            // CompanyCode and CountryCode were dropped from the table, and
            // CompanyCountryCode has been dropped from the request with them. The
            // profile name still arrives as ProfileName because that is what the
            // shipped app posts; it is stored in ProfileName.
            var company = new Profile
            {
                ProfileShortCode = companyCodes.ShortCode,
                DomainName = domainName,
                ProfileName = profileName,
                RegistrationDate = DateTime.UtcNow,
                ContactEmailAddress = contact.EmailAddress,
                ContactFirstName = contact.FirstName,
                ContactLastName = contact.LastName
            };

            _context.Profiles.Add(company);

            return await CreateBusinessUnitAsync(
                company,
                contact,
                Text(request.BusinessUnitName),
                Text(request.BusinessUnitName).ToUpperInvariant(),
                businessUnitCodes,
                trialPeriodDays);
        }

        private async Task<BusinessUnit> AttachBusinessUnitAsync(
            Profile company,
            ProfileRegistrationRequest request,
            (string Code, string ShortCode) businessUnitCodes,
            ushort trialPeriodDays)
        {
            var unitName = Text(request.BusinessUnitName);
            var unitNameInvariant = unitName.ToUpperInvariant();
            var registrantEmail = Text(request.RegistrantEmailAddress);

            // A team of this name already exists here: return it rather than creating
            // a duplicate. This makes the endpoint idempotent for a repeated sign-up,
            // which is what the app relies on when a member retries.
            // Navigated through Company rather than a ProfileId property: BusinessUnit
            // maps that foreign key as a shadow property, so it has no CLR member here.
            var existingUnit = await _context.BusinessUnits
                .FirstOrDefaultAsync(x => x.Profile.Id == company.Id && x.NameInvariant == unitNameInvariant);

            if (existingUnit != null)
            {
                return existingUnit;
            }

            // The registrant used to be looked up in Contacts so one row could be
            // shared by every team they registered. There is no shared row any more,
            // so the request values are written straight onto the new unit.
            var contact = new ContactInfoDto
            {
                EmailAddress = registrantEmail.ToLowerInvariant(),
                FirstName = Text(request.RegistrantFirstName),
                LastName = Text(request.RegistrantLastName)
            };

            return await CreateBusinessUnitAsync(
                company,
                contact,
                unitName,
                unitNameInvariant,
                businessUnitCodes,
                trialPeriodDays);
        }

        /// <summary>
        /// Creates the team and its trial subscription, and saves. Shared by all three
        /// registration paths so they cannot drift apart on defaults.
        /// </summary>
        private async Task<BusinessUnit> CreateBusinessUnitAsync(
            Profile company,
            ContactInfoDto contact,
            string name,
            string nameInvariant,
            (string Code, string ShortCode) codes,
            ushort trialPeriodDays)
        {
            var setGroup = await _context.DailyQuestionSetGroups
                .FirstAsync(g => g.Id == MobileApiConstants.DefaultDailyQuestionSetGroupId);

            var unit = new BusinessUnit
            {
                BusinessUnitCode = codes.Code,
                BusinessUnitShortCode = codes.ShortCode,
                Name = name,
                NameInvariant = nameInvariant,
                Profile = company,
                ContactEmailAddress = contact.EmailAddress,
                ContactFirstName = contact.FirstName,
                ContactLastName = contact.LastName,

                // Date component only. The app formats this as a plain date, and a
                // time component would show up as a spurious "registered at 14:32".
                RegistrationDate = DateTime.UtcNow.Date,
                ActiveDailyQuestionSetGroupId = MobileApiConstants.DefaultDailyQuestionSetGroupId,
                ActiveDailyQuestionSetGroup = setGroup
            };

            _context.BusinessUnits.Add(unit);

            _context.Subscriptions.Add(new Subscription
            {
                Profile = company,
                BusinessUnit = unit,
                SubscriptionType = SubscriptionType.Trial,
                StartDate = DateTime.UtcNow.Date,
                EndDate = DateTime.UtcNow.Date.AddDays(trialPeriodDays)
            });

            await _context.SaveChangesAsync();

            return unit;
        }

        /// <summary>
        /// Trims a registration field, treating an absent one as empty.
        ///
        /// The contract carries these as nullable because a personal sign-up sends
        /// them as null. Every field this is applied to on the company path has
        /// already been through the validator, so the empty fallback only ever
        /// stands in on the personal path, which does not read them.
        /// </summary>
        private static string Text(string? value) => value?.Trim() ?? string.Empty;

        /// <summary>
        /// A code and its short form. The short code is the first 8 hex characters of
        /// a GUID, which is what makes it readable enough to print and type.
        /// </summary>
        private static (string Code, string ShortCode) GenerateCodes()
        {
            var code = Guid.NewGuid().ToString("D");

            return (code, code[..MobileApiConstants.ShortCodeLength]);
        }

        // ── Shared helpers ──────────────────────────────────────────────────

        private Task<BusinessUnit?> LoadUnitWithCompanyAsync(uint businessUnitId) =>
            _context.BusinessUnits
                .AsNoTracking()
                .Include(x => x.Profile)
                .FirstOrDefaultAsync(x => x.Id == businessUnitId);

        private async Task<uint> GetDefaultContentCultureIdAsync()
        {
            var culture = await _context.ContentCultures
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.CultureName == MobileApiConstants.DefaultContentCultureName)
                ?? await _context.ContentCultures.AsNoTracking().FirstOrDefaultAsync();

            return culture?.Id ?? throw new InvalidOperationException("No ContentCulture entries exist.");
        }

        /// <summary>
        /// The timestamp columns are timestamptz, which Npgsql will only accept as UTC.
        /// A client sending a bare local time arrives as Unspecified; it is read as the
        /// UTC the field name promises rather than as server-local time.
        /// </summary>
        private static DateTime? ToUtc(DateTime value)
        {
            if (value == default)
            {
                return null;
            }

            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }
    }
}
