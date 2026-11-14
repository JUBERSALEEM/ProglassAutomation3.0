// ═══════════════════════════════════════════════════════════════
// FILE: OptimizationViewModel.Margins.cs
// MODULE: Optimization — SETTINGS — cutting margins, kerf, breakout, rotation and input build (partial of OptimizationViewModel)
// ═══════════════════════════════════════════════════════════════

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
using ProGlassAutomation.Services.Optimization;
using ProGlassAutomation.Services.Production;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss;

namespace ProGlassAutomation.ViewModels.Optimization
{
    public partial class OptimizationViewModel : ViewModels.ViewModelBase
    {

        // ── MARGINS tab ────────────────────────────────────────────────

        private string _thicknessText = "6";

        /// <summary>
        /// Glass thickness driving the house margin profile.
        ///
        /// PHASE 1 / finding 1d (2026-09-20, owner requirement: "auto select as
        /// per thickness trigger"): changing thickness now AUTO-SELECTS that
        /// bucket's house margins — but only while the operator has not touched
        /// them. A customized SETTINGS tab is never overwritten underneath the
        /// operator; that is the same destructive-refresh class of bug as
        /// RC-016, where navigating into a station silently blanked work the
        /// operator had already done. When the fields are customized the hint
        /// names the house numbers and offers the one-click instead.
        /// </summary>
        public string ThicknessText
        {
            get => _thicknessText;
            set
            {
                if (string.Equals(_thicknessText, value, StringComparison.Ordinal)) return;

                int previousThickness = CurrentThicknessMm;
                _thicknessText = value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentThicknessMm));
                OnPropertyChanged(nameof(HasBreakoutChoice));
                OnPropertyChanged(nameof(BreakoutHint));

                AutoSelectHouseDefaultsOnThicknessChange(previousThickness);

                OnPropertyChanged(nameof(MarginHint));
            }
        }

        /// <summary>Thickness as the engine will read it: unparseable or
        /// non-positive falls back to 6mm, the long-standing default.</summary>
        public int CurrentThicknessMm =>
            TryParseWholeMm(ThicknessText, out int t) && t > 0 ? t : 6;

        /// <summary>
        /// True when the current thickness offers the 50 / 40 breakout choice —
        /// i.e. the thick (9mm and above) house bucket.
        /// </summary>
        public bool HasBreakoutChoice => MarginProfile.HasBreakoutChoice(CurrentThicknessMm);

        /// <summary>The two sanctioned breakout values on thick glass, as text.</summary>
        public ObservableCollection<string> BreakoutOptions { get; } = new()
        {
            MarginProfile.BreakoutPrimaryMm.ToString(CultureInfo.InvariantCulture),
            MarginProfile.BreakoutAlternateMm.ToString(CultureInfo.InvariantCulture)
        };

        private string? _selectedBreakoutOption;

        /// <summary>
        /// The 50 / 40 breakout switch. Writes through to
        /// <see cref="MinBreakoutText"/>, which stays the single source of
        /// truth so session persistence and the engine input path are unchanged.
        /// </summary>
        public string? SelectedBreakoutOption
        {
            get => _selectedBreakoutOption;
            set
            {
                if (string.Equals(_selectedBreakoutOption, value, StringComparison.Ordinal)) return;
                _selectedBreakoutOption = value;
                OnPropertyChanged();

                if (TryParseWholeMm(value, out int mm) && mm > 0)
                    MinBreakoutText = mm.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Explains the breakout choice where it applies.</summary>
        public string BreakoutHint => HasBreakoutChoice
            ? $"{MarginProfile.BreakoutPrimaryMm}mm is the house default on thick glass. " +
              $"Switch to {MarginProfile.BreakoutAlternateMm}mm when the narrower offcut is still worth keeping."
            : "The 50 / 40 choice applies to 9mm and above only — on this thickness the breakout equals the margin.";

        /// <summary>
        /// Names the house profile for the current thickness, and — when the
        /// operator has customized the fields — says so and offers the
        /// one-click. Never overwrites customized values on its own.
        /// </summary>
        public string MarginHint
        {
            get
            {
                int thickness = CurrentThicknessMm;
                var house = MarginProfile.DefaultFor(thickness);
                string houseText = string.Format(CultureInfo.InvariantCulture,
                    "House defaults for {0}mm ({1}): LM {2} · RM {3} · TM {4} · BM {5} · kerf {6} · breakout {7}{8}.",
                    thickness, DescribeBucket(thickness),
                    house.LeftMm, house.RightMm, house.TopMm, house.BottomMm, house.KerfMm,
                    house.MinBreakoutMm,
                    HasBreakoutChoice ? $" (or {MarginProfile.BreakoutAlternateMm})" : string.Empty);

                return FieldsAreHouseDefaults(thickness)
                    ? houseText
                    : houseText + " Your values differ — they will be used exactly as entered. " +
                      "Press LOAD HOUSE DEFAULTS to take the table above.";
            }
        }

        private static string DescribeBucket(int thickness) =>
            MarginProfile.BucketFor(thickness) switch
            {
                ThicknessBucket.Thick => "LM/RM/BM 50, TM 0 — top edge left untrimmed",
                ThicknessBucket.Medium => "30mm on all four edges",
                _ => "15mm on all four edges"
            };

        private string _leftMarginText = "15";
        private string _rightMarginText = "15";
        private string _topMarginText = "15";
        private string _bottomMarginText = "15";
        private string _kerfText = "3";
        private string _minBreakoutText = "15";
        private bool _rotationEnabled = true; // floor default: rotation ON for all pieces

        public string LeftMarginText { get => _leftMarginText; set { _leftMarginText = value; OnPropertyChanged(); OnPropertyChanged(nameof(MarginHint)); } }
        public string RightMarginText { get => _rightMarginText; set { _rightMarginText = value; OnPropertyChanged(); OnPropertyChanged(nameof(MarginHint)); } }
        public string TopMarginText { get => _topMarginText; set { _topMarginText = value; OnPropertyChanged(); OnPropertyChanged(nameof(MarginHint)); } }
        public string BottomMarginText { get => _bottomMarginText; set { _bottomMarginText = value; OnPropertyChanged(); OnPropertyChanged(nameof(MarginHint)); } }
        public string KerfText { get => _kerfText; set { _kerfText = value; OnPropertyChanged(); OnPropertyChanged(nameof(MarginHint)); } }

        /// <summary>
        /// Minimum breakout. Kept as free text (a session restores into it, and
        /// the engine reads it), with <see cref="SelectedBreakoutOption"/> as the
        /// sanctioned 50 / 40 switch on thick glass.
        /// </summary>
        public string MinBreakoutText
        {
            get => _minBreakoutText;
            set
            {
                if (string.Equals(_minBreakoutText, value, StringComparison.Ordinal)) return;
                _minBreakoutText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MarginHint));

                // Keep the switch in step with the text, so the ComboBox never
                // shows a value that is not what the engine will use.
                string? matched = BreakoutOptions.FirstOrDefault(o =>
                    string.Equals(o, (value ?? string.Empty).Trim(), StringComparison.Ordinal));
                if (!string.Equals(_selectedBreakoutOption, matched, StringComparison.Ordinal))
                {
                    _selectedBreakoutOption = matched;
                    OnPropertyChanged(nameof(SelectedBreakoutOption));
                }
            }
        }

        public bool RotationEnabled { get => _rotationEnabled; set { _rotationEnabled = value; OnPropertyChanged(); } }

        public ICommand LoadHouseDefaultsCommand { get; }

        public ObservableCollection<string> EffortLevels { get; } = new() { "1", "2", "3", "4", "5" };
        private string _selectedEffortLevel = "3";
        public string SelectedEffortLevel
        {
            get => _selectedEffortLevel;
            set
            {
                if (_selectedEffortLevel != value)
                {
                    _selectedEffortLevel = value;
                    OnPropertyChanged();

                    // PHASE 5a: effort no longer drags the search family with
                    // it. Effort is how hard the chosen family searches; the
                    // family is chosen on its own control.
                    OnPropertyChanged(nameof(SelectedStageStrategyHint));
                }
            }
        }

        public const string YieldPlatePatternLabel = "Yield";
        public const string FillFirstPlatePatternLabel = "Fill first plate";
        public const string FewestCutsPatternLabel = "Fewest cuts";
        public const string MinimiseStockChangesPatternLabel = "Minimise stock changes";
        public const string RemnantFirstPatternLabel = "Remnant-first (leave reusable offcuts)";
        public const string BalancedSpreadPatternLabel = "Balanced spread";
        private bool _useOffcutReuse;

        /// <summary>
        /// PHASE 2 part 2. When true the run moves the panes off the emptiest
        /// plate into the offcuts the other plates left, and drops any plate
        /// that ends up empty. Off by default: it changes the plan, so it is a
        /// choice, never a surprise.
        /// </summary>
        public bool UseOffcutReuse
        {
            get => _useOffcutReuse;
            set
            {
                if (_useOffcutReuse == value) return;
                _useOffcutReuse = value;
                OnPropertyChanged();
            }
        }

        /// <summary>What the switch does and what it costs, said plainly.</summary>
        public string OffcutReuseHint =>
            "Move the panes on the emptiest plate into the offcuts the other plates already "
            + "left, and skip any plate that ends up empty. Every pane is checked again after it "
            + "moves, and the whole attempt is rolled back if the plan stops verifying. Off by "
            + "default, because it changes the plan.";

        public const string AutoStageStrategyLabel = "Auto — choose the existing stage from this cutting list";
        public const string Stage1StrategyLabel = "Stage 1: QUICK — area shelf, strips, and bounded patterns";
        public const string Stage2StrategyLabel = "Stage 2: three orderings, best-split, and smart placement";
        public const string Stage3StrategyLabel = "Stage 3: full split grid, MaxRects, and beam 4";
        public const string Stage4StrategyLabel = "Stage 4: all orderings, shuffles, and beam 8";
        public const string Stage5StrategyLabel = "Stage 5: deepest shuffle and MaxRects search";

        public ObservableCollection<string> StageStrategies { get; } = new()
        {
            AutoStageStrategyLabel,
            Stage1StrategyLabel,
            Stage2StrategyLabel,
            Stage3StrategyLabel,
            Stage4StrategyLabel,
            Stage5StrategyLabel
        };

        private string _selectedStageStrategy = Stage3StrategyLabel;
        private bool _useAutoEffort;

        /// <summary>
        /// When true, Run Optimize picks stage 3, 4, or 5 from the cutting
        /// group. The plate pattern is not chosen here.
        /// </summary>
        public bool UseAutoEffort
        {
            get => _useAutoEffort;
            private set
            {
                if (_useAutoEffort == value) return;
                _useAutoEffort = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedStageStrategyHint));
            }
        }

        public string SelectedStageStrategy
        {
            get => _selectedStageStrategy;
            set
            {
                if (_selectedStageStrategy == value) return;
                _selectedStageStrategy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedStageStrategyHint));
                OnPropertyChanged(nameof(CurrentMaxStage));

                // Auto is the one family that still hands the portfolio to the
                // packer. Every other choice is a cap the operator set, and it
                // deliberately leaves the effort level alone.
                UseAutoEffort = IsAutoStage(value);
            }
        }

        /// <summary>
        /// What the higher settings cost. Nothing in the module said so, and
        /// a stage-5 run on a large job taking minutes instead of seconds looks
        /// like a hang rather than a search.
        /// </summary>
        private string CostWarning
        {
            get
            {
                if (UseAutoEffort) return string.Empty;
                if (string.Equals(_selectedStageStrategy, Stage5StrategyLabel, StringComparison.Ordinal)
                    || SelectedEffortLevel == "5")
                    return " Stage 5 and effort 5 evaluate every ordering against "
                         + "every generator — expect minutes on a large job, not seconds. "
                         + "Run QUICK first to get a number, then decide whether the "
                         + "extra sheets are worth the wait.";
                if (string.Equals(_selectedStageStrategy, Stage4StrategyLabel, StringComparison.Ordinal)
                    || SelectedEffortLevel == "4")
                    return " Noticeably slower than the default stage 3.";
                return string.Empty;
            }
        }

        /// <summary>
        /// Says what the chosen family runs, and that effort is a separate
        /// axis. This used to read the effort level and describe the stage it
        /// implied, because the two were one control.
        /// </summary>
        public string SelectedStageStrategyHint
        {
            get
            {
                if (UseAutoEffort)
                    return "Auto picks stage 3, 4, or 5 from the piece count, stock "
                         + "mix, and rack cap. It calls that existing stage. It does "
                         + "not change Yield and it does not select Fill first plate.";

                string family = _selectedStageStrategy switch
                {
                    Stage1StrategyLabel =>
                        "the quick portfolio only — area shelf, strips, and bounded patterns.",
                    Stage2StrategyLabel =>
                        "three orderings, best-split, sheet-fill, wall-seed, and smart placement.",
                    Stage3StrategyLabel =>
                        "the full split grid, MaxRects, and a width-4 beam.",
                    Stage4StrategyLabel =>
                        "all six orderings, shuffles, vertical smart placement, and a width-8 beam.",
                    Stage5StrategyLabel =>
                        "the widest shuffle set and MaxRects on every ordering.",
                    _ => "the portfolio its name describes."
                };

                return "Family: " + family + " Effort " + SelectedEffortLevel
                    + " is how hard it searches INSIDE that family — time budget, "
                    + "shuffles, and consolidation sweeps. The two are independent: "
                    + "stage 3 at effort 5 searches harder, it does not run the "
                    + "stage-5 portfolio."
                    + CostWarning;
            }
        }

        /// <summary>
        /// The family label for a stage number, used when migrating sessions
        /// saved before the family was its own setting. Out-of-range effort
        /// levels fall back to stage 3, the default.
        /// </summary>
        public static string LabelForStage(int stage) => stage switch
        {
            1 => Stage1StrategyLabel,
            2 => Stage2StrategyLabel,
            4 => Stage4StrategyLabel,
            5 => Stage5StrategyLabel,
            _ => Stage3StrategyLabel
        };

        /// <summary>
        /// The portfolio cap handed to the packer. 0 means Auto — let the
        /// packer choose the stage from the cutting list. 1..5 runs that
        /// stage's family and nothing wider, whatever the effort level is.
        /// </summary>
        public int CurrentMaxStage
        {
            get
            {
                if (IsAutoStage(_selectedStageStrategy)) return 0;
                return TryParseStageNumber(_selectedStageStrategy, out int stage) ? stage : 0;
            }
        }

        private static bool IsAutoStage(string strategy) =>
            strategy.StartsWith("Auto", StringComparison.OrdinalIgnoreCase);

        private static bool TryParseStageNumber(string strategy, out int stage)
        {
            stage = 0;
            if (strategy.StartsWith("Stage 1", StringComparison.OrdinalIgnoreCase)) stage = 1;
            else if (strategy.StartsWith("Stage 2", StringComparison.OrdinalIgnoreCase)) stage = 2;
            else if (strategy.StartsWith("Stage 3", StringComparison.OrdinalIgnoreCase)) stage = 3;
            else if (strategy.StartsWith("Stage 4", StringComparison.OrdinalIgnoreCase)) stage = 4;
            else if (strategy.StartsWith("Stage 5", StringComparison.OrdinalIgnoreCase)) stage = 5;
            return stage >= 1;
        }

        public ObservableCollection<string> PlatePatterns { get; } = new()
        {
            YieldPlatePatternLabel,
            FillFirstPlatePatternLabel,
            FewestCutsPatternLabel,
            MinimiseStockChangesPatternLabel,
            RemnantFirstPatternLabel,
            BalancedSpreadPatternLabel
        };

        private string _selectedPlatePattern = YieldPlatePatternLabel;
        public string SelectedPlatePattern
        {
            get => _selectedPlatePattern;
            set
            {
                string next = string.IsNullOrWhiteSpace(value) ? YieldPlatePatternLabel : value;
                if (string.Equals(_selectedPlatePattern, next, StringComparison.Ordinal)) return;
                _selectedPlatePattern = next;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentPlatePattern));
                OnPropertyChanged(nameof(SelectedPlatePatternHint));
            }
        }

        /// <summary>
        /// What the pattern changes. Every one of them is a tie-break between
        /// plans that already use the same sheets and leave nothing out — none
        /// of them may buy its preference by adding a sheet. That limit is the
        /// point, and the hints say it.
        /// </summary>
        public string SelectedPlatePatternHint => CurrentPlatePattern switch
        {
            PlatePattern.FillFirstPlate =>
                "Fills sheet 1 before opening another. It does not add sheets or leave more pieces out.",
            PlatePattern.FewestCuts =>
                "Between plans on the same sheet count, takes the one with fewer guillotine cuts — less table time, same material.",
            PlatePattern.MinimiseStockChanges =>
                "Between plans on the same sheet count, takes the one reaching for fewer stock sizes and switching between them less often.",
            PlatePattern.RemnantFirst =>
                "Between plans on the same sheet count, takes the one that concentrates its leftover on one sheet, so the waste is big enough to come off as a usable offcut. It chooses plans that leave reusable leftovers — it does not yet reuse them.",
            PlatePattern.BalancedSpread =>
                "Between plans on the same sheet count, takes the one whose sheets are most evenly loaded. Three even plates beat two full ones and a nearly empty one.",
            _ =>
                "Today's winner: fewest pieces left out, then fewest sheets. A fuller first sheet does not replace it."
        };

        public PlatePattern CurrentPlatePattern =>
            string.Equals(SelectedPlatePattern, FillFirstPlatePatternLabel, StringComparison.Ordinal)
                ? PlatePattern.FillFirstPlate
                : string.Equals(SelectedPlatePattern, FewestCutsPatternLabel, StringComparison.Ordinal)
                    ? PlatePattern.FewestCuts
                    : string.Equals(SelectedPlatePattern, MinimiseStockChangesPatternLabel, StringComparison.Ordinal)
                        ? PlatePattern.MinimiseStockChanges
                        : string.Equals(SelectedPlatePattern, RemnantFirstPatternLabel, StringComparison.Ordinal)
                            ? PlatePattern.RemnantFirst
                            : string.Equals(SelectedPlatePattern, BalancedSpreadPatternLabel, StringComparison.Ordinal)
                                ? PlatePattern.BalancedSpread
                                : PlatePattern.Yield;

        /// <summary>
        /// Stable name written into a saved session. Additive on purpose: a
        /// name this build does not know restores to Yield, so a session saved
        /// by a newer build still opens on an older one.
        /// </summary>
        public string PlatePatternName() => CurrentPlatePattern switch
        {
            PlatePattern.FillFirstPlate => "FillFirstPlate",
            PlatePattern.FewestCuts => "FewestCuts",
            PlatePattern.MinimiseStockChanges => "MinimiseStockChanges",
            PlatePattern.RemnantFirst => "RemnantFirst",
            PlatePattern.BalancedSpread => "BalancedSpread",
            _ => "Yield"
        };

        /// <summary>Inverse of <see cref="PlatePatternName"/> for restore.</summary>
        public static string LabelForPlatePattern(string? name) => name switch
        {
            "FillFirstPlate" => FillFirstPlatePatternLabel,
            "FewestCuts" => FewestCutsPatternLabel,
            "MinimiseStockChanges" => MinimiseStockChangesPatternLabel,
            "RemnantFirst" => RemnantFirstPatternLabel,
            "BalancedSpread" => BalancedSpreadPatternLabel,
            _ => YieldPlatePatternLabel
        };

        private string PlatePatternStamp() => CurrentPlatePattern switch
        {
            PlatePattern.FillFirstPlate => "fill first plate",
            PlatePattern.FewestCuts => "fewest cuts",
            PlatePattern.MinimiseStockChanges => "minimise stock changes",
            PlatePattern.RemnantFirst => "remnant-first",
            PlatePattern.BalancedSpread => "balanced spread",
            _ => "yield"
        };

        // ── cutting complexity (PHASE 1, 2026-09-26) ─────────────────────
        //
        // Kept OUT of MarginProfile on purpose: that type is per-thickness and
        // travels inside saved sessions, so growing it would force a session
        // migration. The complexity choice lives here instead and defaults to
        // free guillotine, which is exactly what the engine has always done.

        public const string FreeGuillotineLabel = "Free guillotine — unlimited stages";
        public const string SimpleFirstXLabel = "Simple — first X cut (2-stage)";
        public const string SimpleFirstYLabel = "Simple — first Y cut (2-stage)";
        public const string SimpleComplexityLabel = "Simple (2-stage, direction auto)";
        public const string ThreeStageLabel = "3-stage";
        public const string ComplexTrimsLabel = "Complex — trims allowed (Z/W)";

        public ObservableCollection<string> CutComplexities { get; } = new()
        {
            FreeGuillotineLabel,
            SimpleFirstXLabel,
            SimpleFirstYLabel,
            SimpleComplexityLabel,
            ThreeStageLabel,
            ComplexTrimsLabel
        };

        private string _selectedCutComplexity = FreeGuillotineLabel;

        /// <summary>
        /// The cutting-complexity level the floor will run. Free guillotine is
        /// the default and reproduces today's plans exactly; every other level
        /// is a production constraint that trades yield for simpler cutting.
        /// </summary>
        public string SelectedCutComplexity
        {
            get => _selectedCutComplexity;
            set
            {
                string next = string.IsNullOrWhiteSpace(value) ? FreeGuillotineLabel : value;
                if (string.Equals(_selectedCutComplexity, next, StringComparison.Ordinal)) return;
                _selectedCutComplexity = next;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CutComplexityHint));
                OnPropertyChanged(nameof(ShowTrimWidth));
            }
        }

        public CutComplexity CurrentCutComplexity =>
            string.Equals(SelectedCutComplexity, SimpleFirstXLabel, StringComparison.Ordinal)
                ? CutComplexity.SimpleFirstX
                : string.Equals(SelectedCutComplexity, SimpleFirstYLabel, StringComparison.Ordinal)
                    ? CutComplexity.SimpleFirstY
                    : string.Equals(SelectedCutComplexity, SimpleComplexityLabel, StringComparison.Ordinal)
                        ? CutComplexity.Simple
                        : string.Equals(SelectedCutComplexity, ThreeStageLabel, StringComparison.Ordinal)
                            ? CutComplexity.ThreeStage
                            : string.Equals(SelectedCutComplexity, ComplexTrimsLabel, StringComparison.Ordinal)
                                ? CutComplexity.ComplexTrims
                                : CutComplexity.FreeGuillotine;

        /// <summary>
        /// Inverse of <see cref="CurrentCutComplexity"/>: the list label for a
        /// level. Unknown values resolve to free guillotine, which is what a
        /// session saved before this setting existed must restore to.
        /// </summary>
        public static string LabelForComplexity(CutComplexity complexity) => complexity switch
        {
            CutComplexity.SimpleFirstX => SimpleFirstXLabel,
            CutComplexity.SimpleFirstY => SimpleFirstYLabel,
            CutComplexity.Simple => SimpleComplexityLabel,
            CutComplexity.ThreeStage => ThreeStageLabel,
            CutComplexity.ComplexTrims => ComplexTrimsLabel,
            _ => FreeGuillotineLabel
        };

        /// <summary>
        /// What the choice costs and buys. Staged patterns are honest about
        /// losing yield — the operator is deciding between material and table
        /// time, and a hint that only promised the upside would be a lie.
        /// </summary>
        public string CutComplexityHint => CurrentCutComplexity switch
        {
            CutComplexity.SimpleFirstX =>
                "Two stages, every sheet cut horizontally (along the width) first. Fewer, longer cuts — costs some yield.",
            CutComplexity.SimpleFirstY =>
                "Two stages, every sheet cut vertically (along the height) first. Fewer, longer cuts — costs some yield.",
            CutComplexity.Simple =>
                "The classic 2-stage pattern: strips, then pieces. Simplest cutting, lowest utilization.",
            CutComplexity.ThreeStage =>
                "Three stages. Recovers some of the yield 2-stage gives up, for one more cutting pass.",
            CutComplexity.ComplexTrims =>
                "Unlimited stages, plus Z/W trims that release reusable remnants. Never applied to laminated stock.",
            _ =>
                "Today's engine: unlimited cut stages, no trim cuts. Best yield — keep this unless the floor asks for simpler cutting."
        };

        /// <summary>The trim-width field only means anything when trims are on.</summary>
        public bool ShowTrimWidth => CurrentCutComplexity == CutComplexity.ComplexTrims;

        private string _minTrimWidthText = "80";

        /// <summary>
        /// Narrowest strip worth cutting to release a remnant. Only read when
        /// the complexity level allows trims.
        /// </summary>
        public string MinTrimWidthText
        {
            get => _minTrimWidthText;
            set
            {
                if (string.Equals(_minTrimWidthText, value, StringComparison.Ordinal)) return;
                _minTrimWidthText = value;
                OnPropertyChanged();
            }
        }

        /// <summary>Trim width as the engine will read it: unparseable or
        /// non-positive falls back to 80mm, the house starting point.</summary>
        public int CurrentMinTrimWidthMm =>
            TryParseWholeMm(MinTrimWidthText, out int w) && w > 0 ? w : 80;

        // ── margins ────────────────────────────────────────────────────

        /// <summary>
        /// Writes one house profile into the SETTINGS fields.
        /// </summary>
        /// <param name="thickness">Thickness whose bucket supplies the numbers.</param>
        /// <param name="breakoutMm">
        /// Breakout to apply. Null keeps the house primary; a sanctioned
        /// alternative (40 on thick glass) is honoured, anything else is
        /// clamped back to the bucket default so a stray value can never
        /// disable breakout protection.
        /// </param>
        private void ApplyHouseDefaults(int thickness, int? breakoutMm = null)
        {
            var m = MarginProfile.DefaultFor(thickness, breakoutMm);

            ThicknessText = thickness.ToString(CultureInfo.InvariantCulture);
            LeftMarginText = m.LeftMm.ToString(CultureInfo.InvariantCulture);
            RightMarginText = m.RightMm.ToString(CultureInfo.InvariantCulture);
            TopMarginText = m.TopMm.ToString(CultureInfo.InvariantCulture);
            BottomMarginText = m.BottomMm.ToString(CultureInfo.InvariantCulture);
            KerfText = m.KerfMm.ToString(CultureInfo.InvariantCulture);
            MinBreakoutText = m.MinBreakoutMm.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// LOAD HOUSE DEFAULTS. Preserves a breakout the operator already chose
        /// from the sanctioned 50 / 40 pair, so switching thickness does not
        /// silently undo a deliberate breakout decision.
        /// </summary>
        private void LoadHouseDefaults()
        {
            // Nothing of the operator's to lose: the fields already hold the
            // house numbers, so reloading them cannot discard anything.
            if (!FieldsAreHouseDefaults(CurrentThicknessMm)
                && !ConfirmDestructive(
                    "Replace the current margins, kerf and breakout with the house defaults?\n\n"
                    + "Every SETTINGS value you have changed for this thickness is discarded.",
                    "Load house defaults")) return;

            int thickness = CurrentThicknessMm;

            int? keepBreakout = TryParseWholeMm(MinBreakoutText, out int bo)
                                && MarginProfile.HasBreakoutChoice(thickness)
                                && (bo == MarginProfile.BreakoutPrimaryMm || bo == MarginProfile.BreakoutAlternateMm)
                ? bo
                : (int?)null;

            ApplyHouseDefaults(thickness, keepBreakout);
        }

        /// <summary>
        /// True when every SETTINGS field still holds the house numbers for
        /// <paramref name="thickness"/> — i.e. the operator has not customized
        /// them. On thick glass either sanctioned breakout (50 or 40) counts as
        /// house, because both are floor-approved; on thin and medium glass the
        /// breakout must equal the margin.
        /// </summary>
        private bool FieldsAreHouseDefaults(int thickness)
        {
            var house = MarginProfile.DefaultFor(thickness);

            if (!TryParseWholeMm(LeftMarginText, out int l)) return false;
            if (!TryParseWholeMm(RightMarginText, out int r)) return false;
            if (!TryParseWholeMm(TopMarginText, out int t)) return false;
            if (!TryParseWholeMm(BottomMarginText, out int b)) return false;
            if (!TryParseWholeMm(KerfText, out int kerf)) return false;
            if (!TryParseWholeMm(MinBreakoutText, out int bo)) return false;

            bool breakoutIsHouse = MarginProfile.HasBreakoutChoice(thickness)
                ? bo == MarginProfile.BreakoutPrimaryMm || bo == MarginProfile.BreakoutAlternateMm
                : bo == house.MinBreakoutMm;

            return l == house.LeftMm
                && r == house.RightMm
                && t == house.TopMm
                && b == house.BottomMm
                && kerf == house.KerfMm
                && breakoutIsHouse;
        }

        /// <summary>
        /// The thickness trigger. Fires from the <see cref="ThicknessText"/>
        /// setter and follows the owner's "auto select as per thickness" rule
        /// without ever destroying customized work:
        ///
        ///   • same bucket as before → the house numbers are identical, so
        ///     there is nothing to select;
        ///   • different bucket, fields untouched → apply the new house profile
        ///     automatically, keeping a sanctioned breakout choice;
        ///   • different bucket, fields customized → change NOTHING, and let
        ///     <see cref="MarginHint"/> name the new house numbers and offer
        ///     the one-click.
        /// </summary>
        private void AutoSelectHouseDefaultsOnThicknessChange(int previousThickness)
        {
            int thickness = CurrentThicknessMm;

            if (MarginProfile.BucketFor(thickness) == MarginProfile.BucketFor(previousThickness))
                return;

            if (!FieldsAreHouseDefaults(previousThickness))
            {
                // Customized. Overwriting here is exactly the RC-016 mistake:
                // the operator did work, a routine navigation-style event
                // erased it, and nothing said so.
                Services.Logging.AppLogService.Info(
                    $"[OPT-MARGINS] thickness {previousThickness}mm → {thickness}mm changed bucket " +
                    $"({MarginProfile.BucketFor(previousThickness)} → {MarginProfile.BucketFor(thickness)}) " +
                    "but the SETTINGS fields are customized, so they were left alone.",
                    new Services.Logging.LogContext { Module = "Optimization", Operation = "MarginsAutoSelect" });
                return;
            }

            int? keepBreakout = TryParseWholeMm(MinBreakoutText, out int bo)
                                && (bo == MarginProfile.BreakoutPrimaryMm || bo == MarginProfile.BreakoutAlternateMm)
                ? bo
                : (int?)null;

            ApplyHouseDefaults(thickness, keepBreakout);

            Services.Logging.AppLogService.Info(
                $"[OPT-MARGINS] thickness {previousThickness}mm → {thickness}mm auto-selected the " +
                $"{MarginProfile.BucketFor(thickness)} house profile " +
                $"(LM {LeftMarginText} RM {RightMarginText} TM {TopMarginText} BM {BottomMarginText} " +
                $"kerf {KerfText} breakout {MinBreakoutText}).",
                new Services.Logging.LogContext { Module = "Optimization", Operation = "MarginsAutoSelect" });
        }

        private static bool TryParseWholeMm(string? text, out int value) =>
            int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out value);

        public string ThicknessSourceHint
        {
            get
            {
                var thicknesses = InputThicknesses();
                return thicknesses.Count > 1
                    ? "Mixed thicknesses: house defaults apply separately to each glass group. Select a group to inspect its thickness. Custom margins apply as entered."
                    : "Thickness follows the imported spec/pane. Manual margin edits are preserved; LOAD HOUSE DEFAULTS resets them.";
            }
        }

        private List<int> InputThicknesses() => Pieces
            .Where(p => SelectedGroupMatches(p.GlassRef) && p.TryParse(out _, out _, out _))
            .Select(p => p.ThicknessMm > 0 ? p.ThicknessMm : ParseThicknessMm(p.GlassRef))
            .Where(t => t > 0).Distinct().ToList();

        private void SyncThicknessFromInputs()
        {
            if (IsRunning || IsDisposed) return;
            var thicknesses = InputThicknesses();
            if (thicknesses.Count == 1)
                ThicknessText = thicknesses[0].ToString(CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(ThicknessSourceHint));
        }

        internal const int MaxExpandedParts = 2_000_000;

        private bool TryBuildInputs(
            out List<CutPiece> pieces, out List<(StockSize Stock, string Composition)> stocks, out MarginProfile margins)
        {
            pieces = new List<CutPiece>();
            stocks = new List<(StockSize Stock, string Composition)>();
            margins = null;

            long totalUnits = 0;
            foreach (var row in Pieces)
            {
                if (!row.TryParse(out _, out _, out int quantity))
                {
                    Blocked($"Piece \"{row.PieceLabel}\" needs positive whole-mm dimensions and quantity.", "Cannot optimize");
                    return false;
                }
                totalUnits += quantity;
            }
            if (totalUnits > MaxExpandedParts)
            {
                Blocked($"This list expands to {totalUnits:N0} pieces; the supported admission limit is {MaxExpandedParts:N0}. Split the job before running.", "Cannot optimize");
                return false;
            }
            pieces.Capacity = (int)totalUnits;
            foreach (var p in Pieces)
            {
                if (!p.TryParse(out int w, out int h, out int q))
                {
                    Blocked($"Piece \"{p.PieceLabel}\" is invalid — needs positive W×H and qty.", "Cannot optimize");
                    return false;
                }
                for (int i = 0; i < q; i++)
                {
                    string baseLabel = string.IsNullOrWhiteSpace(p.PieceLabel)
                        ? (p.GlassRef ?? string.Empty).Trim()
                        : p.PieceLabel.Trim();
                    pieces.Add(new CutPiece
                    {
                        JobRef = string.IsNullOrWhiteSpace(p.JobOrderId) ? p.ProformaId : p.JobOrderId,
                        ProformaId = p.ProformaId,
                        JobOrderId = p.JobOrderId,
                        SpecGroupNumber = p.SpecGroupNumber,
                        SourceRowSerial = p.SourceRowSerial,
                        Color = p.Color,
                        MaterialKey = p.MaterialKey,
                        GlassType = p.GlassType,
                        Processing = p.Processing,
                        Coating = p.Coating,
                        GlassRef = p.GlassRef ?? string.Empty,
                        // "G-101 3/24" — piece identity the 2D view / cut
                        // plan carry, matching the design doc's labelling.
                        PieceLabel = q > 1
                            ? $"{baseLabel} {i + 1}/{q}"
                            : baseLabel,
                        Position = p.Position ?? string.Empty,
                        ThicknessMm = p.ThicknessMm > 0 ? p.ThicknessMm : ParseThicknessMm(p.GlassRef),
                        WidthMm = w,
                        HeightMm = h,
                        AllowRotate = RotationEnabled && !p.RotationLocked
                    });
                }
            }
            if (pieces.Count == 0)
            {
                Blocked("Add pieces first (JOBS tab) or load the reference job.", "Nothing to optimize");
                return false;
            }

            foreach (var s in Stocks)
            {
                // Only racks ticked "use in run" are fed to the optimizer —
                // the STOCKS tab selects which Step 4 sheets each spec uses.
                if (!s.IsUsedInRun) continue;

                if (!int.TryParse(s.WidthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) ||
                    !int.TryParse(s.HeightText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) ||
                    w <= 0 || h <= 0)
                {
                    Blocked("Stock pool has an invalid size (STOCKS tab).", "Nothing to optimize");
                    return false;
                }
                string available = (s.AvailableText ?? "").Trim();
                int max;
                if (available.Length == 0 || available == "∞") max = StockSize.UnlimitedSheets;
                else if (!int.TryParse(available, NumberStyles.Integer, CultureInfo.InvariantCulture, out max) || max < 0)
                {
                    Blocked("Stock availability must be a non-negative whole number, blank or ∞ (STOCKS tab).", "Nothing to optimize");
                    return false;
                }
                else if (max == 0) continue; // zero balance is not unlimited stock
                stocks.Add((new StockSize { WidthMm = w, HeightMm = h, MaxSheets = max,
                    ThicknessMm = s.ThicknessMm > 0 ? s.ThicknessMm : ParseThicknessMm(s.Composition),
                    Color = string.IsNullOrWhiteSpace(s.Color) ? (s.Composition ?? string.Empty).Trim().ToUpperInvariant() : s.Color,
                    Label = s.Composition, MaterialKey = s.MaterialKey },
                    s.Composition ?? string.Empty));
            }
            if (stocks.Count == 0)
            {
                Blocked("Add at least one stock size and tick it for use (STOCKS tab).", "Nothing to optimize");
                return false;
            }

            int Parse(string text, string name, out int value)
            {
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0)
                    return value;
                value = -1;
                Blocked($"{name} must be a whole number ≥ 0 (MARGINS tab).", "Cannot optimize");
                return value;
            }

            int left = Parse(LeftMarginText, "Left margin", out int lm);
            int right = Parse(RightMarginText, "Right margin", out int rm);
            int top = Parse(TopMarginText, "Top margin", out int tm);
            int bottom = Parse(BottomMarginText, "Bottom margin", out int bm);
            int kerf = Parse(KerfText, "Kerf", out int k);
            int breakout = Parse(MinBreakoutText, "Minimum breakout", out int mb);
            if (left < 0 || right < 0 || top < 0 || bottom < 0 || kerf < 0 || breakout < 0)
                return false;

            int thickness = int.TryParse(ThicknessText, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int th) && th > 0 ? th : 6;

            margins = new MarginProfile
            {
                ThicknessMm = thickness,
                LeftMm = left,
                RightMm = right,
                TopMm = top,
                BottomMm = bottom,
                KerfMm = kerf,
                MinBreakoutMm = breakout,
                IsCustomized = !FieldsAreHouseDefaults(thickness)
            };
            return true;
        }
    }
}
