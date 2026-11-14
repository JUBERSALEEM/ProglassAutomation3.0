// ═══════════════════════════════════════════════════════════════════════
// FILE: DeliveryNoteRepository.Part1.cs (Phase 3.8 split — member census verified)
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.DeliveryNote.Common.DeliveryNoteStatus;
using ProGlassAutomation.Models.DeliveryNote.SavedDeliveryNote;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using ProGlassAutomation.Services.DeliveryNote.Database;
using ProGlassAutomation.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Common;
using ProGlassAutomation.Services.Logging;

namespace ProGlassAutomation.Services.DeliveryNote.Repository
{
        public partial class DeliveryNoteRepository : IDeliveryNoteRepository
    {
            /// <summary>
        /// Shared, EF-translatable projection to the list item. Every list
        /// method uses it so the FullSnapshotJson blob column is never read
        /// for a list query. This is the single source of truth for the list
        /// shape (mirrors ToListItem, but runs at the SQL level).
        /// </summary>
        private static readonly System.Linq.Expressions.Expression<
            Func<SavedDeliveryNote, DeliveryNoteListItem>> ToListItemProjection =
            x => new DeliveryNoteListItem
            {
                Id = x.Id,
                DeliveryNoteId = x.DeliveryNoteId,
                JobOrderId = x.JobOrderId,
                ProformaId = x.ProformaId,
                CustomerName = x.CustomerName,
                ProjectName = x.ProjectName,
                Status = x.Status,
                DeliveryType = x.DeliveryType,
                IsFoc = x.IsFoc,
                IsMasterDn = x.IsMasterDn,
                DriverName = x.DriverName,
                VehicleNumber = x.VehicleNumber,
                TotalOrderedQty = x.TotalOrderedQty,
                TotalDeliveredQty = x.TotalDeliveredQty,
                TotalBalanceQty = x.TotalBalanceQty,
                TotalPreviouslyDeliveredQty = x.TotalPreviouslyDeliveredQty,
                SequenceNumber = x.SequenceNumber,
                DeliveryDate = x.DeliveryDate,
                DeliveryTime = x.DeliveryTime,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                TaxInvoiceId = x.TaxInvoiceId,
                HasTaxInvoice = x.HasTaxInvoice,
                IsVoid = x.IsVoid
            };

        public async Task InitializeAsync()
        {
            using var ctx = new DeliveryNotesDbContext();
            await ctx.Database.EnsureCreatedAsync();

            // Additive schema upgrades for an existing deliverynotes.db.
            SchemaMigrationRunner.Upgrade(
                DeliveryNotesDbContext.DatabaseFilePath,
                AppSchemaMigrations.DeliveryNotes,
                "Delivery Note");
        }

        // ── Single DN retrieval ──────────────────────────────────────────

        public async Task<SavedDeliveryNote?> GetByIdAsync(int id)
        {
            using var ctx = new DeliveryNotesDbContext();
            return await ctx.DeliveryNotes
                .Include(x => x.Items)
                .Include(x => x.Attachments)
                .FirstOrDefaultAsync(x => x.Id == id).ConfigureAwait(false);
        }

        public async Task<SavedDeliveryNote?> GetByDeliveryNoteIdAsync(
            string deliveryNoteId)
        {
            using var ctx = new DeliveryNotesDbContext();
            return await ctx.DeliveryNotes
                .Include(x => x.Items)
                .Include(x => x.Attachments)
                .FirstOrDefaultAsync(x => x.DeliveryNoteId == deliveryNoteId).ConfigureAwait(false);
        }

        // ── CHAT 19 — Master DN methods ──────────────────────────────────

        public async Task<SavedDeliveryNote?> GetMasterDnForJobOrderAsync(
            string jobOrderId)
        {
            using var ctx = new DeliveryNotesDbContext();
            return await ctx.DeliveryNotes
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x =>
                    x.JobOrderId == jobOrderId &&
                    x.IsMasterDn).ConfigureAwait(false);
        }

        public async Task<List<DeliveryNoteListItem>> GetMasterDnsAsync()
        {
            using var ctx = new DeliveryNotesDbContext();
            // PERF: project to the list item at the SQL level so the
            // FullSnapshotJson blob is never read for a list query.
            return await ctx.DeliveryNotes
                .Where(x => x.IsMasterDn)
                .AsNoTracking()
                .OrderByDescending(x => x.UpdatedAt)
                .Select(ToListItemProjection)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<SavedDeliveryNote?> CreateMasterDnAsync(
            string jobOrderId,
            string proformaId,
            string customerName,
            string projectName,
            string masterDnId,
            List<DeliveryNoteItem> items)
        {
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.DeliveryNoteEdit, "create a master delivery note");

            using var ctx = new DeliveryNotesDbContext();

            bool exists = await ctx.DeliveryNotes
                .AnyAsync(x => x.JobOrderId == jobOrderId && x.IsMasterDn).ConfigureAwait(false);
            if (exists) return null;

            var now = DateTime.Now;
            int totalQty = items.Sum(i => i.OrderedQty);

            var masterDn = new SavedDeliveryNote
            {
                DeliveryNoteId = masterDnId,
                JobOrderId = jobOrderId,
                ProformaId = proformaId,
                SequenceNumber = 0,
                CustomerName = customerName,
                ProjectName = projectName,
                Status = "Draft",
                DeliveryType = "Dummy",
                IsMasterDn = true,
                LastUpdatedFromDnId = "",
                IsFoc = false,
                DriverName = "",
                VehicleNumber = "",
                ReceiverName = "",
                DeliveryDate = now,
                DeliveryTime = now.ToString("HH:mm"),
                Notes = "Master DN — auto-generated reference tracker",
                TotalOrderedQty = totalQty,
                TotalDeliveredQty = 0,
                TotalBalanceQty = totalQty,
                TotalPreviouslyDeliveredQty = 0,
                TaxInvoiceId = "",
                HasTaxInvoice = false,
                FullSnapshotJson = "{}",
                CreatedAt = now,
                UpdatedAt = now,
                Items = items
            };

            ctx.DeliveryNotes.Add(masterDn);
            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
            return masterDn;
        }

        public async Task UpdateMasterDnAsync(
            string jobOrderId,
            string triggeredByDnId)
        {
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.DeliveryNoteEdit, "update a master delivery note");

            using var ctx = new DeliveryNotesDbContext();

            var masterDn = await ctx.DeliveryNotes
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x =>
                    x.JobOrderId == jobOrderId &&
                    x.IsMasterDn).ConfigureAwait(false);

            if (masterDn == null) return;

            // Confirmed deliveries only. Dispatching/Draft qty is in-progress
            // and must not consume Master remaining until SAVE → Dispatched.
            var realDnIds = await ctx.DeliveryNotes
                .Where(x => x.JobOrderId == jobOrderId &&
                            !x.IsMasterDn &&
                            x.DeliveryType != "Dummy" &&
                            (x.Status == DeliveryNoteStatusValues.Dispatched ||
                             x.Status == DeliveryNoteStatusValues.Delivered ||
                             x.Status == DeliveryNoteStatusValues.Signed ||
                             x.Status == DeliveryNoteStatusValues.Received))
                .Select(x => x.DeliveryNoteId)
                .ToListAsync().ConfigureAwait(false);

            var allRealItems = await ctx.DeliveryNoteItems
                .Where(i => i.JobOrderId == jobOrderId &&
                            realDnIds.Contains(i.DeliveryNoteId))
                .ToListAsync().ConfigureAwait(false);

            var deliveredMap = BuildDeliveredLookup(allRealItems);

            // Self-heal the master's OrderedQty from the authoritative Job
            // Order snapshot. The master is the template every new DN copies
            // OrderedQty from (DeliveryNoteMapper.FromMasterBalance), so if the
            // master ever stored the balance as OrderedQty, every child DN
            // inherited the wrong number (Ordered=64 instead of 76, Prev=0).
            // Rebuild the true per-line ordered map keyed exactly like
            // FromJobOrder, then correct the master in place below. If the JO
            // can't be read, leave stored values untouched.
            var orderedMap = await BuildJobOrderOrderedMapAsync(jobOrderId)
                .ConfigureAwait(false);

            // Recompute PREV/DELIVERED/BALANCE on the master's OWN tracked
            // children, IN PLACE. EF flags each as Modified and issues UPDATEs.
            //
            // BUG FIX (master DN showing empty rows): the previous version
            // mutated masterDn.Items in place AND THEN did
            //   RemoveRange(existingItems); ctx.DeliveryNotes.Update(masterDn);
            // where `existingItems` were the SAME tracked instances as
            // masterDn.Items. EF resolved that to "Deleted", so SaveChanges
            // wiped every master line and re-inserted nothing — leaving the
            // Master DN with zero rows on the next open. Never re-load, delete
            // and re-add tracked children to "refresh" them: mutate in place.
            foreach (var masterItem in masterDn.Items)
            {
                // Heal OrderedQty from the Job Order (source of truth) first.
                var orderedKey = SpecKey(masterItem);
                if (orderedMap.TryGetValue(orderedKey, out var trueOrdered)
                    && trueOrdered > 0
                    && trueOrdered != masterItem.OrderedQty)
                {
                    masterItem.OrderedQty = trueOrdered;
                }

                int totalDelivered = LookupDelivered(deliveredMap, masterItem);
                masterItem.PreviouslyDeliveredQty = totalDelivered;
                masterItem.DeliveredQty = 0;
                masterItem.BalanceQty = Math.Max(0,
                    masterItem.OrderedQty - totalDelivered);
            }

            masterDn.TotalOrderedQty =
                masterDn.Items.Sum(i => i.OrderedQty);
            masterDn.TotalDeliveredQty =
                masterDn.Items.Sum(i => i.PreviouslyDeliveredQty);
            masterDn.TotalBalanceQty =
                masterDn.Items.Sum(i => i.BalanceQty);
            masterDn.TotalPreviouslyDeliveredQty =
                masterDn.TotalDeliveredQty;
            masterDn.LastUpdatedFromDnId = triggeredByDnId;
            masterDn.UpdatedAt = DateTime.Now;

            // The master DN and its Items are already tracked (loaded with
            // Include above), so the change tracker persists the in-place
            // edits — no explicit Update()/RemoveRange() needed.
            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
        }

        public async Task<string> BuildMasterDnIdAsync(string jobOrderId)
        {
            string core = jobOrderId.StartsWith(
                "JOB-", StringComparison.OrdinalIgnoreCase)
                ? jobOrderId.Substring(4)
                : jobOrderId;

            return await Task.FromResult($"MDN-{core}");
        }

        // ── CHAT 19 INVOICED — mark DNs as invoiced ──────────────────────

        /// <summary>
        /// Marks a single DN as invoiced after Tax Invoice is saved.
        /// Sets HasTaxInvoice = true and TaxInvoiceId on the DN.
        /// Only updates indexed columns — FullSnapshotJson NOT rebuilt
        /// (snapshot is used for PDF/export only, not for invoiced state).
        /// Idempotent — safe to call multiple times on same DN.
        /// </summary>
        public async Task MarkAsInvoicedAsync(
            string deliveryNoteId,
            string taxInvoiceId)
        {
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.DeliveryNoteEdit, "mark a delivery note as invoiced");

            if (string.IsNullOrWhiteSpace(deliveryNoteId)) return;
            if (string.IsNullOrWhiteSpace(taxInvoiceId)) return;

            using var ctx = new DeliveryNotesDbContext();

            var entity = await ctx.DeliveryNotes
                .FirstOrDefaultAsync(x =>
                    x.DeliveryNoteId == deliveryNoteId).ConfigureAwait(false);

            if (entity == null) return;

            // Already marked with same TI — no-op
            if (entity.HasTaxInvoice &&
                string.Equals(entity.TaxInvoiceId, taxInvoiceId,
                    StringComparison.OrdinalIgnoreCase))
                return;

            entity.HasTaxInvoice = true;
            entity.TaxInvoiceId = taxInvoiceId;
            entity.UpdatedAt = DateTime.Now;

            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
            DocumentLifecycle.Raise();
        }

        /// <summary>
        /// Marks multiple DNs as invoiced in one operation.
        /// Used when a Master Invoice covers multiple sibling DNs.
        /// deliveryNoteIds parsed from DeliveryNoteReference (comma-separated).
        /// All DNs get the same taxInvoiceId.
        /// Idempotent. Safe with empty list — no-ops gracefully.
        /// Single SaveChangesAsync call for performance.
        /// </summary>
        public async Task MarkMultipleAsInvoicedAsync(
            IEnumerable<string> deliveryNoteIds,
            string taxInvoiceId)
        {
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.DeliveryNoteEdit, "mark delivery notes as invoiced in bulk");

            if (string.IsNullOrWhiteSpace(taxInvoiceId)) return;

            var idList = deliveryNoteIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (idList.Count == 0) return;

            using var ctx = new DeliveryNotesDbContext();

            var entities = await ctx.DeliveryNotes
                .Where(x => idList.Contains(x.DeliveryNoteId))
                .ToListAsync().ConfigureAwait(false);

            if (entities.Count == 0) return;

            var now = DateTime.Now;
            bool anyChanged = false;

            foreach (var entity in entities)
            {
                if (entity.HasTaxInvoice &&
                    string.Equals(entity.TaxInvoiceId, taxInvoiceId,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                entity.HasTaxInvoice = true;
                entity.TaxInvoiceId = taxInvoiceId;
                entity.UpdatedAt = now;
                anyChanged = true;
            }

            if (anyChanged)
            {
                await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
                DocumentLifecycle.Raise();
            }
        }

        /// <summary>
        /// Clears the invoiced flags on every DN still pointing at this
        /// tax invoice. Used when the TI is deleted so list badges and
        /// the DN dependency check match remaining documents.
        /// </summary>
        public async Task ClearInvoicedByTaxInvoiceAsync(string taxInvoiceId)
        {
            if (string.IsNullOrWhiteSpace(taxInvoiceId)) return;

            using var ctx = new DeliveryNotesDbContext();

            var entities = await ctx.DeliveryNotes
                .Where(x => x.HasTaxInvoice &&
                            x.TaxInvoiceId == taxInvoiceId)
                .ToListAsync().ConfigureAwait(false);

            if (entities.Count == 0) return;

            var now = DateTime.Now;
            foreach (var entity in entities)
            {
                entity.HasTaxInvoice = false;
                entity.TaxInvoiceId = "";
                entity.UpdatedAt = now;
            }

            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
            DocumentLifecycle.Raise();
        }

        // ── List retrieval ───────────────────────────────────────────────

        public async Task<List<DeliveryNoteSearchRow>> SearchAsync(
            string term, int take)
        {
            if (!SearchPattern.IsUsable(term) || take <= 0)
                return new List<DeliveryNoteSearchRow>();

            string pattern = SearchPattern.Contains(term);

            using var ctx = new DeliveryNotesDbContext();

            return await ctx.DeliveryNotes
                .AsNoTracking()
                // Same row set as GetAllAsync: master notes are excluded, so a
                // search can never surface a note no list screen shows.
                .Where(x => !x.IsMasterDn)
                .Where(x =>
                    EF.Functions.Like(x.DeliveryNoteId ?? "", pattern, SearchPattern.EscapeChar) ||
                    EF.Functions.Like(x.CustomerName ?? "", pattern, SearchPattern.EscapeChar) ||
                    EF.Functions.Like(x.ProjectName ?? "", pattern, SearchPattern.EscapeChar) ||
                    EF.Functions.Like(x.JobOrderId ?? "", pattern, SearchPattern.EscapeChar))
                .OrderByDescending(x => x.CreatedAt)
                .Take(take)
                .Select(x => new DeliveryNoteSearchRow
                {
                    DeliveryNoteId = x.DeliveryNoteId,
                    CustomerName   = x.CustomerName,
                    ProjectName    = x.ProjectName,
                    JobOrderId     = x.JobOrderId,
                    Status         = x.Status
                })
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<DeliveryNoteListItem>> GetAllAsync()
        {
            using var ctx = new DeliveryNotesDbContext();
            // PERF: SQL-level projection so the FullSnapshotJson blob column
            // is never read for a list query.
            return await ctx.DeliveryNotes
                .Where(x => !x.IsMasterDn)
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .Select(ToListItemProjection)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<(List<DeliveryNoteListItem> Items, int TotalCount)> GetPagedAsync(
            int page, int pageSize, string? search = null)
        {
            using var ctx = new DeliveryNotesDbContext();

            var query = ctx.DeliveryNotes
                .Where(x => !x.IsMasterDn)
                .AsNoTracking();

            // Apply search filter if provided
            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();
                query = query.Where(x =>
                    (x.DeliveryNoteId != null && x.DeliveryNoteId.Contains(term)) ||
                    (x.JobOrderId != null && x.JobOrderId.Contains(term)) ||
                    (x.ProformaId != null && x.ProformaId.Contains(term)) ||
                    (x.CustomerName != null && x.CustomerName.Contains(term)) ||
                    (x.ProjectName != null && x.ProjectName.Contains(term)) ||
                    (x.Status != null && x.Status.Contains(term)));
            }

            int totalCount = await query.CountAsync().ConfigureAwait(false);

            var items = await query
                .OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(ToListItemProjection)
                .ToListAsync().ConfigureAwait(false);

            return (items, totalCount);
        }

        public async Task<List<DeliveryNoteListItem>> GetByJobOrderIdAsync(
            string jobOrderId)
        {
            using var ctx = new DeliveryNotesDbContext();
            // PERF: SQL-level projection so no snapshot blob is read.
            return await ctx.DeliveryNotes
                .Where(x => x.JobOrderId == jobOrderId &&
                            !x.IsMasterDn)
                .AsNoTracking()
                .OrderBy(x => x.SequenceNumber)
                .Select(ToListItemProjection)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<DeliveryNoteListItem>> GetByProformaIdAsync(
            string proformaId)
        {
            using var ctx = new DeliveryNotesDbContext();

            // PERF: SQL-level projection so no snapshot blob is read.
            //
            // Two things must match GetAllAsync exactly, because this replaces
            // a GetAllAsync() + in-memory filter and the callers must see the
            // same rows:
            //   1. the !IsMasterDn exclusion
            //   2. case-insensitive ProformaId matching — the call sites used
            //      StringComparison.OrdinalIgnoreCase, and SQLite `=` is
            //      case-sensitive, so both sides are lower-cased here.
            var key = (proformaId ?? string.Empty).ToLowerInvariant();

            return await ctx.DeliveryNotes
                .Where(x => !x.IsMasterDn &&
                            x.ProformaId.ToLower() == key)
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .Select(ToListItemProjection)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<DeliveryNoteListItem>> GetPreparedAsync()
        {
            using var ctx = new DeliveryNotesDbContext();
            // PERF: SQL-level projection so no snapshot blob is read.
            return await ctx.DeliveryNotes
                .Where(x => !x.IsMasterDn &&
                            (x.Status == DeliveryNoteStatusValues.Draft ||
                             x.Status == DeliveryNoteStatusValues.Dispatching))
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .Select(ToListItemProjection)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<DeliveryNoteListItem>> GetDeliveredAsync()
        {
            using var ctx = new DeliveryNotesDbContext();
            // PERF: SQL-level projection so no snapshot blob is read.
            return await ctx.DeliveryNotes
                .Where(x => (x.Status == DeliveryNoteStatusValues.Delivered ||
                             x.Status == DeliveryNoteStatusValues.Signed ||
                             x.Status == DeliveryNoteStatusValues.Received) &&
                            !x.IsMasterDn)
                .AsNoTracking()
                .OrderByDescending(x => x.DeliveryDate)
                .Select(ToListItemProjection)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<PendingQtySummary>> GetPendingQtyJobsAsync()
        {
            using var ctx = new DeliveryNotesDbContext();

            var realDnIds = await ctx.DeliveryNotes
                .Where(x => !x.IsMasterDn &&
                            x.DeliveryType != "Dummy")
                .Select(x => x.DeliveryNoteId)
                .ToListAsync().ConfigureAwait(false);

            var allItems = await ctx.DeliveryNoteItems
                .Where(i => i.JobOrderId != null &&
                            realDnIds.Contains(i.DeliveryNoteId))
                .ToListAsync().ConfigureAwait(false);

            var grouped = allItems
                .GroupBy(i => i.JobOrderId)
                .ToList();

            // PERF (remove N+1): load the latest real DN per job order in ONE
            // query rather than running a FirstOrDefaultAsync per job order
            // inside the loop. SQLite numbers are small but a large backlog
            // should not hammer the DB with a query per job.
            var joIds = grouped.Select(g => g.Key!).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
            var latestByJo = new Dictionary<string, SavedDeliveryNote>(StringComparer.OrdinalIgnoreCase);

            if (joIds.Count > 0)
            {
                var latestList = await ctx.DeliveryNotes
                    .Where(x => joIds.Contains(x.JobOrderId) && !x.IsMasterDn)
                    .AsNoTracking()
                    .OrderByDescending(x => x.SequenceNumber)
                    .ToListAsync().ConfigureAwait(false);

                foreach (var dn in latestList)
                {
                    if (dn.JobOrderId == null) continue;
                    if (!latestByJo.ContainsKey(dn.JobOrderId))
                        latestByJo[dn.JobOrderId] = dn;
                }
            }

            var result = new List<PendingQtySummary>();

            foreach (var joGroup in grouped)
            {
                string joId = joGroup.Key!;
                if (string.IsNullOrWhiteSpace(joId)) continue;

                var itemGroups = joGroup
                    .GroupBy(i => new { i.SpecGroupNumber, i.SerialNumber });

                int totalOrdered = itemGroups.Sum(g => g.Max(i => i.OrderedQty));
                int totalDelivered = itemGroups.Sum(g => g.Sum(i => i.DeliveredQty));
                int totalBalance = Math.Max(0, totalOrdered - totalDelivered);

                if (totalBalance == 0) continue;

                latestByJo.TryGetValue(joId, out var latestDn);

                result.Add(new PendingQtySummary
                {
                    JobOrderId = joId,
                    CustomerName = latestDn?.CustomerName ?? "",
                    ProjectName = latestDn?.ProjectName ?? "",
                    TotalOrdered = totalOrdered,
                    TotalDelivered = totalDelivered,
                    TotalBalance = totalBalance,
                    LastDnId = latestDn?.DeliveryNoteId ?? ""
                });
            }

            return result.OrderByDescending(x => x.TotalBalance).ToList();
        }

        public async Task<List<DeliveryNoteListItem>> GetReworkAsync()
        {
            using var ctx = new DeliveryNotesDbContext();
            // PERF: SQL-level projection so no snapshot blob is read.
            return await ctx.DeliveryNotes
                .Where(x => x.DeliveryType == "Rework")
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .Select(ToListItemProjection)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<DeliveryNoteListItem>> GetReturnedAsync()
        {
            using var ctx = new DeliveryNotesDbContext();
            // PERF: SQL-level projection so no snapshot blob is read.
            return await ctx.DeliveryNotes
                .Where(x => x.DeliveryType == "Return")
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .Select(ToListItemProjection)
                .ToListAsync().ConfigureAwait(false);
        }

        // ── Delivery history ─────────────────────────────────────────────

        /// <summary>
        /// CHAT 19 INVOICED FIX: Each DeliveryHistoryEntry now carries
        /// HasTaxInvoice and TaxInvoiceId from the DN record.
        /// This allows the history sidebar to show invoiced badge
        /// per DN row without additional DB calls.
        /// </summary>
        public async Task<DeliveryHistorySummary> GetDeliveryHistoryAsync(
            string jobOrderId)
        {
            using var ctx = new DeliveryNotesDbContext();

            var allDns = await ctx.DeliveryNotes
                .Where(x => x.JobOrderId == jobOrderId &&
                            !x.IsMasterDn &&
                            x.DeliveryType != "Dummy")
                .OrderBy(x => x.SequenceNumber)
                .ToListAsync().ConfigureAwait(false);

            var realDnIds = allDns.Select(x => x.DeliveryNoteId).ToList();

            var items = await ctx.DeliveryNoteItems
                .Where(i => i.JobOrderId == jobOrderId &&
                            realDnIds.Contains(i.DeliveryNoteId))
                .ToListAsync().ConfigureAwait(false);

            var itemGroups = items
                .GroupBy(i => new { i.SpecGroupNumber, i.SerialNumber })
                .ToList();

            int totalOrdered = itemGroups.Count > 0
                ? itemGroups.Sum(g => g.Max(i => i.OrderedQty))
                : 0;
            int totalDelivered = itemGroups.Count > 0
                ? itemGroups.Sum(g => g.Sum(i => i.DeliveredQty))
                : 0;
            int totalBalance = Math.Max(0, totalOrdered - totalDelivered);

            if (totalOrdered == 0)
            {
                var masterDn = await ctx.DeliveryNotes
                    .FirstOrDefaultAsync(x =>
                        x.JobOrderId == jobOrderId && x.IsMasterDn).ConfigureAwait(false);
                if (masterDn != null)
                {
                    totalOrdered = masterDn.TotalOrderedQty;
                    totalBalance = totalOrdered;
                }
            }

            var summary = new DeliveryHistorySummary
            {
                JobOrderId = jobOrderId,
                TotalOrdered = totalOrdered,
                TotalDelivered = totalDelivered,
                TotalBalance = totalBalance,
                TotalRework = items.Sum(i => i.ReworkQty),
                TotalReturned = items.Sum(i => i.ReturnedQty)
            };

            int runningPrevTotal = 0;

            foreach (var dn in allDns)
            {
                var dnItems = items
                    .Where(i => i.DeliveryNoteId == dn.DeliveryNoteId);
                int thisDnDelivered = dnItems.Sum(i => i.DeliveredQty);

                int cumulativePrev = runningPrevTotal;
                int cumulativeBalanceAfter = Math.Max(0,
                    totalOrdered - cumulativePrev - thisDnDelivered);

                summary.Entries.Add(new DeliveryHistoryEntry
                {
                    DeliveryNoteId = dn.DeliveryNoteId,
                    DeliveryType = dn.DeliveryType,
                    Status = dn.Status,
                    DeliveredQty = thisDnDelivered,
                    BalanceQty = dn.TotalBalanceQty,
                    DeliveryDate = dn.DeliveryDate,
                    DriverName = dn.DriverName,
                    SequenceNumber = dn.SequenceNumber,
                    OrderedQty = totalOrdered,
                    CumulativePrevQty = cumulativePrev,
                    CumulativeBalanceQty = cumulativeBalanceAfter,

                    // CHAT 19 INVOICED FIX: map invoice state per DN
                    // These drive the invoiced badge in the history sidebar
                    HasTaxInvoice = dn.HasTaxInvoice,
                    TaxInvoiceId = dn.TaxInvoiceId ?? ""
                });

                runningPrevTotal += thisDnDelivered;
            }

            return summary;
        }

        public async Task<int> GetPreviouslyDeliveredQtyAsync(
            string jobOrderId,
            int specGroupNumber,
            int serialNumber)
        {
            using var ctx = new DeliveryNotesDbContext();

            var siblingIds = await ctx.DeliveryNotes
                .Where(x => x.JobOrderId == jobOrderId
                         && !x.IsMasterDn
                         && x.DeliveryType != "Dummy"
                         && (x.Status == DeliveryNoteStatusValues.Dispatched ||
                             x.Status == DeliveryNoteStatusValues.Delivered ||
                             x.Status == DeliveryNoteStatusValues.Signed ||
                             x.Status == DeliveryNoteStatusValues.Received))
                .Select(x => x.DeliveryNoteId)
                .ToListAsync().ConfigureAwait(false);

            return await ctx.DeliveryNoteItems
                .Where(i => siblingIds.Contains(i.DeliveryNoteId)
                         && i.JobOrderId == jobOrderId
                         && i.SpecGroupNumber == specGroupNumber
                         && i.SerialNumber == serialNumber)
                .SumAsync(i => i.DeliveredQty).ConfigureAwait(false);
        }

        public async Task<Dictionary<string, int>> GetPreviouslyDeliveredMapAsync(
            string jobOrderId, string excludeDnId)
        {
            using var ctx = new DeliveryNotesDbContext();

            var siblingIds = await ctx.DeliveryNotes
                .Where(x => x.JobOrderId == jobOrderId
                         && x.DeliveryNoteId != excludeDnId
                         && !x.IsMasterDn
                         && x.DeliveryType != "Dummy"
                         && (x.Status == DeliveryNoteStatusValues.Dispatched ||
                             x.Status == DeliveryNoteStatusValues.Delivered ||
                             x.Status == DeliveryNoteStatusValues.Signed ||
                             x.Status == DeliveryNoteStatusValues.Received))
                .Select(x => x.DeliveryNoteId)
                .ToListAsync().ConfigureAwait(false);

            if (siblingIds.Count == 0)
                return new Dictionary<string, int>();

            var items = await ctx.DeliveryNoteItems
                .Where(i => siblingIds.Contains(i.DeliveryNoteId)
                         && i.JobOrderId == jobOrderId)
                .ToListAsync().ConfigureAwait(false);

            return BuildDeliveredLookup(items);
        }

        private static Dictionary<string, int> BuildDeliveredLookup(
            IEnumerable<DeliveryNoteItem> items)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                if (item.DeliveredQty <= 0) continue;
                // Spec+serial only. Glass-dimension fallback attributed
                // identical panels to each other and zeroed remaining qty.
                Acc(map, SpecKey(item), item.DeliveredQty);
            }
            return map;
        }

        private static int LookupDelivered(
            Dictionary<string, int> map, DeliveryNoteItem item)
        {
            if (map.TryGetValue(SpecKey(item), out var bySpec) && bySpec > 0)
                return bySpec;
            return 0;
        }

        private static string SpecKey(DeliveryNoteItem i) =>
            $"{i.SpecGroupNumber}|{i.SerialNumber}";

        /// <summary>
        /// Authoritative per-line ordered quantities for a job order, keyed
        /// (SpecGroupNumber|SerialNumber) exactly like the DN mapper. Read
        /// from the Job Order snapshot — the ERP source of truth for ordered
        /// qty. Empty map (never throws) when the JO is missing/unreadable, so
        /// callers safely fall back to stored values.
        /// </summary>
        private static async Task<Dictionary<string, int>>
            BuildJobOrderOrderedMapAsync(string jobOrderId)
        {
            try
            {
                if (App.Jobs == null || string.IsNullOrWhiteSpace(jobOrderId))
                    return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                var jobOrder = await App.Jobs
                    .GetByJobOrderIdAsync(jobOrderId).ConfigureAwait(false);
                if (jobOrder == null)
                    return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                var joSnapshot = ProGlassAutomation.Services.JobOrder.Mapper
                    .JobOrderMapper.DeserializeSnapshot(jobOrder);

                return ProGlassAutomation.Services.DeliveryNote.Mapper
                    .DeliveryNoteMapper.BuildOrderedQtyMap(joSnapshot);
            }
            catch (Exception ex)
            {
                AppLogService.Debug(
                    "[DN] BuildJobOrderOrderedMap skipped: " + ex.Message);
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void Acc(Dictionary<string, int> map, string key, int qty)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            map[key] = map.TryGetValue(key, out var n) ? n + qty : qty;
        }

        // ── SQM aggregation ──────────────────────────────────────────────

        public async Task<Dictionary<string, JobOrderSqmSummary>>
            GetSqmSummariesByJobOrderAsync(IEnumerable<string> jobOrderIds)
        {
            var idList = jobOrderIds.ToList();
            if (idList.Count == 0)
                return new Dictionary<string, JobOrderSqmSummary>();

            using var ctx = new DeliveryNotesDbContext();

            var realDnIds = await ctx.DeliveryNotes
                .Where(x => idList.Contains(x.JobOrderId) &&
                            !x.IsMasterDn &&
                            x.DeliveryType != "Dummy")
                .Select(x => x.DeliveryNoteId)
                .ToListAsync().ConfigureAwait(false);

            var items = await ctx.DeliveryNoteItems
                .Where(i => i.JobOrderId != null
                         && idList.Contains(i.JobOrderId)
                         && realDnIds.Contains(i.DeliveryNoteId))
                .ToListAsync().ConfigureAwait(false);

            var result = new Dictionary<string, JobOrderSqmSummary>();
            var joGroups = items.GroupBy(i => i.JobOrderId!);

            foreach (var joGroup in joGroups)
            {
                var uniqueItems = joGroup
                    .GroupBy(i => new { i.SpecGroupNumber, i.SerialNumber });

                decimal orderedSqm = 0m;
                decimal deliveredSqm = 0m;

                foreach (var itemGroup in uniqueItems)
                {
                    var first = itemGroup.First();
                    decimal panelSqm =
                        SqmCalculator.CalculatePanel(first.W1, first.H1);
                    int maxOrdered = itemGroup.Max(i => i.OrderedQty);
                    int totalDelivered = itemGroup.Sum(i => i.DeliveredQty);
                    orderedSqm += panelSqm * maxOrdered;
                    deliveredSqm += panelSqm * totalDelivered;
                }

                result[joGroup.Key!] = new JobOrderSqmSummary
                {
                    OrderedSqm = Math.Round(orderedSqm, 3),
                    DeliveredSqm = Math.Round(deliveredSqm, 3),
                    BalanceSqm = Math.Round(
                        Math.Max(0m, orderedSqm - deliveredSqm), 3)
                };
            }

            return result;
        }

        // ── Numbering ────────────────────────────────────────────────────

        public async Task<int> GetNextSequenceAsync(string jobOrderId)
        {
            using var ctx = new DeliveryNotesDbContext();
            var max = await ctx.DeliveryNotes
                .Where(x => x.JobOrderId == jobOrderId && !x.IsMasterDn)
                .MaxAsync(x => (int?)x.SequenceNumber) ?? 0;
            return max + 1;
        }

        public async Task<string> BuildDeliveryNoteIdAsync(
            string jobOrderId, int sequence)
        {
            string core = jobOrderId.StartsWith(
                "JOB-", StringComparison.OrdinalIgnoreCase)
                ? jobOrderId.Substring(4)
                : jobOrderId;

            return $"DN-{core}-{sequence:D2}";
        }

        // ── CRUD ─────────────────────────────────────────────────────────

        public async Task<SavedDeliveryNote> InsertAsync(SavedDeliveryNote entity)
        {
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.DeliveryNoteEdit, "create a delivery note");

            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            // Master / Dummy tracker ids (MDN-…) are not sequence-bumped.
            if (entity.IsMasterDn)
            {
                using var masterCtx = new DeliveryNotesDbContext();
                entity.CreatedAt = DateTime.Now;
                entity.UpdatedAt = DateTime.Now;
                masterCtx.DeliveryNotes.Add(entity);
                await SqliteResilience.SaveWithRetryAsync(_ => masterCtx.SaveChangesAsync());
                DocumentLifecycle.Raise();
                return entity;
            }

            bool idReallocated = false;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                await using var ctx = new DeliveryNotesDbContext();

                bool exists = await ctx.DeliveryNotes
                    .AnyAsync(x => x.DeliveryNoteId == entity.DeliveryNoteId)
                    .ConfigureAwait(false);
                if (exists)
                {
                    ReallocateDeliveryNoteId(entity);
                    idReallocated = true;
                    continue;
                }

                if (idReallocated)
                    SyncSnapshotDeliveryNoteId(entity);

                entity.CreatedAt = DateTime.Now;
                entity.UpdatedAt = DateTime.Now;
                ctx.DeliveryNotes.Add(entity);
                try
                {
                    await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
                }
                catch (DbUpdateException ex)
                    when (SqliteErrorClassifier.IsUnique(ex) && attempt < 4)
                {
                    ctx.Entry(entity).State = EntityState.Detached;
                    if (entity.Items != null)
                    {
                        foreach (var item in entity.Items)
                            ctx.Entry(item).State = EntityState.Detached;
                    }
                    ReallocateDeliveryNoteId(entity);
                    entity.Id = 0;
                    idReallocated = true;
                    continue;
                }

                DocumentLifecycle.Raise();
                return entity;
            }

            throw new InvalidOperationException(
                "Could not allocate a free DeliveryNoteId after 5 retries " +
                $"starting from '{entity.DeliveryNoteId}'.");
        }

        /// <summary>
        /// Bump SequenceNumber and rebuild DN-{{core}}-{{nn}}. Does not rewrite
        /// an existing stored number; only the in-memory insert that collided.
        /// </summary>
        private static void ReallocateDeliveryNoteId(SavedDeliveryNote entity)
        {
            entity.SequenceNumber = Math.Max(1, entity.SequenceNumber) + 1;
            string jo = entity.JobOrderId ?? "";
            string core = jo.StartsWith("JOB-", StringComparison.OrdinalIgnoreCase)
                ? jo.Substring(4)
                : jo;
            string next = $"DN-{core}-{entity.SequenceNumber:D2}";
            entity.DeliveryNoteId = next;
            if (entity.Items == null) return;
            foreach (var item in entity.Items)
            {
                item.DeliveryNoteId = next;
                item.Id = 0;
            }
        }

        private static void SyncSnapshotDeliveryNoteId(SavedDeliveryNote entity)
        {
            try
            {
                var snap = Mapper.DeliveryNoteMapper.DeserializeSnapshot(entity);
                Mapper.DeliveryNoteMapper.RefreshSnapshot(entity, snap?.JobOrderRef);
            }
            catch (Exception ex)
            {
                AppLogService.Warning(
                    "Could not sync DN snapshot DeliveryNoteId after reallocation: " +
                    ex.Message);
            }
        }

        public async Task UpdateAsync(SavedDeliveryNote entity)
            => await UpdateAsync(entity, expectedRowVersion: null).ConfigureAwait(false);

        /// <summary>
        /// Updates a delivery note. When <paramref name="expectedRowVersion"/>
        /// is supplied and the stored RowVersion has moved on, a
        /// <see cref="Services.Database.ConcurrencyConflictException"/> is thrown
        /// so a concurrent save isn't silently overwritten. Null keeps legacy
        /// behaviour; the counter still increments.
        /// </summary>
        public async Task UpdateAsync(SavedDeliveryNote entity, int? expectedRowVersion)
        {
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.DeliveryNoteEdit, "update a delivery note");

            using var ctx = new DeliveryNotesDbContext();

            // ERP hardening (Phase 5): optimistic-concurrency check + counter.
            var storedRowVersion = await ctx.DeliveryNotes
                .Where(x => x.Id == entity.Id)
                .Select(x => (int?)x.RowVersion)
                .FirstOrDefaultAsync().ConfigureAwait(false);

            if (expectedRowVersion.HasValue
                && storedRowVersion.HasValue
                && storedRowVersion.Value != expectedRowVersion.Value)
            {
                throw new Services.Database.ConcurrencyConflictException(
                    "Delivery Note", entity.DeliveryNoteId,
                    expectedRowVersion.Value, storedRowVersion.Value);
            }

            entity.RowVersion = (storedRowVersion ?? entity.RowVersion) + 1;
            entity.UpdatedAt = DateTime.Now;

            var existingItems = await ctx.DeliveryNoteItems
                .Where(i => i.DeliveryNoteId == entity.DeliveryNoteId)
                .ToListAsync().ConfigureAwait(false);
            ctx.DeliveryNoteItems.RemoveRange(existingItems);

            entity.Attachments.Clear();

            ctx.DeliveryNotes.Update(entity);
            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
            DocumentLifecycle.Raise();
        }
    }
}
