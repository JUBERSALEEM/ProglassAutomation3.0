// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Optimization/OptimizationProgress.cs
// PROJECT: ProGlassAutomation
// MODULE: Optimization — live engine-progress payload (2026-09-17)
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Optimization
{
    /// <summary>One engine heartbeat during PackQuick / PackOptimize.</summary>
    public sealed class OptimizationProgress
    {
        /// <summary>
        /// Short stage tag for the log: CONFIG, SEARCH, SMART FIT,
        /// SMART PLACE, VERIFY, DONE, CANCELLED.
        /// </summary>
        public string Stage { get; init; } = string.Empty;

        /// <summary>
        /// Current activity one-liner — ALWAYS shown.
        /// </summary>
        public string Detail { get; init; } = string.Empty;

        /// <summary>
        /// Full line for the live log. Null/empty = activity-only update.
        /// </summary>
        public string? LogLine { get; init; }

        /// <summary>Highlight the log row green (new best / plan locked).</summary>
        public bool IsBest { get; init; }

        /// <summary>Highlight the log row amber (governor narrowing).</summary>
        public bool IsWarning { get; init; }

        /// <summary>
        /// Search tier for GOVERNOR reports ("NARROWED" / "MINIMUM");
        /// empty on all other reports.
        /// </summary>
        public string Tier { get; init; } = string.Empty;

        /// <summary>0..100 within the current cutting group.</summary>
        public int Percent { get; init; }
    }
}
