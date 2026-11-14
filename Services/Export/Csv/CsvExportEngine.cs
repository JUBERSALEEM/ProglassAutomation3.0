using CsvHelper;
using ProGlassAutomation.Services.Security;
using ProGlassAutomation.Services.Common;
// CsvConfiguration and InjectionOptions live in CsvHelper.Configuration,
// not CsvHelper — required by SafeCsvConfig below (audit fix C-3).
using CsvHelper.Configuration;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.FileSystem;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Export.Csv
{
    /// <summary>
    /// The single CSV export engine used by every module.
    ///
    /// Responsibilities:
    /// - Opens a StreamWriter with UTF-8 BOM (Excel-friendly)
    /// - Creates a CsvWriter with invariant culture
    /// - Delegates all row writing to layout.WriteRows()
    /// - Shows SaveFileDialog when requested
    /// - Launches file after save when requested
    /// - Wraps all errors into ExportResult
    ///
    /// UTF-8 BOM is critical — without it Excel misinterprets Unicode
    /// characters like Arabic company names or the AED symbol.
    /// </summary>
    public class CsvExportEngine
    {
        private readonly SaveFileDialogService _dialogService;

        // ══════════════════════════════════════════════════════════════
        //  CSV FORMULA-INJECTION PROTECTION  (audit finding C-3)
        //
        //  THE PROBLEM THIS SOLVES
        //  Exported cells carry free text that originates from outside
        //  the app: customer names, project names, driver names, remarks.
        //  Excel and Google Sheets treat a cell beginning with = + - @
        //  as a FORMULA, not text. A customer saved as
        //        =cmd|'/c calc'!A0
        //  becomes an executable formula the moment the accountant opens
        //  the exported file. That is CSV injection, and it executes on
        //  the recipient's machine, not ours. CsvHelper ships with the
        //  protection DISABLED by default (InjectionOptions.None), and it
        //  was never enabled here.
        //
        //  WHY Escape RATHER THAN Strip
        //  Strip deletes the offending character, silently corrupting
        //  real data — a project genuinely named "-Phase 2" would export
        //  as "Phase 2". Escape prefixes the OWASP-recommended apostrophe
        //  so the cell renders as inert text with its content intact.
        //  Accounting data must never be silently altered by an export.
        //
        //  WHY THE CHARACTER LIST IS NARROWED
        //  CsvHelper's default injection set is  = @ + - \t \r . Because
        //  '-' is in that set, the library applies the same treatment to
        //  NUMERIC fields, so -1234.56 exports as '-1234.56 and lands in
        //  Excel as text rather than a negative number (JoshClose/
        //  CsvHelper issue #2126). This export feeds financial
        //  reconciliation, so breaking negative amounts would trade a
        //  security bug for a correctness bug.
        //
        //  '=' '@' and '+' are the formula triggers and are never the
        //  first character of a number this app writes, so escaping them
        //  is lossless. '-' is deliberately excluded: a leading '-' alone
        //  cannot start a formula in Excel unless followed by a formula
        //  expression, and excluding it keeps negative currency correct.
        //  \t and \r are excluded for the same data-fidelity reason and
        //  are already handled by RFC4180 quoting.
        // ══════════════════════════════════════════════════════════════
        private static readonly CsvConfiguration SafeCsvConfig =
            new(CultureInfo.InvariantCulture)
            {
                InjectionOptions = InjectionOptions.Escape,
                InjectionCharacters = new[] { '=', '@', '+' },
                InjectionEscapeCharacter = '\''
            };

        public CsvExportEngine()
        {
            _dialogService = new SaveFileDialogService();
        }

        public async Task<ExportResult> ExportAsync(
            ICsvLayout layout,
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
                                   ?? ExportFileNamer.Build(layout, ExportFormat.Csv);
                path = _dialogService.PromptSavePath(
                    layout.DocumentTitle,
                    suggested,
                    "CSV File (*.csv)|*.csv");
                if (string.IsNullOrEmpty(path))
                    return ExportResult.Cancel();
            }

            if (string.IsNullOrWhiteSpace(path))
                return ExportResult.Fail("No output path provided.");

            // ── 2. Write file ─────────────────────────────────────────
            try
            {
                await Task.Run(() =>
                {
                    // Ensure directory exists
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    // UTF-8 with BOM — critical for Excel to render
                    // Unicode chars like Arabic customer names correctly
                    using var stream = new StreamWriter(
                        path,
                        append: false,
                        encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

                    // SafeCsvConfig enables formula-injection escaping —
                    // see the comment on the field for why these exact
                    // settings. Previously this passed a bare
                    // CultureInfo.InvariantCulture, which leaves
                    // InjectionOptions at None (unprotected).
                    using var csv = new CsvWriter(stream, SafeCsvConfig);

                    // Module writes rows here
                    layout.WriteRows(csv);
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
                        ProGlassAutomation.Services.Logging.AppLogService.Warning("[CsvExportEngine] Viewer launch failed after save: " + ex.Message);
                    }
                }

                return ExportResult.Ok(path);
            }
            catch (Exception ex)
            {
                return ExportResult.Fail(
                    $"CSV export failed: {ex.Message}", ex);
            }
        }
    }
}