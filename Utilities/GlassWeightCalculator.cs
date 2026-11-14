using System;

namespace ProGlassAutomation.Utilities
{
    /// <summary>
    /// Single source of truth for glass weight calculations.
    /// 
    /// Business rule (UAE glass industry standard):
    ///   Glass density = 2.5 kg/m² per mm thickness
    ///   Weight per piece (kg) = Area (m²) × Total Glass Thickness (mm) × 2.5
    ///   Total weight (kg) = Weight per piece × Quantity
    /// 
    /// Glass type handling:
    ///   - SGU (Single Glazed Unit): Single glass layer
    ///   - DGU (Double Glazed Unit): Two glass layers + spacer (spacer weight negligible)
    ///   - LAM (Laminated): Two glass layers + PVB interlayer (PVB weight negligible)
    ///   - DGU+LAM: Three glass layers + spacer + interlayer
    /// 
    /// Used by:
    ///   - Delivery Note: Weight per item for logistics planning
    ///   - Tax Invoice: (future — weight-based shipping calculations)
    ///   - Job Order: (future — structural load calculations)
    /// </summary>
    public static class GlassWeightCalculator
    {
        /// <summary>Glass density: 2.5 kg per square meter per millimeter thickness.</summary>
        public const decimal GlassDensityKgPerSqmPerMm = 2.5m;

        /// <summary>
        /// Calculate weight per piece in kilograms.
        /// Returns 0 if area or thickness is invalid.
        /// Rounded to 2 decimal places.
        /// </summary>
        /// <param name="areaM2">Panel area in square meters</param>
        /// <param name="totalThicknessMm">Total glass thickness in millimeters</param>
        public static decimal CalculatePieceWeight(decimal areaM2, decimal totalThicknessMm)
        {
            if (areaM2 <= 0 || totalThicknessMm <= 0) return 0m;
            return Math.Round(areaM2 * totalThicknessMm * GlassDensityKgPerSqmPerMm, 2);
        }

        /// <summary>
        /// Calculate total weight for a quantity of identical pieces.
        /// Returns 0 if inputs are invalid.
        /// Rounded to 2 decimal places.
        /// </summary>
        public static decimal CalculateTotalWeight(decimal areaM2, decimal totalThicknessMm, int quantity)
        {
            if (quantity <= 0) return 0m;
            return Math.Round(CalculatePieceWeight(areaM2, totalThicknessMm) * quantity, 2);
        }

        /// <summary>
        /// Extract total glass thickness from spec group based on glass type.
        /// Handles SGU, DGU, LAM, and DGU+LAM configurations.
        /// Returns 0 if thickness data is unavailable or invalid.
        /// </summary>
        /// <param name="glassType">Glass type: SGU, DGU, LAM, DGU+LAM</param>
        /// <param name="outerThickness">Outer glass thickness (mm)</param>
        /// <param name="innerThickness">Inner glass thickness (mm) — not used for SGU</param>
        /// <param name="lamOuterThickness">Laminated outer thickness (mm) — for DGU+LAM</param>
        /// <param name="lamInnerThickness">Laminated inner thickness (mm) — for DGU+LAM</param>
        public static decimal ExtractTotalThickness(
            string glassType,
            string outerThickness,
            string innerThickness,
            string lamOuterThickness = "",
            string lamInnerThickness = "")
        {
            if (string.IsNullOrWhiteSpace(glassType)) return 0m;

            decimal outer = ParseThickness(outerThickness);
            decimal inner = ParseThickness(innerThickness);
            decimal lamOuter = ParseThickness(lamOuterThickness);
            decimal lamInner = ParseThickness(lamInnerThickness);

            string type = glassType.Trim().ToUpperInvariant();

            // SGU: Single glass layer
            if (type == "SGU" || type == "SINGLE")
            {
                return outer > 0 ? outer : 0m;
            }

            // DGU: Two glass layers (spacer weight negligible)
            if (type == "DGU" || type == "DOUBLE")
            {
                return (outer > 0 && inner > 0) ? outer + inner : 0m;
            }

            // LAM: Two glass layers + PVB (PVB weight negligible)
            if (type == "LAM" || type == "LAMINATED")
            {
                return (outer > 0 && inner > 0) ? outer + inner : 0m;
            }

            // DGU+LAM: Three glass layers (outer + inner + laminated outer)
            if (type.Contains("DGU") && type.Contains("LAM"))
            {
                // Try DGU+LAM specific fields first
                if (lamOuter > 0 && lamInner > 0)
                {
                    return outer + inner + lamOuter + lamInner;
                }
                // Fallback to standard DGU fields
                return (outer > 0 && inner > 0) ? outer + inner : 0m;
            }

            // Unknown type: try to use outer thickness only
            return outer > 0 ? outer : 0m;
        }

        /// <summary>
        /// Parse thickness string to decimal.
        /// Handles formats: "6", "6mm", "6.5", "6.5mm"
        /// Returns 0 if parsing fails.
        /// </summary>
        private static decimal ParseThickness(string thickness)
        {
            if (string.IsNullOrWhiteSpace(thickness)) return 0m;

            string cleaned = thickness.Trim()
                .Replace("mm", "", StringComparison.OrdinalIgnoreCase)
                .Replace(" ", "")
                .Trim();

            if (decimal.TryParse(cleaned, out decimal value) && value > 0)
            {
                return value;
            }

            return 0m;
        }
    }
}
