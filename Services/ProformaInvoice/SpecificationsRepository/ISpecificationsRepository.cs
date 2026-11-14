using ProGlassAutomation.Models.ProformaInvoice.Specifications;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.ProformaInvoice.SpecificationsRepository
{
    /// <summary>
    /// Contract for spec library CRUD operations.
    /// Enables future testing/mocking + swappable persistence.
    /// </summary>
    public interface ISpecificationsRepository
    {
        /// <summary>
        /// Load all specs, optionally filtered by glass type.
        /// Sorted by: UsageCount DESC, then LastUsedAt DESC (most useful first).
        /// </summary>
        Task<List<SavedSpecification>> GetAllAsync(string glassTypeFilter = null);

        /// <summary>
        /// Save or update a spec in the reusable LIBRARY (rate + catalogue
        /// details) so Step 5 can offer it before its invoice is saved.
        /// Does NOT touch UsageCount / LastUsedAt — those are DERIVED by
        /// SpecificationLedgerService from SAVED invoices only.
        /// </summary>
        Task<SavedSpecification> SaveOrUpdateAsync(string glassType, string specName, decimal pricePerSqm);

        /// <summary>
        /// Specifications Center overload. Same save, plus the catalogue
        /// columns: category/colour/thickness parsed from the spec, the
        /// matched Sheet Store size, and the originating proforma.
        ///
        /// The sheet values are recorded for DISPLAY and traceability only
        /// ("which sheet was this quoted against"). Pricing still comes from
        /// pricePerSqm, i.e. from Step 4 -- a later Sheet Store price edit
        /// must never silently re-cost an existing specification.
        ///
        /// Any argument may be null/0; those simply leave the column blank,
        /// so callers that do not know a value are not forced to invent one.
        /// </summary>
        Task<SavedSpecification> SaveOrUpdateAsync(
            string glassType,
            string specName,
            decimal pricePerSqm,
            string? category,
            string? color,
            int thicknessMm,
            int sheetWidthMm,
            int sheetHeightMm,
            int sheetId,
            string? proformaId,
            string? panesJson = null);

        /// <summary>
        /// LEGACY. Usage counts are ledger-derived from saved invoices
        /// (SpecificationLedgerService) — nothing should call this. Kept so
        /// existing compiled callers keep working; any drift it causes is
        /// healed by the next ledger pass.
        /// </summary>
        [System.Obsolete("Usage counts are ledger-derived from saved invoices; do not call.")]
        Task MarkAsUsedAsync(int specId);

        /// <summary>
        /// Appends one usage row recording that <paramref name="specId"/> was
        /// quoted on a proforma, at the price quoted at that moment.
        ///
        /// Append-only: a spec used on five invoices has five rows, so the
        /// Center can list every PI instead of one arbitrary number. Never
        /// throws -- history is diagnostic, and failing to record it must
        /// never block the save that already succeeded.
        ///
        /// LEDGER-OWNED: the only writer is the ledger reconcile, which
        /// derives rows from SAVED invoices. Step 4 must not call this (an
        /// APPLY press is not a use), and rows the ledger did not derive
        /// are purged as orphans on the next pass.
        /// </summary>
        Task RecordUsageAsync(
            int specId,
            string specName,
            string glassType,
            string? proformaId,
            string? customerName,
            string? projectName,
            decimal pricePerSqm,
            int sheetWidthMm,
            int sheetHeightMm);

        /// <summary>
        /// Full usage history for one specification, newest first.
        /// </summary>
        Task<List<SpecificationUsage>> GetUsageHistoryAsync(int specId);

        /// <summary>
        /// Usage rows across all specifications, newest first, capped by
        /// <paramref name="limit"/> so the Center's activity list cannot be
        /// slowed down by a large ledger.
        /// </summary>
        Task<List<SpecificationUsage>> GetRecentUsageAsync(int limit = 200);

        /// <summary>
        /// Bulk upsert used by CSV import. Each row is matched on
        /// GlassType + SpecName exactly like a Step 4 save, so re-importing
        /// a file you exported updates in place instead of duplicating.
        ///
        /// Returns (inserted, updated, skipped). Rows missing a glass type,
        /// a spec name, or with a non-positive price are SKIPPED rather than
        /// written -- a malformed import must not poison the catalogue that
        /// Step 5 prices against.
        /// </summary>
        Task<(int Inserted, int Updated, int Skipped)> ImportAsync(
            IEnumerable<SavedSpecification> rows);

        /// <summary>
        /// Delete a saved spec (user-initiated cleanup).
        /// </summary>
        Task DeleteAsync(int specId);

        /// <summary>
        /// Ensure database file and tables exist.
        /// Called at app startup.
        /// </summary>
        Task InitializeAsync();
    }
}