using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.SheetStore.SheetStoreHistory;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Licensing;         // ReadOnlyGate
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.SheetStore.SheetStoreHistory
{
    public class SheetHistoryRepository : ISheetHistoryRepository
    {
        public async Task<List<SheetHistory>> GetAllAsync()
        {
            using var db = new AppDbContext();
            return await db.SheetHistories.AsNoTracking()
                .OrderByDescending(h => h.Timestamp)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<SheetHistory>> GetBySheetIdAsync(int sheetId)
        {
            using var db = new AppDbContext();
            return await db.SheetHistories
                .Where(h => h.SheetId == sheetId)
                .OrderByDescending(h => h.Timestamp)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task LogAsync(int sheetId, string sheetDescription, string action, string details,
                                    int quantityChanged = 0, decimal? valueChanged = null,
                                    string? documentReference = null,
                                    string? customerName = null,
                                    string? projectName = null,
                                    string? salesman = null,
                                    string? panePosition = null)
        {
            using var db = new AppDbContext();
            var entry = new SheetHistory
            {
                SheetId = sheetId,
                SheetDescription = sheetDescription,
                Action = action,
                Details = details,
                QuantityChanged = quantityChanged,
                ValueChanged = valueChanged,
                Timestamp = DateTime.Now,
                UserName = Environment.UserName,

                // Denormalised audit trail — see SheetHistory.
                DocumentReference = documentReference ?? string.Empty,
                CustomerName = customerName ?? string.Empty,
                ProjectName = projectName ?? string.Empty,
                Salesman = salesman ?? string.Empty,
                PanePosition = panePosition ?? string.Empty
            };
            db.SheetHistories.Add(entry);
            await SqliteResilience.SaveWithRetryAsync(_ => db.SaveChangesAsync());
        }

        public async Task<DateTime?> GetLastUpdatedAsync()
        {
            using var db = new AppDbContext();
            if (!await db.SheetHistories.AnyAsync())
                return null;
            return await db.SheetHistories.MaxAsync(h => h.Timestamp).ConfigureAwait(false);
        }

        public async Task<int> GetTotalCountAsync()
        {
            using var db = new AppDbContext();
            return await db.SheetHistories.CountAsync().ConfigureAwait(false);
        }

        public async Task<SheetHistory?> GetByIdAsync(int id)
        {
            using var db = new AppDbContext();
            return await db.SheetHistories.AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == id).ConfigureAwait(false);
        }

        /// <summary>
        /// Undoes one stock movement by writing a COMPENSATING entry and
        /// restoring the sheet's counters — never by deleting or editing
        /// the original row.
        ///
        /// WHY A CONTRA ENTRY (2026-09-06): SheetHistory documents itself
        /// as an immutable audit record — "the sheet was still consumed
        /// for that customer on that day, and the record must not
        /// silently change or vanish". Deleting the row would erase the
        /// fact that the mistake happened; editing its quantity would
        /// rewrite history under a timestamp that no longer matches what
        /// occurred. The accounting-correct shape is a reversal pair:
        ///
        ///     #41  Used      qty 10   IsReversed = 1
        ///     #57  Reversed  qty -10  ReversesHistoryId = 41
        ///
        /// ATOMICITY: the counter restore and BOTH history writes share
        /// one DbContext and one SaveChanges, so a crash mid-undo cannot
        /// leave stock restored without the ledger saying why (or the
        /// reverse).
        ///
        /// GUARDS (all re-checked server-side, never trusted from the UI):
        ///   • the original must exist,
        ///   • it must be a MOVEMENT (Used / Bought),
        ///   • it must not already be reversed,
        ///   • it must not itself be a compensating row,
        ///   • the restore must not drive a counter negative — undoing a
        ///     Buy whose sheets have since been consumed is refused
        ///     rather than silently clamped.
        /// </summary>
        /// <returns>The compensating entry, or null when refused.</returns>
        public async Task<SheetHistory?> ReverseMovementAsync(int historyId, string reason)
        {
            ReadOnlyGate.Demand("undo a sheet movement");

            using var db = new AppDbContext();

            var original = await db.SheetHistories
                .FirstOrDefaultAsync(h => h.Id == historyId).ConfigureAwait(false);
            if (original == null) return null;

            // Re-validate the model's own rule on the server side.
            if (original.IsReversed
                || original.ReversesHistoryId.HasValue
                || (original.Action != "Used" && original.Action != "Bought"))
                return null;

            var sheet = await db.GlassSheets
                .FirstOrDefaultAsync(s => s.Id == original.SheetId).ConfigureAwait(false);
            if (sheet == null) return null;

            int qty = original.QuantityChanged;
            if (qty <= 0) return null;

            // Restore the counter the original movement touched.
            if (original.Action == "Used")
            {
                if (sheet.Used < qty)
                    throw new InvalidOperationException(
                        $"Cannot undo: only {sheet.Used} sheet(s) are recorded as used, " +
                        $"but this entry consumed {qty}.");
                sheet.Used -= qty;
            }
            else // "Bought"
            {
                // Refuse rather than clamp: if the purchased sheets have
                // already been consumed, undoing the purchase would leave
                // Used > Stock — a corrupt ledger. The operator must undo
                // the consumption first.
                if (sheet.Stock - qty < sheet.Used)
                    throw new InvalidOperationException(
                        $"Cannot undo this purchase: {sheet.Used} sheet(s) have already " +
                        $"been used. Undo the consumption entries first.");
                sheet.Stock -= qty;
            }

            sheet.LastUpdated = DateTime.Now;

            // Flag the original — badge in the grid, and the CanReverse
            // guard now refuses a second undo.
            original.IsReversed = true;

            string what = original.Action == "Used" ? "consumption" : "purchase";
            var compensating = new SheetHistory
            {
                SheetId = original.SheetId,
                SheetDescription = original.SheetDescription,
                Action = "Reversed",
                Details =
                    $"Undo of #{original.Id} ({what} of {qty} sheet(s) on " +
                    $"{original.Timestamp:dd-MMM-yyyy HH:mm}). " +
                    (string.IsNullOrWhiteSpace(reason) ? "" : $"Reason: {reason.Trim()}. ") +
                    (original.Action == "Used"
                        ? $"Used: {sheet.Used + qty} → {sheet.Used}."
                        : $"Stock: {sheet.Stock + qty} → {sheet.Stock}."),

                // Negative quantity: a contra movement, so any SUM over
                // the ledger nets to the truth without special-casing.
                QuantityChanged = -qty,
                ValueChanged = original.ValueChanged.HasValue
                    ? -original.ValueChanged.Value
                    : null,

                Timestamp = DateTime.Now,
                UserName = Environment.UserName,
                ReversesHistoryId = original.Id,

                // Carry the traceability forward so the reversal shows up
                // in the same customer/project/salesman reports the
                // original does — otherwise a netted report would show the
                // consumption but not its undo.
                DocumentReference = original.DocumentReference ?? string.Empty,
                CustomerName = original.CustomerName ?? string.Empty,
                ProjectName = original.ProjectName ?? string.Empty,
                Salesman = original.Salesman ?? string.Empty,
                PanePosition = original.PanePosition ?? string.Empty
            };

            db.SheetHistories.Add(compensating);

            // ONE save: counter restore + original flag + compensating row
            // commit together or not at all.
            await SqliteResilience.SaveWithRetryAsync(_ => db.SaveChangesAsync());

            return compensating;
        }
    }
}