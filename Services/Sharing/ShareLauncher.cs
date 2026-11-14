// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Sharing/ShareLauncher.cs
// PROJECT: ProGlassAutomation
// MODULE: Sharing Center — module integration glue (Stage 3)
//
// The ONLY code modules add to become shareable (plus one button):
//
//   Pattern A — engine-based export (PI/JO/DN/reports/specs/…):
//     await ShareLauncher.GenerateAndShareAsync(
//         context,
//         folder => MyExistingExportMethodTo(folder),  // same exporter,
//         _dialogs);                                    // temp OutputPath
//
//   Pattern B — the module already has a file path (TI bytes saved,
//   statement PDFs, a just-exported SavedPath):
//     if (ShareLauncher.AttachFile(context, path, _dialogs))
//         ShareLauncher.OpenComposer(context, _dialogs);
//
// Generation is NEVER duplicated: Pattern A runs the module's existing
// exporter once, into the share temp folder, with no save dialog and no
// auto-open; the temp file IS the artifact. Pattern B wraps a file that
// already exists. The composer's honesty rules, providers, templates and
// audit apply identically to both.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Dialogs;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.Csv;
using ProGlassAutomation.Services.Export.Excel;
using ProGlassAutomation.Services.Export.FileSystem;
using ProGlassAutomation.Services.Export.Pdf;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Utilities;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Sharing
{
    public static class ShareLauncher
    {
        /// <summary>
        /// Runs the module's existing exporter into a fresh share-temp
        /// folder, attaches the result, and opens the composer. The
        /// <paramref name="generateToFolder"/> callback receives the folder
        /// and returns the generated file path (or null/"" when it already
        /// reported its own failure). Never throws.
        /// </summary>
        public static async Task<bool> GenerateAndShareAsync(
            ShareContext context,
            Func<string, Task<string?>> generateToFolder,
            IDialogService? dialogs = null)
        {
            if (context == null || generateToFolder == null)
                return false;

            string folder = ShareTempFiles.CreateShareFolder();
            if (folder.Length == 0)
            {
                dialogs?.Error(
                    "Could not prepare the share (temporary folder unavailable).",
                    "Share");
                return false;
            }

            string? path = null;
            try
            {
                path = await generateToFolder(folder).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogSharingFailure("generate", context, ex);
                dialogs?.Error(
                    $"Could not generate the file to share: {UserFacingDetail(ex)}",
                    "Share");
                return false;
            }

            if (string.IsNullOrWhiteSpace(path))
                return false; // module reported its own reason

            if (!AttachFile(context, path, dialogs))
                return false;

            return OpenComposer(context, dialogs);
        }

        /// <summary>
        /// Validates an existing file and attaches it to the context.
        /// Shows the reason on failure. Never throws.
        /// </summary>
        public static bool AttachFile(
            ShareContext context,
            string? filePath,
            IDialogService? dialogs = null)
        {
            if (context == null)
                return false;

            bool temp = IsInsideShareTemp(filePath);
            var artifact = ShareableArtifact.FromFile(
                filePath,
                context.DocumentTypeKey ?? "",
                context.DocumentNumber ?? "",
                context.SourceModule ?? "",
                temp,
                out string? reason);

            if (artifact == null)
            {
                dialogs?.Error(reason ?? "The file cannot be shared.", "Share");
                return false;
            }

            context.Artifacts.Add(artifact);
            return true;
        }

        /// <summary>
        /// Opens the share composer for a context that already carries
        /// artifacts. Refuses empty contexts with a reason. Never throws.
        /// Returns true when at least one share succeeded.
        /// </summary>
        public static bool OpenComposer(
            ShareContext context,
            IDialogService? dialogs = null)
        {
            if (context == null)
            {
                AppLogService.Debug("[Sharing.Launcher] composer not opened: context was null.");
                return false;
            }

            if (context.Artifacts.Count == 0)
            {
                dialogs?.Error("No file was generated to share.", "Share");
                return false;
            }

            // The composer is a WPF Window: constructing and showing it
            // REQUIRES the UI (STA) thread. Async share flows commonly
            // resume this continuation on a thread-pool (MTA) thread after
            // an awaited export (ConfigureAwait(false)) — showing a window
            // there throws "The calling thread must be STA…". Marshal the
            // whole open-and-show onto the UI dispatcher so it works from
            // any thread. When already on the UI thread this runs inline;
            // off-thread callers block on Invoke, preserving modal
            // semantics and the return value.
            return RunOnUi(() => OpenComposerCore(context, dialogs));
        }

        private static bool OpenComposerCore(
            ShareContext context, IDialogService? dialogs)
        {
            try
            {
                var service = dialogs
                    ?? App.Dialogs
                    ?? throw new InvalidOperationException("No dialog service.");
                bool? result = service.ShowDialog<
                    Views.Sharing.ShareComposerWindow>(context);
                return result == true;
            }
            catch (Exception ex)
            {
                // Industrial rule: never show a wrapper's message
                // ("Exception has been thrown by the target of an
                // invocation" explains nothing). Unwrap the full chain so
                // the dialog names the REAL cause, and log type + stack.
                LogSharingFailure("open-composer", context, ex);
                try
                {
                    var report = dialogs ?? App.Dialogs;
                    report?.Error(
                        $"Could not open the share window: {UserFacingDetail(ex)}",
                        "Share");
                }
                catch (Exception dialogEx)
                {
                    AppLogService.Debug(
                        "[Sharing.Launcher] error dialog failed: "
                        + ExceptionMessages.Deep(dialogEx));
                }
                return false;
            }
        }

        /// <summary>
        /// Runs <paramref name="work"/> on the WPF UI (STA) thread and
        /// returns its result. Executes inline when already on the UI
        /// thread, or when no Application/Dispatcher exists (headless test
        /// host); otherwise blocks on Dispatcher.Invoke. Never throws:
        /// a marshalling failure is logged and reported as false.
        /// </summary>
        private static bool RunOnUi(Func<bool> work)
        {
            try
            {
                var disp = System.Windows.Application.Current?.Dispatcher;
                if (disp == null || disp.CheckAccess())
                    return work();
                return disp.Invoke(work);
            }
            catch (Exception ex)
            {
                LogSharingFailure("open-composer-dispatch", null, ex);
                return false;
            }
        }

        /// <summary>
        /// Resolves a sharing rule's default format to an export format
        /// for staged regeneration. Central mapping so view-models never
        /// hand-switch the rule text. Unknown text means PDF.
        /// </summary>
        public static ExportFormat RuleFormat(string documentTypeKey)
        {
            try
            {
                string format = (App.Config?.Sharing
                    ?.RuleFor(documentTypeKey ?? "")?.DefaultFormat
                    ?? "").Trim().ToLowerInvariant();
                return format switch
                {
                    "excel" => ExportFormat.Excel,
                    "csv" => ExportFormat.Csv,
                    _ => ExportFormat.Pdf
                };
            }
            catch
            {
                return ExportFormat.Pdf;
            }
        }

        /// <summary>
        /// Runs a module-built layout through the matching export engine
        /// into <paramref name="folder"/> (professional file name, no
        /// dialogs, no auto-open). Returns the engine's ExportResult so
        /// callers read SavedPath/ErrorMessage from one shape. Never
        /// throws — failures come back as ExportResult.Fail.
        /// </summary>
        public static async Task<ExportResult> ExportLayoutToFolderAsync(
            IDocumentLayout layout,
            ExportFormat format,
            string folder)
        {
            try
            {
                if (layout == null)
                    return ExportResult.Fail("Nothing to share.");

                string name = ExportFileNamer.Build(layout, format);
                var request = new ExportRequest
                {
                    OutputPath = Path.Combine(folder, name),
                    PromptForLocation = false,
                    OpenAfterSave = false
                };

                ExportResult result = format switch
                {
                    ExportFormat.Excel => await new ExcelExportEngine()
                        .ExportAsync((IExcelLayout)layout, request)
                        .ConfigureAwait(false),
                    ExportFormat.Csv => await new CsvExportEngine()
                        .ExportAsync((ICsvLayout)layout, request)
                        .ConfigureAwait(false),
                    _ => await new PdfExportEngine()
                        .ExportAsync((IPdfLayout)layout, request)
                        .ConfigureAwait(false)
                };

                if (!result.Success || string.IsNullOrWhiteSpace(result.SavedPath))
                    return ExportResult.Fail(
                        result.ErrorMessage ?? "Export failed.");
                return result;
            }
            catch (Exception ex)
            {
                LogSharingFailure("staged-export", null, ex);
                return ExportResult.Fail(UserFacingDetail(ex));
            }
        }

        /// <summary>
        /// User-facing failure detail: the FULL inner-exception chain
        /// ("outer -> inner -> root cause"), never a wrapper's empty
        /// message. Falls back to the exception type name. Never throws.
        /// </summary>
        private static string UserFacingDetail(Exception ex)
        {
            try
            {
                string deep = ExceptionMessages.Deep(ex);
                if (!string.IsNullOrWhiteSpace(deep))
                    return deep;
                return ex?.GetType().Name ?? "Unknown error";
            }
            catch
            {
                return "Unknown error";
            }
        }

        /// <summary>
        /// Logs a launcher failure with the full exception chain (type +
        /// message per level) and the stack trace, capped so the log row
        /// stays readable. Never throws.
        /// </summary>
        private static void LogSharingFailure(
            string operation,
            ShareContext? context,
            Exception ex,
            [System.Runtime.CompilerServices.CallerMemberName] string method = "")
        {
            try
            {
                var chain = new System.Text.StringBuilder();
                for (Exception? e = ex; e != null; e = e.InnerException)
                {
                    if (chain.Length > 0)
                        chain.Append(" -> ");
                    chain.Append(e.GetType().Name);
                    string message = (e.Message ?? "")
                        .Replace("\r", " ").Replace("\n", " ").Trim();
                    if (message.Length > 0)
                    {
                        chain.Append(": ");
                        chain.Append(message);
                    }
                    if (chain.Length > 1200)
                        break;
                }

                string stack = (ex?.StackTrace ?? "")
                    .Replace("\r", " ").Replace("\n", " ").Trim();
                if (stack.Length > 1500)
                    stack = stack.Substring(0, 1500) + "...";

                string artifacts = context == null ? "no-context"
                    : context.Artifacts == null ? "artifacts-null"
                    : context.Artifacts.Count + "-artifact(s)";

                AppLogService.Warning(
                    $"[Sharing.Launcher] {operation} failed ({artifacts}): {chain}"
                    + (stack.Length > 0 ? $" | Stack: {stack}" : ""),
                    new LogContext
                    {
                        Module = "Sharing",
                        Class = nameof(ShareLauncher),
                        Method = method,
                        Operation = operation
                    });
            }
            catch
            {
                ProGlassAutomation.Services.Logging.AppLogService.Debug("if suppressed");
            }
        }

        private static bool IsInsideShareTemp(string? path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return false;
                string full = Path.GetFullPath(path);
                string root = Path.GetFullPath(ShareTempFiles.RootFolder);
                return full.StartsWith(root + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
