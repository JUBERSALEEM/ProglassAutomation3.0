using System.Collections.Generic;

namespace ProGlassAutomation.Models.DeliveryNote.Common.DeliveryType
{
    /// <summary>
    /// Display metadata for each delivery type.
    /// Stored as string in DB — compare with string constants,
    /// never cast to/from enum directly.
    ///
    /// Brush keys verified against App.xaml resource dictionary:
    ///   TintGreenBrush, TintAmberBrush, TintRedBrush — all confirmed.
    ///   BUG-56 fix: TintOrangeBrush was NOT present in resources —
    ///   replaced with TintAmberBrush for Rework type.
    /// </summary>
    public static class DeliveryTypeMeta
    {
        public static readonly IReadOnlyDictionary<string, TypeInfo> All =
            new Dictionary<string, TypeInfo>(
                System.StringComparer.OrdinalIgnoreCase)
            {
                ["Original"] = new("Original", "📋", "TintGreenBrush"),
                ["Dummy"] = new("Dummy", "📌", "TintAmberBrush"),
                ["Rework"] = new("Rework", "🔧", "TintAmberBrush"),  // BUG-56: was TintOrangeBrush
                ["Return"] = new("Return", "↩️", "TintRedBrush")
            };

        /// <summary>
        /// Returns display info for the given type string.
        /// Case-insensitive lookup. Returns Unknown placeholder if not found.
        /// </summary>
        public static TypeInfo Get(string type)
        {
            if (!string.IsNullOrWhiteSpace(type) &&
                All.TryGetValue(type, out var info))
                return info;

            return new TypeInfo("Unknown", "❓", "SurfaceStrokeBrush");
        }
    }

    /// <summary>
    /// Display metadata for one delivery type value.
    /// </summary>
    public sealed class TypeInfo
    {
        public string DisplayName { get; }
        public string Icon { get; }
        public string BrushKey { get; }

        public TypeInfo(string displayName, string icon, string brushKey)
        {
            DisplayName = displayName;
            Icon = icon;
            BrushKey = brushKey;
        }
    }
}