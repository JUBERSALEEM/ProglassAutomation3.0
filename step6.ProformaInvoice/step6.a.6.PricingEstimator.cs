// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.a.6.PricingEstimator.cs
// MODULE: step6 — stage 2's ONE rate calculation, for all four builds.
//
// The formulas are the reference module's approved arithmetic, unchanged:
//   SGU      (base ÷ yield + cutting + tempering) × (1 + margin)
//   DGU      ((outer + inner) ÷ yield + spacer) × (1 + margin)
//   LAM      ((outer + inner) ÷ yield + PVB + cutting + tempering) × (1 + margin)
//   DGU+LAM  ((outer + middle + inner) ÷ yield + spacer + interlayer
//             + cutting + tempering) × (1 + margin)
//
// The figure BEFORE the margin is the cost basis. Step 9's margin KPI is
// measured against that basis (never against the materials cost), because the
// quoted rate is built from cut area at quoted prices while the P&L is built
// from whole sheets at purchase prices — two different quantities that must
// not be compared by accident.
//
// A yield factor of zero is never divided by: it falls back to 1.0 and the
// result carries the warning. A silent reset to a wrong price is the defect
// the reference module recorded as BUG-40/41.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;

namespace ProGlassAutomation.Models.Proforma
{
    /// <summary>Rates, cost basis and provenance for one glass build.</summary>
    public static class PricingEstimator
    {
        /// <summary>The approved target uplift used when a section does not set one.</summary>
        public const decimal DefaultTargetUpliftPercent = 15m;

        /// <summary>Default yield: 15% of purchased glass is lost to cutting.</summary>
        public const decimal DefaultYieldFactor = 0.85m;

        public const decimal MinimumYieldFactor = 0.60m;
        public const decimal MaximumYieldFactor = 1.00m;
        public const decimal MaximumMarginPercent = 400m;

        /// <summary>Rates for a section, with every problem stated instead of hidden.</summary>
        public static PricingResult Calculate(PricingSection section)
        {
            var result = new PricingResult();
            if (section == null)
            {
                result.Warnings.Add("No pricing section was supplied.");
                return result;
            }

            GlassBuildType build = section.Build;
            if (build == GlassBuildType.Unknown)
            {
                result.Warnings.Add("Choose a glass type (SGU, DGU, LAM or DGU+LAM).");
                return result;
            }

            decimal yield = section.YieldFactor;
            if (yield < MinimumYieldFactor || yield > MaximumYieldFactor)
            {
                result.Warnings.Add(
                    "Yield factor must be between " + MinimumYieldFactor.ToString("0.##", CultureInfo.InvariantCulture) +
                    " and " + MaximumYieldFactor.ToString("0.##", CultureInfo.InvariantCulture) +
                    " — 1.00 was used for this calculation.");
                yield = 1m;
            }
            result.YieldFactorUsed = yield;

            // ── Required inputs ──────────────────────────────────────────
            var panes = section.Panes;
            var missing = new List<string>();
            for (int index = 0; index < panes.Count; index++)
            {
                if (panes[index].BasePricePerSqm <= 0m)
                    missing.Add("pane " + (index + 1).ToString(CultureInfo.InvariantCulture) + " rate");
            }
            if (GlassBuilds.HasSpacer(build) && section.SpacerPricePerSqm <= 0m)
                missing.Add("air spacer rate");
            if (missing.Count > 0)
            {
                result.Warnings.Add("Missing " + string.Join(", ", missing) + " — the rate cannot be calculated.");
                result.CostCard = BuildCostCard(section, 0m, 0m, yield);
                return result;
            }

            decimal margin = MoneyRounding.ClampPercent(section.MarginPercent, -100m, MaximumMarginPercent);
            decimal glassSum = 0m;
            decimal cutting = 0m;
            decimal tempering = 0m;
            foreach (GlassPane pane in panes)
            {
                glassSum += pane.BasePricePerSqm;
                cutting += pane.CuttingCostPerSqm;
                tempering += pane.TemperingCostPerSqm;
            }

            decimal glassPerYield = glassSum / yield;
            decimal production;
            decimal interlayerSell = 0m;

            switch (build)
            {
                case GlassBuildType.Sgu:
                    // Cutting and tempering are the SGU production rows.
                    production = cutting + tempering;
                    break;

                case GlassBuildType.Dgu:
                    // DGU's production row is the air spacer. Pane cutting and
                    // tempering are already inside the pane rates in this trade.
                    production = section.SpacerPricePerSqm;
                    break;

                case GlassBuildType.Lam:
                    // Lamination adds PVB AND per-pane cutting/tempering.
                    production = section.InterlayerSellPerSqm + cutting + tempering;
                    break;

                default: // DGU+LAM
                    interlayerSell = section.InterlayerSellPerSqm > 0m
                        ? section.InterlayerSellPerSqm
                        : section.InterlayerBuyPerSqm;
                    production = section.SpacerPricePerSqm + interlayerSell + cutting + tempering;
                    break;
            }

            decimal basis = glassPerYield + production;
            decimal rate = basis * (1m + margin / 100m);

            result.IsComplete = true;
            result.CostBasisPerSqm = MoneyRounding.Round(basis);
            result.RatePerSqm = MoneyRounding.Round(rate);
            result.SpecName = BuildSpecName(section);
            result.FormulaText = BuildFormulaText(section, build, glassSum, production, yield, margin);
            result.CostCard = BuildCostCard(section, result.CostBasisPerSqm, result.RatePerSqm, yield);

            if (margin < 0m)
                result.Warnings.Add("The margin is negative — this quotation is priced below its cost basis.");
            return result;
        }

        /// <summary>
        /// "6mm Clear FT + 12mm ASP + 6mm Clear FT". Generated from the section
        /// so an operator never types a specification name that disagrees with
        /// the panes beside it.
        /// </summary>
        public static string BuildSpecName(PricingSection section)
        {
            if (section == null) return "";
            GlassBuildType build = section.Build;
            string spacer = SpacerText(section);
            string interlayer = InterlayerText(section);

            return build switch
            {
                GlassBuildType.Sgu => section.Outer.Display,
                GlassBuildType.Dgu => section.Outer.Display + " + " + spacer + " + " + section.Inner.Display,
                GlassBuildType.Lam => section.Outer.Display + " + " + interlayer + " + " + section.Inner.Display,
                GlassBuildType.DguLam =>
                    section.Outer.Display + " + " + spacer + " + " + section.Middle.Display +
                    " + " + interlayer + " + " + section.Inner.Display,
                _ => ""
            };
        }

        private static string SpacerText(PricingSection section)
        {
            string width = (section.SpacerWidth ?? "").Trim();
            if (width.Length == 0) width = "12";
            if (!width.EndsWith("mm", StringComparison.OrdinalIgnoreCase)) width += "mm";
            return width + " ASP";
        }

        private static string InterlayerText(PricingSection section)
        {
            string thickness = (section.InterlayerThickness ?? "").Trim();
            string colour = (section.InterlayerColour ?? "").Trim();
            string core = thickness.Length == 0 ? "PVB" : thickness + " PVB";
            return colour.Length == 0 ? core : colour + " " + core;
        }

        private static string BuildFormulaText(
            PricingSection section, GlassBuildType build, decimal glassSum,
            decimal production, decimal yield, decimal margin)
        {
            string glass = PricingText.Number(glassSum) + " ÷ " + PricingText.Number(yield);
            string parts = build switch
            {
                GlassBuildType.Sgu => glass + " + cutting/tempering " + PricingText.Number(production),
                GlassBuildType.Dgu => glass + " + spacer " + PricingText.Number(production),
                GlassBuildType.Lam => glass + " + PVB/cutting/tempering " + PricingText.Number(production),
                _ => glass + " + spacer/PVB/cutting/tempering " + PricingText.Number(production)
            };
            return "(" + parts + ") × (1 + " + PricingText.Number(margin) + "%)";
        }

        private static SpecCostCard BuildCostCard(
            PricingSection section, decimal basis, decimal quotedRate, decimal yield)
        {
            var card = new SpecCostCard
            {
                GlassType = GlassBuilds.CodeOf(section.Build),
                SpecName = BuildSpecName(section),
                QuotedRatePerSqm = quotedRate,
                CostBasisPerSqm = basis,
                YieldFactor = yield,
                TargetUpliftPercent = section.TargetUpliftPercent > 0m
                    ? section.TargetUpliftPercent
                    : DefaultTargetUpliftPercent,
                // A build without a spacer or an interlayer must not carry a rate
                // for one, or Step 9 would cost a row the job never consumes.
                SpacerCostPerSqm = GlassBuilds.HasSpacer(section.Build)
                    ? section.SpacerPricePerSqm : 0m,
                CuttingCostPerSqm = 0m,
                TemperingCostPerSqm = 0m
            };

            // Provenance per pane: the sheet the group was quoted on and the rate
            // Step 9 must cost with. Only DGU+LAM has a middle (laminated outer)
            // pane; calling a DGU's second pane "Middle" would misdescribe it.
            string[] labels = section.Build switch
            {
                GlassBuildType.Sgu => new[] { "Outer" },
                GlassBuildType.DguLam => new[] { "Outer", "Middle", "Inner" },
                _ => new[] { "Outer", "Inner" }
            };
            var panes = section.Panes;
            for (int index = 0; index < panes.Count && index < labels.Length; index++)
            {
                GlassPane pane = panes[index];
                card.Panes.Add(new SpecCostPane
                {
                    PaneLabel = labels[index],
                    Colour = pane.Colour.Trim(),
                    SheetWidthMm = pane.SheetWidthMm,
                    SheetHeightMm = pane.SheetHeightMm,
                    CostPricePerSqm = pane.CostPricePerSqm,
                    CostBasis = pane.CostBasisLabel
                });
                card.CuttingCostPerSqm += pane.CuttingCostPerSqm;
                card.TemperingCostPerSqm += pane.TemperingCostPerSqm;
            }

            // Costing always prefers what the interlayer COSTS. The sell rate is
            // the quoted production row and is used only when no buy rate exists.
            card.InterlayerCostPerSqm = section.Build switch
            {
                GlassBuildType.Lam => section.InterlayerBuyPerSqm > 0m
                    ? section.InterlayerBuyPerSqm
                    : section.InterlayerSellPerSqm,
                GlassBuildType.DguLam => section.InterlayerBuyPerSqm > 0m
                    ? section.InterlayerBuyPerSqm
                    : section.InterlayerSellPerSqm,
                _ => 0m
            };

            // LAM and DGU+LAM price cutting/tempering as their own production
            // rows; SGU does too. DGU does not, so its card keeps the pane
            // cutting/tempering for display only and Step 9 must not add them.
            if (section.Build == GlassBuildType.Dgu)
            {
                card.CuttingCostPerSqm = 0m;
                card.TemperingCostPerSqm = 0m;
                card.InterlayerCostPerSqm = 0m;
            }

            return card;
        }

        /// <summary>A section ready to price: defaults that a first quotation can use.</summary>
        public static PricingSection NewSection(string glassType, int sectionNumber)
        {
            var section = new PricingSection
            {
                SectionNumber = sectionNumber,
                GlassType = GlassBuilds.Normalize(glassType).Length == 0
                    ? GlassBuilds.Sgu
                    : GlassBuilds.Normalize(glassType),
                YieldFactor = DefaultYieldFactor,
                InterlayerThickness = "0.38"
            };
            return section;
        }
    }
}
