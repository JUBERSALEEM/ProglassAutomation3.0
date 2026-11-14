// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/ProformaInvoice/Step4MaterialIntegrity.cs
// PROJECT: ProGlassAutomation
// MODULE: Step 4 — material identity is either resolved or INVALID
//
// PHASE A (controlled). A missing Sheet Store price, an empty colour, an
// unparseable thickness, or a pane without sheet W/H is an INVALID INPUT
// STATE. It is never a valid zero, and it is never the leftover SGU 46
// default. Callers must not invent a price to keep quoting/optimization
// moving.
// ═══════════════════════════════════════════════════════════════════════

using System;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;

namespace ProGlassAutomation.Services.ProformaInvoice
{
    public static class Step4MaterialIntegrity
    {
        public readonly struct Verdict
        {
            public Verdict(bool isValid, string reason)
            {
                IsValid = isValid;
                Reason = reason ?? string.Empty;
            }

            public bool IsValid { get; }
            public string Reason { get; }

            public static Verdict Ok() => new(true, string.Empty);

            public static Verdict Fail(string reason) =>
                new(false, string.IsNullOrWhiteSpace(reason)
                    ? "Material identity is incomplete."
                    : reason.Trim());
        }

        /// <summary>
        /// True only when Step 4 recorded an explicit Sheet Store hit.
        /// Null/unknown is unresolved — never inferred from a positive price.
        /// </summary>
        public static bool IsStorePriceResolved(bool? flag) => flag == true;

        /// <summary>
        /// Selling price when present, else purchase. Null when the store
        /// row is missing or both columns are zero — never 0 as a valid rate.
        /// </summary>
        public static decimal? ResolveStorePrice(
            (decimal PurchasePerSqm, decimal SellPerSqm, string Supplier)? price)
        {
            if (!price.HasValue) return null;

            decimal sell = price.Value.SellPerSqm > 0m
                ? price.Value.SellPerSqm
                : price.Value.PurchasePerSqm;

            return sell > 0m ? sell : null;
        }

        public static string DescribeMiss(
            string paneLabel, string? color, int thicknessMm, int widthMm, int heightMm)
        {
            string pane = string.IsNullOrWhiteSpace(paneLabel) ? "Pane" : paneLabel.Trim();

            if (string.IsNullOrWhiteSpace(color))
                return pane + ": colour is empty or unparseable.";
            if (thicknessMm <= 0)
                return pane + ": thickness is missing or unparseable.";
            if (widthMm <= 0 || heightMm <= 0)
                return pane + ": sheet width/height is not set — cannot look up a store price.";

            return pane + ": no Sheet Store row for "
                + color.Trim() + " " + thicknessMm.ToString() + "mm "
                + widthMm.ToString() + "×" + heightMm.ToString() + ".";
        }

        public static Verdict EvaluatePane(
            string paneLabel,
            string? color,
            string? thickness,
            string? treatment,
            int widthMm,
            int heightMm,
            decimal pricePerSqm,
            bool? priceResolvedFromStore)
        {
            string pane = string.IsNullOrWhiteSpace(paneLabel) ? "Pane" : paneLabel.Trim();

            if (string.IsNullOrWhiteSpace(color))
                return Verdict.Fail(pane + ": colour is empty or unparseable.");

            int mm = SheetStorePricingService.ParseMm(thickness);
            if (mm <= 0)
                return Verdict.Fail(pane + ": thickness is missing or unparseable.");

            if (string.IsNullOrWhiteSpace(treatment))
                return Verdict.Fail(pane + ": processing/type is empty.");

            if (widthMm <= 0 || heightMm <= 0)
                return Verdict.Fail(pane + ": sheet width/height is not set.");

            // true = store-resolved. false and null/unknown are NOT provenance.
            // A leftover 46/48/25 must never become valid because the flag is missing.
            if (priceResolvedFromStore != true)
                return Verdict.Fail(
                    DescribeMiss(pane, color, mm, widthMm, heightMm)
                    + " Store resolution is missing or failed — leftover prices are not a valid store identity.");

            if (pricePerSqm <= 0m)
                return Verdict.Fail(pane + ": store price is missing or zero — not a valid material state.");

            return Verdict.Ok();
        }

        public static Verdict Evaluate(Step4PricingSnapshot? snapshot)
        {
            if (snapshot == null)
                return Verdict.Fail("No Step 4 pricing snapshot.");

            string type = (snapshot.SelectedGlassTypeCode ?? string.Empty).Trim().ToUpperInvariant();
            return type switch
            {
                "SGU" => EvaluateSgu(snapshot.Sgu),
                "DGU" => EvaluateDgu(snapshot.Dgu),
                "LAM" => EvaluateLam(snapshot.Lam),
                "DGU+LAM" => EvaluateDguLam(snapshot.DguLam),
                _ => Verdict.Fail("Glass type is empty or unsupported.")
            };
        }

        public static Verdict EvaluateSgu(SguPricingPanelSnapshot? sgu)
        {
            var p = sgu ?? new SguPricingPanelSnapshot();
            return EvaluatePane(
                "SGU", p.GlassColor, p.SelectedThickness, p.SelectedTreatment,
                p.SheetWidthMm, p.SheetHeightMm, p.BasePricePerSqm, p.PriceResolvedFromStore);
        }

        public static Verdict EvaluateDgu(DguPricingPanelSnapshot? dgu)
        {
            var p = dgu ?? new DguPricingPanelSnapshot();
            var outer = EvaluatePane(
                "DGU outer", p.OuterGlassColor, p.OuterThickness, p.OuterTreatment,
                p.OuterSheetWidthMm, p.OuterSheetHeightMm, p.OuterBasePrice, p.OuterPriceResolvedFromStore);
            if (!outer.IsValid) return outer;

            return EvaluatePane(
                "DGU inner", p.InnerGlassColor, p.InnerThickness, p.InnerTreatment,
                p.InnerSheetWidthMm, p.InnerSheetHeightMm, p.InnerBasePrice, p.InnerPriceResolvedFromStore);
        }

        public static Verdict EvaluateLam(LamPricingPanelSnapshot? lam)
        {
            var p = lam ?? new LamPricingPanelSnapshot();
            var outer = EvaluatePane(
                "LAM outer", p.OuterGlassColor, p.OuterThickness, p.OuterTreatment,
                p.OuterSheetWidthMm, p.OuterSheetHeightMm, p.OuterBasePrice, p.OuterPriceResolvedFromStore);
            if (!outer.IsValid) return outer;

            return EvaluatePane(
                "LAM inner", p.InnerGlassColor, p.InnerThickness, p.InnerTreatment,
                p.InnerSheetWidthMm, p.InnerSheetHeightMm, p.InnerBasePrice, p.InnerPriceResolvedFromStore);
        }

        public static Verdict EvaluateDguLam(DguLamPricingPanelSnapshot? dguLam)
        {
            var p = dguLam ?? new DguLamPricingPanelSnapshot();
            var outer = EvaluatePane(
                "DGU+LAM outer", p.OuterGlassColor, p.OuterThickness, p.OuterTreatment,
                p.OuterSheetWidthMm, p.OuterSheetHeightMm, p.OuterBasePrice, p.OuterPriceResolvedFromStore);
            if (!outer.IsValid) return outer;

            var lamOuter = EvaluatePane(
                "DGU+LAM lam outer", p.LamOuterColor, p.LamOuterThickness, p.LamOuterTreatment,
                p.LamOuterSheetWidthMm, p.LamOuterSheetHeightMm, p.LamOuterBasePrice, p.LamOuterPriceResolvedFromStore);
            if (!lamOuter.IsValid) return lamOuter;

            return EvaluatePane(
                "DGU+LAM lam inner", p.LamInnerColor, p.LamInnerThickness, p.LamInnerTreatment,
                p.LamInnerSheetWidthMm, p.LamInnerSheetHeightMm, p.LamInnerBasePrice, p.LamInnerPriceResolvedFromStore);
        }

        /// <summary>
        /// Gate used by Step 5 and Job Order optimization. Only
        /// MaterialIsValid == true may proceed. false and null/unresolved
        /// are refused even if leftover numbers look filled.
        /// </summary>
        public static Verdict EvaluateSpecGroup(SpecGroupSnapshot? group)
        {
            if (group == null)
                return Verdict.Fail("Spec group is missing.");

            if (group.MaterialIsValid != true)
            {
                return Verdict.Fail(string.IsNullOrWhiteSpace(group.MaterialInvalidReason)
                    ? "Step 4 material is unresolved or invalid — optimization cannot use this spec."
                    : group.MaterialInvalidReason);
            }

            string type = (group.SelectedGlassType ?? string.Empty).Trim().ToUpperInvariant();
            if (type.Length == 0)
                return Verdict.Fail("Spec " + group.GroupNumber.ToString() + ": glass type is empty.");

            if (string.IsNullOrWhiteSpace(group.SelectedSpecName))
                return Verdict.Fail("Spec " + group.GroupNumber.ToString() + ": specification name is empty.");

            var outer = RequireSheet(
                "Spec " + group.GroupNumber.ToString() + " outer",
                group.OuterGlassColor, group.OuterSheetWidthMm, group.OuterSheetHeightMm, group.OuterSheetPricePerSqm);
            if (!outer.IsValid) return outer;

            if (type is "DGU" or "LAM" or "DGU+LAM")
            {
                string innerColor = type == "DGU+LAM"
                    ? FirstNonBlank(group.InnerGlassColor, group.OuterGlassColor)
                    : group.InnerGlassColor;
                var inner = RequireSheet(
                    "Spec " + group.GroupNumber.ToString() + " inner",
                    innerColor, group.InnerSheetWidthMm, group.InnerSheetHeightMm, group.InnerSheetPricePerSqm);
                if (!inner.IsValid) return inner;
            }

            if (type == "DGU+LAM")
            {
                string thirdColor = FirstNonBlank(group.ThirdGlassColor, group.InnerGlassColor);
                var third = RequireSheet(
                    "Spec " + group.GroupNumber.ToString() + " lam inner",
                    thirdColor, group.ThirdSheetWidthMm, group.ThirdSheetHeightMm, group.ThirdSheetPricePerSqm);
                if (!third.IsValid) return third;
            }

            return Verdict.Ok();
        }

        public static Verdict EvaluateSpecGroups(
            System.Collections.Generic.IEnumerable<SpecGroupSnapshot>? groups)
        {
            if (groups == null)
                return Verdict.Fail("Spec groups are missing.");

            bool any = false;
            foreach (var group in groups)
            {
                if (group == null) continue;
                any = true;
                var v = EvaluateSpecGroup(group);
                if (!v.IsValid) return v;
            }

            return any
                ? Verdict.Ok()
                : Verdict.Fail("No spec groups.");
        }

        private static Verdict RequireSheet(
            string label, string? color, int widthMm, int heightMm, decimal price)
        {
            if (string.IsNullOrWhiteSpace(color))
                return Verdict.Fail(label + ": colour is empty or unparseable.");
            if (widthMm <= 0 || heightMm <= 0)
                return Verdict.Fail(label + ": sheet width/height is not set.");
            if (price <= 0m)
                return Verdict.Fail(label + ": store price is missing or zero — not a valid material state.");
            return Verdict.Ok();
        }

        private static string FirstNonBlank(params string?[] values)
        {
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
            return string.Empty;
        }
    }
}
