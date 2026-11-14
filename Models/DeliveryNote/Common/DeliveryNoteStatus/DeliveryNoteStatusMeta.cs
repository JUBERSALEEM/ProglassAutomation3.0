using System.Collections.Generic;

namespace ProGlassAutomation.Models.DeliveryNote.Common.DeliveryNoteStatus
{
    /// <summary>
    /// Display metadata for each DN status.
    /// Status is stored as string in DB — compare with string constants,
    /// never cast to/from enum directly.
    ///
    /// Brush keys verified against App.xaml resource dictionary:
    ///   TintAmberBrush, TintBlueBrush, TintIndigoBrush,
    ///   TintGreenBrush, AccentBrush — all confirmed present.
    /// </summary>
    public static class DeliveryNoteStatusMeta
    {
        public static readonly IReadOnlyDictionary<string, StatusInfo> All =
            new Dictionary<string, StatusInfo>(
                System.StringComparer.OrdinalIgnoreCase)
            {
                ["Draft"] = new("Dispatching", "📝", "TintAmberBrush"),
                ["Dispatching"] = new("Dispatching", "📝", "TintAmberBrush"),
                ["Dispatched"] = new("Dispatched", "🚚", "TintBlueBrush"),
                ["Delivered"] = new("Delivered", "📦", "TintIndigoBrush"),
                ["Signed"] = new("Signed", "✍️", "TintGreenBrush"),
                ["Received"] = new("Received", "✅", "AccentBrush"),
                // ERP lifecycle hardening (Phase 1): a voided (soft-closed)
                // delivery note carries the Cancelled status.
                ["Cancelled"] = new("Cancelled", "🚫", "TintRedBrush")
            };

        /// <summary>
        /// Returns display info for the given status string.
        /// Case-insensitive lookup. Returns Unknown placeholder if not found.
        /// </summary>
        public static StatusInfo Get(string status)
        {
            if (!string.IsNullOrWhiteSpace(status) &&
                All.TryGetValue(status, out var info))
                return info;

            return new StatusInfo("Unknown", "❓", "SurfaceStrokeBrush");
        }
    }

    /// <summary>
    /// Display metadata for one DN status value.
    /// </summary>
    public sealed class StatusInfo
    {
        public string DisplayName { get; }
        public string Icon { get; }
        public string BrushKey { get; }

        public StatusInfo(string displayName, string icon, string brushKey)
        {
            DisplayName = displayName;
            Icon = icon;
            BrushKey = brushKey;
        }
    }
}