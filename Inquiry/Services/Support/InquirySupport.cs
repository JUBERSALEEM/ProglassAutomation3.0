// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Support/InquirySupport.cs
// MODULE: Inquiry — small shared helpers (V3).
//
// The legacy build leaned on app-wide utilities (AsyncUtility,
// ExceptionMessages, ProcessStartGuard, Result). V3 ports only the
// pieces the Inquiry module needs, in one place, so the module has no
// dependency on the rest of the shell.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace ProGlassAutomation.Services.Inquiry
{
    /// <summary>
    /// A small outcome record for file operations that must not throw
    /// into UI code.
    /// </summary>
    public readonly record struct FileOpResult(bool IsSuccess, string Error)
    {
        public static FileOpResult Ok() => new(true, "");
        public static FileOpResult Fail(string error) => new(false, error ?? "");
    }

    /// <summary>
    /// Attachment upload limits. 30 MB per file to match the
    /// customer-website intake limit (31457280), so web-ingested
    /// documents and manual attachments share one cap.
    /// </summary>
    public static class AttachmentLimits
    {
        public static readonly long MaxFileSizeBytes = 31457280L; // 30 MB, website parity
        public static string MaxFileSizeDisplay => "30 MB";
    }

    /// <summary>Deep exception text for status bars and messages.</summary>
    public static class ExceptionMessages
    {
        public static string Deep(Exception? ex)
        {
            if (ex == null) return "";
            var parts = new System.Text.StringBuilder();
            var current = ex;
            int depth = 0;
            while (current != null && depth < 4)
            {
                if (depth > 0) parts.Append(" → ");
                parts.Append(current.Message);
                current = current.InnerException;
                depth++;
            }
            return parts.ToString();
        }
    }

    /// <summary>
    /// Fire-and-forget with a UI-thread marshal and an observed
    /// exception path — a failed background task must never be
    /// swallowed silently.
    /// </summary>
    public static class AsyncUtility
    {
        public static void SafeFireAndForget(Func<Task> action, string operation = "")
        {
            _ = RunAsync(action, operation);
        }

        private static async Task RunAsync(Func<Task> action, string operation)
        {
            var dispatcher = Application.Current?.Dispatcher;
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Route to a status-visible sink when possible; otherwise
                // at minimum observe the exception so the GC does not
                // treat the faulted task as unobserved.
                string message = $"[{operation}] {ExceptionMessages.Deep(ex)}";
                if (dispatcher is { HasShutdownStarted: false })
                {
                    try
                    {
                        dispatcher.BeginInvoke(() =>
                            MessageBox.Show(message, "ProGlass Automation — background operation failed",
                                MessageBoxButton.OK, MessageBoxImage.Warning));
                    }
                    catch { /* dispatcher shutting down */ }
                }
            }
        }

        /// <summary>
        /// WPF re-queries command CanExecute "sometimes" — after every
        /// field change the editor explicitly invalidates so buttons
        /// track dirty/can-save state without a focus move.
        /// </summary>
        public static void InvalidateCommandsThrottled()
        {
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>
    /// Opens a file with the registered system application. Never
    /// throws — a failure is a FileOpResult the UI can display.
    /// </summary>
    public static class FileOpener
    {
        public static FileOpResult TryOpenFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return FileOpResult.Fail($"'{System.IO.Path.GetFileName(path)}' is not found on disk. It may have been moved or deleted outside ProGlass.");
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return FileOpResult.Ok();
            }
            catch (Exception ex)
            {
                return FileOpResult.Fail("Could not open the file: " + ex.Message);
            }
        }

        /// <summary>
        /// Opens a folder in the system file browser, creating it first
        /// when missing. Never throws.
        /// </summary>
        public static FileOpResult TryOpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return FileOpResult.Ok();
            }
            catch (Exception ex)
            {
                return FileOpResult.Fail("Could not open the folder: " + ex.Message);
            }
        }
    }
}
