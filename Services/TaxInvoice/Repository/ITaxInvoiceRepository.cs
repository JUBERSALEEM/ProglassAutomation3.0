using ProGlassAutomation.Models.TaxInvoice.Attachment;
using ProGlassAutomation.Models.TaxInvoice.SavedTaxInvoice;
using ProGlassAutomation.Models.TaxInvoice.TaxInvoiceFamily;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.TaxInvoice.Repository
{
    /// <summary>
    /// Repository contract for Tax Invoice persistence (taxinvoices.db).
    ///
    /// All methods are async.
    /// Items and Attachments cascade-loaded with parent via GetByIdAsync.
    /// List methods return lightweight TaxInvoiceListItem (no JSON parse).
    /// Family methods return TaxInvoiceFamily (computed aggregates).
    ///
    /// PATTERN: mirrors IDeliveryNoteRepository with invoice-specific additions:
    ///   • Family queries keyed by ProformaId (not JobOrderId)
    ///   • Sequence letter generation (A, B, C) for partial invoices
    ///   • Financial aggregation (Invoiced, Balance per family)
    ///   • Attachment CRUD with byte[] content (not file paths)
    ///   • Status-based filtering (Issued, Paid, Overdue, etc.)
    /// </summary>
    public interface ITaxInvoiceRepository
    {
        Task InitializeAsync();

        // ── Single invoice retrieval ──────────────────────────────────────

        Task<SavedTaxInvoice?> GetByIdAsync(int id);
        Task<SavedTaxInvoice?> GetByInvoiceNumberAsync(string invoiceNumber);

        // ── List retrieval ────────────────────────────────────────────────

        Task<List<TaxInvoiceListItem>> GetAllAsync();

        /// <summary>
        /// Bounded, DB-side search for the global-search box.
        ///
        /// Added 2026-09-08 (Phase 2, category D) for the same reason as
        /// <see cref="Services.DeliveryNote.Repository.IDeliveryNoteRepository.SearchAsync"/>:
        /// the search used to load every invoice to return at most 25 rows.
        /// </summary>
        Task<List<TaxInvoiceSearchRow>> SearchAsync(string term, int take);
        Task<List<TaxInvoiceListItem>> GetByProformaIdAsync(string proformaId);
        Task<List<TaxInvoiceListItem>> GetByStatusAsync(string status);
        Task<List<TaxInvoiceListItem>> GetDraftAsync();
        Task<List<TaxInvoiceListItem>> GetIssuedAsync();
        Task<List<TaxInvoiceListItem>> GetPaidAsync();
        Task<List<TaxInvoiceListItem>> GetOverdueAsync();

        // ── Family retrieval ──────────────────────────────────────────────

        /// <summary>
        /// Returns all invoice families (grouped by ProformaId).
        /// Each family contains aggregated Ordered/Invoiced/Balance totals.
        /// Ordered by latest issue date descending.
        /// </summary>
        Task<List<TaxInvoiceFamily>> GetAllFamiliesAsync();

        /// <summary>
        /// Returns the family for one specific ProformaId.
        /// Returns null if no invoices exist for this PI.
        /// </summary>
        Task<TaxInvoiceFamily?> GetFamilyAsync(string proformaId);

        // ── Financial summary ─────────────────────────────────────────────

        /// <summary>
        /// Returns total invoiced (SUM GrandTotal of Issued+Paid members)
        /// and balance for a ProformaId family.
        /// Fast query — no JSON parsing, uses indexed columns only.
        /// </summary>
        Task<InvoiceFamilySummary> GetFamilySummaryAsync(string proformaId);

        // ── Numbering ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns the next sequence number for a ProformaId family.
        /// First invoice = 1, second = 2, etc.
        /// </summary>
        Task<int> GetNextSequenceNumberAsync(string proformaId);

        /// <summary>
        /// Returns the next sequence letter for partial invoices.
        /// First partial = "A", second = "B", etc.
        /// Returns "A" if no partials exist yet.
        /// </summary>
        Task<string> GetNextSequenceLetterAsync(string proformaId);

        /// <summary>
        /// Builds the invoice number from a ProformaId.
        /// PI-2026-001 → INV-2026-001 (full)
        /// PI-2026-001 → INV-2026-001-A (partial, letter provided)
        /// </summary>
        Task<string> BuildInvoiceNumberAsync(
            string proformaId, string sequenceLetter = "");

        // ── CRUD ──────────────────────────────────────────────────────────

        Task<SavedTaxInvoice> InsertAsync(SavedTaxInvoice entity);
        Task UpdateAsync(SavedTaxInvoice entity);

        /// <summary>
        /// Update with optimistic-concurrency check (Phase 5). Pass the
        /// RowVersion the editor loaded; throws ConcurrencyConflictException if
        /// the stored version moved on. Pass null for legacy behaviour.
        /// </summary>
        Task UpdateAsync(SavedTaxInvoice entity, int? expectedRowVersion);
        Task DeleteAsync(int id);

        /// <summary>
        /// ERP lifecycle hardening (Phase 1): SOFT-CLOSE (void) an ISSUED tax
        /// invoice instead of deleting it — a fiscal document must never be
        /// erased. Keeps the row (number + audit trail survive), sets Status
        /// to the void target, and clears any DN "invoiced" links so those
        /// DNs can be re-invoiced. Permission-gated like DeleteAsync.
        /// </summary>
        Task VoidAsync(int id, string reason);

        /// <summary>
        /// Invoices (any family) whose PurchaseOrderNumber equals the given
        /// LPO/PO reference for the same customer. Used by the save path to
        /// WARN on a re-used LPO reference (audit P1, 2026-09-05): entering
        /// the same LPO number on a second, unrelated invoice is how
        /// double-billing happens at the printer. Same-family reuse
        /// (continuation invoices of one Proforma) is expected and is
        /// filtered by the caller, not blocked.
        /// Non-loading list items only — never entities.
        /// </summary>
        Task<List<TaxInvoiceListItem>> GetByPurchaseOrderAsync(
            string poNumber, string customerName);

        // ── Existence checks ──────────────────────────────────────────────

        Task<bool> ExistsByInvoiceNumberAsync(string invoiceNumber);
        Task<bool> HasAnyInvoiceAsync(string proformaId);

        // ── Status management ─────────────────────────────────────────────

        Task UpdateStatusAsync(string invoiceNumber, string newStatus);

        // ── Attachments ───────────────────────────────────────────────────

        Task<List<TaxInvoiceAttachment>> GetAttachmentsAsync(
            string invoiceNumber);

        Task<TaxInvoiceAttachment> AddAttachmentAsync(
            TaxInvoiceAttachment entity);

        Task DeleteAttachmentAsync(int attachmentId);

        Task<int> GetAttachmentCountAsync(string invoiceNumber);

        Task<Dictionary<string, int>> GetAttachmentCountsAsync(
            IEnumerable<string> invoiceNumbers);

        /// <summary>
        /// Lightweight analytics projection: InvoiceNumber, Status, DueDate,
        /// GrandTotal only. Excludes FullSnapshotJson (which contains base64
        /// attachments). Used by the AI context builder and dashboards to
        /// avoid loading every snapshot blob for simple counts and sums.
        /// </summary>
        Task<List<TaxInvoiceAnalyticsRow>> GetAnalyticsRowsAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  SUPPORTING DTOs
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fast financial summary for a ProformaId family.
    /// Computed from indexed columns — no JSON parsing.
    /// Used by TaxInvoiceListViewModel for hero card stats.
    /// </summary>
    /// <summary>
    /// What the global-search box needs from a tax invoice. See
    /// <see cref="Services.DeliveryNote.Repository.DeliveryNoteSearchRow"/>.
    /// </summary>
    public class TaxInvoiceSearchRow
    {
        public string InvoiceNumber { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string ProformaId { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public class InvoiceFamilySummary
    {
        public string ProformaId { get; set; } = "";
        public int InvoiceCount { get; set; }
        public decimal TotalInvoiced { get; set; }
        public decimal TotalVat { get; set; }
        public decimal TotalGrandTotal { get; set; }
        public decimal ProformaTotal { get; set; }
        public decimal Balance => System.Math.Max(0m, ProformaTotal - TotalInvoiced);
        public bool IsFullyInvoiced => Balance == 0m;
    }
}