// ═══════════════════════════════════════════════════════════════════════
// FILE: Models/SheetStore/InventoryItem.cs
// MODULE: Inventory — non-glass consumables (IGU line materials)
// ADDED: 2026-09-06
//
// WHY A SEPARATE TABLE INSTEAD OF EXTENDING GlassSheet
//
// Owner asked to stock "air spacer silicon air spacer joint ... as per
// their type like qty based sqm based lm based" — i.e. the double-glazing
// consumables, each bought in its own unit.
//
// GlassSheet cannot express that. It hard-codes one shape:
//   • Stock is an INT (whole sheets). You cannot stock 45.55 LM of
//     spacer bar in an integer.
//   • Area is DERIVED from WidthMm x HeightMm. A drum of silicone has no
//     width or height.
//   • Price is always per SQM.
//   • IX_GlassSheet_Unique keys on Category+Thickness+Colour+Size+Supplier,
//     which is meaningless for a sack of desiccant.
//
// Changing Stock to decimal would ripple through every consumer of
// GlassSheet — SheetStorePricingService, GlassPriceStamper,
// SpecPaneResolver, SpecSheetRegistry, DashboardMetricsService and four
// Agent tools — all of which price live proforma invoices. The owner's
// hard constraint was "without breaking its binding to proforma invoice
// or other module".
//
// So glass is left EXACTLY as it is, and consumables get their own,
// correctly-shaped table. Reports union the two. This buys the feature at
// near-zero risk to invoicing, and it is also the honest model: a glass
// sheet really is a different kind of thing from a gas cylinder.
//
// WHEN TO REVISIT: if consumables ever need the same spec-matching and
// invoice-pricing paths glass uses, the two should converge behind a
// shared IStockItem interface rather than by widening GlassSheet.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProGlassAutomation.Models.SheetStore
{
    /// <summary>
    /// Unit of measure a consumable is bought, stocked and consumed in.
    /// Stored as the INT enum value; new members must be APPENDED so
    /// existing rows keep their meaning.
    /// </summary>
    public enum StockUom
    {
        /// <summary>Discrete countable units — corner keys, connectors.</summary>
        Piece = 0,

        /// <summary>Linear metres — spacer bar, Georgian bar, gasket.</summary>
        LinearMetre = 1,

        /// <summary>Square metres — films, interlayers.</summary>
        SquareMetre = 2,

        /// <summary>Kilograms — desiccant, butyl, silicone, polysulphide.</summary>
        Kilogram = 3,

        /// <summary>Litres — liquid sealants sold by volume.</summary>
        Litre = 4,

        /// <summary>Cubic metres — argon and other cavity gases.</summary>
        CubicMetre = 5
    }

    /// <summary>
    /// Broad material class, for grouping and reporting. Also appended-only.
    /// </summary>
    public enum InventoryCategory
    {
        Other = 0,
        SpacerBar = 1,
        Desiccant = 2,
        PrimarySeal = 3,     // butyl / PIB
        SecondarySeal = 4,   // silicone / polysulphide
        Gas = 5,             // argon, krypton
        Hardware = 6,        // corner keys, connectors
        Film = 7             // interlayers, applied films
    }

    /// <summary>
    /// One non-glass inventory line: spacer bar, desiccant, sealant, gas,
    /// hardware. Quantities are DECIMAL because most of these are
    /// continuous measures — 45.55 LM of spacer, 2.4 kg of desiccant.
    /// </summary>
    public class InventoryItem
    {
        [Key]
        public int Id { get; set; }

        // ============ IDENTITY ============

        /// <summary>Human name, e.g. "Warm-edge spacer 16mm black".</summary>
        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        public InventoryCategory Category { get; set; } = InventoryCategory.Other;

        /// <summary>
        /// Optional supplier/manufacturer part number. Free text because
        /// suppliers do not agree on a format.
        /// </summary>
        [MaxLength(100)]
        public string PartNumber { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Supplier { get; set; } = string.Empty;

        /// <summary>
        /// Distinguishing spec — "16mm", "black", "argon 99.9%". Kept as
        /// one free-text field rather than a rigid column set, because the
        /// meaningful attribute differs per category.
        /// </summary>
        [MaxLength(100)]
        public string Specification { get; set; } = string.Empty;

        // ============ UNIT OF MEASURE ============

        /// <summary>
        /// The unit this item is stocked and consumed in. This is the
        /// whole point of the table: a spacer is LM, desiccant is kg,
        /// argon is m³, corner keys are pieces.
        /// </summary>
        public StockUom Uom { get; set; } = StockUom.Piece;

        // ============ STOCK ============
        //
        // DECIMAL, unlike GlassSheet.Stock — see the header note. You
        // genuinely receive 45.55 LM of spacer bar.

        public decimal Stock { get; set; }
        public decimal Used { get; set; }

        /// <summary>
        /// Reorder threshold. Zero disables the alert, so existing rows
        /// and items with no meaningful minimum stay quiet.
        /// </summary>
        public decimal ReorderLevel { get; set; }

        // ============ PRICING (per UoM) ============

        public decimal PurchasePricePerUnit { get; set; }
        public decimal SellPricePerUnit { get; set; }

        // ============ TIMESTAMPS ============
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        public DateTime? LastPurchaseDate { get; set; }

        // ============ COMPUTED (not stored) ============

        /// <summary>Available quantity = Stock − Used.</summary>
        [NotMapped]
        public decimal Balance => Stock - Used;

        /// <summary>
        /// True when the item has fallen to or below its reorder level.
        /// A zero threshold means "not tracked" and never triggers.
        /// </summary>
        [NotMapped]
        public bool IsLowStock => ReorderLevel > 0 && Balance <= ReorderLevel;

        /// <summary>True when nothing is left on the shelf.</summary>
        [NotMapped]
        public bool IsOutOfStock => Balance <= 0;

        [NotMapped]
        public decimal TotalPurchaseValue => Stock * PurchasePricePerUnit;

        [NotMapped]
        public decimal BalanceCostValue => Balance * PurchasePricePerUnit;

        [NotMapped]
        public decimal UsedCostValue => Used * PurchasePricePerUnit;

        // ============ DISPLAY ============

        /// <summary>Short unit symbol for grids and reports.</summary>
        [NotMapped]
        public string UomDisplay => Uom switch
        {
            StockUom.Piece => "pcs",
            StockUom.LinearMetre => "LM",
            StockUom.SquareMetre => "SQM",
            StockUom.Kilogram => "kg",
            StockUom.Litre => "L",
            StockUom.CubicMetre => "m³",
            _ => string.Empty
        };

        /// <summary>
        /// Quantities render to the precision the unit deserves: you do
        /// not order 4.00 corner keys, but 45.55 LM of spacer matters.
        /// </summary>
        [NotMapped]
        public string BalanceDisplay => Uom == StockUom.Piece
            ? $"{Balance:N0} {UomDisplay}"
            : $"{Balance:N2} {UomDisplay}";

        [NotMapped]
        public string CategoryDisplay => Category switch
        {
            InventoryCategory.SpacerBar => "Spacer bar",
            InventoryCategory.Desiccant => "Desiccant",
            InventoryCategory.PrimarySeal => "Primary seal (butyl)",
            InventoryCategory.SecondarySeal => "Secondary seal",
            InventoryCategory.Gas => "Cavity gas",
            InventoryCategory.Hardware => "Hardware",
            InventoryCategory.Film => "Film / interlayer",
            _ => "Other"
        };

        /// <summary>e.g. "Warm-edge spacer 16mm black — 16mm".</summary>
        [NotMapped]
        public string FullDescription => string.IsNullOrWhiteSpace(Specification)
            ? Name
            : $"{Name} — {Specification}";
    }
}
