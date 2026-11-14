using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.SheetStore;
using ProGlassAutomation.Services.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ProGlassAutomation.Services.Audit;

using ProGlassAutomation.Services.Licensing;         // ReadOnlyGate
namespace ProGlassAutomation.Services.SheetStore
{
    /// <summary>
    /// SQLite / EF Core implementation of ISheetStoreRepository.
    /// One DbContext per operation for thread safety.
    /// </summary>
    public class SheetStoreRepository : ISheetStoreRepository
    {
        public async Task<List<GlassSheet>> GetAllAsync()
        {
            using var db = new AppDbContext();
            return await db.GlassSheets
                .AsNoTracking()
                .OrderBy(g => g.Category)
                .ThenBy(g => g.Color)
                .ThenBy(g => g.ThicknessMm)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<GlassSheet?> GetByIdAsync(int id)
        {
            using var db = new AppDbContext();
            return await db.GlassSheets.FindAsync(id).ConfigureAwait(false);
        }

        public async Task<List<GlassSheet>> SearchAsync(
            string? category = null,
            string? color = null,
            string? supplier = null,
            int? thicknessMm = null,
            string? search = null,
            string? sortKey = null,
            int skip = 0,
            int take = 200)
        {
            if (skip < 0) skip = 0;
            if (take <= 0) take = 200;

            using var db = new AppDbContext();

            IQueryable<GlassSheet> query = db.GlassSheets.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(category))
                query = query.Where(g => g.Category == category);
            if (!string.IsNullOrWhiteSpace(color))
                query = query.Where(g => g.Color == color);
            if (!string.IsNullOrWhiteSpace(supplier))
                query = query.Where(g => g.Supplier == supplier);
            if (thicknessMm.HasValue && thicknessMm.Value > 0)
                query = query.Where(g => g.ThicknessMm == thicknessMm.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                string s = search.Trim().ToLower();
                // NOTE: no ThicknessDisplay here - it is a computed property
                // (not a DB column) so EF cannot translate it to SQL. Thickness
                // is searchable via the exact thicknessMm filter above.
                query = query.Where(g =>
                    g.Category.ToLower().Contains(s) ||
                    g.Color.ToLower().Contains(s) ||
                    g.Supplier.ToLower().Contains(s));
            }

            // TotalPurchaseValue / Balance are [NotMapped] computed
            // properties — EF cannot translate them. Value sorts stay
            // in the caller (SheetStoreViewModel.ApplySort) after the
            // page is materialised. Asking SQL to OrderBy them throws.
            query = (sortKey ?? "").ToLowerInvariant() switch
            {
                "newest" => query.OrderByDescending(g => g.CreatedAt),
                "oldest" => query.OrderBy(g => g.CreatedAt),
                "stockasc" => query.OrderBy(g => g.Stock),
                "stockdesc" => query.OrderByDescending(g => g.Stock),
                _ => query.OrderBy(g => g.Category)
                          .ThenBy(g => g.Color)
                          .ThenBy(g => g.ThicknessMm)
            };

            return await query
                .Skip(skip)
                .Take(take)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<GlassSheet> AddAsync(GlassSheet sheet)
        {
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.SheetStoreCreate, "add a glass sheet");

            using var db = new AppDbContext();
            sheet.CreatedAt = DateTime.Now;
            sheet.LastUpdated = DateTime.Now;
            if (sheet.Stock > 0)
                sheet.LastPurchaseDate = DateTime.Now;

            db.GlassSheets.Add(sheet);
            await SqliteResilience.SaveWithRetryAsync(_ => db.SaveChangesAsync());
            return sheet;
        }

        public async Task UpdateAsync(GlassSheet sheet)
        {
            ReadOnlyGate.Demand("update a glass sheet");
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.SheetStoreEdit, "update a glass sheet");

            using var db = new AppDbContext();
            sheet.LastUpdated = DateTime.Now;
            db.GlassSheets.Update(sheet);
            await SqliteResilience.SaveWithRetryAsync(_ => db.SaveChangesAsync());
        }

        public async Task DeleteAsync(int id)
        {
            ReadOnlyGate.Demand("delete a glass sheet");
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.SheetStoreDelete, "delete a glass sheet");

            using var db = new AppDbContext();
            var sheet = await db.GlassSheets.FindAsync(id).ConfigureAwait(false);
            if (sheet != null)
            {
                db.GlassSheets.Remove(sheet);
                await SqliteResilience.SaveWithRetryAsync(_ => db.SaveChangesAsync());
            }
        }

        /// <summary>
        /// Records a stock purchase as a single atomic increment.
        /// </summary>
        /// <remarks>
        /// Was a read-modify-write: load the sheet, add to <c>Stock</c>, save.
        /// Two operators booking stock into the same sheet at once both read the
        /// same starting value, and whichever saved second overwrote the first —
        /// a lost update, silent, with no exception and no way to notice except
        /// that the count was wrong later.
        ///
        /// The increment now happens in SQL (<c>SET Stock = Stock + @q</c>), so
        /// SQLite applies it under one write lock and both purchases land.
        /// Every column the old code touched is named in the setter list, because
        /// a bulk write bypasses change tracking and will not set anything that
        /// is not listed.
        ///
        /// A missing sheet still returns without error, exactly as before.
        /// </remarks>
        public async Task BuyStockAsync(int sheetId, int quantity, decimal? newPurchasePrice = null)
        {
            ReadOnlyGate.Demand("record a stock purchase");
            if (quantity <= 0) return;

            using var db = new AppDbContext();
            var now = DateTime.Now;
            var applyPrice = newPurchasePrice.HasValue && newPurchasePrice.Value > 0;

            // Two explicit branches rather than one setter list with a
            // conditional inside it: keeping the purchase price untouched and
            // overwriting it are different statements, and writing them out
            // separately leaves no doubt about which columns move.
            await SqliteResilience.ExecuteWithRetryAsync(ct => applyPrice
                ? db.GlassSheets
                    .Where(g => g.Id == sheetId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(g => g.Stock, g => g.Stock + quantity)
                        .SetProperty(g => g.PurchasePricePerSqm, _ => newPurchasePrice!.Value)
                        .SetProperty(g => g.LastPurchaseDate, _ => now)
                        .SetProperty(g => g.LastUpdated, _ => now), ct)
                : db.GlassSheets
                    .Where(g => g.Id == sheetId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(g => g.Stock, g => g.Stock + quantity)
                        .SetProperty(g => g.LastPurchaseDate, _ => now)
                        .SetProperty(g => g.LastUpdated, _ => now), ct));
        }

        /// <summary>
        /// Consumes stock as a single guarded atomic increment.
        /// </summary>
        /// <remarks>
        /// The balance check used to run in C# between the read and the write:
        ///
        /// <code>
        /// var sheet = await db.GlassSheets.FindAsync(sheetId);  // read
        /// if (sheet.Balance &lt; quantity) throw ...;            // check
        /// sheet.Used += quantity;                               // write
        /// </code>
        ///
        /// Two operators issuing from the same sheet both read the same balance,
        /// both pass the check, and both write from the same starting value — so
        /// one issue is lost and the balance ends up higher than the physical
        /// stock. With enough overlap the balance can even go negative, because
        /// nothing re-checks at write time.
        ///
        /// The guard is now part of the UPDATE's WHERE clause, evaluated and
        /// applied together under SQLite's write lock. There is no window.
        ///
        /// <c>Balance</c> is a computed property (<c>Stock - Used</c>), not a
        /// column, so the guard is written as the arithmetic EF translates it to.
        ///
        /// Both outcomes the old code produced are preserved: a missing sheet
        /// returns silently, insufficient stock throws — callers depend on the
        /// throw (<c>UpdateSheetStockTool</c>, <c>SheetStoreDashboardViewModel</c>).
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// The sheet exists but does not hold <paramref name="quantity"/> sheets.
        /// </exception>
        public async Task UseStockAsync(int sheetId, int quantity)
        {
            ReadOnlyGate.Demand("record stock usage");
            if (quantity <= 0) return;

            using var db = new AppDbContext();
            var now = DateTime.Now;

            int updated = await SqliteResilience.ExecuteWithRetryAsync(ct =>
                db.GlassSheets
                    .Where(g => g.Id == sheetId && g.Stock - g.Used >= quantity)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(g => g.Used, g => g.Used + quantity)
                        .SetProperty(g => g.LastUpdated, _ => now), ct));

            if (updated > 0) return;

            // Nothing was written. Two reasons, and they are not the same:
            // the sheet may not exist (the old code returned silently) or it may
            // exist and be short (the old code threw). Re-read to tell them
            // apart. This read is only used to choose the error message — the
            // decision about whether to write was already made atomically above,
            // so a concurrent change here cannot corrupt anything.
            int? balance = await db.GlassSheets
                .AsNoTracking()
                .Where(g => g.Id == sheetId)
                .Select(g => (int?)(g.Stock - g.Used))
                .FirstOrDefaultAsync().ConfigureAwait(false);

            if (balance == null) return;

            throw new InvalidOperationException(
                $"Not enough stock. Available: {balance.Value}, Requested: {quantity}");
        }

        public async Task<List<string>> GetCategoriesAsync()
        {
            using var db = new AppDbContext();
            return await db.GlassSheets
                .AsNoTracking()
                .Select(g => g.Category)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<string>> GetColorsAsync()
        {
            using var db = new AppDbContext();
            return await db.GlassSheets
                .AsNoTracking()
                .Select(g => g.Color)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<string>> GetSuppliersAsync()
        {
            using var db = new AppDbContext();
            return await db.GlassSheets
                .AsNoTracking()
                .Select(g => g.Supplier)
                .Distinct()
                .OrderBy(s => s)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<int>> GetThicknessesAsync()
        {
            using var db = new AppDbContext();
            return await db.GlassSheets
                .AsNoTracking()
                .Select(g => g.ThicknessMm)
                .Distinct()
                .OrderBy(t => t)
                .ToListAsync().ConfigureAwait(false);
        }
    }
}