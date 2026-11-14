using ClosedXML.Excel;
using ProGlassAutomation.Services.Security;
using ProGlassAutomation.Services.Common;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.FileSystem;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Export.Excel
{
    /// <summary>
    /// The single Excel export engine used by every module.
    ///
    /// Responsibilities:
    /// - Creates a fresh IXLWorkbook
    /// - Delegates all content to layout.RenderWorkbook()
    /// - Shows SaveFileDialog when requested
    /// - Writes .xlsx bytes to disk
    /// - Launches file with default app after save when requested
    /// - Wraps all errors into ExportResult
    ///
    /// Modules never call ClosedXML directly for file I/O — only for
    /// worksheet + cell manipulation inside RenderWorkbook.
    /// </summary>
    public class ExcelExportEngine
    {
        private readonly SaveFileDialogService _dialogService;

        public ExcelExportEngine()
        {
            _dialogService = new SaveFileDialogService();
        }

        public async Task<ExportResult> ExportAsync(
            IExcelLayout layout,
            ExportRequest request)
        {
            if (layout == null)
                return ExportResult.Fail("Layout is null.");
            if (request == null)
                return ExportResult.Fail("Request is null.");

            // ── 1. Resolve output path ────────────────────────────────
            string? path = request.OutputPath;
            if (request.PromptForLocation)
            {
                string suggested = request.SuggestedFileName
                                   ?? ExportFileNamer.Build(layout, ExportFormat.Excel);
                path = _dialogService.PromptSavePath(
                    layout.DocumentTitle,
                    suggested,
                    "Excel Workbook (*.xlsx)|*.xlsx");
                if (string.IsNullOrEmpty(path))
                    return ExportResult.Cancel();
            }

            if (string.IsNullOrWhiteSpace(path))
                return ExportResult.Fail("No output path provided.");

            // ── 2. Build workbook ─────────────────────────────────────
            try
            {
                await Task.Run(() =>
                {
                    using var workbook = new XLWorkbook();

                    // Set workbook-wide font defaults
                    workbook.Style.Font.FontName = WorkbookStyle.FontFamily;
                    workbook.Style.Font.FontSize = WorkbookStyle.FontSizeBody;

                    // Module renders here
                    layout.RenderWorkbook(workbook);

                    // Ensure directory exists
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    workbook.SaveAs(path);
                });

                // ── 3. Open after save if requested ───────────────────
                if (request.OpenAfterSave && File.Exists(path))
                {
                    try
                    {
                                    ProcessStartGuard.TryOpenFile(path, CorrelationContext.NewId());
                    }
                    catch (System.Exception ex)
                    {
                        // File saved OK — non-fatal if launcher fails
                        ProGlassAutomation.Services.Logging.AppLogService.Warning("[ExcelExportEngine] Viewer launch failed after save: " + ex.Message);
                    }
                }

                return ExportResult.Ok(path);
            }
            catch (Exception ex)
            {
                return ExportResult.Fail(
                    $"Excel export failed: {ex.Message}", ex);
            }
        }
    }
}