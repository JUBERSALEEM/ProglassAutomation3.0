using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.TaxInvoice.Attachment;
using ProGlassAutomation.Models.TaxInvoice.SavedTaxInvoice;
using ProGlassAutomation.Models.TaxInvoice.TaxInvoiceFamily;
using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.Common;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using ProGlassAutomation.Services.Licensing;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Services.ProformaInvoice;
using ProGlassAutomation.Services.TaxInvoice.Database;
using ProGlassAutomation.Services.TaxInvoice.Mapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.TaxInvoice.Repository
{
    /// <summary>
    /// EF Core implementation of ITaxInvoiceRepository.
    ///
    /// Senior hardening notes:
    /// - service-layer permission + read-only enforcement lives here
    /// - save/update/status operations validate business rules before hitting DB
    /// - family queries backfill ProformaTotal from the PI repository so
    ///   progress/balance calculations do not silently fall back to invoice sums
    /// </summary>
    public class TaxInvoiceRepository : ITaxInvoiceRepository
    {
        public async Task InitializeAsync()
        {
            using var ctx = new TaxInvoiceDbContext();
            await ctx.Database.EnsureCreatedAsync();

            SchemaMigrationRunner.Upgrade(
                TaxInvoiceDbContext.DatabaseFilePath,
                AppSchemaMigrations.TaxInvoices,
                "Tax Invoice");
        }

        // ── Single invoice retrieval ──────────────────────────────────

        public async Task<SavedTaxInvoice?> GetByIdAsync(int id)
        {
            using var ctx = new TaxInvoiceDbContext();
            return await ctx.TaxInvoices
                .Include(x => x.Items)
                .Include(x => x.Attachments)
                .FirstOrDefaultAsync(x => x.Id == id).ConfigureAwait(false);
        }

        public async Task<SavedTaxInvoice?> GetByInvoiceNumberAsync(string invoiceNumber)
        {
            using var ctx = new TaxInvoiceDbContext();
            return await ctx.TaxInvoices
                .Include(x => x.Items)
                .Include(x => x.Attachments)
                .FirstOrDefaultAsync(x => x.InvoiceNumber == invoiceNumber).ConfigureAwait(false);
        }

        // ── List retrieval ────────────────────────────────────────────

        public async Task<List<TaxInvoiceSearchRow>> SearchAsync(
            string term, int take)
        {
            if (!SearchPattern.IsUsable(term) || take <= 0)
                return new List<TaxInvoiceSearchRow>();

            string pattern = SearchPattern.Contains(term);

            using var ctx = new TaxInvoiceDbContext();

            return await ctx.TaxInvoices
                .AsNoTracking()
                .Where(x =>
                    EF.Functions.Like(x.InvoiceNumber ?? "", pattern, SearchPattern.EscapeChar) ||
                    EF.Functions.Like(x.CustomerName ?? "", pattern, SearchPattern.EscapeChar) ||
                    EF.Functions.Like(x.ProjectName ?? "", pattern, SearchPattern.EscapeChar) ||
                    EF.Functions.Like(x.ProformaId ?? "", pattern, SearchPattern.EscapeChar))
                .OrderByDescending(x => x.CreatedAt)
                .Take(take)
                .Select(x => new TaxInvoiceSearchRow
                {
                    InvoiceNumber = x.InvoiceNumber,
                    CustomerName  = x.CustomerName,
                    ProjectName   = x.ProjectName,
                    ProformaId    = x.ProformaId,
                    Status        = x.Status
                })
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<TaxInvoiceListItem>> GetAllAsync()
        {
            using var ctx = new TaxInvoiceDbContext();
            return await ctx.TaxInvoices
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new TaxInvoiceListItem
                {
                    Id = x.Id,
                    InvoiceNumber = x.InvoiceNumber,
                    ProformaId = x.ProformaId,
                    DeliveryNoteReference = x.DeliveryNoteReference,
                    JobOrderId = x.JobOrderId,
                    PurchaseOrderNumber = x.PurchaseOrderNumber,
                    InvoiceType = x.InvoiceType,
                    SequenceLetter = x.SequenceLetter,
                    SequenceNumber = x.SequenceNumber,
                    Status = x.Status,
                    CustomerName = x.CustomerName,
                    ProjectName = x.ProjectName,
                    CompanyName = x.CompanyName,
                    PaymentTerms = x.PaymentTerms,
                    IssueDate = x.IssueDate,
                    DueDate = x.DueDate,
                    Subtotal = x.Subtotal,
                    DiscountAmount = x.DiscountAmount,
                    AfterDiscount = x.AfterDiscount,
                    VatAmount = x.VatAmount,
                    GrandTotal = x.GrandTotal,
                    TotalQty = x.TotalQty,
                    TotalSqm = x.TotalSqm,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt,
                    IsVoid = x.IsVoid
                })
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<TaxInvoiceListItem>> GetByProformaIdAsync(string proformaId)
        {
            using var ctx = new TaxInvoiceDbContext();
            var entities = await ctx.TaxInvoices
                .Where(x => x.ProformaId == proformaId)
                .OrderBy(x => x.SequenceNumber)
                .ToListAsync().ConfigureAwait(false);
            return entities.Select(ToListItem).ToList();
        }

        public async Task<List<TaxInvoiceListItem>> GetByStatusAsync(string status)
        {
            using var ctx = new TaxInvoiceDbContext();
            var entities = await ctx.TaxInvoices
                .Where(x => x.Status == status)
                .OrderByDescending(x => x.IssueDate)
                .ToListAsync().ConfigureAwait(false);
            return entities.Select(ToListItem).ToList();
        }

        public async Task<List<TaxInvoiceListItem>> GetByPurchaseOrderAsync(
            string poNumber, string customerName)
        {
            using var ctx = new TaxInvoiceDbContext();
            var entities = await ctx.TaxInvoices
                .Where(x => x.PurchaseOrderNumber == poNumber
                         && x.CustomerName == customerName)
                .OrderByDescending(x => x.IssueDate)
                .ToListAsync().ConfigureAwait(false);
            return entities.Select(ToListItem).ToList();
        }

        public async Task<List<TaxInvoiceListItem>> GetDraftAsync() =>
            await GetByStatusAsync("Draft");

        public async Task<List<TaxInvoiceListItem>> GetIssuedAsync() =>
            await GetByStatusAsync("Issued");

        public async Task<List<TaxInvoiceListItem>> GetPaidAsync() =>
            await GetByStatusAsync("Paid");

        public async Task<List<TaxInvoiceListItem>> GetOverdueAsync()
        {
            using var ctx = new TaxInvoiceDbContext();
            var entities = await ctx.TaxInvoices
                .Where(x => x.Status == "Overdue"
                         || (x.Status == "Issued" && x.DueDate < DateTime.Today))
                .OrderBy(x => x.DueDate)
                .ToListAsync().ConfigureAwait(false);
            return entities.Select(ToListItem).ToList();
        }

        // ── Family retrieval ──────────────────────────────────────────

        public async Task<List<TaxInvoiceFamily>> GetAllFamiliesAsync()
        {
            using var ctx = new TaxInvoiceDbContext();
            var entities = await ctx.TaxInvoices
                .OrderByDescending(x => x.IssueDate)
                .ToListAsync().ConfigureAwait(false);

            var items = entities.Select(ToListItem).ToList();
            var families = TaxInvoiceFamily.BuildFrom(items).ToList();
            await PopulateProformaTotalsAsync(families);
            return families;
        }

        public async Task<TaxInvoiceFamily?> GetFamilyAsync(string proformaId)
        {
            using var ctx = new TaxInvoiceDbContext();
            var entities = await ctx.TaxInvoices
                .Where(x => x.ProformaId == proformaId)
                .OrderBy(x => x.SequenceNumber)
                .ToListAsync().ConfigureAwait(false);

            if (entities.Count == 0) return null;

            var items = entities.Select(ToListItem).ToList();
            var families = TaxInvoiceFamily.BuildFrom(items).ToList();
            await PopulateProformaTotalsAsync(families);
            return families.FirstOrDefault();
        }

        // ── Financial summary ─────────────────────────────────────────

        public async Task<InvoiceFamilySummary> GetFamilySummaryAsync(string proformaId)
        {
            using var ctx = new TaxInvoiceDbContext();

            var members = await ctx.TaxInvoices
                .Where(x => x.ProformaId == proformaId)
                .ToListAsync().ConfigureAwait(false);

            if (members.Count == 0)
                return new InvoiceFamilySummary { ProformaId = proformaId };

            var issuedMembers = members
                .Where(m => ProGlassAutomation.Models.TaxInvoice.Common
                    .TaxInvoiceStatus.TaxInvoiceStatusValues.HasBeenIssued(m.Status))
                .ToList();

            return new InvoiceFamilySummary
            {
                ProformaId = proformaId,
                InvoiceCount = members.Count,
                TotalInvoiced = issuedMembers.Sum(m => m.GrandTotal),
                TotalVat = issuedMembers.Sum(m => m.VatAmount),
                TotalGrandTotal = members.Sum(m => m.GrandTotal),
                ProformaTotal = await ResolveProformaTotalAsync(
                    proformaId,
                    members.Count > 0 ? members.Max(m => m.GrandTotal) : 0m)
            };
        }

        // ── Numbering ────────────────────────────────────────────────

        public async Task<int> GetNextSequenceNumberAsync(string proformaId)
        {
            using var ctx = new TaxInvoiceDbContext();
            var max = await ctx.TaxInvoices
                .Where(x => x.ProformaId == proformaId)
                .MaxAsync(x => (int?)x.SequenceNumber) ?? 0;
            return max + 1;
        }

        public async Task<string> GetNextSequenceLetterAsync(string proformaId)
        {
            using var ctx = new TaxInvoiceDbContext();
            var letters = await ctx.TaxInvoices
                .Where(x => x.ProformaId == proformaId
                         && x.SequenceLetter != null
                         && x.SequenceLetter != "")
                .Select(x => x.SequenceLetter)
                .ToListAsync().ConfigureAwait(false);

            if (letters.Count == 0) return "A";

            var used = new HashSet<string>(
                letters.Where(l => l.Length == 1 && l[0] >= 'A' && l[0] <= 'Z'),
                StringComparer.OrdinalIgnoreCase);

            for (char c = 'A'; c <= 'Z'; c++)
            {
                string letter = c.ToString();
                if (!used.Contains(letter)) return letter;
            }

            throw new InvalidOperationException(
                "No free sequence letter A–Z remains for proforma '" +
                proformaId + "'.");
        }

        public async Task<string> BuildInvoiceNumberAsync(string proformaId, string sequenceLetter = "")
        {
            string core = proformaId.StartsWith("PI-", StringComparison.OrdinalIgnoreCase)
                ? proformaId.Substring(3)
                : proformaId;

            string baseNumber = $"INV-{core}";
            return string.IsNullOrWhiteSpace(sequenceLetter)
                ? baseNumber
                : $"{baseNumber}-{sequenceLetter.ToUpperInvariant().Trim()}";
        }

        // ── CRUD ─────────────────────────────────────────────────────

        public async Task<SavedTaxInvoice> InsertAsync(SavedTaxInvoice entity)
        {
            OperationGuard.Demand(AppPermissions.TaxInvoiceEdit, "create a tax invoice");
            ReadOnlyGate.Demand("create a tax invoice");
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
            ThrowIfInvalid(TaxInvoiceValidationService.ValidateSave(entity));

            bool idReallocated = false;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                await using var ctx = new TaxInvoiceDbContext();

                bool exists = await ctx.TaxInvoices
                    .AnyAsync(x => x.InvoiceNumber == entity.InvoiceNumber)
                    .ConfigureAwait(false);
                if (exists)
                {
                    await ReallocateInvoiceNumberAsync(entity).ConfigureAwait(false);
                    idReallocated = true;
                    continue;
                }

                if (idReallocated)
                    SyncSnapshotInvoiceNumber(entity);

                entity.CreatedAt = DateTime.Now;
                entity.UpdatedAt = DateTime.Now;
                ctx.TaxInvoices.Add(entity);
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
                    await ReallocateInvoiceNumberAsync(entity).ConfigureAwait(false);
                    entity.Id = 0;
                    idReallocated = true;
                    continue;
                }

                DocumentLifecycle.Raise();
                return entity;
            }

            throw new InvalidOperationException(
                "Could not allocate a free InvoiceNumber after 5 retries " +
                $"starting from '{entity.InvoiceNumber}'.");
        }

        private async Task ReallocateInvoiceNumberAsync(SavedTaxInvoice entity)
        {
            entity.SequenceNumber = Math.Max(1, entity.SequenceNumber) + 1;
            string letter = await GetNextSequenceLetterAsync(entity.ProformaId)
                .ConfigureAwait(false);
            entity.SequenceLetter = letter;
            entity.InvoiceType = string.IsNullOrWhiteSpace(letter) ? "Full" : "Partial";
            entity.InvoiceNumber = await BuildInvoiceNumberAsync(
                entity.ProformaId, letter).ConfigureAwait(false);
            if (entity.Items == null) return;
            foreach (var item in entity.Items)
            {
                item.InvoiceNumber = entity.InvoiceNumber;
                item.Id = 0;
            }
        }

        private static void SyncSnapshotInvoiceNumber(SavedTaxInvoice entity)
        {
            try
            {
                var snap = TaxInvoiceMapper.DeserializeSnapshot(entity);
                if (snap?.Header == null) return;
                if (string.Equals(snap.Header.InvoiceNumber, entity.InvoiceNumber,
                        StringComparison.Ordinal))
                    return;
                snap.Header.InvoiceNumber = entity.InvoiceNumber;
                snap.Header.SequenceLetter = entity.SequenceLetter;
                snap.Header.SequenceNumber = entity.SequenceNumber;
                snap.Header.InvoiceType = entity.InvoiceType;
                entity.FullSnapshotJson = JsonSerializer.Serialize(snap);
            }
            catch (Exception ex)
            {
                AppLogService.Warning(
                    "Could not sync TI snapshot InvoiceNumber after reallocation: " +
                    ex.Message);
            }
        }

        public async Task UpdateAsync(SavedTaxInvoice entity)
            => await UpdateAsync(entity, expectedRowVersion: null).ConfigureAwait(false);

        /// <summary>
        /// Updates a tax invoice. When <paramref name="expectedRowVersion"/> is
        /// supplied (the value the editor loaded) and the stored RowVersion has
        /// moved on, a <see cref="ConcurrencyConflictException"/> is thrown so a
        /// second user's save can't be silently overwritten. Passing null keeps
        /// the legacy last-writer-wins behaviour; the counter still increments.
        /// </summary>
        public async Task UpdateAsync(SavedTaxInvoice entity, int? expectedRowVersion)
        {
            OperationGuard.Demand(AppPermissions.TaxInvoiceEdit, "update a tax invoice");
            ReadOnlyGate.Demand("update a tax invoice");
            ThrowIfInvalid(TaxInvoiceValidationService.ValidateSave(entity));

            using var ctx = new TaxInvoiceDbContext();

            // ERP hardening (Phase 5): optimistic-concurrency check + counter.
            var storedRowVersion = await ctx.TaxInvoices
                .Where(x => x.Id == entity.Id)
                .Select(x => (int?)x.RowVersion)
                .FirstOrDefaultAsync().ConfigureAwait(false);

            if (expectedRowVersion.HasValue
                && storedRowVersion.HasValue
                && storedRowVersion.Value != expectedRowVersion.Value)
            {
                throw new ConcurrencyConflictException(
                    "Tax Invoice", entity.InvoiceNumber,
                    expectedRowVersion.Value, storedRowVersion.Value);
            }

            entity.RowVersion = (storedRowVersion ?? entity.RowVersion) + 1;
            entity.UpdatedAt = DateTime.Now;

            var existingItems = await ctx.TaxInvoiceItems
                .Where(i => i.InvoiceNumber == entity.InvoiceNumber)
                .ToListAsync().ConfigureAwait(false);
            ctx.TaxInvoiceItems.RemoveRange(existingItems);

            entity.Attachments.Clear();

            ctx.TaxInvoices.Update(entity);
            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
            DocumentLifecycle.Raise();
        }

        public async Task DeleteAsync(int id)
        {
            OperationGuard.Demand(AppPermissions.TaxInvoiceDelete, "delete a tax invoice");
            ReadOnlyGate.Demand("delete a tax invoice");

            using var ctx = new TaxInvoiceDbContext();
            var entity = await ctx.TaxInvoices
                .Include(x => x.Items)
                .Include(x => x.Attachments)
                .FirstOrDefaultAsync(x => x.Id == id).ConfigureAwait(false);

            if (entity == null) return;

            string invoiceNumber = entity.InvoiceNumber;
            string? proformaId = entity.ProformaId;

            ctx.TaxInvoices.Remove(entity);
            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());

            // ERP hardening (Phase 5): destroying a fiscal document must leave
            // an audit trail even though only a childless draft can reach here.
            AuditService.Log(
                AuditSections.TaxInvoice, "Tax invoice deleted",
                $"Deleted draft tax invoice {invoiceNumber}"
                    + (string.IsNullOrWhiteSpace(proformaId) ? "" : $" (proforma {proformaId})"),
                entityType: nameof(SavedTaxInvoice), entityId: invoiceNumber);

            await ClearLinkedDeliveryNotesAsync(invoiceNumber);
            await ProformaLifecycleSync.AfterDownstreamChangeAsync(proformaId);
            DocumentLifecycle.Raise();
        }

        /// <summary>
        /// ERP lifecycle hardening (Phase 1): soft-close (void) instead of a
        /// physical delete — a Tax Invoice is a fiscal document that must be
        /// retained, never erased. The row (and its number) is kept and made
        /// visible-but-voided to VAT/reports/audit; Status moves to the void
        /// target (Cancelled). Linked DNs are un-invoiced so they can be
        /// re-invoiced (or credited) — the same cleanup a delete did.
        /// </summary>
        public async Task VoidAsync(int id, string reason)
        {
            OperationGuard.Demand(AppPermissions.TaxInvoiceDelete, "void a tax invoice");
            ReadOnlyGate.Demand("void a tax invoice");

            using var ctx = new TaxInvoiceDbContext();
            var entity = await ctx.TaxInvoices
                .FirstOrDefaultAsync(x => x.Id == id).ConfigureAwait(false);

            if (entity == null) return;
            if (entity.IsVoid) return;   // idempotent

            string invoiceNumber = entity.InvoiceNumber;
            string? proformaId = entity.ProformaId;

            entity.IsVoid = true;
            entity.VoidedAtUtc = DateTime.UtcNow;
            entity.VoidedBy = AuditService.CurrentUsername;
            entity.VoidReason = string.IsNullOrWhiteSpace(reason) ? "(no reason given)" : reason.Trim();
            entity.Status = Services.Workflow.DocumentLifecyclePolicy.VoidTargetStatus(
                Services.Workflow.StatusTransitions.Kind.TaxInvoice);
            entity.UpdatedAt = DateTime.Now;

            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());

            // ERP hardening (Phase 5): a void carries a mandatory reason and
            // must be auditable — it's the compliant alternative to deletion.
            AuditService.Log(
                AuditSections.TaxInvoice, "Tax invoice voided",
                $"Voided tax invoice {invoiceNumber}"
                    + (string.IsNullOrWhiteSpace(proformaId) ? "" : $" (proforma {proformaId})"),
                entityType: nameof(SavedTaxInvoice), entityId: invoiceNumber,
                reason: entity.VoidReason);

            await ClearLinkedDeliveryNotesAsync(invoiceNumber);
            await ProformaLifecycleSync.AfterDownstreamChangeAsync(proformaId);
            DocumentLifecycle.Raise();
        }

        private static async Task ClearLinkedDeliveryNotesAsync(string? invoiceNumber)
        {
            if (string.IsNullOrWhiteSpace(invoiceNumber) || App.DeliveryNotes == null)
                return;

            try
            {
                await App.DeliveryNotes.ClearInvoicedByTaxInvoiceAsync(invoiceNumber);
            }
            catch (Exception ex)
            {
                AppLogService.Warning("[TaxInvoiceRepository] Linked-DN badge cleanup failed: " + ex.Message);
            }
        }

        // ── Existence checks ─────────────────────────────────────────

        public async Task<bool> ExistsByInvoiceNumberAsync(string invoiceNumber)
        {
            using var ctx = new TaxInvoiceDbContext();
            return await ctx.TaxInvoices.AnyAsync(x => x.InvoiceNumber == invoiceNumber).ConfigureAwait(false);
        }

        public async Task<bool> HasAnyInvoiceAsync(string proformaId)
        {
            using var ctx = new TaxInvoiceDbContext();
            return await ctx.TaxInvoices.AnyAsync(x => x.ProformaId == proformaId).ConfigureAwait(false);
        }

        // ── Status management ──────────────────────────────────────

        public async Task UpdateStatusAsync(string invoiceNumber, string newStatus)
        {
            OperationGuard.Demand(AppPermissions.TaxInvoiceEdit, "change a tax invoice status");
            ReadOnlyGate.Demand("change a tax invoice status");

            using var ctx = new TaxInvoiceDbContext();
            var entity = await ctx.TaxInvoices
                .FirstOrDefaultAsync(x => x.InvoiceNumber == invoiceNumber).ConfigureAwait(false);

            if (entity == null) return;

            ThrowIfInvalid(TaxInvoiceValidationService.ValidateStatusTransition(entity.Status, newStatus));
            TaxInvoiceMapper.SetHeaderStatus(entity, newStatus);
            entity.UpdatedAt = DateTime.Now;
            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
            DocumentLifecycle.Raise();
        }

        // ── Attachments ─────────────────────────────────────────────

        public async Task<List<TaxInvoiceAttachment>> GetAttachmentsAsync(string invoiceNumber)
        {
            using var ctx = new TaxInvoiceDbContext();
            return await ctx.TaxInvoiceAttachments
                .Where(a => a.InvoiceNumber == invoiceNumber)
                .OrderBy(a => a.AttachmentType == "SignedPI" ? 0
                            : a.AttachmentType == "SignedDN" ? 1 : 2)
                .ThenByDescending(a => a.UploadedAt)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<TaxInvoiceAttachment> AddAttachmentAsync(TaxInvoiceAttachment entity)
        {
            OperationGuard.Demand(AppPermissions.TaxInvoiceEdit, "add a tax invoice attachment");
            ReadOnlyGate.Demand("add a tax invoice attachment");

            using var ctx = new TaxInvoiceDbContext();
            entity.UploadedAt = DateTime.Now;
            ctx.TaxInvoiceAttachments.Add(entity);
            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
            return entity;
        }

        public async Task DeleteAttachmentAsync(int attachmentId)
        {
            OperationGuard.Demand(AppPermissions.TaxInvoiceEdit, "delete a tax invoice attachment");
            ReadOnlyGate.Demand("delete a tax invoice attachment");

            using var ctx = new TaxInvoiceDbContext();
            var entity = await ctx.TaxInvoiceAttachments
                .FirstOrDefaultAsync(a => a.Id == attachmentId).ConfigureAwait(false);
            if (entity == null) return;

            ctx.TaxInvoiceAttachments.Remove(entity);
            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
        }

        public async Task<int> GetAttachmentCountAsync(string invoiceNumber)
        {
            using var ctx = new TaxInvoiceDbContext();
            return await ctx.TaxInvoiceAttachments
                .CountAsync(a => a.InvoiceNumber == invoiceNumber).ConfigureAwait(false);
        }

        public async Task<Dictionary<string, int>> GetAttachmentCountsAsync(IEnumerable<string> invoiceNumbers)
        {
            var idList = invoiceNumbers.ToList();
            if (idList.Count == 0)
                return new Dictionary<string, int>();

            using var ctx = new TaxInvoiceDbContext();
            var raw = await ctx.TaxInvoiceAttachments
                .Where(a => idList.Contains(a.InvoiceNumber))
                .GroupBy(a => a.InvoiceNumber)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToListAsync().ConfigureAwait(false);

            var dict = raw.ToDictionary(x => x.Id, x => x.Count);
            foreach (var id in idList)
                if (!dict.ContainsKey(id))
                    dict[id] = 0;

            return dict;
        }

        // ── Analytics projection ────────────────────────────────────

        public async Task<List<TaxInvoiceAnalyticsRow>> GetAnalyticsRowsAsync()
        {
            using var ctx = new TaxInvoiceDbContext();
            return await ctx.TaxInvoices
                .AsNoTracking()
                .Select(t => new TaxInvoiceAnalyticsRow
                {
                    InvoiceNumber = t.InvoiceNumber,
                    Status = t.Status,
                    DueDate = t.DueDate,
                    GrandTotal = t.GrandTotal
                })
                .ToListAsync().ConfigureAwait(false);
        }

        // ── Private helpers ─────────────────────────────────────────

        private static void ThrowIfInvalid(Result result)
        {
            if (!result.IsFailure) return;
            throw new InvalidOperationException(result.Error);
        }

        private static async Task PopulateProformaTotalsAsync(IEnumerable<TaxInvoiceFamily> families)
        {
            if (families == null) return;

            var familyList = families.ToList();
            if (familyList.Count == 0) return;

            var totals = await ResolveProformaTotalsAsync(familyList.Select(f => f.ProformaId));
            foreach (var family in familyList)
            {
                family.ProformaTotal = totals.TryGetValue(family.ProformaId, out var total)
                    ? total
                    : family.OrderedTotal;
            }
        }

        private static async Task<Dictionary<string, decimal>> ResolveProformaTotalsAsync(IEnumerable<string> proformaIds)
        {
            var ids = (proformaIds ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            if (ids.Count == 0 || App.Invoices == null)
                return totals;

            foreach (var id in ids)
                totals[id] = await ResolveProformaTotalAsync(id, 0m);

            return totals;
        }

        private static async Task<decimal> ResolveProformaTotalAsync(string proformaId, decimal fallback)
        {
            if (string.IsNullOrWhiteSpace(proformaId) || App.Invoices == null)
                return fallback;

            try
            {
                var pi = await App.Invoices.GetByProformaIdAsync(proformaId);
                if (pi != null && pi.GrandTotalVatInclusive > 0m)
                    return pi.GrandTotalVatInclusive;
            }
            catch (Exception ex)
            {
                AppLogService.Debug(
                    "[TaxInvoiceRepository] Proforma total lookup failed for '" +
                    proformaId + "': " + ex.Message);
            }

            return fallback;
        }

        private static TaxInvoiceListItem ToListItem(SavedTaxInvoice e) =>
            new()
            {
                Id = e.Id,
                InvoiceNumber = e.InvoiceNumber,
                ProformaId = e.ProformaId,
                DeliveryNoteReference = e.DeliveryNoteReference,
                JobOrderId = e.JobOrderId,
                PurchaseOrderNumber = e.PurchaseOrderNumber,
                InvoiceType = e.InvoiceType,
                SequenceLetter = e.SequenceLetter,
                SequenceNumber = e.SequenceNumber,
                Status = e.Status,
                CustomerName = e.CustomerName,
                ProjectName = e.ProjectName,
                CompanyName = e.CompanyName,
                PaymentTerms = e.PaymentTerms,
                IssueDate = e.IssueDate,
                DueDate = e.DueDate,
                Subtotal = e.Subtotal,
                DiscountAmount = e.DiscountAmount,
                AfterDiscount = e.AfterDiscount,
                VatAmount = e.VatAmount,
                GrandTotal = e.GrandTotal,
                TotalQty = e.TotalQty,
                TotalSqm = e.TotalSqm,
                CreatedAt = e.CreatedAt,
                UpdatedAt = e.UpdatedAt
            };

        // ── Result-pattern variants ─────────────────────────────────

        public async Task<Result<SavedTaxInvoice>> InsertWithResultAsync(SavedTaxInvoice entity, string? correlationId = null)
        {
            OperationGuard.Demand(AppPermissions.TaxInvoiceEdit, "create a tax invoice (Result variant)");
            ReadOnlyGate.Demand("create a tax invoice");

            var corrId = correlationId ?? CorrelationContext.NewIdFor("TaxInvoice", entity?.InvoiceNumber);
            using var scope = CorrelationContext.BeginDocumentScope("TaxInvoice", entity?.InvoiceNumber ?? "", corrId);

            try
            {
                var result = await InsertAsync(entity!);
                AppLogService.Info(
                    $"TI inserted: {entity!.InvoiceNumber} for DN {entity.DeliveryNoteReference}",
                    new LogContext
                    {
                        Module = "TaxInvoice",
                        Operation = "Insert",
                        DocumentId = entity.InvoiceNumber,
                        DocumentType = "TaxInvoice",
                        CorrelationId = corrId
                    });
                return Result<SavedTaxInvoice>.Ok(result, corrId);
            }
            catch (Exception ex)
            {
                var errorId = AppLogService.Error(
                    $"Failed to insert TI {entity?.InvoiceNumber}: {ex.Message}",
                    ex,
                    new LogContext
                    {
                        Module = "TaxInvoice",
                        Operation = "Insert",
                        DocumentId = entity?.InvoiceNumber ?? "",
                        DocumentType = "TaxInvoice",
                        CorrelationId = corrId
                    });
                return Result<SavedTaxInvoice>.Fail(
                    $"Could not save tax invoice {entity?.InvoiceNumber}: {ex.Message}",
                    ex, corrId, errorId);
            }
        }

        public async Task<Result> DeleteWithResultAsync(int id, string? invoiceNumber = null, string? correlationId = null)
        {
            OperationGuard.Demand(AppPermissions.TaxInvoiceDelete, "delete a tax invoice (Result variant)");
            ReadOnlyGate.Demand("delete a tax invoice");

            var corrId = correlationId ?? CorrelationContext.NewIdFor("TaxInvoice", invoiceNumber ?? id.ToString());
            using var scope = CorrelationContext.BeginDocumentScope("TaxInvoice", invoiceNumber ?? id.ToString(), corrId);

            try
            {
                await DeleteAsync(id);
                AppLogService.Info(
                    $"TI deleted: Id={id}, InvoiceNumber={invoiceNumber}",
                    new LogContext
                    {
                        Module = "TaxInvoice",
                        Operation = "Delete",
                        DocumentId = invoiceNumber ?? id.ToString(),
                        DocumentType = "TaxInvoice",
                        CorrelationId = corrId
                    });
                return Result.Ok(corrId);
            }
            catch (Exception ex)
            {
                var errorId = AppLogService.Error(
                    $"Failed to delete TI Id={id}: {ex.Message}",
                    ex,
                    new LogContext
                    {
                        Module = "TaxInvoice",
                        Operation = "Delete",
                        DocumentId = invoiceNumber ?? id.ToString(),
                        DocumentType = "TaxInvoice",
                        CorrelationId = corrId
                    });
                return Result.Fail($"Could not delete tax invoice: {ex.Message}", ex, corrId, errorId);
            }
        }
    }
}
