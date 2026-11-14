// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Workflow/DocumentOperationCoordinator.cs
// PROJECT: ProGlassAutomation
// MODULE: Core — Multi-DB Operation Coordinator (Phase 2)
// VERSION: Phase 2 — 2026-08-21
//
// PURPOSE (Audit D-4 + X-6):
//   Business operations span multiple SQLite files:
//     PI save → taxinvoices.db + deliverynotes.db
//     TI save → taxinvoices.db + deliverynotes.db (mark DN invoiced)
//     DN delete → deliverynotes.db + proformainvoices.db (maybe cancel PI)
//
//   SQLite cannot do cross-file transactions. If app crashes between
//   two writes, books disagree: e.g. DN flagged invoiced with no TI.
//
//   This coordinator implements:
//     1. CorrelationId — one ID for whole operation, flows to logs
//     2. Result<T> — explicit error, no hidden nulls/exceptions
//     3. Compensation — if step 2 fails, undo step 1 or schedule reconciliation
//     4. Idempotency — MarkMultipleAsInvoicedAsync safe to re-run
//     5. Audit — every step logged with ErrorId + CorrelationId
//
// PATTERN: Operation Coordinator + Idempotent Ops + Reconciliation
//   - Coordinator: this file
//   - Idempotent: MarkMultipleAsInvoicedAsync, ClearInvoicedByTaxInvoiceAsync
//   - Reconciliation: TaxInvoiceDnReconciler.ReconcileAsync at startup
//
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.TaxInvoice.SavedTaxInvoice;
using ProGlassAutomation.Services.Common;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Services.Observability;
using ProGlassAutomation.Services.TaxInvoice.Repository;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Workflow
{
    /// <summary>
    /// Coordinates multi-DB document operations with Result pattern and correlation tracing.
    /// </summary>
    public static class DocumentOperationCoordinator
    {
        /// <summary>
        /// Saves a Tax Invoice and marks its Delivery Notes as invoiced — the classic D-4 case.
        /// Steps:
        ///   1. Insert TI into taxinvoices.db → Result with CorrelationId
        ///   2. Mark DNs as invoiced in deliverynotes.db → Result
        ///   3. If step 2 fails, log critical + schedule reconciliation (compensation)
        ///   4. Return combined Result with correlation for UI
        /// </summary>
        public static async Task<Result<SavedTaxInvoice>> SaveTaxInvoiceWithDnLinkAsync(
            SavedTaxInvoice invoice,
            string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewIdFor("TaxInvoice", invoice?.InvoiceNumber);
            using var scope = CorrelationContext.BeginDocumentScope("TaxInvoice", invoice?.InvoiceNumber ?? "", corrId);
            using var activity = DocumentPipelineTracer.StartDocumentOperation(
                "SaveWithDnLink", "TaxInvoice", invoice?.InvoiceNumber, corrId);

            AppLogService.Info(
                $"Starting TI save with DN link: TI={invoice?.InvoiceNumber}, DNs={invoice?.DeliveryNoteReference}",
                new LogContext
                {
                    Module = "TaxInvoice",
                    Operation = "SaveWithDnLink",
                    DocumentId = invoice?.InvoiceNumber ?? "",
                    DocumentType = "TaxInvoice",
                    CorrelationId = corrId,
                    Extra = $"DNs={invoice?.DeliveryNoteReference}"
                });

            // Step 1: Save TI
            var repo = App.TaxInvoices;
            if (repo == null)
            {
                DocumentPipelineTracer.MarkFailure(activity, "TaxInvoice repository not initialized");
                return Result<SavedTaxInvoice>.Fail("TaxInvoice repository not initialized", corrId);
            }

            Result<SavedTaxInvoice> tiResult;
            if (repo is TaxInvoiceRepository concreteRepo)
                tiResult = await concreteRepo.InsertWithResultAsync(invoice!, corrId);
            else
            {
                try
                {
                    var saved = await repo.InsertAsync(invoice!);
                    tiResult = Result<SavedTaxInvoice>.Ok(saved, corrId);
                }
                catch (Exception ex)
                {
                    var errId = AppLogService.Error(
                        $"TI insert failed: {ex.Message}", ex,
                        new LogContext
                        {
                            Module = "TaxInvoice",
                            Operation = "Insert",
                            DocumentId = invoice?.InvoiceNumber ?? "",
                            DocumentType = "TaxInvoice",
                            CorrelationId = corrId
                        });
                    tiResult = Result<SavedTaxInvoice>.Fail($"Failed to save tax invoice: {ex.Message}", ex, corrId, errId);
                }
            }

            if (tiResult.IsFailure)
            {
                AppLogService.Warning(
                    $"TI save aborted at step 1 (TI insert): {tiResult.Error}",
                    new LogContext
                    {
                        Module = "TaxInvoice",
                        Operation = "SaveWithDnLink",
                        DocumentId = invoice?.InvoiceNumber ?? "",
                        DocumentType = "TaxInvoice",
                        CorrelationId = corrId,
                        Extra = $"ErrorId={tiResult.ErrorId}"
                    });
                DocumentPipelineTracer.MarkFailure(activity, tiResult.Error);
                return tiResult;
            }

            // Step 2: Mark DNs as invoiced
            var dnIds = TaxInvoiceDnReconciler.ParseDnRefs(invoice!.DeliveryNoteReference);
            if (dnIds.Count == 0)
            {
                AppLogService.Info(
                    $"TI {invoice.InvoiceNumber} has no DN refs — no DN marking needed",
                    new LogContext
                    {
                        Module = "TaxInvoice",
                        Operation = "SaveWithDnLink",
                        DocumentId = invoice.InvoiceNumber,
                        DocumentType = "TaxInvoice",
                        CorrelationId = corrId
                    });
                DocumentPipelineTracer.MarkSuccess(activity);
                return tiResult;
            }

            try
            {
                if (App.DeliveryNotes == null)
                    throw new InvalidOperationException("DeliveryNotes repository not initialized");

                await App.DeliveryNotes.MarkMultipleAsInvoicedAsync(dnIds, invoice.InvoiceNumber);

                // Verify
                int verified = 0;
                foreach (var dnId in dnIds)
                {
                    try
                    {
                        var dn = await App.DeliveryNotes.GetByDeliveryNoteIdAsync(dnId);
                        if (dn != null && dn.HasTaxInvoice &&
                            string.Equals(dn.TaxInvoiceId, invoice.InvoiceNumber, StringComparison.OrdinalIgnoreCase))
                            verified++;
                    }
                    catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Debug("[DocumentOperationCoordinator] DN verification probe failed: " + ex.Message); }
                }

                AppLogService.Info(
                    $"TI {invoice.InvoiceNumber} DN link: {verified}/{dnIds.Count} DNs marked invoiced",
                    new LogContext
                    {
                        Module = "TaxInvoice",
                        Operation = "SaveWithDnLink",
                        DocumentId = invoice.InvoiceNumber,
                        DocumentType = "TaxInvoice",
                        CorrelationId = corrId,
                        Extra = $"Verified={verified}/{dnIds.Count}, DNs={string.Join(",", dnIds)}"
                    });

                if (verified < dnIds.Count)
                {
                    AppLogService.Warning(
                        $"TI {invoice.InvoiceNumber} DN link incomplete: {verified}/{dnIds.Count} — reconciliation will heal on next startup",
                        new LogContext
                        {
                            Module = "TaxInvoice",
                            Operation = "SaveWithDnLink",
                            DocumentId = invoice.InvoiceNumber,
                            DocumentType = "TaxInvoice",
                            CorrelationId = corrId
                        });
                }

                DocumentPipelineTracer.MarkSuccess(activity);
                return tiResult;
            }
            catch (Exception ex)
            {
                // Step 2 failed — TI exists but DNs not marked. This is the D-4 inconsistency.
                // We do NOT delete TI (would lose user work). Instead log critical and rely on
                // TaxInvoiceDnReconciler at next startup to heal.
                var errorId = AppLogService.Critical(
                    $"CRITICAL D-4: TI {invoice.InvoiceNumber} saved but DN marking failed — {verifiedMsg(dnIds)} will be healed by reconciler. Error: {ex.Message}",
                    ex,
                    new LogContext
                    {
                        Module = "TaxInvoice",
                        Operation = "SaveWithDnLink",
                        DocumentId = invoice.InvoiceNumber,
                        DocumentType = "TaxInvoice",
                        CorrelationId = corrId,
                        Extra = $"DNs={string.Join(",", dnIds)}, TI={invoice.InvoiceNumber}"
                    });

                // Return success for TI but with warning in Extra — UI can show "Saved, but DN link pending"
                // For now return Ok with warning logged; caller can check logs via correlation
                DocumentPipelineTracer.MarkFailure(activity, "DN marking failed — reconciliation will heal on next startup");
                return Result<SavedTaxInvoice>.Ok(tiResult.Value!, corrId, errorId);
            }

            string verifiedMsg(IReadOnlyList<string> ids) => $"{ids.Count} DN(s)";
        }

        /// <summary>
        /// Deletes a Tax Invoice and clears its DN invoiced flags — with Result and correlation.
        /// </summary>
        public static async Task<Result> DeleteTaxInvoiceWithDnUnlinkAsync(int id, string invoiceNumber, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewIdFor("TaxInvoice", invoiceNumber);
            using var scope = CorrelationContext.BeginDocumentScope("TaxInvoice", invoiceNumber, corrId);
            using var activity = DocumentPipelineTracer.StartDocumentOperation(
                "DeleteWithDnUnlink", "TaxInvoice", invoiceNumber, corrId);

            AppLogService.Info(
                $"Starting TI delete with DN unlink: TI={invoiceNumber} Id={id}",
                new LogContext
                {
                    Module = "TaxInvoice",
                    Operation = "DeleteWithDnUnlink",
                    DocumentId = invoiceNumber,
                    DocumentType = "TaxInvoice",
                    CorrelationId = corrId
                });

            var repo = App.TaxInvoices;
            if (repo == null)
            {
                DocumentPipelineTracer.MarkFailure(activity, "TaxInvoice repository not initialized");
                return Result.Fail("TaxInvoice repository not initialized", corrId);
            }

            try
            {
                if (repo is TaxInvoiceRepository concrete)
                {
                    var delResult = await concrete.DeleteWithResultAsync(id, invoiceNumber, corrId);
                    if (delResult.IsFailure)
                    {
                        DocumentPipelineTracer.MarkFailure(activity, delResult.Error);
                        return delResult;
                    }
                }
                else
                {
                    await repo.DeleteAsync(id);
                }

                // Clear DN flags — best effort, but log
                try
                {
                    if (App.DeliveryNotes != null)
                        await App.DeliveryNotes.ClearInvoicedByTaxInvoiceAsync(invoiceNumber);
                }
                catch (Exception ex)
                {
                    AppLogService.Warning(
                        $"TI {invoiceNumber} deleted but DN unlink failed: {ex.Message} — will be healed by reconciler",
                        new LogContext
                        {
                            Module = "TaxInvoice",
                            Operation = "DeleteWithDnUnlink",
                            DocumentId = invoiceNumber,
                            DocumentType = "TaxInvoice",
                            CorrelationId = corrId
                        });
                }

                DocumentPipelineTracer.MarkSuccess(activity);
                return Result.Ok(corrId);
            }
            catch (Exception ex)
            {
                var errorId = AppLogService.Error(
                    $"Failed to delete TI {invoiceNumber}: {ex.Message}",
                    ex,
                    new LogContext
                    {
                        Module = "TaxInvoice",
                        Operation = "DeleteWithDnUnlink",
                        DocumentId = invoiceNumber,
                        DocumentType = "TaxInvoice",
                        CorrelationId = corrId
                    });
                DocumentPipelineTracer.MarkFailure(activity, ex.Message);
                return Result.Fail($"Could not delete tax invoice {invoiceNumber}: {ex.Message}", ex, corrId, errorId);
            }
        }

        /// <summary>
        /// Saves a Delivery Note with Result pattern — example of Phase 2 adoption.
        /// </summary>
        public static async Task<Result<Models.DeliveryNote.SavedDeliveryNote.SavedDeliveryNote>> SaveDeliveryNoteAsync(
            Models.DeliveryNote.SavedDeliveryNote.SavedDeliveryNote dn,
            string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewIdFor("DeliveryNote", dn?.DeliveryNoteId);
            using var scope = CorrelationContext.BeginDocumentScope("DeliveryNote", dn?.DeliveryNoteId ?? "", corrId);
            using var activity = DocumentPipelineTracer.StartDocumentOperation(
                "Save", "DeliveryNote", dn?.DeliveryNoteId, corrId);

            var repo = App.DeliveryNotes;
            if (repo == null)
            {
                DocumentPipelineTracer.MarkFailure(activity, "DeliveryNotes repo not initialized");
                return Result<Models.DeliveryNote.SavedDeliveryNote.SavedDeliveryNote>.Fail("DeliveryNotes repo not initialized", corrId);
            }

            if (repo is Services.DeliveryNote.Repository.DeliveryNoteRepository concrete)
            {
                var result = await concrete.InsertWithResultAsync(dn!, corrId);
                if (result.IsSuccess) DocumentPipelineTracer.MarkSuccess(activity);
                else DocumentPipelineTracer.MarkFailure(activity, result.Error);
                return result;
            }

            try
            {
                var saved = await repo.InsertAsync(dn!);
                DocumentPipelineTracer.MarkSuccess(activity);
                return Result<Models.DeliveryNote.SavedDeliveryNote.SavedDeliveryNote>.Ok(saved, corrId);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error(
                    $"DN save failed: {ex.Message}", ex,
                    new LogContext
                    {
                        Module = "Delivery",
                        Operation = "Save",
                        DocumentId = dn?.DeliveryNoteId ?? "",
                        DocumentType = "DeliveryNote",
                        CorrelationId = corrId
                    });
                DocumentPipelineTracer.MarkFailure(activity, ex.Message);
                return Result<Models.DeliveryNote.SavedDeliveryNote.SavedDeliveryNote>.Fail(ex.Message, ex, corrId, errId);
            }
        }
    }
}
