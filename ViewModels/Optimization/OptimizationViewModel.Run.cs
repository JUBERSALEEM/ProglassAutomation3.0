// ═══════════════════════════════════════════════════════════════
// FILE: OptimizationViewModel.Run.cs
// MODULE: Optimization — SUMMARY — result surface and the pack-engine execution (partial of OptimizationViewModel)
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.JobOrder.Common.JobOrderStatus;
using ProGlassAutomation.Models.Production;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.JobOrder.Mapper;
using ProGlassAutomation.Services.JobOrder.Repository;
using ProGlassAutomation.Services.Optimization;
using ProGlassAutomation.Services.Production;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss;

namespace ProGlassAutomation.ViewModels.Optimization
{
    public partial class OptimizationViewModel : ViewModels.ViewModelBase
    {

        // ── RESULT tab ─────────────────────────────────────────────────

        public sealed class SheetRowVm : ViewModels.ViewModelBase
        {
            private bool _isSelected;
            public string SheetNo { get; set; } = string.Empty;
            public string StockLabel { get; set; } = string.Empty;
            public string PieceCount { get; set; } = string.Empty;
            public string Utilization { get; set; } = string.Empty;
            public string WasteLabel { get; set; } = string.Empty;
            /// <summary>Utilization band for colour coding: Good ≥85% /
            /// Mid 70–84% / Low &lt;70% (matches the industrial report).</summary>
            public string UtilizationTone { get; set; } = "Good";
            /// <summary>Cutting-group (glass composition) this sheet belongs to.</summary>
            public string Composition { get; set; } = string.Empty;
            public int Index { get; set; }

            /// <summary>Highlighted in the 2D filmstrip when this is the
            /// sheet currently on the canvas.</summary>
            public bool IsSelected
            {
                get => _isSelected;
                set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
            }
        }

        public ObservableCollection<SheetRowVm> SheetRows { get; } = new();

        // ── Million-scale UI guards ──────────────────────────────────
        // SheetRows / UnplacedRows bind to NON-virtualized ItemsControls:
        // at 10k+ rows they hang the UI thread and burn memory per row
        // object. The DATA always stays complete (_flatSheets drives the
        // 2D preview + navigation, the report export lists every sheet)
        // — only the on-screen rows cap, with an honest overflow label.
        private const int MaxSheetRows = 2000;
        private const int MaxUnplacedRows = 500;

        private string _sheetRowsOverflowLabel = string.Empty;
        /// <summary>"Showing first 2,000 of 17,175 sheets…" (empty when all fit).</summary>
        public string SheetRowsOverflowLabel
        {
            get => _sheetRowsOverflowLabel;
            private set { _sheetRowsOverflowLabel = value; OnPropertyChanged(); }
        }

        private bool _hasSheetRowsOverflow;
        public bool HasSheetRowsOverflow
        {
            get => _hasSheetRowsOverflow;
            private set { _hasSheetRowsOverflow = value; OnPropertyChanged(); }
        }

        private string _unplacedOverflowLabel = string.Empty;
        /// <summary>"…and N more unplaced pieces" (empty when all fit).</summary>
        public string UnplacedOverflowLabel
        {
            get => _unplacedOverflowLabel;
            private set { _unplacedOverflowLabel = value; OnPropertyChanged(); }
        }

        private bool _hasUnplacedOverflow;
        public bool HasUnplacedOverflow
        {
            get => _hasUnplacedOverflow;
            private set { _hasUnplacedOverflow = value; OnPropertyChanged(); }
        }

        private bool _hasResult;
        public bool HasResult
        {
            get => _hasResult;
            private set
            {
                _hasResult = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanUpgradeToOptimize));
            }
        }

        private string _resultSheets = "—";
        private string _resultUtilization = "—";
        private string _resultUnplaced = "—";
        private string _resultOracle = "—";
        private string _resultStage = string.Empty;
        private bool _oraclePassed;

        public string ResultSheets { get => _resultSheets; private set { _resultSheets = value; OnPropertyChanged(); } }
        public string ResultUtilization { get => _resultUtilization; private set { _resultUtilization = value; OnPropertyChanged(); } }
        public string ResultUnplaced { get => _resultUnplaced; private set { _resultUnplaced = value; OnPropertyChanged(); } }
        public string ResultOracle { get => _resultOracle; private set { _resultOracle = value; OnPropertyChanged(); } }
        public string ResultStage { get => _resultStage; private set { _resultStage = value; OnPropertyChanged(); } }
        public bool OraclePassed { get => _oraclePassed; private set { _oraclePassed = value; OnPropertyChanged(); } }

        // ── Waste / offcut reporting (utilization + waste = the sheet) ──
        private string _resultWaste = "—";
        /// <summary>Total offcut across the run (stock − used), m².</summary>
        public string ResultWaste { get => _resultWaste; private set { _resultWaste = value; OnPropertyChanged(); } }

        private string _resultWastePct = "—";
        /// <summary>Weighted waste share of the consumed stock, %.</summary>
        public string ResultWastePct { get => _resultWastePct; private set { _resultWastePct = value; OnPropertyChanged(); } }

        /// <summary>Pieces the engine could not place on any stock (labels).</summary>
        public ObservableCollection<string> UnplacedRows { get; } = new();

        private bool _hasUnplaced;
        public bool HasUnplaced { get => _hasUnplaced; private set { _hasUnplaced = value; OnPropertyChanged(); } }

        // ── RESULT tab: per-cutting-group breakdown ─────────────────────
        // One run per glass composition — different glass never mixes on a
        // sheet. Each group reports its own sheets / utilization / waste.

        public sealed class CuttingGroupResultVm
        {
            public string Composition { get; set; } = string.Empty;
            public string Thickness { get; set; } = string.Empty;
            /// <summary>Distinct stock dimensions used by this group, in mm; shared by live and restored results.</summary>
            public string StockSize { get; set; } = string.Empty;
            public string PieceCount { get; set; } = string.Empty;
            public string Sheets { get; set; } = string.Empty;
            public string Utilization { get; set; } = string.Empty;
            public string Waste { get; set; } = string.Empty;
            public string Unplaced { get; set; } = string.Empty;
            public string Note { get; set; } = string.Empty;
            public bool HasNote => !string.IsNullOrWhiteSpace(Note);
        }

        public ObservableCollection<CuttingGroupResultVm> GroupResults { get; } = new();

        /// <summary>Private per-composition run record (composition, margins, result).</summary>
        private sealed class GroupRun
        {
            public string Composition = string.Empty;
            public string Thickness = string.Empty;
            public int PieceCount;
            public MarginProfile Margins = null!;
            public OptimizationResult Result = null!;
            public double UtilizationPct;
            public double WasteM2;
            public string UnplacedLabel = "0";
            public string OracleLabel = string.Empty;

            /// <summary>Oracle violations found for this group. Verified once
            /// when the run finishes and reused by ApplyResults — it used to
            /// re-verify every sheet of every group a second time, and Verify
            /// is O(pieces^2) per sheet.</summary>
            public int ProblemCount;
            public bool ValidationFailed;
            public string SheetsLabel = string.Empty;
            public string MarginNote = string.Empty;
        }

        /// <summary>Flat sheet index → (group run, sheet index in that run).</summary>
        private sealed class FlatSheet
        {
            public GroupRun Run = null!;
            public int SheetIndex;
        }

        private readonly List<GroupRun> _groupRuns = new();
        private readonly List<FlatSheet> _flatSheets = new();

        public ICommand RunQuickCommand { get; }
        public ICommand RunOptimizeCommand { get; }

        /// <summary>Cancels an in-flight run. The engine checks the token once
        /// per part, so cancelling takes effect within about a second even on a
        /// group large enough to have taken minutes.</summary>
        public ICommand CancelRunCommand { get; }

        private string _runMessage = string.Empty;
        public string RunMessage
        {
            get => _runMessage;
            private set
            {
                if (_runMessage == value) return;
                _runMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasRunMessage));
                NotifyHeroStatusChanged();
            }
        }

        /// <summary>True when a status/validation message should be shown in the header.</summary>
        public bool HasRunMessage => !string.IsNullOrWhiteSpace(_runMessage);

        // ── run notes, classified ───────────────────────────────────────
        // The engine writes 38 different notes into OptimizationResult.
        // ScaleNotes. Until now the run pipeline joined them all into one
        // RunMessage paragraph, so "this layout cannot be cut" sat in the same
        // sentence as a utilisation percentage and read like background noise.
        // They are surfaced separately here, rated, and the critical ones get
        // a popup.

        public enum RunNoteSeverity { Info, Warning, Error }

        public sealed class RunNoteVm
        {
            public string Text { get; init; } = string.Empty;
            public RunNoteSeverity Severity { get; init; } = RunNoteSeverity.Info;
        }

        /// <summary>Notes from the latest run, most severe first.</summary>
        public ObservableCollection<RunNoteVm> RunNotes { get; } = new();

        /// <summary>True when the latest run produced a plan that cannot be
        /// produced as drawn. Drives the warning banner.</summary>
        public bool HasCriticalRunNotes =>
            RunNotes.Any(n => n.Severity == RunNoteSeverity.Error);

        /// <summary>True when there is anything worth showing at all.</summary>
        public bool HasRunNotes => RunNotes.Count > 0;

        /// <summary>
        /// Rates one engine note. Keyed on the note's own wording on purpose:
        /// it covers notes from anywhere in the engine, including ones added
        /// later, without the engine having to know about severities.
        ///
        /// Error is reserved for "this plan cannot be produced as drawn" —
        /// the only category that must stop someone sending it to the table.
        /// </summary>
        private static RunNoteSeverity ClassifyNote(string note)
        {
            // NOT EXPORTABLE covers the two notes that end "inspection only,
            // not exportable" - a staged pattern the plan did not hold, and a
            // QUICK run that found no valid candidate. Both mean the plan on
            // screen is not production work, which is what Error is for.
            if (note.Contains("CUT REACHABILITY", StringComparison.OrdinalIgnoreCase)
                || note.Contains("CUT SEQUENCE", StringComparison.OrdinalIgnoreCase)
                || note.Contains("CUT PATTERN", StringComparison.OrdinalIgnoreCase)
                || note.Contains("NOT EXPORTABLE", StringComparison.OrdinalIgnoreCase)
                || note.Contains("VALIDATION FAILURE", StringComparison.OrdinalIgnoreCase))
                return RunNoteSeverity.Error;

            if (note.Contains("GUILLOTINE POLICY", StringComparison.OrdinalIgnoreCase)
                || note.Contains("UNPLACED", StringComparison.OrdinalIgnoreCase)
                || note.Contains("BUDGET", StringComparison.OrdinalIgnoreCase)
                || note.Contains("REDUCED", StringComparison.OrdinalIgnoreCase)
                || note.Contains("SKIPPED", StringComparison.OrdinalIgnoreCase)
                || note.Contains("BREAKOUT WASTE", StringComparison.OrdinalIgnoreCase)
                || note.Contains("WARNING", StringComparison.OrdinalIgnoreCase)
                || note.Contains("NO VALID", StringComparison.OrdinalIgnoreCase))
                return RunNoteSeverity.Warning;

            return RunNoteSeverity.Info;
        }

        private void PublishRunNotes(IEnumerable<string> notes, IEnumerable<GroupRun> runs)
        {
            RunNotes.Clear();

            var all = new List<string>(
                notes.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct());

            // Materialised once: it is enumerated for the unplaced total, the
            // validation text and the critical-note check below.
            var runList = (runs ?? Enumerable.Empty<GroupRun>())
                .Where(r => r != null).ToList();

            // The engine never writes a note for pieces it could not place, so
            // an incomplete job surfaced only as a number on a KPI card. That
            // is the one result an operator should not have to go looking for:
            // it means the customer's glass is not all cut.
            int unplaced = runList.Sum(r => r.Result?.Unplaced?.Count ?? 0);
            if (unplaced > 0)
                all.Add(string.Format(CultureInfo.InvariantCulture,
                    "UNPLACED: {0} piece(s) could not be fitted on any available plate. "
                    + "This run is incomplete - check the stock sizes against the largest "
                    + "pieces before releasing the cutting list.", unplaced));

            // ValidationProblems is the engine's second diagnostic channel,
            // filled in three places and read nowhere: geometry violations,
            // pieces lost between input and output, and stock-cap breaches.
            // The KPI card counts them ("3 issue(s)") and the text that says
            // WHICH three was never surfaced. The count alone is not enough to
            // act on, so the detail is published here.
            foreach (var run in runList)
            {
                var problems = run.Result?.ValidationProblems;
                if (problems == null || problems.Count == 0) continue;

                string group = string.IsNullOrWhiteSpace(run.Composition)
                    ? "this group"
                    : string.IsNullOrWhiteSpace(run.Thickness)
                        ? run.Composition
                        : run.Composition + " " + run.Thickness;

                // Not every problem VerifyComprehensive returns is a failure.
                // It also pulls in AuditBreakout, and the engine is explicit
                // that a thin offcut is wasted glass rather than an illegal
                // layout. Labelling that an error would put a "this plan
                // cannot be cut" popup on a plan that cuts perfectly well,
                // so the two are tagged apart and rated differently.
                foreach (var p in problems
                             .Where(p => !string.IsNullOrWhiteSpace(p))
                             .Distinct())
                {
                    bool thinOffcut = p.Contains("minimum breakout",
                        StringComparison.OrdinalIgnoreCase);

                    all.Add((thinOffcut ? "BREAKOUT WASTE" : "VALIDATION FAILURE")
                            + " (" + group + "): " + p);
                }
            }

            // Distinct again: the notes above were appended after the list was
            // built, and one group can report the same violation twice.
            foreach (var note in all
                         .Where(n => !string.IsNullOrWhiteSpace(n))
                         .Distinct()
                         .Select(n => new RunNoteVm { Text = n, Severity = ClassifyNote(n) })
                         .OrderByDescending(n => n.Severity))
                RunNotes.Add(note);

            OnPropertyChanged(nameof(HasCriticalRunNotes));
            OnPropertyChanged(nameof(HasRunNotes));

            // A warning the operator never sees is not a warning. These mean
            // the plan cannot be cut as drawn, so they interrupt rather than
            // sit in a paragraph under a number.
            var critical = RunNotes
                .Where(n => n.Severity == RunNoteSeverity.Error)
                .Select(n => n.Text)
                .ToList();

            if (critical.Count > 0)
            {
                // The cause decides the advice; generic advice wastes the
                // operator's time. An earlier version of this told them to
                // switch on a guillotine policy, which has no control in the
                // UI at all - an instruction nobody could follow. These keys
                // match what the engine actually writes into its notes.
                bool lostWork = critical.Any(t => t.Contains(
                    "VALIDATION FAILURE", StringComparison.OrdinalIgnoreCase));

                bool badSequence = critical.Any(t => t.Contains(
                    "CUT SEQUENCE", StringComparison.OrdinalIgnoreCase))
                    || critical.Any(t => t.Contains(
                    "CUT REACHABILITY", StringComparison.OrdinalIgnoreCase));

                string advice;
                if (lostWork)
                    advice = "Pieces were lost, or the plan breaks a sheet or stock "
                           + "limit. Re-run the group, and report it if it repeats.";
                else if (badSequence)
                    advice = "The recorded cut list does not match the layout on "
                           + "screen, which is a cut list left behind by an earlier "
                           + "pass rather than anything a setting controls. Re-running "
                           + "usually clears it. If the same sheet fails again, send "
                           + "me the job - that is an engine bug, not something you "
                           + "can configure away.";
                else
                    advice = "Try a different CUTTING COMPLEXITY on the SETTINGS tab, "
                           + "or a higher effort level, so the search returns a plan "
                           + "the table can actually cut.";

                // A geometry sweep on a large job can return hundreds of lines;
                // a popup that tall is unreadable, so it is capped.
                const int MaxShown = 6;
                var shown = critical.Take(MaxShown).Select(t => "\u2022 " + t).ToList();
                if (critical.Count > MaxShown)
                    shown.Add(string.Format(CultureInfo.InvariantCulture,
                        "…and {0} more. The full list is in the RUN NOTES panel "
                        + "on the SUMMARY tab.", critical.Count - MaxShown));

                _dialogs?.Warning(
                    "This run produced a layout that is not safe to cut:\n\n"
                    + string.Join("\n\n", shown)
                    + "\n\nDo not send this plan to the table. " + advice,
                    "Plan is not cuttable");
            }
        }

        // ── async run state (progress bar + button gating) ─────────────
        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                _isRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanRun));
                OnPropertyChanged(nameof(CanUpgradeToOptimize));
                OnPropertyChanged(nameof(IsRunProgressVisible));
                NotifyHeroStatusChanged();
            }
        }

        /// <summary>Run buttons are enabled only when no run is in flight.</summary>
        public bool CanRun => !_isRunning;

        /// <summary>Cancel source for the in-flight run; null while idle.</summary>
        private CancellationTokenSource _runCts;

        /// <summary>True while a run is in flight and can still be cancelled.</summary>
        public bool CanCancel => _isRunning && _runCts != null;

        /// <summary>Requests cancellation. Safe to call when nothing is running.</summary>
        private void CancelRun()
        {
            var cts = _runCts;
            if (cts == null) return;
            try
            {
                cts.Cancel();
                RunStageLabel = "Cancelling…";
            }
            catch (ObjectDisposedException)
            {
                // The run completed between the null check and Cancel().
            }
        }

        private string _runStageLabel = string.Empty;
        public string RunStageLabel
        {
            get => _runStageLabel;
            private set
            {
                _runStageLabel = value;
                OnPropertyChanged();
                NotifyHeroStatusChanged();
            }
        }

        /// <summary>The last successfully applied engine stage ("QUICK" /
        /// "OPTIMIZE") — session save uses it to name and later re-run the
        /// session with the same stage.</summary>
        private string _lastRunId = string.Empty;
        private string _lastInputFingerprint = string.Empty;
        private string _lastRunStage = "QUICK";
        public string LastRunStage => _lastRunStage;

        // ── run ────────────────────────────────────────────────────────

        /// <summary>
        /// Validates inputs on the UI thread, then runs the packing engine
        /// per cutting group (glass composition) on a background thread —
        /// different glass never mixes on a sheet. Results are applied back
        /// on the dispatcher by <see cref="SafeFireAndForget"/>.
        /// </summary>
        private bool Run(string stage)
        {
            // A run that never starts must not leave the "reopen to REPORT"
            // flag dangling — clear it on every early exit.
            if (IsRunning || IsDisposed) { _openToReportAfterRun = false; return false; }
            var blocked = SelectedJobs.FirstOrDefault(j => !j.CanOptimizeMaterial);
            if (blocked != null)
            {
                Blocked("Cannot optimize: " + blocked.MaterialInvalidReason, "Cannot optimize");
                _openToReportAfterRun = false;
                return false;
            }
            if (!TryBuildInputs(out var pieces, out var stockPool, out var margins))
            { _openToReportAfterRun = false; return false; }

            int effort = int.TryParse(SelectedEffortLevel, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int lvl) && lvl >= 1 && lvl <= 5 ? lvl : 3;
            bool autoEffort = UseAutoEffort;
            var rotation = RotationEnabled ? RotationPolicy.Free90 : RotationPolicy.Locked;
            var groups = BuildCuttingGroups(pieces);

            // SIZES dropdown: optimize ONE pane group at a time when a
            // specific composition (outer / inner / …) is selected.
            if (!string.Equals(_selectedCuttingGroup, AllCuttingGroups, StringComparison.OrdinalIgnoreCase))
            {
                groups = groups
                    .Where(g => string.Equals(g.Composition, _selectedCuttingGroup, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (groups.Count == 0)
                {
                    _openToReportAfterRun = false;
                    Blocked("The selected cutting group has no valid pieces.", "Cannot optimize");
                    return false;
                }
            }

            foreach (var group in groups)
            {
                if (ResolveGroupStocks(group.Composition, stockPool, out _).Count == 0)
                {
                    Blocked($"No matching stock for {group.Composition}. Import its Step 4 sheet or add a manual rack.", "Cannot optimize");
                    _openToReportAfterRun = false;
                    return false;
                }
            }
            _lastRunId = Guid.NewGuid().ToString("N");
            _lastInputFingerprint = OptimizationSessionIdentity.FingerprintRows(Pieces.Select(ToPieceRecord));
            OptimizationResultFeed.Invalidate("optimization started");
            HasResult = false;
            IsRunning = true;
            RunStageLabel = stage == "OPTIMIZE"
                ? autoEffort
                    ? $"Optimizing {groups.Count.ToString(CultureInfo.InvariantCulture)} cutting group(s) · Auto effort…"
                    : $"Optimizing {groups.Count.ToString(CultureInfo.InvariantCulture)} cutting group(s) · level {effort.ToString(CultureInfo.InvariantCulture)}…"
                : $"Quick packing {groups.Count.ToString(CultureInfo.InvariantCulture)} cutting group(s)…";
            RunMessage = "";

            int totalParts = groups.Sum(g => g.Pieces.Count);
            RunScaleLabel = string.Format(CultureInfo.InvariantCulture,
                autoEffort
                    ? "{0} cutting group(s) · {1} part(s) · Auto effort"
                    : "{0} cutting group(s) · {1} part(s) · SETTINGS level {2}",
                groups.Count, totalParts, SelectedEffortLevel);
            string finiteNote = stage == "OPTIMIZE" && stockPool.Any(s => s.Stock.MaxSheets > 0 && s.Stock.MaxSheets < StockSize.EffectivelyUnlimitedSheets)
                ? " Finite inventory uses globally capped search instead of partitioning."
                : string.Empty;
            RunArchitectureLabel = autoEffort && stage == "OPTIMIZE"
                ? "Auto effort — each cutting group uses stage 3, 4, or 5 from the piece count, stock mix, and rack cap. Yield is not changed." + finiteNote
                : BuildRunArchitectureLabel(stage, groups.Max(g => g.Pieces.Count), effort) + finiteNote;
            RunProgressPercent = 0;
            RunProgressDetail = stage == "OPTIMIZE"
                ? "Preparing multi-start search…"
                : "Preparing quick pass…";

            _runCts?.Dispose();
            _runCts = new CancellationTokenSource();
            OnPropertyChanged(nameof(CanCancel));
            StartRunClock();

            AsyncUtility.SafeFireAndForget(
                () => RunCoreAsync(stage, groups, stockPool, margins, rotation, effort, autoEffort),
                "Optimization run");
            return true;
        }

        /// <summary>Groups the piece list by glass composition — one pack run
        /// each. Keys are trimmed so the run composition exactly matches the
        /// SIZES merge, the STOCKS rack tags and the industrial report.</summary>
        private static List<(string Composition, List<CutPiece> Pieces)> BuildCuttingGroups(List<CutPiece> pieces)
        {
            return pieces
                .GroupBy(p => string.IsNullOrWhiteSpace(p.GlassRef)
                        ? "(unlabelled glass)" : p.GlassRef.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => (g.Key, g.ToList()))
                .ToList();
        }

        /// <summary>
        /// Thickness-aware margins: when a cutting group's glass thickness
        /// differs from the MARGINS tab, the group is cut with that
        /// thickness's house defaults and the note surfaces on the result.
        /// </summary>
        private static (MarginProfile Margins, string Note) ResolveGroupMargins(
            string composition, MarginProfile userMargins, int paneThicknessMm = 0)
        {
            if (userMargins.IsCustomized) return (userMargins, "custom margins");
            int thickness = paneThicknessMm > 0 ? paneThicknessMm : ParseThicknessMm(composition);
            if (thickness <= 0 || thickness == userMargins.ThicknessMm)
                return (userMargins, string.Empty);
            return (MarginProfile.DefaultFor(thickness, userMargins.MinBreakoutMm),
                "cut with " + thickness.ToString(CultureInfo.InvariantCulture) + "mm house margins");
        }

        /// <summary>
        /// Picks the stock sizes for ONE cutting group. The group's OWN
        /// Step 4 rack (tagged with its glass composition) always wins;
        /// manual "any group" racks (empty composition) are the fallback,
        /// and only when no Step 4 sheet is recorded for the glass do we
        /// drop back to the whole pool so pieces are never silently lost.
        /// A manual rack must never be offered to a group that already has
        /// a dedicated rack — otherwise "smallest stock first" can route a
        /// spec's pieces onto the wrong rack.
        /// </summary>
        private static List<StockSize> ResolveGroupStocks(
            string composition,
            List<(StockSize Stock, string Composition)> pool,
            out string note)
        {
            note = string.Empty;

            // 1. Dedicated Step 4 racks for this glass.
            var dedicated = pool
                .Where(t => !string.IsNullOrWhiteSpace(t.Composition)
                            && string.Equals(t.Composition, composition, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Stock)
                .ToList();
            dedicated = DistinctBySize(dedicated);
            if (dedicated.Count > 0) return dedicated;

            // 2. Manual "any group" racks (no composition tag).
            var manual = pool
                .Where(t => string.IsNullOrWhiteSpace(t.Composition))
                .Select(t => t.Stock)
                .ToList();
            manual = DistinctBySize(manual);
            if (manual.Count > 0)
            {
                note = "no Step 4 sheet — packed on manual racks";
                return manual;
            }

            // Never cut a composition on another spec's tagged glass.
            note = "no matching stock";
            return new List<StockSize>();
        }

        private static List<StockSize> DistinctBySize(List<StockSize> stocks) =>
            stocks.GroupBy(s => (s.WidthMm, s.HeightMm))
                  .Select(g => g.First())
                  .ToList();

        private static (int W, int H, int T, string Color) BalanceKey(StockSize s) =>
            (s.WidthMm, s.HeightMm, s.ThicknessMm, (s.Color ?? string.Empty).Trim().ToUpperInvariant());

        /// <summary>
        /// The pool as the floor sees it RIGHT NOW: every capped rack reduced by
        /// what earlier cutting groups in this same run already consumed, and
        /// exhausted racks removed.
        ///
        /// PHASE 9 / finding B2. Racks absent from <paramref name="ledger"/> are
        /// unlimited — 0 is the <see cref="StockSize.UnlimitedSheets"/> sentinel,
        /// not "none left" — and pass through untouched. An exhausted rack is
        /// DROPPED rather than written as 0, because all three packer strategies
        /// read 0 as infinite glass; zeroing a spent rack would hand the engine an
        /// unlimited supply of the one rack that has run out.
        /// </summary>
        private static List<(StockSize Stock, string Composition)> ApplyRunBalance(
            List<(StockSize Stock, string Composition)> pool,
            Dictionary<(int W, int H, int T, string Color), long> ledger,
            out bool reduced)
        {
            reduced = false;
            if (ledger.Count == 0) return pool;

            var result = new List<(StockSize Stock, string Composition)>(pool.Count);

            foreach (var row in pool)
            {
                var key = BalanceKey(row.Stock);

                if (!ledger.TryGetValue(key, out long remaining))
                {
                    result.Add(row);   // unlimited
                    continue;
                }

                if (remaining <= 0)
                {
                    reduced = true;    // exhausted — drop it, never zero it
                    continue;
                }

                if (remaining >= row.Stock.MaxSheets)
                {
                    result.Add(row);
                    continue;
                }

                reduced = true;
                // remaining is strictly below this row's own cap here, and that cap
                // is a valid int, so the narrowing is in range by construction.
                result.Add((row.Stock.WithMaxSheets((int)remaining), row.Composition));
            }

            return result;
        }

        private async Task RunCoreAsync(
            string stage, List<(string Composition, List<CutPiece> Pieces)> groups,
            List<(StockSize Stock, string Composition)> stockPool,
            MarginProfile margins, RotationPolicy rotation, int effort, bool autoEffort)
        {
            // ── SCALE / ROBUSTNESS ────────────────────────────────────────
            // This method had try/finally with NO catch, and it is invoked
            // through SafeFireAndForget. An OutOfMemoryException from a very
            // large cutting group was therefore swallowed: the spinner cleared,
            // no results appeared, and the operator was told nothing about why.
            // Every failure mode now reports itself in the run header.
            var ct = _runCts?.Token ?? CancellationToken.None;
            int groupCount = groups.Count;
            int groupIndex = 0;
            var scaleNotes = new List<string>();
            var smartWins = new List<string>();
            _lastRunEffort = effort;
            _lastRunAutoEffort = autoEffort;

            try
            {
                // Duplicate aliases of one physical rack share a conservative cap.
                var balanceLedger = stockPool
                    .Where(r => r.Stock.MaxSheets > 0 && r.Stock.MaxSheets < StockSize.EffectivelyUnlimitedSheets)
                    .GroupBy(r => BalanceKey(r.Stock))
                    .ToDictionary(g => g.Key, g => (long)g.Min(r => r.Stock.MaxSheets));
                var runs = new List<GroupRun>();
                foreach (var (composition, groupPieces) in groups)
                {
                    groupIndex++;
                    ct.ThrowIfCancellationRequested();

                    var (groupMargins, marginNote) = ResolveGroupMargins(composition, margins,
                        groupPieces.FirstOrDefault()?.ThicknessMm ?? 0);

                    // Each cutting group packs against ITS OWN Step 4 rack
                    // (the sheet its pane was quoted on), falling back to
                    // manual "any group" racks only when no Step 4 sheet is
                    // recorded for that glass.
                    var effectivePool = ApplyRunBalance(stockPool, balanceLedger, out bool poolReduced);
                    var groupStocks = ResolveGroupStocks(composition, effectivePool, out string stockNote);
                    if (poolReduced) stockNote = "stock balance reduced by earlier cutting groups";
                    string note = string.IsNullOrWhiteSpace(stockNote)
                        ? marginNote
                        : (string.IsNullOrWhiteSpace(marginNote)
                            ? stockNote
                            : marginNote + " · " + stockNote);

                    // Progress<T> captures the UI synchronization context when
                    // it is constructed, so the engine can report from the
                    // worker thread and the label updates on the dispatcher.
                    int gi = groupIndex;
                    string progressRunId = _lastRunId;
                    var groupProgress = new Progress<OptimizeProgress>(p =>
                    {
                        if (IsDisposed || !IsRunning || ct.IsCancellationRequested || progressRunId != _lastRunId) return;
                        RunStageLabel = stage == "OPTIMIZE"
                            ? $"Optimizing {composition} · group {gi} of {groupCount} · {p.Message}"
                            : $"Quick packing {composition} · group {gi} of {groupCount}";
                        RunProgressDetail = p.Message;

                        // Overall completion = finished groups plus this group's
                        // own pass fraction, so a 12-group job moves smoothly
                        // instead of jumping once per group.
                        double done = groupCount <= 1
                            ? p.Fraction
                            : ((gi - 1) + p.Fraction) / groupCount;
                        RunProgressPercent = (int)Math.Round(done * 100.0);
                    });

                    // QUICK reports per-part progress as of 2026-09-17, so this
                    // is a seed rather than the whole story: it puts the bar at
                    // the right place for this group before the first part lands.
                    if (stage != "OPTIMIZE" && groupCount > 0)
                        RunProgressPercent = (int)Math.Round(((gi - 1) * 100.0) / groupCount);

                    var result = await Task.Run(() =>
                    {
                        if (groupStocks.Count == 0)
                        {
                            var exhausted = new OptimizationResult { Rotation = rotation };
                            exhausted.Unplaced.AddRange(groupPieces);
                            return exhausted;
                        }
                        bool finiteStock = groupStocks.Any(stock => stock.MaxSheets > 0
                            && stock.MaxSheets < StockSize.EffectivelyUnlimitedSheets);
                        int stockSizes = groupStocks
                            .Select(stock => (stock.WidthMm, stock.HeightMm))
                            .Distinct()
                            .Count();
                        var recommendation = autoEffort
                            ? OptimizationSearchGovernor.RecommendEffort(groupPieces.Count, stockSizes, finiteStock)
                            : default;
                        int groupEffort = autoEffort ? recommendation.Stage : effort;

                        // PHASE 1: the cut policy is resolved per group because
                        // the laminate rule depends on the glass, and glass is
                        // exactly what defines a cutting group.
                        var cutPolicy = CutPolicy.ResolveFor(
                            CurrentCutComplexity,
                            GuillotinePacker.GroupIsLaminated(groupPieces),
                            CurrentMinTrimWidthMm);
                        bool stagedRun = cutPolicy.IsDepthConstrained;

                        if (stage != "OPTIMIZE")
                        {
                            var quick = GuillotinePacker.PackQuick(groupPieces, groupStocks, groupMargins,
                                rotation, SplitMode.HorizontalFirst, ct, groupProgress,
                                platePattern: CurrentPlatePattern, fillEffort: groupEffort,
                                policy: cutPolicy, reuseOffcuts: UseOffcutReuse);
                            if (autoEffort && CurrentPlatePattern == PlatePattern.FillFirstPlate)
                                quick.ScaleNotes.Add(string.Format(CultureInfo.InvariantCulture,
                                    "Auto effort widened Fill first plate to stage {0}. The quick yield search was unchanged.",
                                    groupEffort));
                            else if (autoEffort)
                                quick.ScaleNotes.Add("Auto effort applies to Optimize. Quick kept the quick portfolio.");
                            return quick;
                        }
                        // The preserved partitioner gives each batch the rack cap independently.
                        // Finite inventory must use the globally capped advanced search instead.
                        // A staged pattern cannot be held by the partitioned
                        // (very large job) path, so a staged run takes the
                        // advanced search instead and says so — never silently
                        // hands back a free-guillotine plan.
                        var packed = (finiteStock || stagedRun)
                            ? GuillotinePacker.PackOptimize(groupPieces, groupStocks, groupMargins,
                                rotation, groupEffort, ct, groupProgress, CurrentPlatePattern, cutPolicy,
                                maxStage: CurrentMaxStage, reuseOffcuts: UseOffcutReuse)
                            : GuillotinePacker.PackOptimizePartitioned(groupPieces, groupStocks, groupMargins,
                                rotation, groupEffort, ct, groupProgress, platePattern: CurrentPlatePattern,
                                maxStage: CurrentMaxStage);
                        if (finiteStock) packed.ScaleNotes.Add("Finite inventory: global advanced search preserves the shared rack cap.");
                        if (stagedRun && !finiteStock)
                            packed.ScaleNotes.Add(string.Format(CultureInfo.InvariantCulture,
                                "Staged cut pattern ({0}): used the advanced search, because the very-large-job path cannot hold a staged pattern.",
                                cutPolicy.Describe()));
                        if (autoEffort)
                            packed.ScaleNotes.Add(string.Format(CultureInfo.InvariantCulture,
                                "Auto effort: stage {0} — {1}", groupEffort, recommendation.Reason));
                        return packed;
                    }, ct).ConfigureAwait(true);

                    foreach (var used in result.Sheets.GroupBy(sheet => BalanceKey(sheet.Stock)))
                        if (balanceLedger.TryGetValue(used.Key, out long remaining))
                            balanceLedger[used.Key] = remaining - used.Count();

                    // Size-driven search reductions are surfaced, never silent.
                    foreach (var scaleNote in result.ScaleNotes)
                        if (!scaleNotes.Contains(scaleNote)) scaleNotes.Add(scaleNote);

                    // Which strategy actually won this group. A multi-start run
                    // evaluates up to 147 complete plans and keeps one, so without
                    // this a better sheet count is unexplainable — and an
                    // unexplainable improvement is one nobody trusts.
                    if (string.Equals(result.Strategy, GuillotinePacker.SmartStrategyLabel,
                            StringComparison.Ordinal) && !smartWins.Contains(composition))
                        smartWins.Add(composition);

                    double glass = result.Sheets.Sum(s => s.Pieces.Sum(p => (long)p.WidthMm * p.HeightMm));
                    double stockArea = result.Sheets.Sum(s => (long)s.Stock.WidthMm * s.Stock.HeightMm);
                    int problems = await Task.Run(() =>
                        GuillotinePacker.VerifyComprehensive(result, groupPieces, groupMargins).Count, ct);

                    runs.Add(new GroupRun
                    {
                        Composition = composition,
                        Thickness = ParseThicknessLabel(composition),
                        PieceCount = groupPieces.Count,
                        Margins = groupMargins,
                        Result = result,
                        UtilizationPct = stockArea == 0 ? 0 : glass / stockArea * 100.0,
                        WasteM2 = Math.Max(0, (stockArea - glass) / 1_000_000.0),
                        UnplacedLabel = result.Unplaced.Count.ToString(CultureInfo.InvariantCulture),
                        OracleLabel = problems == 0 ? "Verified" : problems.ToString(CultureInfo.InvariantCulture) + " issue(s)",
                        ProblemCount = problems,
                        ValidationFailed = problems > 0,
                        SheetsLabel = result.SheetsUsed.ToString(CultureInfo.InvariantCulture),
                        MarginNote = note
                    });
                }

                ct.ThrowIfCancellationRequested();
                if (IsDisposed) return;
                ApplyResults(stage, runs, rotation);

                // APPEND, never replace: ApplyResults has just written the
                // sheet-count / utilization summary the operator reads first.
                // Overwriting it would hide the result exactly on the large
                // jobs where the note matters most.
                if (scaleNotes.Count > 0)
                {
                    string notes = string.Join(" ", scaleNotes);
                    RunMessage = string.IsNullOrWhiteSpace(RunMessage)
                        ? notes
                        : RunMessage + " " + notes;
                }

                // Kept alongside RunMessage, not instead of it: the joined
                // string is what the header shows, and the operator may have
                // grown used to reading it there.
                PublishRunNotes(scaleNotes, runs);

                // Same append-never-replace rule: the summary ApplyResults wrote
                // is what the operator reads first.
                if (smartWins.Count > 0)
                {
                    string smartNote = string.Format(CultureInfo.InvariantCulture,
                        "Smart placement won {0} of {1} cutting group(s): {2}.",
                        smartWins.Count, runs.Count, string.Join(", ", smartWins));
                    RunMessage = string.IsNullOrWhiteSpace(RunMessage)
                        ? smartNote
                        : RunMessage + " " + smartNote;
                }
            }
            catch (OperationCanceledException)
            {
                RunStageLabel = "Cancelled.";
                RunMessage = "Optimization cancelled. No results were applied.";
            }
            catch (OutOfMemoryException)
            {
                RunStageLabel = "Stopped — out of memory.";
                RunMessage = "Optimization stopped: this machine ran out of memory packing a group " +
                    "this large. Split the job into smaller cutting groups or close other " +
                    "applications, then run it again.";
            }
            catch (Exception ex)
            {
                RunStageLabel = "Failed.";
                RunMessage = $"Optimization failed: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
                StopRunClock();
                _runCts?.Dispose();
                _runCts = null;
                OnPropertyChanged(nameof(CanCancel));
                OnPropertyChanged(nameof(CanUpgradeToOptimize));
            }
        }

        private void ApplyResults(string stage, List<GroupRun> runs, RotationPolicy rotation)
        {
            _lastRunStage = stage;

            // Result summary first, then the per-group breakdown ordered
            // MAX → lowest utilization (the owner's preferred reading order).
            runs = runs.OrderByDescending(r => r.UtilizationPct).ToList();

            _groupRuns.Clear();
            _groupRuns.AddRange(runs);

            double glass = runs.Sum(r => r.Result.Sheets.Sum(s => s.Pieces.Sum(p => (long)p.WidthMm * p.HeightMm)));
            double stockArea = runs.Sum(r => r.Result.Sheets.Sum(s => (long)s.Stock.WidthMm * s.Stock.HeightMm));
            double util = stockArea == 0 ? 0 : glass / stockArea * 100.0;
            int totalSheets = runs.Sum(r => r.Result.SheetsUsed);
            int totalUnplaced = runs.Sum(r => r.Result.Unplaced.Count);
            // Reuse the count computed when each group finished. Re-running
            // Verify here doubled the oracle cost of every run.
            int totalProblems = runs.Sum(r => r.ProblemCount);

            string rotationLabel = rotation == RotationPolicy.Free90 ? "rotation ON" : "rotation LOCKED";
            string patternLabel = PlatePatternStamp();
            string effortLabel = _lastRunAutoEffort ? "auto" : "level " + SelectedEffortLevel;
            ResultStage = stage == "OPTIMIZE"
                ? $"OPTIMIZE · {effortLabel} · {patternLabel} · {rotationLabel} · {runs.Count} cutting group(s)"
                : $"QUICK · {patternLabel} · {rotationLabel} · {runs.Count} cutting group(s)";
            // Stamps the SETTINGS level onto this result and, when this run was
            // an upgrade of a previous QUICK result, reports what it bought.
            UpdateUpgradeSummary(stage, totalSheets);

            ResultSheets = totalSheets.ToString(CultureInfo.InvariantCulture);
            ResultUtilization = util.ToString("0.00", CultureInfo.InvariantCulture) + "%";
            ResultUnplaced = totalUnplaced.ToString(CultureInfo.InvariantCulture);
            OraclePassed = totalProblems == 0;
            ResultOracle = totalProblems == 0
                ? "Verified — conservation, margins, kerf & breakout checked"
                : string.Format(CultureInfo.InvariantCulture, "{0} layout issue(s)", totalProblems);

            // Waste = consumed stock − placed glass (the offcut the saw leaves).
            double wasteMm2 = Math.Max(0, stockArea - glass);
            ResultWaste = (wasteMm2 / 1_000_000.0).ToString("0.00", CultureInfo.InvariantCulture) + " m²";
            ResultWastePct = stockArea == 0 ? "0.0%"
                : ((stockArea - glass) / stockArea * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%";

            UnplacedRows.Clear();
            // SCALE FIX: a job that cannot be placed pushes one string per part
            // into a bound ObservableCollection, on the dispatcher, one
            // CollectionChanged each. At 30,000 parts that is a frozen window
            // rather than a list — and the list is not even the number the
            // operator needs, because ResultUnplaced already carries the true
            // total. So this shows a sample and says that it is a sample.
            const int unplacedRowCap = 500;
            int unplacedListed = 0;
            int unplacedSeen = 0;
            foreach (var r in runs)
            {
                foreach (var up in r.Result.Unplaced)
                {
                    unplacedSeen++;
                    if (unplacedListed >= unplacedRowCap) continue;
                    string label = string.IsNullOrWhiteSpace(up.PieceLabel) ? up.GlassRef : up.PieceLabel;
                    UnplacedRows.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0} · {1} · {2} × {3} mm",
                        r.Composition, label, up.WidthMm, up.HeightMm));
                    unplacedListed++;
                }
            }
            if (unplacedSeen > unplacedListed)
            {
                UnplacedRows.Add(string.Format(CultureInfo.InvariantCulture,
                    "…and {0} more. The UNPLACED total above is the full count.",
                    unplacedSeen - unplacedListed));
            }
            HasUnplaced = unplacedSeen > 0;
            HasUnplacedOverflow = unplacedSeen > unplacedListed;
            UnplacedOverflowLabel = HasUnplacedOverflow
                ? string.Format(CultureInfo.InvariantCulture,
                    "…and {0:N0} more unplaced pieces (the full list is in the REPORT export).",
                    unplacedSeen - unplacedListed)
                : string.Empty;

            // Per-cutting-group breakdown (composition → sheets / util / waste).
            GroupResults.Clear();
            foreach (var r in runs)
            {
                GroupResults.Add(new CuttingGroupResultVm
                {
                    Composition = r.Composition,
                    Thickness = r.Thickness,
                    StockSize = string.Join(", ", r.Result.Sheets
                        .Select(sheet => string.Format(CultureInfo.InvariantCulture,
                            "{0}×{1}", sheet.Stock.WidthMm, sheet.Stock.HeightMm))
                        .Distinct()),
                    PieceCount = r.PieceCount.ToString(CultureInfo.InvariantCulture),
                    Sheets = r.SheetsLabel,
                    Utilization = r.UtilizationPct.ToString("0.0", CultureInfo.InvariantCulture) + "%",
                    Waste = r.WasteM2.ToString("0.00", CultureInfo.InvariantCulture) + " m²",
                    Unplaced = r.UnplacedLabel,
                    Note = r.MarginNote
                });
            }

            // Filmstrip: flatten every sheet across all cutting groups.
            _flatSheets.Clear();
            SheetRows.Clear();
            int flatIndex = 0;
            foreach (var r in runs)
            {
                r.Result.Sheets.Sort((a, b) =>
                {
                    long aa = a.Pieces.Sum(p => (long)p.WidthMm * p.HeightMm);
                    long ba = b.Pieces.Sum(p => (long)p.WidthMm * p.HeightMm);
                    return ba.CompareTo(aa);
                });
                for (int i = 0; i < r.Result.Sheets.Count; i++)
                    r.Result.Sheets[i].SheetNo = i + 1;

                for (int i = 0; i < r.Result.Sheets.Count; i++)
                {
                    var sheet = r.Result.Sheets[i];
                    double sheetGlass = sheet.Pieces.Sum(p => (long)p.WidthMm * p.HeightMm);
                    double sheetStock = (long)sheet.Stock.WidthMm * sheet.Stock.HeightMm;
                    double sheetWaste = sheetStock - sheetGlass;
                    double sheetUtil = sheetStock == 0 ? 0 : sheetGlass / sheetStock * 100.0;
                    _flatSheets.Add(new FlatSheet { Run = r, SheetIndex = i });
                    if (SheetRows.Count < MaxSheetRows)
                    {
                        // On-screen rows cap (the ItemsControls don't
                        // virtualize); _flatSheets stays complete so the 2D
                        // preview, navigation and export see every sheet.
                        SheetRows.Add(new SheetRowVm
                        {
                            SheetNo = (flatIndex + 1).ToString(CultureInfo.InvariantCulture),
                            StockLabel = string.Format(CultureInfo.InvariantCulture,
                                "{0} × {1}", sheet.Stock.WidthMm, sheet.Stock.HeightMm),
                            PieceCount = sheet.Pieces.Count.ToString(CultureInfo.InvariantCulture),
                            Utilization = sheetUtil.ToString("0.0", CultureInfo.InvariantCulture) + "%",
                            UtilizationTone = sheetUtil >= 85.0 ? "Good" : sheetUtil >= 70.0 ? "Mid" : "Low",
                            WasteLabel = (sheetWaste / 1_000_000.0).ToString("0.00", CultureInfo.InvariantCulture) + " m²",
                            Composition = r.Composition,
                            Index = flatIndex
                        });
                    }
                    flatIndex++;
                }
            }

            HasSheetRowsOverflow = flatIndex > SheetRows.Count;
            SheetRowsOverflowLabel = HasSheetRowsOverflow
                ? string.Format(CultureInfo.InvariantCulture,
                    "Showing first {0:N0} of {1:N0} sheets — the full list is in the REPORT export.",
                    SheetRows.Count, flatIndex)
                : string.Empty;

            HasResult = true;
            _selectedSheetIndex = -1;
            SelectedSheetIndex = 0;
            RebuildPreview();

            RefreshStockUsage(runs);

            // Priced plan from the same runs, keyed the same way as the usage
            // tally above so the two never disagree about which rack is which.
            RebuildCosting(runs);

            // Hand the finished plan to Step 6 / Step 9 without waiting for the
            // operator to press SAVE on a session.
            PublishLiveFeed();

            // ...and keep a durable copy, so a quote reopened in a LATER app
            // session can still be checked against this run. One reserved row,
            // replaced each run — auto-saving named sessions would bury the ones
            // the operator chose to keep.
            AsyncUtility.SafeFireAndForget(
                () => PersistLatestRunAsync(), "Optimization latest-run snapshot");

            RebuildIndustrialReport();

            RunMessage = string.Format(CultureInfo.InvariantCulture,
                "{0}: {1} cutting group(s), {2} sheet(s), {3:0.00}% utilization, {4} unplaced.",
                stage, runs.Count, totalSheets, util, totalUnplaced);

            RememberRunForUpgrade(stage, totalSheets);

            // The CUT LIST tab reads this run back per row. Stamping here means
            // its QTY USED and OPT % columns are current the moment the run
            // lands, with no second click — and it is a no-op until that tab has
            // rows of its own, so a proforma-driven run costs nothing here.
            StampCutListResults();

            // A session reopen re-runs the saved stage and lands on the
            // REPORT tab; a manual run lands on RESULT.
            SelectedTabIndex = _openToReportAfterRun ? TabIndexReport : TabIndexSummary;
            _openToReportAfterRun = false;
        }

        /// <summary>
        /// Live-connects the run to the STOCKS tab: each rack shows how many
        /// sheets this run consumed. Availability stays unlimited by default
        /// (99999999) — usage is tracked, not capped.
        /// </summary>
    }
}
