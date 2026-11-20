using CloverBackoffice.Services.Implementation;
using OfficeOpenXml;
using Xunit;

namespace CloverBackoffice.Tests.Engine
{
    public class EngineWorkbookReaderTests
    {
        // Builds a sheet in the template layout: labels in A, values in C, header on row 11.
        private static MemoryStream Workbook(Action<ExcelWorksheet> fill, string sheetName = "Platform engineering")
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = new ExcelPackage();

            var blank = package.Workbook.Worksheets.Add("Engine Test Data");
            Header(blank);

            var ws = package.Workbook.Worksheets.Add(sheetName);
            Header(ws);
            ws.Cells["C4"].Value = "Platform engineering";
            ws.Cells["C5"].Value = "Mon-Fri";
            ws.Cells["C6"].Value = new DateTime(2026, 1, 5);
            fill(ws);

            var stream = new MemoryStream(package.GetAsByteArray());
            return stream;
        }

        private static void Header(ExcelWorksheet ws)
        {
            ws.Cells["A4"].Value = "Team name";
            ws.Cells["A5"].Value = "Working days";
            ws.Cells["A6"].Value = "First day of testing (Day 1)";
            string[] headers = ["Day No.", "Team member", "Question (automatic)", "Sentiment", "Chip 1", "Chip 2", "Chip 3",
                "Text feedback (optional)", "Question (automatic)", "Sentiment", "Chip 1", "Chip 2", "Chip 3",
                "Text feedback (optional)", "Custom question", "Answer"];
            for (var i = 0; i < headers.Length; i += 1) ws.Cells[11, i + 1].Value = headers[i];
            ws.Cells["A12"].Value = 4;
            ws.Cells["B12"].Value = "Ana (EXAMPLE - ignored)";
            ws.Cells["D12"].Value = "Orange";
        }

        [Fact]
        public void Reads_team_settings_and_rows_and_ignores_the_blank_template_and_example()
        {
            using var stream = Workbook(ws =>
            {
                ws.Cells["A13"].Value = 1; ws.Cells["B13"].Value = "Hana";
                ws.Cells["D13"].Value = "Yellow"; ws.Cells["E13"].Value = "Polite thanks"; ws.Cells["F13"].Value = "Expected anyway";
                ws.Cells["J13"].Value = "green"; ws.Cells["K13"].Value = "Skills covered"; ws.Cells["N13"].Value = "Good week";
                ws.Cells["A14"].Value = 2; ws.Cells["B14"].Value = "Ife";
                ws.Cells["D14"].Value = "Red";
                ws.Cells["O14"].Value = "How was the offsite?"; ws.Cells["P14"].Value = "Too long";
            });

            var sheets = EngineWorkbookReader.Read(stream);

            var sheet = Assert.Single(sheets);
            Assert.Null(sheet.Error);
            Assert.Equal("Platform engineering", sheet.TeamName);
            Assert.Equal("Mon-Fri", sheet.WorkingDays);
            Assert.Equal(new DateOnly(2026, 1, 5), sheet.StartDate);
            Assert.Equal(2, sheet.Rows.Count);

            var first = sheet.Rows[0];
            Assert.Equal(1, first.Day);
            Assert.Equal("Hana", first.Member);
            Assert.Equal("yellow", first.Individual!.Sentiment);
            Assert.Equal(["Polite thanks", "Expected anyway"], first.Individual.Chips);
            Assert.Equal("green", first.Team!.Sentiment);
            Assert.Equal("Good week", first.Team.Text);

            var second = sheet.Rows[1];
            Assert.Null(second.Team);
            Assert.Equal("How was the offsite?", second.CustomQuestion);
            Assert.Equal("Too long", second.CustomAnswer);
        }

        [Fact]
        public void Bad_rows_become_warnings_not_failures()
        {
            using var stream = Workbook(ws =>
            {
                ws.Cells["A13"].Value = "x"; ws.Cells["B13"].Value = "Hana"; ws.Cells["D13"].Value = "Green";
                ws.Cells["A14"].Value = 1; ws.Cells["B14"].Value = "Ife"; ws.Cells["D14"].Value = "Blue";
                ws.Cells["A15"].Value = 1; ws.Cells["B15"].Value = "Jo"; ws.Cells["D15"].Value = "Green";
                ws.Cells["H15"].Value = new string('a', 400);
            });

            var sheet = Assert.Single(EngineWorkbookReader.Read(stream));

            Assert.Null(sheet.Error);
            var row = Assert.Single(sheet.Rows);
            Assert.Equal("Jo", row.Member);
            Assert.Equal(EngineWorkbookReader.MaxTextLength, row.Individual!.Text!.Length);
            Assert.Equal(4, sheet.Warnings.Count);
        }

        [Fact]
        public void A_moved_header_is_refused()
        {
            using var stream = Workbook(ws =>
            {
                ws.Cells[11, 4].Value = "Mood";
                ws.Cells["A13"].Value = 1; ws.Cells["B13"].Value = "Hana";
            });

            var sheet = Assert.Single(EngineWorkbookReader.Read(stream));
            Assert.Contains("template format", sheet.Error);
        }

        [Fact]
        public void A_text_date_is_read_day_first()
        {
            using var stream = Workbook(ws =>
            {
                ws.Cells["C6"].Value = "05/10/2026";
                ws.Cells["A13"].Value = 1; ws.Cells["B13"].Value = "Hana"; ws.Cells["D13"].Value = "Green";
            });

            var sheet = Assert.Single(EngineWorkbookReader.Read(stream));
            Assert.Equal(new DateOnly(2026, 10, 5), sheet.StartDate);
        }

        /// <summary>
        /// The client's own workbook, when RRIDA_WORKBOOK points at it. Not in the repo, so
        /// the test passes vacuously elsewhere.
        /// </summary>
        [Fact]
        public void Reads_the_client_workbook_when_present()
        {
            var path = Environment.GetEnvironmentVariable("RRIDA_WORKBOOK");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            using var stream = File.OpenRead(path);
            var sheets = EngineWorkbookReader.Read(stream);

            Assert.Equal(8, sheets.Count);
            Assert.All(sheets, s => Assert.Null(s.Error));
            Assert.All(sheets, s => Assert.Empty(s.Warnings));
            Assert.Equal(5220, sheets.Sum(s => s.Rows.Count));
            Assert.Equal(90, sheets.Single(s => s.TeamName == "Engineering leadership").Rows.Max(r => r.Day));
        }
    }
}
