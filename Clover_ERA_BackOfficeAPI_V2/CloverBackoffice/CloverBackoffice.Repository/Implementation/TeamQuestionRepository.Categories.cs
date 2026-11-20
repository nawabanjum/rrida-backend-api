using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models.TeamQuestions;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    /// <summary>
    /// Team question categories — the optional grouping a question can be authored for.
    ///
    /// Category 0 ("General") is seeded by the AddTeamQuestionCategories migration and is
    /// treated as immutable throughout: it is what every uncategorised question points at,
    /// so renaming, deactivating or deleting it would change the meaning of questions
    /// nobody edited.
    /// </summary>
    public partial class TeamQuestionRepository
    {
        public async Task<List<TeamQuestionCategoryDto>> GetTeamQuestionCategoriesAsync(bool includeInactive = true)
        {
            var query = _context.TeamQuestionCategories
                .AsNoTracking()
                .Where(c => !c.IsDel);

            if (!includeInactive)
            {
                query = query.Where(c => c.IsActive);
            }

            var categories = await query.OrderBy(c => c.Id).ToListAsync();

            var questionCounts = await _context.TeamDailyQuestionSpecifications
                .AsNoTracking()
                .GroupBy(s => s.CategoryId)
                .Select(g => new { CategoryId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.CategoryId, x => x.Count);

            return categories.Select(c => Map(c, questionCounts.GetValueOrDefault(c.Id, 0))).ToList();
        }

        public async Task<TeamQuestionCategoryDto?> GetTeamQuestionCategoryByIdAsync(long categoryId)
        {
            if (categoryId < 0)
            {
                return null;
            }

            var id = (uint)categoryId;

            var category = await _context.TeamQuestionCategories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsDel);

            if (category == null)
            {
                return null;
            }

            var questionCount = await _context.TeamDailyQuestionSpecifications
                .AsNoTracking()
                .CountAsync(s => s.CategoryId == id);

            return Map(category, questionCount);
        }

        public async Task<TeamQuestionCategoryResultDto> AddTeamQuestionCategoryAsync(
            CreateTeamQuestionCategoryDto dto,
            int? createdBy)
        {
            var name = (dto.Name ?? string.Empty).Trim();

            var invalid = ValidateName(name);
            if (invalid != null)
            {
                return CategoryFailed(invalid);
            }

            var normalized = name.ToLowerInvariant();

            var duplicate = await _context.TeamQuestionCategories
                .AnyAsync(c => !c.IsDel && c.Name.ToLower() == normalized);

            if (duplicate)
            {
                return CategoryFailed($"A category named '{name}' already exists.");
            }

            var now = DateTime.UtcNow;

            var category = new TeamQuestionCategory
            {
                Name = name,
                Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
                IsActive = dto.IsActive ?? true,
                CreatedBy = createdBy,
                UpdatedBy = createdBy,
                CreatedAt = now,
                UpdatedAt = now,
                IsDel = false
            };

            _context.TeamQuestionCategories.Add(category);
            await _context.SaveChangesAsync();

            return new TeamQuestionCategoryResultDto
            {
                IsSuccess = true,
                Category = Map(category, 0)
            };
        }

        public async Task<TeamQuestionCategoryResultDto> UpdateTeamQuestionCategoryAsync(
            UpdateTeamQuestionCategoryDto dto,
            int? updatedBy)
        {
            if (dto.CategoryId < 0)
            {
                return CategoryFailed($"Invalid CategoryId {dto.CategoryId}.");
            }

            if (dto.CategoryId == TeamQuestionCategory.DefaultCategoryId)
            {
                return CategoryFailed("The default 'General' category cannot be edited.");
            }

            var id = (uint)dto.CategoryId;

            var category = await _context.TeamQuestionCategories
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsDel);

            if (category == null)
            {
                return CategoryFailed($"Category {dto.CategoryId} not found.");
            }

            var name = (dto.Name ?? string.Empty).Trim();

            var invalid = ValidateName(name);
            if (invalid != null)
            {
                return CategoryFailed(invalid);
            }

            var normalized = name.ToLowerInvariant();

            var duplicate = await _context.TeamQuestionCategories
                .AnyAsync(c => c.Id != id && !c.IsDel && c.Name.ToLower() == normalized);

            if (duplicate)
            {
                return CategoryFailed($"A category named '{name}' already exists.");
            }

            category.Name = name;
            category.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
            category.IsActive = dto.IsActive ?? category.IsActive;
            category.UpdatedBy = updatedBy;
            category.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var questionCount = await _context.TeamDailyQuestionSpecifications
                .CountAsync(s => s.CategoryId == id);

            return new TeamQuestionCategoryResultDto
            {
                IsSuccess = true,
                Category = Map(category, questionCount)
            };
        }

        /// <summary>
        /// Marks a category deleted. A category still holding questions is refused rather
        /// than reassigned: moving those questions to "General" would quietly change who
        /// they are served to, which is the caller's decision, not this method's.
        /// </summary>
        public async Task<TeamQuestionCategoryResultDto> DeleteTeamQuestionCategoryAsync(long categoryId, int? updatedBy)
        {
            if (categoryId < 0)
            {
                return CategoryFailed($"Invalid CategoryId {categoryId}.");
            }

            if (categoryId == TeamQuestionCategory.DefaultCategoryId)
            {
                return CategoryFailed("The default 'General' category cannot be deleted.");
            }

            var id = (uint)categoryId;

            var category = await _context.TeamQuestionCategories
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsDel);

            if (category == null)
            {
                return CategoryFailed($"Category {categoryId} not found.");
            }

            var questionCount = await _context.TeamDailyQuestionSpecifications
                .CountAsync(s => s.CategoryId == id);

            if (questionCount > 0)
            {
                return CategoryFailed(
                    $"Category '{category.Name}' still holds {questionCount} question(s). " +
                    "Move or remove them before deleting it.");
            }

            category.IsDel = true;
            category.IsActive = false;
            category.UpdatedBy = updatedBy;
            category.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return new TeamQuestionCategoryResultDto
            {
                IsSuccess = true,
                Category = Map(category, 0)
            };
        }

        private static string? ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Category name is required.";
            }

            return name.Length > 200 ? "Category name cannot be longer than 200 characters." : null;
        }

        private static TeamQuestionCategoryDto Map(TeamQuestionCategory category, int questionCount) =>
            new TeamQuestionCategoryDto
            {
                CategoryId = category.Id,
                Name = category.Name,
                Description = category.Description,
                IsActive = category.IsActive,
                IsDefault = category.Id == TeamQuestionCategory.DefaultCategoryId,
                QuestionCount = questionCount,
                CreatedAt = category.CreatedAt,
                UpdatedAt = category.UpdatedAt
            };

        private static TeamQuestionCategoryResultDto CategoryFailed(string message) =>
            new TeamQuestionCategoryResultDto { IsSuccess = false, ErrorMessage = message };
    }
}
