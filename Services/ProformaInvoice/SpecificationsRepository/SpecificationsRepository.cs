using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.ProformaInvoice.Specifications;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using ProGlassAutomation.Services.Audit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ProGlassAutomation.Services.Database;

using ProGlassAutomation.Services.Licensing;         // ReadOnlyGate
namespace ProGlassAutomation.Services.ProformaInvoice.SpecificationsRepository
{
    /// <summary>
    /// EF Core-backed implementation of ISpecificationsRepository.
    /// Uses a separate SQLite database (specifications.db) so the spec library
    /// can be exported/backed up/shared independently of proforma data.
    /// </summary>
    public class SpecificationsRepository : ISpecificationsRepository
    {
        public async Task InitializeAsync()
        {
            using var db = new SpecificationsDbContext();
            await db.Database.EnsureCreatedAsync();

            // Additive schema upgrades for an existing specifications.db.
            SchemaMigrationRunner.Upgrade(
                SpecificationsDbContext.DatabaseFilePath,
                AppSchemaMigrations.Specifications,
                "Specifications");
        }

        public async Task<List<SavedSpecification>> GetAllAsync(string glassTypeFilter = null)
        {
            using var db = new SpecificationsDbContext();

            var query = db.Specifications.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(glassTypeFilter))
                query = query.Where(s => s.GlassType == glassTypeFilter);

            // Smart sort: most-used first, then most-recent
            return await query
                .OrderByDescending(s => s.UsageCount)
                .ThenByDescending(s => s.LastUsedAt)
                .ToListAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Original 3-argument save. Delegates to the catalogue overload with
        /// empty catalogue values so there is exactly ONE write path and the
        /// two cannot drift apart.
        /// </summary>
        public Task<SavedSpecification> SaveOrUpdateAsync(
            string glassType, string specName, decimal pricePerSqm)
            => SaveOrUpdateAsync(
                glassType, specName, pricePerSqm,
                category: null, color: null, thicknessMm: 0,
                sheetWidthMm: 0, sheetHeightMm: 0, sheetId: 0,
                proformaId: null, panesJson: null);

        public async Task<SavedSpecification> SaveOrUpdateAsync(
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
            string? panesJson = null)
        {
            ReadOnlyGate.Demand("save a specification");
            // A-1: service-layer authorization. A saved spec sets the rate
            // that Step 5 and the P&L then price against, so writing one is
            // a pricing decision, not a preference. ProformaEdit matches the
            // MasterData client/project writes, which are the same kind of
            // shared reference data.
            OperationGuard.Demand(
                AppPermissions.ProformaEdit, "save a pricing specification");

            if (string.IsNullOrWhiteSpace(glassType))
                throw new ArgumentException("Glass type is required", nameof(glassType));
            if (string.IsNullOrWhiteSpace(specName))
                throw new ArgumentException("Spec name is required", nameof(specName));

            using var db = new SpecificationsDbContext();

            // Check if this exact spec already exists (dedup by GlassType + SpecName)
            var existing = await db.Specifications
                .FirstOrDefaultAsync(s => s.GlassType == glassType && s.SpecName == specName).ConfigureAwait(false);

            if (existing != null)
            {
                // LEDGER GROUND TRUTH: this writes the reusable LIBRARY
                // (rate + catalogue details) so Step 5 can offer the spec
                // before its invoice is saved. It deliberately does NOT
                // touch UsageCount / LastUsedAt — those are DERIVED by
                // SpecificationLedgerService from SAVED invoices only. The
                // old code bumped the count on every Step 4 APPLY, including
                // drafts that were never saved, which is how the Center came
                // to show uses that never happened.
                existing.PricePerSqm = pricePerSqm;

                // Refresh catalogue columns, but only when the caller
                // actually supplied a value. A 3-arg save (or a caller that
                // could not resolve a sheet) must not wipe details an
                // earlier, better-informed save already recorded.
                if (!string.IsNullOrWhiteSpace(category))   existing.Category = category;
                if (!string.IsNullOrWhiteSpace(color))      existing.Color = color;
                if (thicknessMm > 0)                        existing.ThicknessMm = thicknessMm;
                if (sheetWidthMm > 0 && sheetHeightMm > 0)
                {
                    existing.SheetWidthMm = sheetWidthMm;
                    existing.SheetHeightMm = sheetHeightMm;
                }
                if (sheetId > 0)                            existing.SheetId = sheetId;
                // ProformaId is ledger-owned verified provenance (most recent
                // SAVED invoice). Honoured here only when a caller explicitly
                // supplies one (CSV round-trip); Step 4 passes null because a
                // draft PI id is not a fact yet, and the ledger heals the rest.
                if (!string.IsNullOrWhiteSpace(proformaId)) existing.ProformaId = proformaId;
                if (!string.IsNullOrWhiteSpace(panesJson)) existing.PanesJson = panesJson;
                await SqliteResilience.SaveWithRetryAsync(_ => db.SaveChangesAsync());

                // Tell every open SpecGroup its dropdown is stale. Without
                // this the price change is on disk but Step 5 keeps showing
                // the old one until the glass type is toggled.
                SpecCatalogNotifier.NotifyChanged();
                return existing;
            }

            // Create new spec. UsageCount starts at ZERO — being built in
            // Step 4 is not being used, and the ledger sets the verified
            // count from saved invoices. LastUsedAt starts at creation (a
            // touch timestamp, healed by the ledger on next reconcile).
            var newSpec = new SavedSpecification
            {
                GlassType = glassType,
                SpecName = specName,
                PricePerSqm = pricePerSqm,
                CreatedAt = DateTime.Now,
                LastUsedAt = DateTime.Now,
                UsageCount = 0,

                Category = category,
                Color = color,
                ThicknessMm = thicknessMm,
                SheetWidthMm = sheetWidthMm,
                SheetHeightMm = sheetHeightMm,
                SheetId = sheetId,
                ProformaId = proformaId,
                PanesJson = panesJson
            };

            db.Specifications.Add(newSpec);
            await SqliteResilience.SaveWithRetryAsync(_ => db.SaveChangesAsync());

            // A brand-new spec must appear in the Step 5 dropdown
            // immediately -- this is the "instant sync" path.
            SpecCatalogNotifier.NotifyChanged();
            return newSpec;
        }

        public async Task MarkAsUsedAsync(int specId)
        {
            // LEGACY. Usage counts are ledger-derived from saved invoices
            // (SpecificationLedgerService) — see the interface contract.
            // Kept working for backward compatibility; the ledger heals any
            // drift this causes on its next pass.
            ReadOnlyGate.Demand("mark a specification as used");
            using var db = new SpecificationsDbContext();
            var spec = await db.Specifications.FindAsync(specId).ConfigureAwait(false);
            if (spec != null)
            {
                spec.LastUsedAt = DateTime.Now;
                spec.UsageCount += 1;
                await SqliteResilience.SaveWithRetryAsync(_ => db.SaveChangesAsync());
            }
        }

        public async Task RecordUsageAsync(
            int specId,
            string specName,
            string glassType,
            string? proformaId,
            string? customerName,
            string? projectName,
            decimal pricePerSqm,
            int sheetWidthMm,
            int sheetHeightMm)
        {
            // Deliberately NOT gated by ReadOnlyGate/OperationGuard.
            // This is an append-only audit trail written as a side effect of
            // a save that has already been authorised; blocking it would
            // leave the catalogue and its history inconsistent. Wrapped so a
            // history failure can never surface as a save failure.
            //
            // LEDGER OWNERSHIP: the only writer is now the ledger reconcile
            // (in bulk). Step 4 must NOT call this — an APPLY press is not a
            // use until its invoice is saved. Rows the ledger did not derive
            // are purged as orphans on the next pass.
            try
            {
                using var db = new SpecificationsDbContext();

                db.SpecificationUsages.Add(new SpecificationUsage
                {
                    SpecificationId = specId,
                    SpecName = specName ?? string.Empty,
                    GlassType = glassType ?? string.Empty,
                    ProformaId = proformaId ?? string.Empty,
                    CustomerName = customerName ?? string.Empty,
                    ProjectName = projectName ?? string.Empty,
                    PricePerSqm = pricePerSqm,
                    SheetWidthMm = sheetWidthMm,
                    SheetHeightMm = sheetHeightMm,
                    UsedAt = DateTime.Now
                });

                await SqliteResilience.SaveWithRetryAsync(_ => db.SaveChangesAsync());
                SpecCatalogNotifier.NotifyChanged();
            }
            catch (Exception ex)
            {
                Services.Logging.AppLogService.Debug(
                    $"[SpecificationsRepository.RecordUsage] {ex.Message}");
            }
        }

        public async Task<List<SpecificationUsage>> GetUsageHistoryAsync(int specId)
        {
            try
            {
                using var db = new SpecificationsDbContext();
                return await db.SpecificationUsages
                    .AsNoTracking()
                    .Where(u => u.SpecificationId == specId)
                    .OrderByDescending(u => u.UsedAt)
                    .ToListAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Services.Logging.AppLogService.Debug(
                    $"[SpecificationsRepository.GetUsageHistory] {ex.Message}");
                return new List<SpecificationUsage>();
            }
        }

        public async Task<List<SpecificationUsage>> GetRecentUsageAsync(int limit = 200)
        {
            if (limit <= 0) limit = 200;

            try
            {
                using var db = new SpecificationsDbContext();
                return await db.SpecificationUsages
                    .AsNoTracking()
                    .OrderByDescending(u => u.UsedAt)
                    .Take(limit)
                    .ToListAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Services.Logging.AppLogService.Debug(
                    $"[SpecificationsRepository.GetRecentUsage] {ex.Message}");
                return new List<SpecificationUsage>();
            }
        }

        public async Task<(int Inserted, int Updated, int Skipped)> ImportAsync(
            IEnumerable<SavedSpecification> rows)
        {
            ReadOnlyGate.Demand("import specifications");
            OperationGuard.Demand(
                AppPermissions.ProformaEdit, "import pricing specifications");

            int inserted = 0, updated = 0, skipped = 0;
            if (rows == null) return (0, 0, 0);

            using var db = new SpecificationsDbContext();

            // Load once and match in memory: an import is a handful of
            // hundred rows at most, and this avoids a per-row round trip.
            var existing = await db.Specifications.ToListAsync().ConfigureAwait(false);

            foreach (var row in rows)
            {
                if (row == null
                    || string.IsNullOrWhiteSpace(row.GlassType)
                    || string.IsNullOrWhiteSpace(row.SpecName)
                    || row.PricePerSqm <= 0m)
                {
                    skipped++;
                    continue;
                }

                var match = existing.FirstOrDefault(s =>
                    string.Equals(s.GlassType, row.GlassType, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.SpecName, row.SpecName, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    match.PricePerSqm = row.PricePerSqm;
                    match.LastUsedAt = DateTime.Now;

                    // Same rule as SaveOrUpdateAsync: only overwrite a
                    // catalogue column when the incoming row actually has a
                    // value, so a sparse import cannot erase good data.
                    if (!string.IsNullOrWhiteSpace(row.Color)) match.Color = row.Color;
                    if (!string.IsNullOrWhiteSpace(row.Category)) match.Category = row.Category;
                    if (row.ThicknessMm > 0) match.ThicknessMm = row.ThicknessMm;
                    if (row.SheetWidthMm > 0 && row.SheetHeightMm > 0)
                    {
                        match.SheetWidthMm = row.SheetWidthMm;
                        match.SheetHeightMm = row.SheetHeightMm;
                    }
                    updated++;
                }
                else
                {
                    row.CreatedAt = row.CreatedAt == default ? DateTime.Now : row.CreatedAt;
                    row.LastUsedAt = DateTime.Now;
                    if (row.UsageCount < 0) row.UsageCount = 0;

                    db.Specifications.Add(row);
                    existing.Add(row);
                    inserted++;
                }
            }

            if (inserted > 0 || updated > 0)
            {
                await SqliteResilience.SaveWithRetryAsync(_ => db.SaveChangesAsync());
                SpecCatalogNotifier.NotifyChanged();
            }

            return (inserted, updated, skipped);
        }

        public async Task DeleteAsync(int specId)
        {
            ReadOnlyGate.Demand("delete a specification");
            // A-1: no caller today, but this is public repository API and
            // guarding it now stops it becoming an unguarded entry point.
            OperationGuard.Demand(
                AppPermissions.ProformaDelete, "delete a pricing specification");

            using var db = new SpecificationsDbContext();
            var spec = await db.Specifications.FindAsync(specId).ConfigureAwait(false);
            if (spec != null)
            {
                db.Specifications.Remove(spec);
                await SqliteResilience.SaveWithRetryAsync(_ => db.SaveChangesAsync());

                // A deleted spec must vanish from open dropdowns too,
                // otherwise it can still be picked and priced.
                SpecCatalogNotifier.NotifyChanged();
            }
        }
    }
}