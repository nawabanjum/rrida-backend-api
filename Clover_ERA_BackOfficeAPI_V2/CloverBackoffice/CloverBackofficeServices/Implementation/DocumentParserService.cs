using CloverBackoffice.Services.Interfaces;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OfficeOpenXml;
using System.Text;
using UglyToad.PdfPig;

namespace CloverBackoffice.Services.Implementation
{
    public class DocumentParserService : IDocumentParserService
    {
        public async Task<string> ExtractTextAsync(Stream fileStream, string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();

            return extension switch
            {
                ".pdf" => ExtractFromPdf(fileStream),
                ".docx" => ExtractFromWord(fileStream),
                ".xlsx" => ExtractFromExcel(fileStream),
                _ => throw new NotSupportedException($"File type '{extension}' is not supported. Supported types: .pdf, .docx, .xlsx")
            };
        }

        private string ExtractFromPdf(Stream stream)
        {
            var sb = new StringBuilder();

            using var document = PdfDocument.Open(stream);
            foreach (var page in document.GetPages())
            {
                sb.AppendLine(page.Text);
            }

            return sb.ToString().Trim();
        }

        private string ExtractFromWord(Stream stream)
        {
            var sb = new StringBuilder();

            using var doc = WordprocessingDocument.Open(stream, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null)
                return string.Empty;

            foreach (var paragraph in body.Elements<Paragraph>())
            {
                sb.AppendLine(paragraph.InnerText);
            }

            return sb.ToString().Trim();
        }

        private string ExtractFromExcel(Stream stream)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            var sb = new StringBuilder();

            using var package = new ExcelPackage(stream);
            foreach (var worksheet in package.Workbook.Worksheets)
            {
                sb.AppendLine($"--- {worksheet.Name} ---");

                if (worksheet.Dimension == null)
                    continue;

                for (int row = worksheet.Dimension.Start.Row; row <= worksheet.Dimension.End.Row; row++)
                {
                    var rowValues = new List<string>();
                    for (int col = worksheet.Dimension.Start.Column; col <= worksheet.Dimension.End.Column; col++)
                    {
                        var cellValue = worksheet.Cells[row, col].Text;
                        if (!string.IsNullOrWhiteSpace(cellValue))
                            rowValues.Add(cellValue);
                    }
                    if (rowValues.Count > 0)
                        sb.AppendLine(string.Join(" | ", rowValues));
                }
            }

            return sb.ToString().Trim();
        }
    }
}
