// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step9ProfitLoss/ProfitLossRow.cs
// PROJECT: ProGlassAutomation
// MODULE: Proforma Invoice — Step 9 Profit & Loss
//
// ProfitLossRow extracted from the large ProfitLossStepViewModel file (Phase 11/12
// God-VM split) — behaviour unchanged, relocated for organisation only.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Commands;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.ProformaInvoice;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.DguLamParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.DguParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.LamParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.SguParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss
{
    public class ProfitLossRow : ViewModelBase
    {
        /// <summary>"Glass", "Spacer" or "Production".</summary>
        public string Kind { get; set; } = "";

        /// <summary>Step 5 spec group this line came from.</summary>
        public int SpecNumber { get; set; }

        public string Description { get; set; } = "";

        public int SheetWidthMm { get; set; }
        public int SheetHeightMm { get; set; }
        public decimal SheetSqm { get; set; }

        /// <summary>
        /// Costing quantity for the row:
        ///   • whole sheets for multi-pane glass rows priced by stock-sheet use,
        ///   • required SQM for SGU glass rows,
        ///   • SQM for production / spacer charges.
        /// </summary>
        public decimal Quantity { get; set; }

        /// <summary>Unit price actually applied.</summary>
        public decimal PricePerSqm { get; set; }

        /// <summary>
        /// Display unit for <see cref="PricePerSqm"/>: SQM, LM, QTY, TRIP,
        /// etc. Defaults to SQM for all existing glass and spacer rows.
        /// </summary>
        public string UnitLabel { get; set; } = "SQM";

        /// <summary>Extended amount in AED.</summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// PHASE G — where the sheet size came from: "chosen in Step 4",
        /// "from Sheet Store", or the default fallback. Shown in the UI so
        /// an unexpected cost is always traceable to its sheet.
        /// </summary>
        public string SheetSource { get; set; } = "";

        /// <summary>
        /// The FULL specification this row belongs to, taken from the Step 5
        /// DataGrid (SpecGroup.SelectedSpecName) — e.g.
        /// "6mm Clear FT Glass + 12mm ASP + 6mm Clear FT Glass".
        ///
        /// The SPEC column used to print only "SPEC 2", which said nothing
        /// about what was actually being costed.
        /// </summary>
        public string SpecName { get; set; } = "";

        // ── Display helpers (bound directly by the view) ──────────────────

        /// <summary>
        /// The specification itself, so the P&amp;L reads as the product
        /// being quoted. Falls back to "SPEC n" only when no spec name was
        /// captured, and to blank for rows with no spec origin.
        /// </summary>
        public string SpecDisplay =>
            !string.IsNullOrWhiteSpace(SpecName)
                ? SpecName
                : (SpecNumber > 0 ? $"SPEC {SpecNumber}" : "");

        /// <summary>e.g. "3210 × 2250 · 7.2225 SQM", or "—" for charge rows.</summary>
        public string SizeDisplay =>
            SheetWidthMm > 0 && SheetHeightMm > 0
                ? $"{SheetWidthMm} × {SheetHeightMm} · {SheetSqm:0.####} SQM"
                : "—";

        /// <summary>e.g. "× 4".</summary>
        public string QuantityDisplay =>
            $"× {Quantity:0.##}";

        /// <summary>
        /// e.g. "46.00 / SQM" or "15.00 / QTY" depending on the row.
        /// </summary>
        public string PriceDisplay => $"{PricePerSqm:N2} / {UnitLabel}";

        /// <summary>e.g. "1,328.94".</summary>
        public string AmountDisplay => $"{Amount:N2}";

        /// <summary>Builds the persistable form of this row.</summary>
        public ProfitLossRowSnapshot ToSnapshot() => new()
        {
            Kind = Kind ?? "",
            SpecNumber = SpecNumber,
            Description = Description ?? "",
            SheetWidthMm = SheetWidthMm,
            SheetHeightMm = SheetHeightMm,
            SheetSqm = SheetSqm,
            Quantity = Quantity,
            PricePerSqm = PricePerSqm,
            UnitLabel = UnitLabel ?? "SQM",
            Amount = Amount,
            SheetSource = SheetSource ?? "",
            SpecName = SpecName ?? ""
        };
    }
}
