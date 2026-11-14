// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step9ProfitLoss/SheetProfitabilityViewModel.cs
// PROJECT: ProGlassAutomation
// MODULE: Sheet Profitability Analysis (Step 9 — SHEET DETAIL tab)
//
// MODEL (proforma-based, NOT stock-based)
//   A job buys whole glass sheets and cuts them. The bought sheets split
//   into exactly two parts:
//
//       BUY SHEET  =  USED SHEET  +  WASTAGE (leftover)
//
//   • BUY SHEET   — sheets bought, priced at the authoritative Step 9
//                   glass row amount (BuildGlassRowAsync already applies
//                   the Step 6 SHEET QTY when the operator set one).
//   • USED SHEET  — whole sheets required to cover the required area
//                   (cut SQM + wastage %), via PnLMeasurement.
//   • WASTAGE     — the LEFTOVER of cutting = BUY − USED (sheets + area +
//                   material value). It is DERIVED, never purchased. There
//                   is deliberately no separate "wastage sheet" line and
//                   no scrap/remnant value.
//
//   The Step 6 WASTAGE CHARGE (WastageRow.ResultAmount) is a separate
//   monetary fee the operator set. It is read from the matching Step 6
//   row's SOURCE (pane side: outer / inner / lam outer / lam inner /
//   middle), its SQM, its SHEET QTY and its rate, then cross-checked
//   against the derived usage:
//
//       bought < used        → SHORTFALL  (Step 6 SHEET QTY too low)
//       charge > 0, no waste → REVIEW     (charged but nothing leftover)
//       waste > 0, no charge → NO CHARGE  (leftover but nothing billed)
//
// FORMULAS
//   Required area  = cut SQM × (1 + wastage % / 100)
//   Used sheets    = ⌈required area ÷ sheet SQM⌉
//   Wastage sheets = buy sheets − used sheets           (raw; negative =
//                                                         shortfall)
//   Buy price      = Step 9 glass row amount            (authoritative)
//   Used price     = required area × sheet rate
//   Wastage value  = buy price − used price             (material leftover)
//   Step 6 charge  = Σ share of WastageRow.ResultAmount per pane source
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges.Wastages;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Services.Reporting;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Sharing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss
{
    /// <summary>
    /// Sheet usage breakdown with a per-pane Step 6 wastage cross-check.
    /// </summary>
    public class SheetProfitabilityViewModel : ViewModelBase
    {
        private readonly ProfitLossStepViewModel _parentViewModel;

        public SheetProfitabilityViewModel(ProfitLossStepViewModel parentViewModel)
        {
            _parentViewModel = parentViewModel ?? throw new ArgumentNullException(nameof(parentViewModel));

            SheetBreakdown = new ObservableCollection<SheetProfitRow>();
            ShortfallMessages = new ObservableCollection<string>();
            ReviewMessages = new ObservableCollection<string>();

            RefreshCommand = new RelayCommand(_ => RefreshAnalysis());
            SelectRowCommand = new RelayCommand(_ => SetSelectedRow(_));
            BackCommand = new RelayCommand(_ => BackToSheetList());
            ExportCsvCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(() => ExportAsync(ExportFormat.Csv), "Step9 Sheet CSV"));
            ExportExcelCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(() => ExportAsync(ExportFormat.Excel), "Step9 Sheet Excel"));
            ExportPdfCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(() => ExportAsync(ExportFormat.Pdf), "Step9 Sheet PDF"));
            ShareCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(ShareAsync, "Step9 Sheet share"));

            // Initial load
            RefreshAnalysis();
        }

        // ══════════════════════════════════════════════════════════════
        //  COLLECTIONS
        // ══════════════════════════════════════════════════════════════

        /// <summary>Per-pane sheet rows.</summary>
        public ObservableCollection<SheetProfitRow> SheetBreakdown { get; }

        /// <summary>Hard errors: a pane buys fewer sheets than it needs.</summary>
        public ObservableCollection<string> ShortfallMessages { get; }

        /// <summary>Softer cross-check notes against the Step 6 setup.</summary>
        public ObservableCollection<string> ReviewMessages { get; }

        // ══════════════════════════════════════════════════════════════
        //  TOTALS
        // ══════════════════════════════════════════════════════════════

        private decimal _totalBuySheets;
        public decimal TotalBuySheets
        {
            get => _totalBuySheets;
            private set => SetTotals(ref _totalBuySheets, value, nameof(TotalBuySheetsDisplay));
        }

        public string TotalBuySheetsDisplay => $"{TotalBuySheets:0}";

        private decimal _totalUsedSheets;
        public decimal TotalUsedSheets
        {
            get => _totalUsedSheets;
            private set => SetTotals(ref _totalUsedSheets, value, nameof(TotalUsedSheetsDisplay));
        }

        public string TotalUsedSheetsDisplay => $"{TotalUsedSheets:0}";

        private decimal _totalWastageSheets;
        /// <summary>Total leftover sheets (shortfalls are not subtracted).</summary>
        public decimal TotalWastageSheets
        {
            get => _totalWastageSheets;
            private set => SetTotals(ref _totalWastageSheets, value, nameof(TotalWastageSheetsDisplay));
        }

        public string TotalWastageSheetsDisplay => $"{TotalWastageSheets:0}";

        private decimal _totalBuySheetArea;
        public decimal TotalBuySheetArea
        {
            get => _totalBuySheetArea;
            private set => SetTotals(ref _totalBuySheetArea, value, nameof(TotalBuySheetAreaDisplay));
        }

        public string TotalBuySheetAreaDisplay => $"{TotalBuySheetArea:0.##} SQM";

        private decimal _totalUsedArea;
        public decimal TotalUsedArea
        {
            get => _totalUsedArea;
            private set => SetTotals(ref _totalUsedArea, value, nameof(TotalUsedAreaDisplay));
        }

        public string TotalUsedAreaDisplay => $"{TotalUsedArea:0.##} SQM";

        private decimal _totalBuyPrice;
        public decimal TotalBuyPrice
        {
            get => _totalBuyPrice;
            private set => SetTotals(ref _totalBuyPrice, value, nameof(TotalBuyPriceDisplay));
        }

        public string TotalBuyPriceDisplay => $"AED {TotalBuyPrice:N2}";

        private decimal _totalUsedPrice;
        public decimal TotalUsedPrice
        {
            get => _totalUsedPrice;
            private set => SetTotals(ref _totalUsedPrice, value, nameof(TotalUsedPriceDisplay));
        }

        public string TotalUsedPriceDisplay => $"AED {TotalUsedPrice:N2}";

        private decimal _totalWastageValue;
        /// <summary>Material value of the leftover glass (buy − used).</summary>
        public decimal TotalWastageValue
        {
            get => _totalWastageValue;
            private set => SetTotals(ref _totalWastageValue, value, nameof(TotalWastageValueDisplay));
        }

        public string TotalWastageValueDisplay => $"AED {TotalWastageValue:N2}";

        private decimal _totalStep6Charge;
        /// <summary>Sum of the Step 6 wastage charges read per pane.</summary>
        public decimal TotalStep6Charge
        {
            get => _totalStep6Charge;
            private set => SetTotals(ref _totalStep6Charge, value, nameof(TotalStep6ChargeDisplay));
        }

        public string TotalStep6ChargeDisplay => $"AED {TotalStep6Charge:N2}";

        // ── RECONCILIATION TOTALS (2026-09-11, owner formula) ──────────
        // BUY = USED + WASTAGE; the Step 6 charge RECOVERS part of the
        // wastage; the remainder is the NET wastage the factory absorbs:
        //     NET = TotalWastageValue − TotalStep6Charge (1,586.66 −
        //           162.22 = 1,424.44 in the owner's example).
        private decimal _totalRecoveredWastageValue;

        public decimal TotalRecoveredWastageValue
        {
            get => _totalRecoveredWastageValue;
            private set => SetTotals(ref _totalRecoveredWastageValue, value, nameof(TotalRecoveredWastageValueDisplay));
        }

        public string TotalRecoveredWastageValueDisplay => $"AED {TotalRecoveredWastageValue:N2}";

        /// <summary>Owner formula, exact at total level: wastage value minus
        /// the Step 6 charge. Negative only in the pathological over-billed
        /// case (charge above the wastage's material value).</summary>
        public decimal TotalNetWastageLoss => TotalWastageValue - TotalStep6Charge;

        public string TotalNetWastageLossDisplay => $"AED {TotalNetWastageLoss:N2}";

        /// <summary>One-line identity with the recovery chain.</summary>
        public string ReconciliationSummaryDisplay =>
            $"BUY {TotalBuyPriceDisplay}  =  USED {TotalUsedPriceDisplay}" +
            $"  +  WASTAGE {TotalWastageValueDisplay}" +
            $"   ·   STEP 6 RECOVERY {TotalStep6ChargeDisplay}" +
            $"   ·   NET WASTAGE LOSS {TotalNetWastageLossDisplay}" +
            $"   ·   BALANCE {ReconciliationBalanceDisplay}{(IsInBalance ? " ✓" : " ✗")}";

        /// <summary>Policy notice — shown only while wastage remains
        /// unrecovered (collapses to nothing otherwise).</summary>
        public string NetWastageNotice => TotalNetWastageLoss <= 0m
            ? ""
            : $"After Step 6 billing recovery of {TotalStep6ChargeDisplay}, " +
              $"{TotalNetWastageLossDisplay} of wastage value remains ABSORBED " +
              "(uncharged panes — e.g. clear, typically not billed to the customer — " +
              "recover AED 0.00). Excluded from the charged-efficiency KPI.";

        private decimal _usageEfficiencyPercent;
        public decimal UsageEfficiencyPercent
        {
            get => _usageEfficiencyPercent;
            private set => SetTotals(ref _usageEfficiencyPercent, value, nameof(UsageEfficiencyDisplay));
        }

        public string UsageEfficiencyDisplay => $"{UsageEfficiencyPercent:0.##}%";

        /// <summary>Reconciliation balance: buy − used − wastage value (0 when healthy).</summary>
        public decimal ReconciliationBalance => MoneyRounding.Round(TotalBuyPrice - TotalUsedPrice - TotalWastageValue);

        public string ReconciliationBalanceDisplay => $"{ReconciliationBalance:+#,##0.00;−#,##0.00;0.00}";

        /// <summary>True when the material identity is in balance (no shortfall).</summary>
        public bool IsInBalance => ReconciliationBalance >= 0m;

        /// <summary>Used price as a share of the buy price.</summary>
        public decimal UsedPriceSharePercent => TotalBuyPrice > 0m
            ? Math.Round(TotalUsedPrice / TotalBuyPrice * 100m, 1)
            : 0m;

        public string UsedPriceShareDisplay => $"{UsedPriceSharePercent:0.#}%";

        /// <summary>Wastage material value as a share of the buy price.</summary>
        public decimal WastageValueSharePercent => TotalBuyPrice > 0m
            ? Math.Round(TotalWastageValue / TotalBuyPrice * 100m, 1)
            : 0m;

        public string WastageValueShareDisplay => $"{WastageValueSharePercent:0.#}%";

        // Landed rate on used area (2026-09-11): TotalBuyPrice ÷
        // TotalUsedArea vs the average given rate — the whole-report
        // yield premium in one line.
        public decimal TotalLandedRatePerSqm => TotalUsedArea > 0m
            ? MoneyRounding.Round(TotalBuyPrice / TotalUsedArea)
            : 0m;

        public decimal TotalAvgGivenRatePerSqm => TotalUsedArea > 0m
            ? MoneyRounding.Round(TotalUsedPrice / TotalUsedArea)
            : 0m;

        public decimal TotalYieldPremiumPercent =>
            TotalAvgGivenRatePerSqm > 0m && TotalLandedRatePerSqm > TotalAvgGivenRatePerSqm
                ? Math.Round((TotalLandedRatePerSqm / TotalAvgGivenRatePerSqm - 1m) * 100m, 1)
                : 0m;

        public string LandedRateSummaryDisplay => TotalLandedRatePerSqm <= 0m
            ? ""
            : $"Landed rate on used area: AED {TotalLandedRatePerSqm:N2}/SQM effective " +
              $"(avg given AED {TotalAvgGivenRatePerSqm:N2}/SQM" +
              (TotalYieldPremiumPercent > 0m
                  ? $", +{TotalYieldPremiumPercent:0.#}% yield premium)"
                  : ")");

        /// <summary>Step 6 wastage charge as a share of the buy price.</summary>
        public decimal Step6ChargeSharePercent => TotalBuyPrice > 0m
            ? Math.Round(TotalStep6Charge / TotalBuyPrice * 100m, 1)
            : 0m;

        public string Step6ChargeShareDisplay => $"{Step6ChargeSharePercent:0.#}%";

        private bool _hasShortfalls;
        public bool HasShortfalls
        {
            get => _hasShortfalls;
            private set
            {
                if (_hasShortfalls == value) return;
                _hasShortfalls = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasDiagnostics));
            }
        }

        private bool _hasReviewItems;
        public bool HasReviewItems
        {
            get => _hasReviewItems;
            private set
            {
                if (_hasReviewItems == value) return;
                _hasReviewItems = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasDiagnostics));
            }
        }

        /// <summary>True when any pane needs a second look at Step 6.</summary>
        public bool HasDiagnostics => HasShortfalls || HasReviewItems;

        private void SetTotals(ref decimal field, decimal value, string displayName)
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(displayName);
        }

        private string _statusMessage = "Press REFRESH to analyze sheet profitability.";
        /// <summary>Explains which rows feed the efficiency KPI (charged
        /// basis per the 2026-09-11 directive; all-material fallback).</summary>
        public string EfficiencyBasisNote { get; private set; } = "";

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (_statusMessage == value) return;
                _statusMessage = value ?? "";
                OnPropertyChanged();
            }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (_isBusy == value) return;
                _isBusy = value;
                OnPropertyChanged();
            }
        }

        /// <summary>Refresh command.</summary>
        public ICommand RefreshCommand { get; }

        /// <summary>Opens the full-screen detail for the clicked sheet row.</summary>
        public ICommand SelectRowCommand { get; }

        /// <summary>Returns from the detail screen back to the sheet list.</summary>
        public ICommand BackCommand { get; }

        // ══════════════════════════════════════════════════════════════
        //  EXPORT (CSV / Excel / PDF) — Step 9 report output
        // ══════════════════════════════════════════════════════════════

        public ICommand ExportCsvCommand { get; }
        public ICommand ExportExcelCommand { get; }
        public ICommand ExportPdfCommand { get; }
        public ICommand ShareCommand { get; }

        private bool _isExporting;
        /// <summary>True while an export runs (disables the toolbar).</summary>
        public bool IsExporting
        {
            get => _isExporting;
            private set
            {
                if (_isExporting == value) return;
                _isExporting = value;
                OnPropertyChanged();
            }
        }

        private async Task ExportAsync(ExportFormat format)
        {
            if (IsExporting || SheetBreakdown.Count == 0) return;
            IsExporting = true;
            try
            {
                var result = await ExportCoreAsync(format, null);
                if (result == null) return;

                if (result.Success)
                    StatusMessage = $"Exported {format} — {result.SavedPath}";
                else if (!result.Cancelled)
                    App.Dialogs.Warning(result.ErrorMessage ?? "Export failed.", "Export");
            }
            catch (Exception ex)
            {
                App.Dialogs.Error($"Export failed: {ExceptionMessages.Deep(ex)}", "Export");
            }
            finally
            {
                IsExporting = false;
            }
        }

        /// <summary>
        /// Single report builder behind export (request null → save
        /// dialog) and share (staged temp request). One code path, no
        /// duplicated row mapping.
        /// </summary>
        private async Task<ExportResult?> ExportCoreAsync(
            ExportFormat format, ExportRequest? request)
        {
            // 2026-09-11 OWNER DIRECTIVE — ⚑ FOCUS HIGHLIGHTS: the exported
            // report now opens with the few lines that need attention
            // (reconciliation, shortfalls, net wastage, efficiency, review
            // items) so a reader sees what matters WITHOUT scanning the
            // table. Zero effect on the UI grid — highlights exist only in
            // the export row stream.
            var rows = new List<SheetProfitRow>();
            rows.AddRange(BuildHighlightRows());
            rows.AddRange(SheetBreakdown.ToList());
            rows.Add(BuildTotalRow());

            return await Step9ReportExport.RunAsync(
                format, rows, "Sheet Price and Wastage Report",
                Step9ReportExport.SheetHeaders,
                Step9ReportExport.SheetMap,
                Step9ReportExport.SheetMapExcel,
                Step9ReportExport.ReportCodeSheetPricing,
                _parentViewModel.GetReportDocumentContext(),
                request);
        }

        // Sharing Center — the SAME analysis staged once into the share
        // temp folder (Report rule's default format), then the composer.
        // An internal cost analysis, so it shares under the Report
        // rule/template — never the PI invoice wording.
        private async Task ShareAsync()
        {
            if (IsExporting || SheetBreakdown.Count == 0) return;
            IsExporting = true;
            try
            {
                var identity = _parentViewModel.GetReportDocumentContext();
                var context = ShareContextFactory.FromListReport(
                    "Report", "Sheet Price and Wastage Report", "ProformaInvoice",
                    identity.CustomerName, identity.CompanyName);
                context.DocumentNumber = identity.ProformaId;

                ExportFormat format = ShareLauncher.RuleFormat("Report");
                await ShareLauncher.GenerateAndShareAsync(
                    context,
                    async folder =>
                    {
                        var result = await ExportCoreAsync(
                            format,
                            Step9ReportExport.StagedRequest(
                                format,
                                Step9ReportExport.ReportCodeSheetPricing,
                                identity,
                                folder));
                        if (result == null || !result.Success)
                            App.Dialogs.Warning(
                                result?.ErrorMessage ?? "Export failed.", "Share");
                        return result?.SavedPath;
                    },
                    App.Dialogs);
            }
            catch (Exception ex)
            {
                App.Dialogs.Error($"Share failed: {ExceptionMessages.Deep(ex)}", "Share");
            }
            finally
            {
                IsExporting = false;
            }
        }

        /// <summary>Synthetic TOTAL row appended to the export.</summary>
        /// <summary>
        /// 2026-09-11 ⚑ FOCUS HIGHLIGHTS — the exported report opens with
        /// these rows. Priority order = money risk first (senior selection):
        ///   1. RECONCILIATION — out of balance is a hard stop for the reader
        ///   2. SHORTFALLS   — bought fewer sheets than needed (money risk)
        ///   3. NET WASTAGE  — the owner formula: wastage − Step 6 recovery
        ///   4. EFFICIENCY   — charged basis, flagged under 70% (UI rule)
        ///   5. REVIEW items — charged-without-waste / setup notices
        /// When nothing needs attention: one green ALL-CHECKS-PASSED line.
        /// Highlight rows carry GlassTypeLabel "HIGHLIGHT" (never "TOTAL"),
        /// so the total-row detection and status logic stay intact.
        /// </summary>
        private List<SheetProfitRow> BuildHighlightRows()
        {
            var highlights = new List<SheetProfitRow>();
            if (SheetBreakdown.Count == 0) return highlights;

            SheetProfitRow Focus(string area, string shortLabel, string message)
                => new()
                {
                    SpecName = area,
                    PaneSide = shortLabel,
                    GlassTypeLabel = "HIGHLIGHT",
                    DiagnosticMessage = message
                };

            // 1 · Reconciliation
            highlights.Add(Focus("RECONCILIATION",
                IsInBalance ? "balanced ✓" : "OUT OF BALANCE ✗",
                IsInBalance
                    ? $"BUY {TotalBuyPriceDisplay} = USED {TotalUsedPriceDisplay} + WASTAGE {TotalWastageValueDisplay} — balances to {ReconciliationBalanceDisplay}."
                    : $"BUY {TotalBuyPriceDisplay} vs USED {TotalUsedPriceDisplay} + WASTAGE {TotalWastageValueDisplay} — OUT by {ReconciliationBalanceDisplay}. Review the sheet rows marked SHORTFALL."));

            // 2 · Shortfalls (money risk — always listed)
            if (HasShortfalls)
            {
                string detail = string.Join(" | ",
                    ShortfallMessages.Take(3));
                if (ShortfallMessages.Count > 3)
                    detail += $" | +{ShortfallMessages.Count - 3} more";
                highlights.Add(Focus("SHORTFALLS",
                    $"{ShortfallMessages.Count} to fix",
                    $"{ShortfallMessages.Count} sheet line(s) bought fewer sheets than needed: {detail}"));
            }

            // 3 · Net wastage (owner formula) — shown whenever wastage exists
            if (TotalWastageValue > 0m)
            {
                highlights.Add(Focus("NET WASTAGE",
                    TotalStep6Charge > 0m ? "after Step 6 recovery" : "no recovery",
                    $"Wastage {TotalWastageValueDisplay} — Step 6 recovered {TotalStep6ChargeDisplay}" +
                    $" → NET ABSORBED {TotalNetWastageLossDisplay}." +
                    (TotalNetWastageLoss > 0m
                        ? " Uncharged panes (e.g. clear) are part of the absorbed loss."
                        : "")));
            }

            // 4 · Efficiency — flagged under the UI's own 70% rule
            if (TotalBuyPrice > 0m || TotalUsedArea > 0m)
            {
                bool low = UsageEfficiencyPercent < 70m;
                highlights.Add(Focus("EFFICIENCY",
                    low ? "below target ⚠" : "ok",
                    $"{UsageEfficiencyDisplay} on the charged basis" +
                    (string.IsNullOrWhiteSpace(EfficiencyBasisNote) ? "" : $" ({EfficiencyBasisNote})") +
                    (low ? " — below the 70% target; review sheet sizes and Step 6 SHEET QTY." : ".")));
            }

            // 5 · Review items
            if (HasReviewItems)
            {
                string detail = string.Join(" | ",
                    ReviewMessages.Take(2));
                if (ReviewMessages.Count > 2)
                    detail += $" | +{ReviewMessages.Count - 2} more";
                highlights.Add(Focus("REVIEW",
                    $"{ReviewMessages.Count} item(s)",
                    $"{ReviewMessages.Count} line(s) need a look against the Step 6 setup: {detail}"));
            }

            // Nothing needs attention → one calm green line
            if (!HasShortfalls && !HasReviewItems && IsInBalance)
            {
                highlights.Add(Focus("STATUS",
                    "all clear ✓",
                    "ALL CHECKS PASSED — reconciliation balances, no shortfalls, no review items. No action required."));
            }

            return highlights;
        }

        private SheetProfitRow BuildTotalRow()
        {
            return new SheetProfitRow
            {
                SpecName = "",
                GlassTypeLabel = "TOTAL",
                PaneSide = "",
                SheetSize = "",
                SheetSqm = SheetBreakdown.Sum(r => r.SheetSqm),
                BuySheets = SheetBreakdown.Sum(r => r.BuySheets),
                UsedSheets = SheetBreakdown.Sum(r => r.UsedSheets),
                WastageSheets = SheetBreakdown.Sum(r => r.WastageSheets),
                BuySheetArea = SheetBreakdown.Sum(r => r.BuySheetArea),
                UsedArea = SheetBreakdown.Sum(r => r.UsedArea),
                WastageArea = SheetBreakdown.Sum(r => r.WastageArea),
                PricePerSqm = 0m,
                BuySheetPrice = SheetBreakdown.Sum(r => r.BuySheetPrice),
                UsedSheetPrice = SheetBreakdown.Sum(r => r.UsedSheetPrice),
                WastageValue = SheetBreakdown.Sum(r => r.WastageValue),
                Step6Charge = SheetBreakdown.Sum(r => r.Step6Charge),
                Step6SourceLabel = "",
                Step6SourceSqm = 0m,
                Step6SheetQuantity = 0,
                Step6Rate = 0m,
                UsageEfficiencyPercent = 0m,
                DiagnosticMessage = $"All sheet rows ({SheetBreakdown.Count})"
            };
        }

        private SheetProfitRow? _selectedRow;
        public SheetProfitRow? SelectedRow
        {
            get => _selectedRow;
            private set
            {
                if (ReferenceEquals(_selectedRow, value)) return;
                _selectedRow = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedRow));
            }
        }

        /// <summary>True while a sheet row's detail screen is open.</summary>
        // ════════════════════════════════════════════════════════════════
        //  SECOND SHEET-SOURCE SWITCH
        //
        //  OFF (the default) leaves this tab exactly as it was: driven by the
        //  Step 6 WASTAGE CHARGE SETUP, per the "Senior ERP rule" below. Step 6
        //  can itself now be fed by the optimization run, so turning that on
        //  already improves this tab without changing anything here.
        //
        //  ON reads each pane's sheet quantity and utilization straight from the
        //  finished run, bypassing whatever was typed in Step 6. It is a
        //  per-pane override with a fallback, never a replacement pipeline: a
        //  pane the run has no row for keeps its Step 6 figures rather than
        //  dropping to zero, because a missing sheet count here silently zeroes
        //  the buy side of the ledger.
        // ════════════════════════════════════════════════════════════════

        private bool _useOptimizationSheetSource;

        // Racks already claimed during the current RefreshAnalysis pass.
        //
        // A triple-glazed spec emits BOTH "Middle (W2/H2)" and "Inner (W2/H2)",
        // and a DGU+LAM spec emits "Lam Outer (W2/H2)" and "Lam Inner (W2/H2)".
        // All of them are cut from the group's ONE inner sheet, so they resolve
        // to the same optimization rack. Letting each pane claim that rack's full
        // sheet count would double - or triple - the buy side of the ledger.
        // The first pane to reach a rack takes the run's figures; the rest fall
        // back to their Step 6 allocation, which already splits a shared setup by
        // source SQM. Inventing a per-pane split here would be worse than either.
        private readonly HashSet<(int W, int H, string C)> _feedRacksClaimed = new();

        /// <summary>Read the Sheet Details pane figures from the optimization
        /// run instead of from the Step 6 setup. Off by default.</summary>
        public bool UseOptimizationSheetSource
        {
            get => _useOptimizationSheetSource;
            set
            {
                if (_useOptimizationSheetSource == value) return;
                _useOptimizationSheetSource = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OptimizationSheetSourceLabel));

                // Flipping the source has to rebuild the tab, otherwise the
                // switch looks dead until REFRESH is pressed. Guarded so a flip
                // during an in-flight analysis cannot re-enter it.
                if (!IsBusy) RefreshAnalysis();
            }
        }

        /// <summary>Provenance for the switch, always visible so the operator
        /// knows which source produced the sheet numbers on screen.</summary>
        /// <remarks>
        /// English by design. The switch <i>label</i> next to this is localized
        /// (<c>ProformaInvoice.ReadFromOptimizationRun</c>) because it is UI
        /// chrome; the explanation underneath is a diagnostic, and the whole
        /// diagnostic channel — this property, <see cref="DescribeSheetSource"/>,
        /// the Step 6 wastage mode captions and the optimization export titles —
        /// is English. Mixing the two would split one surface across languages.
        /// The full rationale, and the procedure for localizing the channel
        /// properly if that decision changes, is on
        /// <c>OptimizationResultFeed.SourceLabel</c>.
        /// </remarks>
        public string OptimizationSheetSourceLabel =>
            !_useOptimizationSheetSource
                ? "Sheet Details are driven by the Step 6 wastage setup."
                : ProGlassAutomation.Services.Optimization.OptimizationResultFeed.HasData
                    ? "Sheet Details read sheet qty and utilization from the optimization run — " +
                      ProGlassAutomation.Services.Optimization.OptimizationResultFeed.SourceLabel
                    : "Optimization source is ON but no run is available — falling back to the Step 6 setup.";

        /// <summary>
        /// Per-row provenance for the sheet figures, so a tab where some panes
        /// came from the run and others fell back to Step 6 is auditable row by
        /// row instead of only in the header label.
        ///
        /// Returns null when the switch is off, which keeps the existing
        /// diagnostics byte-identical for anyone not using it.
        /// </summary>
        private string? DescribeSheetSource(bool fromFeed, int sheets, decimal utilization)
        {
            if (fromFeed)
                return $"Sheet qty ({sheets}) and utilization ({utilization:0.##}%) read from the optimization run.";

            // Switch on but this pane kept its Step 6 figures: either the run
            // has no row for it, or another pane already claimed that rack. Both
            // are worth saying, because the header label alone cannot show which
            // rows are which.
            return _useOptimizationSheetSource
                ? "Optimization source is ON; this pane stayed on the Step 6 setup (no unclaimed run row for it)."
                : null;
        }

        /// <summary>Re-reads the feed's provenance for the switch label. Called
        /// when a new run publishes, so the label names the run on screen rather
        /// than the one that was current when the tab was last built.</summary>
        public void NotifyOptimizationSourceChanged() =>
            OnPropertyChanged(nameof(OptimizationSheetSourceLabel));

        /// <summary>
        /// Resolves one pane against the optimization run. Returns false when the
        /// switch is off, when no run is loaded, or when the run has no row for
        /// this pane — in all three cases the caller keeps its existing figures.
        /// </summary>
        private bool TryGetOptimizationSheetFigures(
            int specNumber, string? sideLabel, int widthMm, int heightMm,
            out int sheets, out decimal utilizationPercent)
        {
            sheets = 0;
            utilizationPercent = 0m;

            if (!_useOptimizationSheetSource) return false;

            var hit = ProGlassAutomation.Services.Optimization.OptimizationResultFeed.Find(
                ProGlassAutomation.Services.Optimization.OptimizationResultFeed.Current,
                specNumber, sideLabel, widthMm, heightMm);

            if (hit == null || hit.SheetQuantity <= 0) return false;

            // One claim per rack per rebuild - see _feedRacksClaimed.
            var rackKey = (hit.SheetWidthMm, hit.SheetHeightMm,
                (hit.Composition ?? string.Empty).Trim().ToLowerInvariant());
            if (!_feedRacksClaimed.Add(rackKey)) return false;

            sheets = hit.SheetQuantity;
            utilizationPercent = hit.UtilizationPercent;
            return true;
        }

        public bool HasSelectedRow => SelectedRow != null;

        // ══════════════════════════════════════════════════════════════
        //  METHODS
        // ══════════════════════════════════════════════════════════════

        private void SetSelectedRow(object? parameter)
        {
            var row = parameter as SheetProfitRow;
            foreach (var r in SheetBreakdown)
                r.IsSelected = ReferenceEquals(r, row);

            SelectedRow = row;
        }

        private void BackToSheetList()
        {
            foreach (var r in SheetBreakdown)
                r.IsSelected = false;

            SelectedRow = null;
        }

        /// <summary>
        /// Rebuild the sheet analysis from the authoritative Step 9 glass
        /// rows and the Step 6 wastage setup, then run the cross-check.
        /// </summary>
        public void RefreshAnalysis()
        {
            // Each rebuild re-decides which pane owns which rack.
            _feedRacksClaimed.Clear();

            if (IsBusy) return;
            IsBusy = true;

            try
            {
                SheetBreakdown.Clear();
                ShortfallMessages.Clear();
                ReviewMessages.Clear();
                SelectedRow = null;

                var groups = _parentViewModel.SpecGroupsView;
                if (groups == null || groups.Count == 0)
                {
                    ResetTotals();
                    StatusMessage = "No specifications found. Please add specifications in Step 5.";
                    return;
                }

                decimal wastagePercent = _parentViewModel.WastagePercent;

                decimal buySheetsSum = 0m;
                decimal usedSheetsSum = 0m;
                decimal wastageSheetsSum = 0m;
                decimal buyAreaSum = 0m;
                decimal usedAreaSum = 0m;
                decimal buyPriceSum = 0m;
                decimal usedPriceSum = 0m;
                decimal wastageValueSum = 0m;
                decimal step6ChargeSum = 0m;
                decimal recoveredWastageValueSum = 0m;
                decimal netWastageLossSum = 0m;
                decimal chargedBuyAreaSum = 0m;
                decimal chargedUsedAreaSum = 0m;

                var step6HandledSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // 2026-09-11 OWNER FIX — Step 6 charges must NEVER vanish
                // from this breakdown. A wastage row that carries an amount
                // but has an incomplete sheet setup (no SHEET QTY, no
                // checked pane source, no allocatable sheet size) used to
                // be skipped with a silent `continue`. It now emits a
                // visible "STEP 6 SETUP" notice row + a review message.
                // MONEY RULE: the notice row deliberately carries charge 0
                // in the SUM — the amount is shown in the diagnostic text.
                // When a matching Step 5 pane exists, the fallback path
                // already posts the share on that pane's row; summing the
                // full amount here as well would DOUBLE-COUNT it.
                void EmitSetupNoticeRow(WastageRow setup, string reason)
                {
                    var row = new SheetProfitRow
                    {
                        SpecName = setup.ColorName ?? "Step 6 charge",
                        SpecNumber = 0,
                        GlassTypeLabel = FamilyLabelFor(setup.ColorName ?? ""),
                        PaneSide = "STEP 6 SETUP",
                        GlassName = setup.ColorName ?? "",
                        SheetSize = $"{setup.SelectedSheetWidthMm} × {setup.SelectedSheetHeightMm} mm",
                        Step6Charge = 0m,
                        Step6SourceLabel = setup.SourceSummaryText,
                        Step6SheetQuantity = setup.SheetQuantity,
                        Step6Rate = setup.Rate,
                        IsSetupNotice = true,
                        DiagnosticMessage = reason +
                            (setup.IsActive && setup.ResultAmount > 0m
                                ? $" The setup carries AED {setup.ResultAmount:N2} — complete the Step 6 sheet setup to post it."
                                : setup.IsActive
                                    ? " The setup has no charge amount."
                                    : " The setup row is inactive — its charge is not posted."),
                    };
                    SheetBreakdown.Add(row);
                    ReviewMessages.Add($"{row.SpecName} · STEP 6 SETUP — {reason}");
                }

                void AddSheetRow(SheetProfitRow row)
                {
                    SheetBreakdown.Add(row);

                    if (row.HasShortfall)
                        ShortfallMessages.Add($"{row.SpecName} · {row.PaneSide} — {row.DiagnosticMessage}");
                    else if (row.HasChargeWithoutWastage || row.HasWastageWithoutCharge)
                        ReviewMessages.Add($"{row.SpecName} · {row.PaneSide} — {row.DiagnosticMessage}");

                    buySheetsSum += row.BuySheets;
                    usedSheetsSum += row.UsedSheets;
                    wastageSheetsSum += Math.Max(0m, row.WastageSheets);
                    buyAreaSum += row.BuySheetArea;
                    usedAreaSum += row.UsedArea;
                    buyPriceSum += row.BuySheetPrice;
                    usedPriceSum += row.UsedSheetPrice;
                    wastageValueSum += row.WastageValue;
                    step6ChargeSum += row.Step6Charge;
                    recoveredWastageValueSum += row.WastageValueRecovered;
                    netWastageLossSum += row.WastageValueNetLoss;
                    if (row.Step6Charge > 0m && !row.IsSetupNotice)
                    {
                        chargedBuyAreaSum += row.BuySheetArea;
                        chargedUsedAreaSum += row.UsedArea;
                    }
                }

                // Senior ERP rule: the Sheet Details tab must be driven by
                // the Step 6 WASTAGE CHARGE SETUP whenever it exists. That
                // setup is where the operator chose the pane source, the
                // sheet size, the exact sheet quantity and the optimization
                // percentage. The Step 9 P&L glass rows are only a fallback
                // for panes that do not yet have a Step 6 setup source.
                var setupRows = _parentViewModel._otherChargesVm?.WastagesSection?.WastageRows;
                if (setupRows != null)
                {
                    foreach (var setup in setupRows)
                    {
                        if (setup == null) continue;

                        var sources = setup.AvailableSources
                            .Where(s => s != null && s.IsChecked)
                            .ToList();
                        if (setup.SheetQuantity <= 0)
                        {
                            EmitSetupNoticeRow(setup,
                                "Step 6 charge has no SHEET QTY — set the quantity in the Step 6 wastage setup.");
                            continue;
                        }
                        if (sources.Count == 0)
                        {
                            EmitSetupNoticeRow(setup,
                                "Step 6 charge has no checked pane source — tick at least one source in the Step 6 wastage setup.");
                            continue;
                        }

                        decimal totalSourceSqm = sources.Sum(s => s.SqmValue);
                        if (totalSourceSqm <= 0m)
                            totalSourceSqm = setup.TotalSqm;

                        var allocations = Step6SheetAllocation.Allocate(setup, sources);
                        if (allocations.Count == 0)
                        {
                            EmitSetupNoticeRow(setup,
                                "Step 6 charge could not be allocated — its sources have no usable sheet sizes.");
                            continue;
                        }

                        foreach (var allocation in allocations)
                        {
                            var src = allocation.Source;
                            decimal sheetSqm = allocation.SheetSqm;
                            decimal unitRate = src.PricePerSqm > 0m
                                ? src.PricePerSqm
                                : setup.FormulaPricePerSqm;

                            decimal buySheets = allocation.SheetQuantity;
                            decimal usedSheets = allocation.SheetQuantity;
                            decimal utilizationPercent = Math.Min(100m, allocation.UtilizationPercent);

                            // Second sheet-source switch: the run's own figures
                            // for this pane replace the Step 6 allocation. The
                            // source carries its sheet size, which confirms the
                            // rack rather than matching on the pane name alone.
                            bool fromFeed = TryGetOptimizationSheetFigures(
                                src.SpecNumber, src.SideLabel,
                                src.SheetWidthMm, src.SheetHeightMm,
                                out int feedSheets, out decimal feedUtilization);

                            if (fromFeed)
                            {
                                buySheets = feedSheets;
                                usedSheets = feedSheets;
                                utilizationPercent = Math.Min(100m, feedUtilization);
                            }

                            decimal buySheetArea = Math.Round(buySheets * sheetSqm, 4);
                            decimal usedArea = utilizationPercent > 0m
                                ? Math.Round(buySheetArea * utilizationPercent / 100m, 4)
                                : src.SqmValue;
                            decimal wastageArea = buySheetArea - usedArea;

                            decimal buySheetPrice = MoneyRounding.Round(buySheetArea * unitRate);
                            decimal usedSheetPrice = MoneyRounding.Round(usedArea * unitRate);
                            decimal wastageValue = Math.Max(0m, buySheetPrice - usedSheetPrice);
                            decimal charge = setup.IsActive && setup.ResultAmount > 0m && totalSourceSqm > 0m
                                ? MoneyRounding.Round(setup.ResultAmount * (src.SqmValue / totalSourceSqm))
                                : 0m;

                            // 2026-09-11 SHORTFALL on setup rows: the checked
                            // sources may need MORE whole sheets than Step 6
                            // bought (classic with the default SHEET QTY 1).
                            // That is a shortfall — surfaced with a negative
                            // WastageSheets so the SHORTFALL badge + list fire —
                            // never as >100% "efficiency".
                            int neededSheets = sheetSqm > 0m
                                ? (int)Math.Ceiling(totalSourceSqm / sheetSqm)
                                : allocation.SheetQuantity;
                            bool setupShortfall = neededSheets > allocation.SheetQuantity;

                            bool mixedSetup = sources.Count > 1;
                            string diagnostic = setupShortfall
                                ? $"SHORTFALL — checked sources need ≈{neededSheets} sheet(s) but Step 6 bought {allocation.SheetQuantity}. Review the Step 6 SHEET QTY."
                                : mixedSetup
                                    ? $"Shared Step 6 setup: {setup.SheetQuantity} sheet(s) split by source SQM and {utilizationPercent:0.##}% utilization. This spec receives {allocation.SheetQuantity} sheet(s)."
                                    : "Step 6 wastage setup source — bought and used sheet quantity both read from Step 6.";
                            if (charge <= 0m && wastageArea > 0m && !setupShortfall)
                                diagnostic += " No active wastage charge is posted for the leftover area.";

                            // Appended, not prepended: this tab is deliberately
                            // alerts-first, so a SHORTFALL stays at the front.
                            string? sourceNote = DescribeSheetSource(
                                fromFeed, feedSheets, feedUtilization);
                            if (sourceNote != null) diagnostic += " " + sourceNote;

                            string key = SheetSourceKey(src.SpecNumber, src.SideLabel);
                            step6HandledSources.Add(key);

                            AddSheetRow(new SheetProfitRow
                            {
                                SpecName = src.SpecName,
                                SpecNumber = src.SpecNumber,
                                GlassTypeLabel = FamilyLabelFor(src.SpecName),
                                PaneSide = src.SemanticSideLabel,
                                GlassName = src.ColorName,
                                SheetSize = $"{src.SheetWidthMm} × {src.SheetHeightMm} mm",
                                SheetSqm = sheetSqm,
                                BuySheets = buySheets,
                                UsedSheets = setupShortfall ? neededSheets : usedSheets,
                                WastageSheets = setupShortfall ? buySheets - neededSheets : 0m,
                                BuySheetArea = buySheetArea,
                                UsedArea = usedArea,
                                WastageArea = wastageArea,
                                PricePerSqm = unitRate,
                                BuySheetPrice = buySheetPrice,
                                UsedSheetPrice = usedSheetPrice,
                                WastageValue = wastageValue,
                                Step6Charge = charge,
                                Step6SourceLabel = src.DisplayLabel,
                                Step6SourceSqm = src.SqmValue,
                                Step6SheetQuantity = allocation.SheetQuantity,
                                Step6Rate = setup.Rate,
                                UsageEfficiencyPercent = utilizationPercent,
                                DiagnosticMessage = diagnostic
                            });
                        }
                    }
                }

                foreach (var specGroup in groups)
                {
                    var specName = specGroup.SpecName ?? "";
                    var cutSqm = specGroup.CutSqm;
                    if (cutSqm <= 0) continue;

                    var glassFamilyLabel = FamilyLabelFor(specName);

                    var glassRows = specGroup.Rows.Where(r => r.Kind == "Glass").ToList();
                    foreach (var glassRow in glassRows)
                    {
                        var sheetSqm = glassRow.SheetSqm;
                        if (sheetSqm <= 0) continue;

                        var unitRate = glassRow.PricePerSqm;

                        string paneSide = PaneSideFrom(glassRow.Description);
                        string? paneStep6SideLabel = Step6SideLabelForPane(paneSide);
                        if (paneStep6SideLabel != null
                            && step6HandledSources.Contains(SheetSourceKey(specGroup.SpecNumber, paneStep6SideLabel)))
                        {
                            continue;
                        }

                        // ── Step 6 linkage ────────────────────────────────
                        int step6SheetQuantity = ResolveStep6SheetQuantity(
                            specGroup.SpecNumber, paneSide);
                        var step6 = ResolveStep6Charge(specGroup.SpecNumber, paneSide);

                        // Second sheet-source switch. paneStep6SideLabel is the
                        // Step 6 side vocabulary, which is what the run is keyed
                        // by; a null side means this pane has no Step 6 identity
                        // and therefore nothing to look up.
                        decimal optimizationUtilization = 0m;

                        // Declared separately, not as out-var declarations inside
                        // the condition: the && short-circuits when this pane has
                        // no Step 6 side, so the call never happens and the
                        // compiler cannot prove them assigned. Reading them later
                        // for the provenance note would then be CS0165.
                        int paneFeedSheets = 0;
                        decimal paneFeedUtilization = 0m;

                        bool paneFromFeed = paneStep6SideLabel != null &&
                            TryGetOptimizationSheetFigures(
                                specGroup.SpecNumber, paneStep6SideLabel, 0, 0,
                                out paneFeedSheets, out paneFeedUtilization);

                        if (paneFromFeed)
                        {
                            step6SheetQuantity = paneFeedSheets;
                            optimizationUtilization = paneFeedUtilization;
                        }

                        // Used side of the ledger. When Step 6 sheet setup is
                        // present, the bought sheet count is the used sheet
                        // count (e.g. 17 bought = 17 used). The Step 6 OPT %
                        // then splits that bought area into utilized vs wastage
                        // area/value. Without Step 6 setup, preserve the legacy
                        // cut SQM + Step 9 wastage-percent fallback.
                        var requiredSqm = PnLMeasurement.AreaWithWastage(cutSqm, wastagePercent);
                        var roundedRequiredSheets = PnLMeasurement.WholeSheetsRequired(requiredSqm, sheetSqm);

                        bool pricedByArea = (glassRow.Description ?? "")
                            .IndexOf("priced by required area", StringComparison.OrdinalIgnoreCase) >= 0;

                        decimal buySheets = step6SheetQuantity > 0
                            ? step6SheetQuantity
                            : pricedByArea ? roundedRequiredSheets : glassRow.Quantity;
                        if (buySheets <= 0) continue;

                        decimal buySheetArea = pricedByArea && step6SheetQuantity <= 0
                            ? requiredSqm
                            : Math.Round(buySheets * sheetSqm, 4);

                        decimal utilizationPercent = optimizationUtilization > 0m
                            ? optimizationUtilization
                            : step6.OptimizationPercent > 0m
                            ? step6.OptimizationPercent
                            : buySheetArea > 0m
                                ? Math.Min(100m, Math.Round(requiredSqm / buySheetArea * 100m, 2))
                                : 0m;

                        decimal usedSheets = step6SheetQuantity > 0
                            ? buySheets
                            : roundedRequiredSheets;
                        decimal usedArea = step6SheetQuantity > 0 && utilizationPercent > 0m
                            ? Math.Round(buySheetArea * utilizationPercent / 100m, 4)
                            : requiredSqm;

                        decimal wastageSheets = step6SheetQuantity > 0
                            ? 0m
                            : buySheets - usedSheets;
                        decimal wastageArea = buySheetArea - usedArea;

                        decimal buySheetPrice = step6SheetQuantity > 0 && unitRate > 0m
                            ? MoneyRounding.Round(buySheetArea * unitRate)
                            : glassRow.Amount;
                        decimal usedSheetPrice = MoneyRounding.Round(usedArea * unitRate);
                        decimal wastageValue = Math.Max(0m, buySheetPrice - usedSheetPrice);

                        string diagnostic;
                        if (wastageSheets < 0m)
                        {
                            string qtyNote = step6SheetQuantity > 0
                                ? $"Step 6 SHEET QTY is {step6SheetQuantity}."
                                : "No Step 6 SHEET QTY is set for this pane.";
                            diagnostic = $"SHORTFALL — bought {buySheets:0} sheet(s) but {usedSheets:0} " +
                                         $"are required. {qtyNote} Bought sheets are less than the " +
                                         $"sheets needed — review the Step 6 wastage setup.";
                        }
                        else if (step6.Charge > 0m && wastageArea <= 0m)
                        {
                            diagnostic = $"Step 6 charges {step6.Charge:N2} AED for this pane " +
                                         $"(source {step6.SourceLabel}), but there is no leftover " +
                                         $"wastage area. Review the Step 6 wastage setup.";
                        }
                        else if (wastageArea > 0m && step6.Charge <= 0m)
                        {
                            diagnostic = $"No Step 6 wastage charge is set for this pane, though " +
                                         $"{wastageArea:0.##} SQM is leftover.";
                        }
                        else
                        {
                            diagnostic = "";
                        }

                        // Only annotate when this pane had a Step 6 side to look
                        // up at all. Without one the reason the figures stayed on
                        // Step 6 is "no pane identity", not "no run row for it".
                        string? paneSourceNote = paneStep6SideLabel != null
                            ? DescribeSheetSource(
                                paneFromFeed, paneFeedSheets, paneFeedUtilization)
                            : null;
                        if (paneSourceNote != null) diagnostic += " " + paneSourceNote;

                        var row = new SheetProfitRow
                        {
                            SpecName = specName,
                            SpecNumber = specGroup.SpecNumber,
                            GlassTypeLabel = glassFamilyLabel,
                            PaneSide = paneSide,
                            GlassName = GlassNameFromDescription(glassRow.Description),
                            SheetSize = $"{glassRow.SheetWidthMm} × {glassRow.SheetHeightMm} mm",
                            SheetSqm = sheetSqm,
                            BuySheets = buySheets,
                            UsedSheets = usedSheets,
                            WastageSheets = wastageSheets,
                            BuySheetArea = buySheetArea,
                            UsedArea = usedArea,
                            WastageArea = wastageArea,
                            PricePerSqm = unitRate,
                            BuySheetPrice = buySheetPrice,
                            UsedSheetPrice = usedSheetPrice,
                            WastageValue = wastageValue,
                            Step6Charge = step6.Charge,
                            Step6SourceLabel = step6.SourceLabel,
                            Step6SourceSqm = step6.SourceSqm,
                            Step6SheetQuantity = step6SheetQuantity,
                            Step6Rate = step6.Rate,
                            UsageEfficiencyPercent = utilizationPercent,
                            DiagnosticMessage = diagnostic
                        };

                        AddSheetRow(row);
                    }
                }

                TotalBuySheets = buySheetsSum;
                TotalUsedSheets = usedSheetsSum;
                TotalWastageSheets = wastageSheetsSum;
                TotalBuySheetArea = buyAreaSum;
                TotalUsedArea = usedAreaSum;
                TotalBuyPrice = buyPriceSum;
                TotalUsedPrice = usedPriceSum;
                TotalWastageValue = wastageValueSum;
                TotalStep6Charge = step6ChargeSum;
                TotalRecoveredWastageValue = recoveredWastageValueSum;
                OnPropertyChanged(nameof(TotalNetWastageLoss));
                OnPropertyChanged(nameof(TotalNetWastageLossDisplay));
                OnPropertyChanged(nameof(ReconciliationSummaryDisplay));
                OnPropertyChanged(nameof(NetWastageNotice));

                // 2026-09-11 OWNER DIRECTIVE: the efficiency KPI is computed
                // on STEP 6-CHARGED rows only — panes deliberately not
                // charged (e.g. clear) must not distort it. All-material
                // fallback when nothing is charged; clamped ≤ 100 (a >100%
                // utilization is a shortfall, never "efficiency").
                decimal effBuy = chargedBuyAreaSum;
                decimal effUsed = chargedUsedAreaSum;
                if (effBuy <= 0m)
                {
                    effBuy = buyAreaSum;
                    effUsed = usedAreaSum;
                    EfficiencyBasisNote =
                        "Efficiency shown on the ALL-MATERIAL basis (no Step 6 charges posted).";
                }
                else
                {
                    EfficiencyBasisNote =
                        "Efficiency computed on Step 6-CHARGED rows only — uncharged panes (e.g. clear) excluded.";
                }
                UsageEfficiencyPercent = effBuy > 0m
                    ? Math.Min(100m, Math.Round(effUsed / effBuy * 100m, 1))
                    : 0m;

                HasShortfalls = ShortfallMessages.Count > 0;
                HasReviewItems = ReviewMessages.Count > 0;

                // Re-raise computed comparison figures (they derive from the
                // totals set above).
                OnPropertyChanged(nameof(ReconciliationBalance));
                OnPropertyChanged(nameof(ReconciliationBalanceDisplay));
                OnPropertyChanged(nameof(IsInBalance));
                OnPropertyChanged(nameof(UsedPriceSharePercent));
                OnPropertyChanged(nameof(UsedPriceShareDisplay));
                OnPropertyChanged(nameof(WastageValueSharePercent));
                OnPropertyChanged(nameof(WastageValueShareDisplay));
                OnPropertyChanged(nameof(LandedRateSummaryDisplay));
                OnPropertyChanged(nameof(ReconciliationSummaryDisplay));
                OnPropertyChanged(nameof(NetWastageNotice));
                OnPropertyChanged(nameof(EfficiencyBasisNote));
                OnPropertyChanged(nameof(Step6ChargeSharePercent));
                OnPropertyChanged(nameof(Step6ChargeShareDisplay));

                if (SheetBreakdown.Count == 0)
                {
                    StatusMessage = "No priced glass sheets found — add glass rows in Step 5 and press REFRESH.";
                }
                else if (HasShortfalls)
                {
                    StatusMessage = $"{ShortfallMessages.Count} shortfall(s) detected — bought sheets are less " +
                                    $"than the sheets needed. Review the Step 6 wastage setup.";
                }
                else if (HasReviewItems)
                {
                    StatusMessage = $"{ReviewMessages.Count} item(s) to review against the Step 6 wastage setup.";
                }
                else
                {
                    StatusMessage = $"Analysis complete. Bought {TotalBuySheets:0} · used {TotalUsedSheets:0} · " +
                                    $"wastage {TotalWastageSheets:0} sheet(s). Step 6 wastage charge: " +
                                    $"{TotalStep6ChargeDisplay}. In balance.";
                }
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[SheetProfitabilityViewModel] Refresh failed: {ExceptionMessages.Deep(ex)}");
                StatusMessage = "Analysis failed. Please check the data and try again.";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ResetTotals()
        {
            TotalBuySheets = 0m;
            TotalUsedSheets = 0m;
            TotalWastageSheets = 0m;
            TotalBuySheetArea = 0m;
            TotalUsedArea = 0m;
            TotalBuyPrice = 0m;
            TotalUsedPrice = 0m;
            TotalWastageValue = 0m;
            TotalStep6Charge = 0m;
            TotalRecoveredWastageValue = 0m;
            OnPropertyChanged(nameof(TotalNetWastageLoss));
            OnPropertyChanged(nameof(TotalNetWastageLossDisplay));
            OnPropertyChanged(nameof(NetWastageNotice));
            EfficiencyBasisNote = "";
            OnPropertyChanged(nameof(EfficiencyBasisNote));
            UsageEfficiencyPercent = 0m;
            HasShortfalls = false;
            HasReviewItems = false;
        }

        /// <summary>
        /// The pane side a Step 9 glass row belongs to, read from the row
        /// description's "{side} pane · …" prefix.
        /// </summary>
        private static string PaneSideFrom(string? description)
        {
            if (string.IsNullOrWhiteSpace(description)) return "Outer";

            int idx = description.IndexOf(" pane", StringComparison.OrdinalIgnoreCase);
            string side = idx > 0 ? description[..idx].Trim() : "Outer";
            return side.Length == 0 ? "Outer" : side;
        }

        private static string GlassNameFromDescription(string description)
        {
            string text = description ?? string.Empty;
            int paneToken = text.IndexOf("pane", StringComparison.OrdinalIgnoreCase);
            int marker = text.IndexOf("mm", paneToken >= 0 ? paneToken : 0, StringComparison.OrdinalIgnoreCase);
            if (marker < 0) return string.Empty;

            int start = marker + 2;
            while (start < text.Length && char.IsWhiteSpace(text[start])) start++;

            int end = text.IndexOf('·', start);
            if (end < 0) end = text.Length;

            return text[start..end].Trim();
        }

        private static string SheetSourceKey(int specNumber, string sideLabel) =>
            $"{specNumber}|{(sideLabel ?? string.Empty).Trim()}";

        /// <summary>
        /// Maps a Step 9 pane side to the exact Step 6 wastage source label.
        /// Unknown sides (e.g. an extra "Pane N" of a 4+ pane hybrid) return
        /// null — they have no Step 6 wastage source.
        /// </summary>
        private static string? Step6SideLabelForPane(string paneSide)
        {
            string side = (paneSide ?? "").Trim();
            if (side.Length == 0) return null;

            if (side.IndexOf("Lam Inner", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Lam Inner (W2/H2)";
            if (side.IndexOf("Lam Outer", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Lam Outer (W2/H2)";
            if (side.IndexOf("Middle", StringComparison.OrdinalIgnoreCase) >= 0)
                return $"{side} (W2/H2)";
            if (side.IndexOf("Inner", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Inner (W2/H2)";
            if (side.IndexOf("Outer", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Outer (W1/H1)";
            return null;
        }

        /// <summary>
        /// The SHEET QTY the operator set in Step 6 for THIS pane — read
        /// even from a DISABLED wastage row (it still owns the setup
        /// quantity). Mirrors ResolveStep6WastageSheetQuantity. 0 = none.
        /// </summary>
        private int ResolveStep6SheetQuantity(int specNumber, string paneSide)
        {
            string? sideLabel = Step6SideLabelForPane(paneSide);
            if (sideLabel == null) return 0;

            var rows = _parentViewModel._otherChargesVm?.WastagesSection?.WastageRows;
            if (rows == null) return 0;

            var row = rows
                .Where(r => r.AvailableSources.Any(s =>
                    s.IsChecked
                    && s.SpecNumber == specNumber
                    && string.Equals(s.SideLabel, sideLabel, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(r => r.IsActive)
                .FirstOrDefault();

            return row?.SheetQuantity ?? 0;
        }

        /// <summary>
        /// The Step 6 wastage charge for THIS pane, with the source it was
        /// read from. An ACTIVE row's ResultAmount is split across its
        /// checked sources by SQM share — the same allocation Step 9's
        /// AppendWastageCostRows uses — and only this pane's share plus its
        /// source identity (side, colour, SQM) and the row's rate are
        /// returned, so the operator can see exactly which outer/inner pane
        /// was charged.
        /// </summary>
        private Step6ChargeInfo ResolveStep6Charge(int specNumber, string paneSide)
        {
            string? sideLabel = Step6SideLabelForPane(paneSide);
            if (sideLabel == null) return Step6ChargeInfo.None;

            var rows = _parentViewModel._otherChargesVm?.WastagesSection?.WastageRows;
            if (rows == null) return Step6ChargeInfo.None;

            foreach (var row in rows)
            {
                if (row == null) continue;

                var sources = row.AvailableSources
                    .Where(s => s != null && s.IsChecked && s.SqmValue > 0m)
                    .ToList();
                if (sources.Count == 0) continue;

                decimal totalSqm = row.TotalSqm;
                if (totalSqm <= 0m) continue;

                foreach (var src in sources)
                {
                    if (src.SpecNumber != specNumber) continue;
                    if (!string.Equals(src.SideLabel, sideLabel, StringComparison.OrdinalIgnoreCase))
                        continue;

                    decimal charge = row.IsActive && row.ResultAmount > 0m
                        ? MoneyRounding.Round(row.ResultAmount * (src.SqmValue / totalSqm))
                        : 0m;
                    return new Step6ChargeInfo(
                        src.DisplayLabel, src.SqmValue, row.SheetQuantity,
                        row.Rate, charge, row.OptimizationPercent);
                }
            }

            return Step6ChargeInfo.None;
        }

        /// <summary>
        /// Build family shown in the GLASS TYPE column: "SGU", "DGU",
        /// "LAM" or "DGU+LAM". Only the build family is shown — the outer/
        /// inner pane identity lives in the pane-specific cross-check, not
        /// in the glass type.
        /// </summary>
        private static string FamilyLabelFor(string specName)
        {
            bool dgu = PnLSpecParser.Spacer(specName).Success
                       || specName.IndexOf("DGU", StringComparison.OrdinalIgnoreCase) >= 0;
            bool lam = PnLSpecParser.Interlayer(specName).Success
                       || specName.IndexOf("LAM", StringComparison.OrdinalIgnoreCase) >= 0;

            if (dgu && lam) return "DGU+LAM";
            if (dgu) return "DGU";
            if (lam) return "LAM";
            return "SGU";
        }

        /// <summary>A Step 6 wastage charge read for one pane source.</summary>
        private sealed class Step6ChargeInfo
        {
            public static readonly Step6ChargeInfo None =
                new Step6ChargeInfo("", 0m, 0, 0m, 0m, 0m);

            public string SourceLabel { get; }
            public decimal SourceSqm { get; }
            public int SheetQuantity { get; }
            public decimal Rate { get; }
            public decimal Charge { get; }
            public decimal OptimizationPercent { get; }

            public Step6ChargeInfo(
                string sourceLabel, decimal sourceSqm, int sheetQuantity,
                decimal rate, decimal charge, decimal optimizationPercent)
            {
                SourceLabel = sourceLabel ?? "";
                SourceSqm = sourceSqm;
                SheetQuantity = sheetQuantity;
                Rate = rate;
                Charge = charge;
                OptimizationPercent = optimizationPercent;
            }
        }
    }

    /// <summary>
    /// One glass pane of the job: the buy / used / wastage (leftover)
    /// split, its Step 6 wastage charge readout, and its cross-check
    /// status.
    /// </summary>
    public class SheetProfitRow : ViewModelBase
    {
        private string _specName = "";
        public string SpecName
        {
            get => _specName;
            set
            {
                if (_specName == value) return;
                _specName = value ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(SpecPaneGlassDisplay));
            }
        }

        private int _specNumber;
        public int SpecNumber
        {
            get => _specNumber;
            set
            {
                if (_specNumber == value) return;
                _specNumber = value;
                OnPropertyChanged();
            }
        }

        /// <summary>Build family: "SGU", "DGU", "LAM" or "DGU+LAM".</summary>
        private string _glassTypeLabel = "";
        public string GlassTypeLabel
        {
            get => _glassTypeLabel;
            set
            {
                if (_glassTypeLabel == value) return;
                _glassTypeLabel = value ?? "";
                OnPropertyChanged();
            }
        }

        /// <summary>Pane side (Outer / Inner / Lam Outer / Lam Inner / Middle).</summary>
        private string _paneSide = "";
        public string PaneSide
        {
            get => _paneSide;
            set
            {
                if (_paneSide == value) return;
                _paneSide = value ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(PaneGlassDisplay));
                OnPropertyChanged(nameof(SpecPaneGlassDisplay));
            }
        }

        private string _glassName = "";
        /// <summary>Glass colour/brand for this pane, e.g. Low-E, Clear or Ramly.</summary>
        public string GlassName
        {
            get => _glassName;
            set
            {
                string next = value ?? "";
                if (_glassName == next) return;
                _glassName = next;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PaneGlassDisplay));
                OnPropertyChanged(nameof(SpecPaneGlassDisplay));
            }
        }

        public string PaneGlassDisplay => string.IsNullOrWhiteSpace(GlassName)
            ? PaneSide
            : $"{PaneSide} · {GlassName}";

        public string SpecPaneGlassDisplay
        {
            get
            {
                string pane = PaneGlassDisplay;
                if (string.IsNullOrWhiteSpace(SpecName)) return pane;
                if (string.IsNullOrWhiteSpace(pane)) return SpecName;
                return $"{SpecName} — {pane}";
            }
        }

        private string _sheetSize = "";
        public string SheetSize
        {
            get => _sheetSize;
            set
            {
                if (_sheetSize == value) return;
                _sheetSize = value ?? "";
                OnPropertyChanged();
            }
        }

        private decimal _sheetSqm;
        public decimal SheetSqm
        {
            get => _sheetSqm;
            set => SetRow(ref _sheetSqm, value, nameof(SheetSqmDisplay));
        }

        public string SheetSqmDisplay => $"{SheetSqm:0.####} SQM";

        private decimal _buySheets;
        /// <summary>Sheets bought (Step 9 quantity; Step 6 SHEET QTY when set).</summary>
        public decimal BuySheets
        {
            get => _buySheets;
            set => SetRow(ref _buySheets, value, nameof(BuySheetsDisplay));
        }

        public string BuySheetsDisplay => $"{BuySheets:0}";

        private decimal _usedSheets;
        /// <summary>Whole sheets required to cover the required area.</summary>
        public decimal UsedSheets
        {
            get => _usedSheets;
            set => SetRow(ref _usedSheets, value, nameof(UsedSheetsDisplay));
        }

        public string UsedSheetsDisplay => $"{UsedSheets:0}";

        private decimal _wastageSheets;
        /// <summary>Leftover = buy − used. Negative means a shortfall.</summary>
        public decimal WastageSheets
        {
            get => _wastageSheets;
            set => SetRow(ref _wastageSheets, value, nameof(WastageSheetsDisplay));
        }

        public string WastageSheetsDisplay => $"{WastageSheets:0}";

        private decimal _buySheetArea;
        public decimal BuySheetArea
        {
            get => _buySheetArea;
            set => SetRow(ref _buySheetArea, value, nameof(BuySheetAreaDisplay));
        }

        public string BuySheetAreaDisplay => $"{BuySheetArea:0.##} SQM";

        private decimal _usedArea;
        /// <summary>Required area including wastage (SQM).</summary>
        public decimal UsedArea
        {
            get => _usedArea;
            set => SetRow(ref _usedArea, value, nameof(UsedAreaDisplay));
        }

        public string UsedAreaDisplay => $"{UsedArea:0.##} SQM";

        private decimal _wastageArea;
        /// <summary>Leftover area = bought − required. Negative = shortfall.</summary>
        public decimal WastageArea
        {
            get => _wastageArea;
            set => SetRow(ref _wastageArea, value, nameof(WastageAreaDisplay));
        }

        public string WastageAreaDisplay => $"{WastageArea:0.##} SQM";

        private decimal _pricePerSqm;
        public decimal PricePerSqm
        {
            get => _pricePerSqm;
            set => SetRow(ref _pricePerSqm, value, nameof(PricePerSqmDisplay));
        }

        public string PricePerSqmDisplay => $"AED {PricePerSqm:N2}/SQM";

        // ── YIELD-INCLUSIVE PRICING (2026-09-11, owner directive) ──────
        // The GIVEN rate R prices one SQM of SHEET. When the yield
        // (utilization) is y, the used material effectively costs
        //     BuySheetPrice ÷ UsedArea  =  R ÷ y
        // e.g. yield 0.85 → +17.6% on the given rate: the wastage premium
        // is ALREADY embedded in the per-SQM sheet price. Surfacing it
        // changes NO totals — buy/used/wastage reconcile by construction;
        // these columns EXPLAIN the embedded premium.
        public decimal EffectivePricePerSqm => UsedArea > 0m
            ? MoneyRounding.Round(BuySheetPrice / UsedArea)
            : 0m;

        public string EffectivePricePerSqmDisplay => EffectivePricePerSqm > 0m
            ? $"AED {EffectivePricePerSqm:N2}/SQM"
            : "—";

        public decimal YieldPremiumPercent => PricePerSqm > 0m && EffectivePricePerSqm > PricePerSqm
            ? Math.Round((EffectivePricePerSqm / PricePerSqm - 1m) * 100m, 1)
            : 0m;

        public string YieldPremiumDisplay => YieldPremiumPercent > 0m
            ? $"+{YieldPremiumPercent:0.#}% yield"
            : "—";

        // ── WASTAGE RECOVERY SPLIT (2026-09-11, owner formula) ─────────
        // The Step 6 CHARGE is the money recovered against wastage:
        //     RECOVERED = min(Step6Charge, WastageValue)
        //     NET LOSS  = WastageValue − RECOVERED      (e.g. 1,586.66 −
        //                162.22 = 1,424.44 absorbed after billing)
        // recovered + net = WastageValue, exact at row AND total level.
        public decimal WastageValueRecovered
            => Math.Min(Step6Charge, WastageValue);

        public string WastageValueRecoveredDisplay => $"AED {WastageValueRecovered:N2}";

        public decimal WastageValueNetLoss => WastageValue - WastageValueRecovered;

        public string WastageValueNetLossDisplay => $"AED {WastageValueNetLoss:N2}";

        private decimal _buySheetPrice;
        /// <summary>Total sheet price = the Step 9 glass row amount.</summary>
        public decimal BuySheetPrice
        {
            get => _buySheetPrice;
            set => SetRow(ref _buySheetPrice, value, nameof(BuySheetPriceDisplay));
        }

        public string BuySheetPriceDisplay => $"AED {BuySheetPrice:N2}";

        private decimal _usedSheetPrice;
        /// <summary>Used sheet price = required area × rate.</summary>
        public decimal UsedSheetPrice
        {
            get => _usedSheetPrice;
            set => SetRow(ref _usedSheetPrice, value, nameof(UsedSheetPriceDisplay));
        }

        public string UsedSheetPriceDisplay => $"AED {UsedSheetPrice:N2}";

        private decimal _wastageValue;
        /// <summary>Material value of the leftover = buy − used.</summary>
        public decimal WastageValue
        {
            get => _wastageValue;
            set => SetRow(ref _wastageValue, value, nameof(WastageValueDisplay));
        }

        public string WastageValueDisplay => $"AED {WastageValue:N2}";

        private decimal _step6Charge;
        /// <summary>The Step 6 wastage charge for this pane.</summary>
        public decimal Step6Charge
        {
            get => _step6Charge;
            set => SetRow(ref _step6Charge, value, nameof(Step6ChargeDisplay));
        }

        public string Step6ChargeDisplay => $"AED {Step6Charge:N2}";

        private string _step6SourceLabel = "";
        /// <summary>The Step 6 source this charge was read from (pane + colour + W1/H1).</summary>
        public string Step6SourceLabel
        {
            get => _step6SourceLabel;
            set
            {
                if (_step6SourceLabel == value) return;
                _step6SourceLabel = value ?? "";
                OnPropertyChanged();
            }
        }

        private decimal _step6SourceSqm;
        /// <summary>The source SQM recorded in Step 6 for this pane.</summary>
        public decimal Step6SourceSqm
        {
            get => _step6SourceSqm;
            set => SetRow(ref _step6SourceSqm, value, nameof(Step6SourceSqmDisplay));
        }

        public string Step6SourceSqmDisplay => $"{Step6SourceSqm:0.###} SQM";

        private int _step6SheetQuantity;
        /// <summary>The SHEET QTY entered in Step 6 for this pane (0 = none).</summary>
        public int Step6SheetQuantity
        {
            get => _step6SheetQuantity;
            set => SetRow(ref _step6SheetQuantity, value, nameof(Step6SheetQuantityDisplay));
        }

        public string Step6SheetQuantityDisplay =>
            Step6SheetQuantity > 0 ? $"{Step6SheetQuantity:0}" : "—";

        private decimal _step6Rate;
        /// <summary>The Step 6 wastage row rate (AED/SQM).</summary>
        public decimal Step6Rate
        {
            get => _step6Rate;
            set => SetRow(ref _step6Rate, value, nameof(Step6RateDisplay));
        }

        public string Step6RateDisplay =>
            Step6Rate > 0m ? $"AED {Step6Rate:0.####}/SQM" : "—";

        private decimal _usageEfficiencyPercent;
        public decimal UsageEfficiencyPercent
        {
            get => _usageEfficiencyPercent;
            set => SetRow(ref _usageEfficiencyPercent, value, nameof(UsageEfficiencyDisplay));
        }

        public string UsageEfficiencyDisplay => $"{UsageEfficiencyPercent:0.##}%";

        private string _diagnosticMessage = "";
        public string DiagnosticMessage
        {
            get => _diagnosticMessage;
            set
            {
                if (_diagnosticMessage == value) return;
                _diagnosticMessage = value ?? "";
                OnPropertyChanged();
            }
        }

        /// <summary>True on Step 6 setup notice rows (incomplete setup —
        /// the charge is explained, not posted). 2026-09-11 owner fix.</summary>
        public bool IsSetupNotice { get; set; }

        /// <summary>Bought fewer sheets than required — Step 6 SHEET QTY too low.</summary>
        public bool HasShortfall => WastageSheets < 0m;

        /// <summary>Step 6 charges wastage but this pane has no leftover.</summary>
        public bool HasChargeWithoutWastage => Step6Charge > 0m && WastageArea <= 0m && !HasShortfall;

        /// <summary>This pane has leftover but Step 6 charges nothing.</summary>
        public bool HasWastageWithoutCharge => WastageArea > 0m && Step6Charge <= 0m;

        /// <summary>No cross-check issue for this pane.</summary>
        public bool IsHealthy => !HasShortfall && !HasChargeWithoutWastage && !HasWastageWithoutCharge;

        /// <summary>Badge shown in the STATUS column.</summary>
        public string StatusLabel
        {
            get
            {
                if (IsSetupNotice) return "STEP 6";
                if (HasShortfall) return "SHORTFALL";
                if (HasChargeWithoutWastage) return "REVIEW";
                if (HasWastageWithoutCharge) return "NO CHARGE";
                return "OK";
            }
        }

        /// <summary>Used price as a share of the buy price.</summary>
        public decimal UsedPriceSharePercent => BuySheetPrice > 0m
            ? Math.Round(UsedSheetPrice / BuySheetPrice * 100m, 1)
            : 0m;

        public string UsedPriceShareDisplay => $"{UsedPriceSharePercent:0.#}%";

        /// <summary>Wastage material value as a share of the buy price.</summary>
        public decimal WastageValueSharePercent => BuySheetPrice > 0m
            ? Math.Round(WastageValue / BuySheetPrice * 100m, 1)
            : 0m;

        public string WastageValueShareDisplay => $"{WastageValueSharePercent:0.#}%";

        /// <summary>Step 6 wastage charge as a share of the buy price.</summary>
        public decimal Step6ChargeSharePercent => BuySheetPrice > 0m
            ? Math.Round(Step6Charge / BuySheetPrice * 100m, 1)
            : 0m;

        public string Step6ChargeShareDisplay => $"{Step6ChargeSharePercent:0.#}%";

        private bool _isSelected;
        /// <summary>True while this row's full-screen detail is open.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        private void SetRow(ref decimal field, decimal value, string displayName)
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(displayName);
        }

        private void SetRow(ref int field, int value, string displayName)
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(displayName);
        }
    }
}
