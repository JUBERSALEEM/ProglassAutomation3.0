// ═══════════════════════════════════════════════════════════════════════
//  FILE: ViewModels/ProformaInvoice/Step5DataGrid/DataGridViewModel.Optimization.cs
//  PROJECT: ProGlassAutomation
//
//  WHY THIS EXISTS
//  ---------------------------------------------------------------------
//  QUICK / OPTIMIZE on the Step 5 top panel, so a cut plan can be produced
//  from the grid the operator is actually looking at instead of requiring a
//  confirmed Job Order on the OPTIMIZATION tab. Step 6 then reads sheet qty
//  and utilization from the run rather than from hand-typed numbers.
//
//  LAYERING
//  This grid does not reference the optimization module. It snapshots its own
//  data and hands it to a delegate the composer (ProformaInvoiceViewModel)
//  binds — the same late-bound idiom already used for Step 4 pricing
//  (BindStep4Pricing) and the Step 6 feed (BindOptimizationFeed). One writer,
//  many readers, no new constructor arguments on a DI-resolved view model.
//
//  NO EXISTING CODE PATH IS ALTERED
//  The commands are created lazily on first binding rather than assigned in
//  InitializeCore, so the shared two-phase initialization that ERR-633EEB
//  hardened is not touched.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid
{
    public partial class DataGridViewModel
    {
        /// <summary>
        /// Runs a cut plan over a Step 5 snapshot. Returns whether a run
        /// actually started plus an operator-facing message for the status line.
        /// </summary>
        /// <param name="specGroups">Live Step 5 spec groups in snapshot form,
        /// including the Step 4 sheet sizes and prices each spec was quoted on —
        /// without those there is no rack to cut against.</param>
        /// <param name="stage">"QUICK" or "OPTIMIZE".</param>
        public delegate (bool Started, string Message) Step5OptimizationRunner(
            IReadOnlyList<SpecGroupSnapshot> specGroups, string stage);

        private Step5OptimizationRunner? _optimizationRunner;

        /// <summary>True once the composer has bound a runner AND no run is in
        /// flight, so the buttons disable instead of failing on click — either
        /// when Step 5 is hosted somewhere with no optimization module behind
        /// it, or while a run is already going.</summary>
        public bool CanRunOptimization => _optimizationRunner != null && !_isOptimizationRunning;

        // ── live run state, mirrored from the optimization host ────────────
        //
        // The run happens inside the optimization module, asynchronously, after
        // this grid's command has already returned. Without this the operator
        // clicks QUICK and the screen shows nothing at all until Step 6 changes
        // — which reads as "the button does nothing". The composer pushes the
        // host's own progress numbers here; nothing is estimated or faked.

        private bool _isOptimizationRunning;

        /// <summary>True while an optimization run started from this grid (or
        /// from the optimization module behind it) is in flight.</summary>
        public bool IsOptimizationRunning
        {
            get => _isOptimizationRunning;
            private set
            {
                if (_isOptimizationRunning == value) return;
                _isOptimizationRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanRunOptimization));
                _runQuickOptimizationCommand?.RaiseCanExecuteChanged();
                _runOptimizeOptimizationCommand?.RaiseCanExecuteChanged();
            }
        }

        private int _optimizationProgressPercent;

        /// <summary>0–100, clamped. Drives the determinate bar.</summary>
        public int OptimizationProgressPercent
        {
            get => _optimizationProgressPercent;
            private set
            {
                int clamped = value < 0 ? 0 : (value > 100 ? 100 : value);
                if (_optimizationProgressPercent == clamped) return;
                _optimizationProgressPercent = clamped;
                OnPropertyChanged();
            }
        }

        private string _optimizationProgressDetail = string.Empty;

        /// <summary>What the engine is doing right now, in its own words.</summary>
        public string OptimizationProgressDetail
        {
            get => _optimizationProgressDetail;
            private set
            {
                string text = value ?? string.Empty;
                if (_optimizationProgressDetail == text) return;
                _optimizationProgressDetail = text;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Pushes run state in from the composer, which owns the optimization
        /// host. Safe to call from the thread the run reports on: every property
        /// here only raises <c>PropertyChanged</c>, which WPF marshals.
        /// </summary>
        public void ReportOptimizationProgress(bool running, int percent, string? detail)
        {
            OptimizationProgressPercent = percent;
            OptimizationProgressDetail = detail;
            // Running last: its setter requeries the buttons, and by then the
            // bar already has the numbers to show.
            IsOptimizationRunning = running;
        }

        /// <summary>Binds the runner. Called once by ProformaInvoiceViewModel.</summary>
        public void BindOptimizationRunner(Step5OptimizationRunner? runner)
        {
            _optimizationRunner = runner;
            OnPropertyChanged(nameof(CanRunOptimization));

            // The buttons take their enabled state from CanExecute, and WPF only
            // re-queries that when the command tells it to. The composer binds
            // before the view normally does, so this is usually a no-op — but if
            // that order ever changes (previewer, a future refactor) the buttons
            // would otherwise stay disabled with a perfectly good runner behind
            // them. The commands are created lazily, so these may still be null.
            _runQuickOptimizationCommand?.RaiseCanExecuteChanged();
            _runOptimizeOptimizationCommand?.RaiseCanExecuteChanged();
            _viewOptimizationResultCommand?.RaiseCanExecuteChanged();
        }

        private RelayCommand? _runQuickOptimizationCommand;
        private RelayCommand? _runOptimizeOptimizationCommand;
        private RelayCommand? _viewOptimizationResultCommand;

        /// <summary>One fast deterministic pass over the live Step 5 grid.</summary>
        public ICommand RunQuickOptimizationCommand =>
            _runQuickOptimizationCommand ??= new RelayCommand(
                _ => RunOptimizationFromStep5("QUICK"),
                _ => CanRunOptimization);

        /// <summary>Deepest multi-start search over the live Step 5 grid.</summary>
        public ICommand RunOptimizeOptimizationCommand =>
            _runOptimizeOptimizationCommand ??= new RelayCommand(
                _ => RunOptimizationFromStep5("OPTIMIZE"),
                _ => CanRunOptimization);

        /// <summary>
        /// Raised when the operator asks to see the result of a run. The grid
        /// does not know what a module or a document is, so it asks and the
        /// composer decides — the same late-binding the runner above uses, which
        /// keeps this view model free of any reference to the optimization
        /// module.
        /// </summary>
        public event System.EventHandler? ViewOptimizationResultRequested;

        /// <summary>
        /// Opens the optimization module on its SUMMARY tab showing this
        /// document's latest run. Enabled on exactly the same condition as the
        /// run buttons — a runner is bound, so there is an optimization module
        /// behind this grid to show anything at all. It is NOT gated on a run
        /// having finished: a document run earlier in the day, or before a
        /// restart, has a result worth looking at, and when there genuinely is
        /// none the summary says so in words rather than the button being
        /// mysteriously grey.
        /// </summary>
        public ICommand ViewOptimizationResultCommand =>
            _viewOptimizationResultCommand ??= new RelayCommand(
                _ => ViewOptimizationResultRequested?.Invoke(this, System.EventArgs.Empty),
                _ => _optimizationRunner != null);

        /// <summary>
        /// Snapshots the grid and asks the bound runner to cut it.
        /// </summary>
        /// <remarks>
        /// Reports through <see cref="IoStatus"/>, which the top panel already
        /// binds and already collapses when empty, so the operator gets feedback
        /// in the strip they clicked without any new XAML surface. The run
        /// itself is asynchronous inside the optimization module; this returns
        /// as soon as it has started or been refused.
        /// </remarks>
        private void RunOptimizationFromStep5(string stage)
        {
            var runner = _optimizationRunner;
            if (runner == null)
            {
                IoStatus = "Optimization is not available on this screen.";
                return;
            }

            try
            {
                try
                {
                    if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.FrameworkElement fe)
                        fe.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
                }
                catch { }

                var groups = BuildSpecGroupSnapshotsForOptimization();

                if (groups.Count == 0)
                {
                    IoStatus = "Nothing to optimize yet — add a spec and enter pane sizes on Step 5.";
                    return;
                }

                var (started, message) = runner(groups, stage);

                IoStatus = string.IsNullOrWhiteSpace(message)
                    ? (started ? "Optimization run started." : "Optimization run did not start.")
                    : message;
            }
            catch (Exception ex)
            {
                // A reporting convenience must never take the quoting grid down.
                IoStatus = "Could not start the optimization run: " + ex.Message;
                Services.Logging.AppLogService.Warning(
                    "[Step5] optimization run failed to start: " +
                    Utilities.ExceptionMessages.Deep(ex));
            }
        }

        /// <summary>
        /// Live Step 5 → <see cref="SpecGroupSnapshot"/>, including the Step 4
        /// sheet sizes and prices.
        /// </summary>
        /// <remarks>
        /// Deliberately separate from Step 6's <c>BuildSpecSnapshotsForCalculator</c>,
        /// which copies only the fields a price calculation needs. The optimizer
        /// additionally needs the rack each spec was quoted on
        /// (<c>OuterSheet*</c> / <c>InnerSheet*</c>) and the pane colours, and it
        /// must not be given them by widening a method another step depends on.
        /// </remarks>
        public IReadOnlyList<SpecGroupSnapshot> BuildSpecGroupSnapshotsForOptimization()
        {
            var result = new List<SpecGroupSnapshot>(SpecGroups.Count);

            foreach (var g in SpecGroups)
            {
                if (g == null) continue;

                var snap = new SpecGroupSnapshot
                {
                    GroupNumber = g.GroupNumber,
                    GroupName = g.GroupName ?? "",
                    SelectedGlassType = g.SelectedGlassType ?? "",
                    SelectedSpecName = g.SelectedSpecName ?? "",
                    OuterGlassColor = g.OuterGlassColor ?? "",
                    InnerGlassColor = g.InnerGlassColor ?? "",
                    ThirdGlassColor = g.ThirdGlassColor ?? "",
                    MaterialIsValid = g.MaterialIsValid,
                    MaterialInvalidReason = g.MaterialInvalidReason ?? "",

                    // The rack this spec was quoted on. Zero on a spec whose
                    // Step 4 sheet was never chosen; RunFromStep5 reports that
                    // rather than cutting against an invented size.
                    OuterSheetWidthMm = g.OuterSheetWidthMm,
                    OuterSheetHeightMm = g.OuterSheetHeightMm,
                    InnerSheetWidthMm = g.InnerSheetWidthMm,
                    InnerSheetHeightMm = g.InnerSheetHeightMm,
                    OuterSheetPricePerSqm = g.OuterSheetPricePerSqm,
                    InnerSheetPricePerSqm = g.InnerSheetPricePerSqm,
                    ThirdSheetWidthMm = g.ThirdSheetWidthMm,
                    ThirdSheetHeightMm = g.ThirdSheetHeightMm,
                    ThirdSheetPricePerSqm = g.ThirdSheetPricePerSqm
                };

                foreach (var r in g.ItemRows)
                {
                    if (r == null || r.IsEmpty) continue;

                    // A row with no quantity or no usable pane dimension cannot
                    // become a cutting piece; skipping it here keeps the run's
                    // piece count honest instead of padding it with zeros.
                    // SENIOR FIX 2026-09-20: previously checked only W1/W2,
                    // ignoring H1/H2 — a row with W1>0 H1=0 passed filter then
                    // got dropped silently in ProductionDistributionEngine,
                    // appearing as 'Step 5 not importing'.
                    if (r.Qty <= 0) continue;
                    bool hasPane1 = r.W1 > 0 && r.H1 > 0;
                    bool hasPane2 = r.W2 > 0 && r.H2 > 0;
                    if (!hasPane1 && !hasPane2) continue;

                    snap.ItemRows.Add(new GlassItemRowSnapshot
                    {
                        SerialNumber = r.SerialNumber,
                        GlassRef = r.GlassRef ?? "",
                        W1 = r.W1,
                        H1 = r.H1,
                        W2 = r.W2,
                        H2 = r.H2,
                        Qty = r.Qty,
                        Rate = r.Rate,
                        PerimeterFactor = r.PerimeterFactor
                    });
                }

                if (snap.ItemRows.Count > 0) result.Add(snap);
            }

            // ── DIAGNOSTIC LOGGING (Rule 18) ────────────────────────────────
            try
            {
                int totalRows = result.Sum(s => s.ItemRows.Count);
                int totalQty = result.Sum(s => s.ItemRows.Sum(row => row.Qty));
                Services.Logging.AppLogService.Info(
                    $"[STEP5] snapshot for optimization: {result.Count} spec(s), {totalRows} row(s), {totalQty} total qty, outer/inner sheets preserved",
                    new Services.Logging.LogContext { Module = "Proforma", Operation = "Step5Snapshot" });
            }
            catch { }

            return result;
        }
    }
}
