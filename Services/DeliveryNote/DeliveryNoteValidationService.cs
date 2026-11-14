// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/DeliveryNote/DeliveryNoteValidationService.cs
// PROJECT: ProGlassAutomation
// MODULE: Delivery Note — Validation Service (Phase 6: God VM Split)
// VERSION: Phase 6 — 2026-08-21 — Extracted from DeliveryNoteDetailViewModel
//
// PURPOSE:
//   Centralizes validation rules that were scattered across Detail VM:
//     • Status transition validation (uses StatusTransitions)
//     • Qty validation (delivered cannot exceed balance, etc.)
//     • Dispatch guard (must have at least one delivered qty)
//     • Tax invoice conversion guard (status + invoiced flag)
//     • Master DN guards
//
//   Returns Result pattern with CorrelationId for UI.
//
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.DeliveryNote.SavedDeliveryNote;
using ProGlassAutomation.Services.Common;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Services.Workflow;
using ProGlassAutomation.ViewModels.DeliveryNote.DeliveryNoteDetail;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Services.DeliveryNote
{
    public sealed class DeliveryNoteValidationService
    {
        public static Result ValidateStatusTransition(string currentStatus, string newStatus, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewIdFor("DeliveryNote", newStatus);
            var transition = StatusTransitions.Validate(StatusTransitions.Kind.DeliveryNote, currentStatus, newStatus);

            if (!transition.IsValid)
            {
                AppLogService.Warning(
                    $"DN status transition invalid: {currentStatus} → {newStatus}: {transition.Reason}",
                    new LogContext
                    {
                        Module = "Delivery",
                        Operation = "ValidateStatus",
                        CorrelationId = corrId,
                        Extra = $"From={currentStatus}, To={newStatus}"
                    });
                return Result.Fail(transition.Reason ?? $"Cannot change status from {currentStatus} to {newStatus}", corrId);
            }

            return Result.Ok(corrId);
        }

        /// <summary>
        /// Delivered this DN cannot be negative and cannot exceed
        /// Ordered − confirmed previously delivered. Optional
        /// <paramref name="confirmedPreviouslyDelivered"/> is a live
        /// spec|serial map (from GetPreviouslyDeliveredMapAsync) so
        /// dispatch re-checks remaining against the database, not a
        /// stale row snapshot. When omitted, row.PreviouslyDeliveredQty
        /// is used.
        ///
        /// The previous outer guard
        /// <c>DeliveredQty &gt; BalanceQty + DeliveredQty</c> reduced to
        /// <c>BalanceQty &lt; 0</c> and skipped the remaining check for
        /// every normal (non-negative balance) row.
        /// </summary>
        public static Result ValidateQuantities(
            IEnumerable<DnItemRowViewModel> rows,
            string? correlationId = null,
            IReadOnlyDictionary<string, int>? confirmedPreviouslyDelivered = null)
        {
            var corrId = correlationId ?? CorrelationContext.CurrentId ?? CorrelationContext.NewId();

            foreach (var row in rows)
            {
                if (row.DeliveredQty < 0)
                    return Result.Fail($"Delivered qty cannot be negative for {row.GlassRef} (Sr {row.SerialNumber})", corrId);

                int previously = row.PreviouslyDeliveredQty;
                if (confirmedPreviouslyDelivered != null)
                {
                    string key = DeliveryNoteWorkflow.LineKey(
                        row.SpecGroupNumber, row.SerialNumber);
                    previously = confirmedPreviouslyDelivered.TryGetValue(key, out var livePrev)
                        ? livePrev
                        : 0;
                }

                int maxAllowed = Math.Max(0, row.OrderedQty - previously);
                if (row.DeliveredQty > maxAllowed)
                    return Result.Fail(
                        $"Delivered qty {row.DeliveredQty} exceeds remaining {maxAllowed} for {row.GlassRef} Sr {row.SerialNumber}",
                        corrId);
            }

            return Result.Ok(corrId);
        }

        public static Result ValidateDispatch(System.Collections.Generic.IEnumerable<DnItemRowViewModel> rows, bool isMasterDn, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.CurrentId ?? CorrelationContext.NewId();

            if (isMasterDn)
                return Result.Fail("Master DNs cannot be dispatched manually — they are internal trackers", corrId);

            if (!rows.Any(r => r.DeliveredQty > 0))
                return Result.Fail("Cannot dispatch — no items have a delivered quantity. Enter qty for at least one item.", corrId);

            return Result.Ok(corrId);
        }

        public static Result ValidateTaxInvoiceConversion(
            SavedDeliveryNote? entity,
            DnHistoryEntryViewModel? historyEntry,
            string proformaId,
            string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.CurrentId ?? CorrelationContext.NewId();

            if (entity == null)
                return Result.Fail("No delivery note loaded", corrId);

            if (entity.IsMasterDn)
                return Result.Fail("Master DNs cannot be converted to Tax Invoice", corrId);

            if (string.IsNullOrWhiteSpace(proformaId))
                return Result.Fail("This delivery note has no linked Proforma Invoice", corrId);

            if (historyEntry != null && historyEntry.HasTaxInvoice)
                return Result.Fail($"DN '{historyEntry.DeliveryNoteId}' is already invoiced as {historyEntry.TaxInvoiceId}", corrId);

            if (historyEntry == null && entity.HasTaxInvoice)
                return Result.Fail($"This DN is already invoiced as {entity.TaxInvoiceId}", corrId);

            var targetStatus = historyEntry?.Status ?? entity.Status;
            bool validStatus = targetStatus == "Dispatched" || targetStatus == "Delivered" || targetStatus == "Signed" || targetStatus == "Received";

            if (!validStatus)
                return Result.Fail($"Cannot invoice DN with status '{targetStatus}' — must be Dispatched, Delivered, Signed, or Received", corrId);

            var targetQty = historyEntry?.DeliveredQty ?? entity.TotalDeliveredQty;
            if (targetQty <= 0)
                return Result.Fail($"DN has 0 delivered qty — nothing to invoice", corrId);

            return Result.Ok(corrId);
        }

        public static Result ValidateSave(SavedDeliveryNote? entity, System.Collections.Generic.IEnumerable<DnItemRowViewModel> rows, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.CurrentId ?? CorrelationContext.NewId();

            if (entity == null)
                return Result.Fail("No delivery note loaded", corrId);

            if (entity.IsMasterDn)
                return Result.Fail("Master DNs cannot be saved manually", corrId);

            var qtyResult = ValidateQuantities(rows, corrId);
            if (qtyResult.IsFailure) return qtyResult;

            return Result.Ok(corrId);
        }
    }
}
