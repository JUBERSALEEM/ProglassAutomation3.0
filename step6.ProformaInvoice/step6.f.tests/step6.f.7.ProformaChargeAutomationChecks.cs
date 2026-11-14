using System;
using System.Linq;
using ProGlassAutomation.Models.Proforma;

namespace ProGlassAutomation.Step6Tests
{
    internal static partial class Program
    {
        private static void ChargeAutomation()
        {
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
                mixedSheet.Predefined.Single(row => row.TemplateKey == "POLISH").Quantity == mixedLam.TotalLm
                && mixedSheet.Predefined.Single(row => row.TemplateKey == "POLISH_MATCHED_LAM").Quantity == group.MatchedPolishLm);

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
