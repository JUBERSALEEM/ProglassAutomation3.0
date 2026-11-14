// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/ProformaInvoice/Validation/CrossStepValidator.cs
// PROJECT: ProGlassAutomation
// MODULE: Proforma Invoice — Cross-step validation
//
// THE PROBLEM THIS SOLVES
//   Each wizard step validates its own data independently. But there
//   are business rules that span multiple steps:
//     • An invoice with 100 SQM of glass and 0 other charges is suspicious
//     • A profit margin below 5% should warn the operator
//     • A missing customer name should block save
//   Without cross-step validation, these issues reach the customer.
//
// CONTRACT
//   • Pure function — no side effects, no WPF, no repositories.
//   • Returns errors (block save) and warnings (operator can override).
//   • Never throws — a validation failure is a result, not an exception.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using ProGlassAutomation.Services.Reporting;

namespace ProGlassAutomation.Services.ProformaInvoice.Validation
{
    /// <summary>
    /// Result of a cross-step validation. Errors block the operation;
    /// warnings are advisory and the operator can choose to proceed.
    /// </summary>
    public sealed class CrossStepValidationResult
    {
        public IReadOnlyList<string> Errors { get; }
        public IReadOnlyList<string> Warnings { get; }
        public bool IsValid => Errors.Count == 0;
        public bool HasWarnings => Warnings.Count > 0;

        public CrossStepValidationResult(
            IReadOnlyList<string> errors,
            IReadOnlyList<string> warnings)
        {
            Errors = errors ?? Array.Empty<string>();
            Warnings = warnings ?? Array.Empty<string>();
        }

        public static CrossStepValidationResult Valid { get; } =
            new(Array.Empty<string>(), Array.Empty<string>());
    }

    /// <summary>
    /// Input snapshot for cross-step validation. Decoupled from the
    /// actual ViewModels so this can be tested without WPF.
    /// </summary>
    public sealed class InvoiceValidationSnapshot
    {
        // Step 1
        public string? ProformaId { get; init; }
        public string? CompanyName { get; init; }
        public DateTime IssueDate { get; init; }
        public DateTime ValidUntil { get; init; }

        // Step 2
        public string? CustomerName { get; init; }

        // Step 5
        public int SpecGroupCount { get; init; }
        public decimal TotalSqm { get; init; }
        public decimal TotalPieces { get; init; }
        public decimal TotalAmount { get; init; }

        // Step 6
        public decimal TotalOtherCharges { get; init; }
        public int ActiveChargeRows { get; init; }
        public bool HasWastageRows { get; init; }

        // Step 7
        public decimal SummaryGlassMaterialsSubtotal { get; init; }
        public decimal SummaryOtherChargesSubtotal { get; init; }
        public decimal NetSubtotal { get; init; }
        public decimal DiscountAmount { get; init; }
        public decimal SubtotalBeforeVat { get; init; }
        public decimal GrandTotal { get; init; }
        public decimal VatAmount { get; init; }
        public decimal VatPercent { get; init; }
        public bool IsVatApplicable { get; init; }
        public decimal DiscountPercent { get; init; }

        // Step 9
        public decimal ProfitMarginPercent { get; init; }
        public decimal TotalCost { get; init; }
        public decimal TotalRevenue { get; init; }
    }

    /// <summary>
    /// Validates business rules that span multiple wizard steps.
    /// Called before save, print, and approval operations.
    /// </summary>
    public static class CrossStepValidator
    {
        // ── Business thresholds (editable per deployment) ───────────
        private const decimal MINIMUM_PROFIT_MARGIN_PERCENT = 5.0m;
        private const decimal WARNING_PROFIT_MARGIN_PERCENT = 10.0m;
        private const int MAXIMUM_VALID_DAYS = 365;
        private const int MINIMUM_QUOTE_VALIDITY_DAYS = 1;
        private const decimal MAXIMUM_DISCOUNT_PERCENT = 50.0m;

        /// <summary>
        /// Validates the invoice snapshot against all cross-step business
        /// rules. Returns errors (must fix) and warnings (should review).
        /// </summary>
        public static CrossStepValidationResult Validate(InvoiceValidationSnapshot snap)
        {
            if (snap == null)
                return new CrossStepValidationResult(
                    new[] { "Validation snapshot is null." },
                    Array.Empty<string>());

            var errors = new List<string>();
            var warnings = new List<string>();

            // ── Step 1: Identity ───────────────────────────────────
            if (string.IsNullOrWhiteSpace(snap.ProformaId))
                errors.Add("Proforma ID is required (Step 1).");

            if (string.IsNullOrWhiteSpace(snap.CompanyName))
                errors.Add("Company name is required (Step 1).");

            // Validity period
            if (snap.ValidUntil < snap.IssueDate)
                errors.Add("Valid-until date cannot be before issue date (Step 1).");

            int validityDays = (snap.ValidUntil - snap.IssueDate).Days;
            if (validityDays < MINIMUM_QUOTE_VALIDITY_DAYS)
                errors.Add("Quote must be valid for at least 1 day (Step 1).");

            if (validityDays > MAXIMUM_VALID_DAYS)
                warnings.Add($"Quote validity is {validityDays} days — unusually long. Please verify.");

            // ── Step 2: Customer ───────────────────────────────────
            if (string.IsNullOrWhiteSpace(snap.CustomerName))
                errors.Add("Customer name is required (Step 2).");

            // ── Step 5: Data grid ──────────────────────────────────
            if (snap.SpecGroupCount == 0)
                errors.Add("At least one spec group is required (Step 5).");

            if (snap.TotalSqm < 0 || snap.TotalPieces < 0 || snap.TotalAmount < 0)
                errors.Add("Step 5 totals cannot be negative — check dimensions, quantity and rates.");

            if (snap.TotalSqm <= 0 && snap.TotalPieces > 0)
                warnings.Add("Total SQM is zero but pieces exist — check sizes (Step 5).");

            if (snap.TotalPieces <= 0 && snap.SpecGroupCount > 0)
                warnings.Add("No glass pieces entered — check item rows (Step 5).");

            // ── Step 6: Other charges ──────────────────────────────
            if (snap.TotalOtherCharges < 0m)
                errors.Add("Step 6 other charges total cannot be negative.");

            if (snap.TotalSqm > 10m && snap.TotalOtherCharges == 0m)
                warnings.Add("No other charges for a large order — verify this is intentional (Step 6).");

            // ── Step 7: Totals ─────────────────────────────────────
            if (snap.SummaryGlassMaterialsSubtotal < 0 || snap.SummaryOtherChargesSubtotal < 0 ||
                snap.NetSubtotal < 0 || snap.DiscountAmount < 0 || snap.SubtotalBeforeVat < 0 ||
                snap.VatAmount < 0)
                errors.Add("Invoice financial totals cannot be negative (Step 7).");

            if (Math.Abs(snap.SummaryGlassMaterialsSubtotal - snap.TotalAmount) > 0.01m)
                errors.Add($"Step 7 glass subtotal ({snap.SummaryGlassMaterialsSubtotal:N2}) does not match Step 5 total ({snap.TotalAmount:N2}). Recalculate before saving.");

            var expectedNet = MoneyRounding.Round(snap.SummaryGlassMaterialsSubtotal + snap.SummaryOtherChargesSubtotal);
            if (Math.Abs(snap.NetSubtotal - expectedNet) > 0.01m)
                errors.Add($"Net subtotal mismatch: Step 7 shows {snap.NetSubtotal:N2}, expected {expectedNet:N2}.");

            if (snap.DiscountPercent > MAXIMUM_DISCOUNT_PERCENT)
                warnings.Add($"Discount is {snap.DiscountPercent:F1}% — exceeds {MAXIMUM_DISCOUNT_PERCENT:F0}% threshold.");

            if (snap.DiscountPercent < 0)
                errors.Add("Discount percentage cannot be negative (Step 7).");

            if (snap.DiscountAmount > snap.NetSubtotal + 0.01m)
                errors.Add("Discount cannot exceed the net subtotal (Step 7).");

            var expectedBeforeVat = MoneyRounding.Round(snap.NetSubtotal - snap.DiscountAmount);
            if (Math.Abs(snap.SubtotalBeforeVat - expectedBeforeVat) > 0.01m)
                errors.Add($"Subtotal-before-VAT mismatch: Step 7 shows {snap.SubtotalBeforeVat:N2}, expected {expectedBeforeVat:N2}.");

            if (snap.VatPercent < 0m || snap.VatPercent > 100m)
                errors.Add("VAT percent must be between 0 and 100 (Step 7).");

            var expectedVat = snap.IsVatApplicable
                ? MoneyRounding.Round(snap.SubtotalBeforeVat * (snap.VatPercent / 100m))
                : 0m;
            if (Math.Abs(snap.VatAmount - expectedVat) > 0.01m)
                errors.Add($"VAT amount mismatch: Step 7 shows {snap.VatAmount:N2}, expected {expectedVat:N2}.");

            var expectedGrand = MoneyRounding.Round(snap.SubtotalBeforeVat + snap.VatAmount);
            if (Math.Abs(snap.GrandTotal - expectedGrand) > 0.01m)
                errors.Add($"Grand total mismatch: Step 7 shows {snap.GrandTotal:N2}, expected {expectedGrand:N2}.");

            if (snap.GrandTotal < 0)
                errors.Add("Grand total is negative — check pricing (Step 7).");

            if (snap.GrandTotal == 0 && snap.TotalPieces > 0)
                warnings.Add("Grand total is zero but pieces exist — verify pricing.");

            // ── Step 9: Profitability ──────────────────────────────
            if (snap.TotalRevenue > 0 && snap.TotalCost > 0)
            {
                if (snap.ProfitMarginPercent < 0)
                    errors.Add($"Profit margin is negative ({snap.ProfitMarginPercent:F1}%) — invoice is at a loss.");
                else if (snap.ProfitMarginPercent < MINIMUM_PROFIT_MARGIN_PERCENT)
                    errors.Add($"Profit margin ({snap.ProfitMarginPercent:F1}%) is below minimum threshold ({MINIMUM_PROFIT_MARGIN_PERCENT:F0}%).");
                else if (snap.ProfitMarginPercent < WARNING_PROFIT_MARGIN_PERCENT)
                    warnings.Add($"Profit margin ({snap.ProfitMarginPercent:F1}%) is below recommended threshold ({WARNING_PROFIT_MARGIN_PERCENT:F0}%).");
            }

            if (snap.TotalCost > snap.TotalRevenue && snap.TotalRevenue > 0)
                errors.Add("Total cost exceeds total revenue — invoice is unprofitable.");

            return new CrossStepValidationResult(errors, warnings);
        }

        /// <summary>
        /// Quick check for save eligibility. Returns null when valid,
        /// or the first blocking error message.
        /// </summary>
        public static string? GetSaveBlocker(InvoiceValidationSnapshot snap)
        {
            var result = Validate(snap);
            return result.IsValid ? null : result.Errors[0];
        }
    }
}
