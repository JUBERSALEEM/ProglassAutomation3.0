// ═══════════════════════════════════════════════════════════════════════
// FILE: Models/ProformaInvoice/SavedInvoice/ProformaListRow.cs
// PROJECT: ProGlassAutomation
//
// PERF-9 (2026-08-30). A narrow, read-only projection for the
// SavedInvoicesList. The previous list page called
// IProformaInvoiceRepository.GetAllAsync(), which materialises EVERY
// column of EVERY row — including FullSnapshotJson, a complete serialized
// invoice snapshot blob (lines, spec groups, pricing, terms) — just to
// render "REF / COMPANY / CUSTOMER / PROJECT / STATUS / TOTAL / DATE".
// With hundreds of invoices that is hundreds of multi-hundred-KB blobs
// deserialized by EF on every page open. This type holds only the indexed
// scalar columns the list actually reads, with AsNoTracking, so the blob
// column is never touched. It is intentionally NOT an entity — no key,
// never tracked, never saved.
//
// It mirrors the fields SavedInvoicesListViewModel passes into
// SavedInvoiceListItem, so the two stay in step.
// ═══════════════════════════════════════════════════════════════════════

using System;

namespace ProGlassAutomation.Models.ProformaInvoice.SavedInvoice
{
    /// <summary>
    /// Read-only projection of <see cref="SavedProformaInvoice"/> for the
    /// saved-invoices list. Excludes FullSnapshotJson by design.
    /// </summary>
    public sealed class ProformaListRow
    {
        public int Id { get; set; }
        public string ProformaId { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string Status { get; set; } = "Draft";
        public decimal GrandTotalVatInclusive { get; set; }

        /// <summary>Proforma issue date (the "PI date" shown on the document).</summary>
        public DateTime IssueDate { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// ERP lifecycle hardening (Phase 1): true when the invoice has been
        /// voided (soft-closed). Voided rows are hidden from the list by
        /// default and revealed by the "Show voided" toggle.
        /// </summary>
        public bool IsVoid { get; set; }
    }
}
