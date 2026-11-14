// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/ProformaInvoice/InquiryLinkSync.cs
// PROJECT: ProGlassAutomation
// MODULE: Proforma Invoice ↔ Inquiry link keeper (senior audit — Aug 2026)
//
// PURPOSE
// ───────
// Called by ProformaInvoiceRepository on every Insert / Update / Delete
// that touches a PI carrying an InquiryId, this helper keeps two things
// in sync:
//
//   1. The denormalised Inquiries.ProformaInvoiceId + ProformaNumber
//      columns — used by the Inquiry list projection and by the
//      "Convert to Proforma Invoice" duplicate-prevention guard.
//
//   2. The IInquiryLifecycleService cache — so a freshly-created /
//      edited / deleted PI shows up on the Inquiry list within one
//      refresh, not after the 60-second TTL expires.
//
// BEFORE THIS EXISTED
// ───────────────────
// The two columns were declared, read by the projection and used by the
// Convert-to-PI guard — but NEVER written by any code path. The guard
// was dead code and a single Confirmed inquiry could produce unlimited
// PIs. See docs/PROFORMA_INQUIRY_INTEGRATION_2026-08.md for the full
// bug analysis.
//
// FIRE-AND-FORGET SAFETY
// ──────────────────────
// The link write is bookkeeping — it must NEVER cause the PI
// operation to fail. Every method here swallows exceptions and logs
// a warning; the calling PI repository always returns success as
// long as its own SaveChanges succeeded.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Inquiry;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.Inquiry;
using ProGlassAutomation.Services.Logging;
using System;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.ProformaInvoice
{
    /// <summary>
    /// Static helper that pushes PI-side changes into the Inquiry
    /// module's denormalised link fields and lifecycle cache.
    ///
    /// SENIOR AUDIT (Aug 2026): Now also advances the inquiry status
    /// based on the PI lifecycle, so the operator doesn't have to
    /// manually keep the two in sync:
    ///   • PI created (first insert) → Inquiry → Sent
    ///   • PI approved / confirmed    → Inquiry → Confirmed
    ///   • PI rejected / cancelled   → Inquiry → Cancelled
    ///   • PI deleted                → Inquiry reverts to Prepared
    ///     (so Convert-to-PI re-enables)
    /// </summary>
    public static class InquiryLinkSync
    {
        /// <summary>
        /// Called after a PI is INSERTED. Writes the new PI id / number
        /// onto the linked inquiry (if any) so the Inquiry list stops
        /// offering "Convert to PI" for that row. Also advances the
        /// inquiry status to "Sent" (the PI IS the sent quotation).
        /// </summary>
        public static async Task AfterInsertAsync(SavedProformaInvoice invoice)
        {
            await WriteLinkAsync(invoice, clearInsteadOfSet: false);
            await AdvanceInquiryStatusAsync(invoice, "Sent");
        }

        /// <summary>
        /// Called after a PI is UPDATED. Idempotent — writes the same
        /// link if it's already there, corrects it if InquiryId
        /// changed. Also advances the inquiry status when the PI
        /// reaches Approved / Confirmed / Rejected / Cancelled.
        /// </summary>
        public static async Task AfterUpdateAsync(SavedProformaInvoice invoice)
        {
            await WriteLinkAsync(invoice, clearInsteadOfSet: false);
            string? targetStatus = MapPiStatusToInquiryStatus(invoice.Status);
            if (targetStatus != null)
                await AdvanceInquiryStatusAsync(invoice, targetStatus);
        }

        /// <summary>
        /// Called after a PI is DELETED. Clears the link on the
        /// previously-linked inquiry so the Convert-to-PI button
        /// re-enables and the list stops showing a stale PI number.
        /// Reverts the inquiry to "Prepared" so the operator can
        /// create a new PI from it.
        /// </summary>
        public static async Task AfterDeleteAsync(SavedProformaInvoice? entity)
        {
            await WriteLinkAsync(entity, clearInsteadOfSet: true);
            if (entity != null && entity.InquiryId.HasValue && entity.InquiryId.Value > 0)
                await AdvanceInquiryStatusAsync(entity, InquiryStatuses.Prepared);
        }

        /// <summary>
        /// Maps a PI status string to the corresponding inquiry status.
        /// Returns null when the PI status doesn't warrant an inquiry
        /// status change (e.g. Draft — the inquiry stays as-is).
        /// </summary>
        private static string? MapPiStatusToInquiryStatus(string? piStatus)
        {
            if (string.IsNullOrWhiteSpace(piStatus)) return null;
            return piStatus.Trim().ToLowerInvariant() switch
            {
                "approved" or "confirmed" => InquiryStatuses.Confirmed,
                "rejected" or "cancelled" or "canceled" => InquiryStatuses.Cancelled,
                // Draft, Expired, etc. — don't change the inquiry status.
                _ => null
            };
        }

        /// <summary>
        /// Advances the linked inquiry's status to the target value,
        /// but only if the transition is valid per the workflow rules.
        /// Fire-and-forget safety: all exceptions are swallowed and
        /// logged — the PI operation must never fail because the
        /// inquiry status write threw.
        /// </summary>
        private static async Task AdvanceInquiryStatusAsync(
            SavedProformaInvoice invoice, string targetStatus)
        {
            if (invoice == null) return;
            if (!invoice.InquiryId.HasValue || invoice.InquiryId.Value <= 0) return;
            if (App.Inquiries == null) return;

            try
            {
                var detail = await App.Inquiries.GetDetailAsync(invoice.InquiryId.Value)
                    .ConfigureAwait(false);
                if (detail == null) return;

                string currentStatus = InquiryStatuses.Normalize(detail.Status);
                string target = InquiryStatuses.Normalize(targetStatus);

                if (string.Equals(currentStatus, target, StringComparison.OrdinalIgnoreCase))
                    return; // Already at the target status — nothing to do.

                // Only advance if the workflow allows this transition.
                // If it doesn't (e.g. trying to go from Cancelled back
                // to Sent), we skip silently — the PI save still
                // succeeds, the inquiry just keeps its current status.
                if (!InquiryWorkflowService.CanTransition(currentStatus, target))
                    return;

                await App.Inquiries.ChangeStatusAsync(
                    invoice.InquiryId.Value,
                    target,
                    AuditService.CurrentUsername)
                    .ConfigureAwait(false);

                AuditService.Log(
                    AuditSections.Proforma,
                    "Inquiry status auto-advanced",
                    $"Inquiry {detail.InquiryNumber}: {currentStatus} → {target} (driven by PI {invoice.ProformaId})",
                    "INQ", detail.InquiryNumber);
            }
            catch (Exception ex)
            {
                AppLogService.Warning(
                    "InquiryLinkSync.AdvanceInquiryStatus failed for PI '" +
                    (invoice.ProformaId ?? "?") + "' → inquiry " + invoice.InquiryId +
                    " (target: " + targetStatus + "): " + ex.Message,
                    new LogContext
                    {
                        Module = "Proforma",
                        Class = nameof(InquiryLinkSync),
                        Operation = "AdvanceStatus"
                    });
            }
        }

        private static async Task WriteLinkAsync(
            SavedProformaInvoice? invoice, bool clearInsteadOfSet)
        {
            if (invoice == null) return;
            if (!invoice.InquiryId.HasValue || invoice.InquiryId.Value <= 0) return;
            if (App.Inquiries == null) return;

            try
            {
                if (clearInsteadOfSet)
                {
                    await App.Inquiries.SetProformaLinkAsync(
                        invoice.InquiryId.Value,
                        proformaInvoiceId: null,
                        proformaNumber: string.Empty)
                        .ConfigureAwait(false);
                }
                else
                {
                    await App.Inquiries.SetProformaLinkAsync(
                        invoice.InquiryId.Value,
                        proformaInvoiceId: invoice.Id,
                        proformaNumber: invoice.ProformaId ?? string.Empty)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // Bookkeeping only — never let the PI save fail because
                // the back-link write threw. The PI is already committed
                // at this point; the worst case is a 60-second cache
                // staleness the next lifecycle read resolves.
                AppLogService.Warning(
                    "InquiryLinkSync." +
                    (clearInsteadOfSet ? "AfterDelete" : "AfterUpsert") +
                    " failed for PI '" + (invoice.ProformaId ?? "?") +
                    "' → inquiry " + invoice.InquiryId + ": " + ex.Message,
                    new LogContext
                    {
                        Module = "Proforma",
                        Class = nameof(InquiryLinkSync),
                        Operation = clearInsteadOfSet ? "AfterDelete" : "AfterUpsert"
                    });
            }
        }
    }
}
