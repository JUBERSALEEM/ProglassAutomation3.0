// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step9ProfitLoss/ProfitLossStepViewModel.TargetPricing.cs
// PROJECT: ProGlassAutomation
// MODULE: Step 9 — Target Profitability (approved KPI model)
//
// This partial owns the target-price model built on the shared
// SpecificationTargetCalculator. It is kept STRICTLY separate from the
// actual-cost rows (ProfitLossStepViewModel.Calculation.cs): the target
// model answers "is the Step 5 given price above the required target?",
// the actual rows answer "what does the job really cost?".
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges;
using ProGlassAutomation.Services.ProformaInvoice;
using ProGlassAutomation.Services.ProformaInvoice.TargetPricing;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step6OtherCharges;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss
{
    public partial class ProfitLossStepViewModel : ViewModelBase
    {
        /// <summary>Version of the target-price calculation model.</summary>
        public const string TargetCalculationVersion = "2.2";

        // ══════════════════════════════════════════════════════════════
        //  OUTPUTS — TARGET PROFITABILITY
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// One row per specification group with its target-price result.
        /// Drives Tab 4 (Glass / Specification Comparison) and the detail
        /// sidebar on Tab 1.
        /// </summary>
        public ObservableCollection<SpecTargetRowViewModel> TargetSpecsView { get; } = new();

        /// <summary>True once at least one specification has a target result.</summary>
        public bool HasTargetSpecs => TargetSpecsView.Count > 0;

        /// <summary>
        /// Selects a specification row (Tab 4) and opens its six-section
        /// detail sidebar.
        /// </summary>
        public ICommand SelectTargetSpecCommand { get; }

        private void SetSelectedTargetSpec(object? parameter)
        {
            var row = parameter as SpecTargetRowViewModel;
            foreach (var r in TargetSpecsView)
                r.IsSelected = ReferenceEquals(r, row);

            SelectedTargetSpec = row;
        }

        /// <summary>
        /// Host-wired navigation back to Step 6 (Other charges) from the
        /// Glass / Spec Comparison tab. The report references Step 6
        /// U-insert / wastage rows, so a one-click jump keeps the estimator
        /// in flow. Wired by ProformaInvoiceViewModel to GoToStep(6).
        /// </summary>
        public Action? NavigateToStep6Requested { get; set; }

        /// <summary>Jumps to Step 6 (Other charges) via the host callback.</summary>
        public ICommand GoToStep6Command { get; }

        private void GoToStep6() => NavigateToStep6Requested?.Invoke();

        /// <summary>Returns from the full-screen spec detail back to the
        /// per-group comparison list (clears the selection).</summary>
        public ICommand BackToComparisonCommand { get; }

        private void BackToComparison()
        {
            foreach (var r in TargetSpecsView)
                r.IsSelected = false;

            SelectedTargetSpec = null;
        }

        /// <summary>Raw job-level aggregate of the last recalculation.</summary>
        public TargetPriceResult? JobTargetResult { get; private set; }

        public bool HasJobTarget => JobTargetResult != null;

        // ── EXPECTED PROFIT MARGIN (middle KPI card, approved uplift) ──
        // The target formula multiplies the pre-uplift specification result
        // by (1 + margin/100). The margin is the company default captured in
        // Settings → Features → Proforma Invoice → Step 9 ("Profit margin %",
        // default 15 → ×1.15). Drafts and invoices saved before the setting
        // existed auto-adapt to the CURRENT global default (always live);
        // SetExpectedProfitMarginOverride can pin a per-invoice value when a
        // saved snapshot is re-hydrated.

        private decimal? _expectedProfitMarginPercent;

        /// <summary>True when this invoice carries its own captured margin
        /// instead of following the global default live.</summary>
        public bool HasExpectedProfitMarginOverride => _expectedProfitMarginPercent.HasValue;

        /// <summary>Effective expected profit margin (%) for this invoice.</summary>
        public decimal ExpectedProfitMarginPercent =>
            _expectedProfitMarginPercent
            ?? Services.Features.Step9PricingDefaults.ExpectedProfitMarginPercent;

        /// <summary>Middle KPI card value, e.g. "15%".</summary>
        public string ExpectedProfitMarginDisplay => $"{ExpectedProfitMarginPercent:0.#}%";

        /// <summary>Factor label, e.g. "×1.15".</summary>
        public string ExpectedProfitMarginFactorLabel =>
            $"×{1m + ExpectedProfitMarginPercent / 100m:0.##}";

        /// <summary>Where the middle-card value comes from.</summary>
        public string ExpectedProfitMarginSourceLabel => HasExpectedProfitMarginOverride
            ? "Captured for this invoice"
            : "Company default · Settings → Features → Proforma Invoice → Step 9";

        /// <summary>Pins an invoice-specific expected margin (e.g. the value
        /// captured when the invoice was created/saved).</summary>
        public void SetExpectedProfitMarginOverride(decimal percent)
        {
            percent = Services.Features.Step9PricingDefaults.ClampPercent(percent);
            if (_expectedProfitMarginPercent == percent) return;
            _expectedProfitMarginPercent = percent;
            OnPropertyChanged(nameof(ExpectedProfitMarginPercent));
            OnPropertyChanged(nameof(ExpectedProfitMarginDisplay));
            OnPropertyChanged(nameof(ExpectedProfitMarginFactorLabel));
            NotifyTargetOutputsChanged();
        }

        /// <summary>Called when the Settings default changes so an unpinned
        /// invoice follows the new global value live.</summary>
        private void OnExpectedProfitMarginDefaultChanged()
        {
            if (HasExpectedProfitMarginOverride) return;

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                RaiseExpectedProfitMarginChangedAndRecalc();
            else
                dispatcher.BeginInvoke(new Action(RaiseExpectedProfitMarginChangedAndRecalc));
        }

        private void RaiseExpectedProfitMarginChangedAndRecalc()
        {
            OnPropertyChanged(nameof(ExpectedProfitMarginPercent));
            OnPropertyChanged(nameof(ExpectedProfitMarginDisplay));
            OnPropertyChanged(nameof(ExpectedProfitMarginFactorLabel));
            QueueRecalculate();
        }

        /// <summary>
        /// When the user selects a specification on the target tab the
        /// sidebar binds to this row. Null until a selection is made.
        /// </summary>
        private SpecTargetRowViewModel? _selectedTargetSpec;
        public SpecTargetRowViewModel? SelectedTargetSpec
        {
            get => _selectedTargetSpec;
            set
            {
                if (ReferenceEquals(_selectedTargetSpec, value)) return;
                _selectedTargetSpec = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedTargetSpec));
            }
        }

        public bool HasSelectedTargetSpec => SelectedTargetSpec != null;

        // ── KPI card 1 — REQUIRED TARGET PRICE ──────────────────────────

        public string RequiredTargetPriceDisplay => FormatRateCard(JobTargetResult?.AverageFinalRequiredTargetPerSqm);
        public string SpecificationTargetAverageDisplay => FormatRateCard(JobTargetResult?.AverageSpecificationTargetPerSqm);

        /// <summary>
        /// Compact one-line value for the KPI card ("Included in given price" /
        /// "Additional — Step 6 extra" / "Not applicable" / "Mixed" /
        /// "Unresolved"). The detailed wording lives in
        /// <see cref="UInsertSummaryDetail"/> (shown as a tooltip), so the
        /// card row never spills onto a second line.
        ///
        /// Revised rule (2026-09-04): the required target is ALWAYS the
        /// base-price specification target. Additional (Step 6 row active) =
        /// the extra is billed separately and stays out of the target;
        /// IncludedInStep5Price (toggle off) = the U-insert is already inside
        /// the given price, so the rate is SHOWN for reference but never
        /// added to the target (adding it would double-count the U-insert).
        /// </summary>
        public string UInsertSummaryDisplay
        {
            get
            {
                var specs = JobTargetResult?.Specifications?.ToList() ?? new List<SpecTargetPriceResult>();
                if (specs.Count == 0) return "—";

                bool anyAdditional = specs.Any(s => s.UInsertTreatment == UInsertTreatment.Additional);
                bool anyIncluded = specs.Any(s => s.UInsertTreatment == UInsertTreatment.IncludedInStep5Price);
                bool anyUnresolved = specs.Any(s => s.UInsertTreatment == UInsertTreatment.Unresolved);

                if (anyUnresolved && specs.Count(s =>
                        s.UInsertTreatment != UInsertTreatment.Unresolved) == 0)
                    return "Unresolved";

                if (anyAdditional && anyIncluded)
                    return "Mixed";

                if (anyIncluded)
                    return "Included in given price";

                if (anyAdditional)
                    return "Additional";

                return "Not applicable";
            }
        }

        /// <summary>
        /// Full explanation behind the compact <see cref="UInsertSummaryDisplay"/>.
        /// </summary>
        public string UInsertSummaryDetail
        {
            get
            {
                var specs = JobTargetResult?.Specifications?.ToList() ?? new List<SpecTargetPriceResult>();
                if (specs.Count == 0) return "No U-insert data for this specification.";

                bool anyAdditional = specs.Any(s => s.UInsertTreatment == UInsertTreatment.Additional);
                bool anyIncluded = specs.Any(s => s.UInsertTreatment == UInsertTreatment.IncludedInStep5Price);
                bool anyUnresolved = specs.Any(s => s.UInsertTreatment == UInsertTreatment.Unresolved);

                // Area-weighted average of the resolved per-spec rates for
                // display only — the required target never moves.
                decimal totalArea = specs.Sum(s => Math.Max(0m, s.AreaSqm));
                decimal avgRate = totalArea > 0m
                    ? specs.Sum(s => Math.Max(0m, s.UInsertRatePerSqm) * Math.Max(0m, s.AreaSqm)) / totalArea
                    : 0m;
                string rateText = avgRate > 0m ? $" (rate AED {avgRate:N2}/m²)" : "";

                if (anyUnresolved)
                    return "Unresolved — check the Step 6 U-insert row and its rate.";

                if (anyAdditional && anyIncluded)
                    return "Mixed — some specifications bill the U-insert separately in Step 6, others include it in the given price." +
                           " The required target is never changed by the U-insert in either case.";

                if (anyAdditional)
                    return "Additional — billed as its own Step 6 extra on top of the given price." +
                           " The required target stays the base-price specification target (rate not added).";

                if (anyIncluded)
                {
                    return avgRate > 0m
                        ? $"Included in the given price{rateText} — the customer's price already covers the U-insert, so the required target stays the base-price specification target (the rate is shown, never added)."
                        : "Included in the given price — the customer's price already covers the U-insert, so the required target stays the base-price specification target.";
                }

                return "This specification carries no U-insert extra.";
            }
        }

        /// <summary>True when any specification's U-insert treatment is unresolved.</summary>
        public bool HasUInsertSummaryWarning =>
            (JobTargetResult?.Specifications?.Any(s => s.UInsertTreatment == UInsertTreatment.Unresolved)
             ?? false);

        // ── KPI card 2 — MARGIN AGAINST TARGET ──────────────────────────

        public string MarginAgainstTargetDisplay =>
            JobTargetResult?.MarginAgainstTargetPercent switch
            {
                null => "—",
                var m => $"{m.Value:0.##}%"
            };

        /// <summary>
        /// Cost margin for the target KPI: selling less base cost and any
        /// active Step 6 U-insert cost, divided by selling. The shared target
        /// aggregate returns null for incomplete or unresolved inputs.
        /// </summary>
        public decimal? CostBasedProfitMarginPercent =>
            JobTargetResult?.CostBasedProfitMarginPercent;

        public string CostBasedProfitMarginDisplay =>
            CostBasedProfitMarginPercent switch
            {
                null => "—",
                var margin => $"{margin.Value:0.##}%"
            };

        public string GivenPricePerSqmDisplay => FormatRateCard(JobTargetResult?.AverageGivenPricePerSqm);

        public string DifferencePerSqmDisplay
        {
            get
            {
                if (JobTargetResult == null) return "—";
                // Compare the GIVEN JUDGED price (net of any U-insert that is
                // inside the given) against the required target, so a toggle-off
                // quotation reads honestly. Falls back to the raw given when the
                // base-price judgement is not calculable.
                decimal given = JobTargetResult.AverageExtraProfitPerSqm.HasValue
                    ? JobTargetResult.AverageGivenJudgedPerSqm
                    : JobTargetResult.AverageGivenPricePerSqm;
                decimal diff = given - JobTargetResult.AverageFinalRequiredTargetPerSqm;
                return diff < 0m
                    ? $"-AED {Math.Abs(diff):N2}/m²"
                    : $"AED {diff:N2}/m²";
            }
        }

        // ── Card 3 · MARGIN AGAINST TARGET — BASE-PRICE JUDGEMENT ───────
        // The KPI trigger is the BASE price (the pre-uplift specification
        // formula: (Outer + Inner) ÷ Yield + ASP, e.g. (25+25)÷0.85+45 =
        // 103.82 → target 103.82×1.15 = 119.39). The margin is judged on the
        // GIVEN price against that base — never against the final required
        // target. The U-insert toggle (the Step 6 row) decides which given
        // counts:
        //   • row ACTIVE  (Additional)        → given 125 is the glass price
        //     → extra profit 125 − 103.82 = +21.18 AED/m² (+20.40%)
        //   • row OFF     (IncludedInStep5Price) → the U-insert is paid out
        //     of the same given → judged given 125 − 10 = 115
        //     → extra profit 115 − 103.82 = +11.18 AED/m² (+10.77%)
        // All figures below come from the shared aggregate (area-weighted
        // averages / value-weighted margins) so Step 9 and Job Profitability
        // always agree.

        /// <summary>Headline: extra profit over the base price, AED/m².</summary>
        public string ExtraProfitPerSqmDisplay
        {
            get
            {
                decimal? extra = JobTargetResult?.AverageExtraProfitPerSqm;
                if (!extra.HasValue) return "—";
                return extra.Value < 0m
                    ? $"-AED {Math.Abs(extra.Value):N2}/m²"
                    : $"AED {extra.Value:N2}/m²";
            }
        }

        public bool IsExtraProfitLoss => (JobTargetResult?.AverageExtraProfitPerSqm ?? 0m) < 0m;

        /// <summary>Caption under the headline: the same judgement as a %.</summary>
        public string ExtraProfitMarginPercentDisplay
        {
            get
            {
                decimal? m = JobTargetResult?.ExtraProfitMarginPercent;
                return m.HasValue
                    ? $"{m.Value:+0.##;-0.##;0.##}% over base price"
                    : "—";
            }
        }

        /// <summary>Area-weighted average base price (pre-uplift formula), AED/m².</summary>

        public string BaseMarginPercentDisplay =>
            JobTargetResult?.ExtraProfitMarginPercent switch
            {
                null => "—",
                var m => $"{m.Value:+0.##;-0.##;0.##}%"
            };

        public string BaseBalancePerSqmDisplay => ExtraProfitPerSqmDisplay;

        public string BaseBalanceValueDisplay
        {
            get
            {
                var specs = JobTargetResult?.Specifications;
                if (specs == null || specs.Count == 0) return "—";

                decimal total = specs
                    .Where(s => s.HasValidExtraProfit && s.AreaSqm > 0m)
                    .Sum(s => (s.ExtraProfitPerSqm ?? 0m) * s.AreaSqm);
                return total < 0m
                    ? $"-AED {Math.Abs(total):N2}"
                    : $"AED {total:N2}";
            }
        }

        public bool IsBaseMarginBelow =>
            (JobTargetResult?.ExtraProfitMarginPercent ?? 0m) < 0m;

        public string TargetMarginPercentDisplay =>
            JobTargetResult?.MarginAgainstTargetPercent switch
            {
                null => "—",
                var m => $"{m.Value:+0.##;-0.##;0.##}%"
            };

        public string TargetBalancePerSqmDisplay => DifferencePerSqmDisplay;

        public string TargetBalanceValueDisplay => TotalDifferenceValueDisplay;

        public bool IsTargetMarginBelow =>
            (JobTargetResult?.MarginAgainstTargetPercent ?? 0m) < 0m;

        public string BasePriceAverageDisplay => FormatRateCard(JobTargetResult?.AverageBasePricePerSqm);

        /// <summary>Area-weighted average given price as judged for the margin.</summary>
        public string GivenJudgedAverageDisplay => FormatRateCard(JobTargetResult?.AverageGivenJudgedPerSqm);

        /// <summary>
        /// Area-weighted average U-insert rate that is covered INSIDE the
        /// given price (toggle off / IncludedInStep5Price) — the amount the
        /// judged given is reduced by. Empty when no spec carries an included
        /// U-insert.
        /// </summary>
        public string IncludedUInsertAverageDisplay
        {
            get
            {
                var specs = JobTargetResult?.Specifications;
                if (specs == null || specs.Count == 0) return "—";

                decimal totalArea = 0m, uW = 0m;
                foreach (var s in specs)
                {
                    if (s == null || s.AreaSqm <= 0m) continue;
                    if (s.UInsertTreatment != UInsertTreatment.IncludedInStep5Price) continue;
                    totalArea += s.AreaSqm;
                    uW += Math.Max(0m, s.UInsertRatePerSqm) * s.AreaSqm;
                }

                return totalArea > 0m
                    ? $"AED {Math.Round(uW / totalArea, 2):N2}/m²"
                    : "—";
            }
        }

        public string TargetJobStatusLabel => TargetStatusLabel(JobTargetResult?.Status);

        public bool IsTargetBelow => JobTargetResult?.Status == TargetStatus.BelowTarget;
        public bool IsTargetAbove => JobTargetResult?.Status == TargetStatus.AboveTarget;
        public bool IsTargetIncomplete =>
            JobTargetResult?.Status is TargetStatus.MissingBasePrice
                or TargetStatus.MissingGlassRate
                or TargetStatus.MissingAspRate
                or TargetStatus.MissingYield
                or TargetStatus.MissingUInsertTreatment
                or TargetStatus.CalculationIncomplete
                or TargetStatus.SnapshotStale;

        // ── Calculation summary (price source + formula detail) ─────────

        public string GivenPriceSourceLabel => Step5PriceSource.SourceLabel;

        /// <summary>Formula text of the FIRST complete spec (single-spec jobs show the exact formula).</summary>
        public string TargetFormulaPreview =>
            (JobTargetResult?.Specifications?.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.FormulaText))
             ?? JobTargetResult?.Specifications?.FirstOrDefault())
            ?.FormulaText ?? "—";

        public string TargetFormulaSpecLabel =>
            (JobTargetResult?.Specifications?.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.FormulaText)))
            ?.SpecNumber.ToString() is string n
                ? $"SPEC {n}"
                : "";

        /// <summary>
        /// The formula worked out stage-by-stage on one line, e.g.
        /// "94 → ÷ 0.85 = 110.59 → + 45 = 155.59 → × 1.15 = 178.93".
        /// The symbolic formula stays in <see cref="TargetFormulaPreview"/>;
        /// this chain shows the running value after every step so the card
        /// row reads as a calculation, not just an expression.
        /// </summary>
        public string TargetFormulaStepsText
        {
            get
            {
                var s = JobTargetResult?.Specifications?.FirstOrDefault(
                            x => !string.IsNullOrWhiteSpace(x.FormulaText))
                        ?? JobTargetResult?.Specifications?.FirstOrDefault();
                if (s == null || s.YieldFactor <= 0m) return "";
                return BuildFormulaSteps(s);
            }
        }

        private static string BuildFormulaSteps(SpecTargetPriceResult s)
        {
            if (s.YieldFactor <= 0m) return "";

            decimal glassSum = s.BuildType switch
            {
                GlassBuildType.Dgu or GlassBuildType.Lam =>
                    s.OuterGlassRatePerSqm + s.InnerGlassRatePerSqm,
                GlassBuildType.DguLam =>
                    s.OuterGlassRatePerSqm + s.InnerGlassRatePerSqm + s.ThirdGlassRatePerSqm,
                _ => s.OuterGlassRatePerSqm
            };

            var steps = new List<string> { FmtSteps(glassSum) };

            decimal running = Math.Round(glassSum / s.YieldFactor, 2);
            steps.Add($"÷ {FmtSteps(s.YieldFactor)} = {FmtSteps(running)}");

            if (s.AspRatePerSqm > 0m)
            {
                running = Math.Round(running + s.AspRatePerSqm, 2);
                steps.Add($"+ {FmtSteps(s.AspRatePerSqm)} = {FmtSteps(running)}");
            }

            if (s.ProcessingRatePerSqm > 0m)
            {
                running = Math.Round(running + s.ProcessingRatePerSqm, 2);
                steps.Add($"+ {FmtSteps(s.ProcessingRatePerSqm)} = {FmtSteps(running)}");
            }

            decimal uplift = s.TargetUpliftPercent > 0m ? s.TargetUpliftPercent : 15m;
            decimal factor = 1m + uplift / 100m;
            steps.Add($"× {FmtSteps(factor)} = {FmtSteps(Math.Round(running * factor, 2))}");

            return string.Join("  →  ", steps);
        }

        private static string FmtSteps(decimal value) =>
            value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>"N specification group(s)" when more than one spec is present.</summary>
        public string TargetScopeNote
        {
            get
            {
                int count = JobTargetResult?.Specifications?.Count ?? 0;
                return count switch
                {
                    0 => "No specification selected — add glass in Step 5.",
                    1 => "1 specification",
                    _ => $"{count} specifications · area-weighted averages"
                };
            }
        }

        public string TargetFormulaAllSpecsText =>
            TargetSpecsView.Count > 0
                ? string.Join(Environment.NewLine, TargetSpecsView.Select(r => r.FormulaSummaryLine))
                : "—";

        // ── Value totals (compact fields under the cards) ───────────────

        public string TotalGivenValueDisplay => JobTargetResult != null
            ? $"AED {JobTargetResult.TotalGivenValue:N2}"
            : "—";

        public string TotalRequiredTargetValueDisplay => JobTargetResult != null
            ? $"AED {JobTargetResult.TotalRequiredTargetValue:N2}"
            : "—";

        public string TotalDifferenceValueDisplay
        {
            get
            {
                if (JobTargetResult == null) return "—";
                return JobTargetResult.TotalDifferenceValue < 0m
                    ? $"-AED {Math.Abs(JobTargetResult.TotalDifferenceValue):N2}"
                    : $"AED {JobTargetResult.TotalDifferenceValue:N2}";
            }
        }

        // ── Calculation state (auto-sync) ────────────────────────────────

        private DateTime? _lastCalculatedAt;
        public DateTime? LastCalculatedAt
        {
            get => _lastCalculatedAt;
            private set
            {
                _lastCalculatedAt = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LastCalculatedAtDisplay));
                RaiseStep9HealthNotifications();
            }
        }

        public string LastCalculatedAtDisplay =>
            LastCalculatedAt switch
            {
                null => "Never calculated",
                var d => $"Calculated {d.Value:dd MMM yyyy, HH:mm:ss}"
            };

        private List<string> _calculationWarnings = new();
        public IReadOnlyList<string> CalculationWarnings => _calculationWarnings;
        public bool HasCalculationWarnings => _calculationWarnings.Count > 0;

        /// <summary>Warning line shown under the KPI cards when relevant.</summary>
        public string CalculationWarningSummary =>
            _calculationWarnings.FirstOrDefault() ?? "";

        // ══════════════════════════════════════════════════════════════
        //  BUILD THE TARGET RESULTS (called from RecalculateAsync)
        // ══════════════════════════════════════════════════════════════

        private void RebuildTargetResults()
        {
            var warnings = new List<string>();
            var specs = new List<(SpecTargetPriceResult Result, SpecGroup Group)>();

            foreach (var group in _dataGrid?.SpecGroups ?? Enumerable.Empty<SpecGroup>())
            {
                if (group == null) continue;

                var (cutSqm, _) = PnLMeasurement.MeasureGroup(group);
                if (cutSqm <= 0m) continue;

                string specName = BuildStep9SpecDisplay(group);
                var (buildType, glassTypeCode) = DetectBuild(group);

                var card = BuildRateCard(group, buildType);
                decimal given = group.BaseRate;

                var spec = SpecificationTargetCalculator.Calculate(
                    group.GroupNumber,
                    specName,
                    glassTypeCode,
                    buildType,
                    card,
                    given,
                    cutSqm);

                var resolution = ResolveUInsert(group, specName, buildType);
                SpecificationTargetCalculator.ApplyUInsert(spec, resolution);

                if (spec.Warnings.Count > 0)
                    warnings.AddRange(spec.Warnings);

                specs.Add((spec, group));
            }

            var aggregate = TargetProfitabilityAggregator.AggregateJob(
                specs.Select(t => t.Result));
            JobTargetResult = aggregate;

            foreach (var w in aggregate.Warnings)
            {
                if (!warnings.Contains(w)) warnings.Add(w);
            }

            _calculationWarnings = warnings.Distinct(StringComparer.Ordinal).ToList();
            OnPropertyChanged(nameof(CalculationWarnings));
            OnPropertyChanged(nameof(HasCalculationWarnings));
            OnPropertyChanged(nameof(CalculationWarningSummary));
            RaiseStep9HealthNotifications();

            // Publish rows in spec order.
            TargetSpecsView.Clear();
            foreach (var (spec, group) in specs)
                TargetSpecsView.Add(SpecTargetRowViewModel.FromResult(spec, group));

            // Preserve the sidebar selection when it still exists. The
            // report stays CLOSED until the user clicks a spec row — no
            // first-row auto-selection (2026-09-06: the report must not
            // open by default; it opens only from the per-group rows).
            SpecTargetRowViewModel? keep = null;
            if (SelectedTargetSpec != null)
            {
                keep = TargetSpecsView.FirstOrDefault(r =>
                    r.SpecNumber == SelectedTargetSpec.SpecNumber
                    && string.Equals(r.SpecName, SelectedTargetSpec.SpecName, StringComparison.Ordinal));
            }

            foreach (var r in TargetSpecsView)
                r.IsSelected = ReferenceEquals(r, keep);

            SelectedTargetSpec = keep;

            OnPropertyChanged(nameof(HasTargetSpecs));
            OnPropertyChanged(nameof(HasJobTarget));
            NotifyTargetOutputsChanged();

            // Track the most specific aggregate status: when any spec is
            // incomplete the job cannot show a real margin.
            LastCalculatedAt = DateTime.Now;
            OnPropertyChanged(nameof(CalculationStateLabel));
            OnPropertyChanged(nameof(CalculationStatus));
        }

        private void NotifyTargetOutputsChanged()
        {
            OnPropertyChanged(nameof(ExpectedProfitMarginPercent));
            OnPropertyChanged(nameof(ExpectedProfitMarginDisplay));
            OnPropertyChanged(nameof(ExpectedProfitMarginFactorLabel));
            OnPropertyChanged(nameof(ExpectedProfitMarginSourceLabel));
            OnPropertyChanged(nameof(RequiredTargetPriceDisplay));
            OnPropertyChanged(nameof(SpecificationTargetAverageDisplay));
            OnPropertyChanged(nameof(UInsertSummaryDisplay));
            OnPropertyChanged(nameof(UInsertSummaryDetail));
            OnPropertyChanged(nameof(HasUInsertSummaryWarning));
            OnPropertyChanged(nameof(MarginAgainstTargetDisplay));
            OnPropertyChanged(nameof(CostBasedProfitMarginPercent));
            OnPropertyChanged(nameof(CostBasedProfitMarginDisplay));
            OnPropertyChanged(nameof(GivenPricePerSqmDisplay));
            OnPropertyChanged(nameof(DifferencePerSqmDisplay));
            OnPropertyChanged(nameof(BasePriceAverageDisplay));
            OnPropertyChanged(nameof(GivenJudgedAverageDisplay));
            OnPropertyChanged(nameof(IncludedUInsertAverageDisplay));
            OnPropertyChanged(nameof(ExtraProfitPerSqmDisplay));
            OnPropertyChanged(nameof(IsExtraProfitLoss));
            OnPropertyChanged(nameof(ExtraProfitMarginPercentDisplay));
            OnPropertyChanged(nameof(BaseMarginPercentDisplay));
            OnPropertyChanged(nameof(BaseBalancePerSqmDisplay));
            OnPropertyChanged(nameof(BaseBalanceValueDisplay));
            OnPropertyChanged(nameof(IsBaseMarginBelow));
            OnPropertyChanged(nameof(TargetMarginPercentDisplay));
            OnPropertyChanged(nameof(TargetBalancePerSqmDisplay));
            OnPropertyChanged(nameof(TargetBalanceValueDisplay));
            OnPropertyChanged(nameof(IsTargetMarginBelow));
            OnPropertyChanged(nameof(TargetJobStatusLabel));
            OnPropertyChanged(nameof(IsTargetBelow));
            OnPropertyChanged(nameof(IsTargetAbove));
            OnPropertyChanged(nameof(IsTargetIncomplete));
            OnPropertyChanged(nameof(TargetFormulaPreview));
            OnPropertyChanged(nameof(TargetFormulaSpecLabel));
            OnPropertyChanged(nameof(TargetFormulaStepsText));
            OnPropertyChanged(nameof(TargetFormulaAllSpecsText));
            OnPropertyChanged(nameof(TargetScopeNote));
            OnPropertyChanged(nameof(TotalGivenValueDisplay));
            OnPropertyChanged(nameof(TotalRequiredTargetValueDisplay));
            OnPropertyChanged(nameof(TotalDifferenceValueDisplay));
            OnPropertyChanged(nameof(CalculationVersionLabel));
            RaiseStep9HealthNotifications();
        }

        public string CalculationVersionLabel => $"Target model v{TargetCalculationVersion}";

        // ── Adapters from the Step 5 / Step 4 / Step 6 objects ──────────

        private static (GlassBuildType BuildType, string GlassTypeCode) DetectBuild(SpecGroup group)
        {
            if (group == null) return (GlassBuildType.Unknown, "");

            string specName = group.SelectedSpecName ?? "";
            string rawType = group.SelectedGlassType ?? "";
            string type = rawType.Trim().ToUpperInvariant();

            var spacer = PnLSpecParser.Spacer(specName);
            var interlayer = PnLSpecParser.Interlayer(specName);
            bool spacerDetected = spacer.Success;
            bool pvbDetected = interlayer.Success;

            if (type == "DGU+LAM" || (spacerDetected && pvbDetected))
                return (GlassBuildType.DguLam, "DGU+LAM");

            if (type.Contains("DGU") || spacerDetected)
                return (GlassBuildType.Dgu, "DGU");

            if (type.Contains("LAM") || pvbDetected)
                return (GlassBuildType.Lam, "LAM");

            if (type.Length == 0 || type.Contains("SGU") || type.Contains("SINGLE"))
                return (GlassBuildType.Sgu, "SGU");

            return (GlassBuildType.Unknown, rawType);
        }

        /// <summary>
        /// The yield factor used by the specification formula
        /// ((glass ÷ yield) + add-ons) × (1 + margin/100).
        ///
        /// APPROVED LINKAGE (2026-09): the yield IS the cutting-wastage
        /// allowance — 15% wastage = ÷0.85, 10% = ÷0.90, 25% = ÷0.75.
        /// It is therefore derived from the Step 9 WASTAGE % box
        /// (yield = 1 − wastage/100) rather than read from the Step 4
        /// Yield rows, so the formula the operator watches on Step 9
        /// moves the moment they edit the box. Step 4's own Yield rows
        /// keep driving the Step 4 sell-rate preview and are untouched
        /// by this rule.
        /// </summary>
        private decimal EffectiveYieldFactor()
        {
            // WastagePercent is clamped to 0–95 by its setter, so the
            // yield stays within (0.05 … 1.00]; the >0 guard in the
            // shared calculator therefore never trips for a valid box.
            decimal yield = 1m - (WastagePercent / 100m);
            return yield <= 0m ? 0.05m : yield;
        }


        private decimal ResolveTargetPaneRate(
            SpecGroup group,
            string paneSide,
            string paneColor,
            decimal groupRate,
            decimal liveFallback)
        {
            // Target formula contract: Step 4 pane price boxes copied onto
            // the Step 5 group are authoritative. Step 6 wastage allocations
            // are actual-cost mechanics and must not override the commercial
            // target formula. Use live panel rates only for legacy rows with
            // no per-group Step 4 handoff at all.
            bool hasAuthoritativeHandoff = paneSide.IndexOf("Inner", StringComparison.OrdinalIgnoreCase) >= 0
                || paneSide.IndexOf("Lam", StringComparison.OrdinalIgnoreCase) >= 0
                ? HasInnerPaneHandoff(group)
                : HasOuterPaneHandoff(group);

            if (groupRate > 0m)
                return groupRate;

            if (hasAuthoritativeHandoff)
                return 0m;

            var rowSources = _otherChargesVm?.WastagesSection?.WastageRows
                .SelectMany(r => r.AvailableSources)
                .Where(s => s != null
                    && s.IsChecked
                    && s.SpecNumber == group.GroupNumber
                    && s.PricePerSqm > 0m
                    && Step6PaneSideMatches(s.SideLabel, paneSide))
                .ToList();

            if (rowSources != null && rowSources.Count > 0)
            {
                string color = (paneColor ?? string.Empty).Trim();
                if (color.Length > 0)
                {
                    var exact = rowSources.FirstOrDefault(s =>
                        string.Equals((s.ColorName ?? string.Empty).Trim(),
                            color, StringComparison.OrdinalIgnoreCase));
                    if (exact != null)
                        return exact.PricePerSqm;
                }
            }

            return liveFallback;
        }

        private static bool Step6PaneSideMatches(string sourceSide, string paneSide)
        {
            string src = sourceSide ?? string.Empty;
            string pane = paneSide ?? string.Empty;

            if (pane.IndexOf("Lam Inner", StringComparison.OrdinalIgnoreCase) >= 0)
                return src.IndexOf("Lam Inner", StringComparison.OrdinalIgnoreCase) >= 0;
            if (pane.IndexOf("Lam Outer", StringComparison.OrdinalIgnoreCase) >= 0)
                return src.IndexOf("Lam Outer", StringComparison.OrdinalIgnoreCase) >= 0;
            if (pane.IndexOf("Inner", StringComparison.OrdinalIgnoreCase) >= 0)
                return src.IndexOf("Inner", StringComparison.OrdinalIgnoreCase) >= 0
                    || src.IndexOf("Middle", StringComparison.OrdinalIgnoreCase) >= 0;
            return src.IndexOf("Outer", StringComparison.OrdinalIgnoreCase) >= 0
                && src.IndexOf("Lam Outer", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static string PaneColor(SpecGroup group, string paneSide)
        {
            // Exact per-spec Step 4 colour wins. The spec-name parser is only
            // fallback for legacy/manual rows where Step 4 did not hand off a
            // colour. This prevents "Obscure Clear Ramly" from being treated
            // as the parsed/generated "Clear" pane in target comparisons.
            if (paneSide.IndexOf("Inner", StringComparison.OrdinalIgnoreCase) >= 0
                || paneSide.IndexOf("Lam", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (!string.IsNullOrWhiteSpace(group?.InnerGlassColor))
                    return group.InnerGlassColor;
            }
            else if (!string.IsNullOrWhiteSpace(group?.OuterGlassColor))
            {
                return group.OuterGlassColor;
            }

            var panes = PnLSpecParser.ResolveAllPanes(group?.SelectedSpecName);
            if (panes.Count > 0)
            {
                if (paneSide.IndexOf("Lam Inner", StringComparison.OrdinalIgnoreCase) >= 0)
                    return panes.Count >= 3 ? panes[^1].Color : panes[^1].Color;
                if (paneSide.IndexOf("Lam Outer", StringComparison.OrdinalIgnoreCase) >= 0)
                    return panes.Count >= 2 ? panes[1].Color : string.Empty;
                if (paneSide.IndexOf("Inner", StringComparison.OrdinalIgnoreCase) >= 0)
                    return panes.Count >= 2 ? panes[^1].Color : string.Empty;
                return panes[0].Color;
            }

            return string.Empty;
        }

        /// <summary>
        /// Builds the commercial target-rate card from the spec group's
        /// exact Step 4 handoff metadata first, then falls back to the bound
        /// Step 4 panel of the same build type only for legacy/incomplete
        /// rows. IMPORTANT: this target formula uses the Step 4 pane box
        /// rates (Outer + Inner) and the Step 9 wastage yield (15% => ÷0.85).
        /// It must not use Step 6 allocated sheet-cost-per-finished-SQM;
        /// that belongs to actual sheet costing, not the target price.
        /// </summary>
        private SpecificationRateCard? BuildRateCard(SpecGroup group, GlassBuildType buildType)
        {
            if (group == null) return null;

            decimal yield = EffectiveYieldFactor();
            decimal upliftPercent = ExpectedProfitMarginPercent;

            switch (buildType)
            {
                case GlassBuildType.Dgu:
                    return new SpecificationRateCard
                    {
                        BuildType = GlassBuildType.Dgu,
                        OuterGlassRatePerSqm = ResolveTargetPaneRate(group, "Outer", PaneColor(group, "Outer"), group.OuterSheetPricePerSqm, _dguVm?.OuterBasePrice ?? 0m),
                        InnerGlassRatePerSqm = ResolveTargetPaneRate(group, "Inner", PaneColor(group, "Inner"), group.InnerSheetPricePerSqm, _dguVm?.InnerBasePrice ?? 0m),
                        AspRatePerSqm = group.Step4SpacerPricePerSqm ?? (HasOuterPaneHandoff(group) || HasInnerPaneHandoff(group) ? 0m : (_dguVm?.SpacerPrice ?? 0m)),
                        ProcessingRatePerSqm = 0m,
                        YieldFactor = yield,
                        TargetUpliftPercent = upliftPercent
                    };

                case GlassBuildType.Sgu:
                    return new SpecificationRateCard
                    {
                        BuildType = GlassBuildType.Sgu,
                        OuterGlassRatePerSqm = ResolveTargetPaneRate(group, "Outer", PaneColor(group, "Outer"), group.OuterSheetPricePerSqm, _sguVm?.BasePricePerSqm ?? 0m),
                        InnerGlassRatePerSqm = 0m,
                        AspRatePerSqm = 0m,
                        ProcessingRatePerSqm = group.Step4CuttingCostPerSqm ?? (HasOuterPaneHandoff(group) ? 0m : ((_sguVm?.CuttingCostPerSqm ?? 0m) + (_sguVm?.TemperingCostPerSqm ?? 0m))),
                        YieldFactor = yield,
                        TargetUpliftPercent = upliftPercent
                    };

                case GlassBuildType.Lam:
                    return new SpecificationRateCard
                    {
                        BuildType = GlassBuildType.Lam,
                        OuterGlassRatePerSqm = ResolveTargetPaneRate(group, "Outer", PaneColor(group, "Outer"), group.OuterSheetPricePerSqm, _lamVm?.OuterBasePrice ?? 0m),
                        InnerGlassRatePerSqm = ResolveTargetPaneRate(group, "Inner", PaneColor(group, "Inner"), group.InnerSheetPricePerSqm, _lamVm?.InnerBasePrice ?? 0m),
                        AspRatePerSqm = 0m,
                        ProcessingRatePerSqm = (group.Step4InterlayerCostPerSqm ?? (HasOuterPaneHandoff(group) || HasInnerPaneHandoff(group) ? 0m : (_lamVm?.PvbMaterialRate ?? 0m)))
                                               + (group.Step4CuttingCostPerSqm ?? (HasOuterPaneHandoff(group) || HasInnerPaneHandoff(group) ? 0m : ((_lamVm?.OuterCuttingCost ?? 0m) + (_lamVm?.InnerCuttingCost ?? 0m)
                                                   + (_lamVm?.OuterTemperingCost ?? 0m) + (_lamVm?.InnerTemperingCost ?? 0m)))),
                        YieldFactor = yield,
                        TargetUpliftPercent = upliftPercent
                    };

                case GlassBuildType.DguLam:
                    return new SpecificationRateCard
                    {
                        BuildType = GlassBuildType.DguLam,
                        OuterGlassRatePerSqm = ResolveTargetPaneRate(group, "Outer", PaneColor(group, "Outer"), group.OuterSheetPricePerSqm, _dguLamVm?.OuterBasePrice ?? 0m),
                        InnerGlassRatePerSqm = ResolveTargetPaneRate(group, "Lam Inner", PaneColor(group, "Lam Inner"), group.InnerSheetPricePerSqm, _dguLamVm?.LamInnerBasePrice ?? 0m),
                        ThirdGlassRatePerSqm = ResolveTargetPaneRate(group, "Lam Outer", PaneColor(group, "Lam Outer"), 0m, _dguLamVm?.LamOuterBasePrice ?? 0m),
                        AspRatePerSqm = group.Step4SpacerPricePerSqm ?? (HasOuterPaneHandoff(group) || HasInnerPaneHandoff(group) ? 0m : (_dguLamVm?.SpacerPrice ?? 0m)),
                        ProcessingRatePerSqm = (group.Step4CuttingCostPerSqm ?? (HasOuterPaneHandoff(group) || HasInnerPaneHandoff(group) ? 0m : (_dguLamVm?.TotalCuttingCost ?? 0m)))
                                               + (group.Step4TemperingCostPerSqm ?? (HasOuterPaneHandoff(group) || HasInnerPaneHandoff(group) ? 0m : (_dguLamVm?.TotalTemperingCost ?? 0m)))
                                               + (group.Step4InterlayerCostPerSqm ?? (HasOuterPaneHandoff(group) || HasInnerPaneHandoff(group) ? 0m : (_dguLamVm?.InterlayerSellPrice ?? 0m))),
                        YieldFactor = yield,
                        TargetUpliftPercent = upliftPercent
                    };

                default:
                    return null;
            }
        }

        /// <summary>
        /// Resolves the U-insert treatment + dynamic rate for one spec group
        /// via the dedicated Step6ChargeResolver. The automation toggle is
        /// never used as the accounting treatment.
        /// </summary>
        private UInsertResolution ResolveUInsert(
            SpecGroup group, string specName, GlassBuildType buildType)
        {
            if (group == null)
                return new UInsertResolution { Treatment = UInsertTreatment.Unresolved };

            var query = new Step6UInsertQuery
            {
                SpecRequiresUInsert = SpecRequiresUInsert(group, specName, buildType),
                Step4SeedRate = group.UInsertPricePerSqm,
                RowExists = FindUInsertRow() != null,
                RowActiveForSpec = UInsertRowActiveForSpec(group),
                RowRate = FindUInsertRow()?.Rate ?? 0m,
                TemplateDefaultRate = PredefinedChargeTemplates.GetAll()
                    .FirstOrDefault(t => string.Equals(t.Key, "U_INSERT", StringComparison.OrdinalIgnoreCase))
                    ?.DefaultRate ?? 0m
            };

            return Step6ChargeResolver.ResolveUInsert(query);
        }

        private bool SpecRequiresUInsert(
            SpecGroup group, string specName, GlassBuildType buildType)
        {
            if (group.UInsertPricePerSqm > 0m) return true;

            string evidence = $"{specName ?? ""} {group.SelectedGlassType ?? ""}";
            if (evidence.IndexOf("U-Insert", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (_otherChargesVm == null) return false;

            try
            {
                bool matchedSameGlass = LaminateGlassMatcher.IsMatchedSameGlass(
                    group.SelectedSpecName,
                    group.SelectedGlassType,
                    group.OuterGlassColor,
                    group.InnerGlassColor);

                return OtherChargeAutomationRules.GetChargeSuggestions(
                        group.SelectedGlassType,
                        group.SelectedSpecName,
                        matchedSameGlass)
                    .Any(s => string.Equals(s.TemplateKey, "U_INSERT", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private Models.ProformaInvoice.Step6OtherCharges.OtherChargeRow? FindUInsertRow()
        {
            return _otherChargesVm?.ChargeRows?.FirstOrDefault(r =>
                string.Equals(r.TemplateKey, "U_INSERT", StringComparison.OrdinalIgnoreCase));
        }

        private bool UInsertRowActiveForSpec(SpecGroup group)
        {
            var row = FindUInsertRow();
            if (row == null || !row.IsActive || row.TargetSelection == null) return false;

            return row.TargetSelection.AvailableSpecs.Any(s =>
                s != null && s.IsChecked && s.SpecGroupNumber == group.GroupNumber);
        }

        private static string TargetStatusLabel(TargetStatus? status)
        {
            return status switch
            {
                TargetStatus.UpToDate => "UP TO DATE",
                TargetStatus.BelowTarget => "BELOW TARGET",
                TargetStatus.AtTarget => "AT TARGET",
                TargetStatus.AboveTarget => "ABOVE TARGET",
                TargetStatus.MissingBasePrice => "MISSING BASE PRICE",
                TargetStatus.MissingGlassRate => "MISSING GLASS RATE",
                TargetStatus.MissingAspRate => "MISSING ASP RATE",
                TargetStatus.MissingYield => "MISSING YIELD",
                TargetStatus.MissingUInsertTreatment => "MISSING U-INSERT TREATMENT",
                TargetStatus.CalculationIncomplete => "CALCULATION INCOMPLETE",
                TargetStatus.SnapshotStale => "SNAPSHOT STALE",
                _ => "—"
            };
        }

        private static string FormatRateCard(decimal? value)
        {
            return value switch
            {
                null => "—",
                var v when v.Value <= 0m => "—",
                var v => $"AED {v.Value:N2}/m²"
            };
        }
    }
}
