// ═══════════════════════════════════════════════════════════════
// FILE: OptimizationViewModel.Stocks.cs
// MODULE: Optimization — STOCKS tab — stock pool, Step 4 import and run usage tracking (partial of OptimizationViewModel)
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

        // ── STOCKS tab ─────────────────────────────────────────────────

        public sealed class StockEntryVm : ViewModels.ViewModelBase
        {
            private string _widthText;
            private string _heightText;
            // Effectively unlimited — usage is tracked, not capped. Sourced from
            // the engine constant so the run ledger, which must recognise this
            // value as "no balance set", can never drift away from it.
            private string _availableText = StockSize.EffectivelyUnlimitedSheets
                .ToString(CultureInfo.InvariantCulture);
            private string _usedLabel = string.Empty;
            private string _sourceLabel = string.Empty;
            private string _composition = string.Empty;
            private bool _isUsedInRun = true;
            private decimal _pricePerSqm;

            public StockEntryVm(string w, string h) { _widthText = w; _heightText = h; }
            public StockEntryVm(string w, string h, string source) { _widthText = w; _heightText = h; _sourceLabel = source; }
            public StockEntryVm(string w, string h, string composition, string source)
            {
                _widthText = w; _heightText = h; _composition = composition ?? string.Empty; _sourceLabel = source;
            }

            /// <summary>Step 4 rate for this rack, per m². 0 when the rack was
            /// added by hand or the job recorded no price — the costing then
            /// shows "—" rather than treating the glass as free.</summary>
            public decimal PricePerSqm
            {
                get => _pricePerSqm;
                set
                {
                    if (_pricePerSqm == value) return;
                    _pricePerSqm = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PriceLabel));
                    OnPropertyChanged(nameof(HasPrice));
                }
            }

            /// <summary>Step 5 spec group this rack was imported from; 0 for a
            /// hand-added rack, which then cannot be joined back to a pane.</summary>
            public int SpecNumber { get; set; }

            /// <summary>"Outer" or "Inner" — the pane of that spec this rack
            /// serves. Matches SpecGroupColorSide.SemanticSideLabel.</summary>
            public string Side { get; set; } = string.Empty;
            public string Color { get; set; } = string.Empty;
            public int ThicknessMm { get; set; }
            public string MaterialKey { get; set; } = string.Empty;
            public string SheetStoreSku { get; set; } = string.Empty;

            public bool HasPrice => _pricePerSqm > 0m;

            public string PriceLabel => _pricePerSqm > 0m
                ? string.Format(CultureInfo.InvariantCulture, "AED {0:0.##} / m²", _pricePerSqm)
                : "—";

            public string WidthText { get => _widthText; set { _widthText = value; OnPropertyChanged(); } }
            public string HeightText { get => _heightText; set { _heightText = value; OnPropertyChanged(); } }
            public string AvailableText { get => _availableText; set { _availableText = value; OnPropertyChanged(); } }

            /// <summary>Sheets consumed by the last run, e.g. "17 sheets used".</summary>
            public string UsedLabel
            {
                get => _usedLabel;
                set { if (_usedLabel == value) return; _usedLabel = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUsed)); }
            }

            public bool HasUsed => !string.IsNullOrWhiteSpace(_usedLabel);

            /// <summary>Provenance, e.g. "Step 4 · Spec 1 outer".</summary>
            public string SourceLabel { get => _sourceLabel; set { if (_sourceLabel == value) return; _sourceLabel = value; OnPropertyChanged(); } }

            /// <summary>
            /// The glass composition (cutting group) this rack belongs to.
            /// Empty = a manual "any glass" rack used by every group without
            /// its own Step 4 sheet. This is what routes each cutting group
            /// to its OWN Step 4 rack instead of a shared pool.
            /// </summary>
            public string Composition
            {
                get => _composition;
                set
                {
                    if (_composition == value) return;
                    _composition = value ?? string.Empty;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CompositionLabel));
                    OnPropertyChanged(nameof(HasComposition));
                }
            }

            /// <summary>Human label for the rack's owner ("All groups" for manual racks).</summary>
            public string CompositionLabel =>
                string.IsNullOrWhiteSpace(_composition) ? "Any group" : _composition;

            public bool HasComposition => !string.IsNullOrWhiteSpace(_composition);

            /// <summary>
            /// Whether this rack participates in the run. Step 4 sheets are
            /// auto-selected (outer + inner, colour-based); the operator can
            /// untick any rack so a size is kept on screen but never fed to
            /// the optimizer.
            /// </summary>
            public bool IsUsedInRun
            {
                get => _isUsedInRun;
                set { if (_isUsedInRun == value) return; _isUsedInRun = value; OnPropertyChanged(); }
            }
        }

        public ObservableCollection<StockEntryVm> Stocks { get; } = new();

        /// <summary>
        /// Report projection of <see cref="Stocks"/>. One row per plate size
        /// and glass colour. Spec 1 and spec 2 of the same glass, cut on the
        /// same rack, are not printed twice with the same used count.
        /// </summary>
        public ObservableCollection<StockAllocationRowVm> AllocationRows { get; } = new();

        public sealed class StockAllocationRowVm
        {
            public string SizeLabel { get; init; } = string.Empty;
            public string ColorLabel { get; init; } = string.Empty;
            public string UsedLabel { get; init; } = string.Empty;
            public string SourceLabel { get; init; } = string.Empty;
        }
        public bool HasStocks => Stocks.Count > 0;
        public ICommand AddStockCommand { get; }
        public ICommand RemoveStockCommand { get; }
        public ICommand ImportStockFromStep4Command { get; }

        private StockEntryVm _newStock = new("3210", "2250", "manual");
        public StockEntryVm NewStock { get => _newStock; private set { _newStock = value; OnPropertyChanged(); } }

        private string _stocksStatusText = "Tick a Job Order — its Step 4 sheet sizes become the stock pool for this run.";
        /// <summary>Feedback line on the STOCKS tab (which job/sheets feed the pool).</summary>
        public string StocksStatusText
        {
            get => _stocksStatusText;
            private set { if (_stocksStatusText == value) return; _stocksStatusText = value; OnPropertyChanged(); }
        }

        // ── stock ops ──────────────────────────────────────────────────

        private void AddStock()
        {
            if (!int.TryParse(NewStock.WidthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) ||
                !int.TryParse(NewStock.HeightText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) ||
                w <= 0 || h <= 0)
            {
                Blocked("Stock needs positive W×H in mm.", "Rack not added");
                return;
            }
            var row = new StockEntryVm(
                w.ToString(CultureInfo.InvariantCulture),
                h.ToString(CultureInfo.InvariantCulture),
                string.Empty, // manual rack — applies to any cutting group
                "manual")
            { AvailableText = NewStock.AvailableText };
            Stocks.Add(row);
            RunMessage = string.Empty;
            StocksStatusText = "Manual rack added — it serves any cutting group without its own Step 4 sheet.";
        }

        private void RemoveStock(StockEntryVm entry)
        {
            if (entry == null) return;

            // The rack itself is untouched - only dropped from this run. Worth
            // saying, because on a screen full of sheet sizes "remove" reads as
            // though it deletes stock.
            if (!ConfirmDestructive(
                    "Remove this stock size from the run?\n\n"
                    + "The rack stays in the sheet store. It is only dropped from "
                    + "this cutting run, so any group that was packing against it "
                    + "will need another size or will not cut.",
                    "Remove stock")) return;

            Stocks.Remove(entry);
        }

        /// <summary>
        /// Replaces the stock pool with the Step 4 chosen sheets of every
        /// ticked Job Order (deduped by size × glass). The job's Step 4
        /// sheet = the exact rack the factory quoted on, tagged with the
        /// pane's glass composition so each cutting group packs against its
        /// own rack — never the whole sheet store.
        /// </summary>
        private void ImportStockFromStep4()
        {
            if (!SelectedJobs.Any())
            {
                StocksStatusText = "Tick a Job Order first — its Step 4 sheets import into the stock pool.";
                RunMessage = StocksStatusText;
                return;
            }

            int count = SyncStocksFromSelectedJobs();
            StocksStatusText = count == 0
                ? "No Step 4 sheet sizes recorded on the selected job(s) — add a rack manually."
                : string.Format(CultureInfo.InvariantCulture,
                    "Stock pool = {0} Step 4 sheet size(s) from the selected job(s) — available = unlimited (usage-tracked).",
                    count);
            RunMessage = string.Empty;
        }

        /// <summary>
        /// Rebuilds <see cref="Stocks"/> so it contains ONLY the Step 4 sheet
        /// sizes of the ticked Job Orders (outer/inner per spec), tagged by
        /// their pane's glass composition — never the whole sheet store.
        /// Runs automatically whenever the job selection changes; the pool
        /// is fully replaced, so a manual rack added earlier is cleared the
        /// moment the selection changes (the pool always mirrors the ticked
        /// jobs' Step 4 sheets). Returns the number of rows.
        /// </summary>
        private int SyncStocksFromSelectedJobs()
        {
            var sheets = SelectedJobs.SelectMany(j => j.Step4Sheets).ToList();

            Stocks.Clear();
            var seen = new HashSet<(int W, int H, string C, int Spec, string Side)>();
            foreach (var (w, h, composition, source, pricePerSqm, specNumber, side) in sheets)
            {
                if (w <= 0 || h <= 0) continue;
                string comp = (composition ?? string.Empty).Trim();
                if (!seen.Add((w, h, comp.ToUpperInvariant(), specNumber, side))) continue;

                // Step 4 sheets are auto-selected for the run (outer and
                // inner, colour-based); the operator can untick any rack.
                Stocks.Add(new StockEntryVm(
                    w.ToString(CultureInfo.InvariantCulture),
                    h.ToString(CultureInfo.InvariantCulture),
                    comp,
                    "Step 4 · " + source)
                { IsUsedInRun = true, PricePerSqm = pricePerSqm, SpecNumber = specNumber, Side = side });
            }

            if (sheets.Count == 0 && Stocks.Count == 0)
                StocksStatusText = "Tick a Job Order — its Step 4 sheet sizes become the stock pool for this run.";

            RebuildAllocationRows();
            return Stocks.Count;
        }

        private void RefreshStockUsage(List<GroupRun> runs)
        {
            // A Step 4 rack is tagged with the glass composition it belongs
            // to. Keying usage by (width, height) ALONE double-counts when a
            // job's outer and inner panes happen to share one sheet size:
            // both the low-e and clear 3210×2250 racks then read "34 sheets
            // used" (17 + 17) instead of their own 17 each. Keying by
            // (width, height, composition) keeps every rack's tally scoped
            // to its OWN cutting group.
            var used = new Dictionary<(int W, int H, string C), int>();
            foreach (var r in runs)
            {
                string comp = (r.Composition ?? string.Empty).Trim().ToLowerInvariant();
                foreach (var sheet in r.Result.Sheets)
                {
                    var key = (sheet.Stock.WidthMm, sheet.Stock.HeightMm, comp);
                    used.TryGetValue(key, out int n);
                    used[key] = n + 1;
                }
            }

            foreach (var s in Stocks)
            {
                if (!int.TryParse(s.WidthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) ||
                    !int.TryParse(s.HeightText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h))
                {
                    s.UsedLabel = string.Empty;
                    continue;
                }

                string comp = (s.Composition ?? string.Empty).Trim().ToLowerInvariant();

                int n = 0;
                if (used.TryGetValue((w, h, comp), out n))
                {
                    s.UsedLabel = n.ToString(CultureInfo.InvariantCulture)
                        + (n == 1 ? " sheet used" : " sheets used");
                }
                else if (comp.Length == 0)
                {
                    // A manual "any group" rack carries no composition tag —
                    // its tally is every group that packed that sheet size.
                    n = runs.Sum(r => r.Result.Sheets.Count(
                        sh => sh.Stock.WidthMm == w && sh.Stock.HeightMm == h));
                    s.UsedLabel = n > 0
                        ? n.ToString(CultureInfo.InvariantCulture)
                            + (n == 1 ? " sheet used" : " sheets used")
                        : string.Empty;
                }
                else
                {
                    s.UsedLabel = string.Empty;
                }
            }

            RebuildAllocationRows();
        }

        /// <summary>
        /// Colour key for allocation. Thickness, hyphens and word order do
        /// not split one glass into two rows ("6mm clear low-e" and
        /// "clear low e" are the same colour).
        /// </summary>
        private static string AllocationColorKey(string? composition)
        {
            string n = CompositionKey.Normalize(composition).Replace('-', ' ');
            while (n.Contains("  ", StringComparison.Ordinal))
                n = n.Replace("  ", " ", StringComparison.Ordinal);
            var tokens = n.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 0
                            && !t.EndsWith("mm", StringComparison.Ordinal)
                            && !int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                .OrderBy(t => t, StringComparer.Ordinal);
            string key = string.Join(" ", tokens);
            return key.Length == 0 ? n : key;
        }

        private void RebuildAllocationRows()
        {
            AllocationRows.Clear();
            var groups = Stocks
                .Select(s =>
                {
                    int.TryParse(s.WidthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w);
                    int.TryParse(s.HeightText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h);
                    return (Row: s, W: w, H: h, Key: AllocationColorKey(s.Composition));
                })
                .GroupBy(x => (x.W, x.H, x.Key))
                .OrderBy(g => g.Key.Key, StringComparer.Ordinal)
                .ThenBy(g => g.Key.W)
                .ThenBy(g => g.Key.H);

            foreach (var group in groups)
            {
                var rows = group.Select(x => x.Row).ToList();
                string color = rows
                    .Select(r => r.CompositionLabel)
                    .OrderByDescending(c => c?.Length ?? 0)
                    .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))
                    ?? "Any group";
                var used = rows
                    .Select(r => r.UsedLabel)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                int specs = rows.Select(r => r.SpecNumber).Where(n => n > 0).Distinct().Count();
                string source = specs > 1
                    ? "Shared colour"
                    : (rows[0].SourceLabel ?? string.Empty);

                AllocationRows.Add(new StockAllocationRowVm
                {
                    SizeLabel = string.Format(CultureInfo.InvariantCulture,
                        "{0} × {1}", group.Key.W, group.Key.H),
                    ColorLabel = color,
                    UsedLabel = used.Count == 0 ? "—" : string.Join(" · ", used),
                    SourceLabel = source
                });
            }
        }
    }
}
