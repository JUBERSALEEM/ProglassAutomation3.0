// ═══════════════════════════════════════════════════════════════════════
// FILE: Models/TaxInvoice/SavedTaxInvoice/TaxInvoiceAnalyticsRow.cs
// PROJECT: ProGlassAutomation
//
// PERF. A narrow, read-only projection for analytics aggregation (the AI
// context builder and dashboards). SavedTaxInvoice carries FullSnapshotJson
// (with attachments base64-encoded inside it); loading every blob just to
// count statuses, sum totals and read due dates is pure waste. This type
// holds only the scalar columns those consumers read. Intentionally NOT an
// entity — no key, never tracked, never saved.
// ═══════════════════════════════════════════════════════════════════════

using System;

namespace ProGlassAutomation.Models.TaxInvoice.SavedTaxInvoice
{
    /// <summary>
    /// Read-only projection of <see cref="SavedTaxInvoice"/> for analytics.
    /// Excludes FullSnapshotJson by design.
    /// </summary>
    public sealed class TaxInvoiceAnalyticsRow
    {
        public string InvoiceNumber { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime DueDate { get; set; }
        public decimal GrandTotal { get; set; }
    }
}
