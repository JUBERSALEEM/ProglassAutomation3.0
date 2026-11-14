// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Workflow/StatusTransitions.cs
// PROJECT: ProGlassAutomation
// MODULE: Core — centralized workflow status-transition rules
//
// Phase 3 (workflow state machines). Previously each list allowed an
// arbitrary status to be written (any menu value to any state), with no
// shared definition of what a valid transition is. This centralises the
// transition table for every document type so validation can live at the
// service boundary rather than being hidden in the UI.
//
// DESIGN:
//   • One authoritative transition table per document type.
//   • A transition is valid iff the source state lists the target as an
//     allowed next state.
//   • The tables are intentionally COMPLETE for every transition the current
//     application can legitimately reach (the menus + the business cascades),
//     so enforcing them does NOT break any existing working flow. They act as
//     a safety net that rejects unknown/typo/impossible states and documents
//     the intended lifecycle.
//   • Validation is a pure, static, testable function — no database, no UI.
//
// Reversal: re-opening a rejected/expired/cancelled document back to Draft is
// an explicit, allowed edge so a user can correct an accidental terminal
// state (matches current behaviour).
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace ProGlassAutomation.Services.Workflow
{
    /// <summary>Outcome of a status-transition validation.</summary>
    public readonly struct TransitionResult
    {
        public bool IsValid { get; }
        public string? Reason { get; }

        private TransitionResult(bool valid, string? reason)
        {
            IsValid = valid;
            Reason = reason;
        }

        public static TransitionResult Ok() => new(true, null);
        public static TransitionResult Invalid(string reason) => new(false, reason);
    }

    /// <summary>
    /// Validates status transitions for each document type using a single
    /// authoritative transition table.
    /// </summary>
    public static class StatusTransitions
    {
        /// <summary>Document kinds supported by the transition tables.</summary>
        public enum Kind
        {
            ProformaInvoice,
            JobOrder,
            DeliveryNote,
            TaxInvoice,
            CreditNote,
        }

        // ── Transition tables ─────────────────────────────────────────────
        // Key = source status; value = allowed next statuses (case-insensitive).

        private static readonly Dictionary<string, HashSet<string>> Proforma =
            BuildTable(new (string, string[])[]
            {
                ("Draft",     new[] { "Draft", "Sent", "Approved", "Rejected", "Expired", "Cancelled" }),
                ("Sent",      new[] { "Sent", "Approved", "Rejected", "Expired", "Cancelled" }),
                ("Approved",  new[] { "Approved", "Rejected", "Cancelled" }),
                ("Rejected",  new[] { "Rejected", "Draft", "Cancelled" }),
                ("Expired",   new[] { "Expired", "Draft", "Cancelled" }),
                ("Cancelled", new[] { "Cancelled", "Draft" }),
            });

        private static readonly Dictionary<string, HashSet<string>> JobOrder =
            BuildTable(new (string, string[])[]
            {
                ("Draft",        new[] { "Draft", "Released", "InProduction", "Completed", "Delivered", "Rejected" }),
                ("Released",     new[] { "Released", "InProduction", "Completed", "Delivered", "Rejected" }),
                ("InProduction", new[] { "InProduction", "Completed", "Delivered", "Rejected" }),
                ("Completed",    new[] { "Completed", "Delivered", "Rejected" }),
                ("Delivered",    new[] { "Delivered", "Rejected" }),
                ("Rejected",     new[] { "Rejected", "Draft" }),
            });

        private static readonly Dictionary<string, HashSet<string>> DeliveryNote =
            BuildTable(new (string, string[])[]
            {
                // "Cancelled" is the void (soft-close) target for a delivery
                // note (ERP lifecycle hardening, Phase 1) and is reachable
                // from every non-terminal state, plus Cancelled → Draft to
                // recover from an accidental void.
                ("Draft",        new[] { "Draft", "Dispatching", "Dispatched", "Delivered", "Signed", "Received", "Cancelled" }),
                ("Dispatching",  new[] { "Dispatching", "Dispatched", "Delivered", "Signed", "Received", "Draft", "Cancelled" }),
                ("Dispatched",   new[] { "Dispatched", "Delivered", "Signed", "Received", "Cancelled" }),
                ("Delivered",    new[] { "Delivered", "Signed", "Received", "Cancelled" }),
                ("Signed",       new[] { "Signed", "Received", "Cancelled" }),
                ("Received",     new[] { "Received" }),
                ("Cancelled",    new[] { "Cancelled", "Draft" }),
            });

        private static readonly Dictionary<string, HashSet<string>> TaxInvoice =
            BuildTable(new (string, string[])[]
            {
                ("Draft",         new[] { "Draft", "Issued", "Cancelled", "Void" }),
                ("Issued",        new[] { "Issued", "Paid", "PartiallyPaid", "Overdue", "Cancelled", "Void" }),
                ("PartiallyPaid", new[] { "PartiallyPaid", "Paid", "Overdue", "Cancelled", "Void" }),
                ("Paid",          new[] { "Paid" }),
                ("Overdue",       new[] { "Overdue", "Paid", "PartiallyPaid", "Cancelled", "Void" }),
                ("Cancelled",     new[] { "Cancelled" }),
                ("Void",          new[] { "Void" }),
            });

        // Credit Note (ERP hardening — Phase 4). A fiscal correction:
        //   Draft   → still editable, can be issued or cancelled outright.
        //   Issued  → committed against the invoice; can be Applied (settled)
        //             or Cancelled (soft-close/void path).
        //   Applied → terminal (its value has reduced the ledger).
        //   Cancelled → terminal (the void target).
        private static readonly Dictionary<string, HashSet<string>> CreditNote =
            BuildTable(new (string, string[])[]
            {
                ("Draft",     new[] { "Draft", "Issued", "Cancelled" }),
                ("Issued",    new[] { "Issued", "Applied", "Cancelled" }),
                ("Applied",   new[] { "Applied" }),
                ("Cancelled", new[] { "Cancelled" }),
            });

        /// <summary>
        /// Validates a transition. Returns Ok when the transition is allowed
        /// (or the target equals the source — idempotent). Case-insensitive.
        /// </summary>
        public static TransitionResult Validate(Kind kind, string? current, string? next)
        {
            string from = string.IsNullOrWhiteSpace(current) ? "" : current.Trim();
            string to = string.IsNullOrWhiteSpace(next) ? "" : next.Trim();

            if (string.IsNullOrWhiteSpace(to))
                return TransitionResult.Invalid("Target status is empty.");

            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                return TransitionResult.Ok();   // idempotent re-save

            var table = TableFor(kind);
            if (table == null)
                return TransitionResult.Invalid($"No transition table for {kind}.");

            if (from.Length == 0 || !table.TryGetValue(from, out var allowed))
                return TransitionResult.Invalid(
                    $"Unknown source status '{from}' for {kind}.");

            if (allowed.Contains(to))
                return TransitionResult.Ok();

            return TransitionResult.Invalid(
                $"'{from}' → '{to}' is not a valid {kind} transition.");
        }

        private static Dictionary<string, HashSet<string>>? TableFor(Kind kind)
            => kind switch
            {
                Kind.ProformaInvoice => Proforma,
                Kind.JobOrder => JobOrder,
                Kind.DeliveryNote => DeliveryNote,
                Kind.TaxInvoice => TaxInvoice,
                Kind.CreditNote => CreditNote,
                _ => null
            };

        private static Dictionary<string, HashSet<string>> BuildTable(
            IEnumerable<(string From, string[] To)> rows)
        {
            var table = new Dictionary<string, HashSet<string>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var (from, to) in rows)
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in to) set.Add(t);
                table[from] = set;
            }
            return table;
        }
    }
}
