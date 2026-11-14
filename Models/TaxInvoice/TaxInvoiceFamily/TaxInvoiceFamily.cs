using ProGlassAutomation.Models.TaxInvoice.Common.TaxInvoiceStatus;
using ProGlassAutomation.Models.TaxInvoice.SavedTaxInvoice;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Models.TaxInvoice.TaxInvoiceFamily
{
    /// <summary>
    /// Groups all Tax Invoices sharing the same ProformaId into one family.
    ///
    /// FAMILY MATH (mirrors DN family rules from master prompt):
    ///   Ordered   = MAX GrandTotal across all members
    ///               (the original PI value — replicated on each invoice)
    ///   Invoiced  = SUM of GrandTotal across all ISSUED members
    ///               (actual money billed so far)
    ///   Balance   = MAX(0, Ordered − Invoiced)
    ///               (remaining amount still to be invoiced)
    ///
    /// PARTIAL INVOICE SEQUENCE:
    ///   First invoice  → INV-2026-001       (Full or first Partial)
    ///   Second invoice → INV-2026-001-A
    ///   Third invoice  → INV-2026-001-B
    ///   Letters assigned by TaxInvoiceRepository.GetNextSequenceLetter()
    ///
    /// STATUS ROLL-UP:
    ///   FamilyStatus is derived from member statuses:
    ///     All Paid              → "Paid"
    ///     Any Overdue           → "Overdue"
    ///     Any Issued            → "PartiallyInvoiced"
    ///     All Draft             → "Draft"
    ///     Any Cancelled/Void    → "HasCancelled"
    ///     Mixed                 → "PartiallyPaid"
    ///
    /// This class is a computed read model — never persisted to DB.
    /// Built on demand by TaxInvoiceRepository.GetFamilyAsync().
    /// </summary>
    public class TaxInvoiceFamily
    {
        // ── Identity ──────────────────────────────────────────────────────

        /// <summary>Shared ProformaId — family key, e.g. "PI-2026-001".</summary>
        public string ProformaId { get; set; } = "";

        /// <summary>Customer name from the latest invoice in the family.</summary>
        public string CustomerName { get; set; } = "";

        /// <summary>Project name from the latest invoice in the family.</summary>
        public string ProjectName { get; set; } = "";

        // ── Members ───────────────────────────────────────────────────────

        /// <summary>
        /// All invoices in this family, ordered by SequenceNumber ascending.
        /// </summary>
        public List<TaxInvoiceListItem> Members { get; set; } = new();

        /// <summary>Count of invoices in this family.</summary>
        public int MemberCount => Members.Count;

        // ── Financial aggregates ──────────────────────────────────────────

        /// <summary>
        /// MAX GrandTotal across all members.
        /// Represents the original PI value (replicated on each invoice).
        /// If invoices have different GrandTotals (partial split), this is
        /// the largest single invoice value — not the PI total.
        ///
        /// NOTE: For true PI total tracking, use ProformaTotal (set by
        /// repository from the PI snapshot when building the family).
        /// </summary>
        public decimal OrderedTotal { get; set; }

        /// <summary>
        /// Original Proforma Invoice total (AED).
        /// Set from PI snapshot by repository — may be 0 if PI not found.
        /// Used as the "Ordered" baseline for Balance calculation.
        /// </summary>
        public decimal ProformaTotal { get; set; }

        /// <summary>
        /// SUM of GrandTotal across all members with Status = "Issued",
        /// "Paid", or "PartiallyPaid".
        /// Represents total money actually invoiced to the customer.
        /// </summary>
        public decimal InvoicedTotal { get; set; }

        /// <summary>
        /// Balance = MAX(0, ProformaTotal − InvoicedTotal).
        /// Amount still to be invoiced.
        /// Uses ProformaTotal (not OrderedTotal) for accuracy.
        /// </summary>
        public decimal BalanceTotal => Math.Max(0m, ProformaTotal - InvoicedTotal);

        /// <summary>SUM of VatAmount across all issued members.</summary>
        public decimal TotalVat { get; set; }

        // ── Quantity / area aggregates ────────────────────────────────────

        /// <summary>SUM of TotalQty across all members.</summary>
        public int TotalQty { get; set; }

        /// <summary>SUM of TotalSqm across all members.</summary>
        public decimal TotalSqm { get; set; }

        // ── Status roll-up ────────────────────────────────────────────────

        /// <summary>
        /// Derived status string for display.
        /// Computed by BuildFrom() — not stored in DB.
        /// Values: Paid | Overdue | PartiallyInvoiced | Draft |
        ///         HasCancelled | PartiallyPaid | Mixed
        /// </summary>
        public string FamilyStatus { get; set; } = "Draft";

        /// <summary>True when all members are Paid.</summary>
        public bool IsFullyPaid { get; set; }

        /// <summary>True when at least one member is Issued or Paid.</summary>
        public bool HasIssuedInvoices { get; set; }

        /// <summary>True when BalanceTotal > 0.</summary>
        public bool HasBalance => BalanceTotal > 0m;

        // ── Latest invoice ────────────────────────────────────────────────

        /// <summary>Most recent invoice in the family (highest SequenceNumber).</summary>
        public TaxInvoiceListItem? LatestInvoice =>
            Members.OrderByDescending(m => m.SequenceNumber).FirstOrDefault();

        /// <summary>Latest invoice number for display (e.g. "INV-2026-001-B").</summary>
        public string LatestInvoiceNumber => LatestInvoice?.InvoiceNumber ?? "";

        /// <summary>Latest issue date for display.</summary>
        public DateTime LatestIssueDate => LatestInvoice?.IssueDate ?? default;

        // ── Next sequence letter ──────────────────────────────────────────

        /// <summary>
        /// Next available sequence letter for partial invoice suffix.
        /// First partial = "A", second = "B", etc.
        /// Computed from Members — not stored.
        /// Returns empty string if no partials exist yet (next = first partial).
        /// </summary>
        public string NextSequenceLetter
        {
            get
            {
                var letters = Members
                    .Where(m => !string.IsNullOrEmpty(m.SequenceLetter))
                    .Select(m => m.SequenceLetter.ToUpperInvariant().Trim())
                    .Where(l => l.Length == 1 && l[0] >= 'A' && l[0] <= 'Z')
                    .OrderBy(l => l)
                    .ToList();

                var used = new HashSet<string>(letters, StringComparer.OrdinalIgnoreCase);
                for (char c = 'A'; c <= 'Z'; c++)
                {
                    string letter = c.ToString();
                    if (!used.Contains(letter)) return letter;
                }
                return "";
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  FACTORY — build from a list of list items
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Groups a flat list of TaxInvoiceListItems by ProformaId,
        /// computes all aggregates, and returns one TaxInvoiceFamily
        /// per unique ProformaId.
        ///
        /// Called by TaxInvoiceRepository.GetAllFamiliesAsync().
        /// ProformaTotal is set to 0 here — repository must populate it
        /// from PI snapshot if available.
        /// </summary>
        public static IReadOnlyList<TaxInvoiceFamily> BuildFrom(
            IEnumerable<TaxInvoiceListItem> items)
        {
            if (items == null) return Array.Empty<TaxInvoiceFamily>();

            return items
                .GroupBy(i => i.ProformaId,
                         StringComparer.OrdinalIgnoreCase)
                .Select(g => BuildFamily(g.Key, g.ToList()))
                .OrderByDescending(f => f.LatestIssueDate)
                .ThenBy(f => f.ProformaId)
                .ToList();
        }

        // ── Private builder ───────────────────────────────────────────────

        private static TaxInvoiceFamily BuildFamily(
            string proformaId,
            List<TaxInvoiceListItem> members)
        {
            // Issued / paid / partial / overdue consume invoiceable money.
            // Draft / Cancelled / Void do not. Overdue was previously omitted
            // so a past-due invoice looked unbilled.
            static bool IsIssued(string status) =>
                TaxInvoiceStatusValues.HasBeenIssued(status);

            var issuedMembers = members.Where(m => IsIssued(m.Status)).ToList();

            decimal invoicedTotal = issuedMembers.Sum(m => m.GrandTotal);
            decimal totalVat = issuedMembers.Sum(m => m.VatAmount);
            decimal orderedTotal = members.Count > 0
                ? members.Max(m => m.GrandTotal)
                : 0m;

            bool allPaid = members.Count > 0 && members.All(m =>
                string.Equals(m.Status, "Paid",
                    StringComparison.OrdinalIgnoreCase));

            bool anyOverdue = members.Any(m =>
                string.Equals(m.Status, "Overdue",
                    StringComparison.OrdinalIgnoreCase));

            bool anyIssued = issuedMembers.Count > 0;

            bool anyCancelled = members.Any(m =>
                string.Equals(m.Status, "Cancelled",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.Status, "Void",
                    StringComparison.OrdinalIgnoreCase));

            bool allDraft = members.All(m =>
                string.Equals(m.Status, "Draft",
                    StringComparison.OrdinalIgnoreCase));

            string familyStatus = allPaid ? "Paid"
                : anyOverdue ? "Overdue"
                : anyCancelled ? "HasCancelled"
                : allDraft ? "Draft"
                : anyIssued ? "PartiallyInvoiced"
                : "Mixed";

            var latest = members
                .OrderByDescending(m => m.SequenceNumber)
                .FirstOrDefault();

            return new TaxInvoiceFamily
            {
                ProformaId = proformaId,
                CustomerName = latest?.CustomerName ?? "",
                ProjectName = latest?.ProjectName ?? "",
                Members = members
                    .OrderBy(m => m.SequenceNumber)
                    .ToList(),
                OrderedTotal = orderedTotal,
                ProformaTotal = 0m, // populated by repository
                InvoicedTotal = invoicedTotal,
                TotalVat = totalVat,
                TotalQty = members.Sum(m => m.TotalQty),
                TotalSqm = members.Sum(m => m.TotalSqm),
                FamilyStatus = familyStatus,
                IsFullyPaid = allPaid,
                HasIssuedInvoices = anyIssued
            };
        }
    }
}