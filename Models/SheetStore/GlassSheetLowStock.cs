// ═══════════════════════════════════════════════════════════════════════
// FILE: Models/SheetStore/GlassSheetLowStock.cs
// PROJECT: ProGlassAutomation
// NAMESPACE: ProGlassAutomation.Models.SheetStore
//
// PURPOSE
// ───────
// The one definition of when a glass sheet counts as low stock.
//
// WHY IT EXISTS (Phase 12, 2026-09-09)
// ──────────────────────────────────
// The threshold was declared twice with two values:
//
//     Agent/Tools/Read/GetInventoryTool.cs:31        LowStockThreshold = 2
//     Services/Dashboard/Core/DashboardMetricsService.cs:60   = 5
//
// Both mean the same thing — a sheet type whose remaining balance is at or
// below this number needs reordering — and both read the same GlassSheet
// column. So for any sheet holding 3 or 4 sheets, the Overview dashboard
// counted it as low stock while the AI agent, asked the same question from the
// same data at the same moment, said it was fine.
//
// Neither value is wrong. Having two is. This is the defect class the other
// five status/threshold fixes came from: no crash, no exception, just two
// numbers on two screens that do not agree.
//
// WHY 5 AND NOT 2
// ───────────────
// The dashboard is the number the operator has been looking at and acting on,
// and its own comment records that 5 "matches the threshold the Overview
// dashboard already used". Keeping 5 means the agent starts agreeing with the
// screen; choosing 2 would have silently shrunk a count someone already trusts.
// Moving the threshold in the earlier-warning direction is also the safe
// direction for a reorder alert.
//
// WHAT THIS DOES NOT COVER
// ────────────────────────
// InventoryItem (consumables) has a per-item ReorderLevel and its own
// IsLowStock property. That is a different, better mechanism and is untouched
// here. GlassSheet has no per-sheet reorder level, which is why a constant is
// the mechanism for it.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Models.SheetStore
{
    /// <summary>
    /// When a glass sheet type counts as low stock.
    /// </summary>
    public static class GlassSheetLowStock
    {
        /// <summary>
        /// A sheet type at or below this remaining balance needs reordering.
        /// </summary>
        public const int Threshold = 5;

        /// <summary>
        /// True when <paramref name="balance"/> (<c>Stock - Used</c>) is low.
        /// </summary>
        /// <remarks>
        /// Comparison is <c>&lt;=</c>, matching both call sites this replaced —
        /// a sheet holding exactly the threshold is already low, because the
        /// next job takes it under.
        /// </remarks>
        public static bool IsLow(int balance) => balance <= Threshold;
    }
}
