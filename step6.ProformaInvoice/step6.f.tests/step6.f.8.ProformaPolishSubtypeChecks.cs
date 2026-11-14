using ProGlassAutomation.Models.Proforma;

namespace ProGlassAutomation.Step6Tests
{
    internal static partial class Program
    {
        private static void PolishSubtypeChecks()
        {
            var group = new SpecGroup
            {
                GroupNumber = 1, GlassType = GlassBuilds.Sgu, SpecName = "Custom glass",
                OuterGlassColor = "Clear"
            };
            group.Rows.Add(new SpecRow { W1 = 1000, H1 = 2000, Qty = 1 });
            Check("all-side polish is 6 lm", PolishMetricCalculator.CalculateRegular(group) == 6m);
            group.PolishSubType = PolishSubType.BothHeightSide;
            Check("both-height polish is 4 lm", group.PolishLm == 4m);
            group.PolishSubType = PolishSubType.BothWidthSide;
            Check("both-width polish is 2 lm", group.PolishLm == 2m);
            group.PolishSubType = PolishSubType.BothHeightOneWidth;
            Check("two heights and one width polish is 5 lm", group.PolishLm == 5m);
            group.PolishSubType = PolishSubType.OneHeightOneWidth;
            Check("one height and one width polish is 3 lm", group.PolishLm == 3m);
            group.PolishSubType = PolishSubType.OneHeightSide;
            Check("one-height polish is 2 lm", group.PolishLm == 2m);
            group.PolishSubType = PolishSubType.OneWidthSide;
            Check("one-width polish is 1 lm", group.PolishLm == 1m);
            group.PolishSubType = PolishSubType.AllSidePolishAsPerDwg;
            Check("as-per-drawing formula matches the legacy measured formula", group.PolishLm == 6m);
            group.PolishSubType = PolishSubType.Custom;
            group.CustomPolishQuantity = 7.25m;
            Check("custom polish uses its manual quantity", group.PolishLm == 7.25m);
            group.CustomPolishDescription = "Polish one height edge as marked on drawing";
            Check("custom quantity and description survive a group clone",
                group.Clone().CustomPolishQuantity == 7.25m
                && group.Clone().CustomPolishDescription == group.CustomPolishDescription);
            group.CustomPolishQuantity = -2m;
            Check("custom polish quantity cannot be negative", group.CustomPolishQuantity == 0m);
            group.CustomPolishQuantity = 7.25m;

            var automatic = group.Clone();
            automatic.PolishSubType = PolishSubType.Auto;
            automatic.CustomPolishQuantity = 0m;
            automatic.SpecName = "Both Height + One Width Side Polish As Per DWG";
            Check("Auto detects drawing-based edge formula", automatic.PolishLm == 5m);
            automatic.SpecName = "One Width Side Polish";
            Check("Auto detects one-width-only polishing", automatic.PolishLm == 1m);

            var dgu = new SpecGroup
            {
                GroupNumber = 2, GlassType = GlassBuilds.Dgu, SpecName = "DGU 6+6",
                OuterGlassColor = "Clear", InnerGlassColor = "Grey",
                PolishSubType = PolishSubType.AllSidePolish
            };
            dgu.Rows.Add(new SpecRow { W1 = 1000, H1 = 2000, W2 = 900, H2 = 1800, Qty = 2 });
            Check("unmatched DGU counts polish on both measured panes",
                dgu.PolishLm == 22.8m && dgu.MatchedPolishLm == 0m);
            var matched = dgu.Clone();
            matched.InnerGlassColor = "Clear";
            Check("matched DGU moves both panes to matched polish",
                matched.PolishLm == 0m && matched.MatchedPolishLm == 22.8m);
            matched.Rows[0].W2 = 0; matched.Rows[0].H2 = 0;
            Check("matched DGU without second dimensions counts pane one twice",
                matched.MatchedPolishLm == 24m);
            Check("all legacy polish choices are available", PolishSubTypeInfo.All.Count == 14);
        }
    }
}