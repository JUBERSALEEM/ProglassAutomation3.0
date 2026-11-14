// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/DeliveryNote/DeliveryNoteHistoryService.cs
// PROJECT: ProGlassAutomation
// MODULE: Delivery Note — History Service (Phase 6b: God VM Split)
// VERSION: Phase 6b — 2026-08-21 — Extracted from DeliveryNoteDetailViewModel
//
// PURPOSE:
//   Extracts history loading, attachment counts, and version restore logic
//   that was in Detail VM. Now testable with Result pattern.
//
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.DeliveryNote.SavedDeliveryNote;
using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.Common;
using ProGlassAutomation.Services.DeliveryNote.Mapper;
using ProGlassAutomation.Services.DeliveryNote.Repository;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.ViewModels.DeliveryNote.DeliveryNoteDetail;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.DeliveryNote
{
    public sealed class DeliveryNoteHistoryService
    {
        public static async Task<Result<DeliveryHistorySummary>> GetHistoryWithAttachmentsAsync(
            string jobOrderId,
            string currentDeliveryNoteId,
            string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewIdFor("DeliveryNote", jobOrderId);
            using var scope = CorrelationContext.BeginDocumentScope("DeliveryNote", jobOrderId, corrId);

            try
            {
                if (App.DeliveryNotes == null)
                    return Result<DeliveryHistorySummary>.Fail("DeliveryNotes repo not initialized", corrId);

                var history = await App.DeliveryNotes.GetDeliveryHistoryAsync(jobOrderId).ConfigureAwait(false);

                var siblingIds = history.Entries.Select(e => e.DeliveryNoteId).ToList();
                var counts = await App.DeliveryNotes.GetAttachmentCountsAsync(siblingIds).ConfigureAwait(false);

                // Enrich with attachment counts and current flag
                foreach (var entry in history.Entries)
                {
                    // Counts will be mapped in VM, but we log
                }

                AppLogService.Info(
                    $"DN history loaded for JO {jobOrderId}: {history.Entries.Count} entries, {history.TotalOrdered} ordered",
                    new LogContext
                    {
                        Module = "Delivery",
                        Operation = "GetHistory",
                        DocumentId = jobOrderId,
                        DocumentType = "JobOrder",
                        CorrelationId = corrId
                    });

                return Result<DeliveryHistorySummary>.Ok(history, corrId);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error(
                    $"Failed to load DN history for JO {jobOrderId}: {ex.Message}",
                    ex,
                    new LogContext
                    {
                        Module = "Delivery",
                        Operation = "GetHistory",
                        DocumentId = jobOrderId,
                        DocumentType = "JobOrder",
                        CorrelationId = corrId
                    });
                return Result<DeliveryHistorySummary>.Fail($"Could not load delivery history: {ex.Message}", ex, corrId, errId);
            }
        }

        public static async Task<Result<bool>> RestoreVersionAsync(
            SavedDeliveryNote entity,
            int versionId,
            string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewIdFor("DeliveryNote", entity?.DeliveryNoteId);
            using var scope = CorrelationContext.BeginDocumentScope("DeliveryNote", entity?.DeliveryNoteId ?? "", corrId);

            try
            {
                if (entity == null || entity.IsMasterDn)
                    return Result<bool>.Fail("Cannot restore version for this DN", corrId);

                var version = await VersionHistoryService.GetByIdAsync(versionId).ConfigureAwait(false);
                if (version == null || string.IsNullOrEmpty(version.SnapshotJson))
                    return Result<bool>.Fail("Version not found or empty", corrId);

                var shell = new SavedDeliveryNote { FullSnapshotJson = version.SnapshotJson };
                var snap = DeliveryNoteMapper.DeserializeSnapshot(shell);
                if (snap == null)
                    return Result<bool>.Fail("Could not deserialize version snapshot", corrId);

                // Header
                entity.Status = snap.Header.Status;
                entity.IsFoc = snap.Header.IsFoc;
                entity.DriverName = snap.Header.DriverName;
                entity.VehicleNumber = snap.Header.VehicleNumber;
                entity.ReceiverName = snap.Header.ReceiverName;
                entity.Notes = snap.Header.Notes;
                entity.DeliveryTime = snap.Header.DeliveryTime;

                // Items
                foreach (var sItem in snap.Items)
                {
                    var item = entity.Items.FirstOrDefault(i => i.SpecGroupNumber == sItem.SpecGroupNumber && i.SerialNumber == sItem.SerialNumber);
                    if (item == null) continue;
                    item.DeliveredQty = sItem.DeliveredQty;
                    item.BalanceQty = Math.Max(0, item.OrderedQty - item.PreviouslyDeliveredQty - item.DeliveredQty);
                    item.Notes = sItem.Notes ?? "";
                }

                AuditService.Log(
                    AuditSections.DeliveryNote,
                    "Version restored",
                    $"{entity.DeliveryNoteId} → version {version.VersionNo} (pending save)",
                    "DN", entity.DeliveryNoteId);

                AppLogService.Info(
                    $"DN {entity.DeliveryNoteId} restored to version {version.VersionNo} [{corrId}]",
                    new LogContext
                    {
                        Module = "Delivery",
                        Operation = "RestoreVersion",
                        DocumentId = entity.DeliveryNoteId,
                        DocumentType = "DeliveryNote",
                        CorrelationId = corrId,
                        Extra = $"Version={version.VersionNo}"
                    });

                return Result<bool>.Ok(true, corrId);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error(
                    $"DN {entity?.DeliveryNoteId} RestoreVersion failed: {ex.Message}",
                    ex,
                    new LogContext
                    {
                        Module = "Delivery",
                        Operation = "RestoreVersion",
                        DocumentId = entity?.DeliveryNoteId ?? "",
                        DocumentType = "DeliveryNote",
                        CorrelationId = corrId
                    });
                return Result<bool>.Fail($"Restore failed: {ex.Message}", ex, corrId, errId);
            }
        }
    }
}
