// ═══════════════════════════════════════════════════════════════
// FILE: OptimizationViewModel.Parts.cs
// MODULE: Optimization — PARTS — cutting list (piece entries) and the derived summaries/KPIs (partial of OptimizationViewModel)
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

        public sealed class PieceEntryVm : ViewModels.ViewModelBase
        {
            private string _glassRef = "6mm clear ft glass";
            private string _pieceLabel = string.Empty;
            private string _position = string.Empty;
            private string _widthText = string.Empty;
            private string _heightText = string.Empty;
            private string _qtyText = "1";
            private bool _rotationLocked;

            /// <summary>Raised when an editable field changes (VM refreshes
            /// the summary, merge groups and cut plan).</summary>
            public Action? OnEdited { get; set; }

            public string GlassRef { get => _glassRef; set { _glassRef = value; OnPropertyChanged(); } }
            public string PieceLabel { get => _pieceLabel; set { _pieceLabel = value; OnPropertyChanged(); } }
            /// <summary>Pane position provenance — "Outer", "Inner", … ("" for ad-hoc pieces).</summary>
            public string Position { get => _position; set { _position = value; OnPropertyChanged(); } }
            public string WidthText { get => _widthText; set { _widthText = value; OnPropertyChanged(); OnEdited?.Invoke(); } }
            public string HeightText { get => _heightText; set { _heightText = value; OnPropertyChanged(); OnEdited?.Invoke(); } }
            public string QtyText { get => _qtyText; set { _qtyText = value; OnPropertyChanged(); OnEdited?.Invoke(); } }

            /// <summary>
            /// PHASE 8 / finding 8e (2026-09-20): per-piece rotation lock.
            /// <c>CutPiece.AllowRotate</c> has been honoured by the packer all
            /// along — <c>Orientations</c>, the smart-placement candidate filter and
            /// the placement verifier all test it — but <c>TryBuildInputs</c> wrote
            /// <c>AllowRotate = RotationEnabled</c> for every row, so the only
            /// choice on offer was all-or-nothing. This surfaces the per-piece flag
            /// that already existed underneath it.
            /// Default false keeps today's behaviour exactly: rotation follows the
            /// SETTINGS choice. Locking a row removes its 90° candidate only, which
            /// is what an operator needs for a pane whose edge work, pattern,
            /// coating direction or tempering must not be turned.
            /// </summary>
            public bool RotationLocked { get => _rotationLocked; set { _rotationLocked = value; OnPropertyChanged(); OnEdited?.Invoke(); } }

            // Canonical identity — copied from PI/JO/Step 5, never invented.
            public string ProformaId { get; set; } = "";
            public string JobOrderId { get; set; } = "";
            public int SpecGroupNumber { get; set; }
            public int SourceRowSerial { get; set; }
            public string GlassType { get; set; } = "";
            public string Color { get; set; } = "";
            public int ThicknessMm { get; set; }
            public string Processing { get; set; } = "";
            public string Coating { get; set; } = "";
            public int W1 { get; set; }
            public int H1 { get; set; }
            public int W2 { get; set; }
            public int H2 { get; set; }
            public string SheetStoreSku { get; set; } = "";
            public int SheetWidthMm { get; set; }
            public int SheetHeightMm { get; set; }
            public decimal SheetPricePerSqm { get; set; }
            public string MaterialKey { get; set; } = "";

            public void EnsureMaterialKey()
            {
                if (!string.IsNullOrWhiteSpace(MaterialKey)) return;
                MaterialKey = OptimizationMaterialIdentity.BuildKey(
                    GlassType, Color, ThicknessMm, Processing, Coating);
            }

            public bool TryParse(out int width, out int height, out int qty)
            {
                width = height = qty = 0;
                return ParsePositiveInt(WidthText, out width)
                    && ParsePositiveInt(HeightText, out height)
                    && ParsePositiveInt(QtyText, out qty);
            }

            /// <summary>
            /// Positive-integer parsing that also accepts integer-valued
            /// decimals: "12" and "12.0" parse, "12.5" and "0" do not.
            /// The app formats sizes with invariant decimals in places, and
            /// an operator who types a half-millimetre must get a clean
            /// validation message — not a row that dies later inside an
            /// unguarded Convert.ToInt32.
            /// </summary>
            private static bool ParsePositiveInt(string? text, out int value)
            {
                value = 0;
                if (string.IsNullOrWhiteSpace(text)) return false;
                text = text.Trim();

                // Match the source branch's integer import path: assign the OUT
                // value, not a temporary. Returning true with value still zero
                // erased W/H/Qty in SIZES and in the engine's input expansion.
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    return value > 0;

                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                {
                    if (d > 0 && d == Math.Floor(d) && d <= int.MaxValue)
                    {
                        value = (int)d;
                        return true;
                    }
                }
                return false;
            }
        }

        public ObservableCollection<PieceEntryVm> Pieces { get; } = new();

        private PieceEntryVm _newPiece = new();
        public PieceEntryVm NewPiece { get => _newPiece; private set { _newPiece = value; OnPropertyChanged(); } }

        public ICommand AddPieceCommand { get; }
        public ICommand RemovePieceCommand { get; }
        public ICommand ClearPiecesCommand { get; }
        public ICommand LoadReferenceJobCommand { get; }

        private string _pieceSummary = "No pieces yet";
        public string PieceSummary { get => _pieceSummary; private set { _pieceSummary = value; OnPropertyChanged(); } }

        // ── piece list ops ─────────────────────────────────────────────

        /// <summary>Creates a list piece with the edit callback wired so the
        /// summary / merge groups / cut plan refresh on inline qty edits.</summary>
        private PieceEntryVm NewPieceRow(string glass, string label, string w, string h, string qty)
        {
            var row = new PieceEntryVm
            {
                GlassRef = glass,
                PieceLabel = label,
                WidthText = w,
                HeightText = h,
                QtyText = qty
            };
            row.OnEdited = () => { InvalidateInputs(); RefreshDerived(); };
            return row;
        }

        private void AddPiece()
        {
            if (!NewPiece.TryParse(out _, out _, out _))
            {
                Blocked("Piece needs a label, positive W×H (mm) and qty ≥ 1.", "Piece not added");
                return;
            }

            Pieces.Add(NewPieceRow(
                NewPiece.GlassRef?.Trim() ?? string.Empty,
                NewPiece.PieceLabel?.Trim() ?? string.Empty,
                NewPiece.WidthText, NewPiece.HeightText, NewPiece.QtyText));
            NewPiece = new PieceEntryVm { GlassRef = NewPiece.GlassRef, PieceLabel = string.Empty };
            RunMessage = string.Empty;
            RefreshDerived();
        }

        private void RemovePiece(PieceEntryVm entry)
        {
            if (entry != null) Pieces.Remove(entry);
            RefreshDerived();
        }

        /// <summary>
        /// Formerly loaded a 152-piece hardcoded calibration DGU job.
        /// REMOVED 2026-09-21 per owner request: optimization must no longer
        /// invent cutting data. All pieces now come ONLY from an approved
        /// Proforma Invoice via its confirmed Job Order (Step 5 W1/H1/W2/H2/Qty
        /// and Step 4 sheet sizes). No fallback rack is injected either —
        /// Stocks are populated from the selected JO's Step 4 sheets.
        /// Never throws: any failure surfaces as a status line.
        /// </summary>
        private void LoadReferenceJob()
        {
            // Hardcoded calibration data (152 pcs: 76 low-e outer / 64 clear inner / 12 ramly)
            // has been removed. Operator must select a confirmed Job Order
            // derived from an approved PI to populate Pieces/Stocks.
            Pieces.Clear();
            Stocks.Clear();
            StocksStatusText = "Select a confirmed Job Order (approved PI) to load racks.";
            RunMessage = "Reference calibration job removed — select a confirmed Job Order (from an approved PI) on the left. Its Step 5 cutting sizes (W1/H1/W2/H2/Qty) and Step 4 sheets will populate SIZES/STOCKS automatically.";
            RefreshDerived();
        }

        private void RefreshDerived()
        {
            int count = 0;
            double area = 0;
            foreach (var p in Pieces)
            {
                if (!p.TryParse(out int w, out int h, out int q)) continue;
                count += q;
                area += w / 1000.0 * (h / 1000.0) * q;
            }

            PieceSummary = count == 0
                ? "No pieces yet"
                : string.Format(CultureInfo.InvariantCulture,
                    "{0} pieces · {1:0.00} m² glass", count, area);

            HasPieces = count > 0;

            RebuildCuttingGroups();
            SyncThicknessFromInputs();
            RefreshSizeViews();
            RefreshKpis();
            RebuildIndustrialReport();
        }

        private void RefreshSizeViews()
        {
            // Merge groups carry ONLY their own pcs + area (low-e 76, clear
            // 64, ramly 12) — and reflect the cutting-group selector, so
            // picking "low-e outer" shows that group alone.
            SizeGroups.Clear();
            foreach (var grp in Pieces
                .Where(p => p.TryParse(out _, out _, out _))
                .GroupBy(p => (p.GlassRef ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!SelectedGroupMatches(grp.Key)) continue;

                int gCount = 0;
                int gRows = 0;
                double gArea = 0;
                var gSizes = new HashSet<(int W, int H)>();
                foreach (var p in grp)
                {
                    p.TryParse(out int w, out int h, out int q);
                    gCount += q;
                    gArea += w / 1000.0 * (h / 1000.0) * q;
                    gSizes.Add((w, h));
                }
                gRows = gSizes.Count;
                SizeGroups.Add(new SizeGroupVm
                {
                    Composition = grp.Key,
                    Thickness = ParseThicknessLabel(grp.Key),
                    PieceCount = gCount.ToString(CultureInfo.InvariantCulture),
                    SizeRows = gRows.ToString(CultureInfo.InvariantCulture),
                    AreaM2 = gArea.ToString("0.00", CultureInfo.InvariantCulture)
                });
            }

            RebuildCutPlan();
        }

        /// <summary>
        /// Recomputes the four dusk-navy KPI cards: confirmed jobs, pieces,
        /// total glass area and cutting groups (the SIZES selector minus the
        /// "ALL" sentinel).
        /// </summary>
        private void RefreshKpis()
        {
            KpiJobs = AvailableJobs.Count.ToString(CultureInfo.InvariantCulture);

            int pcs = 0;
            double area = 0;
            foreach (var p in Pieces)
            {
                if (!p.TryParse(out int w, out int h, out int q)) continue;
                pcs += q;
                area += w / 1000.0 * (h / 1000.0) * q;
            }
            KpiPieces = pcs.ToString(CultureInfo.InvariantCulture);
            KpiGlass = area.ToString("0.0", CultureInfo.InvariantCulture) + " m²";

            int groups = System.Math.Max(0, CuttingGroups.Count - 1); // exclude "ALL — every glass group"
            KpiGroups = groups.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>True when the SIZES selector is ALL or equals the composition.</summary>
    }
}
