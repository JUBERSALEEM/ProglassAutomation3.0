// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/Shared/UiDispatch.cs
// PROJECT: ProGlassAutomation
// MODULE: UI Thread Dispatch (Phase 1 — C-1 fix: non-blocking)
//
// PREVIOUS BUG:
//   disp.Invoke(work, DispatcherPriority.DataBind) BLOCKED the calling
//   thread until the UI thread processed the delegate. When called from
//   Task.Run background threads (Dashboard, StatementGenerator, etc.),
//   this created synchronous cross-thread dependencies that froze the
//   UI whenever the dispatcher was busy.
//
// FIX:
//   Use BeginInvoke (fire-and-forget) instead of Invoke (blocking).
//   The background thread returns immediately; the UI thread processes
//   the delegate at DataBind priority when it reaches that slot.
//
//   For callers that genuinely need the result, a new RunSync() method
//   preserves the old blocking behaviour with an explicit name so code
//   reviewers can spot it.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Windows;
using System.Windows.Threading;

namespace ProGlassAutomation.ViewModels.Shared
{
    /// <summary>
    /// Marshals collection / binding mutations onto the WPF dispatcher.
    /// ObservableCollection throws if a bound view is changed off-thread
    /// (ERR-A65374 during demo Activity / God View loads).
    ///
    /// Phase 1: NON-BLOCKING by default. The old blocking Invoke was
    /// causing UI freezes when called from Task.Run background threads.
    /// </summary>
    public static class UiDispatch
    {
        /// <summary>
        /// Queues the action on the UI thread at DataBind priority and
        /// returns IMMEDIATELY without blocking. If already on the UI
        /// thread, runs the action directly.
        ///
        /// Safe for ObservableCollection mutations — DataBind priority
        /// runs before Render, so the UI picks up the changes in the
        /// next paint cycle.
        /// </summary>
        public static void Run(Action work)
        {
            if (work == null) return;
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.CheckAccess())
            {
                work();
                return;
            }
            // NON-BLOCKING: BeginInvoke queues and returns immediately.
            disp.BeginInvoke(work, DispatcherPriority.DataBind);
        }

        /// <summary>
        /// BLOCKING dispatch — use ONLY when the caller genuinely needs
        /// the result before proceeding (e.g., reading a UI-bound value).
        /// Named explicitly so code reviewers can spot the blocking call.
        /// </summary>
        public static void RunSync(Action work)
        {
            if (work == null) return;
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.CheckAccess())
            {
                work();
                return;
            }
            disp.Invoke(work, DispatcherPriority.DataBind);
        }

        /// <summary>
        /// Queues at Background priority — lower than input and rendering.
        /// Use for cosmetic updates that must not compete with user input.
        /// </summary>
        public static void RunBackground(Action work)
        {
            if (work == null) return;
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.CheckAccess())
            {
                work();
                return;
            }
            disp.BeginInvoke(work, DispatcherPriority.Background);
        }
    }
}
