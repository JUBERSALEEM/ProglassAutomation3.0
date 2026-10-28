// ═══════════════════════════════════════════════════════════════════════
// FILE: Diagnostics/AppLogProcessTraps.cs
// MODULE: application foundation — the part of the log that catches what
//         nothing else caught.
//
// Split out of AppLog.cs (rule K: a C# file stays under 400 lines) when the
// dispatcher policy grew. Three traps, three different truths:
//
//   • DispatcherUnhandledException — a fault on the UI thread that no page
//     handled. Nothing in the application knows which screen state survived, so
//     the fault is recorded, the operator is told, and the application closes
//     through its own shutdown path. It is NOT marked handled and forgotten.
//   • AppDomain.UnhandledException — a fault on a background thread with no
//     handler. The runtime is already terminating; there is no Handled here.
//   • TaskScheduler.UnobservedTaskException — a fault nobody awaited. The
//     application is not terminating; recording it and marking it observed is
//     what keeps an unrelated finaliser from killing the process later.
//
// Attachment is deliberately two-phase: the App type's static constructor runs
// before any Application object exists, so it can take the AppDomain and task
// traps only; OnStartup attaches the dispatcher trap afterwards. The flags are
// separate so the second call cannot be mistaken for "already done".
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace ProGlassAutomation.Diagnostics
{
    public static partial class AppLog
    {

        /// <summary>True once all three traps are attached (the dispatcher one may lag).</summary>
        public static bool ProcessHandlersAttached { get; private set; }

        /// <summary>
        /// True once the UI dispatcher trap is attached. False before the
        /// Application object exists — which is why it is tracked separately:
        /// "attached" must never be assumed when only two of the three are.
        /// </summary>
        public static bool DispatcherHandlerAttached { get; private set; }

        private static bool _domainAttached;
        private static bool _taskAttached;

        /// <summary>
        /// Registers the three process-level traps. Idempotent: calling twice does
        /// not double-record.
        ///
        /// What each one can and cannot do:
        ///   • dispatcher — a fault on the UI thread that no page handled. It is
        ///     recorded, the operator is told, and the application closes: nothing
        ///     here can know which part of the interface is still consistent, so
        ///     continuing would be a guess, not a recovery.
        ///   • AppDomain — a fault on a background thread with no handler. The
        ///     runtime is already terminating; setting Handled does nothing.
        ///   • task — a fault nobody awaited, observed before finalisation. The
        ///     application is NOT terminating; logging and marking it observed is
        ///     what stops an unrelated finalizer from killing the process later.
        /// </summary>
        public static void AttachProcessHandlers()
        {
            if (!_domainAttached)
            {
                AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandled;
                _domainAttached = true;
            }

            if (!_taskAttached)
            {
                TaskScheduler.UnobservedTaskException += OnUnobservedTask;
                _taskAttached = true;
            }

            // The dispatcher trap needs an Application, so the earliest call (the
            // App type's static constructor, before the window object exists)
            // cannot take it. The flags are separate for exactly that reason: a
            // later call attaches it instead of being told "already attached" and
            // silently losing the trap for the life of the process.
            if (!DispatcherHandlerAttached && System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.DispatcherUnhandledException += OnDispatcherUnhandled;
                DispatcherHandlerAttached = true;
            }

            ProcessHandlersAttached = _domainAttached && _taskAttached && DispatcherHandlerAttached;
        }

        /// <summary>Removes the traps. Used by the isolated checks only.</summary>
        public static void DetachProcessHandlers()
        {
            if (DispatcherHandlerAttached && System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.DispatcherUnhandledException -= OnDispatcherUnhandled;
            }

            if (_domainAttached) AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandled;
            if (_taskAttached) TaskScheduler.UnobservedTaskException -= OnUnobservedTask;

            DispatcherHandlerAttached = false;
            _domainAttached = false;
            _taskAttached = false;
            ProcessHandlersAttached = false;
        }

        private static void OnDispatcherUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs args)
        {
            bool recorded;
            try
            {
                recorded = Fatal("app.dispatcher.unhandled",
                    args.Exception,
                    "thread: UI dispatcher; the fault reached the top of the message loop");
            }
            catch (Exception)
            {
                // never let the logger change what the application does next
                recorded = false;
            }

            // A fault that reached here was NOT handled by the page that raised it,
            // so no part of the application knows which screen state survived. The
            // honest response is the one the owner asked for: say so, keep the
            // record, and close. Continuing would be a guess dressed up as
            // recovery, and every later write could act on a half-updated form.
            Announce(recorded);

            // Handled = the runtime must not abort the process from this event; the
            // application then leaves through its own shutdown path below.
            args.Handled = true;

            try
            {
                System.Windows.Application.Current?.Shutdown(1);
            }
            catch (Exception)
            {
                // If even the shutdown request fails, the process still ends: the
                // exception becomes unhandled again on the next dispatch.
            }
        }

        /// <summary>
        /// Tells the operator what happened, and where the record is. Deliberately
        /// best-effort: a message box that cannot be shown must not stop the
        /// shutdown that follows it.
        /// </summary>
        private static void Announce(bool recorded)
        {
            try
            {
                string where = recorded
                    ? "The details were written to:\n" + AppLogFile.CurrentPath
                    : "The details could not be written to the error log.";

                // Fully qualified: this project also references WinForms (tray icon),
                // so a bare MessageBox would be ambiguous.
                System.Windows.MessageBox.Show(
                    "ProGlass Automation hit an unexpected error and cannot guarantee that this "
                    + "screen is still consistent.\n\nThe application will close now. Check the page "
                    + "you were working on before entering anything again.\n\n" + where,
                    "ProGlass Automation",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            catch (Exception)
            {
                // The message is a courtesy; the shutdown is the guarantee.
            }
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
    }
}
