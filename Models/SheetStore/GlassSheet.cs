using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProGlassAutomation.Models.SheetStore
{
    /// <summary>
    /// Represents one glass sheet type in the store inventory.
    /// Each row = unique combination of Category + Thickness + Color + Size + Supplier.
    ///
    /// Pricing model:
    ///   - Prices are per SQM (square meter)
    ///   - Stock is quantity of sheets
    ///   - Total Value = SQM per sheet × Quantity × Price per SQM
    ///
    /// Example: 3210×2250 sheet, Stock=46, Purchase=1000 AED/SQM
    ///   SQM per sheet = 7.2225
    ///   Total SQM     = 7.2225 × 46 = 332.235
    ///   Total Buy     = 332.235 × 1000 = 332,235 AED
    /// </summary>
    public class GlassSheet
    {
        [Key]
        public int Id { get; set; }

        // ============ IDENTITY ============
        [Required, MaxLength(100)]
        public string Category { get; set; } = string.Empty;

        [Required]
        public int ThicknessMm { get; set; }

        [Required, MaxLength(50)]
        public string Color { get; set; } = "Clear";

        [Required]
        public int WidthMm { get; set; }

        [Required]
        public int HeightMm { get; set; }

        [Required, MaxLength(100)]
        public string Supplier { get; set; } = string.Empty;

        // ============ PRICING (per SQM) ============
        public decimal PurchasePricePerSqm { get; set; }
        public decimal SellPricePerSqm { get; set; }

        // ============ STOCK (quantity of sheets) ============
        public int Stock { get; set; }
        public int Used { get; set; }

        // ============ TIMESTAMPS ============
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        public DateTime? LastPurchaseDate { get; set; }

        // ============ COMPUTED PROPERTIES (not stored in DB) ============

        /// <summary>Area of one sheet in square meters. E.g., 3210×2250 = 7.2225 SQM</summary>
        [NotMapped]
        public decimal SqmPerSheet =>
            ProGlassAutomation.Utilities.SqmCalculator.CalculateSheetArea(
                WidthMm, HeightMm);

        /// <summary>Alias for backward compatibility.</summary>
        [NotMapped]
        public decimal Sqm => SqmPerSheet;

        /// <summary>Available quantity = Stock - Used.</summary>
        [NotMapped]
        public int Balance => Stock - Used;

        // ============ TOTAL SQM ============

        /// <summary>Total square meters in stock (SqmPerSheet × Stock).</summary>
        [NotMapped]
        public decimal TotalSqmInStock => SqmPerSheet * Stock;

        /// <summary>Total square meters used (SqmPerSheet × Used).</summary>
        [NotMapped]
        public decimal TotalSqmUsed => SqmPerSheet * Used;

        /// <summary>Total square meters available (SqmPerSheet × Balance).</summary>
        [NotMapped]
        public decimal TotalSqmBalance => SqmPerSheet * Balance;

        // ============ TOTAL VALUES ============

        /// <summary>Total purchase cost for full stock. Example: 7.2225 × 46 × 1000 = 332,235</summary>
        [NotMapped]
        public decimal TotalPurchaseValue => SqmPerSheet * Stock * PurchasePricePerSqm;

        /// <summary>Total sell value if all stock is sold. Example: 7.2225 × 46 × 1044 = 346,933</summary>
        [NotMapped]
        public decimal TotalSellValue => SqmPerSheet * Stock * SellPricePerSqm;

        /// <summary>Value of remaining stock at sell price.</summary>
        [NotMapped]
        public decimal BalanceValue => SqmPerSheet * Balance * SellPricePerSqm;

        /// <summary>Potential profit on full stock (TotalSell - TotalBuy).</summary>
        [NotMapped]
        public decimal ProfitValue => TotalSellValue - TotalPurchaseValue;

        // ── CONSUMED VALUE ───────────────────────────────────────────
        // Stock/Sell values above are computed on TOTAL Stock, i.e. what
        // was bought. Neither says what has actually been CONSUMED, which
        // is the number that tells you whether inventory is turning into
        // revenue or just sitting on a rack.

        /// <summary>What the consumed sheets COST (purchase side).</summary>
        public decimal UsedCostValue => SqmPerSheet * Used * PurchasePricePerSqm;

        /// <summary>What the consumed sheets are WORTH (sell side).</summary>
        public decimal UsedSellValue => SqmPerSheet * Used * SellPricePerSqm;

        /// <summary>Realised margin on what has actually been consumed.</summary>
        public decimal UsedProfitValue => UsedSellValue - UsedCostValue;

        /// <summary>Purchase value still sitting unused on the rack.</summary>
        public decimal BalanceCostValue => SqmPerSheet * Balance * PurchasePricePerSqm;

        /// <summary>Profit margin per SQM.</summary>
        [NotMapped]
        public decimal ProfitPerSqm => SellPricePerSqm - PurchasePricePerSqm;

        // ============ DISPLAY HELPERS ============

        /// <summary>e.g., "3210 × 2250"</summary>
        [NotMapped]
        public string SizeDisplay => $"{WidthMm} × {HeightMm}";

        /// <summary>e.g., "6mm"</summary>
        [NotMapped]
        public string ThicknessDisplay => $"{ThicknessMm}mm";
    }
}