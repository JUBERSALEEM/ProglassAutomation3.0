using System;
using System.Linq;
using ProGlassAutomation.Models.Proforma;

namespace ProGlassAutomation.Step6Tests
{
    internal static partial class Program
    {
        private static bool UInsertSeedCopiesToGroup()
        {
            var section = new PricingSection
            {
                GlassType = GlassBuilds.Dgu,
                HasUInsertExtra = true, UInsertPricePerSqm = 13m
            };
            var group = new SpecGroup();
            group.ApplyPricing(section, new PricingResult
            {
                IsComplete = true, SpecName = "DGU + U-Insert", RatePerSqm = 100m,
                CostCard = new SpecCostCard()
            });
            return group.HasUInsertExtra && group.UInsertPricePerSqm == 13m
                && group.Clone().HasUInsertExtra && group.Clone().UInsertPricePerSqm == 13m;
        }

        private static void ChargeAutomation()
        {
            Check("spaced DGU + LAM labels normalize as the compound build",
                GlassBuilds.Normalize("DGU + LAM") == GlassBuilds.DguLam
                && OtherChargeAutomation.EffectiveGlassType("", "DGU + LAM") == GlassBuilds.DguLam);

            var group = new SpecGroup
            {
                GroupNumber = 1,
                SpecName = "DGU + U-Insert + Overlap + Shutter Bonding + Fanhole + Cutouts and Holes + Beveled + Frosted",
                GlassType = GlassBuilds.Dgu,
                OuterGlassColor = "Clear",
                InnerGlassColor = "Clear"
            };
            group.Rows.Add(new SpecRow
            {
                SerialNumber = 1, W1 = 1000, H1 = 2000,
                W2 = 900, H2 = 1900, Qty = 2
            });

            var suggestions = OtherChargeAutomation.Suggest(group)
                .Select(item => item.TemplateKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (string key in new[]
            {
                "ARGON_GAS", "SILICON_BED", "U_INSERT", "OVERLAP",
                "SHUTTER_BONDING", "FAN_HOLES", "CUTOUT", "HOLES",
                "MITRING", "SANDBLASTING", "POLISH_MATCHED_LAM"
            })
                Check("legacy charge rule suggests " + key, suggestions.Contains(key));

            var sheet = new OtherChargesSheet();
            sheet.EnsureCatalogueRows();
            OtherChargeAutomation.Apply(sheet, new[] { group });
            Check("automation activates a named shutter-bonding row",
                sheet.Predefined.Single(row => row.TemplateKey == "SHUTTER_BONDING").IsActive);
            Check("automation activates a named fan-hole row",
                sheet.Predefined.Single(row => row.TemplateKey == "FAN_HOLES").IsActive);

            var uInsert = new SpecGroup
            {
                GroupNumber = 4,
                SpecName = "DGU + U-Insert",
                GlassType = GlassBuilds.Dgu,
                OuterGlassColor = "Clear",
                InnerGlassColor = "Clear",
                UInsertPricePerSqm = 25m
            };
            uInsert.Rows.Add(new SpecRow
            {
                SerialNumber = 1, W1 = 1000, H1 = 2000, Qty = 2
            });
            SpecGridTotals uInsertTotals = SpecGridTotals.From(new[] { uInsert });
            PricingSection pricingSection = PricingEstimator.NewSection(GlassBuilds.Dgu, 4);
            pricingSection.UInsertPricePerSqm = 25m;
            var pricingSnapshot = new SpecGroup();
            pricingSnapshot.ApplyPricing(pricingSection, PricingEstimator.Calculate(pricingSection));
            Check("Step 4 U-Insert rate is copied into the specification snapshot",
                pricingSnapshot.UInsertPricePerSqm == 25m);
            Check("Step 4 per-spec U-Insert rate is retained on the grid group",
                uInsert.UInsertPricePerSqm == 25m);
            Check("per-spec U-Insert amount overrides the invoice fallback rate",
                uInsertTotals.UInsertSqm == 4m && uInsertTotals.UInsertAmount(10m) == 100m);
            var uInsertSheet = new OtherChargesSheet();
            uInsertSheet.EnsureCatalogueRows();
            OtherChargeAutomation.Apply(uInsertSheet, new[] { uInsert });
            uInsertSheet.Recalculate(new[] { uInsert });
            Check("U-Insert catalogue row bills the per-spec amount",
                uInsertSheet.Predefined.Single(row => row.TemplateKey == "U_INSERT").Amount == 100m);
            Check("customer exports use the blended effective U-Insert rate",
                uInsertSheet.Predefined.Single(row => row.TemplateKey == "U_INSERT").EffectiveRate == 25m);

            var fallbackUInsert = uInsert.Clone();
            fallbackUInsert.UInsertPricePerSqm = 0m;
            Check("U-Insert falls back to invoice catalogue rate when Step 4 rate is blank",
                SpecGridTotals.From(new[] { fallbackUInsert }).UInsertAmount(10m) == 40m);

            SpecGridTotals totals = SpecGridTotals.From(new[] { group });
            PredefinedChargeRow matchedPolish = sheet.Predefined.Single(
                row => row.TemplateKey == "POLISH_MATCHED_LAM");
            sheet.Recalculate(new[] { group });
            Check("matched polish uses the geometry-aware metric without double multiplication",
                matchedPolish.Metric == ChargeMetric.MatchedPolishLm
                && matchedPolish.MetricMultiplier == 1m
                && matchedPolish.Quantity == totals.MatchedPolishLm
                && totals.MatchedPolishLm == totals.TotalLm);

            var sameGlassWithoutSecondSize = group.Clone();
            sameGlassWithoutSecondSize.Rows[0].W2 = 0;
            sameGlassWithoutSecondSize.Rows[0].H2 = 0;
            decimal expectedDoublePane = GlassMeasure.PanelLm(1000, 2000) * 2m * 2m;
            Check("matched polish counts the first pane twice when W2/H2 is absent",
                SpecGridTotals.From(new[] { sameGlassWithoutSecondSize }).MatchedPolishLm == expectedDoublePane);

            PredefinedChargeRow manual = sheet.Predefined.Single(row => row.TemplateKey == "SHUTTER_BONDING");
            manual.SetManualActive(false);
            OtherChargeAutomation.Apply(sheet, new[] { group });
            Check("automation preserves a manual off override", !manual.IsActive);

            var sqmOverlap = new SpecGroup
            {
                GroupNumber = 2,
                SpecName = "Overlap (SQM)",
                GlassType = GlassBuilds.Sgu,
                OuterGlassColor = "Clear",
                InnerGlassColor = "Grey"
            };
            var sqmKeys = OtherChargeAutomation.Suggest(sqmOverlap)
                .Select(item => item.TemplateKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Check("SQM overlap uses the area-based catalogue row",
                sqmKeys.Contains("OVERLAP_SQM") && !sqmKeys.Contains("OVERLAP"));

            var mixedLam = new SpecGroup
            {
                GroupNumber = 3,
                SpecName = "LAM 6+6",
                GlassType = GlassBuilds.Lam,
                OuterGlassColor = "Clear",
                InnerGlassColor = "Grey"
            };
            var mixedLamKeys = OtherChargeAutomation.Suggest(mixedLam)
                .Select(item => item.TemplateKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Check("unmatched laminate uses the regular polish row",
                mixedLamKeys.Contains("POLISH") && !mixedLamKeys.Contains("POLISH_MATCHED_LAM"));

            mixedLam.Rows.Add(new SpecRow
            {
                SerialNumber = 1, W1 = 800, H1 = 1800,
                W2 = 750, H2 = 1750, Qty = 1
            });
            var mixedSheet = new OtherChargesSheet();
            mixedSheet.EnsureCatalogueRows();
            OtherChargeAutomation.Apply(mixedSheet, new[] { group, mixedLam });
            mixedSheet.Recalculate(new[] { group, mixedLam });
            Check("regular and matched polish rows do not charge the same group twice",
                mixedSheet.Predefined.Single(row => row.TemplateKey == "POLISH").Quantity == mixedLam.PolishLm
                && mixedSheet.Predefined.Single(row => row.TemplateKey == "POLISH_MATCHED_LAM").Quantity == group.MatchedPolishLm);

            var legacyPolishSheet = new OtherChargesSheet();
            legacyPolishSheet.Predefined.Add(new PredefinedChargeRow
            {
                TemplateKey = "POLISH", Metric = ChargeMetric.TotalLm,
                MetricMultiplier = 1m, IsActive = false, HasManualActiveOverride = true
            });
            legacyPolishSheet.Predefined.Add(new PredefinedChargeRow
            {
                TemplateKey = "POLISH_MATCHED_LAM", Metric = ChargeMetric.TotalLm,
                MetricMultiplier = 2m, IsActive = false, HasManualActiveOverride = true
            });
            legacyPolishSheet.EnsureCatalogueRows();
            Check("saved legacy polish defaults migrate to geometry metrics",
                legacyPolishSheet.Predefined.Single(row => row.TemplateKey == "POLISH").Metric == ChargeMetric.PolishLm
                && legacyPolishSheet.Predefined.Single(row => row.TemplateKey == "POLISH_MATCHED_LAM").Metric == ChargeMetric.MatchedPolishLm
                && legacyPolishSheet.Predefined.Single(row => row.TemplateKey == "POLISH_MATCHED_LAM").MetricMultiplier == 1m);
            Check("polish metric migration preserves manual active state",
                legacyPolishSheet.Predefined.Where(row => row.TemplateKey == "POLISH" || row.TemplateKey == "POLISH_MATCHED_LAM")
                    .All(row => !row.IsActive && row.HasManualActiveOverride));

            var lamPerimeter = new SpecGroup
            {
                GroupNumber = 9, GlassType = GlassBuilds.Lam, SpecName = "LAM 6+6"
            };
            lamPerimeter.Rows.Add(new SpecRow { W1 = 1000, H1 = 2000, W2 = 1000, H2 = 2000, Qty = 1 });
            var dguPerimeter = lamPerimeter.Clone();
            dguPerimeter.GlassType = GlassBuilds.Dgu;
            Check("legacy LAM applies perimeter factor four to both panes",
                lamPerimeter.PerimeterFactor == 4 && lamPerimeter.TotalLm == 24m);
            Check("DGU retains perimeter factor two",
                dguPerimeter.PerimeterFactor == 2 && dguPerimeter.TotalLm == 12m);

            var targetA = new SpecGroup { GroupNumber = 20, GlassType = GlassBuilds.Sgu, SpecName = "6mm Clear" };
            targetA.Rows.Add(new SpecRow { W1 = 1000, H1 = 2000, Qty = 1 });
            var targetB = new SpecGroup { GroupNumber = 21, GlassType = GlassBuilds.Sgu, SpecName = "6mm Grey" };
            targetB.Rows.Add(new SpecRow { W1 = 500, H1 = 1000, Qty = 1 });
            var targetedSheet = new OtherChargesSheet();
            targetedSheet.Predefined.Add(new PredefinedChargeRow
            {
                TemplateKey = "CUSTOM_TEST", Unit = ChargeUnit.Lm, Metric = ChargeMetric.TotalLm,
                IsActive = true, Rate = 2m, TargetGroupNumbers = new System.Collections.Generic.List<int> { 20 }
            });
            targetedSheet.Recalculate(new[] { targetA, targetB });
            Check("predefined charges can target selected specification groups only",
                targetedSheet.Predefined[0].Quantity == targetA.TotalLm
                && targetedSheet.Predefined[0].Amount == targetA.TotalLm * 2m);
            Check("target group editor starts with explicit selected ids",
                targetedSheet.Predefined[0].TargetGroupsText == "20");
            targetedSheet.Predefined[0].TargetGroupsText = "ALL";
            targetedSheet.Recalculate(new[] { targetA, targetB });
            Check("ALL restores the invoice-wide target",
                targetedSheet.Predefined[0].TargetGroupNumbers.Count == 0
                && targetedSheet.Predefined[0].Quantity == targetA.TotalLm + targetB.TotalLm);
            targetedSheet.Predefined[0].TargetGroupsText = "21, 20, 21";
            Check("target group editor normalizes and de-duplicates ids",
                targetedSheet.Predefined[0].TargetGroupsText == "20, 21"
                && targetedSheet.Clone().Predefined[0].TargetGroupsText == "20, 21");

            var uInsertA = new SpecGroup
            {
                GroupNumber = 10, SpecName = "DGU 6+6", GlassType = GlassBuilds.Dgu,
                HasUInsertExtra = true, UInsertPricePerSqm = 12m
            };
            uInsertA.Rows.Add(new SpecRow { W1 = 1000, H1 = 2000, Qty = 1 });
            var uInsertB = new SpecGroup
            {
                GroupNumber = 11, SpecName = "DGU 6+6", GlassType = GlassBuilds.Dgu,
                HasUInsertExtra = true, UInsertPricePerSqm = 0m
            };
            uInsertB.Rows.Add(new SpecRow { W1 = 1000, H1 = 2000, Qty = 1 });
            var multiUInsertSheet = new OtherChargesSheet();
            multiUInsertSheet.EnsureCatalogueRows();
            OtherChargeAutomation.Apply(multiUInsertSheet, new[] { uInsertA, uInsertB });
            PredefinedChargeRow multiUInsertRow = multiUInsertSheet.Predefined.Single(row => row.TemplateKey == "U_INSERT");
            multiUInsertRow.Rate = 10m;
            multiUInsertSheet.Recalculate(new[] { uInsertA, uInsertB });
            Check("U-Insert uses pane-one cavity area and Step 4 per-spec rates before Step 6 fallback",
                multiUInsertRow.IsActive && multiUInsertRow.Quantity == 4m && multiUInsertRow.Amount == 44m);
            multiUInsertRow.Metric = ChargeMetric.TotalLm;
            multiUInsertSheet.Recalculate(new[] { uInsertA, uInsertB });
            Check("U-Insert cannot be switched away from its cavity-area metric",
                multiUInsertRow.Quantity == 4m && multiUInsertRow.Amount == 44m);
            Check("pricing section U-Insert seed survives clone and group creation",
                new PricingSection { UInsertPricePerSqm = 13m }.Clone().UInsertPricePerSqm == 13m
                && UInsertSeedCopiesToGroup());

            var wastage = new OtherChargesSheet();
            wastage.RebuildWastages(new[] { group });
            WastageChargeRow clear = wastage.Wastages.Single(row => row.ColorName == "Clear");
            Check("clear-glass wastage explains its optional status",
                clear.IsOptionalSuggestion && clear.SuggestionReason.Contains("optional", StringComparison.OrdinalIgnoreCase));
            Check("wastage sheet-cost quantity is editable and defaults to one",
                clear.SheetQuantity == 1 && wastage.Clone().Wastages.Single().SheetQuantity == 1);
            clear.SheetQuantity = -5;
            Check("wastage sheet-cost quantity cannot become negative", clear.SheetQuantity == 0);
        }
    }
}
