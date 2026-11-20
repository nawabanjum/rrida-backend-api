using System.Globalization;
using OfficeOpenXml;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Reads the "RRIDA Daily Check-in - Test Data" workbook. This layout is the source of
    /// truth for the import, so the cells below are the contract:
    ///
    ///   One sheet per team. Column A holds the labels, column C the values:
    ///     "Team name"                     C  e.g. Platform engineering
    ///     "Working days"                  C  Mon-Fri, Sun-Thu, Sat-Wed, Tue-Sat, Wed-Sun, Every day
    ///     "First day of testing (Day 1)"  C  a date
    ///   The header row is the one whose column A reads "Day No."; data starts below it:
    ///     A Day No. (the asked-day index, 1..90)   B Team member
    ///     C..H  engine (individual) question: D sentiment, E-G chips, H text
    ///     I..N  team daily question:           J sentiment, K-M chips, N text
    ///     O..P  custom question and its answer (optional)
    ///   Sentiment is Red, Orange, Yellow or Green. Chips are the chip labels as the app
    ///   shows them. The question columns are formulas and are never read: the question
    ///   comes from the engine's own rotation for that Day No.
    ///
    /// A sheet with no team name and no rows (the blank template tab) is ignored. A row
    /// whose member contains "EXAMPLE" is the template's worked example and is skipped.
    /// </summary>
    public static class EngineWorkbookReader
    {
        public const int MaxTextLength = 300;

        private const int ColDay = 1, ColMember = 2;
        private const int ColIndSentiment = 4, ColIndChip1 = 5, ColIndText = 8;
        private const int ColTeamSentiment = 10, ColTeamChip1 = 11, ColTeamText = 14;
        private const int ColCustomQuestion = 15, ColCustomAnswer = 16;

        // The header must still be where the template put it; otherwise the columns above
        // would be read from the wrong place, silently.
        private static readonly (int Col, string Text)[] ExpectedHeaders =
        [
            (ColDay, "Day No."), (ColMember, "Team member"),
            (ColIndSentiment, "Sentiment"), (ColTeamSentiment, "Sentiment"),
            (ColCustomQuestion, "Custom question"), (ColCustomAnswer, "Answer")
        ];

        private static readonly string[] Sentiments = ["Red", "Orange", "Yellow", "Green"];

        public sealed class Sheet
        {
            public string SheetName { get; init; } = string.Empty;
            public string? TeamName { get; set; }
            public string? WorkingDays { get; set; }
            public DateOnly? StartDate { get; set; }

            /// <summary>Set when the sheet cannot be imported at all.</summary>
            public string? Error { get; set; }

            public List<Row> Rows { get; } = [];
            public List<string> Warnings { get; } = [];
        }

        public sealed class Row
        {
            public int RowNumber { get; init; }
            public int Day { get; init; }
            public string Member { get; init; } = string.Empty;
            public Section? Individual { get; init; }
            public Section? Team { get; init; }
            public string? CustomQuestion { get; init; }
            public string? CustomAnswer { get; init; }
        }

        /// <summary>One question's answer: lowercase sentiment, chip labels, optional text.</summary>
        public sealed class Section
        {
            public string Sentiment { get; init; } = string.Empty;
            public List<string> Chips { get; init; } = [];
            public string? Text { get; init; }
        }

        public static List<Sheet> Read(Stream stream)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = new ExcelPackage(stream);

            var sheets = new List<Sheet>();
            foreach (var ws in package.Workbook.Worksheets)
            {
                var sheet = ReadSheet(ws);
                if (sheet is not null) sheets.Add(sheet);
            }

            return sheets;
        }

        private static Sheet? ReadSheet(ExcelWorksheet ws)
        {
            var sheet = new Sheet { SheetName = ws.Name };
            var lastRow = ws.Dimension?.End.Row ?? 0;

            var headerRow = 0;
            for (var r = 1; r <= Math.Min(lastRow, 40); r += 1)
            {
                var label = Text(ws, r, 1);
                if (label is null) continue;

                if (label.Equals("Team name", StringComparison.OrdinalIgnoreCase))
                    sheet.TeamName = Text(ws, r, 3);
                else if (label.Equals("Working days", StringComparison.OrdinalIgnoreCase))
                    sheet.WorkingDays = Text(ws, r, 3);
                else if (label.StartsWith("First day of testing", StringComparison.OrdinalIgnoreCase))
                    sheet.StartDate = ReadDate(ws.Cells[r, 3].Value);
                else if (label.Equals("Day No.", StringComparison.OrdinalIgnoreCase))
                {
                    headerRow = r;
                    break;
                }
            }

            if (headerRow == 0)
            {
                // Not one of ours (a notes tab, a pivot). Ignored rather than failed.
                return null;
            }

            foreach (var (col, text) in ExpectedHeaders)
            {
                if (!string.Equals(Text(ws, headerRow, col), text, StringComparison.OrdinalIgnoreCase))
                {
                    sheet.Error = $"Row {headerRow} column {ColumnName(col)} should read \"{text}\". "
                                  + "The sheet is not in the template format.";
                    return sheet;
                }
            }

            for (var r = headerRow + 1; r <= lastRow; r += 1)
            {
                var row = ReadRow(ws, r, sheet.Warnings);
                if (row is not null) sheet.Rows.Add(row);
            }

            // The blank template tab: nothing to import and nothing worth reporting.
            if (string.IsNullOrWhiteSpace(sheet.TeamName) && sheet.Rows.Count == 0) return null;

            if (string.IsNullOrWhiteSpace(sheet.TeamName))
                sheet.Error = "\"Team name\" (C4) is empty.";
            else if (sheet.StartDate is null)
                sheet.Error = "\"First day of testing (Day 1)\" (C6) is empty or not a date.";
            else if (string.IsNullOrWhiteSpace(sheet.WorkingDays))
                sheet.Error = "\"Working days\" (C5) is empty.";
            else if (sheet.Rows.Count == 0)
                sheet.Error = "The sheet has no check-in rows.";

            return sheet;
        }

        private static Row? ReadRow(ExcelWorksheet ws, int r, List<string> warnings)
        {
            var dayCell = ws.Cells[r, ColDay].Value;
            var member = Text(ws, r, ColMember);

            if (dayCell is null && member is null) return null;
            if (member is not null && member.Contains("EXAMPLE", StringComparison.OrdinalIgnoreCase)) return null;

            if (!TryReadInt(dayCell, out var day) || day < 1)
            {
                warnings.Add($"Row {r}: Day No. \"{dayCell}\" is not a whole number from 1. Row skipped.");
                return null;
            }

            if (member is null)
            {
                warnings.Add($"Row {r}: Team member is empty. Row skipped.");
                return null;
            }

            var individual = ReadSection(ws, r, ColIndSentiment, ColIndChip1, ColIndText, "engine question", warnings);
            var team = ReadSection(ws, r, ColTeamSentiment, ColTeamChip1, ColTeamText, "team question", warnings);
            var customQuestion = Text(ws, r, ColCustomQuestion);
            var customAnswer = Clip(Text(ws, r, ColCustomAnswer), r, "custom answer", warnings);

            if (customQuestion is null && customAnswer is not null)
            {
                warnings.Add($"Row {r}: a custom answer has no custom question. Answer skipped.");
                customAnswer = null;
            }

            if (individual is null && team is null && customQuestion is null)
            {
                warnings.Add($"Row {r}: {member} on day {day} answered nothing. Row skipped.");
                return null;
            }

            return new Row
            {
                RowNumber = r,
                Day = day,
                Member = member,
                Individual = individual,
                Team = team,
                CustomQuestion = customQuestion,
                CustomAnswer = customAnswer
            };
        }

        private static Section? ReadSection(
            ExcelWorksheet ws, int r, int sentimentCol, int chip1Col, int textCol, string what, List<string> warnings)
        {
            var sentiment = Text(ws, r, sentimentCol);
            var chips = Enumerable.Range(chip1Col, 3)
                .Select(c => Text(ws, r, c))
                .Where(c => c is not null)
                .Select(c => c!)
                .ToList();
            var text = Clip(Text(ws, r, textCol), r, $"{what} text", warnings);

            if (sentiment is null)
            {
                if (chips.Count > 0 || text is not null)
                    warnings.Add($"Row {r}: {what} has chips or text but no Sentiment. Section skipped.");
                return null;
            }

            var known = Sentiments.FirstOrDefault(s => s.Equals(sentiment, StringComparison.OrdinalIgnoreCase));
            if (known is null)
            {
                warnings.Add($"Row {r}: {what} Sentiment \"{sentiment}\" is not Red, Orange, Yellow or Green. Section skipped.");
                return null;
            }

            return new Section { Sentiment = known.ToLowerInvariant(), Chips = chips, Text = text };
        }

        private static string? Clip(string? text, int r, string what, List<string> warnings)
        {
            if (text is null || text.Length <= MaxTextLength) return text;
            warnings.Add($"Row {r}: {what} is {text.Length} characters; cut to {MaxTextLength}.");
            return text[..MaxTextLength];
        }

        private static string? Text(ExcelWorksheet ws, int r, int c)
        {
            var value = ws.Cells[r, c].Value;
            if (value is null) return null;
            var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        private static bool TryReadInt(object? value, out int result)
        {
            result = 0;
            switch (value)
            {
                case double d when d == Math.Floor(d) && d is >= int.MinValue and <= int.MaxValue:
                    result = (int)d;
                    return true;
                case int i:
                    result = i;
                    return true;
                case string s:
                    return int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
                default:
                    return false;
            }
        }

        // The template asks for "e.g. 05/10/2026", a UK date, so a date typed as text is
        // read day first. A real date cell arrives as DateTime or as an OLE serial.
        private static DateOnly? ReadDate(object? value)
        {
            switch (value)
            {
                case DateTime dt:
                    return DateOnly.FromDateTime(dt);
                case double serial:
                    return DateOnly.FromDateTime(DateTime.FromOADate(serial));
                case string s:
                    var formats = new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy" };
                    return DateOnly.TryParseExact(s.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                        ? d
                        : null;
                default:
                    return null;
            }
        }

        private static string ColumnName(int col) => ((char)('A' + col - 1)).ToString();
    }
}
