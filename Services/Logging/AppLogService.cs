// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Logging/AppLogService.cs
// PROJECT: ProGlassAutomation
// MODULE: Core — technical application log
//
// One central technical log for the whole app (startup, shutdown,
// warnings, errors, exceptions, DB/import/export problems). Distinct
// from the USER ACTIVITY log in Services/Audit (who did what) — this
// log is for diagnostics (what went wrong, exactly where).
//
// Every error gets a short ErrorId ("ERR-3F9A2C") that is shown in
// the error dialog and written to the file, so support can find the
// full technical record in seconds.
//
// Files: %AppData%\ProGlassAutomation\logs\app-yyyyMMdd.log
// Retention: 14 days, cleaned at startup.
// Thread-safe; never throws — logging must not break the app.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Audit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace ProGlassAutomation.Services.Logging
{
    /// <summary>Central technical logger (static facade).</summary>
    public static class AppLogService
    {
        private static readonly object _writeLock = new();
        private static readonly Queue<LogEntry> _recent = new();
        private const int MaxBuffered = 500;
        private const int RetentionDays = 14;

        private static string LogFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ProGlassAutomation", "logs");

        /// <summary>
        /// "Session is open" marker. Written when logging initialises,
        /// removed by <see cref="MarkCleanShutdown"/>. If it still exists
        /// at the next startup, the previous session ended without a clean
        /// shutdown — the signature of an uncatchable crash (a
        /// StackOverflowException, native fault or kill) that could not run
        /// any exception handler and therefore wrote no crash report.
        /// </summary>
        private static string SessionMarkerPath =>
            Path.Combine(LogFolder, "session-open.marker");

        /// <summary>Raised on the UI thread after each entry is written.</summary>
        public static event EventHandler<LogEntry>? LogWritten;

        /// <summary>Creates the log folder and prunes old files once.</summary>
        public static void Initialize()
        {
            try
            {
                Directory.CreateDirectory(LogFolder);

                var cutoff = DateTime.Now.AddDays(-RetentionDays);
                foreach (var file in new DirectoryInfo(LogFolder)
                    .GetFiles("app-*.log")
                    .Where(f => f.LastWriteTime < cutoff))
                {
                    file.Delete();
                }
            }
            catch { /* never fail startup because of logging */ }

            // ── Unclean-shutdown detection (2026-09-17) ─────────────────
            // A StackOverflowException (or any native / process-level fault)
            // terminates a .NET process immediately: DispatcherUnhandledException,
            // AppDomain.UnhandledException and UnobservedTaskException NEVER
            // run for it, so no log entry and no crash-*.txt can be written
            // at the moment of death. The marker below is the evidence that
            // survives: it is only removed on a clean exit, so its presence
            // at startup proves the previous run died hard. The app-*.log
            // lines around the recorded end time then show the last thing
            // the app was doing (module navigation breadcrumbs are written
            // synchronously to disk for exactly this reason).
            try
            {
                bool unclean = File.Exists(SessionMarkerPath);
                string previousMarker = unclean
                    ? SafeReadMarker()
                    : "";

                File.WriteAllText(
                    SessionMarkerPath,
                    $"session-open pid={Environment.ProcessId} " +
                    $"started={DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");

                if (unclean)
                {
                    Warning(
                        "Previous session ended WITHOUT a clean shutdown " +
                        "(no OnExit). This is the signature of an uncatchable " +
                        "crash such as a StackOverflowException — those bypass " +
                        "every .NET exception handler, which is why no crash " +
                        "report was written at the time. Previous session: " +
                        (string.IsNullOrWhiteSpace(previousMarker)
                            ? "(marker unreadable)"
                            : previousMarker.Trim()) +
                        ". Check the log lines immediately before this entry " +
                        "and the Windows Event Viewer (Application log, " +
                        ".NET Runtime / Application Error) for the faulting " +
                        "module.",
                        new LogContext { Operation = "Startup" });
                }
            }
            catch { /* marker bookkeeping must never break startup */ }
        }

        private static string SafeReadMarker()
        {
            try { return File.ReadAllText(SessionMarkerPath); }
            catch { return ""; }
        }

        /// <summary>
        /// Removes the "session is open" marker. Called from App.OnExit so
        /// only a clean shutdown clears it. A crash-triggered Shutdown still
        /// runs OnExit — that is fine, because a catchable crash has already
        /// written its crash report; this marker exists for the uncatchable
        /// ones that write nothing.
        /// </summary>
        public static void MarkCleanShutdown()
        {
            try
            {
                if (File.Exists(SessionMarkerPath))
                    File.Delete(SessionMarkerPath);
            }
            catch { /* never throw from shutdown bookkeeping */ }
        }

        // ── Convenience API ─────────────────────────────────────────────

        public static void Debug(string message, LogContext? context = null)
            => Write(LogLevel.Debug, message, null, context);

        public static void Info(string message, LogContext? context = null)
            => Write(LogLevel.Info, message, null, context);

        public static void Warning(string message, LogContext? context = null)
            => Write(LogLevel.Warning, message, null, context);

        /// <summary>Logs an error; returns its ErrorId for user-facing dialogs.</summary>
        public static string Error(
            string message, Exception? ex = null, LogContext? context = null)
        {
            var id = NewErrorId();
            Write(LogLevel.Error, message, ex, context, id);
            return id;
        }

        /// <summary>Logs a crash-level error; returns its ErrorId.</summary>
        public static string Critical(
            string message, Exception? ex = null, LogContext? context = null)
        {
            var id = NewErrorId();
            Write(LogLevel.Critical, message, ex, context, id);
            return id;
        }

        /// <summary>Snapshot of the in-memory ring buffer (newest last).</summary>
        public static List<LogEntry> GetRecent(int count = 200)
        {
            lock (_writeLock)
                return _recent.TakeLast(count).ToList();
        }

        // ── Core writer ─────────────────────────────────────────────────

        private static void Write(
            LogLevel level, string message, Exception? ex,
            LogContext? context, string errorId = "")
        {
            // Phase 2: Auto-flow ambient correlation if context doesn't have one
            if (context != null && string.IsNullOrWhiteSpace(context.CorrelationId))
            {
                try
                {
                    var ambient = ProGlassAutomation.Services.Common.CorrelationContext.CurrentId;
                    if (!string.IsNullOrWhiteSpace(ambient))
                        context.CorrelationId = ambient;
                }
                catch { /* Logger must never throw — see file header. */ }
            }
            if (context != null && string.IsNullOrWhiteSpace(context.DocumentId))
            {
                try
                {
                    var docId = ProGlassAutomation.Services.Common.CorrelationContext.CurrentDocumentId;
                    if (!string.IsNullOrWhiteSpace(docId))
                        context.DocumentId = docId;
                }
                catch { /* Logger must never throw — see file header. */ }
            }
            if (context != null && string.IsNullOrWhiteSpace(context.DocumentType))
            {
                try
                {
                    var docType = ProGlassAutomation.Services.Common.CorrelationContext.CurrentDocumentType;
                    if (!string.IsNullOrWhiteSpace(docType))
                        context.DocumentType = docType;
                }
                catch { /* Logger must never throw — see file header. */ }
            }

            var entry = new LogEntry
            {
                Level = level,
                Message = message ?? "",
                Context = context ?? new LogContext(),
                ErrorId = errorId,
                Username = AuditService.CurrentUsername,
                ExceptionText = ex == null ? "" : FormatException(ex)
            };

            // Also flow correlation from context if it was empty before but now filled
            if (string.IsNullOrWhiteSpace(entry.Context.CorrelationId) && !string.IsNullOrWhiteSpace(errorId))
            {
                // Use ErrorId as correlation fallback for errors without explicit correlation
            }

            lock (_writeLock)
            {
                _recent.Enqueue(entry);
                while (_recent.Count > MaxBuffered)
                    _recent.Dequeue();
            }

            try
            {
                var path = Path.Combine(
                    LogFolder,
                    $"app-{DateTime.Now:yyyyMMdd}.log");

                var line = new StringBuilder()
                    .Append(entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append("  [").Append(entry.LevelDisplay.PadRight(8)).Append("]")
                    .Append(string.IsNullOrEmpty(errorId) ? "" : $"  ({errorId})")
                    .Append(string.IsNullOrEmpty(entry.CorrelationId) ? "" : $"  {entry.CorrelationId}")
                    .Append(string.IsNullOrEmpty(entry.DocumentType) ? "" : $"  {entry.DocumentType}")
                    .Append(string.IsNullOrEmpty(entry.DocumentId) ? "" : $"  {entry.DocumentId}")
                    .Append(string.IsNullOrEmpty(entry.Username) ? "" : $"  user={entry.Username}")
                    .Append(entry.ContextDisplay.Length == 0 ? "" : $"  [{entry.ContextDisplay}]")
                    .Append("  ").Append(entry.Message)
                    .AppendLine(entry.ExceptionText.Length == 0
                        ? ""
                        : Environment.NewLine + entry.ExceptionText)
                    .ToString();

                lock (_writeLock)
                    File.AppendAllText(path, line);
            }
            catch
            {
                // A logging failure must never surface to the user.
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                if (dispatcher.CheckAccess())
                    LogWritten?.Invoke(null, entry);
                else
                    dispatcher.InvokeAsync(() => LogWritten?.Invoke(null, entry));
            }
        }

        private static string FormatException(Exception ex)
        {
            var sb = new StringBuilder();
            var current = ex;
            int depth = 0;

            while (current != null && depth < 4)
            {
                if (depth > 0) sb.AppendLine("  caused by:");
                sb.Append("  ").Append(current.GetType().Name)
                  .Append(": ").AppendLine(current.Message);

                var stack = current.StackTrace ?? "";
                if (stack.Length > 1500) stack = stack[..1500] + " ...";
                if (stack.Length > 0) sb.AppendLine(stack);

                current = current.InnerException;
                depth++;
            }

            return sb.ToString().TrimEnd();
        }

        private static string NewErrorId() =>
            "ERR-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
    }
}
