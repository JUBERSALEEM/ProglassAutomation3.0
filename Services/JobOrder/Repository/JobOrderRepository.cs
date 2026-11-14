using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.JobOrder.SavedJobOrder;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using ProGlassAutomation.Services.JobOrder.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.ProformaInvoice;
using ProGlassAutomation.Services.JobOrder.Mapper;

using ProGlassAutomation.Services.Licensing;         // ReadOnlyGate
namespace ProGlassAutomation.Services.JobOrder.Repository
{
    /// <summary>
    /// SQLite / EF Core implementation of IJobOrderRepository.
    /// Follows project convention: one DbContext per operation via `using`.
    /// </summary>
    public class JobOrderRepository : IJobOrderRepository
    {
        public async Task InitializeAsync()
        {
            using var db = new JobOrdersDbContext();
            await db.Database.EnsureCreatedAsync();

            // Additive schema upgrades for an existing joborders.db.
            SchemaMigrationRunner.Upgrade(
                JobOrdersDbContext.DatabaseFilePath,
                AppSchemaMigrations.JobOrders,
                "Job Order");
        }

        // ── CRUD ──────────────────────────────────────────────────────────

        public async Task<List<SavedJobOrder>> GetAllAsync()
        {
            using var db = new JobOrdersDbContext();
            return await db.JobOrders
                .AsNoTracking()
                .OrderByDescending(j => j.UpdatedAt)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<SavedJobOrder?> GetByIdAsync(int id)
        {
            using var db = new JobOrdersDbContext();
            return await db.JobOrders.FindAsync(id).ConfigureAwait(false);
        }

        public async Task<List<JobOrderListRow>> GetListRowsAsync()
        {
            using var db = new JobOrdersDbContext();
            return await db.JobOrders
                .AsNoTracking()
                .OrderByDescending(j => j.UpdatedAt)
                .Select(j => new JobOrderListRow
                {
                    Id = j.Id,
                    JobOrderId = j.JobOrderId ?? "",
                    SourceProformaId = j.SourceProformaId ?? "",
                    CustomerName = j.CustomerName ?? "",
                    ProjectName = j.ProjectName ?? "",
                    GrandTotalVatInclusive = j.GrandTotalVatInclusive,
                    Status = j.Status ?? "",
                    Priority = j.Priority ?? "",
                    DeliveryDate = j.DeliveryDate,
                    CreatedAt = j.CreatedAt,
                    UpdatedAt = j.UpdatedAt,
                    IsVoid = j.IsVoid
                })
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<SavedJobOrder?> GetByJobOrderIdAsync(string jobOrderId)
        {
            if (string.IsNullOrWhiteSpace(jobOrderId)) return null;

            using var db = new JobOrdersDbContext();
            return await db.JobOrders
                .FirstOrDefaultAsync(j => j.JobOrderId == jobOrderId).ConfigureAwait(false);
        }

        public async Task<SavedJobOrder> InsertAsync(SavedJobOrder jobOrder)
        {
            ReadOnlyGate.Demand("create a job order");
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.JobOrderEdit, "create a job order");

            if (jobOrder == null)
                throw new ArgumentNullException(nameof(jobOrder));

            bool idReallocated = false;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                await using var db = new JobOrdersDbContext();

                bool exists = await db.JobOrders
                    .AnyAsync(j => j.JobOrderId == jobOrder.JobOrderId)
                    .ConfigureAwait(false);
                if (exists)
                {
                    jobOrder.JobOrderId = await SuggestNextIdAsync(
                        jobOrder.JobOrderId).ConfigureAwait(false);
                    idReallocated = true;
                    continue;
                }

                if (idReallocated)
                    SyncSnapshotJobOrderId(jobOrder);

                jobOrder.CreatedAt = DateTime.Now;
                jobOrder.UpdatedAt = DateTime.Now;
                db.JobOrders.Add(jobOrder);
                try
                {
                    await SqliteResilience.SaveWithRetryAsync(_ => db.SaveChangesAsync())
                        .ConfigureAwait(false);
                }
                catch (DbUpdateException ex)
                    when (SqliteErrorClassifier.IsUnique(ex) && attempt < 4)
                {
                    db.Entry(jobOrder).State = EntityState.Detached;
                    jobOrder.JobOrderId = await SuggestNextIdAsync(
                        jobOrder.JobOrderId).ConfigureAwait(false);
                    jobOrder.Id = 0;
                    idReallocated = true;
                    continue;
                }

                DocumentLifecycle.Raise();
                return jobOrder;
            }

            throw new InvalidOperationException(
                $"Could not allocate a free JobOrderId after 5 retries " +
                $"starting from '{jobOrder.JobOrderId}'.");
        }

        private static void SyncSnapshotJobOrderId(SavedJobOrder jobOrder)
        {
            if (jobOrder == null || string.IsNullOrWhiteSpace(jobOrder.JobOrderId))
                return;
            try
            {
                var snap = JobOrderMapper.DeserializeSnapshot(jobOrder);
                if (snap == null) return;
                if (string.Equals(snap.JobOrderId, jobOrder.JobOrderId,
                        StringComparison.Ordinal))
                    return;
                snap.JobOrderId = jobOrder.JobOrderId;
                jobOrder.FullSnapshotJson = JobOrderMapper.SerializeSnapshot(snap);
            }
            catch (Exception ex)
            {
                ProGlassAutomation.Services.Logging.AppLogService.Warning(
                    "Could not sync JO snapshot JobOrderId after reallocation: " +
                    ex.Message);
            }
        }

        public async Task UpdateAsync(SavedJobOrder jobOrder)
            => await UpdateAsync(jobOrder, expectedRowVersion: null).ConfigureAwait(false);

        /// <summary>
        /// Updates a job order. When <paramref name="expectedRowVersion"/> is
        /// supplied and the stored RowVersion has moved on, a
        /// <see cref="Services.Database.ConcurrencyConflictException"/> is thrown
        /// so a concurrent save isn't silently overwritten. Null keeps legacy
        /// behaviour; the counter still increments.
        /// </summary>
        public async Task UpdateAsync(SavedJobOrder jobOrder, int? expectedRowVersion)
        {
            ReadOnlyGate.Demand("update a job order");
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.JobOrderEdit, "update a job order");

            using var db = new JobOrdersDbContext();

            // ERP hardening (Phase 5): optimistic-concurrency check + counter.
            var storedRowVersion = await db.JobOrders
                .Where(x => x.Id == jobOrder.Id)
                .Select(x => (int?)x.RowVersion)
                .FirstOrDefaultAsync().ConfigureAwait(false);

            if (expectedRowVersion.HasValue
                && storedRowVersion.HasValue
                && storedRowVersion.Value != expectedRowVersion.Value)
            {
                throw new Services.Database.ConcurrencyConflictException(
                    "Job Order", jobOrder.JobOrderId,
                    expectedRowVersion.Value, storedRowVersion.Value);
            }

            jobOrder.RowVersion = (storedRowVersion ?? jobOrder.RowVersion) + 1;
            jobOrder.UpdatedAt = DateTime.Now;
            db.JobOrders.Update(jobOrder);
            await SqliteResilience.SaveWithRetryAsync(_ => db.SaveChangesAsync());
            DocumentLifecycle.Raise();
        }

        public async Task DeleteAsync(int id)
        {
            ReadOnlyGate.Demand("delete a job order");
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.JobOrderDelete, "delete a job order");

            using var db = new JobOrdersDbContext();
            var job = await db.JobOrders.FindAsync(id).ConfigureAwait(false);
            if (job == null) return;

            string? sourcePi = job.SourceProformaId;
            string? jobOrderId = job.JobOrderId;

            db.JobOrders.Remove(job);
            await SqliteResilience.SaveWithRetryAsync(_ => db.SaveChangesAsync());

            await DeleteMasterDnQuietlyAsync(jobOrderId);

            // Run the lifecycle sync BEFORE raising the change signal so a
            // PI list refresh sees the final (cancelled) status the first
            // time — no stale "PI confirmed / PI created" frame.
            await ProformaLifecycleSync.AfterDownstreamChangeAsync(sourcePi);
            DocumentLifecycle.Raise();
        }

        /// <summary>
        /// ERP lifecycle hardening (Phase 1): soft-close (void) instead of a
        /// physical delete. Keeps the row (job-order number + immutable PI
        /// snapshot + audit trail survive); Status moves to the void target
        /// (Rejected). The master DN is intentionally left intact — the job
        /// order still exists, it is simply cancelled.
        /// </summary>
        public async Task VoidAsync(int id, string reason)
        {
            ReadOnlyGate.Demand("void a job order");
            OperationGuard.Demand(
                AppPermissions.JobOrderDelete, "void a job order");

            using var db = new JobOrdersDbContext();
            var job = await db.JobOrders.FindAsync(id).ConfigureAwait(false);
            if (job == null) return;
            if (job.IsVoid) return;   // idempotent

            string? sourcePi = job.SourceProformaId;

            job.IsVoid = true;
            job.VoidedAtUtc = DateTime.UtcNow;
            job.VoidedBy = AuditService.CurrentUsername;
            job.VoidReason = string.IsNullOrWhiteSpace(reason) ? "(no reason given)" : reason.Trim();
            job.Status = Services.Workflow.DocumentLifecyclePolicy.VoidTargetStatus(
                Services.Workflow.StatusTransitions.Kind.JobOrder);
            job.UpdatedAt = DateTime.Now;

            await SqliteResilience.SaveWithRetryAsync(_ => db.SaveChangesAsync());

            // Recover the parent PI's derived status just like a delete would.
            await ProformaLifecycleSync.AfterDownstreamChangeAsync(sourcePi);
            DocumentLifecycle.Raise();
        }

        /// <summary>
        /// Master DNs are internal and excluded from GetByJobOrderIdAsync,
        /// so they never block JO delete. Remove the leftover tracker so
        /// the delivery-note database does not keep an orphan MDN.
        /// Failures are swallowed — the JO is already gone.
        /// </summary>
        private static async Task DeleteMasterDnQuietlyAsync(string? jobOrderId)
        {
            if (string.IsNullOrWhiteSpace(jobOrderId) || App.DeliveryNotes == null)
                return;

            try
            {
                var master = await App.DeliveryNotes
                    .GetMasterDnForJobOrderAsync(jobOrderId);
                if (master != null)
                    await App.DeliveryNotes.DeleteAsync(master.Id);
            }
            catch (System.Exception ex)
            {
                // Best-effort cleanup. A leftover Master DN is hidden
                // from every public list and does not keep the PI open.
                ProGlassAutomation.Services.Logging.AppLogService.Warning("[JobOrderRepository] Master-DN quiet cleanup failed: " + ex.Message);
            }
        }

        // ── DUPLICATE DETECTION ───────────────────────────────────────────

        public async Task<bool> ExistsByJobOrderIdAsync(string jobOrderId)
        {
            if (string.IsNullOrWhiteSpace(jobOrderId)) return false;

            using var db = new JobOrdersDbContext();
            return await db.JobOrders
                .AnyAsync(j => j.JobOrderId == jobOrderId).ConfigureAwait(false);
        }

        public async Task<string> SuggestNextIdAsync(string baseId)
        {
            if (string.IsNullOrWhiteSpace(baseId)) return baseId;

            // Parse trailing number, e.g. "JOB-2026-001" → prefix "JOB-2026-",
            // numeric "001" (width 3). Increment until an unused ID is found.
            int lastDash = baseId.LastIndexOf('-');
            if (lastDash < 0 || lastDash == baseId.Length - 1)
                return baseId + "-002";

            string prefix = baseId.Substring(0, lastDash + 1);
            string numericPart = baseId.Substring(lastDash + 1);

            if (!int.TryParse(numericPart, out int currentNum))
                return baseId + "-002";

            int width = numericPart.Length;

            using var db = new JobOrdersDbContext();

            int candidate = currentNum + 1;
            string candidateId;
            while (true)
            {
                candidateId = prefix + candidate.ToString().PadLeft(width, '0');
                bool exists = await db.JobOrders
                    .AnyAsync(j => j.JobOrderId == candidateId).ConfigureAwait(false);
                if (!exists) return candidateId;
                candidate++;

                // safety net — very unlikely
                if (candidate > 99999) return prefix + Guid.NewGuid().ToString("N").Substring(0, 8);
            }
        }

        // ── ANALYTICS PROJECTION ─────────────────────────────────────────

        public async Task<List<JobOrderAnalyticsRow>> GetAnalyticsRowsAsync()
        {
            using var db = new JobOrdersDbContext();
            return await db.JobOrders
                .AsNoTracking()
                .Select(j => new JobOrderAnalyticsRow
                {
                    JobOrderId = j.JobOrderId,
                    Status = j.Status,
                    DeliveryDate = j.DeliveryDate
                })
                .ToListAsync().ConfigureAwait(false);
        }

        // ── SEARCH (bounded, DB-side) ─────────────────────────────────────

        public async Task<List<JobOrderSearchRow>> SearchAsync(
            string term, int take)
        {
            using var db = new JobOrdersDbContext();

            string q = term?.Trim() ?? "";
            if (string.IsNullOrEmpty(q) || take <= 0)
                return new List<JobOrderSearchRow>();

            // Escape LIKE wildcards so DB-side search matches the previous
            // in-memory IndexOf(OrdinalIgnoreCase) semantics for literal terms.
            string like = q.Replace("%", "[%]").Replace("_", "[_]");
            string pattern = $"%{like}%";

            return await db.JobOrders
                .AsNoTracking()
                .Where(j =>
                    EF.Functions.Like(j.JobOrderId ?? "", pattern) ||
                    EF.Functions.Like(j.CustomerName ?? "", pattern) ||
                    EF.Functions.Like(j.ProjectName ?? "", pattern) ||
                    EF.Functions.Like(j.SourceProformaId ?? "", pattern))
                .OrderByDescending(j => j.UpdatedAt)
                .Take(take)
                .Select(j => new JobOrderSearchRow
                {
                    JobOrderId = j.JobOrderId,
                    CustomerName = j.CustomerName,
                    ProjectName = j.ProjectName,
                    SourceProformaId = j.SourceProformaId,
                    Status = j.Status
                })
                .ToListAsync().ConfigureAwait(false);
        }

        // ── LOOKUP FROM PI ────────────────────────────────────────────────

        public async Task<List<SavedJobOrder>> GetBySourceProformaIdAsync(string proformaId)
        {
            if (string.IsNullOrWhiteSpace(proformaId)) return new List<SavedJobOrder>();

            using var db = new JobOrdersDbContext();
            return await db.JobOrders
                .Where(j => j.SourceProformaId == proformaId)
                .OrderByDescending(j => j.CreatedAt)
                .ToListAsync().ConfigureAwait(false);
        }
    }
}