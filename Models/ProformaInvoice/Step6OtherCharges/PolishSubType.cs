using System.Collections.Generic;

namespace ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges
{
    /// <summary>
    /// Polish sub-type controls how LM is calculated per glass row.
    /// Each sub-type has its own perimeter formula.
    /// </summary>
    public enum PolishSubType
    {
        AllSidePolish,
        BothHeightSide,
        BothWidthSide,
        AllSidePolishAsPerDwg,
        BothHeightSideAsPerDwg,
        BothWidthSideAsPerDwg,
        BothHeightOneWidth,
        BothHeightOneWidthAsPerDwg,
        OneHeightOneWidth,
        OneHeightOneWidthAsPerDwg,
        OneHeightSide,
        OneWidthSide,
        Custom
    }

    /// <summary>
    /// Metadata for a polish sub-type (label + formula hint).
    /// Top-level class (not nested) so WPF XAML serializer can handle it.
    /// </summary>
    public class PolishSubTypeMeta
    {
        public PolishSubType Type { get; set; }
        public string DisplayLabel { get; set; } = "";
        public string FormulaHint { get; set; } = "";

        public override string ToString() => DisplayLabel;
    }

    /// <summary>
    /// Static registry of all polish sub-types.
    /// </summary>
    public static class PolishSubTypeInfo
    {
        public static List<PolishSubTypeMeta> All { get; } = new()
        {
            new PolishSubTypeMeta
            {
                Type = PolishSubType.AllSidePolish,
                DisplayLabel = "All Side Polish",
                FormulaHint = "(W+H)/1000 × 2 × Qty"
            },
            new PolishSubTypeMeta
            {
                Type = PolishSubType.AllSidePolishAsPerDwg,
                DisplayLabel = "All Side Polish As Per DWG",
                FormulaHint = "(W+H)/1000 × 2 × Qty (As Per DWG)"
            },
            new PolishSubTypeMeta
            {
                Type = PolishSubType.BothHeightSide,
                DisplayLabel = "Both Height Side",
                FormulaHint = "H/1000 × 2 × Qty"
            },
            new PolishSubTypeMeta
            {
                Type = PolishSubType.BothHeightSideAsPerDwg,
                DisplayLabel = "Both Height Side As Per DWG",
                FormulaHint = "H/1000 × 2 × Qty (As Per DWG)"
            },
            new PolishSubTypeMeta
            {
                Type = PolishSubType.BothWidthSide,
                DisplayLabel = "Both Width Side",
                FormulaHint = "W/1000 × 2 × Qty"
            },
            new PolishSubTypeMeta
            {
                Type = PolishSubType.BothWidthSideAsPerDwg,
                DisplayLabel = "Both Width Side As Per DWG",
                FormulaHint = "W/1000 × 2 × Qty (As Per DWG)"
            },
            new PolishSubTypeMeta
            {
                Type = PolishSubType.BothHeightOneWidth,
                DisplayLabel = "Both Height + One Width Side Polish",
                FormulaHint = "(H×2 + W)/1000 × Qty"
            },
            new PolishSubTypeMeta
            {
                Type = PolishSubType.BothHeightOneWidthAsPerDwg,
                DisplayLabel = "Both Height + One Width Side Polish As Per DWG",
                FormulaHint = "(H×2 + W)/1000 × Qty (As Per DWG)"
            },
            new PolishSubTypeMeta
            {
                Type = PolishSubType.OneHeightOneWidth,
                DisplayLabel = "One Height + One Width Side Polish",
                FormulaHint = "(H + W)/1000 × Qty"
            },
            new PolishSubTypeMeta
            {
                Type = PolishSubType.OneHeightOneWidthAsPerDwg,
                DisplayLabel = "One Height + One Width Side Polish As Per DWG",
                FormulaHint = "(H + W)/1000 × Qty (As Per DWG)"
            },
            new PolishSubTypeMeta
            {
                Type = PolishSubType.OneHeightSide,
                DisplayLabel = "One Height Side Polish",
                FormulaHint = "H/1000 × Qty"
            },
            new PolishSubTypeMeta
            {
                Type = PolishSubType.OneWidthSide,
                DisplayLabel = "One Width Side Polish",
                FormulaHint = "W/1000 × Qty"
            },
            new PolishSubTypeMeta
            {
                Type = PolishSubType.Custom,
                DisplayLabel = "Custom (Manual)",
                FormulaHint = "Enter description + manual qty"
            }
        };

        public static PolishSubTypeMeta GetInfo(PolishSubType type)
        {
            foreach (var info in All)
                if (info.Type == type) return info;
            return All[0];
        }
    }
}