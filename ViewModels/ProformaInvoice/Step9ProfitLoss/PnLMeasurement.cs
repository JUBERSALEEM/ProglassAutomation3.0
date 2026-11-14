// ═══════════════════════════════════════════════════════════════════════
//  ViewModels/ProformaInvoice/Step9ProfitLoss/PnLMeasurement.cs
//
//  Pure static measurement/description helpers for the Step 9 Profit & Loss
//  builder. Extracted from ProfitLossStepViewModel (1924 lines) so the row
//  construction stays in the VM while the measurement math lives here.
//  All methods are static and side-effect free.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Utilities;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid;
using System;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss
{
    /// <summary>
    /// Pure measurement helpers for building Step 9 Profit &amp; Loss rows.
    /// </summary>
    public static class PnLMeasurement
    {
        /// <summary>
        /// Treats a non-positive yield factor as 100% (i.e. 1m), matching the
        /// prior behaviour: a 0/negative factor must not divide or blow up.
        /// </summary>
        public static decimal SafeYield(decimal yieldFactor) =>
            yieldFactor <= 0m ? 1m : yieldFactor;

        /// <summary>
        /// Totals billable cut area (SQM) and perimeter (linear metres) for
        /// every priced row in a spec group. Step 5 charges every panel with
        /// the 0.5 m² minimum floor, so Step 9 must use the same billable
        /// SQM source of truth instead of raw width × height / 1,000,000.
        /// </summary>
        public static (decimal CutSqm, decimal PerimeterLm) MeasureGroup(
            SpecGroup group)
        {
            decimal cutSqm = 0m;
            decimal perimeterLm = 0m;

            var rows = group.ItemRows;
            if (rows == null) return (0m, 0m);

            foreach (var row in rows)
            {
                if (row == null) continue;

                int qty = row.Qty > 0 ? row.Qty : 0;
                if (qty == 0) continue;

                // Step 5's SQM boxes use CalculatePanel(), including the
                // UAE glass-industry 0.5 m² minimum per panel. P&L charges
                // (glass, spacer, production) must reconcile to those same
                // billable Step 5 SQM values, not raw physical cut area.
                decimal area = SqmCalculator.CalculatePanel(row.W1, row.H1);

                if (row.W2 > 0 && row.H2 > 0)
                    area += SqmCalculator.CalculatePanel(row.W2, row.H2);

                cutSqm += area * qty;

                perimeterLm += (2m * (row.W1 + row.H1) / 1000m) * qty;
            }

            return (cutSqm, perimeterLm);
        }

        /// <summary>
        /// Required glass area after wastage is applied. This is the SGU
        /// costing basis requested by the business: price the glass by SQM,
        /// then add cutting and tempering as separate SQM-based rows. When
        /// the operator has set a SHEET QTY on the matching Step 6 wastage
        /// row, that row's quantity wins over this SQM estimate (see
        /// ProfitLossStepViewModel.BuildGlassRowAsync).
        /// </summary>
        public static decimal AreaWithWastage(decimal cutSqm, decimal wastagePercent)
        {
            if (cutSqm <= 0m) return 0m;
            decimal wasteFactor = 1m + Math.Max(0m, wastagePercent) / 100m;
            return Math.Round(cutSqm * wasteFactor, 4);
        }

        /// <summary>
        /// Whole sheets required for a pane when a build is costed against
        /// actual stock-sheet consumption rather than required SQM.
        /// </summary>
        public static int WholeSheetsRequired(decimal requiredSqm, decimal sheetSqm)
        {
            if (requiredSqm <= 0m || sheetSqm <= 0m) return 0;
            int sheets = (int)Math.Ceiling(requiredSqm / sheetSqm);
            return sheets < 1 ? 1 : sheets;
        }

        /// <summary>
        /// Builds the detail half of a P&amp;L row.
        ///
        /// The full specification is carried separately on
        /// <see cref="ProfitLossRow.SpecName"/> and rendered in the SPEC
        /// column, so it deliberately is NOT repeated here — printing it in
        /// both places made every row unreadably long.
        /// </summary>
        public static string BuildRowDescription(
            SpecGroup group, string part, string detail)
        {
            return string.IsNullOrWhiteSpace(detail) ? part : $"{part} · {detail}";
        }
    }
}
