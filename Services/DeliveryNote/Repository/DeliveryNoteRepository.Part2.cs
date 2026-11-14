// ═══════════════════════════════════════════════════════════════════════
// FILE: DeliveryNoteRepository.Part2.cs (Phase 3.8 split — member census verified)
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.EntityFrameworkCore;
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
            public async Task DeleteAsync(int id)
        {
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.DeliveryNoteEdit, "delete a delivery note");

            using var ctx = new DeliveryNotesDbContext();
            var entity = await ctx.DeliveryNotes
                .Include(x => x.Items)
                .Include(x => x.Attachments)
                .FirstOrDefaultAsync(x => x.Id == id).ConfigureAwait(false);

            if (entity == null) return;

            string? proformaId = entity.ProformaId;
            string jobOrderId = entity.JobOrderId;
            string deliveryNoteId = entity.DeliveryNoteId;
            bool isMaster = entity.IsMasterDn;

            ctx.DeliveryNotes.Remove(entity);
            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());

            // Master DNs are internal trackers — they do not participate
            // in the PI display status. A real DN delete may cancel the
            // source PI when nothing downstream remains. Run the sync
            // BEFORE raising the change signal so PI list refreshes see
            // the final (cancelled) status the first time.
            if (!isMaster)
            {
                try
                {
                    await UpdateMasterDnAsync(jobOrderId, deliveryNoteId)
                        .ConfigureAwait(false);
                }
                catch (Exception masterEx)
                {
                    AppLogService.Warning(
                        "[DN] Master remaining refresh after delete skipped: " +
                        masterEx.Message);
                }

                await ProGlassAutomation.Services.ProformaInvoice
                    .ProformaLifecycleSync.AfterDownstreamChangeAsync(proformaId);
            }

            DocumentLifecycle.Raise();
        }

        /// <summary>
        /// ERP lifecycle hardening (Phase 1): soft-close (void) instead of a
        /// physical delete. Keeps the row (DN number + audit trail survive);
        /// Status moves to the void target (Cancelled). Items/attachments are
        /// left intact — the note still exists, it is simply cancelled.
        /// </summary>
        public async Task VoidAsync(int id, string reason)
        {
            OperationGuard.Demand(
                AppPermissions.DeliveryNoteEdit, "void a delivery note");

            using var ctx = new DeliveryNotesDbContext();
            var entity = await ctx.DeliveryNotes
                .FirstOrDefaultAsync(x => x.Id == id).ConfigureAwait(false);

            if (entity == null) return;
            if (entity.IsVoid) return;   // idempotent

            string? proformaId = entity.ProformaId;
            string jobOrderId = entity.JobOrderId;
            string deliveryNoteId = entity.DeliveryNoteId;
            bool isMaster = entity.IsMasterDn;

            entity.IsVoid = true;
            entity.VoidedAtUtc = DateTime.UtcNow;
            entity.VoidedBy = AuditService.CurrentUsername;
            entity.VoidReason = string.IsNullOrWhiteSpace(reason) ? "(no reason given)" : reason.Trim();
            entity.Status = Services.Workflow.DocumentLifecyclePolicy.VoidTargetStatus(
                Services.Workflow.StatusTransitions.Kind.DeliveryNote);
            entity.UpdatedAt = DateTime.Now;

            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());

            if (!isMaster)
            {
                try
                {
                    await UpdateMasterDnAsync(jobOrderId, deliveryNoteId)
                        .ConfigureAwait(false);
                }
                catch (Exception masterEx)
                {
                    AppLogService.Warning(
                        "[DN] Master remaining refresh after void skipped: " +
                        masterEx.Message);
                }

                await ProGlassAutomation.Services.ProformaInvoice
                    .ProformaLifecycleSync.AfterDownstreamChangeAsync(proformaId);
            }

            DocumentLifecycle.Raise();
        }

        // ── Existence checks ─────────────────────────────────────────────

        public async Task<bool> ExistsByDeliveryNoteIdAsync(string deliveryNoteId)
        {
            using var ctx = new DeliveryNotesDbContext();
            return await ctx.DeliveryNotes
                .AnyAsync(x => x.DeliveryNoteId == deliveryNoteId).ConfigureAwait(false);
        }

        public async Task<bool> HasAnyDeliveryAsync(string jobOrderId)
        {
            using var ctx = new DeliveryNotesDbContext();
            return await ctx.DeliveryNotes
                .AnyAsync(x => x.JobOrderId == jobOrderId && !x.IsMasterDn).ConfigureAwait(false);
        }

        // ── Attachments ──────────────────────────────────────────────────

        public async Task<List<DeliveryNoteAttachment>> GetAttachmentsAsync(
            string deliveryNoteId)
        {
            using var ctx = new DeliveryNotesDbContext();
            return await ctx.DeliveryNoteAttachments
                .Where(a => a.DeliveryNoteId == deliveryNoteId)
                .OrderByDescending(a => a.UploadedAt)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<DeliveryNoteAttachment> AddAttachmentAsync(
            DeliveryNoteAttachment entity)
        {
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.DeliveryNoteEdit, "add a delivery note attachment");

            using var ctx = new DeliveryNotesDbContext();
            entity.UploadedAt = DateTime.Now;
            ctx.DeliveryNoteAttachments.Add(entity);
            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
            return entity;
        }

        public async Task DeleteAttachmentAsync(int attachmentId)
        {
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.DeliveryNoteEdit, "delete a delivery note attachment");

            using var ctx = new DeliveryNotesDbContext();
            var entity = await ctx.DeliveryNoteAttachments
                .FirstOrDefaultAsync(a => a.Id == attachmentId).ConfigureAwait(false);
            if (entity == null) return;

            ctx.DeliveryNoteAttachments.Remove(entity);
            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
        }

        public async Task<int> GetAttachmentCountAsync(string deliveryNoteId)
        {
            using var ctx = new DeliveryNotesDbContext();
            return await ctx.DeliveryNoteAttachments
                .CountAsync(a => a.DeliveryNoteId == deliveryNoteId).ConfigureAwait(false);
        }

        public async Task<Dictionary<string, int>> GetAttachmentCountsAsync(
            IEnumerable<string> deliveryNoteIds)
        {
            var idList = deliveryNoteIds.ToList();
            if (idList.Count == 0) return new Dictionary<string, int>();

            using var ctx = new DeliveryNotesDbContext();

            var raw = await ctx.DeliveryNoteAttachments
                .Where(a => idList.Contains(a.DeliveryNoteId))
                .GroupBy(a => a.DeliveryNoteId)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToListAsync().ConfigureAwait(false);

            var dict = raw.ToDictionary(x => x.Id, x => x.Count);

            foreach (var id in idList)
                if (!dict.ContainsKey(id))
                    dict[id] = 0;

            return dict;
        }

        // ── ANALYTICS PROJECTION ────────────────────────────────────────

        public async Task<List<DeliveryNoteAnalyticsRow>> GetAnalyticsRowsAsync()
        {
            using var ctx = new DeliveryNotesDbContext();
            return await ctx.DeliveryNotes
                .Where(d => !d.IsMasterDn)
                .AsNoTracking()
                .Select(d => new DeliveryNoteAnalyticsRow
                {
                    DeliveryNoteId = d.DeliveryNoteId,
                    Status = d.Status,
                    DeliveryDate = d.DeliveryDate,
                    HasTaxInvoice = d.HasTaxInvoice
                })
                .ToListAsync().ConfigureAwait(false);
        }

        // ── Private helpers ──────────────────────────────────────────────
        // (ToListItem removed — every list method now uses the EF-translatable
        // ToListItemProjection above so the snapshot blob never crosses the
        // wire. Keeping an unused in-memory mapper invites drift between the
        // SQL projection and any future caller.)

        // ═══════════════════════════════════════════════════════════════
        //  PHASE 2 — Result Pattern with CorrelationId
        // ═══════════════════════════════════════════════════════════════

        public async Task<Result<SavedDeliveryNote>> InsertWithResultAsync(SavedDeliveryNote entity, string? correlationId = null)
        {
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.DeliveryNoteEdit, "create a delivery note (Result variant)");

            var corrId = correlationId ?? CorrelationContext.NewIdFor("DeliveryNote", entity?.DeliveryNoteId);
            using var scope = CorrelationContext.BeginDocumentScope("DeliveryNote", entity?.DeliveryNoteId ?? "", corrId);

            try
            {
                var result = await InsertAsync(entity!);
                AppLogService.Info(
                    $"DN inserted: {entity!.DeliveryNoteId} for JO {entity.JobOrderId}",
                    new LogContext
                    {
                        Module = "Delivery",
                        Operation = "Insert",
                        DocumentId = entity.DeliveryNoteId,
                        DocumentType = "DeliveryNote",
                        CorrelationId = corrId
                    });
                return Result<SavedDeliveryNote>.Ok(result, corrId);
            }
            catch (Exception ex)
            {
                var errorId = AppLogService.Error(
                    $"Failed to insert DN {entity?.DeliveryNoteId}: {ex.Message}",
                    ex,
                    new LogContext
                    {
                        Module = "Delivery",
                        Operation = "Insert",
                        DocumentId = entity?.DeliveryNoteId ?? "",
                        DocumentType = "DeliveryNote",
                        CorrelationId = corrId
                    });
                return Result<SavedDeliveryNote>.Fail($"Could not save delivery note {entity?.DeliveryNoteId}: {ex.Message}", ex, corrId, errorId);
            }
        }

        public async Task<Result> UpdateWithResultAsync(SavedDeliveryNote entity, string? correlationId = null)
        {
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.DeliveryNoteEdit, "update a delivery note (Result variant)");

            var corrId = correlationId ?? CorrelationContext.NewIdFor("DeliveryNote", entity?.DeliveryNoteId);
            using var scope = CorrelationContext.BeginDocumentScope("DeliveryNote", entity?.DeliveryNoteId ?? "", corrId);

            try
            {
                await UpdateAsync(entity!);
                AppLogService.Info(
                    $"DN updated: {entity!.DeliveryNoteId}",
                    new LogContext
                    {
                        Module = "Delivery",
                        Operation = "Update",
                        DocumentId = entity.DeliveryNoteId,
                        DocumentType = "DeliveryNote",
                        CorrelationId = corrId
                    });
                return Result.Ok(corrId);
            }
            catch (Exception ex)
            {
                var errorId = AppLogService.Error(
                    $"Failed to update DN {entity?.DeliveryNoteId}: {ex.Message}",
                    ex,
                    new LogContext
                    {
                        Module = "Delivery",
                        Operation = "Update",
                        DocumentId = entity?.DeliveryNoteId ?? "",
                        DocumentType = "DeliveryNote",
                        CorrelationId = corrId
                    });
                return Result.Fail($"Could not update delivery note {entity?.DeliveryNoteId}: {ex.Message}", ex, corrId, errorId);
            }
        }
    }
}
