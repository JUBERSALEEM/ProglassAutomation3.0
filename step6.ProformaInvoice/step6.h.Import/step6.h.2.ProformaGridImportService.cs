using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;

namespace ProGlassAutomation.Services.Proforma
{
    /// <summary>Reads supported cut-list files without locking workbooks open in Excel.</summary>
    public static class ProformaGridImportService
    {
        public const string FileDialogFilter =
            "Cut list (*.xlsx;*.csv;*.txt;*.json)|*.xlsx;*.csv;*.txt;*.json|" +
            "Excel workbook (*.xlsx)|*.xlsx|CSV / text (*.csv;*.txt)|*.csv;*.txt|" +
            "Scanner JSON (*.json)|*.json|All files (*.*)|*.*";

        public static async Task<ProformaGridImportResult> ImportFileAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new ProformaGridImportResult { Warning = "File not found." };

            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".xlsx") return ReadExcel(path);
            if (extension == ".json")
                return ProformaGridImportParser.ParseJsonArray(await ReadTextSharedAsync(path).ConfigureAwait(false));
            if (extension is ".csv" or ".txt")
                return ProformaGridImportParser.ParseDelimitedText(await ReadTextSharedAsync(path).ConfigureAwait(false));
            return new ProformaGridImportResult
            {
                Warning = "This V3 build imports Excel, CSV, text and scanner JSON. PDF/photo scanning is not available because V3 has no Gemini file-AI service."
            };
        }

        public static ProformaGridImportResult ReadExcel(string path)
        {
            using var memory = new MemoryStream(ReadBytesShared(path));
            using var workbook = new XLWorkbook(memory);
            IXLWorksheet? sheet = workbook.Worksheets.FirstOrDefault();
            IXLRange? used = sheet?.RangeUsed();
            if (used == null)
                return new ProformaGridImportResult { Warning = "The Excel worksheet is empty." };

            var table = new List<IReadOnlyList<string>>();
            int first = used.FirstColumn().ColumnNumber();
            int last = used.LastColumn().ColumnNumber();
            foreach (var row in used.RowsUsed())
            {
                var cells = new List<string>();
                foreach (IXLCell cell in row.Cells(first, last))
                    cells.Add(cell.GetFormattedString().Trim());
                table.Add(cells);
            }
            return ProformaGridImportParser.ParseTable(table);
        }

        private static byte[] ReadBytesShared(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        private static async Task<string> ReadTextSharedAsync(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }
    }
}