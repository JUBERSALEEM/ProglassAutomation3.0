// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Workflow/DocumentLifecyclePolicy.cs
// PROJECT: ProGlassAutomation
// MODULE: Core — document delete/void/edit lifecycle policy (ERP hardening)
//
// THE PROBLEM THIS SOLVES
//   Every document delete in the app was a physical row delete, including
//   posted Tax Invoices — a fiscal/compliance failure (an issued invoice
//   must be retained, not erased; it is corrected by a credit note). There
//   was also no single authority for "may this document's body still be
//   edited?" — InvoiceLifecycleStateMachine.IsEditable existed but was
//   enforced nowhere.
//
//   This class is the SINGLE authority for three questions, matching the
//   §3 matrix in docs/ERP_LIFECYCLE_HARDENING_ANALYSIS_2026-09-09.md:
//
//     • CanHardDelete(kind, status, hasChildren)
//         → true  = a childless pre-posting DRAFT: safe to physically delete
//         → false = posted / has children: must be VOIDED, never deleted
//
//     • CanEditBody(kind, status)
//         → true  = still a working draft: line items / totals editable
//         → false = posted: body frozen, only status transitions allowed
//                   (correct it via Cancel/Void or a Credit Note)
//
//     • VoidTargetStatus(kind)
//         → the terminal "soft-closed" status a Void writes for each kind
//           (Cancelled for PI/TI-cancel path, Rejected for JO, Cancelled
//           for DN). Kept here so repositories don't hard-code it.
//
// DESIGN RULES (mirrors StatusTransitions.cs):
//   • Pure, static, testable — no database, no WPF, never throws.
//   • Reuses StatusTransitions.Kind so there is ONE document-kind vocabulary.
//   • Case-insensitive status matching, tolerant of null/blank (unknown or
//     blank status is treated as NOT hard-deletable and NOT editable — the
//     safe default for a fiscal document is "protect it").
//   • The status sets are deliberately CONSERVATIVE: only genuinely
//     pre-posting states are hard-deletable / body-editable, so enforcing
//     this can never silently destroy or rewrite a committed document.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace ProGlassAutomation.Services.Workflow
{
    /// <summary>
    /// Single authority for delete-vs-void and body-edit-lock decisions per
    /// document type. See the class header and the ERP hardening analysis.
    /// </summary>
    public static class DocumentLifecyclePolicy
    {
        // ── Pre-posting statuses that MAY be physically deleted ────────────
        // Anything not in these sets is soft-closed (voided) instead. These
        // are the states a document can be in BEFORE it becomes a committed
        // order / fiscal document.
        private static readonly Dictionary<StatusTransitions.Kind, HashSet<string>> HardDeletable =
            new()
            {
                // A PI in these states is only ever a quotation that never
                // became a real order — safe to delete. Approved (it converted
                // to a Job Order) and Cancelled are void-only.
                [StatusTransitions.Kind.ProformaInvoice] = CaseInsensitive(
                    "Draft", "Sent", "Rejected", "Expired"),

                // Once a Job Order is Released it is in production; only a raw
                // Draft may be deleted.
                [StatusTransitions.Kind.JobOrder] = CaseInsensitive(
                    "Draft"),

                // Once a Delivery Note is Dispatched the goods have left; only
                // a Draft may be deleted.
                [StatusTransitions.Kind.DeliveryNote] = CaseInsensitive(
                    "Draft"),

                // A Tax Invoice is a fiscal document the moment it is Issued —
                // only a Draft may ever be deleted. Everything else = void +
                // credit note.
                [StatusTransitions.Kind.TaxInvoice] = CaseInsensitive(
                    "Draft"),

                // A Credit Note is itself a fiscal correction document: once
                // Issued it must be retained (voided, never deleted). Only a
                // Draft may be physically deleted.
                [StatusTransitions.Kind.CreditNote] = CaseInsensitive(
                    "Draft"),
            };

        // ── Statuses whose document BODY (lines / totals / party / dates)
        //    may still be edited. Status-only transitions (e.g. Issued→Paid)
        //    are always governed separately by StatusTransitions. ──────────
        private static readonly Dictionary<StatusTransitions.Kind, HashSet<string>> BodyEditable =
            new()
            {
                [StatusTransitions.Kind.ProformaInvoice] = CaseInsensitive(
                    "Draft", "Sent", "Rejected", "Expired"),

                // A Job Order's PI snapshot is immutable by design; its
                // production details stay editable until it is delivered or
                // rejected.
                [StatusTransitions.Kind.JobOrder] = CaseInsensitive(
                    "Draft", "Released", "InProduction", "Completed"),

                [StatusTransitions.Kind.DeliveryNote] = CaseInsensitive(
                    "Draft", "Dispatching"),

                // Only a Draft Tax Invoice body is editable. Once Issued the
                // figures are frozen.
                [StatusTransitions.Kind.TaxInvoice] = CaseInsensitive(
                    "Draft"),

                // Only a Draft Credit Note body is editable. Once Issued its
                // figures are frozen — it has legal effect on the ledger.
                [StatusTransitions.Kind.CreditNote] = CaseInsensitive(
                    "Draft"),
            };

        // ── The terminal status a Void writes, per kind ───────────────────
        private static readonly Dictionary<StatusTransitions.Kind, string> VoidStatus =
            new()
            {
                [StatusTransitions.Kind.ProformaInvoice] = "Cancelled",
                [StatusTransitions.Kind.JobOrder]        = "Rejected",
                [StatusTransitions.Kind.DeliveryNote]    = "Cancelled",
                [StatusTransitions.Kind.TaxInvoice]      = "Cancelled",
                [StatusTransitions.Kind.CreditNote]      = "Cancelled",
            };

        /// <summary>
        /// True only when the document is a childless, pre-posting draft that
        /// is safe to PHYSICALLY delete. Any posted state, or any document
        /// that still has dependents, returns false — the caller must VOID it
        /// instead. Unknown/blank status is treated as not-deletable (safe).
        /// </summary>
        public static bool CanHardDelete(StatusTransitions.Kind kind, string? status, bool hasChildren)
        {
            if (hasChildren) return false;
            if (string.IsNullOrWhiteSpace(status)) return false;
            return HardDeletable.TryGetValue(kind, out var set)
                && set.Contains(status.Trim());
        }

        /// <summary>
        /// True when the document body (line items, totals, party, dates) may
        /// still be edited. False for posted documents — those are corrected
        /// via Cancel/Void or a Credit Note, not by rewriting the original.
        /// Unknown/blank status is treated as not-editable (safe).
        /// </summary>
        public static bool CanEditBody(StatusTransitions.Kind kind, string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return false;
            return BodyEditable.TryGetValue(kind, out var set)
                && set.Contains(status.Trim());
        }

        /// <summary>
        /// The terminal "soft-closed" status a Void writes for a document
        /// kind (e.g. Cancelled for a PI, Rejected for a Job Order). Falls
        /// back to "Cancelled" for an unmapped kind.
        /// </summary>
        public static string VoidTargetStatus(StatusTransitions.Kind kind)
            => VoidStatus.TryGetValue(kind, out var s) ? s : "Cancelled";

        /// <summary>
        /// The three-way decision a "delete" action must take, per the §3
        /// matrix. This is the single place the rule lives so every module
        /// behaves identically.
        /// </summary>
        public enum DeleteDecision
        {
            /// <summary>Childless pre-posting draft — physically delete it.</summary>
            HardDelete,

            /// <summary>Has LIVE dependents — block; the children must be
            /// handled first (voided/deleted). Never orphan or void a parent
            /// out from under live children.</summary>
            Blocked,

            /// <summary>Posted but childless — soft-close (void) so the
            /// number, snapshot and audit trail survive.</summary>
            Void,
        }

        /// <summary>
        /// Decides what a user-initiated "delete" should actually do for a
        /// document, given its status and whether it still has LIVE
        /// dependents (the dependency guard already excludes voided children).
        /// </summary>
        public static DeleteDecision DecideDelete(
            StatusTransitions.Kind kind, string? status, bool hasLiveChildren)
        {
            if (CanHardDelete(kind, status, hasLiveChildren))
                return DeleteDecision.HardDelete;
            if (hasLiveChildren)
                return DeleteDecision.Blocked;
            return DeleteDecision.Void;
        }

        private static HashSet<string> CaseInsensitive(params string[] values)
            => new(values, StringComparer.OrdinalIgnoreCase);
    }
}
