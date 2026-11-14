using QuestPDF.Fluent;
using ProGlassAutomation.Services.Security;
using ProGlassAutomation.Services.Common;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.FileSystem;
using ProGlassAutomation.Services.Export.Pdf.Renderers;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Xps;
using System.Windows.Xps.Packaging;
using ProGlassAutomation.Services.Dialogs;

namespace ProGlassAutomation.Services.Export.Pdf
{
    /// <summary>
    /// The single PDF export engine used by every module.
    ///
    /// Responsibilities:
    /// - QuestPDF page setup (size, orientation, margins)
    /// - Renders shared letterhead on PAGE 1 ONLY (Phase 9.5a-r4)
    /// - Renders shared footer on EVERY page with DocumentId + page X of Y
    /// - Delegates body rendering to layout.RenderContent()
    /// - Shows SaveFileDialog when requested
    /// - Writes bytes to disk
    /// - Launches file after save when requested
    /// - Wraps all errors into ExportResult
    /// - PrintAsync: sends layout to physical printer via PrintDialog (Phase 9.5b)
    ///
    /// Modules never call QuestPDF directly for page setup — only for
    /// content rendering inside RenderContent.
    ///
    /// Phase 9.5a-r4: Letterhead now uses .ShowOnce() so it appears only
    /// on page 1. Continuation pages get more vertical space for actual
    /// content, and self-identify via the DocumentId in the footer.
    /// This behavior applies automatically to every module — no per-layout
    /// opt-in needed.
    ///
    /// Phase 9.5b: Added PrintAsync(IPdfLayout, string) for physical printer
    /// support. Generates a FixedDocument from QuestPDF bytes via XPS pipeline,
    /// shows PrintDialog, and sends to printer. All future modules get print
    /// capability for free by calling this method.
    /// </summary>
    public class PdfExportEngine
    {
        private readonly SaveFileDialogService _dialogService;

        public PdfExportEngine()
        {
            _dialogService = new SaveFileDialogService();

            // QuestPDF community license — set once, safe to set multiple times
            QuestPDF.Settings.License = LicenseType.Community;

            // 2026-09-12 — Detailed layout diagnostics.
            // When a document throws "The provided document content contains
            // conflicting size constraints", QuestPDF only points at the whole
            // document by default. With this flag the exception message also
            // includes the offending element's source-code location and its
            // measured layout subtree, so a failing export becomes
            // self-diagnosing instead of a mystery. It only enriches the
            // error path — it does not change successful output.
            QuestPDF.Settings.EnableDebugging = true;
        }

        // ════════════════════════════════════════════════════════════════════
        //  EXPORT TO FILE
        // ════════════════════════════════════════════════════════════════════

        public async Task<ExportResult> ExportAsync(
            IPdfLayout layout,
            ExportRequest request)
        {
            if (layout == null)
                return ExportResult.Fail("Layout is null.");
            if (request == null)
                return ExportResult.Fail("Request is null.");

            // ── 1. Resolve output path ────────────────────────────────────
            string? path = request.OutputPath;
            if (request.PromptForLocation)
            {
                string suggested = request.SuggestedFileName
                                   ?? ExportFileNamer.Build(layout, ExportFormat.Pdf);
                path = _dialogService.PromptSavePath(
                    layout.DocumentTitle,
                    suggested,
                    "PDF Files (*.pdf)|*.pdf");
                if (string.IsNullOrEmpty(path))
                    return ExportResult.Cancel();
            }

            if (string.IsNullOrWhiteSpace(path))
                return ExportResult.Fail("No output path provided.");

            // ── 2. Build + write document ─────────────────────────────────
            try
            {
                var document = BuildQuestPdfDocument(layout);

                await Task.Run(() =>
                {
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory) &&
                        !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    document.GeneratePdf(path);
                });

                // ── 3. Open after save if requested ──────────────────────
                if (request.OpenAfterSave && File.Exists(path))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(
                            new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = path,
                                UseShellExecute = true
                            });
                    }
                    catch (System.Exception ex)
                    {
                        // File saved OK — couldn't launch viewer. Non-fatal.
                        ProGlassAutomation.Services.Logging.AppLogService.Warning("[PdfExportEngine] Viewer launch failed after save: " + ex.Message);
                    }
                }

                return ExportResult.Ok(path);
            }
            catch (Exception ex)
            {
                return ExportResult.Fail(
                    $"PDF export failed: {ex.Message}", ex);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  PRINT TO PHYSICAL PRINTER  (Phase 9.5b — Q2)
        //
        //  Strategy:
        //    1. Generate PDF bytes in-memory via QuestPDF
        //    2. Write to a temp file
        //    3. Show PrintDialog — if user cancels, delete temp + return
        //    4. Convert PDF bytes to XPS FixedDocument via image-per-page
        //       pipeline (WPF's native print path; no third-party PDF renderer)
        //    5. Send FixedDocument.DocumentPaginator to printer
        //    6. Delete temp file
        //
        //  NOTE: WPF has no built-in PDF-to-FixedDocument bridge.
        //  The safe cross-platform approach is to generate to a temp PDF
        //  and then use Process.Start with the printer as the verb, which
        //  delegates to the system's registered PDF handler.
        //  This avoids taking a dependency on a PDF rendering library.
        //
        //  Must be called from the UI thread (PrintDialog is a WPF dialog).
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Shows a PrintDialog and sends the layout to the selected physical
        /// printer. If the user cancels the dialog, returns silently.
        /// Errors are surfaced via MessageBox (caller does not need to handle).
        /// </summary>
        /// <param name="layout">The IPdfLayout to render.</param>
        /// <param name="documentName">
        ///     Job name shown in the Windows print queue,
        ///     e.g. "Proforma Invoice - PI-2026-001".
        /// </param>
        public async Task PrintAsync(IPdfLayout layout, string documentName)
        {
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));

            // ── 1. Show PrintDialog first — bail early if cancelled ───────
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true) return;

            // ── 2. Generate PDF bytes to a temp file ─────────────────────
            string tempPath = Path.Combine(
                Path.GetTempPath(),
                $"ProGlass_Print_{Guid.NewGuid():N}.pdf");

            try
            {
                var document = BuildQuestPdfDocument(layout);

                await Task.Run(() => document.GeneratePdf(tempPath));

                // ── 3. Print via shell verb ───────────────────────────────
                //  Use the system's registered PDF viewer to handle printing.
                //  This is the most reliable cross-machine approach in WPF
                //  without a PDF rendering library.
                //
                //  "print" verb sends to the DEFAULT printer without a dialog.
                //  Since we already showed PrintDialog above to let the user
                //  pick the printer, we use "printto" with the printer name
                //  so the job lands on the printer they selected.
                await Task.Run(() =>
                {
                    try
                    {
                        // printto: sends to a specific printer by name
                        // Format: printto "<printer name>"
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = tempPath,
                            Verb = "printto",
                            Arguments = $"\"{dialog.PrintQueue.FullName}\"",
                            UseShellExecute = true,
                            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                        };
                        var proc = System.Diagnostics.Process.Start(psi);
                        // Give the PDF viewer time to spool before we delete
                        proc?.WaitForExit(15_000);
                    }
                    catch
                    {
                        // printto verb not supported by installed PDF viewer —
                        // fall back to opening the file so user can print manually
                        System.Diagnostics.Process.Start(
                            new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = tempPath,
                                UseShellExecute = true
                            });
                    }
                });
            }
            catch (Exception ex)
            {
                Prompts.Show($"Print failed:\n\n{ex.Message}",
                    "Print Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                // ── 4. Clean up temp file ─────────────────────────────────
                // Small delay — PDF viewer may still be reading the file
                await Task.Delay(20_000).ContinueWith(_ =>
                {
                    try
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                    catch (System.Exception ex)
                    {
                        // Non-fatal — temp folder will clean up eventually
                        ProGlassAutomation.Services.Logging.AppLogService.Debug("[PdfExportEngine] Temp-file cleanup failed: " + ex.Message);
                    }
                });
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  SHARED DOCUMENT BUILDER (used by both ExportAsync + PrintAsync)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds the QuestPDF IDocument from the layout.
        /// Extracted so both ExportAsync and PrintAsync share the same
        /// document construction logic — no duplication.
        /// </summary>
        private static IDocument BuildQuestPdfDocument(IPdfLayout layout)
        {
            PdfThemeCatalog.ApplySaved();
            var pageLayout = layout.PageLayout ?? PageLayout.A4Portrait;
            string documentId = layout.DocumentId ?? string.Empty;
            // 2026-09-11: the footer identifies the DOCUMENT, never the
            // skin. Printing the active layout/theme name beside the id
            // read as a leaked debug string on customer-facing documents
            // (owner-reported issue 1) — the operator already sees which
            // style is active on the Step 7 switcher.
            string footerId = documentId;

            return Document.Create(container =>
            {
                // Reset runs inside the composition delegate, not before it:
                // QuestPDF executes this lambda on whichever thread calls
                // GeneratePdf (ExportAsync/PrintAsync both wrap it in
                // Task.Run). Resetting out here would clear a counter that
                // belongs to a different thread.
                PdfChrome.Reset();

                var metrics = DocumentStyle.Metrics;
                float marginMm = metrics.Theme.MarginMm;

                container.Page(page =>
                {
                    page.Size(ResolvePageSize(pageLayout));
                    page.Margin(marginMm, Unit.Millimetre);
                    page.DefaultTextStyle(t => t
                        .FontFamily(DocumentStyle.FontFamily)
                        .FontSize(DocumentStyle.FontSizeBody)
                        .FontColor(DocumentStyle.ColorText));

                    if (layout.Letterhead != null)
                    {
                        var letterhead = layout.Letterhead;
                        // Module-owned letterhead (e.g. the PI WYSIWYG
                        // preview-parity design) wins; everything else keeps
                        // the shared themed chrome.
                        var style = layout.LetterheadStyle;
                        page.Header().Column(h =>
                        {
                            h.Item().ShowOnce().Element(c =>
                            {
                                if (style != null) style.Render(c, letterhead);
                                else LetterheadRenderer.Render(c, letterhead);
                            });
                            h.Item().SkipOnce().Element(c =>
                            {
                                if (style != null) style.RenderContinuation(c, letterhead);
                                else LetterheadRenderer.RenderContinuation(c, letterhead);
                            });
                        });
                    }

                    page.Content().PaddingTop(metrics.BlockGap).Column(col =>
                    {
                        col.Spacing(metrics.SectionGap);
                        layout.RenderContent(col);
                    });

                    page.Footer().Element(f =>
                        FooterRenderer.Render(f, footerId));
                });
            });
        }

        // ════════════════════════════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════════════════════════════

        private static QuestPDF.Helpers.PageSize ResolvePageSize(
            PageLayout layout)
        {
            var basePage = layout.Size switch
            {
                Pdf.PageSize.A3 => PageSizes.A3,
                Pdf.PageSize.Letter => PageSizes.Letter,
                _ => PageSizes.A4
            };

            return layout.Orientation == PageOrientation.Landscape
                ? basePage.Landscape()
                : basePage.Portrait();
        }
    }
}