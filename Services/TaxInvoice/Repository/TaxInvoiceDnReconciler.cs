// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/TaxInvoice/Repository/TaxInvoiceDnReconciler.cs
// PROJECT: ProGlassAutomation
// MODULE: Tax Invoice — cross-database DN invoiced-link reconciliation
// VERSION: Phase 2 — 2026-08-21 — Added Result + CorrelationId
//
// PURPOSE (Phase 2 — cross-database data integrity):
//   A Tax Invoice is written to taxinvoices.db, and the Delivery Notes it
//   covers are marked invoiced in deliverynotes.db. These are two separate
//   SQLite files, so no single transaction can span both. If the app crashes
//   or the second write fails, the books disagree: an invoice exists while
//   its delivery notes still look un-invoiced (so they could be invoiced
//   again).
//
//   This service provides the RECONCILIATION half of the "Operation
//   Coordinator + Idempotent Operations + Reconciliation" pattern:
//     • Idempotent: MarkMultipleAsInvoicedAsync is safe to re-run and only
//       marks the exact DNs referenced by the invoice.
//     • Verification: after repair it re-checks that the DNs now carry the
//       invoice number.
//     • Non-destructive: it never un-marks a DN and never touches a DN the
//       invoice does not reference.
//
//   A full sweep (ReconcileAsync) runs at app startup so any link left
//   broken by a crash or failed write is automatically healed on the next
//   launch. It is a background, fire-and-forget operation.
//
//   Phase 2: Now returns Result with CorrelationId for tracing.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.Common;
using ProGlassAutomation.Services.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.TaxInvoice.Repository
{
    /// <summary>
    /// Stateless reconciler that ensures every Tax Invoice's referenced
    /// Delivery Notes are marked invoiced with that invoice number.
    /// </summary>
    public static class TaxInvoiceDnReconciler
    {
        /// <summary>
        /// Splits a stored DeliveryNoteReference (comma-separated, e.g.
        /// "DN-2026-001-01,DN-2026-001-02") into individual DN ids.
        /// Tolerates "DN-001, DN-002" whitespace.
        /// </summary>
        public static IReadOnlyList<string> ParseDnRefs(string? reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return Array.Empty<string>();

            return reference
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Full sweep: for every saved Tax Invoice, mark any of its referenced
        /// Delivery Notes as invoiced with that invoice number when they are
        /// not already. Idempotent and non-destructive.
        /// Returns the number of DN links repaired (0 when fully consistent).
        /// Never throws — logs failures and returns 0.
        /// Phase 2: Now supports CorrelationId for tracing.
        /// </summary>
        public static async Task<int> ReconcileAsync(string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewIdFor("Reconciler", "StartupSweep");
            using var scope = CorrelationContext.BeginScope(corrId, null, "Reconciler");

            if (App.TaxInvoices == null || App.DeliveryNotes == null)
                return 0;

            try
            {
                var invoices = await App.TaxInvoices.GetAllAsync();
                if (invoices == null || invoices.Count == 0) return 0;

                // Load every DN once and key by DeliveryNoteId so the sweep
                // is O(invoices + DNs), not N+1 per invoice.
                var dns = await App.DeliveryNotes.GetAllAsync();
                var dnMap = new Dictionary<string, bool>(
                    StringComparer.OrdinalIgnoreCase);   // DeliveryNoteId → HasTaxInvoice
                var dnInvoiceMap = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);   // DeliveryNoteId → TaxInvoiceId
                if (dns != null)
                {
                    foreach (var dn in dns)
                    {
                        dnMap[dn.DeliveryNoteId] = dn.HasTaxInvoice;
                        dnInvoiceMap[dn.DeliveryNoteId] = dn.TaxInvoiceId ?? "";
                    }
                }

                int repaired = 0;

                foreach (var inv in invoices)
                {
                    string invNum = inv.InvoiceNumber ?? "";
                    if (string.IsNullOrWhiteSpace(invNum)) continue;

                    var dnIds = ParseDnRefs(inv.DeliveryNoteReference);
                    if (dnIds.Count == 0) continue;

                    // DNs that are NOT yet marked with THIS invoice.
                    var missing = dnIds
                        .Where(id => dnMap.TryGetValue(id, out bool marked)
                                     && !(marked &&
                                          string.Equals(
                                              dnInvoiceMap[id], invNum,
                                              StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    if (missing.Count == 0) continue;

                    // Idempotent repair — only the referenced DNs.
                    await App.DeliveryNotes.MarkMultipleAsInvoicedAsync(missing, invNum);

                    // Verify the repair took effect.
                    var verified = await VerifyMarkedAsync(invNum, missing);
                    repaired += verified;

                    // Warning, not Info (Phase 14). Reaching this line means the
                    // tax-invoice and delivery-note records had already diverged
                    // and something had to heal them — normally a crash or a
                    // failed write. Info reads as routine housekeeping and gets
                    // filtered out of every report; an operator triaging "why are
                    // my invoiced quantities wrong" needs this to survive a log
                    // level filter.
                    AppLogService.Warning(
                        $"Reconciled {verified}/{missing.Count} DN link(s) for " +
                        $"TI '{invNum}' — these links had diverged.",
                        new LogContext
                        {
                            Module = "TaxInvoice",
                            Class = nameof(TaxInvoiceDnReconciler),
                            Operation = "ReconcileAsync",
                            CorrelationId = corrId,
                            DocumentId = invNum,
                            DocumentType = "TaxInvoice",
                            Extra = $"Missing={missing.Count}, Verified={verified}"
                        });

                    try
                    {
                        AuditService.Log(
                            AuditSections.TaxInvoice,
                            "DN invoiced-link repaired",
                            $"{verified} DN(s) for TI {invNum} marked invoiced by " +
                            "reconciliation sweep.",
                            "TI", invNum);
                    }
                    catch { /* audit is best-effort */ }
                }

                if (repaired > 0)
                {
                    AppLogService.Info(
                        $"Reconciliation sweep complete: {repaired} DN link(s) repaired [{corrId}]",
                        new LogContext
                        {
                            Module = "TaxInvoice",
                            Operation = "ReconcileAsync",
                            CorrelationId = corrId,
                            Extra = $"Repaired={repaired}"
                        });
                }

                return repaired;
            }
            catch (Exception ex)
            {
                AppLogService.Error(
                    "Tax-invoice → delivery-note reconciliation sweep failed.",
                    ex,
                    new LogContext
                    {
                        Module = "TaxInvoice",
                        Class = nameof(TaxInvoiceDnReconciler),
                        Operation = "ReconcileAsync",
                        CorrelationId = corrId
                    });
                return 0;
            }
        }

        /// <summary>
        /// Phase 2: Reconcile with Result pattern — for explicit error handling in startup.
        /// </summary>
        public static async Task<Result<int>> ReconcileWithResultAsync(string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewId();
            try
            {
                var count = await ReconcileAsync(corrId);
                return Result<int>.Ok(count, corrId);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error(
                    $"ReconcileWithResult failed: {ex.Message}", ex,
                    new LogContext
                    {
                        Module = "TaxInvoice",
                        Operation = "ReconcileWithResult",
                        CorrelationId = corrId
                    });
                return Result<int>.Fail(ex.Message, ex, corrId, errId);
            }
        }

        /// <summary>
        /// Re-reads the given DNs and returns how many are now marked
        /// invoiced with <paramref name="taxInvoiceId"/>.
        /// </summary>
        private static async Task<int> VerifyMarkedAsync(
            string taxInvoiceId, IReadOnlyList<string> dnIds)
        {
            if (App.DeliveryNotes == null || dnIds.Count == 0) return 0;

            int verified = 0;
            foreach (var id in dnIds)
            {
                try
                {
                    var dn = await App.DeliveryNotes.GetByDeliveryNoteIdAsync(id);
                    if (dn != null
                        && dn.HasTaxInvoice
                        && string.Equals(dn.TaxInvoiceId, taxInvoiceId,
                            StringComparison.OrdinalIgnoreCase))
                        verified++;
                }
                catch (Exception ex)
                {
                    // Phase 2: Log verification failures with correlation
                    AppLogService.Warning(
                        $"VerifyMarked failed for DN {id}: {ex.Message}",
                        new LogContext
                        {
                            Module = "TaxInvoice",
                            Operation = "VerifyMarked",
                            DocumentId = id,
                            DocumentType = "DeliveryNote"
                        });
                }
            }
            return verified;
        }
    }
}
