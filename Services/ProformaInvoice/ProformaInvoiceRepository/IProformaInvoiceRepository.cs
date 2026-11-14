using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.ProformaInvoice.ProformaInvoiceRepository
{
    /// <summary>
    /// Repository contract for saved proforma invoices (proformainvoices.db).
    ///
    /// BUG-07: Return types now nullable — GetByIdAsync and
    ///         GetByProformaIdAsync can legitimately return null when
    ///         the requested record does not exist.
    /// BUG-12: CancellationToken added on long-running queries.
    ///         Default value keeps backward compatibility with callers.
    /// </summary>
    public interface IProformaInvoiceRepository
    {
        /// <summary>Ensure database file + tables exist. Called once at app startup.</summary>
        Task InitializeAsync();

        /// <summary>Returns true if an invoice with the given ProformaId already exists.</summary>
        Task<bool> ExistsByProformaIdAsync(string proformaId);

        /// <summary>
        /// Suggests the next available ProformaId based on a duplicate.
        /// E.g. "PI-2026-001" → "PI-2026-002" (auto-increments the numeric suffix).
        /// </summary>
        Task<string> SuggestNextIdAsync(string currentId);

        /// <summary>Persist a new invoice. Throws if ProformaId already exists.</summary>
        Task<int> InsertAsync(SavedProformaInvoice invoice);

        /// <summary>Update the invoice matching ProformaId. Creates if missing.</summary>
        Task UpdateAsync(SavedProformaInvoice invoice);

        /// <summary>
        /// Update with optimistic-concurrency check (Phase 5). Pass the loaded
        /// RowVersion; throws ConcurrencyConflictException if it moved on. Pass
        /// null for legacy behaviour.
        /// </summary>
        Task UpdateAsync(SavedProformaInvoice invoice, int? expectedRowVersion);

        /// <summary>
        /// Fetches by numeric primary key.
        /// BUG-07: Returns null if not found — callers must handle nulls.
        /// </summary>
        Task<SavedProformaInvoice?> GetByIdAsync(int id);

        /// <summary>
        /// Fetches by user-facing ProformaId.
        /// BUG-07: Returns null if not found — callers must handle nulls.
        /// </summary>
        Task<SavedProformaInvoice?> GetByProformaIdAsync(string proformaId);

        /// <summary>All rows of one revision chain (original + revisions),
        /// ordered by revision number. 2026-09-11 revision support.</summary>
        Task<List<SavedProformaInvoice>> GetRevisionChainAsync(string rootProformaId);

        /// <summary>
        /// Returns all invoices ordered by CreatedAt DESC.
        /// BUG-12: Optional cancellation token for long-running loads.
        /// </summary>
        Task<List<SavedProformaInvoice>> GetAllAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns only invoices whose stored Status matches, ordered by
        /// CreatedAt DESC. Uses the indexed Status column so callers that
        /// need the full snapshot (e.g. Job Profitability) never materialise
        /// the FullSnapshotJson blob for drafts / sent / cancelled rows.
        /// </summary>
        Task<List<SavedProformaInvoice>> GetByStatusAsync(
            string status,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// PERF-2: analytics-only projection for dashboards.
        ///
        /// GetAllAsync materialises every column of every row, including
        /// FullSnapshotJson — a complete invoice snapshot blob — and the
        /// dashboards were reading all of it just to count rows and sum
        /// totals. This reads only the 5 scalar columns they actually use,
        /// with AsNoTracking, so the blob column is never touched.
        ///
        /// Deliberately a NEW method: GetAllAsync has 23 callers and some
        /// of them genuinely need the snapshot, so changing it in place
        /// would have been unsafe.
        /// </summary>
        Task<List<ProformaAnalyticsRow>> GetAnalyticsRowsAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Bounded, DB-side search for the global-search box. Filters the
        /// indexed columns with a case-insensitive contains and returns at
        /// most <paramref name="take"/> rows, projecting only the columns
        /// search displays. Avoids loading every row (and its
        /// FullSnapshotJson blob) just to find top matches.
        /// </summary>
        Task<List<ProformaSearchRow>> SearchAsync(
            string term, int take);

        /// <summary>
        /// PERF-9: list-page projection for the Saved Invoices screen.
        ///
        /// GetAllAsync materialises every column of every row, including
        /// FullSnapshotJson — a complete invoice snapshot blob — and the
        /// list was reading all of it just to show REF / COMPANY /
        /// CUSTOMER / PROJECT / STATUS / TOTAL / DATE. This reads only the
        /// scalar columns the list actually binds, with AsNoTracking.
        ///
        /// Deliberately a NEW method: GetAllAsync has many callers and
        /// some genuinely need the snapshot, so changing it in place
        /// would have been unsafe.
        /// </summary>
        Task<List<ProformaListRow>> GetListRowsAsync(
            CancellationToken cancellationToken = default);

        Task DeleteAsync(int id);

        /// <summary>
        /// ERP lifecycle hardening (Phase 1): SOFT-CLOSE (void) a posted or
        /// dependent invoice instead of deleting it. Sets the void audit
        /// columns, moves Status to the void target, and keeps the row so the
        /// number, snapshot and audit trail survive. Permission-gated exactly
        /// like DeleteAsync.
        /// </summary>
        Task VoidAsync(int id, string reason);

        /// <summary>
        /// Internal lifecycle write: marks the invoice with the given
        /// ProformaId as Cancelled (stored "Rejected") and syncs the
        /// snapshot's ProformaStatus, WITHOUT demanding ProformaEdit.
        ///
        /// Authorization is the originating downstream delete (JobOrder
        /// Delete / DeliveryNote Edit / TaxInvoice Edit) — the repository
        /// that invoked the lifecycle sync has already enforced it. The
        /// Production role, for example, can delete a Job Order without
        /// holding ProformaEdit; demanding it here would make the sync
        /// throw and silently leave the PI in its old status. Same
        /// bookkeeping philosophy as JobOrderRepository's master-DN
        /// cleanup. Returns false when no invoice has that ProformaId.
        /// </summary>
        Task<bool> MarkCancelledAsync(string proformaId);
    }
}
