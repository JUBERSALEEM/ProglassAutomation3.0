// ═══════════════════════════════════════════════════════════════════════
// FILE: Models/JobOrder/SavedJobOrder/JobOrderAnalyticsRow.cs
// PROJECT: ProGlassAutomation
//
// PERF. A narrow, read-only projection for analytics aggregation (the AI
// context builder and any dashboard). SavedJobOrder carries FullSnapshotJson
// (a complete serialized snapshot, one per row); loading every blob just to
// count statuses and read the delivery date is pure waste. This type holds
// only the scalar columns those consumers actually read. It is intentionally
// NOT an entity — no key, never tracked, never saved.
// ═══════════════════════════════════════════════════════════════════════

using System;

namespace ProGlassAutomation.Models.JobOrder.SavedJobOrder
{
    /// <summary>
    /// Read-only projection of <see cref="SavedJobOrder"/> for analytics.
    /// Excludes FullSnapshotJson by design.
    /// </summary>
    public sealed class JobOrderAnalyticsRow
    {
        public string JobOrderId { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime? DeliveryDate { get; set; }
    }
}
