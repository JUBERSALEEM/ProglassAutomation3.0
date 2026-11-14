using System;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Models.Proforma
{
    public enum PolishSubType
    {
        Auto = 0, AllSidePolish, AllSidePolishAsPerDwg, BothHeightSide,
        BothHeightSideAsPerDwg, BothWidthSide, BothWidthSideAsPerDwg,
        BothHeightOneWidth, BothHeightOneWidthAsPerDwg, OneHeightOneWidth,
        OneHeightOneWidthAsPerDwg, OneHeightSide, OneWidthSide, Custom
    }

    public sealed class PolishSubTypeMeta
    {
        public PolishSubType Type { get; init; }
        public string DisplayLabel { get; init; } = "";
        public string FormulaHint { get; init; } = "";
    }

    public static class PolishSubTypeInfo
    {
        public static IReadOnlyList<PolishSubTypeMeta> All { get; } = Array.AsReadOnly(new[]
        {
            new PolishSubTypeMeta { Type = PolishSubType.Auto, DisplayLabel = "Auto (from specification)", FormulaHint = "Uses a named polish instruction; defaults to all sides." },
            new PolishSubTypeMeta { Type = PolishSubType.AllSidePolish, DisplayLabel = "All Side Polish", FormulaHint = "(W+H)/1000 × 2 × Qty" },
            new PolishSubTypeMeta { Type = PolishSubType.AllSidePolishAsPerDwg, DisplayLabel = "All Side Polish As Per DWG", FormulaHint = "(W+H)/1000 × 2 × Qty (As Per DWG)" },
            new PolishSubTypeMeta { Type = PolishSubType.BothHeightSide, DisplayLabel = "Both Height Side", FormulaHint = "H/1000 × 2 × Qty" },
            new PolishSubTypeMeta { Type = PolishSubType.BothHeightSideAsPerDwg, DisplayLabel = "Both Height Side As Per DWG", FormulaHint = "H/1000 × 2 × Qty (As Per DWG)" },
            new PolishSubTypeMeta { Type = PolishSubType.BothWidthSide, DisplayLabel = "Both Width Side", FormulaHint = "W/1000 × 2 × Qty" },
            new PolishSubTypeMeta { Type = PolishSubType.BothWidthSideAsPerDwg, DisplayLabel = "Both Width Side As Per DWG", FormulaHint = "W/1000 × 2 × Qty (As Per DWG)" },
            new PolishSubTypeMeta { Type = PolishSubType.BothHeightOneWidth, DisplayLabel = "Both Height + One Width Side Polish", FormulaHint = "(H×2 + W)/1000 × Qty" },
            new PolishSubTypeMeta { Type = PolishSubType.BothHeightOneWidthAsPerDwg, DisplayLabel = "Both Height + One Width Side Polish As Per DWG", FormulaHint = "(H×2 + W)/1000 × Qty (As Per DWG)" },
            new PolishSubTypeMeta { Type = PolishSubType.OneHeightOneWidth, DisplayLabel = "One Height + One Width Side Polish", FormulaHint = "(H + W)/1000 × Qty" },
            new PolishSubTypeMeta { Type = PolishSubType.OneHeightOneWidthAsPerDwg, DisplayLabel = "One Height + One Width Side Polish As Per DWG", FormulaHint = "(H + W)/1000 × Qty (As Per DWG)" },
            new PolishSubTypeMeta { Type = PolishSubType.OneHeightSide, DisplayLabel = "One Height Side Polish", FormulaHint = "H/1000 × Qty" },
            new PolishSubTypeMeta { Type = PolishSubType.OneWidthSide, DisplayLabel = "One Width Side Polish", FormulaHint = "W/1000 × Qty" },
            new PolishSubTypeMeta { Type = PolishSubType.Custom, DisplayLabel = "Custom (Manual)", FormulaHint = "Enter the manual linear-metre quantity below." }
        });

        public static PolishSubTypeMeta GetInfo(PolishSubType type) =>
            All.FirstOrDefault(item => item.Type == type) ?? All[0];

        public static string DisplaySpecName(SpecGroup group)
        {
            string name = (group.SpecName ?? "").Trim();
            if (group.PolishSubType == PolishSubType.Auto) return name;
            string polish = GetInfo(group.PolishSubType).DisplayLabel;
            return name.Length == 0 ? "Polish: " + polish : name + " · Polish: " + polish;
        }

        public static string DisplayGroupTitle(SpecGroup group)
        {
            string title = group.DisplayText;
            return group.PolishSubType == PolishSubType.Auto
                ? title : title + " · " + GetInfo(group.PolishSubType).DisplayLabel;
        }
    }

    public static class PolishMetricCalculator
    {
        public static decimal CalculateRegular(SpecGroup group) => Calculate(group, false);
        public static decimal CalculateMatched(SpecGroup group) => Calculate(group, true);

        private static decimal Calculate(SpecGroup group, bool matchedOnly)
        {
            if (group == null) return 0m;
            string build = GlassBuilds.Normalize(group.GlassType);
            bool doubleGlass = build == GlassBuilds.Dgu || build == GlassBuilds.Lam || build == GlassBuilds.DguLam;
            bool matched = doubleGlass && group.IsMatchedSameGlass;
            if ((matchedOnly && !matched) || (!matchedOnly && matched)) return 0m;

            PolishSubType subtype = group.PolishSubType == PolishSubType.Auto
                ? Detect(group.SpecName + " " + group.GroupName) : group.PolishSubType;
            if (subtype == PolishSubType.Custom)
                return Math.Round(Math.Max(0m, group.CustomPolishQuantity), 3);

            decimal total = 0m;
            foreach (SpecRow row in group.Rows)
            {
                if (row.Qty <= 0) continue;
                decimal lm = row.W1 > 0 && row.H1 > 0
                    ? CalculatePanel(row.W1, row.H1, subtype) : 0m;
                if (doubleGlass && row.W2 > 0 && row.H2 > 0)
                    lm += CalculatePanel(row.W2, row.H2, subtype);
                else if (matchedOnly && matched && row.W1 > 0 && row.H1 > 0)
                    lm += CalculatePanel(row.W1, row.H1, subtype);
                total += lm * row.Qty;
            }
            return Math.Round(total, 3);
        }

        private static PolishSubType Detect(string? specification)
        {
            string text = (specification ?? "").Replace('-', ' ').Replace('_', ' ');
            string upper = string.Join(" ", text.Split(
                new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
            bool dwg = upper.Contains("AS PER DWG", StringComparison.Ordinal)
                || upper.Contains("AS PER DRAWING", StringComparison.Ordinal);
            if (upper.Contains("ALL SIDE POLISH", StringComparison.Ordinal))
                return dwg ? PolishSubType.AllSidePolishAsPerDwg : PolishSubType.AllSidePolish;
            if (upper.Contains("BOTH HEIGHT + ONE WIDTH SIDE POLISH", StringComparison.Ordinal)
                || upper.Contains("BOTH HEIGHT ONE WIDTH SIDE POLISH", StringComparison.Ordinal))
                return dwg ? PolishSubType.BothHeightOneWidthAsPerDwg : PolishSubType.BothHeightOneWidth;
            if (upper.Contains("ONE HEIGHT + ONE WIDTH SIDE POLISH", StringComparison.Ordinal)
                || upper.Contains("ONE HEIGHT ONE WIDTH SIDE POLISH", StringComparison.Ordinal))
                return dwg ? PolishSubType.OneHeightOneWidthAsPerDwg : PolishSubType.OneHeightOneWidth;
            if (upper.Contains("BOTH HEIGHT SIDE POLISH", StringComparison.Ordinal))
                return dwg ? PolishSubType.BothHeightSideAsPerDwg : PolishSubType.BothHeightSide;
            if (upper.Contains("BOTH WIDTH SIDE POLISH", StringComparison.Ordinal))
                return dwg ? PolishSubType.BothWidthSideAsPerDwg : PolishSubType.BothWidthSide;
            if (upper.Contains("ONE HEIGHT SIDE POLISH", StringComparison.Ordinal)) return PolishSubType.OneHeightSide;
            if (upper.Contains("ONE WIDTH SIDE POLISH", StringComparison.Ordinal)) return PolishSubType.OneWidthSide;
            return PolishSubType.AllSidePolish;
        }

        public static decimal CalculatePanel(int width, int height, PolishSubType subtype) => subtype switch
        {
            PolishSubType.AllSidePolish or PolishSubType.AllSidePolishAsPerDwg => ((decimal)width + height) / 1000m * 2m,
            PolishSubType.BothHeightSide or PolishSubType.BothHeightSideAsPerDwg => (decimal)height / 1000m * 2m,
            PolishSubType.BothWidthSide or PolishSubType.BothWidthSideAsPerDwg => (decimal)width / 1000m * 2m,
            PolishSubType.BothHeightOneWidth or PolishSubType.BothHeightOneWidthAsPerDwg => ((decimal)height * 2m + width) / 1000m,
            PolishSubType.OneHeightOneWidth or PolishSubType.OneHeightOneWidthAsPerDwg => ((decimal)height + width) / 1000m,
            PolishSubType.OneHeightSide => (decimal)height / 1000m,
            PolishSubType.OneWidthSide => (decimal)width / 1000m,
            _ => 0m
        };
    }
}