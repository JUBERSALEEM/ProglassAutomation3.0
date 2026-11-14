// ═══════════════════════════════════════════════════════════════════════
// FILE: Models/ProformaInvoice/SavedInvoice/ProformaAnalyticsRow.cs
// PROJECT: ProGlassAutomation
// MODULE: Proforma Invoice — analytics projection
//
// PERF-2. A narrow, read-only row for dashboard aggregation.
//
// SavedProformaInvoice carries FullSnapshotJson: a complete serialized
// invoice snapshot, one per row. Dashboards were loading every one of
// those blobs through GetAllAsync purely to count invoices and sum
// totals. On a developer database with 20 invoices that is invisible;
// on a real one it is the dominant cost of opening a dashboard.
//
// This type holds only the five scalar columns the dashboards actually
// read. It is intentionally NOT an entity — it has no key, is never
// tracked, and must never be saved.
// ═══════════════════════════════════════════════════════════════════════

using System;

namespace ProGlassAutomation.Models.ProformaInvoice.SavedInvoice
{
    /// <summary>
    /// Read-only projection of <see cref="SavedProformaInvoice"/> for
    /// dashboard metrics. Excludes FullSnapshotJson by design.
    /// </summary>
    public sealed class ProformaAnalyticsRow
    {
        public string Status { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public decimal GrandTotalVatInclusive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime IssueDate { get; set; }
    }
}
