// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.a.3.GlassMeasure.cs
// MODULE: step6 — billable glass area and edge length, in one place.
//
// UAE trade rule carried over from the reference implementation: a panel is
// billed at a MINIMUM of 0.5 m². Without it a 200 × 300 mm pane costs the same
// as a 1 × 1 m pane on the quotation and the shop loses money silently.
// Step 5 (the grid), Step 6 (perimeter charges) and Step 9 (the P&L) all call
// this class, so a quoted area and a costed area cannot disagree.
// ═══════════════════════════════════════════════════════════════════════

using System;

namespace ProGlassAutomation.Models.Proforma
{
    /// <summary>Areas (m²) and edges (linear metres) for glass panels.</summary>
    public static class GlassMeasure
    {
        /// <summary>Minimum billable area for any one panel, in m².</summary>
        public const decimal MinimumPanelSqm = 0.5m;

        /// <summary>One panel, floored at the billable minimum, 3 decimals.</summary>
        public static decimal PanelSqm(int widthMm, int heightMm)
        {
            if (widthMm <= 0 || heightMm <= 0) return 0m;
            decimal actual = Math.Round((decimal)widthMm * heightMm / 1_000_000m, 3);
            return actual < MinimumPanelSqm ? MinimumPanelSqm : actual;
        }

        /// <summary>Panel area × quantity, 3 decimals. Zero for a non-positive quantity.</summary>
        public static decimal TotalSqm(int widthMm, int heightMm, int quantity)
        {
            if (quantity <= 0) return 0m;
            return Math.Round(PanelSqm(widthMm, heightMm) * quantity, 3);
        }

        /// <summary>
        /// Physical area of one STOCK SHEET, 4 decimals. A sheet is inventory, not
        /// a supply, so it has no billable floor.
        /// </summary>
        public static decimal SheetSqm(int widthMm, int heightMm)
        {
            if (widthMm <= 0 || heightMm <= 0) return 0m;
            return Math.Round((decimal)widthMm * heightMm / 1_000_000m, 4);
        }

        /// <summary>Perimeter of one panel in linear metres.</summary>
        public static decimal PanelLm(int widthMm, int heightMm)
        {
            if (widthMm <= 0 || heightMm <= 0) return 0m;
            return 2m * (widthMm + heightMm) / 1000m;
        }

        /// <summary>Perimeter × quantity, 3 decimals.</summary>
        public static decimal TotalLm(int widthMm, int heightMm, int quantity)
        {
            if (quantity <= 0) return 0m;
            return Math.Round(PanelLm(widthMm, heightMm) * quantity, 3);
        }

        /// <summary>
        /// Ceiling of required area over one sheet's area. Never returns 0 for a
        /// positive requirement: half a sheet is a sheet the shop must buy.
        /// </summary>
        public static int SheetsRequired(decimal requiredSqm, decimal sheetSqm)
        {
            if (requiredSqm <= 0m || sheetSqm <= 0m) return 0;
            int sheets = (int)Math.Ceiling(requiredSqm / sheetSqm);
            return sheets < 1 ? 1 : sheets;
        }

        /// <summary>Area grown by a wastage allowance, 4 decimals.</summary>
        public static decimal WithWastage(decimal cutSqm, decimal wastagePercent)
        {
            if (cutSqm <= 0m) return 0m;
            decimal waste = MoneyRounding.ClampPercent(wastagePercent, 0m, 95m);
            return Math.Round(cutSqm * (1m + waste / 100m), 4);
        }
    }
}
