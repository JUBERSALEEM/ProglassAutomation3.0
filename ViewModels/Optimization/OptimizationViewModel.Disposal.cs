// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/Optimization/OptimizationViewModel.Disposal.cs
// MODULE: Optimization — lifetime teardown (partial of OptimizationViewModel)
//
// PHASE 6 / findings 11 and 13 (2026-09-20).
//
// WHY THIS FILE EXISTS
//   OptimizationViewModel owned three things that outlive it and had no way to
//   release any of them:
//
//     • _runClock, a DispatcherTimer (Progress.cs). A DispatcherTimer is rooted
//       by the WPF Dispatcher while it is RUNNING, and its Tick handler is an
//       instance method — so a live timer is a strong root to the entire view
//       model: Pieces, Stocks, AvailableJobs, _groupRuns, _flatSheets. It was
//       stopped in exactly one place, RunCoreAsync's finally, which is correct
//       for the normal and exceptional run paths but leaves nothing to fall back
//       on if the run is never entered (StartRunClock fires in Run() on the UI
//       thread, RunCoreAsync is reached through SafeFireAndForget) or if the VM
//       is replaced mid-run.
//
//     • _runCts and _bridgeCts, two CancellationTokenSources. Both were disposed
//       on some paths (Run.cs, Phase14.cs) and leaked on others.
//
//     • PropertyChanged handlers attached to every job in AvailableJobs and
//       every pane layer in JobSpecBreakdown. Those were detached only when the
//       collections were rebuilt, never on teardown.
//
//   The headless Step 5 host made this worse from the other side: it is a full
//   OptimizationViewModel, and ProformaInvoiceViewModel subscribed to its
//   PropertyChanged without ever unsubscribing — so the host rooted all nine
//   wizard step VMs. See the matching change in ProformaInvoiceViewModel.Dispose.
//
//   ViewModelBase implements only INotifyPropertyChanged, so IDisposable is
//   introduced here rather than overridden.
// ═══════════════════════════════════════════════════════════════════════

using System;
using ProGlassAutomation.Services.Logging;

namespace ProGlassAutomation.ViewModels.Optimization
{
    public partial class OptimizationViewModel : IDisposable
    {
        private bool _disposed;

        /// <summary>True once teardown has started. Lets a caller avoid touching
        /// a VM that is being released.</summary>
        public bool IsDisposed => _disposed;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (!disposing) return;
            DisposeInputLifetime();

            // Assigned with the null-forgiving operator: _runClock and _runCts
            // are declared non-nullable in their own partials, and this project
            // builds with Nullable=enable and TreatWarningsAsErrors=true, so a
            // bare `= null` would be a build error rather than a warning.

            // ── 1. the clock ─────────────────────────────────────────────
            // Stopped AND detached AND dropped. Stopping alone is not enough:
            // while the Tick handler is attached the Dispatcher holds a path to
            // this instance through the timer's event list.
            try
            {
                StopRunClock();
                if (_runClock != null)
                {
                    _runClock.Tick -= OnRunClockTick;
                    _runClock = null!;
                }
            }
            catch (Exception ex) { LogTeardown("run clock", ex); }

            // ── 2. cancellation sources ──────────────────────────────────
            // Cancel first so an in-flight run observes the teardown instead of
            // completing into a VM nobody is listening to, then dispose.
            try
            {
                if (_runCts != null)
                {
                    try { _runCts.Cancel(); } catch (ObjectDisposedException) { }
                    _runCts.Dispose();
                    _runCts = null!;
                }
            }
            catch (Exception ex) { LogTeardown("run cancellation", ex); }

            try
            {
                if (_bridgeCts != null)
                {
                    try { _bridgeCts.Cancel(); } catch (ObjectDisposedException) { }
                    _bridgeCts.Dispose();
                    _bridgeCts = null;
                }
            }
            catch (Exception ex) { LogTeardown("bridge cancellation", ex); }

            // ── 3. job and layer handlers ────────────────────────────────
            try
            {
                foreach (var job in AvailableJobs)
                    if (job != null) job.PropertyChanged -= OnJobSelectionChanged;
            }
            catch (Exception ex) { LogTeardown("job handlers", ex); }



            try
            {
                foreach (var spec in JobSpecBreakdown)
                {
                    if (spec?.Layers == null) continue;
                    foreach (var layer in spec.Layers)
                        if (layer != null) layer.PropertyChanged -= OnLayerSelectionChanged;
                }
            }
            catch (Exception ex) { LogTeardown("layer handlers", ex); }

            // ── 4. retained plan data (finding 13) ───────────────────────
            // _groupRuns holds every GroupRun → Result.Sheets → PlacedPiece.Piece
            // → the fully expanded input piece list. On a large run that is the
            // whole workload, retained for as long as the VM lives. Clearing here
            // releases it at teardown; ApplyResults and RestoreSessionCore already
            // clear and replace it between runs, so nothing holds a completed
            // plan longer than the screen showing it.
            try
            {
                _groupRuns.Clear();
                _flatSheets.Clear();

            }
            catch (Exception ex) { LogTeardown("retained plan data", ex); }
        }

        /// <summary>
        /// Teardown must never throw out of Dispose — a failure releasing one
        /// resource should not prevent releasing the rest, and Dispose is called
        /// from shutdown paths that cannot handle an exception.
        /// </summary>
        private static void LogTeardown(string what, Exception ex)
        {
            try
            {
                AppLogService.Debug(
                    $"[OptimizationViewModel] Dispose: best-effort teardown of {what} failed: {ex.Message}",
                    new LogContext { Module = "Optimization", Operation = "Dispose" });
            }
            catch { }
        }
    }
}
