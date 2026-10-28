// ═══════════════════════════════════════════════════════════════════════
// FILE: Diagnostics/AppLog.cs
// MODULE: application foundation — the one technical error log.
//
// WHAT THIS IS FOR
//   Until now a failure in V3.0 left, at most, a Debug.WriteLine that a Release
//   build throws away, or a MessageBox that disappears when the owner clicks OK.
//   There was no way to answer "what went wrong on that machine yesterday?".
//   This writes that answer down.
//
// WHAT IT IS NOT
//   • not an activity recorder: no startup, navigation, successful save, theme
//     change, heartbeat or ordinary validation message is ever written;
//   • not the business audit trail. Who changed which inquiry, client, user or
//     company profile is InquiryCommunications / CurrentOperator / PreparedBy /
//     UpdatedBy and stays there. This file records TECHNICAL failure only;
//   • not a replacement for the user-facing error experience. When code recovers
//     and shows a message, the message is unchanged — this only also records the
//     exception so it can be diagnosed later.
//
// THREE RULES
//   1. Failure only. If nothing failed, nothing is written and no file is created.
//   2. Defensive. Every entry point swallows its own faults (AppLogFile.LastFailure
//      holds them); logging can never throw into, or mask, the exception being
//      logged, and can never recurse.
//   3. Honest. A log record proves the exception was OBSERVED. It does not prove
//      the process survived, and it does not claim coverage of a kill/TerminateProcess
//      or a fault before this type was ever touched.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace ProGlassAutomation.Diagnostics
{
    /// <summary>Severity levels this log is allowed to record.</summary>
    public enum AppLogSeverity
    {
        /// <summary>A failure that was handled or reported; the app carried on.</summary>
        Error,

        /// <summary>A failure the application cannot continue from.</summary>
        Fatal
    }

    /// <summary>The single, failure-only diagnostic log for the application.</summary>
    public static class AppLog
    {
        /// <summary>
        /// Identical failures repeated inside this window are counted, not
        /// re-written, so one failing paint/refresh loop cannot flood the file.
        /// The count is emitted with the next distinct entry.
        /// </summary>
        public static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(10);

        private static readonly object Gate = new();
        private static string _lastSignature = "";
        private static DateTimeOffset _lastWrittenAt;
        private static int _suppressed;

        /// <summary>True once <see cref="AttachProcessHandlers"/> has run.</summary>
        public static bool ProcessHandlersAttached { get; private set; }

        /// <summary>Entries recorded this run (a suppressed repeat is not one).</summary>
        public static int EntriesRecorded => AppLogFile.RecordsWritten;

        /// <summary>Where the owner is told to look. Empty until a failure occurs.</summary>
        public static string LogFilePath => AppLogFile.CurrentPath;

        /// <summary>
        /// Records an ERROR: a failure the application recovered from, or reported
        /// to the user, and continued after.
        /// </summary>
        /// <param name="operation">Stable, greppable id: "module.thing.action".</param>
        /// <param name="error">The exception. Null is allowed for a reported fault.</param>
        /// <param name="context">
        /// Extra facts that help a diagnosis (record id, file name, size). Must not
        /// contain credentials or customer content — it is scrubbed anyway.
        /// </param>
        public static bool Error(string operation, Exception? error, string? context = null)
            => Write(AppLogSeverity.Error, operation, error, context);

        /// <summary>
        /// Records a FATAL: the application cannot continue. Callers still show
        /// their existing message first; this only makes it retrievable later.
        /// </summary>
        public static bool Fatal(string operation, Exception? error, string? context = null)
            => Write(AppLogSeverity.Fatal, operation, error, context);

        /// <summary>
        /// Builds and appends one record. Returns false when nothing was written
        /// (suppressed repeat, or a write fault — see <see cref="LastWriteFailure"/>).
        /// Never throws.
        /// </summary>
        public static bool Write(AppLogSeverity severity, string operation, Exception? error, string? context = null)
        {
            try
            {
                string entry = Compose(severity, operation, error, context, out string signature);

                lock (Gate)
                {
                    DateTimeOffset now = DateTimeOffset.Now;
                    if (signature.Length > 0
                        && signature == _lastSignature
                        && now - _lastWrittenAt < RepeatWindow)
                    {
                        _suppressed++;
                        return false;
                    }

                    if (_suppressed > 0)
                    {
                        entry = ComposeRepeatNote(_suppressed) + entry;
                        _suppressed = 0;
                    }

                    bool written = AppLogFile.Append(entry);
                    if (written)
                    {
                        _lastSignature = signature;
                        _lastWrittenAt = now;
                    }

                    return written;
                }
            }
            catch (Exception)
            {
                // Rule 2: logging never becomes the incident.
                return false;
            }
        }

        /// <summary>Why the last write failed, or "" when the last write succeeded.</summary>
        public static string LastWriteFailure => AppLogFile.LastFailure;

        /// <summary>
        /// Registers the three process-level traps. Idempotent: calling twice does
        /// not double-record.
        ///
        /// What each one can and cannot do:
        ///   • dispatcher — a fault on the UI thread the app did not handle. We log
        ///     it, then set Handled so the shell is not torn down for one bad click;
        ///     the app continuing is deliberate, not a claim the fault was harmless.
        ///   • AppDomain — a fault on a background thread with no handler. The
        ///     runtime is already terminating; setting Handled does nothing.
        ///   • task — a fault nobody awaited, observed before finalisation. The
        ///     application is NOT terminating; logging and marking it observed is
        ///     what stops an unrelated finalizer from killing the process later.
        /// </summary>
        public static void AttachProcessHandlers()
        {
            if (ProcessHandlersAttached) return;
            ProcessHandlersAttached = true;

            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.DispatcherUnhandledException += OnDispatcherUnhandled;
            }

            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandled;
            TaskScheduler.UnobservedTaskException += OnUnobservedTask;
        }

        /// <summary>Removes the traps. Used by the isolated checks only.</summary>
        public static void DetachProcessHandlers()
        {
            if (!ProcessHandlersAttached) return;
            ProcessHandlersAttached = false;

            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.DispatcherUnhandledException -= OnDispatcherUnhandled;
            }

            AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandled;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTask;
        }

        private static void OnDispatcherUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs args)
        {
            try
            {
                Fatal("app.dispatcher.unhandled",
                    args.Exception,
                    "thread: UI dispatcher; the fault reached the top of the message loop");
            }
            catch (Exception)
            {
                // never let the logger change what the application does next
            }

            // The shell stays up: a single unhandled click handler is not proof the
            // whole application is unusable, and terminating would lose the owner's
            // unsaved work. The record above is the evidence.
            args.Handled = true;
        }

        private static void OnAppDomainUnhandled(object? sender, UnhandledExceptionEventArgs args)
        {
            try
            {
                Fatal("app.domain.unhandled",
                    args.ExceptionObject as Exception,
                    "isTerminating: " + args.IsTerminating
                    + (args.ExceptionObject is Exception ? "" : "; non-exception object: "
                        + (args.ExceptionObject?.GetType().Name ?? "null")));
            }
            catch (Exception)
            {
                // The runtime is terminating either way; nothing to recover.
            }
        }

        private static void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            try
            {
                Error("app.task.unobserved",
                    args.Exception,
                    "an awaitable faulted and nothing observed it");
            }
            catch (Exception)
            {
                // ignore

            }
        }

        /// <summary>
        /// Formats one record. Public so the checks can assert the exact shape
        /// without touching a file.
        /// </summary>
        public static string Compose(
            AppLogSeverity severity, string operation, Exception? error, string? context,
            out string signature)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            signature = error == null
                ? ""
                : severity + "|" + operation + "|" + error.GetType().FullName + "|" + FirstFrame(error);

            var text = new StringBuilder(1024);
            text.Append("──────────────────────────────────────────────────────────────────────\n");
            text.Append(now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture))
                .Append("  ")
                .Append(ZoneLabel(now))
                .Append("  [")
                .Append(severity == AppLogSeverity.Fatal ? "FATAL" : "ERROR")
                .Append("]  ")
                .Append(AppVersion())
                .Append('\n');

            text.Append("operation : ").Append(Line(operation)).Append('\n');
            text.Append("module    : ").Append(Line(ModuleOf(operation))).Append('\n');

            if (!string.IsNullOrWhiteSpace(context))
                text.Append("context   : ").Append(Line(context)).Append('\n');

            if (error == null)
            {
                text.Append("exception : (none supplied — a failure was reported without one)\n");
            }
            else
            {
                AppendError(text, error, depth: 1);
            }

            return text.ToString();
        }

        private static void AppendError(StringBuilder text, Exception error, int depth)
        {
            string indent = depth == 1 ? "" : new string(' ', 2 * (depth - 1));
            text.Append(indent).Append("exception : ")
                .Append(error.GetType().FullName ?? error.GetType().Name)
                .Append('\n');
            text.Append(indent).Append("message   : ").Append(Line(error.Message)).Append('\n');

            string? stack = error.StackTrace;
            if (!string.IsNullOrWhiteSpace(stack))
            {
                text.Append(indent).Append("stack     :").Append('\n');
                foreach (string frame in stack!.Split('\n'))
                    text.Append(indent).Append("  ").Append(Line(frame.Trim())).Append('\n');
            }
            else
            {
                text.Append(indent).Append("stack     : (not captured; the exception was created without a throw)\n");
            }

            if (error is AggregateException aggregate)
            {
                int index = 0;
                foreach (Exception inner in aggregate.Flatten().InnerExceptions)
                {
                    index++;
                    text.Append(indent).Append("aggregate ").Append(index.ToString(CultureInfo.InvariantCulture))
                        .Append(':').Append('\n');
                    AppendError(text, inner, depth + 1);
                }
                return;
            }

            if (error.InnerException != null)
            {
                text.Append(indent).Append("inner     :").Append('\n');
                AppendError(text, error.InnerException, depth + 1);
            }
        }

        private static string ComposeRepeatNote(int suppressed)
        {
            return "──────────────────────────────────────────────────────────────────────\n"
                + DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture)
                + "  [ERROR]  " + AppVersion() + "\n"
                + "operation : app.log.repeat-suppressed\n"
                + "detail    : the previous failure repeated " + suppressed.ToString(CultureInfo.InvariantCulture)
                + " more time(s) inside " + RepeatWindow.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)
                + "s and was counted instead of re-written.\n";
        }

        /// <summary>
        /// Timestamp zone, spelled so two machines' logs can be compared without
        /// guessing: "UTC+04:00 (Arabian Standard Time)".
        /// </summary>
        private static string ZoneLabel(DateTimeOffset now)
        {
            TimeSpan offset = now.Offset;
            string sign = offset < TimeSpan.Zero ? "-" : "+";
            string hours = Math.Abs(offset.Hours).ToString("00", CultureInfo.InvariantCulture);
            string minutes = Math.Abs(offset.Minutes).ToString("00", CultureInfo.InvariantCulture);
            string id;
            try { id = TimeZoneInfo.Local.Id; }
            catch (Exception) { id = "unknown zone"; }
            return "UTC" + sign + hours + ":" + minutes + " (" + id + ")";
        }

        /// <summary>The running application's version, as shown in support.</summary>
        public static string AppVersion()
        {
            try
            {
                Assembly assembly = typeof(AppLog).Assembly;
                string? informational = assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(informational))
                    return "ProGlassAutomation " + informational!.Split('+')[0];

                Version? version = assembly.GetName().Version;
                return "ProGlassAutomation "
                    + (version == null ? "unknown version" : version.ToString());
            }
            catch (Exception)
            {
                return "ProGlassAutomation (version unavailable)";
            }
        }

        /// <summary>"step4.company.save" → "step4.company"; "app.startup" → "app".</summary>
        internal static string ModuleOf(string operation)
        {
            if (string.IsNullOrWhiteSpace(operation)) return "(unstated)";
            int last = operation.LastIndexOf('.');
            return last <= 0 ? operation : operation.Substring(0, last);
        }

        private static string FirstFrame(Exception error)
        {
            string? stack = error.StackTrace;
            if (string.IsNullOrWhiteSpace(stack)) return "";
            return stack!.Split('\n').FirstOrDefault()?.Trim() ?? "";
        }

        /// <summary>
        /// One record, one line each: CR/LF are folded so a stack trace can never
        /// forge an extra record header.
        /// </summary>
        private static string Line(string? value)
            => AppLogRedaction.Scrub((value ?? "").Replace("\r", " ").Replace("\n", " ")).Trim();
    }
}
