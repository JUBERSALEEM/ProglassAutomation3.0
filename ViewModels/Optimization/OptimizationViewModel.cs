// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/Optimization/OptimizationViewModel.cs
// PROJECT: ProGlassAutomation
// MODULE: Optimization — module VM (design 2026-09-12)
//
// Tabs: JOBS → SIZES → STOCKS → SETTINGS → SUMMARY → 2D VIEW → REPORT.
//   • JOBS     — piece list (ref / W×H / qty); approved-JO picker only
//                (source lock: confirmed JOs from approved PIs). No
//                hardcoded calibration job — data comes from Step 5 / Step 4.
//   • SIZES    — merged thickness+color summary of the piece list.
//   • STOCKS   — Step 4 stock pool: the sheet sizes the selected specs
//                were quoted on (outer/inner per spec), tagged by pane
//                composition so each cutting group packs against its own
//                rack. Racks are ticked "use in run" — never the whole
//                sheet store.
//   • SETTINGS — per-thickness LM/RM/TM/BM, kerf, minimum breakout,
//                rotation (enabled by default for all pieces).
//   • SUMMARY  — sheets, utilization, unplaced, oracle verdict, ordered
//                highest → lowest utilization.
//   • 2D VIEW  — centred sheet canvas with two-tone margin/breakout
//                overlay and prev/next navigation.
//   • REPORT   — layout / parts / stocks summaries with CSV export.
//
// Engine: Services/Optimization/GuillotinePacker (pure, deterministic,
// calibrated against the owner's benchmark: Low-E 17 @ 82.77%).
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.JobOrder.Common.JobOrderStatus;
using ProGlassAutomation.Models.Production;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.JobOrder.Mapper;
using ProGlassAutomation.Services.JobOrder.Repository;
using ProGlassAutomation.Services.Dialogs;
using ProGlassAutomation.Services.Optimization;
using ProGlassAutomation.Services.Production;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss;

namespace ProGlassAutomation.ViewModels.Optimization
{
    public partial class OptimizationViewModel : ViewModels.ViewModelBase
    {

        // ── tabs ────────────────────────────────────────────────────────
        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set { _selectedTabIndex = value; OnPropertyChanged(); }
        }
        // ── confirmation seam ──────────────────────────────────────────
        // Destructive actions in this module used to fire on a single click
        // with nothing between the operator and the loss — including
        // DeleteSession, which removes a saved run from the database for
        // good. The seam below lets the module ask first without changing
        // anything for the callers that cannot answer.
        private readonly IDialogService? _dialogs;

        // ── ctor ───────────────────────────────────────────────────────

        public OptimizationViewModel() : this(false) { }

        /// <param name="isHeadlessHost">True when another module drives this
        /// VM (the proforma costing host) instead of the Optimization screen.
        /// No UI is shown in that mode.</param>
        /// <param name="dialogs">Used to confirm before destroying work.
        /// Optional and null by default, so every existing caller keeps
        /// working unchanged; see <see cref="ConfirmDestructive"/>.</param>
        public OptimizationViewModel(bool isHeadlessHost, IDialogService? dialogs = null)
        {
            IsHeadlessHost = isHeadlessHost;
            _dialogs = dialogs;
            InitializeInputLifetime();
            // Breadcrumb for uncatchable crashes (e.g. a StackOverflowException
            // kills the process before any exception handler can run, so the
            // LAST line in app-*.log is the only evidence of where it died).
            // Written synchronously to disk by AppLogService.
            ProGlassAutomation.Services.Logging.AppLogService.Info(
                "Optimization module opening — constructing view model",
                new ProGlassAutomation.Services.Logging.LogContext
                {
                    Module = "Optimization",
                    Operation = "OpenModule"
                });

            AddPieceCommand = new RelayCommand(_ => AddPiece());
            RemovePieceCommand = new RelayCommand(p => RemovePiece(p as PieceEntryVm));
            ClearPiecesCommand = new RelayCommand(_ =>
            {
                // Guarded by Count so an already-empty list does not prompt.
                if (Pieces.Count > 0
                    && !ConfirmDestructive(
                        "Remove every piece from the list?\n\n"
                        + "This clears the whole piece list, and the run results go with it. "
                        + "There is no undo.",
                        "Clear all pieces")) return;
                Pieces.Clear();
                RefreshDerived();
            });
            LoadReferenceJobCommand = new RelayCommand(_ => LoadReferenceJob());
            RefreshJobsCommand = new RelayCommand(_ => LoadJobsAsync());
            AddSelectedPanesCommand = new RelayCommand(_ => AddSelectedPanes(),
                _ => CanUseJobInputs && HasSelectedPanes);
            SelectAllPanesCommand = new RelayCommand(_ => SetAllPanesSelected(true));
            ClearPaneSelectionCommand = new RelayCommand(_ => SetAllPanesSelected(false));
            if (!IsHeadlessHost) LoadJobsAsync();

            AddStockCommand = new RelayCommand(_ => AddStock());
            RemoveStockCommand = new RelayCommand(p => RemoveStock(p as StockEntryVm));
            Stocks.CollectionChanged += (_, _) => RebuildAllocationRows();
            ImportStockFromStep4Command = new RelayCommand(_ => ImportStockFromStep4());

            LoadHouseDefaultsCommand = new RelayCommand(_ => LoadHouseDefaults());

            RunQuickCommand = new RelayCommand(_ => Run(stage: "QUICK"));
            RunOptimizeCommand = new RelayCommand(_ => Run(stage: "OPTIMIZE"));
            CancelRunCommand = new RelayCommand(_ => CancelRun());
            UpgradeToOptimizeCommand = new RelayCommand(_ => UpgradeToOptimize());

            ExportLayoutReportCommand = new RelayCommand(p => ExportLayoutReport(ParseExportFormat(p)));
            ExportPartsReportCommand = new RelayCommand(p => ExportPartsReport(ParseExportFormat(p)));
            ExportStocksReportCommand = new RelayCommand(p => ExportStocksReport(ParseExportFormat(p)));
            ExportIndustrialReportCommand = new RelayCommand(p => ExportIndustrialReport(ParseExportFormat(p)));
            ExportAllCommand = new RelayCommand(p => ExportAll(ParseExportFormat(p)));

            SaveSessionCommand = new RelayCommand(_ => SaveSessionAsync());
            OpenSessionCommand = new RelayCommand(p => OpenSession(p as SessionRowVm));
            DeleteSessionCommand = new RelayCommand(p => DeleteSession(p as SessionRowVm));
            RefreshSessionsCommand = new RelayCommand(_ => LoadSavedSessionsAsync());
            if (!IsHeadlessHost) _ = LoadSavedSessionsAsync();

            SelectPreviousSheetCommand = new RelayCommand(_ =>
            {
                if (SelectedSheetIndex > 0) SelectedSheetIndex--;
            });
            SelectNextSheetCommand = new RelayCommand(_ =>
            {
                if (SelectedSheetIndex < _flatSheets.Count - 1)
                    SelectedSheetIndex++;
            });
            SelectSheetCommand = new RelayCommand(p =>
            {
                if (p is int idx && idx >= 0 && idx < _flatSheets.Count)
                {
                    SelectedSheetIndex = idx;
                    IsGalleryMode = false;
                    SelectedTabIndex = TabIndexTwoDView; // jump straight to the 2D VIEW
                }
            });
            PreviewZoomInCommand = new RelayCommand(_ => { IsFitView = false; PreviewScale += 0.25; });
            PreviewZoomOutCommand = new RelayCommand(_ => { IsFitView = false; PreviewScale -= 0.25; });
            PreviewZoomResetCommand = new RelayCommand(_ => { IsFitView = true; PreviewScale = 1.0; });
            PreviewFitCommand = new RelayCommand(_ => { IsFitView = true; PreviewScale = 1.0; });
            InitPreviewGalleryCommands();

            // The stock pool starts EMPTY — it is populated from the ticked
            // Job Orders' Step 4 sheet sizes (STOCKS tab rule). No generic
            // "full rack" is pre-seeded; the run packs each cutting group
            // against the rack its spec was quoted on.
            RefreshDerived();

            // Phase 14 + 15 bridge + cutting workflow
            InitPhase14Optimization();
            InitPhase15Optimization();

            // Pairs with the opening breadcrumb above. If the process dies
            // between the two lines, the fault is inside this constructor;
            // if the log shows this line but the view never appeared, the
            // fault is in the view's XAML / binding inflation instead.
            ProGlassAutomation.Services.Logging.AppLogService.Info(
                "Optimization module — view model constructed",
                new ProGlassAutomation.Services.Logging.LogContext
                {
                    Module = "Optimization",
                    Operation = "OpenModule"
                });
        }

        /// <summary>
        /// Asks before destroying work. Returns true when it is safe to proceed.
        ///
        /// Returns true when no dialog service is wired up, on purpose. Headless
        /// hosts and unit tests would otherwise block on a window nobody will
        /// answer, and a destructive action that silently stopped working is
        /// worse than one that asks. The real module gets the prompt because DI
        /// injects a service; nothing else in the codebase changes behaviour.
        /// </summary>
        private bool ConfirmDestructive(string message, string caption)
        {
            if (_dialogs == null) return true;
            if (IsHeadlessHost) return true;
            return _dialogs.Confirm(message, caption);
        }

        /// <summary>
        /// Refuses an action and says why, in front of the operator.
        ///
        /// These messages all answer the same question — "why did nothing
        /// happen when I pressed that button?" The operator is looking at the
        /// button, not at the hero card at the top of the module, so the
        /// answer belongs where they are looking. That is the whole split
        /// between this and RunMessage: RunMessage reports the OUTCOME of a
        /// run that happened; this reports a run that never started.
        ///
        /// Falls back to the hero card when there is no dialog service, so a
        /// headless host still records the reason instead of dropping it.
        /// </summary>
        private void Blocked(string message, string caption)
        {
            if (_dialogs == null || IsHeadlessHost) { RunMessage = message; return; }
            _dialogs.Warning(message, caption);
        }
    }
}
