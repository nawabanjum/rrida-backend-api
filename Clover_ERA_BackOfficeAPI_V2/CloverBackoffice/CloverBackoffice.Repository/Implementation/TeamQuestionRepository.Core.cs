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
    /// Team Daily Questions bank — reads, single add, and update.
    ///
    /// Mirrors <see cref="QuestionRepository" /> over the Team tables, with one structural
    /// simplification: the Team tables use Postgres identity columns, so none of the legacy
    /// "LOCK TABLE … then MAX(Id) + 1" manual key allocation is repeated here. EF assigns
    /// the keys on SaveChanges.
    ///
    /// The import and workbook-update halves live in TeamQuestionRepository.Import.cs.
    /// </summary>
    public partial class TeamQuestionRepository : ITeamQuestionRepository
    {
        /// <summary>
        /// The free-text option appended to every question so a respondent always has a way
        /// out. Legacy carries this sentence with two spelling mistakes ("expain futher"),
        /// which the mobile client matches on; the team chain is new, so it is spelled
        /// correctly here and matched by <see cref="IsFreeTextReason" /> rather than by an
        /// exact string compare.
        /// </summary>
        private const string FreeTextReasonText = "Other - I'll explain further in the text box";

        private readonly ApplicationDbContext _context;

        public TeamQuestionRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        // ── Listing ─────────────────────────────────────────────────────────

        public async Task<List<TeamQuestionListItemDto>> GetTeamQuestionListAsync(long? categoryId = null)
        {
            var query = _context.TeamDailyQuestionSpecifications
                .AsNoTracking()
                .Include(s => s.Question)
                .Include(s => s.Category)
                .Include(s => s.Reasons)
                .AsQueryable();

            // Filtering on 0 is meaningful, not "no filter" — it asks for the general
            // questions specifically, so the check is HasValue rather than a truthiness test.
            if (categoryId.HasValue)
            {
                var wanted = (uint)categoryId.Value;
                query = query.Where(s => s.CategoryId == wanted);
            }

            var specifications = await query
                .OrderBy(s => s.CategoryId)
                .ThenBy(s => s.SubCategory)
                .ThenBy(s => s.Ordinal)
                .ThenBy(s => s.Variant)
                .ToListAsync();

            var reasonIds = specifications.SelectMany(s => s.Reasons).Select(r => r.Id).ToList();

            var actionCountByReasonId = await _context.TeamDailyMicroActions
                .AsNoTracking()
                .Where(a => reasonIds.Contains(a.TeamDailyReasonId))
                .GroupBy(a => a.TeamDailyReasonId)
                .Select(g => new { ReasonId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ReasonId, x => x.Count);

            return specifications.Select(s => new TeamQuestionListItemDto
            {
                QuestionId = s.TeamDailyQuestionId,
                QuestionText = s.Question?.Text ?? string.Empty,
                SpecificationId = s.Id,
                CategoryId = s.CategoryId,
                CategoryName = s.Category?.Name ?? string.Empty,
                TeamElement = TeamQuestionEnumMapper.ToLabel(s.TeamElement),
                SubCategory = TeamQuestionEnumMapper.ToLabel(s.SubCategory),
                Variant = TeamQuestionEnumMapper.ToLabel(s.Variant),
                Ordinal = s.Ordinal,
                PositiveReasonCount = s.Reasons.Count(r => r.TeamDailyReasonSentimentType == TeamDailyReasonSentimentType.Positive),
                NegativeReasonCount = s.Reasons.Count(r => r.TeamDailyReasonSentimentType == TeamDailyReasonSentimentType.Negative),
                MicroActionCount = s.Reasons.Sum(r => actionCountByReasonId.GetValueOrDefault(r.Id, 0))
            }).ToList();
        }

        public async Task<TeamQuestionInfoResponseDto?> GetTeamQuestionInfoByIdAsync(long questionId)
        {
            var question = await _context.TeamDailyQuestions
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == questionId);

            if (question == null)
            {
                return null;
            }

            var specification = await _context.TeamDailyQuestionSpecifications
                .AsNoTracking()
                .Include(s => s.Category)
                .FirstOrDefaultAsync(s => s.TeamDailyQuestionId == questionId);

            if (specification == null)
            {
                return null;
            }

            var reasons = await _context.TeamDailyReasons
                .AsNoTracking()
                .Where(r => r.SpecificationId == specification.Id)
                .OrderBy(r => r.Id)
                .ToListAsync();

            var reasonIds = reasons.Select(r => r.Id).ToList();

            var actions = await _context.TeamDailyMicroActions
                .AsNoTracking()
                .Where(a => reasonIds.Contains(a.TeamDailyReasonId))
                .OrderBy(a => a.TeamDailyReasonId)
                .ThenBy(a => a.Id)
                .ToListAsync();

            return new TeamQuestionInfoResponseDto
            {
                QuestionId = question.Id,
                QuestionText = question.Text,
                Specification = new TeamQuestionSpecificationInfoDto
                {
                    SpecificationId = specification.Id,
                    CategoryId = specification.CategoryId,
                    CategoryName = specification.Category?.Name ?? string.Empty,
                    TeamElement = TeamQuestionEnumMapper.ToLabel(specification.TeamElement),
                    SubCategory = TeamQuestionEnumMapper.ToLabel(specification.SubCategory),
                    Variant = TeamQuestionEnumMapper.ToLabel(specification.Variant),
                    Ordinal = specification.Ordinal,
                    Reasons = reasons.Select(reason => new TeamReasonInfoDto
                    {
                        ReasonId = reason.Id,
                        Text = reason.Text,
                        SentimentType = reason.TeamDailyReasonSentimentType.ToString(),
                        TeamState = TeamQuestionEnumMapper.ToLabel(reason.TeamState),
                        RequiresFreeTextFeedback = reason.RequiresFreeTextFeedback,
                        Actions = actions
                            .Where(a => a.TeamDailyReasonId == reason.Id)
                            .Select(a => new TeamActionInfoDto { ActionId = a.Id, Text = a.Text })
                            .ToList()
                    }).ToList()
                }
            };
        }

        // ── Single add ──────────────────────────────────────────────────────

        public async Task<TeamQuestionResponseDto?> AddTeamQuestionAsync(AddTeamQuestionDto dto)
        {
            // The single-add endpoint keeps the duplicate check against the database; import
            // does not, which is the same split the legacy chain makes.
            var normalizedText = SanitizeText(dto.QuestionText).ToLowerInvariant();

            var alreadyExists = await _context.TeamDailyQuestions
                .AnyAsync(q => q.Text.ToLower() == normalizedText);

            if (alreadyExists)
            {
                return null;
            }

            return await AddTeamQuestionInternalAsync(dto);
        }

        /// <summary>
        /// Writes one question, its specification, its reasons and their micro-actions in a
        /// single transaction. Throws <see cref="InvalidOperationException" /> when the
        /// requested rotation slot is already occupied — the caller turns that into a row-level
        /// import failure rather than losing the whole file.
        /// </summary>
        private async Task<TeamQuestionResponseDto> AddTeamQuestionInternalAsync(AddTeamQuestionDto dto)
        {
            var teamElement = TeamQuestionEnumMapper.ParseTeamElement(dto.TeamElement)
                ?? throw new ArgumentException($"Unsupported Team Element '{dto.TeamElement}'.");

            var subCategory = TeamQuestionEnumMapper.ParseSubCategory(dto.SubCategory)
                ?? throw new ArgumentException($"Unsupported Sub-category '{dto.SubCategory}'.");

            // The workbook only spells the variant out for Goal Affinity; everything else is
            // generic, so an absent variant is not an error.
            var variant = string.IsNullOrWhiteSpace(dto.Variant)
                ? TeamQuestionVariant.Generic
                : TeamQuestionEnumMapper.ParseVariant(dto.Variant)
                  ?? throw new ArgumentException($"Unsupported Variant '{dto.Variant}'.");

            var positiveReasons = NormalizeReasonInputs(dto.PositiveReasons);
            var negativeReasons = NormalizeReasonInputs(dto.NegativeReasons);

            if (positiveReasons.Count == 0 && negativeReasons.Count == 0)
            {
                throw new InvalidOperationException("At least one reason must be provided.");
            }

            var categoryId = await ResolveCategoryIdAsync(dto.CategoryId);
            var contentCulture = await GetDefaultContentCultureAsync();

            // Each category runs its own rotation, so the next free slot is looked up within
            // the category — Ask #1 exists once per category, not once overall.
            var ordinal = dto.Ordinal.HasValue && dto.Ordinal.Value > 0
                ? (ushort)dto.Ordinal.Value
                : await GetNextOrdinalAsync(categoryId, subCategory, variant, contentCulture.Id);

            // Checked before the insert so the caller gets a sentence rather than a Postgres
            // unique-violation. The index still backs it up under concurrency.
            var slotTaken = await _context.TeamDailyQuestionSpecifications
                .AnyAsync(s => s.CategoryId == categoryId
                               && s.SubCategory == subCategory
                               && s.Variant == variant
                               && s.Ordinal == ordinal
                               && s.ContentCultureId == contentCulture.Id);

            if (slotTaken)
            {
                throw new InvalidOperationException(
                    $"Ask #{ordinal} for {TeamQuestionEnumMapper.ToLabel(subCategory)} " +
                    $"({TeamQuestionEnumMapper.ToLabel(variant)}) already exists in category {categoryId}.");
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var question = new TeamDailyQuestion
                {
                    Text = SanitizeText(dto.QuestionText),
                    ContentCultureId = contentCulture.Id
                };

                _context.TeamDailyQuestions.Add(question);
                await _context.SaveChangesAsync();

                var specification = new TeamDailyQuestionSpecification
                {
                    CategoryId = categoryId,
                    TeamElement = teamElement,
                    SubCategory = subCategory,
                    Variant = variant,
                    Ordinal = ordinal,
                    ContentCultureId = contentCulture.Id,
                    TeamDailyQuestionId = question.Id
                };

                _context.TeamDailyQuestionSpecifications.Add(specification);
                await _context.SaveChangesAsync();

                var positive = await PersistReasonsAsync(
                    positiveReasons, TeamDailyReasonSentimentType.Positive, specification.Id, contentCulture.Id);

                var negative = await PersistReasonsAsync(
                    negativeReasons, TeamDailyReasonSentimentType.Negative, specification.Id, contentCulture.Id);

                await transaction.CommitAsync();

                return new TeamQuestionResponseDto
                {
                    TeamDailyQuestionId = question.Id,
                    QuestionText = question.Text,
                    SpecificationId = specification.Id,
                    CategoryId = categoryId,
                    CategoryName = await GetCategoryNameAsync(categoryId),
                    TeamElement = TeamQuestionEnumMapper.ToLabel(teamElement),
                    SubCategory = TeamQuestionEnumMapper.ToLabel(subCategory),
                    Variant = TeamQuestionEnumMapper.ToLabel(variant),
                    Ordinal = ordinal,
                    PositiveReasons = positive,
                    NegativeReasons = negative
                };
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Drops blank rows, sanitises the text, and appends the free-text option when the
        /// caller did not supply one — the same guarantee the legacy import gives.
        /// </summary>
        private static List<TeamReasonInputDto> NormalizeReasonInputs(IEnumerable<TeamReasonInputDto>? inputs)
        {
            var reasons = (inputs ?? Enumerable.Empty<TeamReasonInputDto>())
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Text))
                .Select(r => new TeamReasonInputDto
                {
                    Text = SanitizeText(r.Text),
                    TeamState = r.TeamState,
                    MicroAction = string.IsNullOrWhiteSpace(r.MicroAction) ? null : SanitizeText(r.MicroAction!),
                    RequiresFreeTextFeedback = r.RequiresFreeTextFeedback
                })
                .ToList();

            if (reasons.Count == 0)
            {
                return reasons;
            }

            if (!reasons.Any(r => IsFreeTextReason(r.Text)))
            {
                reasons.Add(new TeamReasonInputDto
                {
                    Text = FreeTextReasonText,
                    RequiresFreeTextFeedback = true
                });
            }

            return reasons;
        }

        private async Task<List<TeamReasonResponseDto>> PersistReasonsAsync(
            List<TeamReasonInputDto> inputs,
            TeamDailyReasonSentimentType sentiment,
            uint specificationId,
            uint contentCultureId)
        {
            if (inputs.Count == 0)
            {
                return new List<TeamReasonResponseDto>();
            }

            var reasons = inputs.Select(input => new TeamDailyReason
            {
                TeamDailyReasonSentimentType = sentiment,
                Text = input.Text,
                RequiresFreeTextFeedback = input.RequiresFreeTextFeedback ?? IsFreeTextReason(input.Text),
                TeamState = TeamQuestionEnumMapper.ParseTeamState(input.TeamState),
                SpecificationId = specificationId,
                ContentCultureId = contentCultureId
            }).ToList();

            _context.TeamDailyReasons.AddRange(reasons);
            await _context.SaveChangesAsync();

            // Reasons and inputs stay index-aligned because AddRange preserves order, so the
            // micro-action written here is the one that came in beside the reason text.
            var actions = new List<TeamDailyMicroAction>();

            for (var i = 0; i < reasons.Count; i++)
            {
                var microAction = inputs[i].MicroAction;

                if (string.IsNullOrWhiteSpace(microAction))
                {
                    continue;
                }

                actions.Add(new TeamDailyMicroAction
                {
                    Text = microAction!,
                    ContentCultureId = contentCultureId,
                    TeamDailyReasonId = reasons[i].Id
                });
            }

            if (actions.Count > 0)
            {
                _context.TeamDailyMicroActions.AddRange(actions);
                await _context.SaveChangesAsync();
            }

            var actionByReasonId = actions.ToDictionary(a => a.TeamDailyReasonId);

            return reasons.Select(r => new TeamReasonResponseDto
            {
                ReasonId = r.Id,
                Text = r.Text,
                TeamState = TeamQuestionEnumMapper.ToLabel(r.TeamState),
                RequiresFreeTextFeedback = r.RequiresFreeTextFeedback,
                MicroActionId = actionByReasonId.TryGetValue(r.Id, out var action) ? action.Id : null,
                MicroAction = actionByReasonId.TryGetValue(r.Id, out var a) ? a.Text : null
            }).ToList();
        }

        // ── Shared helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Turns the caller's optional category into the id to store. Omitted, null or 0 all
        /// mean the seeded "General" category, which is how "this question is for everyone"
        /// is expressed. Anything else must be a live category — a question filed against a
        /// deleted or unknown category would silently never be served.
        /// </summary>
        private async Task<uint> ResolveCategoryIdAsync(long? categoryId)
        {
            if (!categoryId.HasValue || categoryId.Value == TeamQuestionCategory.DefaultCategoryId)
            {
                return TeamQuestionCategory.DefaultCategoryId;
            }

            if (categoryId.Value < 0)
            {
                throw new ArgumentException($"Invalid CategoryId {categoryId.Value}.");
            }

            var id = (uint)categoryId.Value;

            var exists = await _context.TeamQuestionCategories
                .AsNoTracking()
                .AnyAsync(c => c.Id == id && !c.IsDel);

            if (!exists)
            {
                throw new ArgumentException($"Category {categoryId.Value} does not exist.");
            }

            return id;
        }

        private async Task<string> GetCategoryNameAsync(uint categoryId)
        {
            return await _context.TeamQuestionCategories
                .AsNoTracking()
                .Where(c => c.Id == categoryId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync() ?? string.Empty;
        }

        private async Task<ContentCulture> GetDefaultContentCultureAsync()
        {
            var culture = await _context.ContentCultures
                .FirstOrDefaultAsync(c => c.CultureName == DataConstants.DefaultContentCultureName)
                ?? await _context.ContentCultures.FirstOrDefaultAsync();

            return culture ?? throw new InvalidOperationException("No ContentCulture entries exist.");
        }

        private async Task<ushort> GetNextOrdinalAsync(
            uint categoryId,
            TeamSubCategory subCategory,
            TeamQuestionVariant variant,
            uint contentCultureId)
        {
            var maxOrdinal = await _context.TeamDailyQuestionSpecifications
                .Where(s => s.CategoryId == categoryId
                            && s.SubCategory == subCategory
                            && s.Variant == variant
                            && s.ContentCultureId == contentCultureId)
                .MaxAsync(s => (int?)s.Ordinal) ?? 0;

            return (ushort)(maxOrdinal + 1);
        }

        /// <summary>
        /// Recognises the free-text option however it is punctuated. Matching on the opening
        /// "other" plus "explain" survives the hyphen / en-dash / em-dash variants that the
        /// requirement workbooks mix, and the legacy misspelling if a file carries it over.
        /// </summary>
        private static bool IsFreeTextReason(string? reasonText)
        {
            if (string.IsNullOrWhiteSpace(reasonText))
            {
                return false;
            }

            var normalized = reasonText.Trim().ToLowerInvariant();

            return normalized.StartsWith("other", StringComparison.Ordinal)
                   && (normalized.Contains("explain") || normalized.Contains("expain"));
        }

        private static string SanitizeText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            // Smart quotes come out of Excel and break exact-text matching on re-import.
            return text
                .Replace("‘", "'")
                .Replace("’", "'")
                .Trim();
        }
    }
}
