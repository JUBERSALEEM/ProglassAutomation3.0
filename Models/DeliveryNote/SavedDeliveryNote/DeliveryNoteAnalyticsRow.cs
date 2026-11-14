// ═══════════════════════════════════════════════════════════════════════
// FILE: Models/DeliveryNote/SavedDeliveryNote/DeliveryNoteAnalyticsRow.cs
// PROJECT: ProGlassAutomation
//
// PERF. A narrow, read-only projection for analytics aggregation (the AI
// context builder and dashboards). SavedDeliveryNote carries FullSnapshotJson;
// loading every blob just to count statuses and read the delivery date is
// waste. This type holds only the scalar columns those consumers read.
// Intentionally NOT an entity — no key, never tracked, never saved.
// ═══════════════════════════════════════════════════════════════════════

using System;

namespace ProGlassAutomation.Models.DeliveryNote.SavedDeliveryNote
{
    /// <summary>
    /// Read-only projection of <see cref="SavedDeliveryNote"/> for analytics.
    /// Excludes FullSnapshotJson by design.
    /// </summary>
    public sealed class DeliveryNoteAnalyticsRow
    {
        public string DeliveryNoteId { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime DeliveryDate { get; set; }
        public bool HasTaxInvoice { get; set; }
    }
}
