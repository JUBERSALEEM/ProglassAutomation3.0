// ══════════════════════════════════════════════════════════════════
// FILE: ViewModels/Optimization/OptimizationViewModel.Costing.cs
// PROJECT: ProGlassAutomation
// MODULE: Optimization — Step 4 priced costing of a cutting plan
//
// WHY THIS FILE
//   The REPORT tab showed sheets, utilization and offcut but no money, so the
//   plan could not answer the question the owner actually asks: what does this
//   cutting plan COST, and how much of that is glass we threw away?
//
//   Rates come from STEP 4, carried through with the sheet size itself
//   (SpecGroup.OuterSheetPricePerSqm / InnerSheetPricePerSqm → Step4Sheets →
//   StockEntryVm.PricePerSqm). That is deliberate: the alternative is looking
//   the rate up in the Sheet Store by colour, and this module only has a glass
//   REF string ("6mm clear ft glass"), not a Sheet Store colour. Guessing that
//   mapping would silently price the wrong glass, so a row with no Step 4 price
//   shows "—" and says why instead of inventing a number.
//
//   Rows are keyed (width, height, composition) exactly like RefreshStockUsage:
//   keying by size alone prices a low-e rack at the clear rack's rate whenever
//   a job's outer and inner panes share one sheet size.
// ══════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace ProGlassAutomation.ViewModels.Optimization
{
    public partial class OptimizationViewModel
    {
        /// <summary>One row of the priced cutting plan: a glass composition on
        /// one Step 4 sheet size.</summary>
        /// <summary>
        /// One Step 5 spec pane that draws from an optimization rack. Persisted
        /// with the session so Step 6 and Step 9 can join a finished run back to
        /// the pane it cut — structurally, by spec number and side, rather than
        /// by matching a colour string that Step 6 itself allows to be blank.
        /// </summary>
        public sealed class PaneIdentity
        {
            public int SpecNumber { get; set; }
            public string Side { get; set; } = string.Empty;
        }

        public sealed class CostingRowVm
        {
            public string Composition { get; set; } = string.Empty;
            public string Thickness { get; set; } = string.Empty;

            /// <summary>The Step 4 sheet this group was cut from, "3210 × 2250".</summary>
            public string SheetSize { get; set; } = string.Empty;

            /// <summary>Sheets consumed — the quantity the saw actually runs.</summary>
            public string SheetQty { get; set; } = "0";

            /// <summary>Parts placed on those sheets.</summary>
            public string PieceQty { get; set; } = "0";

            /// <summary>Glass area ÷ stock area for these sheets.</summary>
            public string Utilization { get; set; } = "—";

            public string GlassArea { get; set; } = "0.00";
            public string StockArea { get; set; } = "0.00";
            public string WasteArea { get; set; } = "0.00";

            /// <summary>Step 4 rate, "AED 170 / m²", or "—" when not recorded.</summary>
            public string RateLabel { get; set; } = "—";

            public string GlassCostLabel { get; set; } = "—";
            public string WasteCostLabel { get; set; } = "—";
            public string TotalCostLabel { get; set; } = "—";

            /// <summary>False when no Step 4 price exists for this row, so the
            /// UI can show the money columns as pending rather than zero.</summary>
            public bool HasPrice { get; set; }

            // Raw values, for the export (Excel keeps numbers as numbers).
            public int SheetCount { get; set; }
            public decimal Rate { get; set; }
            public decimal GlassM2 { get; set; }
            public decimal StockM2 { get; set; }
            public decimal WasteM2Value { get; set; }
            public decimal GlassCost { get; set; }
            public decimal WasteCost { get; set; }
            public decimal TotalCost { get; set; }

            // ── join identity, so Step 6 / Step 9 can read this run ──
            public int WidthMm { get; set; }
            public int HeightMm { get; set; }
            public int PieceCount { get; set; }
            public decimal UtilizationPercent { get; set; }

            /// <summary>Which Step 5 spec pane(s) this rack was cut for, e.g.
            /// "Spec 1 · Outer". Shows the join on screen instead of leaving the
            /// operator to trust it. "—" means a hand-added rack that belongs to
            /// no spec, and "shared" means several panes drew from it.</summary>
            public string PaneLabel { get; set; } = "—";

            /// <summary>The single Step 5 spec pane that owns this rack, or 0
            /// when several panes share it and the sheets cannot be told apart
            /// after the cut.</summary>
            public int SpecNumber { get; set; }
            public string Side { get; set; } = string.Empty;

            /// <summary>True when more than one spec pane draws from this rack.
            /// Utilization stays exact — it belongs to the rack — but the sheet
            /// quantity is a shared total that Step 6 / Step 9 must split with
            /// the existing weighted allocation instead of reusing per pane.</summary>
            public bool IsSharedAcrossSpecs { get; set; }
            public int SharedWithSpecs { get; set; }
            public List<PaneIdentity> Panes { get; } = new();
        }

        public ObservableCollection<CostingRowVm> CostingRows { get; } = new();

        /// <summary>True once a run has produced priced rows.</summary>
        public bool HasCosting => CostingRows.Count > 0;

        private string _costingTotalLabel = string.Empty;
        /// <summary>Grand total line for the whole plan.</summary>
        public string CostingTotalLabel
        {
            get => _costingTotalLabel;
            private set
            {
                if (_costingTotalLabel == value) return;
                _costingTotalLabel = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCostingTotal));
            }
        }

        public bool HasCostingTotal => !string.IsNullOrWhiteSpace(_costingTotalLabel);

        private string _costingPriceNote = string.Empty;
        /// <summary>Explains any row that could not be priced from Step 4. Empty
        /// when every row carries a rate — the note is never decorative.</summary>
        public string CostingPriceNote
        {
            get => _costingPriceNote;
            private set
            {
                if (_costingPriceNote == value) return;
                _costingPriceNote = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCostingPriceNote));
            }
        }

        public bool HasCostingPriceNote => !string.IsNullOrWhiteSpace(_costingPriceNote);

        /// <summary>
        /// Names the Step 5 pane(s) a rack served. Deliberately says "shared"
        /// rather than picking one, because the engine cuts by composition and
        /// cannot say which of several specs on one rack took which sheet.
        /// </summary>
        private static string DescribePanes(List<PaneIdentity> identities)
        {
            if (identities == null || identities.Count == 0) return "—";

            var specs = new List<int>();
            foreach (var p in identities)
                if (!specs.Contains(p.SpecNumber)) specs.Add(p.SpecNumber);
            specs.Sort();

            string specText = "Spec " + string.Join(" + ",
                specs.Select(n => n.ToString(CultureInfo.InvariantCulture)));

            if (identities.Count == 1)
            {
                string side = identities[0].Side ?? string.Empty;
                return side.Length == 0 ? specText : specText + " · " + side;
            }

            return specText + " · shared";
        }

        /// <summary>
        /// Rebuilds the priced plan from the runs just applied. Called from
        /// ApplyResults immediately after RefreshStockUsage, so the sheet tally
        /// and the costing always describe the same run.
        /// </summary>
        private void RebuildCosting(List<GroupRun> runs)
        {
            CostingRows.Clear();

            var rates = new Dictionary<(int W, int H, string C), decimal>();

            // Which Step 5 spec panes draw from each rack. The engine groups by
            // composition, so two specs sharing one glass and one sheet size are
            // cut from the same pool and their sheets cannot be separated
            // afterwards — recorded here so the consumer can say so honestly.
            var panes = new Dictionary<(int W, int H, string C), List<PaneIdentity>>();

            foreach (var s in Stocks)
            {
                if (!int.TryParse(s.WidthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) ||
                    !int.TryParse(s.HeightText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h))
                    continue;

                var key = (w, h, (s.Composition ?? string.Empty).Trim().ToLowerInvariant());

                // A real Step 4 rate is never overwritten by a manual rack's 0.
                if (!rates.TryGetValue(key, out decimal existing) || existing <= 0m)
                    rates[key] = s.PricePerSqm;

                // A hand-added rack carries no spec, so it joins to nothing.
                if (s.SpecNumber > 0)
                {
                    if (!panes.TryGetValue(key, out var list))
                        panes[key] = list = new List<PaneIdentity>();

                    bool already = false;
                    foreach (var p in list)
                        if (p.SpecNumber == s.SpecNumber &&
                            string.Equals(p.Side, s.Side, StringComparison.OrdinalIgnoreCase))
                        { already = true; break; }

                    if (!already)
                        list.Add(new PaneIdentity { SpecNumber = s.SpecNumber, Side = s.Side ?? string.Empty });
                }
            }

            decimal totStock = 0m, totGlass = 0m, totWaste = 0m, totCost = 0m;
            int pricedRows = 0;

            foreach (var r in runs)
            {
                string comp = (r.Composition ?? string.Empty).Trim().ToLowerInvariant();

                foreach (var bySize in r.Result.Sheets
                             .GroupBy(sh => (sh.Stock.WidthMm, sh.Stock.HeightMm))
                             .OrderBy(g => g.Key.WidthMm)
                             .ThenBy(g => g.Key.HeightMm))
                {
                    int qty = bySize.Count();
                    long stockMm2 = bySize.Sum(sh => (long)sh.Stock.WidthMm * sh.Stock.HeightMm);
                    long glassMm2 = bySize.Sum(sh => sh.Pieces.Sum(p => (long)p.WidthMm * p.HeightMm));
                    int pieces = bySize.Sum(sh => sh.Pieces.Count);

                    decimal stockM2 = stockMm2 / 1_000_000m;
                    decimal glassM2 = glassMm2 / 1_000_000m;
                    decimal wasteM2 = stockM2 - glassM2;
                    if (wasteM2 < 0m) wasteM2 = 0m;

                    rates.TryGetValue((bySize.Key.WidthMm, bySize.Key.HeightMm, comp), out decimal rate);
                    bool hasPrice = rate > 0m;

                    panes.TryGetValue((bySize.Key.WidthMm, bySize.Key.HeightMm, comp), out var identities);
                    identities ??= new List<PaneIdentity>();
                    if (hasPrice) pricedRows++;

                    decimal glassCost = glassM2 * rate;
                    decimal wasteCost = wasteM2 * rate;
                    decimal totalCost = stockM2 * rate;

                    totStock += stockM2;
                    totGlass += glassM2;
                    totWaste += wasteM2;
                    if (hasPrice) totCost += totalCost;

                    decimal util = stockMm2 == 0 ? 0m : (decimal)glassMm2 / stockMm2 * 100m;

                    var row = new CostingRowVm
                    {
                        Composition = r.Composition,
                        Thickness = r.Thickness,
                        SheetSize = bySize.Key.WidthMm.ToString(CultureInfo.InvariantCulture) + " × " +
                                    bySize.Key.HeightMm.ToString(CultureInfo.InvariantCulture),
                        SheetQty = qty.ToString(CultureInfo.InvariantCulture),
                        PieceQty = pieces.ToString(CultureInfo.InvariantCulture),
                        Utilization = util.ToString("0.00", CultureInfo.InvariantCulture) + "%",
                        GlassArea = glassM2.ToString("0.00", CultureInfo.InvariantCulture),
                        StockArea = stockM2.ToString("0.00", CultureInfo.InvariantCulture),
                        WasteArea = wasteM2.ToString("0.00", CultureInfo.InvariantCulture),
                        RateLabel = hasPrice
                            ? string.Format(CultureInfo.InvariantCulture, "AED {0:0.##} / m²", rate)
                            : "—",
                        GlassCostLabel = hasPrice
                            ? string.Format(CultureInfo.InvariantCulture, "AED {0:N2}", glassCost) : "—",
                        WasteCostLabel = hasPrice
                            ? string.Format(CultureInfo.InvariantCulture, "AED {0:N2}", wasteCost) : "—",
                        TotalCostLabel = hasPrice
                            ? string.Format(CultureInfo.InvariantCulture, "AED {0:N2}", totalCost) : "—",
                        HasPrice = hasPrice,
                        SheetCount = qty,
                        Rate = rate,
                        GlassM2 = glassM2,
                        StockM2 = stockM2,
                        WasteM2Value = wasteM2,
                        GlassCost = glassCost,
                        WasteCost = wasteCost,
                        TotalCost = totalCost,

                        WidthMm = bySize.Key.WidthMm,
                        HeightMm = bySize.Key.HeightMm,
                        PieceCount = pieces,
                        UtilizationPercent = util,
                        SpecNumber = identities.Count == 1 ? identities[0].SpecNumber : 0,
                        Side = identities.Count == 1 ? identities[0].Side : string.Empty,
                        IsSharedAcrossSpecs = identities.Count > 1,
                        SharedWithSpecs = identities.Count
                    };

                    foreach (var idn in identities) row.Panes.Add(idn);
                    row.PaneLabel = DescribePanes(identities);
                    CostingRows.Add(row);
                }
            }

            CostingTotalLabel = string.Format(CultureInfo.InvariantCulture,
                "{0} sheet(s) · {1:0.00} m² stock · {2:0.00} m² glass · {3:0.00} m² wastage · AED {4:N2}",
                runs.Sum(r => r.Result.SheetsUsed), totStock, totGlass, totWaste, totCost);

            // Never let a missing rate read as free glass.
            CostingPriceNote = CostingRows.Count == 0 || pricedRows == CostingRows.Count
                ? string.Empty
                : pricedRows == 0
                    ? "No Step 4 sheet price is recorded for this job — import the Step 4 sheets on the " +
                      "STOCKS tab, or price them in Step 4, to cost the plan."
                    : string.Format(CultureInfo.InvariantCulture,
                          "{0} of {1} row(s) have no Step 4 price and read \"—\"; the total covers priced rows only.",
                          CostingRows.Count - pricedRows, CostingRows.Count);

            OnPropertyChanged(nameof(HasCosting));
        }
    }
}
