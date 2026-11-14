using ProGlassAutomation.Services.Reporting;

namespace ProGlassAutomation.Services.ProformaInvoice
{
    /// <summary>
    /// Turns a target grand total (VAT inclusive) into the AED discount
    /// that must come off net subtotal. Clerks type 15,500.00 instead of
    /// working the VAT formula by hand.
    /// </summary>
    public static class InvoiceRoundTo
    {
        /// <summary>
        /// Discount in AED applied to <paramref name="netSubtotal"/> so that
        /// (net − discount) + VAT equals <paramref name="targetGrand"/>,
        /// matching Step 7 rounding (2 decimal places).
        /// Returns 0 when the target is at or above the undiscounted total.
        /// </summary>
        public static decimal DiscountOnNet(
            decimal netSubtotal,
            decimal vatPercent,
            bool vatApplicable,
            decimal targetGrand)
        {
            netSubtotal = MoneyRounding.Round(netSubtotal);
            targetGrand = MoneyRounding.Round(targetGrand);
            if (netSubtotal <= 0m) return 0m;
            if (targetGrand < 0m) targetGrand = 0m;

            decimal maxGrand = GrandFromDiscount(netSubtotal, 0m, vatPercent, vatApplicable);
            if (targetGrand >= maxGrand) return 0m;

            decimal factor = vatApplicable ? 1m + vatPercent / 100m : 1m;
            decimal beforeVat = factor <= 0m
                ? 0m
                : MoneyRounding.Round(targetGrand / factor);
            if (beforeVat < 0m) beforeVat = 0m;
            if (beforeVat > netSubtotal) beforeVat = netSubtotal;

            decimal discount = MoneyRounding.Round(netSubtotal - beforeVat);

            for (int i = 0; i < 40; i++)
            {
                decimal grand = GrandFromDiscount(netSubtotal, discount, vatPercent, vatApplicable);
                if (grand == targetGrand) break;
                discount += grand > targetGrand ? 0.01m : -0.01m;
                if (discount < 0m) { discount = 0m; break; }
                if (discount > netSubtotal) { discount = netSubtotal; break; }
            }

            return MoneyRounding.Round(discount);
        }

        public static decimal GrandFromDiscount(
            decimal netSubtotal,
            decimal discountAed,
            decimal vatPercent,
            bool vatApplicable)
        {
            decimal after = netSubtotal - discountAed;
            if (after < 0m) after = 0m;
            decimal vat = vatApplicable
                ? MoneyRounding.Round(after * vatPercent / 100m)
                : 0m;
            return MoneyRounding.Round(after + vat);
        }
    }
}
