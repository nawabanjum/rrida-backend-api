using CloverBackoffice.Infrastructure.BusinessModel;
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
    /// Team Daily Questions — edit one question, and delete by Id range.
    /// </summary>
    public partial class TeamQuestionRepository
    {
        public async Task<UpdateTeamQuestionInfoResultDto> UpdateTeamQuestionInfoByIdAsync(UpdateTeamQuestionInfoDto dto)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var question = await _context.TeamDailyQuestions
                    .FirstOrDefaultAsync(q => q.Id == dto.QuestionId);

                if (question == null)
                {
                    await transaction.RollbackAsync();
                    return Failed($"Question with ID {dto.QuestionId} not found.");
                }

                var specification = await _context.TeamDailyQuestionSpecifications
                    .Include(s => s.Reasons)
                    .FirstOrDefaultAsync(s => s.Id == dto.Specification.SpecificationId);

                if (specification == null)
                {
                    await transaction.RollbackAsync();
                    return Failed($"Specification with ID {dto.Specification.SpecificationId} not found.");
                }

                if (specification.TeamDailyQuestionId != question.Id)
                {
                    await transaction.RollbackAsync();
                    return Failed(
                        $"Specification {specification.Id} does not belong to question {question.Id}.");
                }

                var teamElement = TeamQuestionEnumMapper.ParseTeamElement(dto.Specification.TeamElement);
                if (teamElement == null)
                {
                    await transaction.RollbackAsync();
                    return Failed(
                        $"Invalid Team Element '{dto.Specification.TeamElement}'. Valid values are: " +
                        "Direction and learning, Design and mechanics, What the team believes.");
                }

                var subCategory = TeamQuestionEnumMapper.ParseSubCategory(dto.Specification.SubCategory);
                if (subCategory == null)
                {
                    await transaction.RollbackAsync();
                    return Failed(
                        $"Invalid Sub-category '{dto.Specification.SubCategory}'. Valid values are: " +
                        "Goal Affinity, Results, Improvement Loop, Structure, Coordination Quality, " +
                        "Accountability, Potency, Belonging.");
                }

                var variant = string.IsNullOrWhiteSpace(dto.Specification.Variant)
                    ? specification.Variant
                    : TeamQuestionEnumMapper.ParseVariant(dto.Specification.Variant) ?? specification.Variant;

                if (!string.IsNullOrWhiteSpace(dto.Specification.Variant)
                    && TeamQuestionEnumMapper.ParseVariant(dto.Specification.Variant) == null)
                {
                    await transaction.RollbackAsync();
                    return Failed(
                        $"Invalid Variant '{dto.Specification.Variant}'. Valid values are: Generic, Goal-anchored.");
                }

                var ordinal = dto.Specification.Ordinal.HasValue && dto.Specification.Ordinal.Value > 0
                    ? (ushort)dto.Specification.Ordinal.Value
                    : specification.Ordinal;

                // Null means "leave the category alone"; 0 is a real instruction, moving the
                // question into the general set.
                uint categoryId;

                try
                {
                    categoryId = dto.Specification.CategoryId.HasValue
                        ? await ResolveCategoryIdAsync(dto.Specification.CategoryId)
                        : specification.CategoryId;
                }
                catch (ArgumentException ex)
                {
                    await transaction.RollbackAsync();
                    return Failed(ex.Message);
                }

                // Moving a question onto an occupied slot would violate the unique index; report
                // it as a sentence instead of letting Postgres raise it.
                var slotTaken = await _context.TeamDailyQuestionSpecifications
                    .AnyAsync(s => s.Id != specification.Id
                                   && s.CategoryId == categoryId
                                   && s.SubCategory == subCategory.Value
                                   && s.Variant == variant
                                   && s.Ordinal == ordinal
                                   && s.ContentCultureId == specification.ContentCultureId);

                if (slotTaken)
                {
                    await transaction.RollbackAsync();
                    return Failed(
                        $"Ask #{ordinal} for {TeamQuestionEnumMapper.ToLabel(subCategory.Value)} " +
                        $"({TeamQuestionEnumMapper.ToLabel(variant)}) is already used by another question " +
                        $"in category {categoryId}.");
                }

                question.Text = SanitizeText(dto.QuestionText);
                specification.CategoryId = categoryId;
                specification.TeamElement = teamElement.Value;
                specification.SubCategory = subCategory.Value;
                specification.Variant = variant;
                specification.Ordinal = ordinal;

                await _context.SaveChangesAsync();

                var error = await ApplyReasonChangesAsync(dto, specification);

                if (error != null)
                {
                    await transaction.RollbackAsync();
                    return Failed(error);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return new UpdateTeamQuestionInfoResultDto
                {
                    IsSuccess = true,
                    QuestionInfo = await GetTeamQuestionInfoByIdAsync(dto.QuestionId)
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Failed($"An error occurred while updating the question: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies the reason and micro-action edits. Returns the error sentence on the first
        /// problem found, or null when everything applied.
        /// </summary>
        private async Task<string?> ApplyReasonChangesAsync(
            UpdateTeamQuestionInfoDto dto,
            TeamDailyQuestionSpecification specification)
        {
            var contentCultureId = specification.ContentCultureId;

            foreach (var reasonDto in dto.Specification.Reasons ?? new List<UpdateTeamReasonInfoDto>())
            {
                var isDelete = reasonDto.IsDel == true;
                var hasId = reasonDto.ReasonId.HasValue && reasonDto.ReasonId.Value > 0;

                if (!isDelete && string.IsNullOrWhiteSpace(reasonDto.Text))
                {
                    return "Reason text cannot be empty.";
                }

                if (isDelete)
                {
                    if (!hasId)
                    {
                        continue;
                    }

                    var error = await DeleteReasonAsync(reasonDto.ReasonId!.Value);
                    if (error != null)
                    {
                        return error;
                    }

                    continue;
                }

                var sentiment = TeamQuestionEnumMapper.ParseSentiment(reasonDto.SentimentType);
                if (sentiment == null)
                {
                    return hasId
                        ? $"Invalid sentiment type '{reasonDto.SentimentType}' for reason ID {reasonDto.ReasonId}. Valid values are: Positive, Negative."
                        : $"Invalid sentiment type '{reasonDto.SentimentType}' for new reason. Valid values are: Positive, Negative.";
                }

                // A supplied-but-unrecognised Team State is a typo, not a "clear it" instruction.
                var teamState = TeamQuestionEnumMapper.ParseTeamState(reasonDto.TeamState);
                if (!string.IsNullOrWhiteSpace(reasonDto.TeamState) && teamState == null)
                {
                    return $"Invalid Team State '{reasonDto.TeamState}'. Valid values are: " +
                           "Cruise, Drift, Headwinds, Stall, External, Neutral.";
                }

                TeamDailyReason reason;

                if (hasId)
                {
                    var existing = await _context.TeamDailyReasons
                        .FirstOrDefaultAsync(r => r.Id == reasonDto.ReasonId!.Value);

                    if (existing == null)
                    {
                        return $"Reason with ID {reasonDto.ReasonId} not found. Cannot update non-existent reason.";
                    }

                    if (existing.SpecificationId != specification.Id)
                    {
                        return $"Reason {existing.Id} does not belong to specification {specification.Id}.";
                    }

                    existing.Text = SanitizeText(reasonDto.Text);
                    existing.TeamDailyReasonSentimentType = sentiment.Value;
                    existing.RequiresFreeTextFeedback = reasonDto.RequiresFreeTextFeedback;
                    existing.TeamState = teamState;

                    reason = existing;
                }
                else
                {
                    reason = new TeamDailyReason
                    {
                        Text = SanitizeText(reasonDto.Text),
                        TeamDailyReasonSentimentType = sentiment.Value,
                        RequiresFreeTextFeedback = reasonDto.RequiresFreeTextFeedback,
                        TeamState = teamState,
                        SpecificationId = specification.Id,
                        ContentCultureId = contentCultureId
                    };

                    _context.TeamDailyReasons.Add(reason);
                }

                // Saved before the actions so a brand-new reason has its identity key.
                await _context.SaveChangesAsync();

                var actionError = await ApplyActionChangesAsync(reasonDto, reason.Id, contentCultureId);
                if (actionError != null)
                {
                    return actionError;
                }
            }

            return null;
        }

        private async Task<string?> ApplyActionChangesAsync(
            UpdateTeamReasonInfoDto reasonDto,
            uint reasonId,
            uint contentCultureId)
        {
            foreach (var actionDto in reasonDto.Actions ?? new List<UpdateTeamActionInfoDto>())
            {
                var isDelete = actionDto.IsDel == true;
                var hasId = actionDto.ActionId.HasValue && actionDto.ActionId.Value > 0;

                if (hasId)
                {
                    var existing = await _context.TeamDailyMicroActions
                        .FirstOrDefaultAsync(a => a.Id == actionDto.ActionId!.Value);

                    if (existing == null)
                    {
                        return $"Action with ID {actionDto.ActionId} not found. Cannot update non-existent action.";
                    }

                    if (isDelete)
                    {
                        _context.TeamDailyMicroActions.Remove(existing);
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(actionDto.Text))
                    {
                        return $"Action text cannot be empty for action ID {actionDto.ActionId}.";
                    }

                    existing.Text = SanitizeText(actionDto.Text);
                    continue;
                }

                if (isDelete || string.IsNullOrWhiteSpace(actionDto.Text))
                {
                    continue;
                }

                _context.TeamDailyMicroActions.Add(new TeamDailyMicroAction
                {
                    Text = SanitizeText(actionDto.Text),
                    ContentCultureId = contentCultureId,
                    TeamDailyReasonId = reasonId
                });
            }

            await _context.SaveChangesAsync();
            return null;
        }

        /// <summary>
        /// Removes a reason and its micro-actions. A reason that has already been answered is
        /// refused: TeamDailyFeedback holds the reason by foreign key so the historical reading
        /// could not be reconstructed without it.
        /// </summary>
        private async Task<string?> DeleteReasonAsync(long reasonId)
        {
            var reason = await _context.TeamDailyReasons.FirstOrDefaultAsync(r => r.Id == reasonId);

            if (reason == null)
            {
                return $"Reason with ID {reasonId} not found. Cannot delete non-existent reason.";
            }

            var answered = await _context.TeamDailyFeedback.AnyAsync(f => f.TeamDailyReasonId == reason.Id);

            if (answered)
            {
                return $"Reason {reasonId} has feedback recorded against it and cannot be deleted.";
            }

            var actions = await _context.TeamDailyMicroActions
                .Where(a => a.TeamDailyReasonId == reason.Id)
                .ToListAsync();

            if (actions.Count > 0)
            {
                _context.TeamDailyMicroActions.RemoveRange(actions);
            }

            _context.TeamDailyReasons.Remove(reason);
            await _context.SaveChangesAsync();

            return null;
        }

        private static UpdateTeamQuestionInfoResultDto Failed(string message) =>
            new UpdateTeamQuestionInfoResultDto { IsSuccess = false, ErrorMessage = message };
    }
}
