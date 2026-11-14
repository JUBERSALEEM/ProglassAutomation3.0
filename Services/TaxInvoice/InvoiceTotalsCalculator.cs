// ═══════════════════════════════════════════════════════════════════════
//  Services/TaxInvoice/InvoiceTotalsCalculator.cs
//
//  Single source of truth for Tax Invoice financial totals:
//
//      DiscountAmount = round(Subtotal × DiscountPercent / 100)
//      AfterDiscount  = Subtotal − DiscountAmount
//      VatAmount      = round(AfterDiscount × VAT_RATE)    // UAE 5%
//      GrandTotal     = AfterDiscount + VatAmount
//
//  Previously duplicated in TaxInvoiceDetailViewModel.RecalculateFinancials
//  and TaxInvoiceMapper. Centralised here (Phase 11/12) so the UAE VAT
//  business rule lives in one place and is trivially unit-testable.
// ═══════════════════════════════════════════════════════════════════════

using System;
using ProGlassAutomation.Services.Reporting;

namespace ProGlassAutomation.Services.TaxInvoice
{
    /// <summary>
    /// Pure financial-total computation for tax invoices.
    /// </summary>
    public static class InvoiceTotalsCalculator
    {
        /// <summary>UAE standard VAT rate (5%). Single source of truth is VatCalculator.</summary>
        public static decimal VatRate => ProGlassAutomation.Services.Reporting.VatCalculator.VAT_RATE_DECIMAL;

        /// <summary>Computed tax-invoice financial totals.</summary>
        public readonly record struct Totals(
            decimal DiscountAmount,
            decimal AfterDiscount,
            decimal VatAmount,
            decimal GrandTotal);

        /// <summary>
        /// Computes discount, VAT and grand total from a subtotal and a
        /// discount percentage. Discount and VAT use MoneyRounding
        /// (2dp, midpoint away from zero) so a half-fil does not banker's-round
        /// down and disagree with the line values.
        /// </summary>
        public static Totals Compute(decimal subtotal, decimal discountPercent)
        {
            decimal discountAmt = MoneyRounding.Round(subtotal * (discountPercent / 100m));
            decimal afterDiscount = subtotal - discountAmt;
            decimal vatAmount = MoneyRounding.Round(afterDiscount * VatRate);
            decimal grandTotal = afterDiscount + vatAmount;

            return new Totals(discountAmt, afterDiscount, vatAmount, grandTotal);
        }
    }
}
