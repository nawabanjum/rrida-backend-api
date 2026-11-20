using CloverBackoffice.Infrastructure.DTO;
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
    /// Team Daily Questions — importing the workbook, and re-applying a corrected workbook
    /// over questions that are already in the bank.
    /// </summary>
    public partial class TeamQuestionRepository
    {
        public async Task<ImportTeamQuestionResponseDto> ImportTeamQuestionsAsync(IFormFile file)
        {
            var response = new ImportTeamQuestionResponseDto();

            var (rows, error) = await LoadRowsAsync(file);

            if (error != null)
            {
                response.FailureCount = 1;
                response.Results.Add(new ImportTeamQuestionRowResultDto
                {
                    RowNumber = 0,
                    IsSuccess = false,
                    Message = error
                });

                return response;
            }

            response.TotalRows = rows.Count;

            // Loaded once rather than per row. The lookup covers the name spelling of the
            // column as well as the id, so a file can say "Healthcare" instead of "3".
            var categoriesByName = await _context.TeamQuestionCategories
                .AsNoTracking()
                .Where(c => !c.IsDel)
                .ToDictionaryAsync(c => c.Name.ToLower(), c => c.Id);

            // Duplicate detection is per-file, matching the legacy importer: a question already
            // in the database is not compared against. The rotation slot check inside the add
            // is what actually stops the same workbook being imported twice.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                var result = new ImportTeamQuestionRowResultDto
                {
                    RowNumber = row.RowNumber,
                    QuestionText = row.QuestionText
                };

                // A question repeated in the file is genuinely ambiguous — the goal-anchored
                // variants share an Ask # but carry different wording, so text is the right key.
                if (!seen.Add(SanitizeText(row.QuestionText)))
                {
                    result.Message = "Duplicate question within the uploaded file. Only the first occurrence was imported.";
                    response.FailureCount++;
                    response.Results.Add(result);
                    continue;
                }

                var validation = ValidateRow(row);

                if (validation != null)
                {
                    result.Message = validation;
                    response.FailureCount++;
                    response.Results.Add(result);
                    continue;
                }

                var (rowCategoryId, categoryError) = ResolveRowCategory(row.CategoryCell, categoriesByName);

                if (categoryError != null)
                {
                    result.Message = categoryError;
                    response.FailureCount++;
                    response.Results.Add(result);
                    continue;
                }

                try
                {
                    var dto = new AddTeamQuestionDto
                    {
                        QuestionText = row.QuestionText,
                        TeamElement = row.TeamElement,
                        SubCategory = row.SubCategory,
                        Variant = row.Variant,
                        CategoryId = rowCategoryId,
                        Ordinal = row.AskNumber,
                        PositiveReasons = ToReasonInputs(row.GoodReasons),
                        NegativeReasons = ToReasonInputs(row.BadReasons)
                    };

                    result.QuestionData = await AddTeamQuestionInternalAsync(dto);
                    result.IsSuccess = true;
                    result.Message = "Question imported successfully.";
                    response.SuccessCount++;
                }
                catch (Exception ex)
                {
                    // One bad row must not cost the other twenty-six; each add has its own
                    // transaction, so a failure here leaves the rows before it committed.
                    result.Message = ex.GetBaseException().Message;
                    response.FailureCount++;
                }

                response.Results.Add(result);
            }

            return response;
        }

        /// <summary>
        /// Works out which category a row belongs to.
        ///
        /// An empty cell is not an error — it is the documented way of saying "this question
        /// is for everyone", and it resolves to the seeded General category (0). A numeric
        /// cell is taken as the id and checked when the question is written; anything else is
        /// treated as a category NAME, so a file can carry "Healthcare" rather than "3".
        /// </summary>
        private static (long CategoryId, string? Error) ResolveRowCategory(
            string categoryCell,
            Dictionary<string, uint> categoriesByName)
        {
            var text = (categoryCell ?? string.Empty).Trim();

            if (text.Length == 0)
            {
                return (Infrastructure.Models.TeamQuestions.TeamQuestionCategory.DefaultCategoryId, null);
            }

            if (long.TryParse(text, out var id))
            {
                return id < 0
                    ? (0, $"Invalid CategoryId '{text}'.")
                    : (id, null);
            }

            if (categoriesByName.TryGetValue(text.ToLowerInvariant(), out var byName))
            {
                return (byName, null);
            }

            return (0, $"Category '{text}' does not exist. Create it first, or leave the column blank " +
                       "to import the question under the default General category.");
        }

        /// <summary>
        /// Everything a row needs before it is worth opening a transaction for. Returns the
        /// error sentence, or null when the row is importable.
        /// </summary>
        private static string? ValidateRow(WorkbookRow row)
        {
            if (string.IsNullOrWhiteSpace(row.QuestionText))
            {
                return "Question text is required.";
            }

            if (Infrastructure.BusinessModel.TeamQuestionEnumMapper.ParseTeamElement(row.TeamElement) == null)
            {
                return $"Invalid Team Element '{row.TeamElement}'. Valid values are: " +
                       "Direction and learning, Design and mechanics, What the team believes.";
            }

            if (Infrastructure.BusinessModel.TeamQuestionEnumMapper.ParseSubCategory(row.SubCategory) == null)
            {
                return $"Invalid Sub-category '{row.SubCategory}'. Valid values are: " +
                       "Goal Affinity, Results, Improvement Loop, Structure, Coordination Quality, " +
                       "Accountability, Potency, Belonging.";
            }

            // Blank means generic — only a value that is present and unrecognised is a problem.
            if (!string.IsNullOrWhiteSpace(row.Variant)
                && Infrastructure.BusinessModel.TeamQuestionEnumMapper.ParseVariant(row.Variant) == null)
            {
                return $"Invalid Variant '{row.Variant}'. Valid values are: Generic, Goal-anchored.";
            }

            if (row.GoodReasons.Count == 0 && row.BadReasons.Count == 0)
            {
                return "At least one reason (Good or Bad) must be provided.";
            }

            return null;
        }

        private static List<TeamReasonInputDto> ToReasonInputs(IEnumerable<WorkbookReason> reasons) =>
            reasons.Select(r => new TeamReasonInputDto
            {
                Text = r.Text,
                TeamState = r.TeamState,
                MicroAction = r.MicroAction
            }).ToList();

        private static async Task<(List<WorkbookRow> Rows, string? Error)> LoadRowsAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return (new List<WorkbookRow>(), "File is empty or not provided.");
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            return extension switch
            {
                ".xlsx" => await ReadWorkbookAsync(file),
                ".csv" => await ReadCsvAsync(file),
                _ => (new List<WorkbookRow>(), "Invalid file format. Only CSV and XLSX files are supported.")
            };
        }
    }
}
