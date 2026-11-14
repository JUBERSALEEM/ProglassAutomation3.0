using ProGlassAutomation.Models.DeliveryNote.SavedDeliveryNote;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.DeliveryNote.Repository
{
    /// <summary>
    /// Repository contract for delivery note persistence (deliverynotes.db).
    ///
    /// All methods are async. Items and Attachments are cascade-loaded
    /// with their parent DN via GetByIdAsync and GetByJobOrderIdAsync.
    /// List methods return lightweight DeliveryNoteListItem (no JSON parse).
    ///
    /// Phase 10.5c: Added GetReturnedAsync() for the Returned tab.
    /// Phase 10.6 : Added attachment CRUD.
    /// Phase 10.6b: Added GetAttachmentCountAsync for per-sibling counts.
    /// Phase 10.7 : Added GetPreviouslyDeliveredMapAsync for self-healing PREV.
    /// Phase 10.8b: DeliveryHistoryEntry carries cumulative timeline.
    /// Ship 8c-2  : Added GetSqmSummariesByJobOrderAsync.
    ///
    /// CHAT 19: Master DN system added.
    ///   GetMasterDnsAsync            — list all Master DNs (Tab 6)
    ///   GetMasterDnForJobOrderAsync  — load single Master DN for a JO
    ///   CreateMasterDnAsync          — auto-called after JO insert
    ///   UpdateMasterDnAsync          — recalculates after real DN dispatch
    ///   BuildMasterDnIdAsync         — MDN-YYYY-NNN format
    ///   GetAllAsync                  — now excludes Master DNs
    ///   GetPreparedAsync             — now excludes Master DNs
    ///   GetDeliveredAsync            — now excludes Master DNs
    ///   HasAnyDeliveryAsync          — now excludes Master DNs
    ///
    /// CHAT 19 INVOICED:
    ///   MarkAsInvoicedAsync          — marks single DN as invoiced after TI saved
    ///   MarkMultipleAsInvoicedAsync  — marks multiple DNs as invoiced (Master Invoice)
    ///   Both called by TaxInvoiceDetailViewModel.SaveAsync() after TI insert/update.
    ///   Sets HasTaxInvoice = true and TaxInvoiceId on the DN record.
    ///   Drives invoiced badge in DN list and history sidebar.
    ///   Both are idempotent — safe to call multiple times.
    /// </summary>
    public interface IDeliveryNoteRepository
    {
        Task InitializeAsync();

        // ── Single DN retrieval ──────────────────────────────────────────

        Task<SavedDeliveryNote?> GetByIdAsync(int id);
        Task<SavedDeliveryNote?> GetByDeliveryNoteIdAsync(string deliveryNoteId);

        // ── CHAT 19 — Master DN retrieval ────────────────────────────────

        /// <summary>
        /// Returns the Master DN entity for a specific Job Order.
        /// Returns null if no Master DN exists (pre-Chat 19 JOs).
        /// </summary>
        Task<SavedDeliveryNote?> GetMasterDnForJobOrderAsync(string jobOrderId);

        /// <summary>
        /// Returns all Master DNs for Tab 6 (Master DN list).
        /// Ordered by UpdatedAt descending.
        /// </summary>
        Task<List<DeliveryNoteListItem>> GetMasterDnsAsync();

        /// <summary>
        /// Creates a Master DN for a Job Order.
        /// Called automatically by JobOrderRepository.InsertAsync.
        /// Guard: only one Master DN per JO — returns null if already exists.
        /// </summary>
        Task<SavedDeliveryNote?> CreateMasterDnAsync(
            string jobOrderId,
            string proformaId,
            string customerName,
            string projectName,
            string masterDnId,
            List<DeliveryNoteItem> items);

        /// <summary>
        /// Recalculates Master DN delivered/balance totals after a real
        /// DN is dispatched. Updates PreviouslyDeliveredQty on each item
        /// so SELECT ALL REMAINING shows correct balance pcs.
        /// Confirmed (non-Draft) real DNs only — drafts do not count.
        /// triggeredByDnId = the real DN that caused this update (audit).
        /// </summary>
        Task UpdateMasterDnAsync(string jobOrderId, string triggeredByDnId);

        /// <summary>
        /// Builds the Master DN ID from a JO ID.
        /// Format: MDN-2026-001 (from JOB-2026-001).
        /// </summary>
        Task<string> BuildMasterDnIdAsync(string jobOrderId);

        // ── CHAT 19 INVOICED — mark DNs as invoiced ──────────────────────

        /// <summary>
        /// Marks a single DN as invoiced after Tax Invoice is saved.
        /// Sets HasTaxInvoice = true and TaxInvoiceId = taxInvoiceId.
        /// Called by TaxInvoiceDetailViewModel.SaveAsync() after insert/update.
        /// Idempotent — safe to call multiple times on same DN.
        /// Only updates indexed columns — no snapshot rebuild needed.
        /// </summary>
        Task MarkAsInvoicedAsync(string deliveryNoteId, string taxInvoiceId);

        /// <summary>
        /// Marks multiple DNs as invoiced in one operation.
        /// Used when a Master Invoice covers multiple sibling DNs.
        /// All DNs in the list get the same taxInvoiceId.
        /// deliveryNoteIds parsed from DeliveryNoteReference field (comma-separated).
        /// Idempotent. Safe with empty list — no-ops gracefully.
        /// </summary>
        Task MarkMultipleAsInvoicedAsync(
            IEnumerable<string> deliveryNoteIds,
            string taxInvoiceId);

        /// <summary>
        /// Clears HasTaxInvoice / TaxInvoiceId on every DN marked with this
        /// tax invoice. Called when the Tax Invoice is deleted so the DN
        /// list no longer shows an invoiced badge for a missing invoice.
        /// Idempotent. Safe with a blank id — no-ops.
        /// </summary>
        Task ClearInvoicedByTaxInvoiceAsync(string taxInvoiceId);

        // ── List retrieval ───────────────────────────────────────────────

        /// <summary>CHAT 19: excludes Master DNs (IsMasterDn=true).</summary>
        Task<List<DeliveryNoteListItem>> GetAllAsync();

        /// <summary>
        /// Bounded, DB-side search for the global-search box.
        ///
        /// <para>
        /// Added 2026-09-08 (Phase 2, category D). The search previously
        /// called <see cref="GetAllAsync"/> and filtered in memory, so every
        /// keystroke materialised the entire delivery-note table to return at
        /// most 25 rows. This pushes the filter into SQL and applies the
        /// limit there, so a 50,000-note table costs the same as a 50-note one.
        /// </para>
        ///
        /// <para>
        /// Excludes Master DNs, matching <see cref="GetAllAsync"/> — a search
        /// that returned notes no list screen shows would be worse than the
        /// full-table load it replaces.
        /// </para>
        /// </summary>
        Task<List<DeliveryNoteSearchRow>> SearchAsync(string term, int take);

        /// <summary>
        /// Paged query for the God View. Returns a bounded page of results
        /// plus the total count, so the UI can show "showing X of Y".
        /// Avoids loading 10K+ records into memory at once.
        /// </summary>
        Task<(List<DeliveryNoteListItem> Items, int TotalCount)> GetPagedAsync(
            int page, int pageSize, string? search = null);

        Task<List<DeliveryNoteListItem>> GetByJobOrderIdAsync(string jobOrderId);

        /// <summary>
        /// Delivery notes belonging to one proforma invoice, excluding Master
        /// DNs. Same row set as <see cref="GetAllAsync"/> narrowed to one
        /// proforma — it exists to replace a <c>GetAllAsync()</c> followed by
        /// an in-memory <c>ProformaId</c> filter, which loaded every note in
        /// the database to answer a question about one invoice.
        ///
        /// <para>
        /// The match is case-insensitive, matching the
        /// <c>StringComparison.OrdinalIgnoreCase</c> the call sites used.
        /// Plain SQL <c>=</c> on SQLite is case-sensitive, so the comparison
        /// is lower-cased on both sides.
        /// </para>
        /// </summary>
        Task<List<DeliveryNoteListItem>> GetByProformaIdAsync(string proformaId);

        /// <summary>CHAT 19: excludes Master DNs from Draft tab.</summary>
        Task<List<DeliveryNoteListItem>> GetPreparedAsync();

        /// <summary>CHAT 19: excludes Master DNs from Delivered tab.</summary>
        Task<List<DeliveryNoteListItem>> GetDeliveredAsync();

        Task<List<PendingQtySummary>> GetPendingQtyJobsAsync();
        Task<List<DeliveryNoteListItem>> GetReworkAsync();
        Task<List<DeliveryNoteListItem>> GetReturnedAsync();

        // ── Delivery history ─────────────────────────────────────────────

        Task<DeliveryHistorySummary> GetDeliveryHistoryAsync(string jobOrderId);

        Task<int> GetPreviouslyDeliveredQtyAsync(
            string jobOrderId, int specGroupNumber, int serialNumber);

        // ── Phase 10.7 — Self-healing PREV map ──────────────────────────

        /// <summary>
        /// Returns a dictionary keyed by "specGroupNumber|serialNumber"
        /// whose value is the total DeliveredQty across all CONFIRMED
        /// (non-Draft) real (non-Dummy/Master) sibling DNs for this JO,
        /// EXCLUDING the DN identified by excludeDnId.
        /// CHAT 19: excludes Dummy/Master DNs from the map.
        /// </summary>
        Task<Dictionary<string, int>> GetPreviouslyDeliveredMapAsync(
            string jobOrderId, string excludeDnId);

        // ── Ship 8c-2 — SQM aggregation ─────────────────────────────────

        /// <summary>
        /// Batch-computes SQM aggregates per Job Order.
        /// CHAT 19: excludes Master DN items from calculation.
        /// </summary>
        Task<Dictionary<string, JobOrderSqmSummary>>
            GetSqmSummariesByJobOrderAsync(IEnumerable<string> jobOrderIds);

        // ── Numbering ────────────────────────────────────────────────────

        /// <summary>CHAT 19: excludes Master DN (sequence=0) from MAX.</summary>
        Task<int> GetNextSequenceAsync(string jobOrderId);

        Task<string> BuildDeliveryNoteIdAsync(string jobOrderId, int sequence);

        // ── CRUD ─────────────────────────────────────────────────────────

        Task<SavedDeliveryNote> InsertAsync(SavedDeliveryNote entity);
        Task UpdateAsync(SavedDeliveryNote entity);

        /// <summary>
        /// Update with optimistic-concurrency check (Phase 5). Pass the loaded
        /// RowVersion; throws ConcurrencyConflictException if it moved on. Pass
        /// null for legacy behaviour.
        /// </summary>
        Task UpdateAsync(SavedDeliveryNote entity, int? expectedRowVersion);
        Task DeleteAsync(int id);

        /// <summary>
        /// ERP lifecycle hardening (Phase 1): SOFT-CLOSE (void) a posted or
        /// dependent delivery note instead of deleting it. Keeps the row +
        /// audit trail; sets Status to the void target. Permission-gated like
        /// the edit path.
        /// </summary>
        Task VoidAsync(int id, string reason);

        // ── Existence checks ─────────────────────────────────────────────

        Task<bool> ExistsByDeliveryNoteIdAsync(string deliveryNoteId);

        /// <summary>CHAT 19: excludes Master DN from check.</summary>
        Task<bool> HasAnyDeliveryAsync(string jobOrderId);

        // ── Attachments (Phase 10.6) ─────────────────────────────────────

        Task<List<DeliveryNoteAttachment>> GetAttachmentsAsync(
            string deliveryNoteId);
        Task<DeliveryNoteAttachment> AddAttachmentAsync(
            DeliveryNoteAttachment entity);
        Task DeleteAttachmentAsync(int attachmentId);

        // ── Attachment counts (Phase 10.6b) ──────────────────────────────

        Task<int> GetAttachmentCountAsync(string deliveryNoteId);

        Task<Dictionary<string, int>> GetAttachmentCountsAsync(
            IEnumerable<string> deliveryNoteIds);

        /// <summary>
        /// Lightweight analytics projection: DeliveryNoteId, Status,
        /// DeliveryDate, HasTaxInvoice only. Excludes FullSnapshotJson.
        /// Used by the AI context builder and dashboards to avoid loading
        /// every snapshot blob for simple counts and sums.
        /// </summary>
        Task<List<DeliveryNoteAnalyticsRow>> GetAnalyticsRowsAsync();
    }

    // ── Supporting DTOs ──────────────────────────────────────────────────

    public class PendingQtySummary
    {
        public string JobOrderId { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public int TotalOrdered { get; set; }
        public int TotalDelivered { get; set; }
        public int TotalBalance { get; set; }
        public string LastDnId { get; set; } = "";
    }

    public class DeliveryHistorySummary
    {
        public string JobOrderId { get; set; } = "";
        public int TotalOrdered { get; set; }
        public int TotalDelivered { get; set; }
        public int TotalBalance { get; set; }
        public int TotalRework { get; set; }
        public int TotalReturned { get; set; }
        public System.Collections.Generic.List<DeliveryHistoryEntry> Entries
        { get; set; } = new();
    }

    /// <summary>
    /// One row in the DN detail history feed.
    ///
    /// LEGACY FIELDS (kept for backward compat):
    ///   DeliveredQty — pcs delivered by THIS DN
    ///   BalanceQty   — per-DN snapshot (display only)
    ///
    /// PHASE 10.8b — CUMULATIVE TIMELINE FIELDS:
    ///   OrderedQty           — total JO ordered (same across all rows)
    ///   CumulativePrevQty    — sum of DeliveredQty from all prior DNs
    ///   CumulativeBalanceQty — balance remaining AFTER this DN
    ///   SequenceNumber       — this DN's position in delivery chain
    ///
    /// CHAT 19: Master DN never appears in history entries.
    ///
    /// CHAT 19 INVOICED:
    ///   HasTaxInvoice  — true when this DN has been invoiced
    ///   TaxInvoiceId   — the Tax Invoice reference (e.g. TI-2026-001)
    /// </summary>
    public class DeliveryHistoryEntry
    {
        public string DeliveryNoteId { get; set; } = "";
        public string DeliveryType { get; set; } = "";
        public string Status { get; set; } = "";
        public int DeliveredQty { get; set; }
        public int BalanceQty { get; set; }
        public System.DateTime DeliveryDate { get; set; }
        public string DriverName { get; set; } = "";

        // Phase 10.8b
        public int OrderedQty { get; set; }
        public int CumulativePrevQty { get; set; }
        public int CumulativeBalanceQty { get; set; }
        public int SequenceNumber { get; set; }

        // CHAT 19 INVOICED
        public bool HasTaxInvoice { get; set; }
        public string TaxInvoiceId { get; set; } = "";
    }

    /// <summary>
    /// What the global-search box needs from a delivery note: the four
    /// columns it matches on and the one status pill it renders. Nothing
    /// else — in particular no snapshot blob.
    /// </summary>
    public class DeliveryNoteSearchRow
    {
        public string DeliveryNoteId { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string JobOrderId { get; set; } = "";
        public string Status { get; set; } = "";
    }

    // ── Ship 8c-2 ────────────────────────────────────────────────────────

    /// <summary>
    /// Returned by GetSqmSummariesByJobOrderAsync.
    /// All values in m², rounded to 3 decimal places.
    /// Panel SQM uses SqmCalculator with 0.5 m² minimum floor.
    /// </summary>
    public class JobOrderSqmSummary
    {
        public decimal OrderedSqm { get; set; }
        public decimal DeliveredSqm { get; set; }
        public decimal BalanceSqm { get; set; }
    }
}