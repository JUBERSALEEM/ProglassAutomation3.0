// ══════════════════════════════════════════════════════════════════
// FILE: ViewModels/Optimization/OptimizationViewModel.Progress.cs
// PROJECT: ProGlassAutomation
// MODULE: Optimization — run progress screen, effort stamping, upgrade
//
// WHY THIS FILE
//   A large job (many cutting groups, thousands of parts) used to show only a
//   3px indeterminate bar in the header, so a run that legitimately takes
//   minutes was indistinguishable from a hung UI. This adds the state a real
//   progress screen needs — determinate percentage, what the engine is doing
//   right now, elapsed time — plus two things the owner asked for:
//
//     • every result is STAMPED with the optimization level from SETTINGS, so
//       a plan always says which effort produced it;
//     • a QUICK result can be UPGRADED to OPTIMIZE in one click, and the
//       before/after sheet count is reported instead of silently replaced.
//
//   All of it is additive: nothing here changes how the engine packs.
// ══════════════════════════════════════════════════════════════════

using System;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using ProGlassAutomation.Commands;

namespace ProGlassAutomation.ViewModels.Optimization
{
    public partial class OptimizationViewModel
    {
        // ── progress screen state ────────────────────────────────────────

        private int _runProgressPercent;
        /// <summary>Overall run completion, 0–100. Across groups this is
        /// (finished groups + current group's pass fraction) / group count, so
        /// a 12-group job moves smoothly instead of jumping per group.</summary>
        public int RunProgressPercent
        {
            get => _runProgressPercent;
            private set
            {
                int clamped = value < 0 ? 0 : (value > 100 ? 100 : value);
                if (_runProgressPercent == clamped) return;
                _runProgressPercent = clamped;
                OnPropertyChanged();
            }
        }

        private string _runProgressDetail = string.Empty;
        /// <summary>What the engine is doing right now — the current cutting
        /// group and, for OPTIMIZE, the candidate pass and its ordering.</summary>
        public string RunProgressDetail
        {
            get => _runProgressDetail;
            private set
            {
                if (_runProgressDetail == value) return;
                _runProgressDetail = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        private string _runElapsedLabel = "0:00";
        /// <summary>Wall time since the run started, ticking while it runs.</summary>
        public string RunElapsedLabel
        {
            get => _runElapsedLabel;
            private set
            {
                if (_runElapsedLabel == value) return;
                _runElapsedLabel = value;
                OnPropertyChanged();
            }
        }

        private string _runScaleLabel = string.Empty;
        /// <summary>Size of the job being run, so the operator can see why a run
        /// is taking time ("12 cutting groups · 8,412 parts").</summary>
        public string RunScaleLabel
        {
            get => _runScaleLabel;
            private set
            {
                if (_runScaleLabel == value) return;
                _runScaleLabel = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasRunScale));
            }
        }

        /// <summary>True when a job-size line is available to show.</summary>
        public bool HasRunScale => !string.IsNullOrWhiteSpace(_runScaleLabel);

        private string _runArchitectureLabel = string.Empty;
        /// <summary>
        /// The search architecture the CURRENT run actually uses, derived
        /// from the engine's real tiering (part count + effort). The
        /// progress screen used to show a static "Stratified Proportional
        /// Batches (K=400)" line that matched nothing in the engine — the
        /// operator was told an algorithm that was never run. This label is
        /// built from the same thresholds GuillotinePacker uses, so what the
        /// screen says and what the run does agree.
        /// </summary>
        public string RunArchitectureLabel
        {
            get => _runArchitectureLabel;
            private set
            {
                if (_runArchitectureLabel == value) return;
                _runArchitectureLabel = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        /// <summary>True while the progress screen should be on display.</summary>
        public bool IsRunProgressVisible => _isRunning;

        /// <summary>
        /// Mirrors the engine's ACTUAL routing, so the line on screen is the
        /// architecture the run performs:
        ///   • groups of ≤2,000 parts → GuillotinePacker.PackOptimize — the
        ///     full multi-candidate search, breadth growing with effort level;
        ///   • any group over 2,000 parts → GuillotinePacker.
        ///     PackOptimizePartitioned — stratified production batches plus a
        ///     global multi-pass baseline (the two are compared, best kept).
        /// The words below name the search that stage actually runs.
        /// They are not the old pattern-family titles.
        /// </summary>
        private static string BuildRunArchitectureLabel(string stage, int maxGroupParts, int effort)
        {
            if (!string.Equals(stage, "OPTIMIZE", StringComparison.Ordinal) || effort <= 1)
                return maxGroupParts >= 100000
                    ? "QUICK industrial — count-based pattern master, scalable strip floors and bounded remainder repair; all panes retained"
                    : "QUICK — area shelf, capacity strips, bounded patterns, and repeated quantities. This is not stages 2 to 5.";

            if (maxGroupParts >= 100000)
                return "Industrial quantity search — count-based pattern master, scalable strip floors and bounded remainder search; no whole-order quadratic beam";

            if (maxGroupParts <= 2000)
                return effort == 2
                        ? "Stage 2 — three orderings, best-split, sheet-fill, wall-seed, and smart placement"
                        : effort == 3
                            ? "Stage 3 — twelve ordering and split candidates, best-split, sheet-fill, wall-seed, smart placement, MaxRects, and beam 4"
                            : effort == 4
                                ? "Stage 4 — all six orderings, shuffles, vertical smart placement, and beam 8"
                                : "Stage 5 — widest shuffle set and MaxRects on every ordering. A better plan already found is kept.";

            // > 2,000 parts per group → PackOptimizePartitioned.
            return "Stratified partitioned search — anchor/medium/filler production batches, per-batch bounded pattern repair and multi-strategy passes, global quantity-aware QUICK floor, cross-batch consolidation, tail repack and unplaced recovery";
        }

        private DispatcherTimer _runClock;
        private DateTime _runStartedAt;

        /// <summary>Starts the elapsed-time clock. Called on the UI thread just
        /// before the run is handed to the background.</summary>
        private void StartRunClock()
        {
            _runStartedAt = DateTime.UtcNow;
            RunElapsedLabel = FormatElapsed(TimeSpan.Zero);

            if (_runClock == null)
            {
                _runClock = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                _runClock.Tick += OnRunClockTick;
            }
            _runClock.Start();
        }

        private void StopRunClock() => _runClock?.Stop();

        private void OnRunClockTick(object sender, EventArgs e) =>
            RunElapsedLabel = FormatElapsed(DateTime.UtcNow - _runStartedAt);

        private static string FormatElapsed(TimeSpan t) =>
            t.TotalHours >= 1.0
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}",
                      (int)t.TotalHours, t.Minutes, t.Seconds)
                : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", t.Minutes, t.Seconds);

        // ── effort stamping ──────────────────────────────────────────────

        private int _lastRunEffort;
        private bool _lastRunAutoEffort;

        private string _resultEffortLabel = string.Empty;
        /// <summary>Which optimization level produced the current result, read
        /// from SETTINGS. Shown on the result and written into the report so a
        /// plan can never be mistaken for one from a different level.</summary>
        public string ResultEffortLabel
        {
            get => _resultEffortLabel;
            private set
            {
                if (_resultEffortLabel == value) return;
                _resultEffortLabel = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasResultEffort));
                NotifyHeroStatusChanged();
            }
        }

        /// <summary>True once a run has stamped its level.</summary>
        public bool HasResultEffort => !string.IsNullOrWhiteSpace(_resultEffortLabel);

        /// <summary>The level the next OPTIMIZE run will use, from SETTINGS.</summary>
        public string SettingsEffortLabel => SelectedEffortLevel;

        // ── QUICK → OPTIMIZE upgrade ─────────────────────────────────────

        private string _previousRunStage = string.Empty;
        private int _previousRunSheetCount;

        /// <summary>A QUICK result can be deepened to OPTIMIZE at the SETTINGS
        /// level. Hidden once the result already came from OPTIMIZE.</summary>
        public bool CanUpgradeToOptimize =>
            _hasResult && !_isRunning &&
            string.Equals(_lastRunStage, "QUICK", StringComparison.Ordinal);

        public ICommand UpgradeToOptimizeCommand { get; private set; }

        private string _upgradeSummaryLabel = string.Empty;
        /// <summary>What the deeper search actually bought, e.g. "OPTIMIZE
        /// level 5 saved 13 sheet(s) over QUICK (354 → 341)".</summary>
        public string UpgradeSummaryLabel
        {
            get => _upgradeSummaryLabel;
            private set
            {
                if (_upgradeSummaryLabel == value) return;
                _upgradeSummaryLabel = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUpgradeSummary));
                NotifyHeroStatusChanged();
            }
        }

        /// <summary>True when an upgrade comparison is available to show.</summary>
        public bool HasUpgradeSummary => !string.IsNullOrWhiteSpace(_upgradeSummaryLabel);

        /// <summary>Re-runs the current cutting list through OPTIMIZE at the
        /// level chosen in SETTINGS, keeping the QUICK result for comparison.</summary>
        private void UpgradeToOptimize()
        {
            if (!CanUpgradeToOptimize) return;
            Run(stage: "OPTIMIZE");
        }

        /// <summary>Records the stage/sheet count of the run that just finished
        /// so the NEXT run can compare against it. Called at the end of
        /// ApplyResults, after the comparison has been read.</summary>
        private void RememberRunForUpgrade(string stage, int totalSheets)
        {
            _previousRunStage = stage;
            _previousRunSheetCount = totalSheets;
        }

        /// <summary>Builds the QUICK → OPTIMIZE comparison line, if this run was
        /// an upgrade of a previous QUICK result.</summary>
        private void UpdateUpgradeSummary(string stage, int totalSheets)
        {
            ResultEffortLabel = stage == "OPTIMIZE"
                ? _lastRunAutoEffort
                    ? string.Format(CultureInfo.InvariantCulture,
                        "Auto effort · existing stage per cutting group · {0}", PlatePatternStamp())
                    : string.Format(CultureInfo.InvariantCulture,
                        "Optimization level {0} of 5 · {1} (from SETTINGS)", _lastRunEffort, PlatePatternStamp())
                : string.Format(CultureInfo.InvariantCulture,
                      "Quick single pass · {1} — SETTINGS level {0} available via Upgrade to Optimize",
                      _lastRunEffort, PlatePatternStamp());

            bool isUpgrade = stage == "OPTIMIZE"
                && string.Equals(_previousRunStage, "QUICK", StringComparison.Ordinal)
                && _previousRunSheetCount > 0;

            if (!isUpgrade)
            {
                UpgradeSummaryLabel = string.Empty;
                return;
            }

            int saved = _previousRunSheetCount - totalSheets;
            string levelWord = _lastRunAutoEffort
                ? "Auto effort"
                : "Level " + _lastRunEffort.ToString(CultureInfo.InvariantCulture);
            UpgradeSummaryLabel = saved > 0
                ? string.Format(CultureInfo.InvariantCulture,
                      "{0} saved {1} sheet(s) over QUICK: {2} → {3}.",
                      levelWord, saved, _previousRunSheetCount, totalSheets)
                : saved == 0
                    ? string.Format(CultureInfo.InvariantCulture,
                          "{0} matched QUICK at {1} sheets — this job was already optimal.",
                          levelWord, totalSheets)
                    : string.Format(CultureInfo.InvariantCulture,
                          "{0} returned {1} sheets vs QUICK {2}.",
                          levelWord, totalSheets, _previousRunSheetCount);
        }
    }
}
