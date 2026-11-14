// ═══════════════════════════════════════════════════════════════════════
//  FILE: Models/Specifications/SpecPane.cs
//  PROJECT: ProGlassAutomation
//  CREATED: 2026-09-02 — per-pane sheet sizes in the Specifications Center
//
//  WHY THIS EXISTS
//  ---------------------------------------------------------------------
//  A specification is not one piece of glass. "6mm HD Grey FT Glass +
//  12mm ASP + 6mm Clear FT Glass" is TWO panes, and each is cut from its
//  own Sheet Store sheet — HD Grey might only come as 3210 × 2250 while
//  Clear is stocked as 3660 × 2440.
//
//  SavedSpecification carries a single SheetWidthMm/SheetHeightMm pair,
//  which could only ever describe the OUTER pane. That is why the detail
//  pane showed one size (or none) instead of a size per glass: the inner
//  pane was never captured at all.
//
//  Rather than bolt Inner* columns onto the table — which would break
//  again the moment a DGU+LAM needs three or four panes — the panes are
//  stored as a JSON list in one column. The shape is open-ended, so a
//  laminated triple works without another migration.
// ═══════════════════════════════════════════════════════════════════════

using System;

namespace ProGlassAutomation.Models.ProformaInvoice.Specifications
{
    /// <summary>
    /// One glass leaf within a specification, with the Sheet Store sheet
    /// it was matched to.
    /// </summary>
    public class SpecPane
    {
        /// <summary>"Outer", "Inner", "Lam Outer", "Lam Inner".</summary>
        public string Position { get; set; } = "";

        /// <summary>Colour as it appears in Sheet Store, e.g. "HD Grey".</summary>
        public string Color { get; set; } = "";

        public int ThicknessMm { get; set; }

        /// <summary>Matched Sheet Store size. 0 when nothing matched.</summary>
        public int SheetWidthMm { get; set; }
        public int SheetHeightMm { get; set; }

        // ── Display helpers ──────────────────────────────────────────────

        public bool HasSheet => SheetWidthMm > 0 && SheetHeightMm > 0;

        /// <summary>"Glass — HD Grey" style label for the detail pane.</summary>
        public string GlassLabel =>
            string.IsNullOrWhiteSpace(Color) ? "Glass" : $"Glass — {Color}";

        /// <summary>"6mm" or blank when the thickness is unknown.</summary>
        public string ThicknessDisplay =>
            ThicknessMm > 0 ? $"{ThicknessMm}mm" : "";

        public string WidthDisplay => SheetWidthMm > 0 ? SheetWidthMm.ToString() : "—";
        public string HeightDisplay => SheetHeightMm > 0 ? SheetHeightMm.ToString() : "—";

        /// <summary>"3210 × 2250", or an explicit miss.</summary>
        public string SheetSizeDisplay =>
            HasSheet ? $"{SheetWidthMm} × {SheetHeightMm}" : "No Sheet Store match";

        public decimal SheetSqm =>
            HasSheet
                ? Utilities.SqmCalculator.CalculateSheetArea(SheetWidthMm, SheetHeightMm)
                : 0m;
    }
}
