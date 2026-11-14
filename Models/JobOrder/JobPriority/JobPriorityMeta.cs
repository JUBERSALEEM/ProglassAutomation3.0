using System.Collections.Generic;

namespace ProGlassAutomation.Models.JobOrder.Common.JobPriority
{
    /// <summary>
    /// Maps JobPriority enum values to display metadata:
    /// user-facing label, semantic brush key, and pill style key.
    ///
    /// Priority colors follow the Phase 8.3 design system convention:
    /// High = Danger (red), Medium = Success (green), Low = Neutral (gray).
    /// See screenshot #4 (task list with priority pills).
    /// </summary>
    public static class JobPriorityMeta
    {
        private static readonly Dictionary<JobPriority, (string Label, string BrushKey, string PillStyleKey)> _map =
            new()
            {
                { JobPriority.High,   ("High",   "DangerBrush",  "PriorityPillHighStyle")   },
                { JobPriority.Medium, ("Medium", "SuccessBrush", "PriorityPillMediumStyle") },
                { JobPriority.Low,    ("Low",    "NeutralBrush", "PriorityPillLowStyle")    }
            };

        /// <summary>User-facing label for the given priority.</summary>
        public static string GetLabel(JobPriority priority) =>
            _map.TryGetValue(priority, out var m) ? m.Label : priority.ToString();

        /// <summary>
        /// Theme brush KEY name for priority indicator (icon, left border, etc).
        /// Resolve via BrushKeyConverter at render time.
        /// </summary>
        public static string GetBrushKey(JobPriority priority) =>
            _map.TryGetValue(priority, out var m) ? m.BrushKey : "NeutralBrush";

        /// <summary>
        /// Full pill style key from PillStyles.xaml (Phase 8.3 design system).
        /// Use directly as {StaticResource ...} in XAML.
        /// </summary>
        public static string GetPillStyleKey(JobPriority priority) =>
            _map.TryGetValue(priority, out var m) ? m.PillStyleKey : "PriorityPillLowStyle";

        /// <summary>All priorities highest-first (for dropdowns).</summary>
        public static IEnumerable<JobPriority> AllPriorities => new[]
        {
            JobPriority.High,
            JobPriority.Medium,
            JobPriority.Low
        };
    }
}