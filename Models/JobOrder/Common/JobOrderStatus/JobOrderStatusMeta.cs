using System.Collections.Generic;

namespace ProGlassAutomation.Models.JobOrder.Common.JobOrderStatus
{
    /// <summary>
    /// Maps JobOrderStatus enum values to display metadata:
    /// user-facing label, semantic brush key (for pills/badges),
    /// and tint brush key (for card backgrounds).
    ///
    /// Extracted from the enum file per project rule:
    /// nested classes in enum files can crash XamlWriter.Save().
    /// </summary>
    public static class JobOrderStatusMeta
    {
        private static readonly Dictionary<JobOrderStatus, (string Label, string BrushKey, string TintKey)> _map =
            new()
            {
                { JobOrderStatus.Draft,        ("Draft",                 "NeutralBrush",  "TintGrayBrush")   },
                { JobOrderStatus.Released,     ("Released to factory",   "InfoBrush",     "TintBlueBrush")   },
                { JobOrderStatus.InProduction, ("In Production",         "WarningBrush",  "TintAmberBrush")  },
                { JobOrderStatus.Completed,    ("Completed",             "SuccessBrush",  "TintGreenBrush")  },
                { JobOrderStatus.Delivered,    ("Delivered",             "AccentBrush",   "TintIndigoBrush") }
            };

        /// <summary>User-facing label for the given status.</summary>
        public static string GetLabel(JobOrderStatus status) =>
            _map.TryGetValue(status, out var m) ? m.Label : status.ToString();

        /// <summary>
        /// Theme brush KEY name for status pills / icon badges.
        /// Resolve via BrushKeyConverter at render time.
        /// </summary>
        public static string GetBrushKey(JobOrderStatus status) =>
            _map.TryGetValue(status, out var m) ? m.BrushKey : "NeutralBrush";

        /// <summary>
        /// Theme brush KEY name for card background tint.
        /// Resolve via BrushKeyConverter at render time.
        /// </summary>
        public static string GetTintKey(JobOrderStatus status) =>
            _map.TryGetValue(status, out var m) ? m.TintKey : "TintGrayBrush";

        /// <summary>All statuses in workflow order (for dropdowns).</summary>
        public static IEnumerable<JobOrderStatus> AllStatuses => new[]
        {
            JobOrderStatus.Draft,
            JobOrderStatus.Released,
            JobOrderStatus.InProduction,
            JobOrderStatus.Completed,
            JobOrderStatus.Delivered
        };
    }
}