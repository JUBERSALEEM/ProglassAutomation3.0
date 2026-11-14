using System;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Models.Proforma
{
    /// <summary>Per-group edge-polish selection; Auto retains legacy spec-name detection.</summary>
    public enum PolishSubType
    {
        Auto = 0,
        AllSidePolish = 1,
        AllSidePolishAsPerDwg = 2,
        BothHeightSide = 3,
        BothHeightSideAsPerDwg = 4,
        BothWidthSide = 5,
        BothWidthSideAsPerDwg = 6,
        BothHeightOneWidth = 7,
        BothHeightOneWidthAsPerDwg = 8,
        OneHeightOneWidth = 9,
        OneHeightOneWidthAsPerDwg = 10,
        OneHeightSide = 11,
        OneWidthSide = 12,
        Custom = 13
    }

    public sealed class PolishSubTypeMeta
    {
        public PolishSubType Type { get; init; }
        public string DisplayLabel { get; init; } = "";
        public string FormulaHint { get; init; } = "";
    }

    public static class PolishSubTypeInfo
    {
        public static IReadOnlyList<PolishSubTypeMeta> All { get; } = new List<PolishSubTypeMeta>
        {
            new() { Type = PolishSubType.Auto, DisplayLabel = "Auto from specification", FormulaHint = "Uses the polish wording in the specification; otherwise full-edge polish." },
            new() { Type = PolishSubType.AllSidePolish, DisplayLabel = "All Side Polish", FormulaHint = "(W + H) / 1000 × 2 × Qty" },
            new() { Type = PolishSubType.AllSidePolishAsPerDwg, DisplayLabel = "All Side Polish As Per DWG", FormulaHint = "(W + H) / 1000 × 2 × Qty" },
            new() { Type = PolishSubType.BothHeightSide, DisplayLabel = "Both Height Side", FormulaHint = "H / 1000 × 2 × Qty" },
            new() { Type = PolishSubType.BothHeightSideAsPerDwg, DisplayLabel = "Both Height Side As Per DWG", FormulaHint = "H / 1000 × 2 × Qty" },
            new() { Type = PolishSubType.BothWidthSide, DisplayLabel = "Both Width Side", FormulaHint = "W / 1000 × 2 × Qty" },
            new() { Type = PolishSubType.BothWidthSideAsPerDwg, DisplayLabel = "Both Width Side As Per DWG", FormulaHint = "W / 1000 × 2 × Qty" },
            new() { Type = PolishSubType.BothHeightOneWidth, DisplayLabel = "Both Height + One Width", FormulaHint = "(H × 2 + W) / 1000 × Qty" },
            new() { Type = PolishSubType.BothHeightOneWidthAsPerDwg, DisplayLabel = "Both Height + One Width As Per DWG", FormulaHint = "(H × 2 + W) / 1000 × Qty" },
            new() { Type = PolishSubType.OneHeightOneWidth, DisplayLabel = "One Height + One Width", FormulaHint = "(H + W) / 1000 × Qty" },
            new() { Type = PolishSubType.OneHeightOneWidthAsPerDwg, DisplayLabel = "One Height + One Width As Per DWG", FormulaHint = "(H + W) / 1000 × Qty" },
            new() { Type = PolishSubType.OneHeightSide, DisplayLabel = "One Height Side", FormulaHint = "H / 1000 × Qty" },
            new() { Type = PolishSubType.OneWidthSide, DisplayLabel = "One Width Side", FormulaHint = "W / 1000 × Qty" },
            new() { Type = PolishSubType.Custom, DisplayLabel = "Custom (Manual)", FormulaHint = "Set the Polish charge metric to Fixed Quantity and enter the measured quantity." }
        };

        public static PolishSubTypeMeta GetInfo(PolishSubType type) =>
            All.FirstOrDefault(item => item.Type == type) ?? All[0];

        public static PolishSubType? Detect(string? specName)
        {
            string spec = specName ?? "";
            if (Contains(spec, "All Side Polish As Per DWG")) return PolishSubType.AllSidePolishAsPerDwg;
            if (Contains(spec, "All Side Polish")) return PolishSubType.AllSidePolish;
            if (Contains(spec, "Polished Edges")) return PolishSubType.AllSidePolish;
            if (Contains(spec, "Both Height Side Polish As Per DWG")) return PolishSubType.BothHeightSideAsPerDwg;
            if (Contains(spec, "Both Height Side Polish")) return PolishSubType.BothHeightSide;
            if (Contains(spec, "Both Width Side Polish As Per DWG")) return PolishSubType.BothWidthSideAsPerDwg;
            if (Contains(spec, "Both Width Side Polish")) return PolishSubType.BothWidthSide;
            if (Contains(spec, "Both Height + One Width Side Polish As Per DWG")) return PolishSubType.BothHeightOneWidthAsPerDwg;
            if (Contains(spec, "Both Height + One Width Side Polish")) return PolishSubType.BothHeightOneWidth;
            if (Contains(spec, "One Height + One Width Side Polish As Per DWG")) return PolishSubType.OneHeightOneWidthAsPerDwg;
            if (Contains(spec, "One Height + One Width Side Polish")) return PolishSubType.OneHeightOneWidth;
            if (Contains(spec, "One Height Side Polish")) return PolishSubType.OneHeightSide;
            if (Contains(spec, "One Width Side Polish")) return PolishSubType.OneWidthSide;
            return null;
        }

        private static bool Contains(string source, string value) =>
            source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Geometry-based polish length, shared by charge calculation and P&amp;L.</summary>
    public static class PolishMetricCalculator
    {
        public static PolishSubType Resolve(SpecGroup group)
        {
            if (group.PolishSubType != PolishSubType.Auto)
                return group.PolishSubType;
            return PolishSubTypeInfo.Detect(group.SpecName) ?? PolishSubType.AllSidePolish;
        }

        public static decimal CalculateRegular(SpecGroup group)
        {
            string build = GlassBuilds.Normalize(group.GlassType);
            bool doubleGlass = IsDoubleGlass(build);
            bool matchedDouble = doubleGlass && group.IsMatchedSameGlass;
            bool lamLike = build == GlassBuilds.Lam || build == GlassBuilds.DguLam;
            bool polishNamed = group.PolishSubType != PolishSubType.Auto
                || PolishSubTypeInfo.Detect(group.SpecName).HasValue
                || (group.SpecName ?? "").IndexOf("POLISH", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasPaneColour = !string.IsNullOrWhiteSpace(group.OuterGlassColor);
            if (matchedDouble || !(lamLike || polishNamed || hasPaneColour)) return 0m;

            PolishSubType type = Resolve(group);
            if (type == PolishSubType.Custom) return 0m;
            decimal total = 0m;
            foreach (SpecRow row in group.Rows)
            {
                if (row.Qty <= 0) continue;
                if (row.W1 > 0 && row.H1 > 0)
                    total += CalculatePanel(row.W1, row.H1, type) * row.Qty;
                if (doubleGlass && row.W2 > 0 && row.H2 > 0)
                    total += CalculatePanel(row.W2, row.H2, type) * row.Qty;
            }
            return Math.Round(total, 3);
        }

        public static decimal CalculateMatched(SpecGroup group)
        {
            string build = GlassBuilds.Normalize(group.GlassType);
            if (!IsDoubleGlass(build) || !group.IsMatchedSameGlass) return 0m;

            PolishSubType type = Resolve(group);
            if (type == PolishSubType.Custom) return 0m;
            decimal total = 0m;
            foreach (SpecRow row in group.Rows)
            {
                if (row.Qty <= 0 || row.W1 <= 0 || row.H1 <= 0) continue;
                decimal first = CalculatePanel(row.W1, row.H1, type);
                decimal second = row.W2 > 0 && row.H2 > 0
                    ? CalculatePanel(row.W2, row.H2, type) : first;
                total += (first + second) * row.Qty;
            }
            return Math.Round(total, 3);
        }

        public static decimal CalculatePanel(int widthMm, int heightMm, PolishSubType type)
        {
            if (widthMm <= 0 || heightMm <= 0 || type == PolishSubType.Custom || type == PolishSubType.Auto)
                return 0m;
            decimal width = widthMm;
            decimal height = heightMm;
            return type switch
            {
                PolishSubType.AllSidePolish or PolishSubType.AllSidePolishAsPerDwg =>
                    (width + height) / 1000m * 2m,
                PolishSubType.BothHeightSide or PolishSubType.BothHeightSideAsPerDwg =>
                    height / 1000m * 2m,
                PolishSubType.BothWidthSide or PolishSubType.BothWidthSideAsPerDwg =>
                    width / 1000m * 2m,
                PolishSubType.BothHeightOneWidth or PolishSubType.BothHeightOneWidthAsPerDwg =>
                    (height * 2m + width) / 1000m,
                PolishSubType.OneHeightOneWidth or PolishSubType.OneHeightOneWidthAsPerDwg =>
                    (height + width) / 1000m,
                PolishSubType.OneHeightSide => height / 1000m,
                PolishSubType.OneWidthSide => width / 1000m,
                _ => 0m
            };
        }

        private static bool IsDoubleGlass(string build) =>
            build == GlassBuilds.Dgu || build == GlassBuilds.Lam || build == GlassBuilds.DguLam;
    }
}