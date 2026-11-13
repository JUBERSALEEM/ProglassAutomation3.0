// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.a.2.MoneyRounding.cs
// MODULE: step6 — the ONE money rounding home and the ONE "make the total
// come out at what the clerk typed" calculation.
//
// Every currency figure in the invoice passes through Round(): line values,
// charges, the discount, VAT and the grand total. Rounding at each step (and
// never comparing unrounded values with rounded ones) is what makes the
// printed waterfall add up exactly.
// ═══════════════════════════════════════════════════════════════════════

using System;

namespace ProGlassAutomation.Models.Proforma
{
    /// <summary>Currency and percentage rounding for the invoice.</summary>
    public static class MoneyRounding
    {
        /// <summary>UAE dirhams use two decimals; .5 rounds away from zero.</summary>
        public static decimal Round(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);

        /// <summary>Areas and rates keep four decimals; money does not.</summary>
        public static decimal RoundArea(decimal value) =>
            Math.Round(value, 4, MidpointRounding.AwayFromZero);

        /// <summary>A percentage shown at one decimal (margins, yields).</summary>
        public static decimal RoundPercent(decimal value) =>
            Math.Round(value, 1, MidpointRounding.AwayFromZero);

        /// <summary>Clamps a percentage into a sane range without throwing.</summary>
        public static decimal ClampPercent(decimal value, decimal min, decimal max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>Grand total implied by a discount, at the invoice's VAT rule.</summary>
        public static decimal GrandTotal(
            decimal netSubtotal, decimal discountAed, decimal vatPercent, bool vatApplicable)
        {
            decimal afterDiscount = netSubtotal - discountAed;
            if (afterDiscount < 0m) afterDiscount = 0m;
            decimal vat = vatApplicable
                ? Round(afterDiscount * ClampPercent(vatPercent, 0m, 100m) / 100m)
                : 0m;
            return Round(afterDiscount + vat);
        }

        /// <summary>
        /// The discount that makes the VAT-inclusive total equal
        /// <paramref name="targetGrand"/>. A clerk types 15,500.00 and the
        /// discount is worked out, instead of the VAT formula being done by
        /// hand on a quotation pad.
        /// </summary>
        public static decimal DiscountForTargetTotal(
            decimal netSubtotal, decimal vatPercent, bool vatApplicable, decimal targetGrand)
        {
            netSubtotal = Round(netSubtotal);
            targetGrand = Round(targetGrand);
            if (netSubtotal <= 0m) return 0m;
            if (targetGrand < 0m) targetGrand = 0m;
            if (targetGrand >= GrandTotal(netSubtotal, 0m, vatPercent, vatApplicable)) return 0m;

            decimal factor = vatApplicable ? 1m + ClampPercent(vatPercent, 0m, 100m) / 100m : 1m;
            decimal beforeVat = Round(targetGrand / factor);
            if (beforeVat < 0m) beforeVat = 0m;
            if (beforeVat > netSubtotal) beforeVat = netSubtotal;

            decimal discount = Round(netSubtotal - beforeVat);

            // Two-decimal landings are not always reachable in one division (the
            // VAT itself rounds), so walk one fils at a time for a bounded number
            // of steps. The result is a discount the printed total reconciles to.
            for (int step = 0; step < 40; step++)
            {
                decimal grand = GrandTotal(netSubtotal, discount, vatPercent, vatApplicable);
                if (grand == targetGrand) break;
                discount += grand > targetGrand ? 0.01m : -0.01m;
                if (discount < 0m) { discount = 0m; break; }
                if (discount > netSubtotal) { discount = netSubtotal; break; }
            }

            return Round(discount);
        }

        /// <summary>Parses a typed amount; an unreadable box is zero, never a throw.</summary>
        public static decimal Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0m;
            return decimal.TryParse(text.Trim(),
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.CurrentCulture, out decimal value)
                ? value
                : 0m;
        }
    }
}
