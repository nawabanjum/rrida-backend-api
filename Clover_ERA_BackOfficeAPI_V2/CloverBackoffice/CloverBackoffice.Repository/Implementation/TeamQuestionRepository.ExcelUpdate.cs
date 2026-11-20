using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models.TeamQuestions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    /// <summary>
    /// Re-applies a corrected workbook over questions that are already in the bank.
    ///
    /// Rows are matched to questions by question TEXT, as the legacy equivalent does, and only
    /// the reason wording, the micro-action wording and the Team State are rewritten. Nothing
    /// is created or removed: a row whose question is not in the bank is reported and skipped,
    /// which keeps this operation safe to run against a partially-imported bank.
    /// </summary>
    public partial class TeamQuestionRepository
    {
        public async Task<UpdateTeamQuestionsFromExcelResponseDto> UpdateTeamQuestionsFromExcelAsync(IFormFile file)
        {
            var response = new UpdateTeamQuestionsFromExcelResponseDto();

            if (file == null || file.Length == 0)
            {
                response.Results.Add(Row(0, "File is empty or not provided."));
                return response;
            }

            if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                response.Results.Add(Row(0, "Invalid file format. Only XLSX files are supported for this operation."));
                return response;
            }

            var (rows, error) = await ReadWorkbookAsync(file);

            if (error != null)
            {
                response.Results.Add(Row(0, error));
                return response;
            }

            response.TotalRowsInExcel = rows.Count;

            if (rows.Count == 0)
            {
                response.Results.Add(Row(0, "No data rows found in the file."));
                return response;
            }

            var specifications = await _context.TeamDailyQuestionSpecifications
                .Include(s => s.Question)
                .Include(s => s.Reasons)
                .OrderBy(s => s.Id)
                .ToListAsync();

            var reasonIds = specifications.SelectMany(s => s.Reasons).Select(r => r.Id).ToList();

            var actions = await _context.TeamDailyMicroActions
                .Where(a => reasonIds.Contains(a.TeamDailyReasonId))
                .ToListAsync();

            var actionsByReasonId = actions
                .GroupBy(a => a.TeamDailyReasonId)
                .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Id).ToList());

            var specByQuestionText = new Dictionary<string, TeamDailyQuestionSpecification>(StringComparer.OrdinalIgnoreCase);

            foreach (var specification in specifications)
            {
                var key = SanitizeText(specification.Question?.Text);

                if (!string.IsNullOrEmpty(key) && !specByQuestionText.ContainsKey(key))
                {
                    specByQuestionText[key] = specification;
                }
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                foreach (var row in rows)
                {
                    var questionText = SanitizeText(row.QuestionText);

                    if (!specByQuestionText.TryGetValue(questionText, out var matched))
                    {
                        response.SkippedRows++;
                        response.Results.Add(new TeamExcelUpdateRowResultDto
                        {
                            RowNumber = row.RowNumber,
                            QuestionText = questionText,
                            Message = "No matching question found in database."
                        });

                        continue;
                    }

                    response.MatchedQuestions++;

                    var rowResult = new TeamExcelUpdateRowResultDto
                    {
                        RowNumber = row.RowNumber,
                        QuestionText = questionText,
                        IsMatched = true
                    };

                    ApplyRow(matched, row, actionsByReasonId, response, rowResult);

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
                response.Results.Add(Row(0, $"Transaction failed: {ex.GetBaseException().Message}"));
            }

            return response;
        }

        private void ApplyRow(
            TeamDailyQuestionSpecification specification,
            WorkbookRow row,
            Dictionary<uint, List<TeamDailyMicroAction>> actionsByReasonId,
            UpdateTeamQuestionsFromExcelResponseDto response,
            TeamExcelUpdateRowResultDto rowResult)
        {
            ApplySentiment(
                specification, row.GoodReasons, TeamDailyReasonSentimentType.Positive,
                actionsByReasonId, response, rowResult);

            ApplySentiment(
                specification, row.BadReasons, TeamDailyReasonSentimentType.Negative,
                actionsByReasonId, response, rowResult);
        }

        /// <summary>
        /// Pairs the stored reasons with the workbook's reasons by position, the way the legacy
        /// comparison does, and rewrites whatever differs. The free-text ("Other") reason is
        /// excluded from the pairing — it is appended by the importer, never present in the
        /// workbook, and including it would shift every pair by one.
        /// </summary>
        private void ApplySentiment(
            TeamDailyQuestionSpecification specification,
            List<WorkbookReason> workbookReasons,
            TeamDailyReasonSentimentType sentiment,
            Dictionary<uint, List<TeamDailyMicroAction>> actionsByReasonId,
            UpdateTeamQuestionsFromExcelResponseDto response,
            TeamExcelUpdateRowResultDto rowResult)
        {
            var isPositive = sentiment == TeamDailyReasonSentimentType.Positive;
            var label = isPositive ? "Positive" : "Negative";

            var storedReasons = specification.Reasons
                .Where(r => r.TeamDailyReasonSentimentType == sentiment && !IsFreeTextReason(r.Text))
                .OrderBy(r => r.Id)
                .ToList();

            var count = Math.Min(storedReasons.Count, workbookReasons.Count);

            for (var i = 0; i < count; i++)
            {
                var stored = storedReasons[i];
                var incoming = workbookReasons[i];

                var incomingText = SanitizeText(incoming.Text);

                if (!string.Equals(stored.Text, incomingText, StringComparison.Ordinal))
                {
                    rowResult.ChangeDetails.Add(
                        $"{label} Reason #{i + 1} (ID {stored.Id}): \"{stored.Text}\" → \"{incomingText}\"");

                    stored.Text = incomingText;
                    _context.TeamDailyReasons.Update(stored);

                    if (isPositive)
                    {
                        response.UpdatedPositiveReasons++;
                    }
                    else
                    {
                        response.UpdatedNegativeReasons++;
                    }
                }

                // A blank Team State cell means "not classified in this revision", so it is left
                // alone rather than used to wipe a state that is already recorded.
                var incomingState = TeamQuestionEnumMapper.ParseTeamState(incoming.TeamState);

                if (incomingState != null && stored.TeamState != incomingState)
                {
                    rowResult.ChangeDetails.Add(
                        $"{label} Reason #{i + 1} (ID {stored.Id}) Team State: " +
                        $"\"{TeamQuestionEnumMapper.ToLabel(stored.TeamState) ?? "none"}\" → \"{incomingState}\"");

                    stored.TeamState = incomingState;
                    _context.TeamDailyReasons.Update(stored);
                    response.UpdatedTeamStates++;
                }

                ApplyMicroAction(stored, incoming, actionsByReasonId, label, i, isPositive, response, rowResult);
            }
        }

        private void ApplyMicroAction(
            TeamDailyReason stored,
            WorkbookReason incoming,
            Dictionary<uint, List<TeamDailyMicroAction>> actionsByReasonId,
            string label,
            int index,
            bool isPositive,
            UpdateTeamQuestionsFromExcelResponseDto response,
            TeamExcelUpdateRowResultDto rowResult)
        {
            var incomingAction = SanitizeText(incoming.MicroAction);

            if (string.IsNullOrWhiteSpace(incomingAction))
            {
                return;
            }

            if (actionsByReasonId.TryGetValue(stored.Id, out var storedActions) && storedActions.Count > 0)
            {
                var storedAction = storedActions[0];

                if (string.Equals(storedAction.Text, incomingAction, StringComparison.Ordinal))
                {
                    return;
                }

                rowResult.ChangeDetails.Add(
                    $"{label} Action for Reason #{index + 1} (ID {storedAction.Id}): " +
                    $"\"{storedAction.Text}\" → \"{incomingAction}\"");

                storedAction.Text = incomingAction;
                _context.TeamDailyMicroActions.Update(storedAction);
            }
            else
            {
                // The reason was imported before its micro-action existed in the workbook.
                // Filling that gap is the point of re-running this, so it is created.
                var created = new TeamDailyMicroAction
                {
                    Text = incomingAction,
                    ContentCultureId = stored.ContentCultureId,
                    TeamDailyReasonId = stored.Id
                };

                _context.TeamDailyMicroActions.Add(created);
                actionsByReasonId[stored.Id] = new List<TeamDailyMicroAction> { created };

                rowResult.ChangeDetails.Add(
                    $"{label} Action for Reason #{index + 1} (ID {stored.Id}): added \"{incomingAction}\"");
            }

            if (isPositive)
            {
                response.UpdatedPositiveActions++;
            }
            else
            {
                response.UpdatedNegativeActions++;
            }
        }

        private static TeamExcelUpdateRowResultDto Row(int rowNumber, string message) =>
            new TeamExcelUpdateRowResultDto { RowNumber = rowNumber, Message = message };
    }
}
