// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step9ProfitLoss/ProfitLossStepViewModel.cs
// PROJECT: ProGlassAutomation
// MODULE: Wizard Step 9 — Profit & Loss (PHASE B)
//
// WHAT IT DOES
//   Answers the question the estimator cannot: "after we buy the glass and
//   run production, what is actually left on this invoice?"
//
//   For every Step 5 spec group it reverse-engineers the build from the
//   generated spec name, prices the raw sheets against the Sheet Store,
//   adds the production charges, and compares the total against the
//   quoted grand total (ex-VAT).
//
// SPEC NAME GRAMMAR (produced by Step 4, parsed back here)
//   outer   : "6mm HD Grey FT Glass"
//             ^(?<thk>\d+)\s*mm\s+(?<color>.+?)\s+(FT|Annealed|
//              Heat Strengthened|Heat Soaked)\s+Glass
//   spacer  : "+ 12mm ASP"           →  \+\s*(?<mm>\d+)\s*mm\s+ASP
//   inner   : same outer pattern, matched AFTER the spacer
//
// COST MODEL
//   cutSqm       = Σ (W1·H1/1e6  [+ W2·H2/1e6 when W2/H2 > 0]) · Qty
//   perimeterLm  = Σ (2·(W1+H1)/1000) · Qty
//   sheets       = ceil( cutSqm · (1 + Wastage%/100) / sheetSqm ), min 1
//   glass amount = round(sheetSqm · sheets · purchasePricePerSqm, 2)
//                  for multi-pane builds costed on full stock-sheet use
//   SGU glass    = round((cutSqm + wastage) · sheetPricePerSqm, 2)
//                  business rule: SGU glass is priced by required SQM,
//                  while cutting and tempering remain separate rows
//   DGU spacer   = round(cutSqm · DguVM.SpacerPrice, 2)
//                  ASP is quoted PER SQM in Step 4 (37.5+((w-6)/2)*2.5,
//                  so 45.00 at 12mm) and that price already includes
//                  cutting, tempering and production for DGU.
//   SGU prod.    = round(cutSqm · CuttingCostPerSqm, 2)
//                  + round(cutSqm · TemperingCostPerSqm, 2)
//
//   Unpriced panes are still SHOWN (so a DGU never collapses to only
//   the air spacer) but their amount stays 0 and is excluded from profit.
//   Step 4 pane rates are the fallback when Sheet Store has no match.
//
// TOTALS
//   NetExVat = Step 7 subtotal-before-VAT when bound; legacy fallback strips 5% VAT
//   Profit   = NetExVat − TotalGlassCost − TotalProductionCost
//   Margin   = round(Profit / NetExVat · 100, 1)
//
// SAFETY: Bind() may be called before every dependency exists and
// RecalculateAsync() never throws — the wizard must stay usable even with
// an empty Sheet Store or a half-filled Step 5 grid.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Commands;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Sharing;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.ProformaInvoice;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.DguLamParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.DguParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.LamParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.SguParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step6OtherCharges;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using System.Text.RegularExpressions; // Match still used while production rows read spacer/pvb groups

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss
{
    // PARTIAL CLASS (senior audit — future safe extraction seam)
    // ─────────────────────────────────────────────────────────
    // Declared partial so a follow-up PR can move regions into
    // sibling files (ProfitLossStepViewModel.Cost.cs,
    // ProfitLossStepViewModel.Rollup.cs, ProfitLossStepViewModel
    // .Serialization.cs) without disturbing this file's audit
    // history. See notes on SpecGroup for the same rationale.
    public partial class ProfitLossStepViewModel : ViewModelBase, IDisposable
    {
        public string Title { get; } = "PROFIT & LOSS";

        public string Subtitle { get; } =
            "Real cost of this invoice — glass from Sheet Store, production from Step 4 rates.";

        // ── Fallback stock sheet when the store has no size on file ──────
        // 3210 × 2250 is the standard jumbo stock sheet actually used here.
        // The old 2440 × 1830 default understated sheet area by ~38%
        // (4.4652 vs 7.2225 SQM), so any row that fell through to the
        // fallback reported a materially wrong cost -- and therefore a
        // wrong profit figure -- while looking perfectly plausible.
        private const int DefaultSheetWidthMm = 3210;
        private const int DefaultSheetHeightMm = 2250;

        /// <summary>
        /// Legacy fallback only. New bindings pass Step 7 SubtotalBeforeVat directly
        /// so zero-rated/custom-VAT invoices are not forced through a 5% divisor.
        ///
        /// Aliased to <c>VatCalculator.VAT_MULTIPLIER</c> rather than declared
        /// as its own 1.05m, so a change to the UAE rate moves this too.
        /// </summary>
        private const decimal VatDivisor =
            ProGlassAutomation.Services.Reporting.VatCalculator.VAT_MULTIPLIER;

        // ── Bound dependencies (assigned by the shell via Bind) ──────────
        private DataGridViewModel? _dataGrid;
        private DguParametersViewModel? _dguVm;
        private SguParametersViewModel? _sguVm;
        private LamParametersViewModel? _lamVm;   // PHASE J
        private DguLamParametersViewModel? _dguLamVm;   // DGU+LAM hybrid
        internal OtherChargesViewModel? _otherChargesVm;
        private Func<decimal>? _grossProvider;
        private Func<decimal>? _netRevenueProvider;
        private INotifyPropertyChanged? _revenueSource;
        private Func<IReadOnlyList<Step4PricingSnapshot>>? _step4SnapshotProvider;
        private Func<ProGlassAutomation.Services.Reporting.ReportDocumentContext>? _reportDocumentContextProvider;

        // Spec-name grammar lives in PnLSpecParser — one definition for
        // the view-model and the tests. Do not re-declare those regexes.

        public ProfitLossStepViewModel()
        {
            RecalculateCommand = new RelayCommand(_ => AsyncUtility.SafeFireAndForget(FireRecalculateAsync, "ProfitLoss Recalc"));
            SelectTargetSpecCommand = new RelayCommand(_ => SetSelectedTargetSpec(_));
            GoToStep6Command = new RelayCommand(_ => GoToStep6());
            BackToComparisonCommand = new RelayCommand(_ => BackToComparison());
            ExportOverviewCsvCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(() => ExportOverviewAsync(ExportFormat.Csv), "Step9 Overview CSV"));
            ExportOverviewExcelCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(() => ExportOverviewAsync(ExportFormat.Excel), "Step9 Overview Excel"));
            ExportOverviewPdfCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(() => ExportOverviewAsync(ExportFormat.Pdf), "Step9 Overview PDF"));
            ExportTargetCsvCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(() => ExportTargetAsync(ExportFormat.Csv), "Step9 Target CSV"));
            ExportTargetExcelCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(() => ExportTargetAsync(ExportFormat.Excel), "Step9 Target Excel"));
            ExportTargetPdfCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(() => ExportTargetAsync(ExportFormat.Pdf), "Step9 Target PDF"));
            ExportPnlCsvCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(() => ExportPnlAsync(ExportFormat.Csv), "Step9 PnL CSV"));
            ExportPnlExcelCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(() => ExportPnlAsync(ExportFormat.Excel), "Step9 PnL Excel"));
            ExportPnlPdfCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(() => ExportPnlAsync(ExportFormat.Pdf), "Step9 PnL PDF"));
            ShareOverviewCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(ShareOverviewAsync, "Step9 Overview share"));
            ShareTargetCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(ShareTargetAsync, "Step9 Target share"));
            SharePnlCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(SharePnlAsync, "Step9 PnL share"));

            Rows.CollectionChanged += OnRowsCollectionChanged;

            // Live default: an unpinned invoice follows the Settings → Step 9
            // expected profit margin whenever it changes.
            Services.Features.Step9PricingDefaults.MarginDefaultChanged += OnExpectedProfitMarginDefaultChanged;

            // Initialize analysis ViewModels
            ServiceProfitability = new ServiceProfitabilityViewModel(this);
            SheetProfitability = new SheetProfitabilityViewModel(this);
            GlassTypeComparison = new GlassTypeComparisonViewModel(this);
        }

        private void OnRowsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasRows));
            RaiseStep9HealthNotifications();
        }

        // ── IDisposable ──────────────────────────────────────────────────
        // The wizard shell disposes this VM; every event handler registered
        // on Step 4 / Step 5 / Step 6 sources is detached here so a
        // short-lived wizard instance cannot leak.
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { Services.Features.Step9PricingDefaults.MarginDefaultChanged -= OnExpectedProfitMarginDefaultChanged; }
            catch {
                ProGlassAutomation.Services.Logging.AppLogService.Debug("Dispose suppressed");
            }

            try { _subscriptions.Dispose(); }
            catch {
                ProGlassAutomation.Services.Logging.AppLogService.Debug("Dispose suppressed");
            }

            foreach (var token in _groupSubscriptions.Values.SelectMany(v => v))
            {
                try { token.Dispose(); }
                catch {
                ProGlassAutomation.Services.Logging.AppLogService.Debug("Dispose suppressed");
            }
            }

            _groupSubscriptions.Clear();
        }

        // ══════════════════════════════════════════════════════════════
        //  ANALYSIS VIEWS (Phase 1-3 Implementation)
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Service profitability analysis (Phase 1).
        /// Breaks down profit by service type (glass, spacer, cutting, tempering, etc.)
        /// </summary>
        public ServiceProfitabilityViewModel ServiceProfitability { get; }

        /// <summary>
        /// Sheet profitability analysis (Phase 2).
        /// Tracks sheet usage, offcuts, and real costs.
        /// </summary>
        public SheetProfitabilityViewModel SheetProfitability { get; }

        /// <summary>
        /// Glass type comparison analysis (Phase 3).
        /// Compares profitability across DGU, SGU, LAM, DGU+LAM.
        /// </summary>
        public GlassTypeComparisonViewModel GlassTypeComparison { get; }

        // ══════════════════════════════════════════════════════════════
        //  OVERVIEW EXPORT (CSV / Excel / PDF) — main section report
        // ══════════════════════════════════════════════════════════════

        public ICommand ExportOverviewCsvCommand { get; }
        public ICommand ExportOverviewExcelCommand { get; }
        public ICommand ExportOverviewPdfCommand { get; }
        public ICommand ShareOverviewCommand { get; }

        // ══════════════════════════════════════════════════════════════
        //  TARGET-COMPARISON EXPORT (CSV / Excel / PDF)
        // ══════════════════════════════════════════════════════════════

        public ICommand ExportTargetCsvCommand { get; }
        public ICommand ExportTargetExcelCommand { get; }
        public ICommand ExportTargetPdfCommand { get; }
        public ICommand ShareTargetCommand { get; }

        private bool _isTargetExporting;
        /// <summary>True while a target-comparison export runs.</summary>
        public bool IsTargetExporting
        {
            get => _isTargetExporting;
            private set
            {
                if (_isTargetExporting == value) return;
                _isTargetExporting = value;
                OnPropertyChanged();
            }
        }

        // ── Main P&L cost breakdown export (the tab-1 line-item table) ──
        public ICommand ExportPnlCsvCommand { get; }
        public ICommand ExportPnlExcelCommand { get; }
        public ICommand ExportPnlPdfCommand { get; }
        public ICommand SharePnlCommand { get; }

        private bool _isPnlExporting;
        /// <summary>True while a cost-breakdown export runs.</summary>
        public bool IsPnlExporting
        {
            get => _isPnlExporting;
            private set
            {
                if (_isPnlExporting == value) return;
                _isPnlExporting = value;
                OnPropertyChanged();
            }
        }

        private async Task ExportOverviewAsync(ExportFormat format)
        {
            if (_isPnlExporting) return;
            _isPnlExporting = true;
            try
            {
                var result = await ExportOverviewCoreAsync(format, null);
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
                _isPnlExporting = false;
            }
        }

        /// <summary>
        /// Single overview builder behind export (request null → save
        /// dialog) and share (staged temp request).
        /// </summary>
        private async Task<ExportResult?> ExportOverviewCoreAsync(
            ExportFormat format, ExportRequest? request)
        {
            var rows = Step9ReportExport.BuildOverviewRows(this);
            if (rows.Count == 0) return null;

            return await Step9ReportExport.RunAsync(
                format, rows, "Step 9 Executive Cost & Price Decision Summary",
                Step9ReportExport.OverviewHeaders,
                Step9ReportExport.OverviewMap,
                Step9ReportExport.OverviewMapExcel,
                Step9ReportExport.ReportCodeOverview,
                GetReportDocumentContext(),
                request);
        }

        // Sharing Center — the SAME overview staged once into the share
        // temp folder (Report rule's default format), then the composer.
        private async Task ShareOverviewAsync()
        {
            if (_isPnlExporting) return;
            _isPnlExporting = true;
            try
            {
                await ShareStep9ReportAsync(
                    "Step 9 Executive Cost & Price Decision Summary",
                    Step9ReportExport.ReportCodeOverview,
                    (format, request) => ExportOverviewCoreAsync(format, request));
            }
            catch (Exception ex)
            {
                App.Dialogs.Error($"Share failed: {ExceptionMessages.Deep(ex)}", "Share");
            }
            finally
            {
                _isPnlExporting = false;
            }
        }

        /// <summary>
        /// Shared share flow for the three Step 9 parent reports: one
        /// context shape, one staged request, one composer handoff. The
        /// caller supplies only its title, report code and core builder.
        /// </summary>
        private async Task ShareStep9ReportAsync(
            string reportTitle,
            string reportCode,
            Func<ExportFormat, ExportRequest, Task<ExportResult?>> runCoreAsync)
        {
            var identity = GetReportDocumentContext();
            var context = ShareContextFactory.FromListReport(
                "Report", reportTitle, "ProformaInvoice",
                identity.CustomerName, identity.CompanyName);
            context.DocumentNumber = identity.ProformaId;

            ExportFormat format = ShareLauncher.RuleFormat("Report");
            await ShareLauncher.GenerateAndShareAsync(
                context,
                async folder =>
                {
                    var result = await runCoreAsync(
                        format,
                        Step9ReportExport.StagedRequest(
                            format, reportCode, identity, folder));
                    if (result == null || !result.Success)
                        App.Dialogs.Warning(
                            result?.ErrorMessage ?? "Export failed.", "Share");
                    return result?.SavedPath;
                },
                App.Dialogs);
        }

        private async Task ExportPnlAsync(ExportFormat format)
        {
            if (_isPnlExporting || Rows.Count == 0) return;
            _isPnlExporting = true;
            try
            {
                var result = await ExportPnlCoreAsync(format, null);
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
                _isPnlExporting = false;
            }
        }

        /// <summary>
        /// Single cost-ledger builder behind export (request null → save
        /// dialog) and share (staged temp request).
        /// </summary>
        private async Task<ExportResult?> ExportPnlCoreAsync(
            ExportFormat format, ExportRequest? request)
        {
            var rows = Rows.ToList();
            var spacerRows = Rows
                .Where(r => string.Equals(r.Kind, "Spacer", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (spacerRows.Count > 0)
            {
                var spacerQty = spacerRows.Sum(r => r.Quantity);
                var spacerAmount = spacerRows.Sum(r => r.Amount);
                rows.Add(new ProfitLossRow
                {
                    Kind = "SPACER TOTAL",
                    Description = $"Spacer / ASP production service total ({spacerRows.Count} line(s))",
                    Quantity = spacerQty,
                    UnitLabel = "SQM",
                    PricePerSqm = spacerQty > 0m ? Math.Round(spacerAmount / spacerQty, 2) : 0m,
                    Amount = spacerAmount
                });
            }

            var productionRows = Rows
                .Where(r => string.Equals(r.Kind, "Production", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (productionRows.Count > 0)
            {
                rows.Add(new ProfitLossRow
                {
                    Kind = "PRODUCTION TOTAL",
                    Description = $"Other production service total ({productionRows.Count} line(s))",
                    Amount = productionRows.Sum(r => r.Amount)
                });
            }

            rows.Add(new ProfitLossRow
            {
                Kind = "TOTAL",
                Description = $"All costed lines ({Rows.Count})",
                Amount = Rows.Sum(r => r.Amount)
            });

            return await Step9ReportExport.RunAsync(
                format, rows, "Material and Production Cost Ledger",
                Step9ReportExport.PnlHeaders,
                Step9ReportExport.PnlMap,
                Step9ReportExport.PnlMapExcel,
                Step9ReportExport.ReportCodeCostLedger,
                GetReportDocumentContext(),
                request);
        }

        // Sharing Center — the SAME cost ledger staged once into the
        // share temp folder (Report rule's default format), then the
        // composer.
        private async Task SharePnlAsync()
        {
            if (_isPnlExporting || Rows.Count == 0) return;
            _isPnlExporting = true;
            try
            {
                await ShareStep9ReportAsync(
                    "Material and Production Cost Ledger",
                    Step9ReportExport.ReportCodeCostLedger,
                    (format, request) => ExportPnlCoreAsync(format, request));
            }
            catch (Exception ex)
            {
                App.Dialogs.Error($"Share failed: {ExceptionMessages.Deep(ex)}", "Share");
            }
            finally
            {
                _isPnlExporting = false;
            }
        }

        private async Task ExportTargetAsync(ExportFormat format)
        {
            if (_isTargetExporting || TargetSpecsView.Count == 0) return;
            _isTargetExporting = true;
            try
            {
                var result = await ExportTargetCoreAsync(format, null);
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
                _isTargetExporting = false;
            }
        }

        /// <summary>
        /// Single price-decision builder behind export (request null →
        /// save dialog) and share (staged temp request).
        /// </summary>
        private async Task<ExportResult?> ExportTargetCoreAsync(
            ExportFormat format, ExportRequest? request)
        {
            var rows = TargetSpecsView.ToList();
            const string reportTitle = "Step 9 Specification Price Decision Report";
            var reportIdentity = GetReportDocumentContext();

            // Nine audit columns are practical in CSV/Excel, but make a
            // dense PDF table that truncates exactly the explanation an
            // approver needs. PDF therefore uses the same eight discrete
            // price-decision lines as the Overview: one complete card per
            // specification, with the same canonical values and formula.
            return format == ExportFormat.Pdf
                ? await Step9ReportExport.RunAsync(
                    format,
                    rows.SelectMany(Step9ReportExport.BuildPriceDecisionRows).ToList(),
                    reportTitle,
                    Step9ReportExport.OverviewHeaders,
                    Step9ReportExport.OverviewMap,
                    Step9ReportExport.OverviewMapExcel,
                    Step9ReportExport.ReportCodePriceDecision,
                    reportIdentity,
                    request)
                : await Step9ReportExport.RunAsync(
                    format, rows, reportTitle,
                    Step9ReportExport.TargetHeaders,
                    Step9ReportExport.TargetMap,
                    Step9ReportExport.TargetMapExcel,
                    Step9ReportExport.ReportCodePriceDecision,
                    reportIdentity,
                    request);
        }

        // Sharing Center — the SAME price-decision report staged once
        // into the share temp folder (Report rule's default format),
        // then the composer.
        private async Task ShareTargetAsync()
        {
            if (_isTargetExporting || TargetSpecsView.Count == 0) return;
            _isTargetExporting = true;
            try
            {
                await ShareStep9ReportAsync(
                    "Step 9 Specification Price Decision Report",
                    Step9ReportExport.ReportCodePriceDecision,
                    (format, request) => ExportTargetCoreAsync(format, request));
            }
            catch (Exception ex)
            {
                App.Dialogs.Error($"Share failed: {ExceptionMessages.Deep(ex)}", "Share");
            }
            finally
            {
                _isTargetExporting = false;
            }
        }

    }
}
