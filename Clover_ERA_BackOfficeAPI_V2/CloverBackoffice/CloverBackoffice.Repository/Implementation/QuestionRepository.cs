using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    public class QuestionRepository : IQuestionRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration? _configuration;

        /// <summary>
        /// Safe delimiter for import flow to avoid splitting on commas within text content.
        /// </summary>
        private const string ImportDelimiter = "|||";

        /// <summary>
        /// Optional time zone the daily question rotation is anchored to. When unset, the
        /// server's local date is used. See <see cref="ResolveQuestionDate" />.
        /// </summary>
        private const string QuestionTimeZoneConfigKey = "DailyQuestion:TimeZoneId";

        public QuestionRepository(ApplicationDbContext context, IConfiguration? configuration = null)
        {
            _context = context;
            _configuration = configuration;
        }

        public async Task<bool> AddDailyQuestionAsync(CreateDailyQuestionDto dto)
        {
            if (dto == null)
            {
                throw new ArgumentNullException(nameof(dto));
            }

            if (string.IsNullOrWhiteSpace(dto.Question))
            {
                throw new ArgumentException("Question text is required.", nameof(dto.Question));
            }

            if (string.IsNullOrWhiteSpace(dto.Category))
            {
                throw new ArgumentException("Category is required.", nameof(dto.Category));
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var contentCulture = await GetDefaultContentCultureAsync();
                var cloverCategory = ParseCategory(dto.Category);
                var question = new DailyQuestion
                {
                    Text = dto.Question.Trim(),
                    ContentCulture = contentCulture
                };

                _context.DailyQuestions.Add(question);
                await _context.SaveChangesAsync();


                var questionSetGroup = await GetDefaultQuestionSetGroupAsync();
                var questionSet = await GetOrCreateQuestionSetAsync(questionSetGroup, contentCulture);
                var nextOrdinal = await GetNextOrdinalAsync(questionSetGroup.Id);

                var specification = new DailyQuestionSpecification
                {
                    DailyQuestionSetGroupId = questionSetGroup.Id,
                    DailyQuestionSetGroup = questionSetGroup,
                    DailyQuestionSet = questionSet,
                    CloverCategory = cloverCategory,
                    Ordinal = nextOrdinal,
                    ContentCultureId = contentCulture.Id,
                    ContentCulture = contentCulture,
                    Question = question,
                    Reasons = new List<DailyReason>()
                };

                _context.DailyQuestionSpecifications.Add(specification);
                await _context.SaveChangesAsync();

                var positiveReasons = BuildReasons(dto.PositiveReasons, DailyReasonSentimentType.Positive, contentCulture, specification);
                var negativeReasons = BuildReasons(dto.NegativeReasons, DailyReasonSentimentType.Negative, contentCulture, specification);

                if (positiveReasons.Count == 0 && negativeReasons.Count == 0)
                {
                    throw new InvalidOperationException("At least one reason must be provided.");
                }

                _context.DailyReasons.AddRange(positiveReasons);
                _context.DailyReasons.AddRange(negativeReasons);

                await _context.SaveChangesAsync();

                var positiveReasonIds = await LoadPersistedReasonIdsAsync(specification.Id, DailyReasonSentimentType.Positive);
                var negativeReasonIds = await LoadPersistedReasonIdsAsync(specification.Id, DailyReasonSentimentType.Negative);

                var positiveActions = BuildMicroActions(positiveReasonIds, dto.PositiveActions, contentCulture);
                var negativeActions = BuildMicroActions(negativeReasonIds, dto.NegativeActions, contentCulture);

                if (positiveActions.Count > 0)
                {
                    _context.DailyMicroActions.AddRange(positiveActions);
                }

                if (negativeActions.Count > 0)
                {
                    _context.DailyMicroActions.AddRange(negativeActions);
                }

                if (positiveActions.Count > 0 || negativeActions.Count > 0)
                {
                    await _context.SaveChangesAsync();
                }

                await transaction.CommitAsync();
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                // Re-throw with more context for debugging
                throw new InvalidOperationException($"Failed to add daily question: {ex.Message}", ex);
            }
        }

        private async Task<ContentCulture> GetDefaultContentCultureAsync()
        {
            var culture = await _context.ContentCultures
                .FirstOrDefaultAsync(c => c.CultureName == DataConstants.DefaultContentCultureName)
                ?? await _context.ContentCultures.FirstOrDefaultAsync();

            return culture ?? throw new InvalidOperationException("No ContentCulture entries exist.");
        }

        private async Task<DailyQuestionSetGroup> GetDefaultQuestionSetGroupAsync()
        {
            return await _context.DailyQuestionSetGroups
                .FirstOrDefaultAsync(g => g.Id == DataConstants.DefaultDailyQuestionSetGroupId)
                ?? throw new InvalidOperationException("Default DailyQuestionSetGroup is missing.");
        }

        private async Task<DailyQuestionSet> GetOrCreateQuestionSetAsync(DailyQuestionSetGroup group, ContentCulture culture)
        {
            // Query with Include to get navigation properties
            var questionSet = await _context.DailyQuestionSets
                .Include(s => s.DailyQuestionSetGroup)
                .Include(s => s.ContentCulture)
                .FirstOrDefaultAsync(s => s.DailyQuestionSetGroup.Id == group.Id && s.ContentCulture.Id == culture.Id);

            if (questionSet != null)
            {
                return questionSet;
            }

            questionSet = new DailyQuestionSet
            {
                VersionNumber = 1,
                Name = $"Default-{culture.CultureName}",
                ContentCulture = culture,
                DailyQuestionSetGroup = group
            };

            _context.DailyQuestionSets.Add(questionSet);
            await _context.SaveChangesAsync();

            return questionSet;
        }

        private async Task<ushort> GetNextOrdinalAsync(uint groupId)
        {
            var maxOrdinal = await _context.DailyQuestionSpecifications
                .Where(s => s.DailyQuestionSetGroupId == groupId)
                .MaxAsync(s => (ushort?)s.Ordinal) ?? 0;

            return (ushort)(maxOrdinal + 1);
        }

        private static CloverCategory ParseCategory(string category)
        {
            if (Enum.TryParse<CloverCategory>(category, true, out var parsed))
            {
                return parsed;
            }

            throw new ArgumentException($"Unsupported Clover category '{category}'.", nameof(category));
        }

        private static List<DailyReason> BuildReasons(string csv, DailyReasonSentimentType sentiment, ContentCulture culture, DailyQuestionSpecification specification)
        {
            var texts = SplitCsv(csv);
            var reasons = new List<DailyReason>(texts.Count);

            foreach (var text in texts)
            {
                var isOther = text.Trim().StartsWith("Other – I’ll explain further in the text box", StringComparison.OrdinalIgnoreCase);
                var reason = new DailyReason
                {
                    DailyReasonSentimentType = sentiment,
                    Text = SanitizeText(text),
                    RequiresFreeTextFeedback = isOther,
                    SpecificationId = specification.Id,
                    Specification = specification,
                    ContentCulture = culture
                };

                specification.Reasons.Add(reason);
                reasons.Add(reason);
            }

            return reasons;
        }

        private async Task<List<uint>> LoadPersistedReasonIdsAsync(uint specificationId, DailyReasonSentimentType sentiment)
        {
            return await _context.DailyReasons
                .Where(r => r.SpecificationId == specificationId && r.DailyReasonSentimentType == sentiment)
                .OrderBy(r => r.Id)
                .Select(r => r.Id)
                .ToListAsync();
        }

        private static List<DailyMicroAction> BuildMicroActions(IReadOnlyList<uint> reasonIds, string csv, ContentCulture culture)
        {
            var actionTexts = SplitCsv(csv);
            var actions = new List<DailyMicroAction>();

            for (var i = 0; i < reasonIds.Count && i < actionTexts.Count; i++)
            {
                var actionText = actionTexts[i];
                if (string.IsNullOrWhiteSpace(actionText))
                {
                    continue;
                }

                var reasonId = reasonIds[i];

                if (reasonId == 0)
                {
                    throw new InvalidOperationException($"Reason at index {i} does not have a valid ID. Reasons must be saved before creating actions.");
                }

                actions.Add(new DailyMicroAction
                {
                    Text = actionText.Trim(),
                    ContentCultureId = culture.Id,
                    ContentCulture = culture,
                    ReasonId = reasonId,
                    Reason = null!
                });
            }

            return actions;
        }

        private static List<DailyMicroAction> BuildMicroActionsWithManualIds(
            IReadOnlyList<uint> reasonIds,
            string csv,
            ContentCulture culture,
            ref long nextId,
            string delimiter = ",")
        {
            // Split preserving empty entries to maintain position mapping
            var actionTexts = string.IsNullOrWhiteSpace(csv)
                ? new List<string>()
                : csv.Split(delimiter, StringSplitOptions.None).Select(part => part.Trim()).ToList();

            var actions = new List<DailyMicroAction>();

            // Map actions to reasons: each action maps to the corresponding reason by index
            // If there are more actions than reasons, extra actions are ignored
            // If there are more reasons than actions, only the first N reasons get actions
            var maxCount = Math.Min(reasonIds.Count, actionTexts.Count);

            for (var i = 0; i < maxCount; i++)
            {
                var actionText = actionTexts[i];
                if (string.IsNullOrWhiteSpace(actionText))
                {
                    continue; // Skip empty actions but continue to next index
                }

                var reasonId = reasonIds[i];

                if (reasonId == 0)
                {
                    throw new InvalidOperationException($"Reason at index {i} does not have a valid ID. Reasons must be saved before creating actions.");
                }

                actions.Add(new DailyMicroAction
                {
                    Id = checked((uint)nextId++),
                    Text = actionText.Trim(),
                    ContentCultureId = culture.Id,
                    ContentCulture = culture,
                    ReasonId = reasonId,
                    Reason = null!
                });
            }

            return actions;
        }

        private static List<string> SplitCsv(string value, string delimiter = ",")
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new List<string>();
            }

            return value
                .Split(delimiter, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToList();
        }
        public async Task<CustomQuestionResponseDto?> AddCustomQuestionAsync(CustomAddQuestionDto dto)
        {
            // Single-add endpoint keeps duplicate checking against the database.
            return await AddCustomQuestionInternalAsync(dto, skipDuplicateCheck: false);
        }

        private async Task<CustomQuestionResponseDto?> AddCustomQuestionInternalAsync(CustomAddQuestionDto dto, bool skipDuplicateCheck)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Import passes skipDuplicateCheck = true so a question that already exists in
                // the database is still inserted as a NEW entry. Duplicate detection during
                // import is performed per-file only (see ImportQuestionsAsync).
                if (!skipDuplicateCheck)
                {
                    var normalizedText = dto.QuestionText.Trim().ToLower();

                    bool alreadyExists = await _context.DailyQuestions
                        .AnyAsync(q => q.Text.ToLower() == normalizedText);

                    if (alreadyExists)
                        return null;
                }

                // Always use ContentCultureId = 1
                var contentCulture = await _context.ContentCultures
                    .FirstOrDefaultAsync(c => c.Id == 1)
                    ?? throw new ArgumentException("Default ContentCulture '1' not found.");

                // Auto-generate ordinal
                var totalQuestions = await _context.DailyQuestions.CountAsync();
                var calculatedOrdinal = (ushort)(totalQuestions + 1);

                if (!Enum.TryParse<CloverCategory>(dto.CloverCategory, true, out var parsedCategory))
                    throw new ArgumentException("Invalid Clover Category");

                var questionSetGroup = await GetDefaultQuestionSetGroupAsync();
                var questionSet = await GetOrCreateQuestionSetAsync(questionSetGroup, contentCulture);

                // Insert Question
                // Get current max Id from DailyQuestions
                var maxId = await _context.DailyQuestions.MaxAsync(q => (long?)q.Id) ?? 0;
                var nextId = maxId + 1;

                var question = new DailyQuestion
                {
                    Id = (uint)nextId, // manually set Id
                    Text = dto.QuestionText.Trim(),
                    ContentCulture = contentCulture
                };
                _context.DailyQuestions.Add(question);
                await _context.SaveChangesAsync();


                // Insert Specification
                var maxSpecId = await _context.DailyQuestionSpecifications
                .MaxAsync(s => (long?)s.Id) ?? 0;

                var specification = new DailyQuestionSpecification
                {
                    Id = (uint)(maxSpecId + 1),   // MANUAL ID
                    DailyQuestionSetGroupId = questionSetGroup.Id,
                    DailyQuestionSetGroup = questionSetGroup,
                    DailyQuestionSet = questionSet,
                    CloverCategory = parsedCategory,

                    // Using auto generated ordinal
                    Ordinal = calculatedOrdinal,

                    ContentCultureId = contentCulture.Id,
                    ContentCulture = contentCulture,
                    Question = question,
                    Reasons = new List<DailyReason>()
                };
                _context.DailyQuestionSpecifications.Add(specification);
                await _context.SaveChangesAsync();

                // Insert Reasons
                await _context.Database.ExecuteSqlRawAsync("LOCK TABLE \"DailyReasons\" IN EXCLUSIVE MODE");
                var maxReasonId = await _context.DailyReasons.MaxAsync(r => (long?)r.Id) ?? 0;
                var nextReasonId = maxReasonId + 1;

                var delimiter = dto.Delimiter ?? ",";
                var positiveReasons = BuildReasonsWithManualIds(dto.PositiveReasons, DailyReasonSentimentType.Positive, contentCulture, specification, ref nextReasonId, delimiter);
                var negativeReasons = BuildReasonsWithManualIds(dto.NegativeReasons, DailyReasonSentimentType.Negative, contentCulture, specification, ref nextReasonId, delimiter);

                if (positiveReasons.Count == 0 && negativeReasons.Count == 0)
                {
                    throw new InvalidOperationException("At least one reason must be provided.");
                }

                _context.DailyReasons.AddRange(positiveReasons);
                _context.DailyReasons.AddRange(negativeReasons);
                await _context.SaveChangesAsync();

                var positiveReasonIds = positiveReasons.Select(r => r.Id).ToList();
                var negativeReasonIds = negativeReasons.Select(r => r.Id).ToList();

                // Insert Actions
                await _context.Database.ExecuteSqlRawAsync("LOCK TABLE \"DailyMicroActions\" IN EXCLUSIVE MODE");
                var maxMicroActionId = await _context.DailyMicroActions.MaxAsync(a => (long?)a.Id) ?? 0;
                var nextMicroActionId = maxMicroActionId + 1;

                var positiveActions = BuildMicroActionsWithManualIds(positiveReasonIds, dto.PositiveActions, contentCulture, ref nextMicroActionId, delimiter);
                var negativeActions = BuildMicroActionsWithManualIds(negativeReasonIds, dto.NegativeActions, contentCulture, ref nextMicroActionId, delimiter);

                if (positiveActions.Any()) _context.DailyMicroActions.AddRange(positiveActions);
                if (negativeActions.Any()) _context.DailyMicroActions.AddRange(negativeActions);
                if (positiveActions.Any() || negativeActions.Any()) await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return new CustomQuestionResponseDto
                {
                    DailyQuestionId = (long)question.Id,
                    QuestionText = question.Text,
                    SpecificationId = (long)specification.Id,
                    CloverCategory = dto.CloverCategory,

                    // Return the auto-generated ordinal
                    Ordinal = calculatedOrdinal,

                    PositiveReasons = positiveReasons.Select(r => r.Text),
                    NegativeReasons = negativeReasons.Select(r => r.Text),
                    PositiveActions = positiveActions.Select(a => a.Text),
                    NegativeActions = negativeActions.Select(a => a.Text)
                };
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private static List<DailyReason> BuildReasonsWithManualIds(
            string csv,
            DailyReasonSentimentType sentiment,
            ContentCulture culture,
            DailyQuestionSpecification specification,
            ref long nextId,
            string delimiter = ",")
        {
            var texts = SplitCsv(csv, delimiter);
            var reasons = new List<DailyReason>(texts.Count);

            foreach (var text in texts)
            {
                var isOther = text.Trim().Contains("Other–I'll expain futher in the text box", StringComparison.OrdinalIgnoreCase);
                var reason = new DailyReason
                {
                    Id = checked((uint)nextId++),
                    DailyReasonSentimentType = sentiment,
                    Text = SanitizeText(text),
                    RequiresFreeTextFeedback = isOther,
                    SpecificationId = specification.Id,
                    Specification = specification,
                    ContentCulture = culture
                };

                specification.Reasons.Add(reason);
                reasons.Add(reason);
            }

            return reasons;
        }

        public async Task<QuestionInfoResponseDto?> GetQuestionInfoByIdAsync(long questionId)
        {
            // Check if question exists
            var question = await _context.DailyQuestions
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == questionId);

            if (question == null)
            {
                return null;
            }

            // Get specification for this question
            var specification = await _context.DailyQuestionSpecifications
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Question.Id == questionId);

            if (specification == null)
            {
                return null;
            }

            // Get all reasons for this specification
            var reasons = await _context.DailyReasons
                .AsNoTracking()
                .Where(r => r.SpecificationId == specification.Id)
                .OrderBy(r => r.Id)
                .ToListAsync();

            var reasonIds = reasons.Select(r => r.Id).ToList();

            // Get all actions for all reasons
            var actions = await _context.DailyMicroActions
                .AsNoTracking()
                .Where(a => reasonIds.Contains(a.ReasonId))
                .OrderBy(a => a.ReasonId)
                .ThenBy(a => a.Id)
                .ToListAsync();

            // Build nested DTO structure
            var reasonInfoList = reasons.Select(reason =>
            {
                var reasonActions = actions
                    .Where(a => a.ReasonId == reason.Id)
                    .Select(a => new ActionInfoDto
                    {
                        ActionId = a.Id,
                        Text = a.Text,
                        // IsDel = a.IsDel
                    })
                    .ToList();

                return new ReasonInfoDto
                {
                    ReasonId = reason.Id,
                    Text = reason.Text,
                    SentimentType = reason.DailyReasonSentimentType.ToString(),
                    //  IsDel = reason.IsDel,
                    Actions = reasonActions
                };
            }).ToList();

            return new QuestionInfoResponseDto
            {
                QuestionId = question.Id,
                QuestionText = question.Text,
                Specification = new QuestionSpecificationInfoDto
                {
                    SpecificationId = specification.Id,
                    CloverCategory = specification.CloverCategory.ToString(),
                    Reasons = reasonInfoList
                }
            };
        }

        public async Task<UpdateQuestionInfoResultDto> UpdateQuestionInfoByIdAsync(UpdateQuestionInfoDto dto)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Get the question
                var question = await _context.DailyQuestions
                    .FirstOrDefaultAsync(q => q.Id == dto.QuestionId);

                if (question == null)
                {
                    await transaction.RollbackAsync();
                    return new UpdateQuestionInfoResultDto
                    {
                        IsSuccess = false,
                        ErrorMessage = $"Question with ID {dto.QuestionId} not found."
                    };
                }

                // Update question text
                question.Text = dto.QuestionText.Trim();
                _context.DailyQuestions.Update(question);
                await _context.SaveChangesAsync();

                // Get the specification
                var specification = await _context.DailyQuestionSpecifications
                    .Include(s => s.Reasons)
                    .FirstOrDefaultAsync(s => s.Id == dto.Specification.SpecificationId);

                if (specification == null)
                {
                    await transaction.RollbackAsync();
                    return new UpdateQuestionInfoResultDto
                    {
                        IsSuccess = false,
                        ErrorMessage = $"Specification with ID {dto.Specification.SpecificationId} not found."
                    };
                }

                // Validate and update CloverCategory
                if (!Enum.TryParse<CloverCategory>(dto.Specification.CloverCategory, true, out var parsedCategory))
                {
                    await transaction.RollbackAsync();
                    return new UpdateQuestionInfoResultDto
                    {
                        IsSuccess = false,
                        ErrorMessage = $"Invalid Clover category '{dto.Specification.CloverCategory}'. Valid values are: Communication, Learning, Opportunities, Vulnerability, Enablement, Reflection."
                    };
                }
                specification.CloverCategory = parsedCategory;
                _context.DailyQuestionSpecifications.Update(specification);
                await _context.SaveChangesAsync();

                // Get content culture
                var contentCulture = await GetDefaultContentCultureAsync();

                // Process reasons - only update/create/delete what's in the request
                if (dto.Specification.Reasons != null)
                {
                    foreach (var reasonDto in dto.Specification.Reasons)
                    {
                        // Specific check: Text is required unless we are deleting
                        if (reasonDto.IsDel != true && string.IsNullOrWhiteSpace(reasonDto.Text))
                        {
                            await transaction.RollbackAsync();
                            return new UpdateQuestionInfoResultDto
                            {
                                IsSuccess = false,
                                ErrorMessage = "Reason text cannot be empty."
                            };
                        }

                        if (reasonDto.IsDel == true && reasonDto.ReasonId.HasValue && reasonDto.ReasonId.Value > 0)
                        {
                            // Soft delete existing reason
                            var existingReason = await _context.DailyReasons
                                .FirstOrDefaultAsync(r => r.Id == reasonDto.ReasonId.Value);
                            if (existingReason == null)
                            {
                                await transaction.RollbackAsync();
                                return new UpdateQuestionInfoResultDto
                                {
                                    IsSuccess = false,
                                    ErrorMessage = $"Reason with ID {reasonDto.ReasonId.Value} not found. Cannot delete non-existent reason."
                                };
                            }
                            // existingReason.IsDel = true;
                        }
                        else if (reasonDto.ReasonId.HasValue && reasonDto.ReasonId.Value > 0)
                        {
                            // Update existing reason
                            var existingReason = await _context.DailyReasons
                                .FirstOrDefaultAsync(r => r.Id == reasonDto.ReasonId.Value);
                            if (existingReason == null)
                            {
                                await transaction.RollbackAsync();
                                return new UpdateQuestionInfoResultDto
                                {
                                    IsSuccess = false,
                                    ErrorMessage = $"Reason with ID {reasonDto.ReasonId.Value} not found. Cannot update non-existent reason."
                                };
                            }

                            if (string.IsNullOrWhiteSpace(reasonDto.SentimentType))
                            {
                                await transaction.RollbackAsync();
                                return new UpdateQuestionInfoResultDto
                                {
                                    IsSuccess = false,
                                    ErrorMessage = $"SentimentType is required for reason ID {reasonDto.ReasonId.Value}."
                                };
                            }

                            if (!Enum.TryParse<DailyReasonSentimentType>(reasonDto.SentimentType, true, out var sentimentType))
                            {
                                await transaction.RollbackAsync();
                                return new UpdateQuestionInfoResultDto
                                {
                                    IsSuccess = false,
                                    ErrorMessage = $"Invalid sentiment type '{reasonDto.SentimentType}' for reason ID {reasonDto.ReasonId.Value}. Valid values are: Positive, Negative."
                                };
                            }
                            existingReason.Text = reasonDto.Text.Trim();
                            existingReason.DailyReasonSentimentType = sentimentType;
                            existingReason.RequiresFreeTextFeedback = reasonDto.RequiresFreeTextFeedback;
                            // existingReason.IsDel = false;
                        }
                        else if (reasonDto.IsDel != true)
                        {
                            // Create new reason (only if not marked for deletion)
                            if (string.IsNullOrWhiteSpace(reasonDto.SentimentType))
                            {
                                await transaction.RollbackAsync();
                                return new UpdateQuestionInfoResultDto
                                {
                                    IsSuccess = false,
                                    ErrorMessage = "SentimentType is required when creating a new reason."
                                };
                            }

                            if (!Enum.TryParse<DailyReasonSentimentType>(reasonDto.SentimentType, true, out var sentimentType))
                            {
                                await transaction.RollbackAsync();
                                return new UpdateQuestionInfoResultDto
                                {
                                    IsSuccess = false,
                                    ErrorMessage = $"Invalid sentiment type '{reasonDto.SentimentType}' for new reason. Valid values are: Positive, Negative."
                                };
                            }

                            await _context.Database.ExecuteSqlRawAsync("LOCK TABLE \"DailyReasons\" IN EXCLUSIVE MODE");
                            var maxReasonId = await _context.DailyReasons.MaxAsync(r => (long?)r.Id) ?? 0;
                            var nextReasonId = maxReasonId + 1;

                            var newReason = new DailyReason
                            {
                                Id = checked((uint)nextReasonId),
                                Text = reasonDto.Text.Trim(),
                                DailyReasonSentimentType = sentimentType,
                                RequiresFreeTextFeedback = reasonDto.RequiresFreeTextFeedback,
                                SpecificationId = specification.Id,
                                Specification = specification,
                                ContentCulture = contentCulture,
                                //   IsDel = false
                            };
                            _context.DailyReasons.Add(newReason);
                            await _context.SaveChangesAsync();

                            // Update the DTO with the new ID for action processing
                            reasonDto.ReasonId = newReason.Id;
                        }
                    }
                }

                await _context.SaveChangesAsync();

                // Process actions for each reason
                if (dto.Specification.Reasons != null)
                {
                    foreach (var reasonDto in dto.Specification.Reasons)
                    {
                        if (reasonDto.ReasonId.HasValue && reasonDto.ReasonId.Value > 0 && reasonDto.Actions != null)
                        {
                            var reasonId = (uint)reasonDto.ReasonId.Value;

                            // Update or create actions
                            foreach (var actionDto in reasonDto.Actions)
                            {
                                if (actionDto.IsDel == true && actionDto.ActionId.HasValue && actionDto.ActionId.Value > 0)
                                {
                                    // Soft delete existing action
                                    var existingAction = await _context.DailyMicroActions
                                        .FirstOrDefaultAsync(a => a.Id == actionDto.ActionId.Value);
                                    if (existingAction == null)
                                    {
                                        await transaction.RollbackAsync();
                                        return new UpdateQuestionInfoResultDto
                                        {
                                            IsSuccess = false,
                                            ErrorMessage = $"Action with ID {actionDto.ActionId.Value} not found. Cannot delete non-existent action."
                                        };
                                    }
                                    // existingAction.IsDel = true;
                                }
                                else if (actionDto.ActionId.HasValue && actionDto.ActionId.Value > 0)
                                {
                                    // Update existing action
                                    var existingAction = await _context.DailyMicroActions
                                        .FirstOrDefaultAsync(a => a.Id == actionDto.ActionId.Value);
                                    if (existingAction == null)
                                    {
                                        await transaction.RollbackAsync();
                                        return new UpdateQuestionInfoResultDto
                                        {
                                            IsSuccess = false,
                                            ErrorMessage = $"Action with ID {actionDto.ActionId.Value} not found. Cannot update non-existent action."
                                        };
                                    }

                                    if (actionDto.IsDel != true && string.IsNullOrWhiteSpace(actionDto.Text))
                                    {
                                        await transaction.RollbackAsync();
                                        return new UpdateQuestionInfoResultDto
                                        {
                                            IsSuccess = false,
                                            ErrorMessage = $"Action text cannot be empty for action ID {actionDto.ActionId.Value}."
                                        };
                                    }

                                    existingAction.Text = actionDto.Text.Trim();
                                    // existingAction.IsDel = false;
                                }
                                else if (actionDto.IsDel != true && !string.IsNullOrWhiteSpace(actionDto.Text))
                                {
                                    // Create new action (only if not marked for deletion and has text)
                                    await _context.Database.ExecuteSqlRawAsync("LOCK TABLE \"DailyMicroActions\" IN EXCLUSIVE MODE");
                                    var maxMicroActionId = await _context.DailyMicroActions.MaxAsync(a => (long?)a.Id) ?? 0;
                                    var nextMicroActionId = maxMicroActionId + 1;

                                    var newAction = new DailyMicroAction
                                    {
                                        Id = checked((uint)nextMicroActionId),
                                        Text = actionDto.Text.Trim(),
                                        ContentCultureId = contentCulture.Id,
                                        ContentCulture = contentCulture,
                                        ReasonId = reasonId,
                                        Reason = null!,
                                        //  IsDel = false
                                    };
                                    _context.DailyMicroActions.Add(newAction);
                                    await _context.SaveChangesAsync();
                                }
                            }
                        }
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Return updated question info
                var updatedQuestion = await GetQuestionInfoByIdAsync(dto.QuestionId);
                return new UpdateQuestionInfoResultDto
                {
                    IsSuccess = true,
                    ErrorMessage = null,
                    QuestionInfo = updatedQuestion
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new UpdateQuestionInfoResultDto
                {
                    IsSuccess = false,
                    ErrorMessage = $"An error occurred while updating the question: {ex.Message}"
                };
            }
        }

        public async Task<ImportQuestionResponseDto> ImportQuestionsAsync(IFormFile file)
        {
            var response = new ImportQuestionResponseDto();

            try
            {
                if (file == null || file.Length == 0)
                {
                    response.FailureCount = 1;
                    response.Results.Add(new ImportQuestionRowResultDto
                    {
                        RowNumber = 0,
                        IsSuccess = false,
                        Message = "File is empty or not provided.",
                        QuestionText = string.Empty
                    });
                    return response;
                }

                var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();

                if (fileExtension != ".csv" && fileExtension != ".xlsx")
                {
                    response.FailureCount = 1;
                    response.Results.Add(new ImportQuestionRowResultDto
                    {
                        RowNumber = 0,
                        IsSuccess = false,
                        Message = "Invalid file format. Only CSV and XLSX files are supported.",
                        QuestionText = string.Empty
                    });
                    return response;
                }

                // Validate file headers before processing
                string? validationError = null;
                if (fileExtension == ".csv")
                {
                    validationError = await ValidateCsvHeadersAsync(file);
                }
                else if (fileExtension == ".xlsx")
                {
                    validationError = await ValidateExcelHeadersAsync(file);
                }

                if (validationError != null)
                {
                    response.FailureCount = 1;
                    response.Results.Add(new ImportQuestionRowResultDto
                    {
                        RowNumber = 0,
                        IsSuccess = false,
                        Message = validationError,
                        QuestionText = string.Empty
                    });
                    return response;
                }

                // Reset file stream position for reading data
                List<ImportQuestionRowData> rows;
                if (fileExtension == ".csv")
                {
                    rows = await ReadCsvFileAsync(file);
                }
                else
                {
                    rows = await ReadExcelFileAsync(file);
                }

                response.TotalRows = rows.Count;

                // Track question texts seen within THIS uploaded file. Duplicate detection
                // applies to the file only; existing database records are never compared.
                var seenQuestionTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var rowData in rows)
                {
                    try
                    {
                        // Map Clover Element to CloverCategory
                        var cloverCategory = MapCloverElementToCategory(rowData.CloverElement);
                        if (string.IsNullOrWhiteSpace(cloverCategory))
                        {
                            response.FailureCount++;
                            response.Results.Add(new ImportQuestionRowResultDto
                            {
                                RowNumber = rowData.RowNumber,
                                IsSuccess = false,
                                Message = $"Invalid Clover Element '{rowData.CloverElement}'. Valid values are: C, L, O, V, E, R.",
                                QuestionText = rowData.QuestionText ?? string.Empty
                            });
                            continue;
                        }

                        // Validate required fields
                        if (string.IsNullOrWhiteSpace(rowData.QuestionText))
                        {
                            response.FailureCount++;
                            response.Results.Add(new ImportQuestionRowResultDto
                            {
                                RowNumber = rowData.RowNumber,
                                IsSuccess = false,
                                Message = "Question text is required.",
                                QuestionText = string.Empty
                            });
                            continue;
                        }

                        // Duplicate check within the uploaded file ONLY (not against the database).
                        // First occurrence is imported; later rows with the same text are skipped.
                        if (!seenQuestionTexts.Add(rowData.QuestionText.Trim()))
                        {
                            response.FailureCount++;
                            response.Results.Add(new ImportQuestionRowResultDto
                            {
                                RowNumber = rowData.RowNumber,
                                IsSuccess = false,
                                Message = "Duplicate question within the uploaded file. Only the first occurrence was imported.",
                                QuestionText = rowData.QuestionText
                            });
                            continue;
                        }

                        // Combine reasons and actions
                        // For reasons, filter out empty values
                        // Combine reasons and actions using alignment helper
                        var (positiveReasons, positiveActions) = AlignReasonsAndActions(
                            rowData.GoodReason1, rowData.GoodAction1,
                            rowData.GoodReason2, rowData.GoodAction2,
                            rowData.GoodReason3, rowData.GoodAction3);

                        var (negativeReasons, negativeActions) = AlignReasonsAndActions(
                            rowData.BadReason1, rowData.BadAction1,
                            rowData.BadReason2, rowData.BadAction2,
                            rowData.BadReason3, rowData.BadAction3);

                        // NEW: Append Good Reason 4 if present (no action for R4)
                        if (!string.IsNullOrWhiteSpace(rowData.GoodReason4))
                        {
                            positiveReasons = string.IsNullOrWhiteSpace(positiveReasons)
                                ? SanitizeText(rowData.GoodReason4)
                                : $"{positiveReasons},{SanitizeText(rowData.GoodReason4)}";

                            // Append empty action to maintain alignment (R4 has no micro-action)
                            positiveActions = string.IsNullOrWhiteSpace(positiveActions)
                                ? ""
                                : $"{positiveActions},";
                        }

                        // NEW: Append Bad Reason 4 if present (no action for R4)
                        if (!string.IsNullOrWhiteSpace(rowData.BadReason4))
                        {
                            negativeReasons = string.IsNullOrWhiteSpace(negativeReasons)
                                ? SanitizeText(rowData.BadReason4)
                                : $"{negativeReasons},{SanitizeText(rowData.BadReason4)}";

                            // Append empty action to maintain alignment (R4 has no micro-action)
                            negativeActions = string.IsNullOrWhiteSpace(negativeActions)
                                ? ""
                                : $"{negativeActions},";
                        }

                        // Validate at least one reason exists
                        if (string.IsNullOrWhiteSpace(positiveReasons) && string.IsNullOrWhiteSpace(negativeReasons))
                        {
                            response.FailureCount++;
                            response.Results.Add(new ImportQuestionRowResultDto
                            {
                                RowNumber = rowData.RowNumber,
                                IsSuccess = false,
                                Message = "At least one reason (Good or Bad) must be provided.",
                                QuestionText = rowData.QuestionText
                            });
                            continue;
                        }

                        // Auto-append "Other" reason if not already present in the file
                        const string OtherReasonText = "Other\u2013I'll expain futher in the text box";
                        if (!ContainsOtherReason(positiveReasons))
                        {
                            positiveReasons = string.IsNullOrWhiteSpace(positiveReasons)
                                ? OtherReasonText
                                : $"{positiveReasons},{OtherReasonText}";
                            // Append empty action to maintain alignment (Other has no micro-action)
                            positiveActions = string.IsNullOrWhiteSpace(positiveActions)
                                ? ""
                                : $"{positiveActions},";
                        }
                        if (!ContainsOtherReason(negativeReasons))
                        {
                            negativeReasons = string.IsNullOrWhiteSpace(negativeReasons)
                                ? OtherReasonText
                                : $"{negativeReasons},{OtherReasonText}";
                            negativeActions = string.IsNullOrWhiteSpace(negativeActions)
                                ? ""
                                : $"{negativeActions},";
                        }

                        // Create DTO
                        var dto = new CustomAddQuestionDto
                        {
                            QuestionText = SanitizeText(rowData.QuestionText),
                            CloverCategory = cloverCategory,
                            PositiveReasons = SanitizeText(positiveReasons) ?? string.Empty,
                            NegativeReasons = SanitizeText(negativeReasons) ?? string.Empty,
                            PositiveActions = SanitizeText(positiveActions) ?? string.Empty,
                            NegativeActions = SanitizeText(negativeActions) ?? string.Empty
                        };

                        // Call AddCustomQuestionAsync - wrap in try-catch to handle exceptions gracefully
                        CustomQuestionResponseDto? result = null;
                        string errorMessage = string.Empty;

                        try
                        {
                            // Import always inserts a NEW question (skipDuplicateCheck: true).
                            // Existing database records are intentionally NOT compared or updated;
                            // a duplicate of an existing question is created as a new entry.
                            result = await AddCustomQuestionInternalAsync(dto, skipDuplicateCheck: true);
                        }
                        catch (Exception ex)
                        {
                            errorMessage = $"Error processing question: {ex.Message}";
                        }

                        if (result == null)
                        {
                            response.FailureCount++;
                            response.Results.Add(new ImportQuestionRowResultDto
                            {
                                RowNumber = rowData.RowNumber,
                                IsSuccess = false,
                                Message = errorMessage,
                                QuestionText = rowData.QuestionText
                            });
                        }
                        else
                        {
                            response.SuccessCount++;
                            response.Results.Add(new ImportQuestionRowResultDto
                            {
                                RowNumber = rowData.RowNumber,
                                IsSuccess = true,
                                Message = "Question imported successfully.",
                                QuestionText = rowData.QuestionText,
                                QuestionData = result
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        response.FailureCount++;
                        response.Results.Add(new ImportQuestionRowResultDto
                        {
                            RowNumber = rowData.RowNumber,
                            IsSuccess = false,
                            Message = $"Error processing row: {ex.Message}",
                            QuestionText = rowData.QuestionText ?? string.Empty
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                response.FailureCount++;
                response.Results.Add(new ImportQuestionRowResultDto
                {
                    RowNumber = 0,
                    IsSuccess = false,
                    Message = $"Error reading file: {ex.Message}",
                    QuestionText = string.Empty
                });
            }

            return response;
        }

        /// <summary>
        /// Adds "Other" reason entries (one Good, one Bad) for ALL question specifications
        /// that don't already have them. This runs after import to ensure every question
        /// has the "Other" option available.
        /// Order: Old questions first, then new questions - so all "Other" entries go to the bottom.
        /// </summary>
        private async Task AddOtherReasonsToAllQuestionsAsync()
        {
            const string OtherReasonText = "Other–I'll expain futher in the text box";

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Get default content culture
                var contentCulture = await GetDefaultContentCultureAsync();

                // Get total questions count in the database
                var totalQuestions = await _context.DailyQuestionSpecifications.CountAsync();

                // Get all specification IDs ordered by ID (old questions first, then new)
                var allSpecificationIds = await _context.DailyQuestionSpecifications
                    .OrderBy(s => s.Id)
                    .Select(s => s.Id)
                    .ToListAsync();

                // Get all existing "Other" reasons to check for duplicates
                var existingOtherReasons = await _context.DailyReasons
                    .Where(r => r.RequiresFreeTextFeedback && r.Text.ToLower().Contains("other"))
                    .Select(r => new { r.SpecificationId, r.DailyReasonSentimentType })
                    .ToListAsync();

                // Create lookup for existing "Other" reasons
                var existingPositiveOthers = existingOtherReasons
                    .Where(r => r.DailyReasonSentimentType == DailyReasonSentimentType.Positive)
                    .Select(r => r.SpecificationId)
                    .ToHashSet();

                var existingNegativeOthers = existingOtherReasons
                    .Where(r => r.DailyReasonSentimentType == DailyReasonSentimentType.Negative)
                    .Select(r => r.SpecificationId)
                    .ToHashSet();

                // Lock the table to ensure consistent ID generation (requires transaction)
                await _context.Database.ExecuteSqlRawAsync("LOCK TABLE \"DailyReasons\" IN EXCLUSIVE MODE");

                // Get current max reason ID
                var maxReasonId = await _context.DailyReasons.MaxAsync(r => (long?)r.Id) ?? 0;
                var nextReasonId = maxReasonId + 1;

                var reasonsToAdd = new List<DailyReason>();

                // Process each specification in order (old first, then new)
                // Add "Other" option two times per question: one Good Reason + one Bad Reason
                foreach (var specificationId in allSpecificationIds)
                {
                    // Add Negative "Other" reason (Bad Reason)
                    if (!existingNegativeOthers.Contains(specificationId))
                    {
                        reasonsToAdd.Add(new DailyReason
                        {
                            Id = checked((uint)nextReasonId++),
                            DailyReasonSentimentType = DailyReasonSentimentType.Negative,
                            Text = OtherReasonText,
                            RequiresFreeTextFeedback = true,
                            SpecificationId = specificationId,
                            Specification = null!,
                            ContentCulture = contentCulture
                        });
                    }

                    // Add Positive "Other" reason (Good Reason)
                    if (!existingPositiveOthers.Contains(specificationId))
                    {
                        reasonsToAdd.Add(new DailyReason
                        {
                            Id = checked((uint)nextReasonId++),
                            DailyReasonSentimentType = DailyReasonSentimentType.Positive,
                            Text = OtherReasonText,
                            RequiresFreeTextFeedback = true,
                            SpecificationId = specificationId,
                            Specification = null!,
                            ContentCulture = contentCulture
                        });
                    }
                }

                // Bulk add all new "Other" reasons
                if (reasonsToAdd.Any())
                {
                    _context.DailyReasons.AddRange(reasonsToAdd);
                    await _context.SaveChangesAsync();
                }

                await transaction.CommitAsync();
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private async Task<string?> ValidateCsvHeadersAsync(IFormFile file)
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            using var reader = new StreamReader(stream, Encoding.UTF8);
            string? headerLine = await reader.ReadLineAsync();

            if (string.IsNullOrWhiteSpace(headerLine))
            {
                return "Invalid file format. File header row is missing.";
            }

            var headers = ParseCsvLine(headerLine);
            return ValidateHeaders(headers);
        }

        private async Task<string?> ValidateExcelHeadersAsync(IFormFile file)
        {
            OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            using var package = new ExcelPackage(stream);

            var worksheet = package.Workbook.Worksheets[0];

            if (worksheet.Dimension == null || worksheet.Dimension.Rows < 1)
            {
                return "Invalid file format. File header row is missing.";
            }

            var headers = new List<string>();
            int maxCol = worksheet.Dimension.Columns;

            for (int col = 1; col <= maxCol; col++)
            {
                var headerValue = worksheet.Cells[1, col].Text?.Trim() ?? string.Empty;
                headers.Add(headerValue);
            }

            return ValidateHeaders(headers);
        }

        private static string? ValidateHeaders(List<string> headers)
        {
            // Normalize headers (trim and case-insensitive comparison)
            var normalizedHeaders = headers.Select(h => h?.Trim() ?? string.Empty).ToList();

            // Required mandatory columns (accept alternate names)
            var requiredColumns = new[]
            {
                new[] { "Clover Element" },
                new[] { "New Question", "Question" }
            };

            // Check mandatory columns exist (search by name, not position)
            foreach (var alternatives in requiredColumns)
            {
                bool exists = alternatives.Any(alt =>
                    normalizedHeaders.Any(h => h.Equals(alt, StringComparison.OrdinalIgnoreCase)));

                if (!exists)
                {
                    return "Invalid file format. One or more required columns are missing.";
                }
            }

            // Check reason/action dependency pairs (new format with interleaved columns)
            // Each reason maps to multiple accepted action column names (space vs dash variants)
            var reasonActionPairs = new (string Reason, string[] ActionAlternatives)[]
            {
                ("Good Reason 1", new[] { "GR1 Micro-Action" }),
                ("Good Reason 2", new[] { "GR2 Micro-Action" }),
                ("Good Reason 3", new[] { "GR3 Micro-Action" }),
                ("Bad Reason 1",  new[] { "BR1 Micro-Action" }),
                ("Bad Reason 2",  new[] { "BR2 Micro-Action", "BR2-Micro-Action" }),
                ("Bad Reason 3",  new[] { "BR3 Micro-Action", "BR3-Micro-Action" })
            };

            foreach (var (reasonName, actionAlternatives) in reasonActionPairs)
            {
                bool reasonExists = normalizedHeaders.Any(h =>
                    h.Equals(reasonName, StringComparison.OrdinalIgnoreCase));

                bool actionExists = actionAlternatives.Any(alt =>
                    normalizedHeaders.Any(h => h.Equals(alt, StringComparison.OrdinalIgnoreCase)));

                // If reason exists, action must exist
                if (reasonExists && !actionExists)
                {
                    return "Invalid file format. Required reason/action columns are missing.";
                }
            }

            return null; // Validation passed
        }

        private async Task<List<ImportQuestionRowData>> ReadCsvFileAsync(IFormFile file)
        {
            var rows = new List<ImportQuestionRowData>();

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            using var reader = new StreamReader(stream, Encoding.UTF8);

            // Read header row and build header map
            var headerLine = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(headerLine))
                return rows;

            var headers = ParseCsvLine(headerLine);
            var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++)
            {
                var h = headers[i]?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(h) && !headerMap.ContainsKey(h))
                    headerMap[h] = i;
            }

            // Map header names to column indices
            int colClover = headerMap.GetValueOrDefault("Clover Element", -1);
            int colQuestion = headerMap.ContainsKey("New Question") ? headerMap["New Question"]
                            : headerMap.GetValueOrDefault("Question", -1);
            int colGR1 = headerMap.GetValueOrDefault("Good Reason 1", -1);
            int colGA1 = headerMap.GetValueOrDefault("GR1 Micro-Action", -1);
            int colGR2 = headerMap.GetValueOrDefault("Good Reason 2", -1);
            int colGA2 = headerMap.GetValueOrDefault("GR2 Micro-Action", -1);
            int colGR3 = headerMap.GetValueOrDefault("Good Reason 3", -1);
            int colGA3 = headerMap.GetValueOrDefault("GR3 Micro-Action", -1);
            int colGR4 = headerMap.GetValueOrDefault("Good Reason 4", -1);
            int colBR1 = headerMap.GetValueOrDefault("Bad Reason 1", -1);
            int colBA1 = headerMap.GetValueOrDefault("BR1 Micro-Action", -1);
            int colBR2 = headerMap.GetValueOrDefault("Bad Reason 2", -1);
            int colBA2 = headerMap.ContainsKey("BR2 Micro-Action") ? headerMap["BR2 Micro-Action"]
                       : headerMap.GetValueOrDefault("BR2-Micro-Action", -1);
            int colBR3 = headerMap.GetValueOrDefault("Bad Reason 3", -1);
            int colBA3 = headerMap.ContainsKey("BR3 Micro-Action") ? headerMap["BR3 Micro-Action"]
                       : headerMap.GetValueOrDefault("BR3-Micro-Action", -1);
            int colBR4 = headerMap.GetValueOrDefault("Bad Reason 4", -1);

            string ColVal(List<string> cols, int idx) => idx >= 0 && idx < cols.Count ? cols[idx]?.Trim() ?? string.Empty : string.Empty;

            int rowNumber = 1;
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                rowNumber++;
                var columns = ParseCsvLine(line);

                if (columns.Count < 2)
                    continue;

                var rowData = new ImportQuestionRowData
                {
                    RowNumber = rowNumber,
                    CloverElement = ColVal(columns, colClover),
                    QuestionText = ColVal(columns, colQuestion),
                    GoodReason1 = ColVal(columns, colGR1),
                    GoodAction1 = ColVal(columns, colGA1),
                    GoodReason2 = ColVal(columns, colGR2),
                    GoodAction2 = ColVal(columns, colGA2),
                    GoodReason3 = ColVal(columns, colGR3),
                    GoodAction3 = ColVal(columns, colGA3),
                    GoodReason4 = ColVal(columns, colGR4),
                    BadReason1 = ColVal(columns, colBR1),
                    BadAction1 = ColVal(columns, colBA1),
                    BadReason2 = ColVal(columns, colBR2),
                    BadAction2 = ColVal(columns, colBA2),
                    BadReason3 = ColVal(columns, colBR3),
                    BadAction3 = ColVal(columns, colBA3),
                    BadReason4 = ColVal(columns, colBR4)
                };

                rows.Add(rowData);
            }

            return rows;
        }

        private async Task<List<ImportQuestionRowData>> ReadExcelFileAsync(IFormFile file)
        {
            var rows = new List<ImportQuestionRowData>();

            OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            using var package = new ExcelPackage(stream);

            var worksheet = package.Workbook.Worksheets[0];
            int rowCount = worksheet.Dimension?.Rows ?? 0;
            int colCount = worksheet.Dimension?.Columns ?? 0;

            // Build header map: header name (lowercase) -> column index
            var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int c = 1; c <= colCount; c++)
            {
                var headerText = worksheet.Cells[1, c].Text?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(headerText) && !headerMap.ContainsKey(headerText))
                {
                    headerMap[headerText] = c;
                }
            }

            // Map header names to column indices (supports both "New Question" and "Question")
            int colClover = headerMap.GetValueOrDefault("Clover Element", 0);
            int colQuestion = headerMap.ContainsKey("New Question") ? headerMap["New Question"]
                            : headerMap.GetValueOrDefault("Question", 0);
            int colGR1 = headerMap.GetValueOrDefault("Good Reason 1", 0);
            int colGA1 = headerMap.GetValueOrDefault("GR1 Micro-Action", 0);
            int colGR2 = headerMap.GetValueOrDefault("Good Reason 2", 0);
            int colGA2 = headerMap.GetValueOrDefault("GR2 Micro-Action", 0);
            int colGR3 = headerMap.GetValueOrDefault("Good Reason 3", 0);
            int colGA3 = headerMap.GetValueOrDefault("GR3 Micro-Action", 0);
            int colGR4 = headerMap.GetValueOrDefault("Good Reason 4", 0);
            int colBR1 = headerMap.GetValueOrDefault("Bad Reason 1", 0);
            int colBA1 = headerMap.GetValueOrDefault("BR1 Micro-Action", 0);
            int colBR2 = headerMap.GetValueOrDefault("Bad Reason 2", 0);
            int colBA2 = headerMap.GetValueOrDefault("BR2 Micro-Action", headerMap.GetValueOrDefault("BR2-Micro-Action", 0));
            int colBR3 = headerMap.GetValueOrDefault("Bad Reason 3", 0);
            int colBA3 = headerMap.GetValueOrDefault("BR3 Micro-Action", headerMap.GetValueOrDefault("BR3-Micro-Action", 0));
            int colBR4 = headerMap.GetValueOrDefault("Bad Reason 4", 0);

            string CellText(int row, int col) => col > 0 ? worksheet.Cells[row, col].Text?.Trim() ?? string.Empty : string.Empty;

            for (int row = 2; row <= rowCount; row++)
            {
                var cloverElement = CellText(row, colClover);
                var questionText = CellText(row, colQuestion);

                if (string.IsNullOrWhiteSpace(cloverElement) && string.IsNullOrWhiteSpace(questionText))
                    continue;

                var rowData = new ImportQuestionRowData
                {
                    RowNumber = row,
                    CloverElement = cloverElement,
                    QuestionText = questionText,
                    GoodReason1 = CellText(row, colGR1),
                    GoodAction1 = CellText(row, colGA1),
                    GoodReason2 = CellText(row, colGR2),
                    GoodAction2 = CellText(row, colGA2),
                    GoodReason3 = CellText(row, colGR3),
                    GoodAction3 = CellText(row, colGA3),
                    GoodReason4 = CellText(row, colGR4),
                    BadReason1 = CellText(row, colBR1),
                    BadAction1 = CellText(row, colBA1),
                    BadReason2 = CellText(row, colBR2),
                    BadAction2 = CellText(row, colBA2),
                    BadReason3 = CellText(row, colBR3),
                    BadAction3 = CellText(row, colBA3),
                    BadReason4 = CellText(row, colBR4)
                };

                rows.Add(rowData);
            }

            return rows;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var columns = new List<string>();
            var currentColumn = new StringBuilder();
            bool insideQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    insideQuotes = !insideQuotes;
                }
                else if (c == ',' && !insideQuotes)
                {
                    columns.Add(currentColumn.ToString());
                    currentColumn.Clear();
                }
                else
                {
                    currentColumn.Append(c);
                }
            }

            columns.Add(currentColumn.ToString()); // Add last column
            return columns;
        }

        private static string MapCloverElementToCategory(string element)
        {
            if (string.IsNullOrWhiteSpace(element))
                return string.Empty;

            var normalized = element.Trim().ToUpperInvariant();
            return normalized switch
            {
                "C" => "Communication",
                "L" => "Learning",
                "O" => "Opportunities",
                "V" => "Vulnerability",
                "E" => "Enablement",
                "R" => "Reflection",
                _ => string.Empty
            };
        }

        private static string? CombineValues(params string?[] values)
        {
            var nonEmptyValues = values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim())
                .ToList();

            return nonEmptyValues.Any() ? string.Join(",", nonEmptyValues) : null;
        }

        private static string? CombineValuesPreserveEmpty(params string?[] values)
        {
            // Preserve all values including empty ones to maintain position mapping
            // Empty values will be represented as empty strings in the CSV
            var allValues = values
                .Select(v => string.IsNullOrWhiteSpace(v) ? string.Empty : v!.Trim())
                .ToList();

            // Only return null if all values are empty
            if (allValues.All(v => string.IsNullOrWhiteSpace(v)))
            {
                return null;
            }

            return string.Join(",", allValues);
        }

        private class ImportQuestionRowData
        {
            public int RowNumber { get; set; }
            public string CloverElement { get; set; } = string.Empty;
            public string QuestionText { get; set; } = string.Empty;
            public string GoodReason1 { get; set; } = string.Empty;
            public string GoodReason2 { get; set; } = string.Empty;
            public string GoodReason3 { get; set; } = string.Empty;
            public string GoodReason4 { get; set; } = string.Empty; // NEW: Reason 4 without action
            public string BadReason1 { get; set; } = string.Empty;
            public string BadReason2 { get; set; } = string.Empty;
            public string BadReason3 { get; set; } = string.Empty;
            public string BadReason4 { get; set; } = string.Empty; // NEW: Reason 4 without action
            public string GoodAction1 { get; set; } = string.Empty;
            public string GoodAction2 { get; set; } = string.Empty;
            public string GoodAction3 { get; set; } = string.Empty;
            public string BadAction1 { get; set; } = string.Empty;
            public string BadAction2 { get; set; } = string.Empty;
            public string BadAction3 { get; set; } = string.Empty;
        }

        private static (string Reasons, string Actions) AlignReasonsAndActions(
            string r1, string a1,
            string r2, string a2,
            string r3, string a3)
        {
            var reasons = new List<string>();
            var actions = new List<string>();

            // Tuple list of reason/action pairs
            var pairs = new[]
            {
                (r1, a1),
                (r2, a2),
                (r3, a3)
            };

            foreach (var (r, a) in pairs)
            {
                if (!string.IsNullOrWhiteSpace(r))
                {
                    reasons.Add(SanitizeText(r.Trim()));
                    // Preserve action even if empty, as long as reason exists
                    // Also sanitize action text
                    actions.Add(string.IsNullOrWhiteSpace(a) ? "" : SanitizeText(a.Trim()));
                }
            }

            return (string.Join(",", reasons), string.Join(",", actions));
        }

        /// <summary>
        /// Checks if a reason text is the "Other" option that should be skipped during import.
        /// Matches the exact "Other–I'll expain futher in the text box" sentence.
        /// "Other" reasons are added at the end for ALL questions after import completes.
        /// </summary>
        private static bool IsOtherReason(string reasonText)
        {
            if (string.IsNullOrWhiteSpace(reasonText))
                return false;

            var normalized = SanitizeText(reasonText).ToLowerInvariant();

            // Check for the exact "Other" option sentence (with different dash variants)
            // Exact text from Excel: "Other–I'll expain futher in the text box"
            const string otherSentence1 = "other–i'll expain futher in the text box";
            const string otherSentence2 = "other—i'll expain futher in the text box";
            const string otherSentence3 = "other-i'll expain futher in the text box";

            return normalized == otherSentence1 ||
                   normalized == otherSentence2 ||
                   normalized == otherSentence3;
        }

        /// <summary>
        /// Checks if a comma-separated reasons string already contains an "Other" reason.
        /// </summary>
        private static bool ContainsOtherReason(string reasonsCsv)
        {
            if (string.IsNullOrWhiteSpace(reasonsCsv))
                return false;

            return reasonsCsv.Split(',')
                .Any(r => IsOtherReason(r.Trim()));
        }

        private static string SanitizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            // Replace smart quotes (left \u2018 and right \u2019) with single quote
            return text.Replace("'", "'")
                       .Replace("\u2018", "'")
                       .Replace("\u2019", "'")
                       .Trim();
        }

        public async Task<DeleteByRangeResultDto> DeleteQuestionsByRangeAsync(long fromId, long toId)
        {
            if (fromId <= 0 || toId <= 0)
            {
                return new DeleteByRangeResultDto
                {
                    IsSuccess = false,
                    Message = "FromId and ToId must be greater than 0.",
                    DeletedCount = 0
                };
            }

            if (fromId > toId)
            {
                return new DeleteByRangeResultDto
                {
                    IsSuccess = false,
                    Message = "FromId cannot be greater than ToId.",
                    DeletedCount = 0
                };
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // 1. Delete DailyMicroActions (Lowest level)
                var deletedActions = await _context.Database.ExecuteSqlRawAsync(
                    @"DELETE FROM ""DailyMicroActions""
                      WHERE ""ReasonId"" IN (
                          SELECT r.""Id""
                          FROM ""DailyReasons"" r
                          JOIN ""DailyQuestionSpecifications"" s ON r.""SpecificationId"" = s.""Id""
                          WHERE s.""QuestionId"" BETWEEN {0} AND {1}
                      )", fromId, toId);

                // 2. Delete DailyReasons
                var deletedReasons = await _context.Database.ExecuteSqlRawAsync(
                    @"DELETE FROM ""DailyReasons""
                      WHERE ""SpecificationId"" IN (
                          SELECT ""Id""
                          FROM ""DailyQuestionSpecifications""
                          WHERE ""QuestionId"" BETWEEN {0} AND {1}
                      )", fromId, toId);

                // 3. Delete DailyQuestionSpecifications
                var deletedSpecs = await _context.Database.ExecuteSqlRawAsync(
                    @"DELETE FROM ""DailyQuestionSpecifications""
                      WHERE ""QuestionId"" BETWEEN {0} AND {1}", fromId, toId);

                // 4. Delete DailyQuestions (Root level)
                var deletedQuestions = await _context.Database.ExecuteSqlRawAsync(
                    @"DELETE FROM ""DailyQuestions""
                      WHERE ""Id"" BETWEEN {0} AND {1}", fromId, toId);

                await transaction.CommitAsync();

                return new DeleteByRangeResultDto
                {
                    IsSuccess = true,
                    Message = $"Successfully deleted {deletedQuestions} question(s), {deletedSpecs} specification(s), {deletedReasons} reason(s), and {deletedActions} micro-action(s).",
                    DeletedCount = deletedQuestions
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new DeleteByRangeResultDto
                {
                    IsSuccess = false,
                    Message = $"An error occurred while deleting questions: {ex.Message}",
                    DeletedCount = 0
                };
            }
        }

        public async Task<DeleteByRangeResultDto> DeleteReasonsByRangeAsync(long fromId, long toId)
        {
            if (fromId <= 0 || toId <= 0)
            {
                return new DeleteByRangeResultDto
                {
                    IsSuccess = false,
                    Message = "FromId and ToId must be greater than 0.",
                    DeletedCount = 0
                };
            }

            if (fromId > toId)
            {
                return new DeleteByRangeResultDto
                {
                    IsSuccess = false,
                    Message = "FromId cannot be greater than ToId.",
                    DeletedCount = 0
                };
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // First delete any MicroActions linked to these reasons
                //var deletedActions = await _context.Database.ExecuteSqlRawAsync(
                //    @"DELETE FROM ""DailyMicroActions""
                //      WHERE ""ReasonId"" IN (
                //          SELECT ""Id"" FROM ""DailyReasons""
                //          WHERE ""Id"" BETWEEN {0} AND {1}
                //      )", fromId, toId);

                // Then delete the reasons
                var deletedReasons = await _context.Database.ExecuteSqlRawAsync(
                    @"DELETE FROM ""DailyReasons""
                      WHERE ""Id"" BETWEEN {0} AND {1}", fromId, toId);

                await transaction.CommitAsync();

                return new DeleteByRangeResultDto
                {
                    IsSuccess = true,
                    Message = $"Successfully deleted {deletedReasons} reason(s).",
                    DeletedCount = deletedReasons
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new DeleteByRangeResultDto
                {
                    IsSuccess = false,
                    Message = $"An error occurred while deleting reasons: {ex.Message}",
                    DeletedCount = 0
                };
            }
        }

        public async Task<UpdateQuestionsFromExcelResponseDto> UpdateQuestionsFromExcelAsync(IFormFile file)
        {
            var response = new UpdateQuestionsFromExcelResponseDto();

            try
            {
                if (file == null || file.Length == 0)
                {
                    response.Results.Add(new ExcelUpdateRowResultDto
                    {
                        RowNumber = 0,
                        IsMatched = false,
                        HasChanges = false,
                        Message = "File is empty or not provided."
                    });
                    return response;
                }

                var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (fileExtension != ".xlsx")
                {
                    response.Results.Add(new ExcelUpdateRowResultDto
                    {
                        RowNumber = 0,
                        IsMatched = false,
                        HasChanges = false,
                        Message = "Invalid file format. Only XLSX files are supported for this operation."
                    });
                    return response;
                }

                // Read all rows using header-aware reader (reads by column NAME, not index)
                var (excelRows, readError) = await ReadExcelByHeaderAsync(file);

                if (readError != null)
                {
                    response.Results.Add(new ExcelUpdateRowResultDto
                    {
                        RowNumber = 0,
                        IsMatched = false,
                        HasChanges = false,
                        Message = readError
                    });
                    return response;
                }

                response.TotalRowsInExcel = excelRows.Count;

                if (excelRows.Count == 0)
                {
                    response.Results.Add(new ExcelUpdateRowResultDto
                    {
                        RowNumber = 0,
                        IsMatched = false,
                        HasChanges = false,
                        Message = "No data rows found in the file."
                    });
                    return response;
                }

                // Load all existing questions with their specifications and reasons
                var allSpecs = await _context.DailyQuestionSpecifications
                    .Include(s => s.Question)
                    .Include(s => s.Reasons)
                    .OrderBy(s => s.Id)
                    .ToListAsync();

                // Load all micro-actions for all reasons
                var allReasonIds = allSpecs.SelectMany(s => s.Reasons).Select(r => r.Id).ToList();
                var allActions = await _context.DailyMicroActions
                    .Where(a => allReasonIds.Contains(a.ReasonId))
                    .ToListAsync();

                // Build lookup: ReasonId -> List<DailyMicroAction> (ordered by Id)
                var actionsByReasonId = allActions
                    .GroupBy(a => a.ReasonId)
                    .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Id).ToList());

                // Build lookup: sanitized question text -> specification (case-insensitive)
                var specByQuestionText = new Dictionary<string, DailyQuestionSpecification>(StringComparer.OrdinalIgnoreCase);
                foreach (var spec in allSpecs)
                {
                    var key = SanitizeText(spec.Question.Text);
                    if (!specByQuestionText.ContainsKey(key))
                    {
                        specByQuestionText[key] = spec;
                    }
                }

                await using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    foreach (var row in excelRows)
                    {
                        var excelQuestionText = SanitizeText(row.QuestionText);

                        if (string.IsNullOrWhiteSpace(excelQuestionText))
                        {
                            response.SkippedRows++;
                            response.Results.Add(new ExcelUpdateRowResultDto
                            {
                                RowNumber = row.RowNumber,
                                QuestionText = excelQuestionText,
                                IsMatched = false,
                                HasChanges = false,
                                Message = "Empty question text, skipped."
                            });
                            continue;
                        }

                        // Match Excel row to existing question by sanitized text
                        if (!specByQuestionText.TryGetValue(excelQuestionText, out var matchedSpec))
                        {
                            response.SkippedRows++;
                            response.Results.Add(new ExcelUpdateRowResultDto
                            {
                                RowNumber = row.RowNumber,
                                QuestionText = excelQuestionText,
                                IsMatched = false,
                                HasChanges = false,
                                Message = "No matching question found in database."
                            });
                            continue;
                        }

                        response.MatchedQuestions++;
                        var rowResult = new ExcelUpdateRowResultDto
                        {
                            RowNumber = row.RowNumber,
                            QuestionText = excelQuestionText,
                            IsMatched = true
                        };

                        // ──────────────────────────────────────────────
                        // Build POSITIVE reason/action pairs from Excel
                        // ──────────────────────────────────────────────
                        var excelPositivePairs = new List<(string ReasonText, string ActionText)>();
                        AddReasonActionPair(excelPositivePairs, row.GoodReason1, row.GoodAction1);
                        AddReasonActionPair(excelPositivePairs, row.GoodReason2, row.GoodAction2);
                        AddReasonActionPair(excelPositivePairs, row.GoodReason3, row.GoodAction3);

                        // ──────────────────────────────────────────────
                        // Build NEGATIVE reason/action pairs from Excel
                        // ──────────────────────────────────────────────
                        var excelNegativePairs = new List<(string ReasonText, string ActionText)>();
                        AddReasonActionPair(excelNegativePairs, row.BadReason1, row.BadAction1);
                        AddReasonActionPair(excelNegativePairs, row.BadReason2, row.BadAction2);
                        AddReasonActionPair(excelNegativePairs, row.BadReason3, row.BadAction3);

                        // ──────────────────────────────────────────────
                        // Get existing DB reasons by sentiment (exclude "Other")
                        // ──────────────────────────────────────────────
                        var dbPositiveReasons = matchedSpec.Reasons
                            .Where(r => r.DailyReasonSentimentType == DailyReasonSentimentType.Positive
                                        && !IsOtherReason(r.Text))
                            .OrderBy(r => r.Id)
                            .ToList();

                        var dbNegativeReasons = matchedSpec.Reasons
                            .Where(r => r.DailyReasonSentimentType == DailyReasonSentimentType.Negative
                                        && !IsOtherReason(r.Text))
                            .OrderBy(r => r.Id)
                            .ToList();

                        // ──────────────────────────────────────────────
                        // Compare & update POSITIVE reasons + actions
                        // ──────────────────────────────────────────────
                        CompareAndUpdateReasonActionPairs(
                            dbPositiveReasons,
                            excelPositivePairs,
                            actionsByReasonId,
                            "Positive",
                            response,
                            rowResult);

                        // ──────────────────────────────────────────────
                        // Compare & update NEGATIVE reasons + actions
                        // ──────────────────────────────────────────────
                        CompareAndUpdateReasonActionPairs(
                            dbNegativeReasons,
                            excelNegativePairs,
                            actionsByReasonId,
                            "Negative",
                            response,
                            rowResult);

                        // Finalize row result
                        rowResult.HasChanges = rowResult.ChangeDetails.Count > 0;
                        if (rowResult.HasChanges)
                        {
                            response.UpdatedQuestions++;
                            rowResult.Message = $"Updated: {rowResult.ChangeDetails.Count} change(s) applied.";
                        }
                        else
                        {
                            rowResult.Message = "No changes detected, skipped.";
                        }

                        response.Results.Add(rowResult);
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    response.Results.Add(new ExcelUpdateRowResultDto
                    {
                        RowNumber = 0,
                        IsMatched = false,
                        HasChanges = false,
                        Message = $"Transaction failed: {ex.Message}"
                    });
                }
            }
            catch (Exception ex)
            {
                response.Results.Add(new ExcelUpdateRowResultDto
                {
                    RowNumber = 0,
                    IsMatched = false,
                    HasChanges = false,
                    Message = $"Error reading file: {ex.Message}"
                });
            }

            return response;
        }

        /// <summary>
        /// Reads the Excel file by HEADER NAME (not by hardcoded column index).
        /// This correctly handles any column layout — 14 columns (no Reason 4) or 16 columns (with Reason 4).
        /// Each cell is read individually by EPPlus so commas inside text are preserved.
        /// </summary>
        private async Task<(List<UpdateExcelRowData> Rows, string? Error)> ReadExcelByHeaderAsync(IFormFile file)
        {
            OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            using var package = new ExcelPackage(stream);

            var worksheet = package.Workbook.Worksheets[0];
            if (worksheet.Dimension == null || worksheet.Dimension.Rows < 2)
            {
                return (new List<UpdateExcelRowData>(), "File is empty or has no data rows.");
            }

            int totalCols = worksheet.Dimension.Columns;

            // Build header name → column index mapping from row 1
            var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int col = 1; col <= totalCols; col++)
            {
                var headerText = worksheet.Cells[1, col].Text?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(headerText) && !headerMap.ContainsKey(headerText))
                {
                    headerMap[headerText] = col;
                }
            }

            // Resolve column indices — try common header name variants
            int? questionCol = FindColumn(headerMap, "Question", "New Question");
            if (!questionCol.HasValue)
            {
                return (new List<UpdateExcelRowData>(), "Required column 'Question' or 'New Question' not found in header row.");
            }

            int? gr1Col = FindColumn(headerMap, "Good Reason 1");
            int? gr1ActCol = FindColumn(headerMap, "GR1 Micro-Action", "GR1-Micro-Action");
            int? gr2Col = FindColumn(headerMap, "Good Reason 2");
            int? gr2ActCol = FindColumn(headerMap, "GR2 Micro-Action", "GR2-Micro-Action");
            int? gr3Col = FindColumn(headerMap, "Good Reason 3");
            int? gr3ActCol = FindColumn(headerMap, "GR3 Micro-Action", "GR3-Micro-Action");
            int? br1Col = FindColumn(headerMap, "Bad Reason 1");
            int? br1ActCol = FindColumn(headerMap, "BR1 Micro-Action", "BR1-Micro-Action");
            int? br2Col = FindColumn(headerMap, "Bad Reason 2");
            int? br2ActCol = FindColumn(headerMap, "BR2 Micro-Action", "BR2-Micro-Action");
            int? br3Col = FindColumn(headerMap, "Bad Reason 3");
            int? br3ActCol = FindColumn(headerMap, "BR3 Micro-Action", "BR3-Micro-Action");

            // Read data rows
            var rows = new List<UpdateExcelRowData>();
            int rowCount = worksheet.Dimension.Rows;

            for (int row = 2; row <= rowCount; row++)
            {
                var questionText = ReadCell(worksheet, row, questionCol);

                // Skip empty rows
                if (string.IsNullOrWhiteSpace(questionText))
                    continue;

                rows.Add(new UpdateExcelRowData
                {
                    RowNumber = row,
                    QuestionText = questionText,
                    GoodReason1 = ReadCell(worksheet, row, gr1Col),
                    GoodAction1 = ReadCell(worksheet, row, gr1ActCol),
                    GoodReason2 = ReadCell(worksheet, row, gr2Col),
                    GoodAction2 = ReadCell(worksheet, row, gr2ActCol),
                    GoodReason3 = ReadCell(worksheet, row, gr3Col),
                    GoodAction3 = ReadCell(worksheet, row, gr3ActCol),
                    BadReason1 = ReadCell(worksheet, row, br1Col),
                    BadAction1 = ReadCell(worksheet, row, br1ActCol),
                    BadReason2 = ReadCell(worksheet, row, br2Col),
                    BadAction2 = ReadCell(worksheet, row, br2ActCol),
                    BadReason3 = ReadCell(worksheet, row, br3Col),
                    BadAction3 = ReadCell(worksheet, row, br3ActCol)
                });
            }

            return (rows, null);
        }

        private static int? FindColumn(Dictionary<string, int> headerMap, params string[] names)
        {
            foreach (var name in names)
            {
                if (headerMap.TryGetValue(name, out var col))
                    return col;
            }
            return null;
        }

        private static string ReadCell(ExcelWorksheet worksheet, int row, int? col)
        {
            if (!col.HasValue) return string.Empty;
            return worksheet.Cells[row, col.Value].Text?.Trim() ?? string.Empty;
        }

        /// <summary>
        /// Row data read by header-aware reader.
        /// 3 Good Reasons + 3 Good Actions, 3 Bad Reasons + 3 Bad Actions.
        /// </summary>
        private class UpdateExcelRowData
        {
            public int RowNumber { get; set; }
            public string QuestionText { get; set; } = string.Empty;
            public string GoodReason1 { get; set; } = string.Empty;
            public string GoodAction1 { get; set; } = string.Empty;
            public string GoodReason2 { get; set; } = string.Empty;
            public string GoodAction2 { get; set; } = string.Empty;
            public string GoodReason3 { get; set; } = string.Empty;
            public string GoodAction3 { get; set; } = string.Empty;
            public string BadReason1 { get; set; } = string.Empty;
            public string BadAction1 { get; set; } = string.Empty;
            public string BadReason2 { get; set; } = string.Empty;
            public string BadAction2 { get; set; } = string.Empty;
            public string BadReason3 { get; set; } = string.Empty;
            public string BadAction3 { get; set; } = string.Empty;
        }

        private static void AddReasonActionPair(
            List<(string ReasonText, string ActionText)> pairs,
            string reasonText,
            string actionText)
        {
            var sanitizedReason = SanitizeText(reasonText);
            if (!string.IsNullOrWhiteSpace(sanitizedReason) && !IsOtherReason(sanitizedReason))
            {
                pairs.Add((sanitizedReason, SanitizeText(actionText)));
            }
        }

        /// <summary>
        /// Compares DB reasons+actions against Excel pairs for a single sentiment (Positive or Negative).
        /// Updates DB records where text differs; skips where text matches.
        /// Tracks changes separately for Positive vs Negative in the response counters.
        /// </summary>
        private void CompareAndUpdateReasonActionPairs(
            List<DailyReason> dbReasons,
            List<(string ReasonText, string ActionText)> excelPairs,
            Dictionary<uint, List<DailyMicroAction>> actionsByReasonId,
            string sentimentLabel,
            UpdateQuestionsFromExcelResponseDto response,
            ExcelUpdateRowResultDto rowResult)
        {
            bool isPositive = sentimentLabel == "Positive";
            int count = Math.Min(dbReasons.Count, excelPairs.Count);

            for (int i = 0; i < count; i++)
            {
                var dbReason = dbReasons[i];
                var excelReasonText = excelPairs[i].ReasonText;
                var excelActionText = excelPairs[i].ActionText;

                // ── Compare DailyReason text ──
                if (!string.Equals(dbReason.Text, excelReasonText, StringComparison.Ordinal))
                {
                    var detail = $"{sentimentLabel} Reason #{i + 1} (ID {dbReason.Id}): \"{dbReason.Text}\" → \"{excelReasonText}\"";
                    dbReason.Text = excelReasonText;
                    _context.DailyReasons.Update(dbReason);

                    if (isPositive)
                        response.UpdatedPositiveReasons++;
                    else
                        response.UpdatedNegativeReasons++;

                    rowResult.ChangeDetails.Add(detail);
                }

                // ── Compare DailyMicroAction text ──
                if (actionsByReasonId.TryGetValue(dbReason.Id, out var dbActions) && dbActions.Count > 0)
                {
                    var dbAction = dbActions[0]; // First (primary) action for this reason

                    if (!string.IsNullOrWhiteSpace(excelActionText)
                        && !string.Equals(dbAction.Text, excelActionText, StringComparison.Ordinal))
                    {
                        var detail = $"{sentimentLabel} Action for Reason #{i + 1} (ID {dbAction.Id}): \"{dbAction.Text}\" → \"{excelActionText}\"";
                        dbAction.Text = excelActionText;
                        _context.DailyMicroActions.Update(dbAction);

                        if (isPositive)
                            response.UpdatedPositiveActions++;
                        else
                            response.UpdatedNegativeActions++;

                        rowResult.ChangeDetails.Add(detail);
                    }
                }
            }
        }
        #region Daily question selection (ported from the Clover ERA mobile API)

        /// <summary>
        /// Returns the question the mobile app serves today for the given business unit.
        /// The ordinal is derived from the number of whole UTC days elapsed since
        /// <see cref="DataConstants.RotationStartDateUtc" />, wrapped over the rotation window
        /// that starts at <see cref="DataConstants.StartOrdinal" />.
        /// </summary>
        public async Task<DailyQuestionFetchResponseDto?> GetDailyQuestionAsync(uint businessUnitId)
        {
            var businessUnit = await _context.BusinessUnits
                .AsNoTracking()
                .Include(b => b.Profile)
                .FirstOrDefaultAsync(b => b.Id == businessUnitId);

            if (businessUnit == null)
            {
                return null;
            }

            var contentCulture = await GetDefaultContentCultureAsync();

            var ordinal = await GetDailyQuestionOrdinalAsync(
                businessUnit.ActiveDailyQuestionSetGroupId,
                contentCulture.Id);

            if (!ordinal.HasValue)
            {
                return null;
            }

            var specification = await _context.DailyQuestionSpecifications
                .AsNoTracking()
                .Include(s => s.Question)
                .Include(s => s.Reasons)
                .FirstOrDefaultAsync(s =>
                    s.DailyQuestionSetGroupId == businessUnit.ActiveDailyQuestionSetGroupId
                    && s.Ordinal == ordinal.Value
                    && s.ContentCultureId == contentCulture.Id);

            return specification == null
                ? null
                : MapSpecification(businessUnit, specification);
        }

        /// <summary>
        /// Resolves the team a calendar invite is built for: the unit, or the company's first
        /// team when only a company is supplied. Null when neither is supplied or nothing matches.
        /// </summary>
        public async Task<DailyQuestionCalendarTeamDto?> GetDailyQuestionCalendarTeamAsync(
            uint? profileId,
            uint? unitId)
        {
            // Without a filter the query below would hand back some other company's team.
            if (!unitId.HasValue && !profileId.HasValue)
            {
                return null;
            }

            var businessUnitQuery = _context.BusinessUnits
                .AsNoTracking()
                .Where(b => b.IsDel != true);

            if (unitId.HasValue)
            {
                businessUnitQuery = businessUnitQuery.Where(b => b.Id == unitId.Value);
            }

            if (profileId.HasValue)
            {
                businessUnitQuery = businessUnitQuery.Where(b => b.ProfileId == profileId.Value);
            }

            return await businessUnitQuery
                .OrderBy(b => b.Id)
                .Select(b => new DailyQuestionCalendarTeamDto
                {
                    UnitId = b.Id,
                    TeamCode = b.BusinessUnitShortCode
                })
                .FirstOrDefaultAsync();
        }


        /// <summary>
        /// Derives the ordinal of the question served today for a question set group, using the
        /// mobile app's rotation: the window runs from <see cref="DataConstants.StartOrdinal" />
        /// to the highest ordinal held for the group / culture, and the day count since
        /// <see cref="DataConstants.RotationStartDateUtc" /> is wrapped over that window.
        /// Returns null when nothing is scheduled for the group.
        /// </summary>
        private async Task<ushort?> GetDailyQuestionOrdinalAsync(uint questionSetGroupId, uint contentCultureId)
        {
            var maxOrdinal = await _context.DailyQuestionSpecifications
                .AsNoTracking()
                .Where(s =>
                    s.DailyQuestionSetGroupId == questionSetGroupId
                    && s.ContentCultureId == contentCultureId
                    && s.Ordinal >= DataConstants.StartOrdinal)
                .MaxAsync(s => (int?)s.Ordinal);

            if (!maxOrdinal.HasValue)
            {
                return null;
            }

            var rangeSize = maxOrdinal.Value - DataConstants.StartOrdinal + 1;

            if (rangeSize <= 0)
            {
                return null;
            }

            var dayIndex = (ResolveQuestionDate() - DataConstants.RotationStartDateUtc.Date).Days;

            return (ushort)(DataConstants.StartOrdinal + (dayIndex % rangeSize));
        }

        /// <summary>
        /// The date the rotation is evaluated against. The mobile API counts days in UTC, which
        /// serves the previous day's question whenever the calendar day has already turned over
        /// locally but not yet in UTC. The date is therefore taken from the time zone named by
        /// the <c>DailyQuestion:TimeZoneId</c> setting, falling back to the server's local date.
        /// Set that key to <c>UTC</c> to count days exactly as the mobile API does.
        /// </summary>
        private DateTime ResolveQuestionDate()
        {
            var timeZoneId = _configuration?[QuestionTimeZoneConfigKey];

            if (!string.IsNullOrWhiteSpace(timeZoneId))
            {
                try
                {
                    var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                    return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone).Date;
                }
                catch (TimeZoneNotFoundException)
                {
                    // Fall through to the server's local date.
                }
                catch (InvalidTimeZoneException)
                {
                    // Fall through to the server's local date.
                }
            }

            return DateTime.Now.Date;
        }

        /// <summary>
        /// Projects a specification onto the response contract. Reasons are ordered with the
        /// free-text ("Other") reasons last, as the mobile app expects.
        /// </summary>
        private static DailyQuestionFetchResponseDto MapSpecification(
            BusinessUnit businessUnit,
            DailyQuestionSpecification specification)
        {
            return new DailyQuestionFetchResponseDto
            {
                CompanyName = businessUnit.Profile.ProfileName,
                BusinessUnitName = businessUnit.Name,
                QuestionId = specification.Question.Id,
                QuestionText = specification.Question.Text,
                CloverCategory = specification.CloverCategory,
                Reasons = specification.Reasons
                    .Where(r => !r.RequiresFreeTextFeedback)
                    .Concat(specification.Reasons.Where(r => r.RequiresFreeTextFeedback))
                    .Select(r => new DailyReasonResponseDto
                    {
                        ReasonId = r.Id,
                        ReasonText = r.Text,
                        RequiresFreeTextFeedback = r.RequiresFreeTextFeedback,
                        Sentiment = r.DailyReasonSentimentType
                    })
                    .ToArray()
            };
        }

        #endregion

    }
}
