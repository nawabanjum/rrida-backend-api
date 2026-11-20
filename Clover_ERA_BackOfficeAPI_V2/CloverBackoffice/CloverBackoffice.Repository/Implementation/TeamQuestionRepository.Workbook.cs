using Microsoft.AspNetCore.Http;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    /// <summary>
    /// Reads the RRIDA Team Conditions workbook.
    ///
    /// The layout that has to be handled (RRIDA_Team_Conditions_Questions_V1, 22 columns):
    ///
    ///   Ask # | Team Element | Sub-category | Variant | Question
    ///
    /// An optional "CategoryId" column may be added to file the rows against a category;
    /// rows without one are imported into the seeded General category (0).
    ///   Good Reason 1 | Team State | GR1 Micro-Action
    ///   Good Reason 2 | Team State | GR2 Micro-Action
    ///   Good Reason 3 | Team State | GR3 Micro-Action
    ///   Bad Reason 1  | Team State | BR1 Micro-Action
    ///   Bad Reason 2  | Team State | BR2 Micro-Action
    ///   Bad Reason 3  |            | BR3 Micro-Action
    ///
    /// "Team State" appears five times under the same name, so it cannot be resolved from a
    /// name-to-index map the way every other column is — the map would keep only the first.
    /// It is resolved POSITIONALLY instead: the Team State for a reason is the next column
    /// along, and only when that column is actually headed "Team State". That is why the last
    /// pair, which has no Team State column at all, needs no special case.
    /// </summary>
    public partial class TeamQuestionRepository
    {
        /// <summary>One reason with the two cells that belong to it.</summary>
        private sealed class WorkbookReason
        {
            public string Text { get; set; } = string.Empty;
            public string? TeamState { get; set; }
            public string? MicroAction { get; set; }
        }

        private sealed class WorkbookRow
        {
            public int RowNumber { get; set; }
            public int? AskNumber { get; set; }

            /// <summary>
            /// Raw "CategoryId" (or "Category") cell. Blank means the question belongs to
            /// everyone and is filed under the seeded General category.
            /// </summary>
            public string CategoryCell { get; set; } = string.Empty;
            public string TeamElement { get; set; } = string.Empty;
            public string SubCategory { get; set; } = string.Empty;
            public string Variant { get; set; } = string.Empty;
            public string QuestionText { get; set; } = string.Empty;
            public List<WorkbookReason> GoodReasons { get; set; } = new List<WorkbookReason>();
            public List<WorkbookReason> BadReasons { get; set; } = new List<WorkbookReason>();
        }

        /// <summary>Header names accepted for each fixed column, first match wins.</summary>
        private static readonly string[] AskHeaders = { "Ask #", "Ask#", "Ask No", "Ask Number", "Ordinal" };
        private static readonly string[] CategoryHeaders = { "CategoryId", "Category Id", "Category_Id", "Category" };
        private static readonly string[] TeamElementHeaders = { "Team Element", "TeamElement", "Element", "Group" };
        private static readonly string[] SubCategoryHeaders = { "Sub-category", "Sub category", "Subcategory", "Condition" };
        private static readonly string[] VariantHeaders = { "Variant" };
        private static readonly string[] QuestionHeaders = { "Question", "New Question" };

        private const int MaxReasonsPerSentiment = 4;

        /// <summary>
        /// Checks the header row carries everything the import needs. Returns the error
        /// sentence, or null when the file is usable.
        /// </summary>
        private static string? ValidateWorkbookHeaders(List<string> headers)
        {
            var normalized = headers.Select(h => h?.Trim() ?? string.Empty).ToList();

            bool Has(params string[] names) =>
                names.Any(n => normalized.Any(h => h.Equals(n, StringComparison.OrdinalIgnoreCase)));

            if (!Has(QuestionHeaders))
            {
                return "Invalid file format. The 'Question' column is missing.";
            }

            if (!Has(TeamElementHeaders))
            {
                return "Invalid file format. The 'Team Element' column is missing.";
            }

            if (!Has(SubCategoryHeaders))
            {
                return "Invalid file format. The 'Sub-category' column is missing.";
            }

            // Every reason present must bring its micro-action column, the same dependency the
            // legacy importer enforces. A reason column that is absent entirely is fine — the
            // workbook only fills three of the four slots.
            foreach (var prefix in new[] { "G", "B" })
            {
                var word = prefix == "G" ? "Good" : "Bad";

                for (var i = 1; i <= MaxReasonsPerSentiment; i++)
                {
                    if (!Has($"{word} Reason {i}"))
                    {
                        continue;
                    }

                    if (!Has($"{prefix}R{i} Micro-Action", $"{prefix}R{i}-Micro-Action", $"{prefix}R{i} Micro Action"))
                    {
                        return $"Invalid file format. '{word} Reason {i}' has no matching '{prefix}R{i} Micro-Action' column.";
                    }
                }
            }

            if (!Has("Good Reason 1") && !Has("Bad Reason 1"))
            {
                return "Invalid file format. No reason columns were found.";
            }

            return null;
        }

        /// <summary>
        /// Reads every data row of the first worksheet. Returns the rows and, when the file
        /// cannot be used at all, the error sentence.
        /// </summary>
        private static async Task<(List<WorkbookRow> Rows, string? Error)> ReadWorkbookAsync(IFormFile file)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            using var package = new ExcelPackage(stream);

            var worksheet = package.Workbook.Worksheets.FirstOrDefault();

            if (worksheet?.Dimension == null || worksheet.Dimension.Rows < 1)
            {
                return (new List<WorkbookRow>(), "Invalid file format. File header row is missing.");
            }

            var columnCount = worksheet.Dimension.Columns;

            // Kept as a positional list, not just a map: the Team State lookup needs to know
            // what is in the column NEXT to a reason, which a name-keyed map cannot answer.
            var headers = new List<string>(columnCount);
            for (var col = 1; col <= columnCount; col++)
            {
                headers.Add(worksheet.Cells[1, col].Text?.Trim() ?? string.Empty);
            }

            var headerError = ValidateWorkbookHeaders(headers);
            if (headerError != null)
            {
                return (new List<WorkbookRow>(), headerError);
            }

            int? Find(params string[] names)
            {
                foreach (var name in names)
                {
                    var index = headers.FindIndex(h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0)
                    {
                        return index + 1; // EPPlus columns are 1-based.
                    }
                }

                return null;
            }

            var askCol = Find(AskHeaders);
            // Optional: a file without it imports everything under the General category.
            var categoryCol = Find(CategoryHeaders);
            var elementCol = Find(TeamElementHeaders);
            var subCategoryCol = Find(SubCategoryHeaders);
            var variantCol = Find(VariantHeaders);
            var questionCol = Find(QuestionHeaders);

            // Resolve each reason slot once, up front: the reason column, the Team State column
            // beside it if there is one, and the micro-action column by name.
            var goodSlots = ResolveReasonSlots(headers, Find, "Good", "G");
            var badSlots = ResolveReasonSlots(headers, Find, "Bad", "B");

            string Cell(int row, int? col) =>
                col.HasValue ? worksheet.Cells[row, col.Value].Text?.Trim() ?? string.Empty : string.Empty;

            var rows = new List<WorkbookRow>();

            for (var row = 2; row <= worksheet.Dimension.Rows; row++)
            {
                var questionText = Cell(row, questionCol);

                // The workbook ends with free-text notes under the table; a row with no question
                // is not a row.
                if (string.IsNullOrWhiteSpace(questionText))
                {
                    continue;
                }

                rows.Add(new WorkbookRow
                {
                    RowNumber = row,
                    AskNumber = int.TryParse(Cell(row, askCol), out var ask) ? ask : null,
                    CategoryCell = Cell(row, categoryCol),
                    TeamElement = Cell(row, elementCol),
                    SubCategory = Cell(row, subCategoryCol),
                    Variant = Cell(row, variantCol),
                    QuestionText = questionText,
                    GoodReasons = ReadSlots(goodSlots, row, Cell),
                    BadReasons = ReadSlots(badSlots, row, Cell)
                });
            }

            return (rows, null);
        }

        /// <summary>The three columns that make up one reason slot; any of them may be absent.</summary>
        private static List<(int? ReasonCol, int? TeamStateCol, int? ActionCol)> ResolveReasonSlots(
            List<string> headers,
            Func<string[], int?> find,
            string word,
            string prefix)
        {
            var slots = new List<(int?, int?, int?)>();

            for (var i = 1; i <= MaxReasonsPerSentiment; i++)
            {
                var reasonCol = find(new[] { $"{word} Reason {i}" });

                if (reasonCol == null)
                {
                    continue;
                }

                // Positional, by design — see the class comment.
                int? teamStateCol = null;
                var nextIndex = reasonCol.Value; // 0-based index of the column after the reason.
                if (nextIndex < headers.Count && headers[nextIndex].Equals("Team State", StringComparison.OrdinalIgnoreCase))
                {
                    teamStateCol = reasonCol.Value + 1;
                }

                var actionCol = find(new[]
                {
                    $"{prefix}R{i} Micro-Action",
                    $"{prefix}R{i}-Micro-Action",
                    $"{prefix}R{i} Micro Action"
                });

                slots.Add((reasonCol, teamStateCol, actionCol));
            }

            return slots;
        }

        private static List<WorkbookReason> ReadSlots(
            List<(int? ReasonCol, int? TeamStateCol, int? ActionCol)> slots,
            int row,
            Func<int, int?, string> cell)
        {
            var reasons = new List<WorkbookReason>();

            foreach (var slot in slots)
            {
                var text = cell(row, slot.ReasonCol);

                // An empty reason cell means the slot is unused; its micro-action, if any, has
                // nothing to hang off and is dropped with it.
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var teamState = cell(row, slot.TeamStateCol);
                var action = cell(row, slot.ActionCol);

                reasons.Add(new WorkbookReason
                {
                    Text = text,
                    TeamState = string.IsNullOrWhiteSpace(teamState) ? null : teamState,
                    MicroAction = string.IsNullOrWhiteSpace(action) ? null : action
                });
            }

            return reasons;
        }

        /// <summary>
        /// CSV counterpart of <see cref="ReadWorkbookAsync" />, for callers exporting the
        /// workbook to CSV first. Same column rules, including the positional Team State.
        /// </summary>
        private static async Task<(List<WorkbookRow> Rows, string? Error)> ReadCsvAsync(IFormFile file)
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            using var reader = new StreamReader(stream, Encoding.UTF8);

            var headerLine = await reader.ReadLineAsync();

            if (string.IsNullOrWhiteSpace(headerLine))
            {
                return (new List<WorkbookRow>(), "Invalid file format. File header row is missing.");
            }

            var headers = ParseCsvLine(headerLine).Select(h => h.Trim()).ToList();

            var headerError = ValidateWorkbookHeaders(headers);
            if (headerError != null)
            {
                return (new List<WorkbookRow>(), headerError);
            }

            int? Find(params string[] names)
            {
                foreach (var name in names)
                {
                    var index = headers.FindIndex(h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0)
                    {
                        return index + 1; // Kept 1-based so the slot resolver is shared with Excel.
                    }
                }

                return null;
            }

            var askCol = Find(AskHeaders);
            var categoryCol = Find(CategoryHeaders);
            var elementCol = Find(TeamElementHeaders);
            var subCategoryCol = Find(SubCategoryHeaders);
            var variantCol = Find(VariantHeaders);
            var questionCol = Find(QuestionHeaders);

            var goodSlots = ResolveReasonSlots(headers, Find, "Good", "G");
            var badSlots = ResolveReasonSlots(headers, Find, "Bad", "B");

            var rows = new List<WorkbookRow>();
            var rowNumber = 1;
            string? line;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                rowNumber++;

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var columns = ParseCsvLine(line);

                string Cell(int _, int? col) =>
                    col.HasValue && col.Value - 1 < columns.Count
                        ? columns[col.Value - 1]?.Trim() ?? string.Empty
                        : string.Empty;

                var questionText = Cell(rowNumber, questionCol);

                if (string.IsNullOrWhiteSpace(questionText))
                {
                    continue;
                }

                rows.Add(new WorkbookRow
                {
                    RowNumber = rowNumber,
                    AskNumber = int.TryParse(Cell(rowNumber, askCol), out var ask) ? ask : null,
                    CategoryCell = Cell(rowNumber, categoryCol),
                    TeamElement = Cell(rowNumber, elementCol),
                    SubCategory = Cell(rowNumber, subCategoryCol),
                    Variant = Cell(rowNumber, variantCol),
                    QuestionText = questionText,
                    GoodReasons = ReadSlots(goodSlots, rowNumber, Cell),
                    BadReasons = ReadSlots(badSlots, rowNumber, Cell)
                });
            }

            return (rows, null);
        }

        private static List<string> ParseCsvLine(string line)
        {
            var columns = new List<string>();
            var current = new StringBuilder();
            var insideQuotes = false;

            foreach (var c in line)
            {
                if (c == '"')
                {
                    insideQuotes = !insideQuotes;
                }
                else if (c == ',' && !insideQuotes)
                {
                    columns.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            columns.Add(current.ToString());
            return columns;
        }
    }
}
