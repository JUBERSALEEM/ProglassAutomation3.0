using System;
using ProGlassAutomation.Models.Proforma;

namespace ProGlassAutomation.Step6Tests
{
    internal static partial class Program
    {
        private static void PolishSubtypeChecks()
        {
            Eq("all-side polish", 6m,
                PolishMetricCalculator.CalculatePanel(1000, 2000, PolishSubType.AllSidePolish));
            Eq("both-height polish", 4m,
                PolishMetricCalculator.CalculatePanel(1000, 2000, PolishSubType.BothHeightSide));
            Eq("both-width polish", 2m,
                PolishMetricCalculator.CalculatePanel(1000, 2000, PolishSubType.BothWidthSide));
            Eq("both-height plus one width polish", 5m,
                PolishMetricCalculator.CalculatePanel(1000, 2000, PolishSubType.BothHeightOneWidth));
            Eq("one-height plus one-width polish", 3m,
                PolishMetricCalculator.CalculatePanel(1000, 2000, PolishSubType.OneHeightOneWidth));
            Eq("one-height polish", 2m,
                PolishMetricCalculator.CalculatePanel(1000, 2000, PolishSubType.OneHeightSide));
            Eq("one-width polish", 1m,
                PolishMetricCalculator.CalculatePanel(1000, 2000, PolishSubType.OneWidthSide));

            Check("spec-name detection preserves the DWG subtype",
                PolishSubTypeInfo.Detect("Both Height Side Polish As Per DWG")
                    == PolishSubType.BothHeightSideAsPerDwg);

            var mixed = new SpecGroup
            {
                GroupNumber = 1, GlassType = GlassBuilds.Dgu,
                OuterGlassColor = "Clear", InnerGlassColor = "Grey",
                PolishSubType = PolishSubType.BothHeightSide
            };
            mixed.Rows.Add(new SpecRow
            {
                SerialNumber = 1, W1 = 1000, H1 = 2000,
                W2 = 900, H2 = 1900, Qty = 2
            });
            Eq("mixed DGU uses selected edge subtype on both measured panes",
                15.6m, mixed.PolishLm);

            var matched = mixed.Clone();
            matched.GroupNumber = 2;
            matched.InnerGlassColor = "Clear";
            Eq("matched DGU uses both measured panes for selected subtype",
                15.6m, matched.MatchedPolishLm);

            matched.Rows[0].W2 = 0;
            matched.Rows[0].H2 = 0;
            Eq("matched DGU counts first pane twice when second dimensions are absent",
                16m, matched.MatchedPolishLm);

            matched.PolishSubType = PolishSubType.Custom;
            Eq("custom polish leaves quantity to the manual charge metric",
                0m, matched.MatchedPolishLm);
        }
    }
}
