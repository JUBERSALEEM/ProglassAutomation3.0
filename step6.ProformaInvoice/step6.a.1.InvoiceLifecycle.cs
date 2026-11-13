// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.a.1.InvoiceLifecycle.cs
// MODULE: step6 — the Proforma Invoice status vocabulary and the ONE
// transition table. Nothing else in the module may compare status strings.
//
// Owner instruction (2026-11-13): the ADV nine-step wizard is the reference
// for BEHAVIOUR only. Its steps 1–3 (Company, Client, Project) collapse into
// this module's single header stage, which READS the existing Company, Client,
// User and Project records instead of retyping them. Steps 4–9 are kept as
// stages 2–7 (see InvoiceStage).
//
// A document is never deleted. Cancelled is terminal: its number stays used,
// its snapshot stays readable, and no second document can inherit the number.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Models.Proforma
{
    /// <summary>Canonical status values stored in ProformaInvoices.Status.</summary>
    public static class ProformaInvoiceStatus
    {
        public const string Draft = "Draft";
        public const string Sent = "Sent";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Expired = "Expired";
        public const string Cancelled = "Cancelled";

        public static readonly IReadOnlyList<string> All = new[]
        { Draft, Sent, Approved, Rejected, Expired, Cancelled };

        /// <summary>
        /// Normalizes casing/spacing onto the vocabulary. An unrecognized value
        /// is returned trimmed — never silently re-mapped to Draft, because an
        /// unexpected status is something to surface, not to hide.
        /// </summary>
        public static string Normalize(string? value)
        {
            string text = (value ?? "").Trim();
            foreach (string known in All)
                if (string.Equals(known, text, StringComparison.OrdinalIgnoreCase))
                    return known;
            return text;
        }

        public static bool IsDefined(string? value) => All.Contains(Normalize(value));
    }

    /// <summary>
    /// The wizard's stages in THIS application. The ADV step numbers are shown
    /// beside them so the two modules can be talked about without ambiguity.
    /// </summary>
    public enum InvoiceStage
    {
        /// <summary>ADV steps 1–3, merged: company + client + project + prepared-by.</summary>
        Header = 1,
        /// <summary>ADV step 4 — the pricing estimator (glass build → rate/sqm).</summary>
        Pricing = 2,
        /// <summary>ADV step 5 — the specification grid (W1/H1/W2/H2 × qty).</summary>
        SpecGrid = 3,
        /// <summary>ADV step 6 — predefined, wastage and custom charges.</summary>
        OtherCharges = 4,
        /// <summary>ADV step 7 — discount, VAT and the totals waterfall.</summary>
        Summary = 5,
        /// <summary>ADV step 8 — the evidence chain.</summary>
        Attachments = 6,
        /// <summary>ADV step 9 — costs, profit, margin and the target KPI.</summary>
        ProfitLoss = 7
    }

    /// <summary>One legal status change, with the wording the UI shows.</summary>
    public sealed class InvoiceTransition
    {
        public string From { get; init; } = "";
        public string To { get; init; } = "";
        public string Description { get; init; } = "";
    }

    /// <summary>
    /// The transition authority. A caller cannot invent a status change: the
    /// repository asks this table before it writes, and the verification
    /// executable asks the same table.
    /// </summary>
    public static class InvoiceLifecycle
    {
        private static readonly InvoiceTransition[] Table =
        {
            new() { From = ProformaInvoiceStatus.Draft, To = ProformaInvoiceStatus.Sent,
                    Description = "Issue to the customer" },
            new() { From = ProformaInvoiceStatus.Draft, To = ProformaInvoiceStatus.Cancelled,
                    Description = "Cancel this draft" },
            new() { From = ProformaInvoiceStatus.Sent, To = ProformaInvoiceStatus.Approved,
                    Description = "Record customer acceptance" },
            new() { From = ProformaInvoiceStatus.Sent, To = ProformaInvoiceStatus.Rejected,
                    Description = "Record customer rejection" },
            new() { From = ProformaInvoiceStatus.Sent, To = ProformaInvoiceStatus.Expired,
                    Description = "Mark the validity period ended" },
            new() { From = ProformaInvoiceStatus.Sent, To = ProformaInvoiceStatus.Cancelled,
                    Description = "Cancel this quotation" },
            new() { From = ProformaInvoiceStatus.Approved, To = ProformaInvoiceStatus.Cancelled,
                    Description = "Cancel an accepted quotation" },
            new() { From = ProformaInvoiceStatus.Rejected, To = ProformaInvoiceStatus.Draft,
                    Description = "Revise and return to draft" },
            new() { From = ProformaInvoiceStatus.Expired, To = ProformaInvoiceStatus.Draft,
                    Description = "Revise and return to draft" }
        };

        /// <summary>Every transition, for a menu or a check.</summary>
        public static IReadOnlyList<InvoiceTransition> All => Table;

        /// <summary>The transitions available from one status.</summary>
        public static IReadOnlyList<InvoiceTransition> From(string? status)
        {
            string current = ProformaInvoiceStatus.Normalize(status);
            return Table.Where(t => string.Equals(t.From, current, StringComparison.Ordinal))
                        .ToList();
        }

        /// <summary>True only for a pair in the table. Cancelled is terminal.</summary>
        public static bool CanTransition(string? from, string? to)
        {
            string source = ProformaInvoiceStatus.Normalize(from);
            string target = ProformaInvoiceStatus.Normalize(to);
            if (source.Length == 0 || target.Length == 0) return false;
            if (string.Equals(source, target, StringComparison.Ordinal)) return false;
            return Table.Any(t =>
                string.Equals(t.From, source, StringComparison.Ordinal) &&
                string.Equals(t.To, target, StringComparison.Ordinal));
        }

        /// <summary>True when the status is an end state (nothing follows).</summary>
        public static bool IsTerminal(string? status) =>
            string.Equals(ProformaInvoiceStatus.Normalize(status),
                          ProformaInvoiceStatus.Cancelled, StringComparison.Ordinal);

        /// <summary>
        /// A document whose body may still be edited. An issued, accepted,
        /// rejected or expired document is a record of what the customer was
        /// told; a revision is a NEW document, never a silent rewrite.
        /// </summary>
        public static bool CanEditBody(string? status) =>
            string.Equals(ProformaInvoiceStatus.Normalize(status),
                          ProformaInvoiceStatus.Draft, StringComparison.Ordinal);
    }
}
