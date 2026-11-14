// Live Step 5 cut-list import / export.
// Excel + CSV are deterministic. PDF/image scan uses Gemini when configured.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.Win32;
using ProGlassAutomation.Services.AI.AIService;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.Csv;
using ProGlassAutomation.Services.Export.Excel;
using ProGlassAutomation.Services.Export.Pdf;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid;

namespace ProGlassAutomation.Services.ProformaInvoice.Step5Grid
{
    public static class CutListGridIo
    {
        public const string ImportFilter =
            "Cut list (*.xlsx;*.csv;*.txt;*.pdf;*.png;*.jpg;*.jpeg;*.webp)|*.xlsx;*.csv;*.txt;*.pdf;*.png;*.jpg;*.jpeg;*.webp|" +
            "Excel (*.xlsx)|*.xlsx|" +
            "CSV / text (*.csv;*.txt)|*.csv;*.txt|" +
            "PDF / image (*.pdf;*.png;*.jpg;*.jpeg;*.webp)|*.pdf;*.png;*.jpg;*.jpeg;*.webp|" +
            "All files (*.*)|*.*";

        public static string? PromptOpenPath()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import cut list",
                Filter = ImportFilter,
                CheckFileExists = true,
                Multiselect = false
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public static async Task<CutListImportResult> ImportFileAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new CutListImportResult { Warning = "File not found." };

            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".xlsx" => ReadExcel(path),
                ".csv" or ".txt" => CutListImportParser.ParseDelimitedText(
                    await ReadAllTextSharedAsync(path)),
                ".pdf" or ".png" or ".jpg" or ".jpeg" or ".webp" =>
                    await ScanDocumentAsync(path, ext),
                _ => CutListImportParser.ParseDelimitedText(
                    await ReadAllTextSharedAsync(path))
            };
        }

        /// <summary>
        /// Reads a whole file even while it is open in Excel. Excel holds a
        /// write-lock on open workbooks, so the default FileStream/File.Read*
        /// (FileShare.Read) throws IOException. Opening with FileShare.ReadWrite
        /// lets us read the last-saved bytes without asking the user to close it.
        /// </summary>
        private static byte[] ReadAllBytesShared(string path)
        {
            using var fs = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            return ms.ToArray();
        }

        private static async Task<string> ReadAllTextSharedAsync(string path)
        {
            using var fs = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            return await sr.ReadToEndAsync().ConfigureAwait(false);
        }

        public static CutListImportResult ReadExcel(string path)
        {
            // Open from memory so a workbook that is open in Excel can still
            // be imported (we read its last-saved bytes with FileShare.ReadWrite).
            using var wb = new XLWorkbook(new MemoryStream(ReadAllBytesShared(path)));
            var ws = wb.Worksheets.First();
            var used = ws.RangeUsed();
            if (used == null)
                return new CutListImportResult { Warning = "Excel sheet is empty." };

            var table = new List<IReadOnlyList<string>>();
            foreach (var row in used.RowsUsed())
            {
                var cells = new List<string>();
                foreach (var cell in row.Cells(used.FirstColumn().ColumnNumber(), used.LastColumn().ColumnNumber()))
                    cells.Add(cell.GetFormattedString()?.Trim() ?? "");
                table.Add(cells);
            }

            return CutListImportParser.ParseTable(table);
        }

        public static async Task<ExportResult> ExportExcelAsync(
            IReadOnlyList<SpecGroup> groups, string documentId,
            string? customer = null, string? project = null,
            string? outputPath = null)
        {
            var layout = new CutListExcelLayout(groups, documentId, customer, project);
            return await new ExcelExportEngine().ExportAsync(
                layout, RequestFor(outputPath));
        }

        public static async Task<ExportResult> ExportCsvAsync(
            IReadOnlyList<SpecGroup> groups, string documentId,
            string? customer = null, string? project = null,
            string? outputPath = null)
        {
            var layout = new CutListCsvLayout(groups, documentId, customer, project);
            return await new CsvExportEngine().ExportAsync(
                layout, RequestFor(outputPath));
        }

        public static async Task<ExportResult> ExportPdfAsync(
            IReadOnlyList<SpecGroup> groups, string documentId,
            string? customer = null, string? project = null,
            string? outputPath = null)
        {
            var layout = new CutListPdfLayout(groups, documentId, customer, project);
            return await new PdfExportEngine().ExportAsync(
                layout, RequestFor(outputPath));
        }

        /// <summary>
        /// Interactive save-dialog request by default; a direct staged
        /// write (no dialog, no auto-open) when Sharing hands a temp path.
        /// Additive Stage 3 seam — existing callers are unaffected.
        /// </summary>
        private static ExportRequest RequestFor(string? outputPath)
        {
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                return new ExportRequest
                {
                    OutputPath = outputPath,
                    PromptForLocation = false,
                    OpenAfterSave = false
                };
            }
            return new ExportRequest
            {
                PromptForLocation = true,
                OpenAfterSave = true
            };
        }

        private static async Task<CutListImportResult> ScanDocumentAsync(string path, string ext)
        {
            if (App.Ai is not GeminiAiService gemini || !gemini.IsConfigured)
            {
                return new CutListImportResult
                {
                    Warning = "PDF / photo import needs Gemini in Settings → AI. " +
                              "Excel and CSV import work without it."
                };
            }

            byte[] bytes = ReadAllBytesShared(path);
            string mime = ext switch
            {
                ".pdf" => "application/pdf",
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };

            const string system =
                "You extract glass cut-list rows from factory documents. " +
                "Return ONLY a JSON array. Each object: " +
                "{\"ref\":\"\",\"type\":\"\",\"w1\":0,\"h1\":0,\"w2\":0,\"h2\":0,\"qty\":1}. " +
                "Set type to the glass type when stated: SGU, DGU, LAM or DGU+LAM " +
                "(empty string when unknown). Sizes are millimetres. qty defaults " +
                "to 1. Skip headers and totals.";

            var response = await gemini.AskWithFileAsync(
                system,
                "Extract every glass panel row from this cut list.",
                mime,
                bytes);

            if (!response.Success)
            {
                return new CutListImportResult
                {
                    Warning = response.ErrorMessage ?? "Scanner failed."
                };
            }

            var parsed = CutListImportParser.ParseJsonArray(response.Text);
            parsed.Layout = "scan";
            return parsed;
        }

    }
}
