using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.TeamQuestions;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    /// <summary>
    /// Custom Questions — questions a company authors for one of its business units,
    /// and the free-text answers given to them.
    ///
    /// A question is owned by ProfileId + UnitId, scheduled with a single ScheduledAt
    /// stamp, and carries a lifecycle Status. Every write to either table is
    /// snapshotted into its audit table — see CustomQuestionEntities.cs.
    /// </summary>
    public class CustomQuestionRepository : ICustomQuestionRepository
    {
        private readonly ApplicationDbContext _context;

        public CustomQuestionRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<CreateCustomQuestionResultDto> AddCustomQuestionAsync(
            CreateCustomQuestionDto dto,
            int? createdBy)
        {
            var text = (dto.QuestionText ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                return Failed("QuestionText is required.");
            }

            if (text.Length > 1000)
            {
                return Failed("QuestionText cannot be longer than 1000 characters.");
            }

            // ── Ownership ──
            if (dto.ProfileId <= 0)
            {
                return Failed("ProfileId is required.");
            }

            if (dto.UnitId <= 0)
            {
                return Failed("UnitId is required.");
            }

            var profileId = (uint)dto.ProfileId;
            var unitId = (uint)dto.UnitId;

            var companyExists = await _context.Profiles.AnyAsync(c => c.Id == profileId);

            if (!companyExists)
            {
                return Failed($"Company {dto.ProfileId} was not found.");
            }

            // Checked against the company as well as by Id: a question filed under a unit
            // belonging to a different company would be invisible to both tenants.
            var unit = await _context.BusinessUnits
                .Include(u => u.Profile)
                .FirstOrDefaultAsync(u => u.Id == unitId && u.IsDel != true);

            if (unit == null)
            {
                return Failed($"Business unit {dto.UnitId} was not found.");
            }

            if (unit.Profile == null || unit.Profile.Id != profileId)
            {
                return Failed($"Business unit {dto.UnitId} does not belong to company {dto.ProfileId}.");
            }

            // ── Scheduling ──
            // The column is timestamptz, so Npgsql refuses anything that is not UTC.
            var scheduledAt = ToUtc(dto.ScheduledAt);

            // ── Status ──
            CustomQuestionStatus status;

            if (string.IsNullOrWhiteSpace(dto.Status))
            {
                // Derived rather than defaulted flat to Draft: a question posted with a future
                // stamp is already scheduled, and saying so saves a second call.
                status = scheduledAt.HasValue && scheduledAt.Value > DateTime.UtcNow
                    ? CustomQuestionStatus.Scheduled
                    : CustomQuestionStatus.Draft;
            }
            else if (!TryParseEnum<CustomQuestionStatus>(dto.Status, out status))
            {
                return Failed($"Invalid Status '{dto.Status}'. Valid values are: " +
                              "Draft, Scheduled, Active, Expired, Cancelled.");
            }

            var normalizedText = text.ToLowerInvariant();

            // Scoped to the owning unit: the same wording is legitimate in two different
            // teams, so a global uniqueness check would block real questions.
            var alreadyExists = await _context.CustomQuestions
                .AnyAsync(q => !q.IsDel
                               && q.ProfileId == profileId
                               && q.UnitId == unitId
                               && q.Text.ToLower() == normalizedText);

            if (alreadyExists)
            {
                return Failed("A custom question with this text already exists for this company and unit.");
            }

            var contentCulture = await GetDefaultContentCultureAsync();
            var now = DateTime.UtcNow;

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var question = new CustomQuestion
                {
                    Text = text,
                    ContentCultureId = contentCulture.Id,
                    ProfileId = profileId,
                    UnitId = unitId,
                    ScheduledAt = scheduledAt,
                    Status = status,
                    CreatedBy = createdBy,
                    UpdatedBy = createdBy,
                    CreatedAt = now,
                    UpdatedAt = now,
                    IsDel = false
                };

                _context.CustomQuestions.Add(question);
                await _context.SaveChangesAsync();

                _context.CustomQuestionAudits.Add(new CustomQuestionAudit
                {
                    CustomQuestionId = question.Id,
                    Text = question.Text,
                    ContentCultureId = question.ContentCultureId,
                    ProfileId = question.ProfileId,
                    UnitId = question.UnitId,
                    ScheduledAt = question.ScheduledAt,
                    Status = question.Status,
                    ChangeSummary = $"Created as {status}.",
                    CreatedBy = question.CreatedBy,
                    UpdatedBy = question.UpdatedBy,
                    CreatedAt = question.CreatedAt,
                    UpdatedAt = question.UpdatedAt,
                    IsDel = false,
                    LoggedAt = now,
                    VersionNumber = 1,
                    OperationType = "INSERT"
                });

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return new CreateCustomQuestionResultDto
                {
                    IsSuccess = true,
                    Question = Map(question)
                };
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<CreateCustomQuestionFeedbackResultDto> AddCustomQuestionFeedbackAsync(
            CreateCustomQuestionFeedbackDto dto,
            int? createdBy)
        {
            if (dto.CustomQuestionId <= 0)
            {
                return FeedbackFailed("CustomQuestionId is required.");
            }

            if (dto.UnitId <= 0)
            {
                return FeedbackFailed("UnitId is required.");
            }

            var questionId = (uint)dto.CustomQuestionId;
            var unitId = (uint)dto.UnitId;

            // The owning unit is read rather than the row merely counted: the answer records
            // which unit it came from, and that has to be checked against the question.
            var question = await _context.CustomQuestions
                .AsNoTracking()
                .Where(q => q.Id == questionId && !q.IsDel)
                .Select(q => new { q.Id, q.UnitId })
                .FirstOrDefaultAsync();

            if (question == null)
            {
                return FeedbackFailed($"Custom question {dto.CustomQuestionId} was not found.");
            }

            // A custom question belongs to exactly one unit, so an answer from any other unit
            // is a caller mistake, not a case to store. Refusing it keeps the stored UnitId
            // and the question's UnitId from ever disagreeing.
            if (question.UnitId != unitId)
            {
                return FeedbackFailed(
                    $"Custom question {dto.CustomQuestionId} belongs to business unit {question.UnitId}, not {dto.UnitId}.");
            }

            var freeText = string.IsNullOrWhiteSpace(dto.FreeText) ? null : dto.FreeText.Trim();

            if (freeText != null && freeText.Length > 300)
            {
                return FeedbackFailed("FreeText cannot be longer than 300 characters.");
            }

            var now = DateTime.UtcNow;

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var feedback = new CustomQuestionDailyFeedback
                {
                    CustomQuestionId = questionId,
                    UnitId = unitId,
                    FreeText = freeText,
                    CreatedBy = createdBy,
                    UpdatedBy = createdBy,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                _context.CustomQuestionDailyFeedback.Add(feedback);
                await _context.SaveChangesAsync();

                _context.CustomQuestionDailyFeedbackAudits.Add(new CustomQuestionDailyFeedbackAudit
                {
                    CustomQuestionDailyFeedbackId = feedback.Id,
                    CustomQuestionId = feedback.CustomQuestionId,
                    UnitId = feedback.UnitId,
                    FreeText = feedback.FreeText,
                    ChangeSummary = "Answer recorded.",
                    CreatedBy = feedback.CreatedBy,
                    UpdatedBy = feedback.UpdatedBy,
                    CreatedAt = feedback.CreatedAt,
                    UpdatedAt = feedback.UpdatedAt,
                    LoggedAt = now,
                    VersionNumber = 1,
                    OperationType = "INSERT"
                });

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return new CreateCustomQuestionFeedbackResultDto
                {
                    IsSuccess = true,
                    Feedback = new CustomQuestionFeedbackRecordDto
                    {
                        FeedbackId = feedback.Id,
                        CustomQuestionId = feedback.CustomQuestionId,
                        UnitId = feedback.UnitId,
                        FreeText = feedback.FreeText,
                        CreatedBy = feedback.CreatedBy,
                        CreatedAt = feedback.CreatedAt
                    }
                };
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // ── Reading what is due ─────────────────────────────────────────────

        public async Task<ScheduledCustomQuestionsResultDto> GetScheduledCustomQuestionsAsync(
            long? unitId,
            string? unitShortCode)
        {
            var hasUnitId = unitId.HasValue && unitId.Value > 0;
            var shortCode = string.IsNullOrWhiteSpace(unitShortCode) ? null : unitShortCode.Trim();

            if (!hasUnitId && shortCode == null)
            {
                return ReadFailed("Provide either UnitId or UnitShortCode.");
            }

            // Either identifier resolves the same unit. Passing both is allowed and treated
            // as an AND: they have to point at the same unit, so a caller holding a stale
            // pairing is told about it rather than silently served another team's questions.
            var unitQuery = _context.BusinessUnits.AsNoTracking().Where(u => u.IsDel != true);

            if (hasUnitId)
            {
                var id = (uint)unitId!.Value;
                unitQuery = unitQuery.Where(u => u.Id == id);
            }

            if (shortCode != null)
            {
                unitQuery = unitQuery.Where(u => u.BusinessUnitShortCode == shortCode);
            }

            var unit = await unitQuery
                .Select(u => new
                {
                    u.Id,
                    u.BusinessUnitShortCode,
                    u.Name,
                    ProfileId = u.Profile.Id
                })
                .FirstOrDefaultAsync();

            if (unit == null)
            {
                return ReadFailed(DescribeMissingUnit(hasUnitId ? unitId : null, shortCode));
            }

            // ── Today ──
            // The date is the backend's to decide, not the caller's: the client asks what
            // its unit should be answering now. ScheduledAt is timestamptz, so the window
            // bounds have to be UTC or Npgsql refuses the comparison.
            var today = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
            var dayStart = today;
            var dayEnd = today.AddDays(1);

            // A custom question carries one ScheduledAt, not a from/to pair, so its validity
            // period is the UTC day that stamp falls in, and it runs out at dayEnd. Selecting
            // on today's window is therefore the whole of "scheduled for today and not past
            // its validity period" — a question outside the window is either not due yet or
            // already over. Status is checked too but is not relied on for this: the expiry
            // sweep runs on a schedule, so a question can still read Scheduled after its day
            // has ended and must not be served.
            var rows = await _context.CustomQuestions
                .AsNoTracking()
                .Where(q => !q.IsDel
                            && q.UnitId == unit.Id
                            && q.ScheduledAt != null
                            && q.ScheduledAt >= dayStart
                            && q.ScheduledAt < dayEnd
                            // Draft was never published, Cancelled was pulled and Expired
                            // has already been swept — none of them are still active.
                            && (q.Status == CustomQuestionStatus.Scheduled
                                || q.Status == CustomQuestionStatus.Active))
                .OrderBy(q => q.ScheduledAt)
                .ThenBy(q => q.Id)
                // Status stays an enum through the query and is named below: it is an int
                // column, so asking the provider to translate ToString() on it buys nothing.
                .Select(q => new
                {
                    q.Id,
                    q.Text,
                    q.ProfileId,
                    q.UnitId,
                    q.Status,
                    q.ScheduledAt
                })
                .ToListAsync();

            var questions = rows
                .Select(q => new ScheduledCustomQuestionDto
                {
                    CustomQuestionId = q.Id,
                    QuestionText = q.Text,
                    ProfileId = q.ProfileId,
                    UnitId = q.UnitId,
                    Status = q.Status.ToString(),
                    ScheduledAt = q.ScheduledAt,
                    UnitShortCode = unit.BusinessUnitShortCode,
                    UnitName = unit.Name,
                    ValidUntil = dayEnd
                })
                .ToList();

            return new ScheduledCustomQuestionsResultDto
            {
                IsSuccess = true,
                Data = new ScheduledCustomQuestionsDto
                {
                    Date = today,
                    ProfileId = unit.ProfileId,
                    UnitId = unit.Id,
                    UnitShortCode = unit.BusinessUnitShortCode,
                    UnitName = unit.Name,
                    TotalCount = questions.Count,
                    Questions = questions
                }
            };
        }

        // ── Expiry sweep ────────────────────────────────────────────────────

        public async Task<ExpireCustomQuestionsResultDto> ExpireDueCustomQuestionsAsync(int? updatedBy = null)
        {
            var now = DateTime.UtcNow;

            // The validity period ends at the midnight after the scheduled day, so anything
            // stamped before today's midnight has had its whole day and is done. A question
            // scheduled for today is left alone however early in the day this runs.
            var cutoff = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);

            var due = await _context.CustomQuestions
                .Where(q => !q.IsDel
                            && q.ScheduledAt != null
                            && q.ScheduledAt < cutoff
                            && (q.Status == CustomQuestionStatus.Scheduled
                                || q.Status == CustomQuestionStatus.Active))
                .ToListAsync();

            var result = new ExpireCustomQuestionsResultDto { EvaluatedAtUtc = now };

            if (due.Count == 0)
            {
                return result;
            }

            var dueIds = due.Select(q => q.Id).ToList();

            // One grouped read rather than a query per question: the audit trail numbers
            // versions per question, and a sweep can touch a lot of rows at once.
            var lastVersions = await _context.CustomQuestionAudits
                .Where(a => dueIds.Contains(a.CustomQuestionId))
                .GroupBy(a => a.CustomQuestionId)
                .Select(g => new { QuestionId = g.Key, LastVersion = g.Max(a => a.VersionNumber) })
                .ToDictionaryAsync(x => x.QuestionId, x => x.LastVersion);

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                foreach (var question in due)
                {
                    var previousStatus = question.Status;

                    // The stamp says which day it was valid for; the period ran out at the
                    // midnight after it.
                    var validUntil = DateTime
                        .SpecifyKind(question.ScheduledAt!.Value.ToUniversalTime().Date, DateTimeKind.Utc)
                        .AddDays(1);

                    question.Status = CustomQuestionStatus.Expired;
                    question.UpdatedBy = updatedBy ?? question.UpdatedBy;
                    question.UpdatedAt = now;

                    _context.CustomQuestionAudits.Add(new CustomQuestionAudit
                    {
                        CustomQuestionId = question.Id,
                        Text = question.Text,
                        ContentCultureId = question.ContentCultureId,
                        ProfileId = question.ProfileId,
                        UnitId = question.UnitId,
                        ScheduledAt = question.ScheduledAt,
                        Status = question.Status,
                        ChangeSummary =
                            $"Moved from {previousStatus} to Expired: validity period ended {validUntil:O}.",
                        CreatedBy = question.CreatedBy,
                        UpdatedBy = question.UpdatedBy,
                        CreatedAt = question.CreatedAt,
                        UpdatedAt = question.UpdatedAt,
                        IsDel = question.IsDel,
                        LoggedAt = now,
                        VersionNumber = (lastVersions.TryGetValue(question.Id, out var last) ? last : 0) + 1,
                        OperationType = "EXPIRE"
                    });

                    result.ExpiredQuestionIds.Add(question.Id);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }

            result.ExpiredCount = result.ExpiredQuestionIds.Count;

            return result;
        }

        private static CustomQuestionRecordDto Map(CustomQuestion question) => new CustomQuestionRecordDto
        {
            CustomQuestionId = question.Id,
            QuestionText = question.Text,
            ProfileId = question.ProfileId,
            UnitId = question.UnitId,
            ScheduledAt = question.ScheduledAt,
            Status = question.Status.ToString(),
            CreatedBy = question.CreatedBy,
            CreatedAt = question.CreatedAt
        };

        private async Task<ContentCulture> GetDefaultContentCultureAsync()
        {
            var culture = await _context.ContentCultures
                .FirstOrDefaultAsync(c => c.CultureName == DataConstants.DefaultContentCultureName)
                ?? await _context.ContentCultures.FirstOrDefaultAsync();

            return culture ?? throw new InvalidOperationException("No ContentCulture entries exist.");
        }

        /// <summary>
        /// Accepts the enum name in any casing, and rejects the bare numeric form —
        /// Enum.TryParse would otherwise take "47" as a valid value.
        /// </summary>
        private static bool TryParseEnum<TEnum>(string? value, out TEnum parsed) where TEnum : struct, Enum
        {
            parsed = default;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var text = value.Trim().Replace(" ", string.Empty);

            return Enum.TryParse(text, ignoreCase: true, out parsed) && Enum.IsDefined(typeof(TEnum), parsed);
        }

        /// <summary>
        /// ScheduledAt is timestamptz. A client posting "2026-09-01T09:00:00" with no
        /// offset arrives as Unspecified, which Npgsql rejects; it is read as the UTC the
        /// field name promises rather than as server-local time.
        /// </summary>
        private static DateTime? ToUtc(DateTime? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            return value.Value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.Value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            };
        }

        /// <summary>
        /// Names whichever identifier the caller actually supplied, so a wrong short code
        /// does not come back reported as a missing id.
        /// </summary>
        private static string DescribeMissingUnit(long? unitId, string? shortCode)
        {
            if (unitId.HasValue && shortCode != null)
            {
                return $"No business unit matches UnitId {unitId.Value} and UnitShortCode '{shortCode}'.";
            }

            if (unitId.HasValue)
            {
                return $"Business unit {unitId.Value} was not found.";
            }

            return $"Business unit with short code '{shortCode}' was not found.";
        }

        private static CreateCustomQuestionResultDto Failed(string message) =>
            new CreateCustomQuestionResultDto { IsSuccess = false, ErrorMessage = message };

        private static ScheduledCustomQuestionsResultDto ReadFailed(string message) =>
            new ScheduledCustomQuestionsResultDto { IsSuccess = false, ErrorMessage = message };

        private static CreateCustomQuestionFeedbackResultDto FeedbackFailed(string message) =>
            new CreateCustomQuestionFeedbackResultDto { IsSuccess = false, ErrorMessage = message };
    }
}
