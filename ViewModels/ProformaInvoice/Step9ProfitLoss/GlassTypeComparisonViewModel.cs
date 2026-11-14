// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step9ProfitLoss/GlassTypeComparisonViewModel.cs
// PROJECT: ProGlassAutomation
// MODULE: Glass Type Comparison Analysis
//
// WHAT IT IS
//   Separate view for comparing profitability across glass types.
//   Shows:
//   - DGU (Double Glazed Unit) profitability
//   - SGU (Single Glazed Unit) profitability
//   - LAM (Laminated) profitability
//   - DGU+LAM (Hybrid) profitability
//   - Recommendations based on profitability
//
// WHY SEPARATE
//   Helps make strategic decisions about which glass types to promote
//   based on actual profitability data.
//
// FORMULAS
//   Total Revenue = Sum of all charges for this glass type
//   Total Cost = Sum of all costs for this glass type
//   Total Profit = Total Revenue - Total Cost
//   Margin % = (Total Profit / Total Revenue) × 100
//   Average Margin = Average of all job margins for this glass type
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Commands;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid;
using ProGlassAutomation.Services.ProformaInvoice.TargetPricing;
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
    /// Glass type comparison for strategic analysis.
    /// </summary>
    public class GlassTypeComparisonViewModel : ViewModelBase
    {
        private readonly ProfitLossStepViewModel _parentViewModel;

        public GlassTypeComparisonViewModel(ProfitLossStepViewModel parentViewModel)
        {
            _parentViewModel = parentViewModel ?? throw new ArgumentNullException(nameof(parentViewModel));
            
            GlassTypeRows = new ObservableCollection<GlassTypeComparisonRow>();
            RefreshCommand = new RelayCommand(_ => RefreshAnalysis());
            ExportCsvCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(() => ExportAsync(ExportFormat.Csv), "Step9 GlassType CSV"));
            ExportExcelCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(() => ExportAsync(ExportFormat.Excel), "Step9 GlassType Excel"));
            ExportPdfCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(() => ExportAsync(ExportFormat.Pdf), "Step9 GlassType PDF"));
            ShareCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(ShareAsync, "Step9 GlassType share"));
            
            // Initial load
            RefreshAnalysis();
        }

        // ══════════════════════════════════════════════════════════════
        //  PROPERTIES
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Glass type comparison rows.
        /// </summary>
        public ObservableCollection<GlassTypeComparisonRow> GlassTypeRows { get; }

        private string _bestPerformingGlassType = "";
        /// <summary>Best performing glass type.</summary>
        public string BestPerformingGlassType
        {
            get => _bestPerformingGlassType;
            private set
            {
                if (_bestPerformingGlassType == value) return;
                _bestPerformingGlassType = value ?? "";
                OnPropertyChanged();
            }
        }

        private string _worstPerformingGlassType = "";
        /// <summary>Worst performing glass type.</summary>
        public string WorstPerformingGlassType
        {
            get => _worstPerformingGlassType;
            private set
            {
                if (_worstPerformingGlassType == value) return;
                _worstPerformingGlassType = value ?? "";
                OnPropertyChanged();
            }
        }

        private string _recommendation = "";
        /// <summary>Strategic recommendation.</summary>
        public string Recommendation
        {
            get => _recommendation;
            private set
            {
                if (_recommendation == value) return;
                _recommendation = value ?? "";
                OnPropertyChanged();
            }
        }

        private decimal _totalRevenue;
        /// <summary>Total revenue across all glass types.</summary>
        public decimal TotalRevenue
        {
            get => _totalRevenue;
            private set
            {
                if (_totalRevenue == value) return;
                _totalRevenue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalRevenueDisplay));
            }
        }

        public string TotalRevenueDisplay => $"AED {TotalRevenue:N2}";

        private decimal _totalCost;
        /// <summary>Total cost across all glass types.</summary>
        public decimal TotalCost
        {
            get => _totalCost;
            private set
            {
                if (_totalCost == value) return;
                _totalCost = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalCostDisplay));
            }
        }

        public string TotalCostDisplay => $"AED {TotalCost:N2}";

        private decimal _totalProfit;
        /// <summary>Total profit across all glass types.</summary>
        public decimal TotalProfit
        {
            get => _totalProfit;
            private set
            {
                if (_totalProfit == value) return;
                _totalProfit = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalProfitDisplay));
            }
        }

        public string TotalProfitDisplay => $"AED {TotalProfit:N2}";

        private decimal _overallMarginPercent;
        /// <summary>Overall margin percentage.</summary>
        public decimal OverallMarginPercent
        {
            get => _overallMarginPercent;
            private set
            {
                if (_overallMarginPercent == value) return;
                _overallMarginPercent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OverallMarginDisplay));
            }
        }

        public string OverallMarginDisplay => $"{OverallMarginPercent:0.#}%";

        private string _statusMessage = "Press REFRESH to analyze glass type profitability.";
        /// <summary>Status message.</summary>
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
        /// <summary>True while refreshing.</summary>
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

        /// <summary>Total analysed jobs (sum of the per-type job counts).</summary>
        public int TotalJobCount => GlassTypeRows.Sum(r => r.JobCount);

        /// <summary>Total analysed area across glass types (SQM).</summary>
        public decimal TotalArea => GlassTypeRows.Sum(r => r.TotalArea);

        public string TotalAreaDisplay => $"{TotalArea:0.##} SQM";
        public string TotalJobCountDisplay => $"{TotalJobCount}";

        private async Task ExportAsync(ExportFormat format)
        {
            if (IsExporting || GlassTypeRows.Count == 0) return;
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
            var rows = GlassTypeRows.ToList();
            rows.Add(BuildTotalRow());

            return await Step9ReportExport.RunAsync(
                format, rows, "Spec Build Strategy Report",
                Step9ReportExport.GlassTypeHeaders,
                Step9ReportExport.GlassTypeMap,
                Step9ReportExport.GlassTypeMapExcel,
                Step9ReportExport.ReportCodeSpecStrategy,
                _parentViewModel.GetReportDocumentContext(),
                request);
        }

        // Sharing Center — the SAME analysis staged once into the share
        // temp folder (Report rule's default format), then the composer.
        // An internal cost analysis, so it shares under the Report
        // rule/template — never the PI invoice wording.
        private async Task ShareAsync()
        {
            if (IsExporting || GlassTypeRows.Count == 0) return;
            IsExporting = true;
            try
            {
                var identity = _parentViewModel.GetReportDocumentContext();
                var context = ShareContextFactory.FromListReport(
                    "Report", "Spec Build Strategy Report", "ProformaInvoice",
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
                                Step9ReportExport.ReportCodeSpecStrategy,
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

        /// <summary>
        /// Synthetic TOTAL row appended to the export so the column-aligned
        /// totals travel with the file. Margin is recomputed from the sums,
        /// never summed across rows.
        /// </summary>
        private GlassTypeComparisonRow BuildTotalRow()
        {
            decimal revenue = GlassTypeRows.Sum(r => r.TotalRevenue);
            decimal cost = GlassTypeRows.Sum(r => r.TotalCost);
            decimal profit = GlassTypeRows.Sum(r => r.TotalProfit);
            decimal margin = revenue > 0m ? Math.Round(profit / revenue * 100m, 1) : 0m;

            return new GlassTypeComparisonRow
            {
                GlassType = "TOTAL",
                JobCount = GlassTypeRows.Sum(r => r.JobCount),
                TotalArea = GlassTypeRows.Sum(r => r.TotalArea),
                TotalRevenue = revenue,
                TotalCost = cost,
                TotalProfit = profit,
                MarginPercent = margin,
                IsProfitable = profit >= 0m
            };
        }

        // ══════════════════════════════════════════════════════════════
        //  METHODS
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Refresh the glass type comparison analysis.
        /// </summary>
        public void RefreshAnalysis()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                GlassTypeRows.Clear();

                var groups = _parentViewModel.SpecGroupsView;
                if (groups == null || groups.Count == 0)
                {
                    ResetSummary();
                    StatusMessage = "No specifications found. Please add specifications in Step 5.";
                    return;
                }

                // ── REVENUE BASIS (senior-review fix) ────────────────────
                // Revenue was previously FAKE: every spec was charged cost ×
                // 1.15, so every glass type always showed ~13% margin no
                // matter what the customer was actually quoted. Now, when the
                // target model has run (Step 9 recalculated), each spec is
                // priced at its REAL Step 5 given value and classified from
                // the authoritative BuildType — never guessed from text and
                // never invented when the spec has no costing evidence.
                //
                // All-or-nothing basis: if the target result exists we use
                // actuals for every covered spec and SKIP (with a count) the
                // few specs the target run did not cover, rather than mixing
                // real and estimated revenue in one column. Only when no
                // target data exists at all does the legacy cost × 1.15
                // estimate remain, explicitly flagged in the status line.
                var targets = _parentViewModel.JobTargetResult?.Specifications;
                bool hasTargetModel = targets != null && targets.Count > 0;
                var targetBySpec = hasTargetModel
                    ? targets!.Where(t => t.SpecNumber > 0)
                              .GroupBy(t => t.SpecNumber)
                              .ToDictionary(g => g.Key, g => g.First())
                    : new Dictionary<int, SpecTargetPriceResult>();

                var buckets = new Dictionary<string,
                    (decimal Revenue, decimal Cost, decimal Area, int SpecCount)>();

                int skippedUncosted = 0;
                int skippedNotCovered = 0;

                foreach (var specGroup in groups)
                {
                    var specCost = specGroup.TotalCost;
                    if (specCost <= 0m)
                    {
                        skippedUncosted++; // no Step 9 costing → never invent a 0% row
                        continue;
                    }

                    string bucketKey;
                    decimal revenue;

                    if (hasTargetModel)
                    {
                        if (!targetBySpec.TryGetValue(specGroup.SpecNumber, out var target)
                            || target == null
                            || target.GivenValue <= 0m)
                        {
                            skippedNotCovered++;
                            continue;
                        }

                        bucketKey = BuildTypeKey(
                            target.BuildType, target.GlassTypeCode, specGroup.SpecName);
                        revenue = target.GivenValue;
                    }
                    else
                    {
                        bucketKey = DetectGlassType(specGroup.SpecName, specGroup) ?? "SGU";
                        revenue = specCost * 1.15m; // legacy estimate basis (flagged below)
                    }

                    if (!buckets.ContainsKey(bucketKey))
                        buckets[bucketKey] = (0m, 0m, 0m, 0);

                    var b = buckets[bucketKey];
                    buckets[bucketKey] = (
                        b.Revenue + revenue,
                        b.Cost + specCost,
                        b.Area + specGroup.CutSqm,
                        b.SpecCount + 1);
                }

                // Build the comparison rows (only buckets with real cost)
                decimal totalRevenue = 0m;
                decimal totalCost = 0m;
                decimal totalProfit = 0m;

                foreach (var kvp in buckets)
                {
                    var (revenue, cost, area, specCount) = kvp.Value;
                    if (cost <= 0m) continue;

                    var profit = revenue - cost;
                    var marginPercent = revenue > 0m
                        ? Math.Round((profit / revenue) * 100m, 1)
                        : 0m;

                    var row = new GlassTypeComparisonRow
                    {
                        GlassType = kvp.Key,
                        JobCount = specCount,
                        TotalArea = area,
                        TotalRevenue = revenue,
                        TotalCost = cost,
                        TotalProfit = profit,
                        MarginPercent = marginPercent,
                        IsProfitable = profit >= 0m,
                        IsBestPerformer = false,
                        IsWorstPerformer = false
                    };

                    GlassTypeRows.Add(row);
                    totalRevenue += revenue;
                    totalCost += cost;
                    totalProfit += profit;
                }

                if (GlassTypeRows.Count > 0)
                {
                    var bestPerformer = GlassTypeRows
                        .OrderByDescending(r => r.MarginPercent).First();
                    bestPerformer.IsBestPerformer = true;
                    BestPerformingGlassType = bestPerformer.GlassType;

                    var worstPerformer = GlassTypeRows
                        .OrderBy(r => r.MarginPercent).First();
                    worstPerformer.IsWorstPerformer = true;
                    WorstPerformingGlassType = worstPerformer.GlassType;

                    Recommendation = GenerateRecommendation(bestPerformer, worstPerformer);
                }
                else
                {
                    BestPerformingGlassType = "";
                    WorstPerformingGlassType = "";
                    Recommendation = "";
                }

                TotalRevenue = totalRevenue;
                TotalCost = totalCost;
                TotalProfit = totalProfit;
                OverallMarginPercent = totalRevenue > 0m
                    ? Math.Round((totalProfit / totalRevenue) * 100m, 1)
                    : 0m;

                // Derived totals for the column-aligned footer row.
                OnPropertyChanged(nameof(TotalJobCount));
                OnPropertyChanged(nameof(TotalJobCountDisplay));
                OnPropertyChanged(nameof(TotalArea));
                OnPropertyChanged(nameof(TotalAreaDisplay));

                // ── Honest status line ───────────────────────────────────
                string basis = hasTargetModel
                    ? "Actual Step 5 given prices"
                    : "ESTIMATED basis (cost × 1.15) — calculate Step 9 to compare real quoted prices";
                string note = $"{GlassTypeRows.Count} glass type(s) analyzed on {basis}.";

                var gaps = new List<string>();
                if (skippedUncosted > 0)
                    gaps.Add($"{skippedUncosted} spec group(s) skipped — no Step 9 cost rows (never shown as 0%)");
                if (skippedNotCovered > 0)
                    gaps.Add($"{skippedNotCovered} spec group(s) not covered by the target run — recalculate Step 9");

                if (gaps.Count > 0)
                    note += "  " + string.Join("; ", gaps) + ".";

                StatusMessage = note;
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[GlassTypeComparisonViewModel] Refresh failed: {ExceptionMessages.Deep(ex)}");
                StatusMessage = "Analysis failed. Please check the data and try again.";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ResetSummary()
        {
            TotalRevenue = 0m;
            TotalCost = 0m;
            TotalProfit = 0m;
            OverallMarginPercent = 0m;
            BestPerformingGlassType = "";
            WorstPerformingGlassType = "";
            Recommendation = "";
            OnPropertyChanged(nameof(TotalJobCount));
            OnPropertyChanged(nameof(TotalJobCountDisplay));
            OnPropertyChanged(nameof(TotalArea));
            OnPropertyChanged(nameof(TotalAreaDisplay));
        }

        /// <summary>
        /// Authoritative build label from the target model; falls back to the
        /// legacy text detection when the build is Unknown.
        /// </summary>
        private static string BuildTypeKey(
            GlassBuildType buildType, string glassTypeCode, string specName)
        {
            string label = buildType switch
            {
                GlassBuildType.Sgu => "SGU",
                GlassBuildType.Dgu => "DGU",
                GlassBuildType.Lam => "LAM",
                GlassBuildType.DguLam => "DGU+LAM",
                _ => ""
            };
            if (!string.IsNullOrEmpty(label)) return label;

            string evidence = $"{glassTypeCode ?? ""} {specName ?? ""}";
            if (evidence.IndexOf("DGU", StringComparison.OrdinalIgnoreCase) >= 0)
                return "DGU";
            if (evidence.IndexOf("LAM", StringComparison.OrdinalIgnoreCase) >= 0)
                return "LAM";
            return "SGU";
        }

        /// <summary>
        /// Legacy text-based classification used ONLY when no target-model
        /// data exists yet. Spacer/interlayer evidence drives DGU/LAM; a
        /// single-pane spec without markers defaults to SGU.
        /// </summary>
        private static string DetectGlassType(string specName, SpecCostGroup specGroup)
        {
            specName ??= "";
            specGroup ??= new SpecCostGroup();
            string groupName = specGroup.SpecName ?? "";

            var spacerMatch = PnLSpecParser.Spacer(specName);
            bool spacerDetected = spacerMatch.Success;

            var pvbMatch = PnLSpecParser.Interlayer(specName);
            bool pvbDetected = pvbMatch.Success;

            bool isDgu = groupName.IndexOf("DGU", StringComparison.OrdinalIgnoreCase) >= 0
                         || spacerDetected;
            bool isLam = groupName.IndexOf("LAM", StringComparison.OrdinalIgnoreCase) >= 0
                         || pvbDetected;
            bool isDguLam = spacerDetected && pvbDetected;

            if (isDguLam) return "DGU+LAM";
            if (isDgu) return "DGU";
            if (isLam) return "LAM";
            return "SGU";
        }

        private string GenerateRecommendation(GlassTypeComparisonRow bestPerformer, GlassTypeComparisonRow worstPerformer)
        {
            var recommendations = new List<string>();

            // Recommend promoting best performer
            recommendations.Add($"Promote {bestPerformer.GlassType} (margin: {bestPerformer.MarginPercent:0.#}%)");

            // Warn about worst performer if margin is low
            if (worstPerformer.MarginPercent < 10m)
            {
                recommendations.Add($"Review {worstPerformer.GlassType} pricing (margin only {worstPerformer.MarginPercent:0.#}%)");
            }

            // Suggest pricing adjustments
            if (worstPerformer.MarginPercent < 5m)
            {
                var requiredIncrease = (10m - worstPerformer.MarginPercent);
                recommendations.Add($"Increase {worstPerformer.GlassType} prices by ~{requiredIncrease:0}% to achieve 10% margin");
            }

            // Highlight significant differences
            var marginDifference = bestPerformer.MarginPercent - worstPerformer.MarginPercent;
            if (marginDifference > 15m)
            {
                recommendations.Add($"Significant margin gap: {marginDifference:0.#}% between best and worst performers");
            }

            return string.Join(". ", recommendations) + ".";
        }
    }

    /// <summary>
    /// Glass type comparison row for breakdown display.
    /// </summary>
    public class GlassTypeComparisonRow : ViewModelBase
    {
        private string _glassType = "";
        public string GlassType
        {
            get => _glassType;
            set
            {
                if (_glassType == value) return;
                _glassType = value ?? "";
                OnPropertyChanged();
            }
        }

        private int _jobCount;
        public int JobCount
        {
            get => _jobCount;
            set
            {
                if (_jobCount == value) return;
                _jobCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(JobCountDisplay));
            }
        }

        public string JobCountDisplay => $"{JobCount}";

        private decimal _totalArea;
        public decimal TotalArea
        {
            get => _totalArea;
            set
            {
                if (_totalArea == value) return;
                _totalArea = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalAreaDisplay));
            }
        }

        public string TotalAreaDisplay => $"{TotalArea:0.##} SQM";

        private decimal _totalRevenue;
        public decimal TotalRevenue
        {
            get => _totalRevenue;
            set
            {
                if (_totalRevenue == value) return;
                _totalRevenue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalRevenueDisplay));
            }
        }

        public string TotalRevenueDisplay => $"AED {TotalRevenue:N2}";

        private decimal _totalCost;
        public decimal TotalCost
        {
            get => _totalCost;
            set
            {
                if (_totalCost == value) return;
                _totalCost = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalCostDisplay));
            }
        }

        public string TotalCostDisplay => $"AED {TotalCost:N2}";

        private decimal _totalProfit;
        public decimal TotalProfit
        {
            get => _totalProfit;
            set
            {
                if (_totalProfit == value) return;
                _totalProfit = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalProfitDisplay));
            }
        }

        public string TotalProfitDisplay => $"AED {TotalProfit:N2}";

        private decimal _marginPercent;
        public decimal MarginPercent
        {
            get => _marginPercent;
            set
            {
                if (_marginPercent == value) return;
                _marginPercent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MarginDisplay));
            }
        }

        public string MarginDisplay => $"{MarginPercent:0.#}%";

        private bool _isProfitable = true;
        public bool IsProfitable
        {
            get => _isProfitable;
            set
            {
                if (_isProfitable == value) return;
                _isProfitable = value;
                OnPropertyChanged();
            }
        }

        private bool _isBestPerformer;
        public bool IsBestPerformer
        {
            get => _isBestPerformer;
            set
            {
                if (_isBestPerformer == value) return;
                _isBestPerformer = value;
                OnPropertyChanged();
            }
        }

        private bool _isWorstPerformer;
        public bool IsWorstPerformer
        {
            get => _isWorstPerformer;
            set
            {
                if (_isWorstPerformer == value) return;
                _isWorstPerformer = value;
                OnPropertyChanged();
            }
        }
    }
}
