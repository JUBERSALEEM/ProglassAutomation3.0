using ProGlassAutomation.Models.JobOrder.SavedJobOrder;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.JobOrder.Repository
{
    /// <summary>
    /// Contract for Job Order data access.
    /// ViewModels depend on this interface, never on the concrete
    /// repository. Allows swapping to mock/test implementations easily.
    /// </summary>
    public interface IJobOrderRepository
    {
        /// <summary>Ensure database + tables exist. Call once at startup.</summary>
        Task InitializeAsync();

        // ── CRUD ──────────────────────────────────────────────────────────

        Task<List<SavedJobOrder>> GetAllAsync();

        /// <summary>
        /// Lightweight projection of the indexed list columns, excluding the
        /// FullSnapshotJson blob. The saved-job-orders list only renders
        /// JobOrderId, customer, project, value, status, priority and dates —
        /// none of which need the JSON. Using this avoids loading (and
        /// deserializing nothing, but transferring) the full snapshot blob
        /// for every row on every refresh.
        /// </summary>
        Task<List<JobOrderListRow>> GetListRowsAsync();
        Task<SavedJobOrder?> GetByIdAsync(int id);
        Task<SavedJobOrder?> GetByJobOrderIdAsync(string jobOrderId);
        Task<SavedJobOrder> InsertAsync(SavedJobOrder jobOrder);
        Task UpdateAsync(SavedJobOrder jobOrder);

        /// <summary>
        /// Update with optimistic-concurrency check (Phase 5). Pass the loaded
        /// RowVersion; throws ConcurrencyConflictException if it moved on. Pass
        /// null for legacy behaviour.
        /// </summary>
        Task UpdateAsync(SavedJobOrder jobOrder, int? expectedRowVersion);
        Task DeleteAsync(int id);

        /// <summary>
        /// ERP lifecycle hardening (Phase 1): SOFT-CLOSE (void) a posted or
        /// dependent job order instead of deleting it. Keeps the row + audit
        /// trail; sets Status to the void target. Permission-gated like Delete.
        /// </summary>
        Task VoidAsync(int id, string reason);

        // ── DUPLICATE DETECTION ───────────────────────────────────────────

        /// <summary>True if a job order with this ID already exists.</summary>
        Task<bool> ExistsByJobOrderIdAsync(string jobOrderId);

        /// <summary>
        /// Suggests the next available ID given a base ID.
        /// e.g. base "JOB-2026-001" → returns "JOB-2026-002" if 001 taken.
        /// </summary>
        Task<string> SuggestNextIdAsync(string baseId);

        // ── LOOKUP FROM PI ────────────────────────────────────────────────

        /// <summary>
        /// Get all job orders sourced from a specific Proforma Invoice.
        /// Since our model is 1 PI = 1 JOB, this typically returns 0 or 1
        /// item, but the method returns a list for future flexibility.
        /// </summary>
        Task<List<SavedJobOrder>> GetBySourceProformaIdAsync(string proformaId);

        /// <summary>
        /// Lightweight analytics projection: JobOrderId, Status, DeliveryDate
        /// only. Excludes FullSnapshotJson. Used by the AI context builder
        /// and dashboards to avoid loading every snapshot blob for simple
        /// counts and sums.
        /// </summary>
        Task<List<JobOrderAnalyticsRow>> GetAnalyticsRowsAsync();

        /// <summary>
        /// Bounded, DB-side search for the global-search box. Filters the
        /// indexed columns with a case-insensitive contains and returns at
        /// most <paramref name="take"/> rows, projecting only the columns
        /// search displays. Avoids loading every row (and its
        /// FullSnapshotJson blob) just to find top matches.
        /// </summary>
        Task<List<JobOrderSearchRow>> SearchAsync(
            string term, int take);
    }
}