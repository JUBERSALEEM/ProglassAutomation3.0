// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.a.11.InvoiceSummary.cs
// MODULE: step6 — stage 5 (ADV step 7): discount, VAT and the totals waterfall.
//
// The waterfall, in the order the printed document shows it:
//   net subtotal       = glass + other charges
//   discount           = percent of net, or a fixed amount, never above net
//   subtotal before VAT = net − discount
//   VAT                = subtotal before VAT × 5% (UAE standard rate)
//   grand total        = subtotal before VAT + VAT
//
// VAT is applied AFTER the discount because UAE Federal Decree-Law 8/2017
// Article 34 values the supply net of discount. The discount is capped at the
// net subtotal so a typo can never produce a negative supply.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Text.Json.Serialization;

namespace ProGlassAutomation.Models.Proforma
{
    /// <summary>How a discount is expressed.</summary>
    public enum DiscountType
    {
        Percentage = 0,
        FixedAmount = 1
    }

    /// <summary>The stage-5 settings and the waterfall they produce.</summary>
    public sealed class InvoiceSummary
    {
        /// <summary>The only currency this module prints.</summary>
        public const string CurrencyCode = "AED";

        /// <summary>UAE standard VAT rate. Changed only by an owner decision.</summary>
        public const decimal DefaultVatPercent = 5m;

        public DiscountType DiscountMethod { get; set; } = DiscountType.Percentage;
        public decimal DiscountValue { get; set; }
        public bool IsVatApplicable { get; set; } = true;
        public decimal VatPercent { get; set; } = DefaultVatPercent;

        /// <summary>Stage 3's value, carried so the waterfall can be recomputed alone.</summary>
        public decimal GlassSubtotal { get; set; }

        /// <summary>Stage 4's value, carried for the same reason.</summary>
        public decimal OtherChargesSubtotal { get; set; }

        public decimal NetSubtotal { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal SubtotalBeforeVat { get; set; }
        public decimal VatAmount { get; set; }
        public decimal GrandTotal { get; set; }

        /// <summary>Recomputes every derived figure from the two subtotals.</summary>
        public void Recalculate()
        {
            decimal net = MoneyRounding.Round(GlassSubtotal + OtherChargesSubtotal);
            if (net < 0m) net = 0m;

            decimal discount = DiscountMethod == DiscountType.Percentage
                ? MoneyRounding.Round(net * MoneyRounding.ClampPercent(DiscountValue, 0m, 100m) / 100m)
                : MoneyRounding.Round(DiscountValue);
            if (discount > net) discount = net;
            if (discount < 0m) discount = 0m;

            decimal beforeVat = net - discount;
            decimal vat = IsVatApplicable
                ? MoneyRounding.Round(beforeVat * MoneyRounding.ClampPercent(VatPercent, 0m, 100m) / 100m)
                : 0m;

            NetSubtotal = net;
            DiscountAmount = discount;
            SubtotalBeforeVat = MoneyRounding.Round(beforeVat);
            VatAmount = vat;
            GrandTotal = MoneyRounding.Round(beforeVat + vat);
        }

        /// <summary>
        /// "Make the total 15,500" — sets a fixed discount that lands on the
        /// typed VAT-inclusive total. Returns false when the target is
        /// unreachable (at or above the undiscounted total), leaving the
        /// existing discount alone.
        /// </summary>
        public bool SetTargetGrandTotal(decimal targetGrandTotal)
        {
            decimal net = MoneyRounding.Round(GlassSubtotal + OtherChargesSubtotal);
            if (net <= 0m) return false;

            decimal discount = MoneyRounding.DiscountForTargetTotal(
                net, VatPercent, IsVatApplicable, targetGrandTotal);
            decimal achieved = MoneyRounding.GrandTotal(net, discount, VatPercent, IsVatApplicable);
            if (achieved != MoneyRounding.Round(targetGrandTotal)) return false;

            DiscountMethod = DiscountType.FixedAmount;
            DiscountValue = discount;
            Recalculate();
            return true;
        }

        /// <summary>Discount as a percentage of net, for reporting and warnings.</summary>
        [JsonIgnore]
        public decimal EffectiveDiscountPercent => NetSubtotal <= 0m
            ? 0m
            : MoneyRounding.RoundPercent(DiscountAmount / NetSubtotal * 100m);

        public InvoiceSummary Clone() => (InvoiceSummary)MemberwiseClone();
    }
}
