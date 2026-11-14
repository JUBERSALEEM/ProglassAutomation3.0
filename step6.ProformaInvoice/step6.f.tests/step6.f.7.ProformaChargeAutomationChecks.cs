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

            var wastage = new OtherChargesSheet();
            wastage.RebuildWastages(new[] { group });
            WastageChargeRow clear = wastage.Wastages.Single(row => row.ColorName == "Clear");
            Check("clear-glass wastage explains its optional status",
                clear.IsOptionalSuggestion && clear.SuggestionReason.Contains("optional", StringComparison.OrdinalIgnoreCase));
            Check("wastage sheet-cost quantity is editable and defaults to one",
                clear.SheetQuantity == 1 && wastage.Clone().Wastages.Single().SheetQuantity == 1);
        }
    }
}
