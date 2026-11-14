// ═══════════════════════════════════════════════════════════════
// FILE: OptimizationViewModel.Report.cs
// MODULE: Optimization — REPORTS tab — industrial cut report + session summaries (partial of OptimizationViewModel)
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CsvHelper;
using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.JobOrder.Common.JobOrderStatus;
using ProGlassAutomation.Models.Production;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.Csv;
using ProGlassAutomation.Services.JobOrder.Mapper;
using ProGlassAutomation.Services.JobOrder.Repository;
using ProGlassAutomation.Services.Optimization;
using ProGlassAutomation.Services.Production;
using ProGlassAutomation.Services.Reporting;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss;

namespace ProGlassAutomation.ViewModels.Optimization
{
    public partial class OptimizationViewModel : ViewModels.ViewModelBase
    {

        // ── REPORTS tab ───────────────────────────────────────────────
        // The industrial cut report (the owner's production format) groups
        // the session by glass composition: one utilization line per spec
        // × sheet size, then a GLASS REF / W1 / H1 / W2 / H2 / QTY /
        // TOTAL SQM piece table per spec. The legacy layout / parts /
        // stocks summaries remain below as session detail.

        // ── Industrial report models ─────────────────────────────────

        /// <summary>One glass composition (spec) in the industrial report:
        /// its utilization line(s) and its per-ref piece table.</summary>
        public sealed class IndustrialReportSpecVm
        {
            public string Composition { get; set; } = string.Empty;
            public string ThicknessLabel { get; set; } = string.Empty;
            public string SpecName { get; set; } = string.Empty;
            public string ColorLabel { get; set; } = string.Empty;
            public string OuterColor { get; set; } = string.Empty;
            public string InnerColor { get; set; } = string.Empty;
            public bool HasColorPair =>
                OuterColor.Length > 0 && InnerColor.Length > 0
                && !string.Equals(OuterColor, InnerColor, StringComparison.OrdinalIgnoreCase);
            public int SpecGroupNumber { get; set; }
            public string Side { get; set; } = string.Empty;
            public List<IndustrialUtilizationLineVm> Utilization { get; } = new();
            public List<IndustrialReportPieceVm> Pieces { get; } = new();
            public bool HasUtilization => Utilization.Count > 0;
        }

        /// <summary>
        /// One report row. A spec with two colours is Outer (left) and
        /// Inner (right). A single colour spans the row. This is the
        /// cutting-popup column contract, not a left-stacked list.
        /// </summary>
        public sealed class IndustrialReportBandVm
        {
            public IndustrialReportSpecVm Outer { get; set; } = null!;
            public IndustrialReportSpecVm? Inner { get; set; }
            public bool HasInner => Inner != null;
            public bool IsSingle => Inner == null;
        }

        /// <summary>One owner-format utilization line, e.g.
        /// "6MM THICKNESS CLIMAGUARD HP NEUTRAL 70 LOW E(SPEC) 3210 * 2250
        /// SHEET UTILIZED QTY 17 (SHEET SIZE) - 82.77% UTILIZATION OUTER".
        /// Tone drives the green/amber/red colour coding (≥85 / 70–84 / &lt;70).</summary>
        public sealed class IndustrialUtilizationLineVm
        {
            public string Line { get; set; } = string.Empty;
            public double UtilizationPct { get; set; }
            public string Tone { get; set; } = "Good";
        }

        /// <summary>One piece row in a spec's table: GLASS REF · W1 · H1 ·
        /// W2 · H2 · QTY · TOTAL SQM.</summary>
        public sealed class IndustrialReportPieceVm
        {
            public string GlassRef { get; set; } = string.Empty;
            public string W1 { get; set; } = string.Empty;
            public string H1 { get; set; } = string.Empty;
            public string W2 { get; set; } = string.Empty;
            public string H2 { get; set; } = string.Empty;
            public string Qty { get; set; } = string.Empty;
            public string TotalSqm { get; set; } = string.Empty;
        }

        /// <summary>Flattened row for the Excel/PDF industrial export —
        /// composition as the section key, an optional utilization line,
        /// then the piece-table columns.</summary>
        public sealed class IndustrialSectionRow
        {
            public string Section = string.Empty;
            public string Line = string.Empty;
            public string GlassRef = string.Empty;
            public string W1 = string.Empty;
            public string H1 = string.Empty;
            public string W2 = string.Empty;
            public string H2 = string.Empty;
            public string Qty = string.Empty;
            public string TotalSqm = string.Empty;
        }

        public ObservableCollection<IndustrialReportSpecVm> IndustrialReport { get; } = new();

        /// <summary>Display rows for the report tab. Export still reads
        /// <see cref="IndustrialReport"/>.</summary>
        public ObservableCollection<IndustrialReportBandVm> IndustrialReportBands { get; } = new();

        private bool _hasIndustrialReport;
        public bool HasIndustrialReport { get => _hasIndustrialReport; private set { _hasIndustrialReport = value; OnPropertyChanged(); } }

        // ── export commands ──────────────────────────────────────────
        // One command per report surface; the format travels as the button's
        // CommandParameter ("CSV" / "Excel" / "PDF"). ExportAll emits the
        // three session summaries as a single sectioned document.

        public ICommand ExportLayoutReportCommand { get; }
        public ICommand ExportPartsReportCommand { get; }
        public ICommand ExportStocksReportCommand { get; }
        public ICommand ExportIndustrialReportCommand { get; }
        public ICommand ExportAllCommand { get; }

        /// <summary>Maps a button parameter to the export format (default CSV).</summary>
        private static ExportFormat ParseExportFormat(object? parameter)
        {
            if (parameter is ExportFormat fmt) return fmt;
            string? s = parameter as string;
            if (string.Equals(s, "Excel", StringComparison.OrdinalIgnoreCase)) return ExportFormat.Excel;
            if (string.Equals(s, "PDF", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "Pdf", StringComparison.OrdinalIgnoreCase)) return ExportFormat.Pdf;
            return ExportFormat.Csv;
        }

        /// <summary>Numeric cells stay numeric in Excel; anything else stays text.</summary>
        private static object ExcelNumber(string text)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return i;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
            return text;
        }

        // ══════════════════════════════════════════════════════════════════
        //  PHASE 4 / findings 4b, 4c — THE EXPORT GATE
        //
        //  A run result was applied to the screen, published to Step 6 and
        //  written to a report or cut file with nothing standing between the
        //  plan and the paper. ProblemCount existed and only ever became a
        //  column label. Two of the three oracles in GuillotinePacker were
        //  reachable from production code, and the strongest of them —
        //  VerifyComprehensive, the only check covering MINIMUM BREAKOUT and
        //  MATERIAL CONSERVATION as well as geometry — had zero callers
        //  anywhere in the repository, tests included.
        //
        //  The gate belongs here rather than on every run: the search evaluates
        //  up to 147 candidate plans and Verify is O(pieces²) per sheet, so
        //  running the comprehensive oracle per candidate would be unaffordable,
        //  while export happens once and is the moment a number becomes glass
        //  somebody cuts and somebody invoices.
        //
        //  The plan stays on screen when the gate refuses it. An operator who
        //  cannot see the plan cannot diagnose it, and hiding the result would
        //  repeat the original RC-009 complaint — a module that shows nothing.
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Rebuilds an input-piece list from a finished result. Used only for the
        /// oracles whose geometry and material checks need it; the CONSERVATION
        /// check deliberately does NOT use this, because a list reconstructed
        /// from a result always agrees with that result and would pass by
        /// construction.
        /// </summary>
        private static List<CutPiece> ReconstructInputPieces(OptimizationResult result)
        {
            var list = new List<CutPiece>();
            if (result == null) return list;

            foreach (var sheet in result.Sheets)
            {
                if (sheet?.Pieces == null) continue;
                foreach (var placed in sheet.Pieces)
                    if (placed?.Piece != null) list.Add(placed.Piece);
            }

            foreach (var unplaced in result.Unplaced)
                if (unplaced != null) list.Add(unplaced);

            return list;
        }

        /// <summary>
        /// True when every cutting group in the current result may be exported.
        /// On refusal, <see cref="RunMessage"/> names the groups and the first
        /// problem found in each, and the reason is logged.
        /// </summary>
        private bool TryPassExportGate(string what)
        {
            if (!HasResult || IsRunning)
            {
                Blocked("Run optimization again before exporting a cutting plan.", "Nothing to export");
                return false;
            }
            if (_groupRuns.Count == 0) return true;

            var problems = new List<string>();

            foreach (var r in _groupRuns)
            {
                if (r?.Result == null) continue;

                // 1. A plan any oracle already failed during the run.
                if (r.ValidationFailed)
                {
                    problems.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0}: {1} layout validation issue(s)", r.Composition, r.ProblemCount));
                    continue;
                }

                // 2. Genuine quantity conservation, against the count this group
                //    was BUILT from (GroupRun.PieceCount, captured before the
                //    engine ran) rather than a list reconstructed afterwards.
                int placed = r.Result.Sheets.Sum(sh => sh.Pieces.Count);
                int unplaced = r.Result.Unplaced.Count;
                if (placed + unplaced != r.PieceCount)
                {
                    problems.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0}: {1} piece(s) went in but the plan accounts for {2} placed + {3} unplaced",
                        r.Composition, r.PieceCount, placed, unplaced));
                    continue;
                }

                // 3. The comprehensive oracle. First call site it has ever had.
                var deep = GuillotinePacker.VerifyComprehensive(
                    r.Result, ReconstructInputPieces(r.Result), r.Margins);
                if (deep.Count > 0)
                {
                    string first = deep[0];

                    // PHASE 9 (safe): finding B3 made racks carry their thickness,
                    // which woke VerifyComprehensive's piece-vs-stock thickness
                    // check — dormant until then, because every rack reported 0mm
                    // and the check is guarded by `Stock.ThicknessMm > 0`. The
                    // common legitimate way to trip it is the cross-composition
                    // opt-in, so when that setting is on the message says so,
                    // rather than leaving an operator to work out why a run that
                    // exported yesterday will not export today.
                    if (AllowCrossCompositionStock &&
                        first.IndexOf("does not match stock", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        first += " SETTINGS ▸ Allow cross-composition stock is ON, which is what let "
                               + "this group borrow a rack of a different thickness. Turn it off for a "
                               + "plan you can actually cut.";
                    }

                    problems.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0}: {1}", r.Composition, first));
                }
            }

            if (problems.Count == 0) return true;

            string detail = string.Join(" | ", problems.Take(4).ToArray())
                + (problems.Count > 4
                    ? string.Format(CultureInfo.InvariantCulture, " | +{0} more group(s)", problems.Count - 4)
                    : string.Empty);

            RunMessage = "EXPORT BLOCKED — " + what + " was NOT written. " + detail +
                " The plan is still on screen for inspection; fix the cause or re-run before exporting.";

            try
            {
                Services.Logging.AppLogService.Warning(
                    $"[Optimization] export of {what} blocked for {problems.Count} cutting group(s): " +
                    string.Join(" | ", problems.ToArray()),
                    new Services.Logging.LogContext { Module = "Optimization", Operation = "ExportGate" });
            }
            catch { }

            return false;
        }

        private void ExportLayoutReport(ExportFormat format)
        {
            if (!HasResult || SheetRows.Count == 0)
            {
                Blocked("Run a cut first — the layout summary has no sheets yet.", "Nothing to export");
                return;
            }

            if (!TryPassExportGate("the layout summary")) return;
            AsyncUtility.SafeFireAndForget(
                async () => await OptimizationReportExport.RunAsync(
                    format,
                    SheetRows.ToList(),
                    "Optimization — Layout Summary",
                    new[] { "Sheet", "Glass", "Stock (mm)", "Pieces", "Utilization", "Offcut (m²)" },
                    r => new[] { r.SheetNo, r.Composition, r.StockLabel, r.PieceCount, r.Utilization, r.WasteLabel },
                    r => new object[] { ExcelNumber(r.SheetNo), r.Composition, r.StockLabel, ExcelNumber(r.PieceCount), r.Utilization, r.WasteLabel },
                    "Optimization_Layout_Summary"),
                "Optimization layout report export");
        }

        private void ExportPartsReport(ExportFormat format)
        {
            if (!HasPieces || SizeGroups.Count == 0)
            {
                Blocked("The cutting list is empty — add pieces before exporting the parts summary.", "Nothing to export");
                return;
            }
            AsyncUtility.SafeFireAndForget(
                async () => await OptimizationReportExport.RunAsync(
                    format,
                    SizeGroups.ToList(),
                    "Optimization — Parts Summary",
                    new[] { "Glass Composition", "Thickness", "Pieces", "Area (m²)" },
                    g => new[] { g.Composition, g.Thickness, g.PieceCount, g.AreaM2 },
                    g => new object[] { g.Composition, g.Thickness, ExcelNumber(g.PieceCount), ExcelNumber(g.AreaM2) },
                    "Optimization_Parts_Summary"),
                "Optimization parts report export");
        }

        private void ExportStocksReport(ExportFormat format)
        {
            if (Stocks.Count == 0)
            {
                Blocked("The stock pool is empty — select a job order to import its Step 4 sheets.", "Nothing to export");
                return;
            }
            AsyncUtility.SafeFireAndForget(
                async () => await OptimizationReportExport.RunAsync(
                    format,
                    Stocks.ToList(),
                    "Optimization — Stocks Summary",
                    new[] { "Sheet (mm)", "Glass / Group", "Available", "Used this run", "Source", "In run" },
                    s => new[]
                    {
                        s.WidthText + "×" + s.HeightText,
                        s.CompositionLabel,
                        s.AvailableText,
                        s.UsedLabel,
                        s.SourceLabel,
                        s.IsUsedInRun ? "Yes" : "No"
                    },
                    s => new object[]
                    {
                        s.WidthText + "×" + s.HeightText,
                        s.CompositionLabel,
                        ExcelNumber(s.AvailableText),
                        s.UsedLabel,
                        s.SourceLabel,
                        s.IsUsedInRun ? "Yes" : "No"
                    },
                    "Optimization_Stocks_Summary"),
                "Optimization stocks report export");
        }

        private void ExportIndustrialReport(ExportFormat format)
        {
            if (!HasIndustrialReport)
            {
                Blocked("Nothing to export — add pieces (JOBS tab) and run a cut first.", "Nothing to export");
                return;
            }

            if (!TryPassExportGate("the industrial cut report")) return;

            if (format == ExportFormat.Csv)
            {
                // Owner format: utilization line(s) then the GLASS REF /
                // W1 / H1 / W2 / H2 / QTY / TOTAL SQM table per composition.
                var layout = new IndustrialReportCsvLayout(IndustrialReport.ToList());
                AsyncUtility.SafeFireAndForget(
                    async () => await new CsvExportEngine().ExportAsync(
                        layout,
                        new ExportRequest { SuggestedFileName = "Optimization_Industrial_Cut_Report" }),
                    "Optimization industrial report export");
                return;
            }

            // Excel / PDF: one sectioned table — the composition is the
            // SECTION, the utilization line rides in its own LINE column,
            // then the piece rows.
            var rows = new List<IndustrialSectionRow>();
            foreach (var spec in IndustrialReport)
            {
                if (spec.Utilization.Count == 0)
                    rows.Add(new IndustrialSectionRow { Section = spec.Composition });
                else
                    foreach (var line in spec.Utilization)
                        rows.Add(new IndustrialSectionRow { Section = spec.Composition, Line = line.Line });

                foreach (var p in spec.Pieces)
                {
                    rows.Add(new IndustrialSectionRow
                    {
                        Section = spec.Composition,
                        GlassRef = p.GlassRef,
                        W1 = p.W1, H1 = p.H1, W2 = p.W2, H2 = p.H2,
                        Qty = p.Qty, TotalSqm = p.TotalSqm
                    });
                }
            }

            AsyncUtility.SafeFireAndForget(
                async () => await OptimizationReportExport.RunAsync(
                    format,
                    rows,
                    "Optimization — Industrial Cut Report",
                    new[] { "SECTION", "LINE", "GLASS REF", "W1", "H1", "W2", "H2", "QTY", "TOTAL SQM" },
                    r => new[] { r.Section, r.Line, r.GlassRef, r.W1, r.H1, r.W2, r.H2, r.Qty, r.TotalSqm },
                    r => new object[] { r.Section, r.Line, r.GlassRef, ExcelNumber(r.W1), ExcelNumber(r.H1), ExcelNumber(r.W2), ExcelNumber(r.H2), ExcelNumber(r.Qty), ExcelNumber(r.TotalSqm) },
                    "Optimization_Industrial_Cut_Report"),
                "Optimization industrial report export");
        }

        /// <summary>
        /// The ONE export. Everything the REPORT tab can show goes into a single
        /// sectioned document — run summary, the Step 4 priced costing, the
        /// owner's industrial cut report, then the layout / parts / stocks
        /// summaries — instead of five separate downloads the operator had to
        /// staple together. Sections share one row shape and leave the columns
        /// they do not use empty, which is how the existing combined export
        /// already groups rows.
        /// </summary>
        private void ExportAll(ExportFormat format)
        {
            if (!HasResult && !HasPieces && Stocks.Count == 0)
            {
                Blocked("Nothing to export — add pieces, select job orders and run a cut first.", "Nothing to export");
                return;
            }

            if (!TryPassExportGate("the combined export")) return;

            var rows = new List<OptimizationReportExport.CombinedRow>();

            // ── 1. RUN SUMMARY — headline numbers, and which SETTINGS level
            //       produced this plan, so a level-2 plan is never read as a
            //       level-5 one.
            rows.Add(new OptimizationReportExport.CombinedRow
            {
                Section = "Run Summary", GlassSheet = "Stage", SizeThickness = ResultStage
            });
            rows.Add(new OptimizationReportExport.CombinedRow
            {
                Section = "Run Summary", GlassSheet = "Optimization level",
                SizeThickness = ResultEffortLabel
            });
            rows.Add(new OptimizationReportExport.CombinedRow
            {
                Section = "Run Summary", GlassSheet = "Sheets",
                SizeThickness = ResultSheets, UtilArea = ResultUtilization
            });
            rows.Add(new OptimizationReportExport.CombinedRow
            {
                Section = "Run Summary", GlassSheet = "Wastage",
                SizeThickness = ResultWaste, UtilArea = ResultWastePct
            });
            rows.Add(new OptimizationReportExport.CombinedRow
            {
                Section = "Run Summary", GlassSheet = "Unplaced", SizeThickness = ResultUnplaced
            });
            rows.Add(new OptimizationReportExport.CombinedRow
            {
                Section = "Run Summary", GlassSheet = "Oracle", SizeThickness = ResultOracle
            });
            if (HasUpgradeSummary)
                rows.Add(new OptimizationReportExport.CombinedRow
                {
                    Section = "Run Summary", GlassSheet = "Upgrade", SizeThickness = UpgradeSummaryLabel
                });
            if (HasCostingTotal)
                rows.Add(new OptimizationReportExport.CombinedRow
                {
                    Section = "Run Summary", GlassSheet = "Plan total", SizeThickness = CostingTotalLabel
                });
            if (HasCostingPriceNote)
                rows.Add(new OptimizationReportExport.CombinedRow
                {
                    Section = "Run Summary", GlassSheet = "Pricing note", SizeThickness = CostingPriceNote
                });

            // ── 2. OPTIMIZATION COSTING — sheet size, sheet qty, utilization
            //       and the Step 4 rate, with the money split into the glass we
            //       kept and the glass we threw away.
            foreach (var c in CostingRows)
                rows.Add(new OptimizationReportExport.CombinedRow
                {
                    Section = "Optimization Costing",
                    GlassSheet = c.Composition,
                    SizeThickness = c.SheetSize,
                    Pieces = c.PieceQty,
                    UtilArea = c.Utilization,
                    OffcutUsed = c.WasteArea + " m²",
                    Source = string.IsNullOrWhiteSpace(c.PaneLabel) || c.PaneLabel == "—"
                        ? c.Thickness
                        : c.Thickness + " · " + c.PaneLabel,
                    SheetQty = c.SheetQty,
                    RateLabel = c.RateLabel,
                    GlassCostLabel = c.GlassCostLabel,
                    WasteCostLabel = c.WasteCostLabel,
                    TotalCostLabel = c.TotalCostLabel,
                    SheetQtyValue = c.SheetCount,
                    Rate = c.HasPrice ? c.Rate : (decimal?)null,
                    GlassCost = c.HasPrice ? c.GlassCost : (decimal?)null,
                    WasteCost = c.HasPrice ? c.WasteCost : (decimal?)null,
                    TotalCost = c.HasPrice ? c.TotalCost : (decimal?)null
                });

            // ── 3. INDUSTRIAL CUT REPORT — the owner's fixed external format,
            //       now a section of the same document rather than a separate
            //       download.
            foreach (var spec in IndustrialReport)
            {
                foreach (var line in spec.Utilization)
                    rows.Add(new OptimizationReportExport.CombinedRow
                    {
                        Section = "Industrial · " + spec.Composition,
                        SizeThickness = line.Line
                    });

                foreach (var piece in spec.Pieces)
                    rows.Add(new OptimizationReportExport.CombinedRow
                    {
                        Section = "Industrial · " + spec.Composition,
                        GlassSheet = piece.GlassRef,
                        SizeThickness = piece.W1 + "×" + piece.H1 +
                            (string.IsNullOrWhiteSpace(piece.W2)
                                ? string.Empty
                                : " / " + piece.W2 + "×" + piece.H2),
                        Pieces = piece.Qty,
                        UtilArea = piece.TotalSqm + " m²"
                    });
            }

            // ── 4/5/6. the three session summaries, unchanged.
            foreach (var r in SheetRows)
                rows.Add(new OptimizationReportExport.CombinedRow
                {
                    Section = "Layout Summary",
                    GlassSheet = r.Composition,
                    SizeThickness = r.StockLabel,
                    Pieces = r.PieceCount,
                    UtilArea = r.Utilization,
                    OffcutUsed = r.WasteLabel,
                    Source = "Sheet " + r.SheetNo
                });

            foreach (var g in SizeGroups)
                rows.Add(new OptimizationReportExport.CombinedRow
                {
                    Section = "Parts Summary",
                    GlassSheet = g.Composition,
                    SizeThickness = g.Thickness,
                    Pieces = g.PieceCount,
                    UtilArea = g.AreaM2 + " m²"
                });

            if (AllocationRows.Count == 0)
                RebuildAllocationRows();
            foreach (var st in AllocationRows)
                rows.Add(new OptimizationReportExport.CombinedRow
                {
                    Section = "Stocks Summary",
                    GlassSheet = st.ColorLabel,
                    SizeThickness = st.SizeLabel,
                    OffcutUsed = st.UsedLabel,
                    Source = st.SourceLabel
                });

            if (rows.Count == 0)
            {
                Blocked("Nothing to export — the session summaries are empty.", "Nothing to export");
                return;
            }

            AsyncUtility.SafeFireAndForget(
                async () => await OptimizationReportExport.RunCombinedAsync(format, rows),
                "Optimization full report export");
        }


        // ── industrial report build ──────────────────────────────────

        /// <summary>
        /// Rebuilds <see cref="IndustrialReport"/> from the current cutting
        /// list (piece tables) and the last run (utilization lines). One
        /// group per glass composition, ordered like the SIZES merge.
        /// Composition text is never shown empty — unlabelled glass reads
        /// "(unlabelled glass)".
        /// </summary>
        private void RebuildIndustrialReport()
        {
            RebuildAllocationRows();
            IndustrialReport.Clear();
            IndustrialReportBands.Clear();

            foreach (var grp in Pieces
                .Where(p => p.TryParse(out _, out _, out _))
                .GroupBy(p => (p.GlassRef ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                string composition = string.IsNullOrWhiteSpace(grp.Key)
                    ? UnlabelledGlass
                    : grp.Key;

                var spec = new IndustrialReportSpecVm
                {
                    Composition = composition,
                    ThicknessLabel = FormatThicknessLabel(composition),
                    SpecName = FormatSpecName(composition),
                    ColorLabel = FormatColorLabel(grp),
                    OuterColor = FirstColor(grp, IsFirstPane),
                    InnerColor = FirstColor(grp, IsSecondPane),
                    SpecGroupNumber = grp.Select(p => p.SpecGroupNumber).FirstOrDefault(n => n > 0),
                    Side = DominantSide(grp)
                };

                // ── piece table (Step 5 style): one row per glass ref,
                //    outer/inner pane dimensions re-joined from the list ──
                foreach (var refGroup in grp
                    .GroupBy(p => (p.PieceLabel ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    var outer = refGroup.FirstOrDefault(p => IsFirstPane(p.Position));
                    var inner = refGroup.FirstOrDefault(p => IsSecondPane(p.Position));

                    int w1 = 0, h1 = 0, w2 = 0, h2 = 0, qty = 0;
                    // Outer/Glass pane → W1/H1; Inner/Lam Inner pane → W2/H2
                    // (the Step 5 column semantics: W1/H1 = first pane, W2/H2
                    // = second pane). A ref carrying both panes fills both.
                    if (outer != null && outer.TryParse(out int ow, out int oh, out int oq)) { w1 = ow; h1 = oh; qty = oq; }
                    if (inner != null && inner.TryParse(out int iw, out int ih, out int iq)) { w2 = iw; h2 = ih; if (qty == 0) qty = iq; }

                    double unitMm2 = w1 > 0 && h1 > 0 ? (double)w1 * h1 : (double)w2 * h2;
                    double totalSqm = unitMm2 / 1_000_000.0 * qty;

                    spec.Pieces.Add(new IndustrialReportPieceVm
                    {
                        GlassRef = string.IsNullOrWhiteSpace(refGroup.Key) ? "—" : refGroup.Key,
                        W1 = w1 > 0 ? w1.ToString(CultureInfo.InvariantCulture) : string.Empty,
                        H1 = h1 > 0 ? h1.ToString(CultureInfo.InvariantCulture) : string.Empty,
                        W2 = w2 > 0 ? w2.ToString(CultureInfo.InvariantCulture) : string.Empty,
                        H2 = h2 > 0 ? h2.ToString(CultureInfo.InvariantCulture) : string.Empty,
                        Qty = qty.ToString(CultureInfo.InvariantCulture),
                        TotalSqm = totalSqm.ToString("0.000", CultureInfo.InvariantCulture)
                    });
                }

                // ── utilization lines: one per sheet size the group cut on ──
                var run = _groupRuns.FirstOrDefault(r =>
                    string.Equals((r.Composition ?? string.Empty).Trim(), composition, StringComparison.OrdinalIgnoreCase));
                if (run != null)
                {
                    string position = PositionsLabel(grp);
                    foreach (var sheetGroup in run.Result.Sheets
                        .GroupBy(s => (s.Stock.WidthMm, s.Stock.HeightMm)))
                    {
                        double glass = sheetGroup.Sum(s => s.Pieces.Sum(p => (long)p.WidthMm * p.HeightMm));
                        double stockArea = sheetGroup.Sum(s => (long)s.Stock.WidthMm * s.Stock.HeightMm);
                        double util = stockArea == 0 ? 0.0 : glass / stockArea * 100.0;

                        spec.Utilization.Add(new IndustrialUtilizationLineVm
                        {
                            Line = string.Format(CultureInfo.InvariantCulture,
                                "{0} THICKNESS {1}(SPEC) {2} * {3} SHEET UTILIZED QTY {4} (SHEET SIZE) - {5}% UTILIZATION {6}",
                                spec.ThicknessLabel,
                                spec.SpecName,
                                sheetGroup.Key.WidthMm,
                                sheetGroup.Key.HeightMm,
                                sheetGroup.Count(),
                                util.ToString("0.00", CultureInfo.InvariantCulture),
                                position).Trim(),
                            UtilizationPct = util,
                            Tone = UtilizationTone(util)
                        });
                    }
                }

                IndustrialReport.Add(spec);
            }

            PairIndustrialBands();
            HasIndustrialReport = IndustrialReport.Count > 0;
        }

        /// <summary>
        /// Outer left, inner right, one row. Same spec group only — two
        /// unrelated colours are not forced into a pair. A spec that has
        /// no partner stays a single full-width column.
        /// </summary>
        private void PairIndustrialBands()
        {
            var used = new HashSet<IndustrialReportSpecVm>();
            foreach (var spec in IndustrialReport)
            {
                if (!used.Add(spec)) continue;

                IndustrialReportSpecVm? partner = null;
                if (spec.SpecGroupNumber > 0)
                {
                    bool specInner = IsSecondPane(spec.Side);
                    partner = IndustrialReport.FirstOrDefault(other =>
                        other != spec
                        && !used.Contains(other)
                        && other.SpecGroupNumber == spec.SpecGroupNumber
                        && (specInner ? IsFirstPane(other.Side) : IsSecondPane(other.Side)));
                }

                if (partner != null)
                    used.Add(partner);

                var outer = spec;
                var inner = partner;
                if (partner != null && IsSecondPane(spec.Side) && IsFirstPane(partner.Side))
                {
                    outer = partner;
                    inner = spec;
                }
                else if (partner != null && IsSecondPane(spec.Side) && !IsFirstPane(partner.Side))
                {
                    outer = partner;
                    inner = spec;
                }

                IndustrialReportBands.Add(new IndustrialReportBandVm
                {
                    Outer = outer,
                    Inner = inner,
                });
            }
        }

        private static string FirstColor(IEnumerable<PieceEntryVm> pieces, Func<string?, bool> side)
        {
            string color = pieces
                .Where(p => side(p.Position))
                .Select(p => (p.Color ?? string.Empty).Trim())
                .FirstOrDefault(c => c.Length > 0) ?? string.Empty;
            return color.Length == 0 ? string.Empty : color.ToUpperInvariant();
        }

        private static string FormatColorLabel(IEnumerable<PieceEntryVm> pieces)
        {
            string color = pieces
                .Select(p => (p.Color ?? string.Empty).Trim())
                .FirstOrDefault(c => c.Length > 0) ?? string.Empty;
            return color.Length == 0 ? string.Empty : color.ToUpperInvariant();
        }

        private static string DominantSide(IEnumerable<PieceEntryVm> pieces)
        {
            if (pieces.Any(p => IsFirstPane(p.Position))) return "Outer";
            if (pieces.Any(p => IsSecondPane(p.Position))) return "Inner";
            return string.Empty;
        }

        // ── formatting helpers ───────────────────────────────────────

        /// <summary>Leading thickness of a composition, e.g. "6mm clear …" → "6MM".</summary>
        private static string FormatThicknessLabel(string? composition)
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                composition ?? string.Empty, @"^\s*(\d+(?:\.\d+)?)\s*mm");
            return m.Success ? m.Groups[1].Value.ToUpperInvariant() + "MM" : string.Empty;
        }

        /// <summary>Owner-style spec name from a composition: strips the
        /// leading thickness and trailing "ft glass"/"glass", uppercases —
        /// "6mm climaguard hp neutral 70 low e ft glass" → "CLIMAGUARD HP
        /// NEUTRAL 70 LOW E".</summary>
        private static string FormatSpecName(string? composition)
        {
            if (string.IsNullOrWhiteSpace(composition)) return string.Empty;
            string s = System.Text.RegularExpressions.Regex.Replace(
                composition, @"^\s*\d+(?:\.\d+)?\s*mm\s*", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            s = System.Text.RegularExpressions.Regex.Replace(
                s, @"\s+(ft\s+)?glass\s*$", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            s = s.Trim().ToUpperInvariant();
            return s.Length == 0 ? "(UNLABELLED GLASS)" : s;
        }

        private static bool IsFirstPane(string? position) =>
            string.Equals(position, "Outer", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(position, "Glass", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(position, "Lam Outer", StringComparison.OrdinalIgnoreCase);

        private static bool IsSecondPane(string? position) =>
            string.Equals(position, "Inner", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(position, "Lam Inner", StringComparison.OrdinalIgnoreCase);

        /// <summary>Pane positions present in a composition's pieces, e.g.
        /// "OUTER" / "INNER" / "OUTER/INNER" (empty when none are tagged).</summary>
        private static string PositionsLabel(IEnumerable<PieceEntryVm> pieces)
        {
            var positions = pieces
                .Select(p => p.Position ?? string.Empty)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return positions.Count == 0 ? string.Empty : string.Join("/", positions);
        }

        /// <summary>Colour-code band: green ≥85%, amber 70–84%, red &lt;70%.</summary>
        private static string UtilizationTone(double util) =>
            util >= 85.0 ? "Good" : util >= 70.0 ? "Mid" : "Low";

        // ── CSV layout for the owner-format industrial report ───────

        /// <summary>
        /// Writes the industrial report exactly like the owner prints it:
        /// per glass composition, the utilization line(s), then the
        /// GLASS REF / W1 / H1 / W2 / H2 / QTY / TOTAL SQM table, then a
        /// blank row before the next composition. Uses the shared CSV
        /// engine (UTF-8 BOM, formula-injection safe).
        /// </summary>
        private sealed class IndustrialReportCsvLayout : ICsvLayout
        {
            private readonly List<IndustrialReportSpecVm> _specs;

            public IndustrialReportCsvLayout(List<IndustrialReportSpecVm> specs) => _specs = specs;

            public string DocumentPrefix => "OPT";
            public string DocumentId => "OPTIMIZATION-INDUSTRIAL-CUT-REPORT";
            public string DocumentTitle => "Optimization — Industrial Cut Report";
            public string CompanyName => "ProGlass";
            public string Reference => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            public DateTime DocumentDate => DateTime.Now;

            public void WriteRows(CsvWriter csv)
            {
                foreach (var spec in _specs)
                {
                    // Utilization line(s) — one per sheet size.
                    foreach (var line in spec.Utilization)
                    {
                        csv.WriteField(line.Line);
                        csv.NextRecord();
                    }
                    if (spec.Utilization.Count == 0)
                    {
                        csv.WriteField(spec.Composition.ToUpperInvariant());
                        csv.NextRecord();
                    }

                    // Piece table header.
                    csv.WriteField("GLASS REF");
                    csv.WriteField("W1");
                    csv.WriteField("H1");
                    csv.WriteField("W2");
                    csv.WriteField("H2");
                    csv.WriteField("QTY");
                    csv.WriteField("TOTAL SQM");
                    csv.NextRecord();

                    foreach (var p in spec.Pieces)
                    {
                        csv.WriteField(p.GlassRef);
                        csv.WriteField(p.W1);
                        csv.WriteField(p.H1);
                        csv.WriteField(p.W2);
                        csv.WriteField(p.H2);
                        csv.WriteField(p.Qty);
                        csv.WriteField(p.TotalSqm);
                        csv.NextRecord();
                    }

                    csv.NextRecord(); // blank row between compositions
                }
            }
        }
    }
}
