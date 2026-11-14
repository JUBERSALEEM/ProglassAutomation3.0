// ═══════════════════════════════════════════════════════════════════════
// FILE: Models/TaxInvoice/Common/TaxInvoiceStatus/TaxInvoiceStatus.cs
// PROJECT: ProGlassAutomation
// MODULE: Canonical Tax Invoice status vocabulary
//
// WHY THIS EXISTS (2026-09-08):
//   Job Order and Delivery Note each had a canonical status home. Tax
//   Invoice did not — its seven statuses were spelled out as string literals
//   in DocumentListStatus, TaxInvoiceMetricsService, TaxInvoiceFamily,
//   AiContextBuilder and ReportingDataService, with no single place stating
//   which values are real, which are display-only, and how they group.
//
//   That is precisely the condition that produced the Job Order
//   ("In Production" vs "InProduction") and Delivery Note ("Draft" vs
//   "Dispatching") splits, both of which under-reported on the dashboards.
//   Tax Invoice had not broken yet. This file is the reason it stays that
//   way.
//
//   Tests/Agent/StatusVocabularySourceTests.cs documents a typed boundary
//   for this module in its header but did not assert one. It does now.
// ═══════════════════════════════════════════════════════════════════════

using System;

namespace ProGlassAutomation.Models.TaxInvoice.Common.TaxInvoiceStatus
{
    /// <summary>
    /// Lifecycle of a tax invoice. Typed so a status cannot be bypassed with
    /// an arbitrary string at a new call site.
    /// </summary>
    public enum TaxInvoiceStatus
    {
        Draft = 0,
        Issued = 1,
        PartiallyPaid = 2,
        Paid = 3,
        Overdue = 4,
        Cancelled = 5,
        Void = 6,
    }

    /// <summary>
    /// Canonical strings for <see cref="TaxInvoiceStatus"/>, plus the grouping
    /// rules that several services were each re-deriving.
    /// </summary>
    public static class TaxInvoiceStatusValues
    {
        public const string Draft = "Draft";
        public const string Issued = "Issued";
        public const string PartiallyPaid = "PartiallyPaid";
        public const string Paid = "Paid";
        public const string Overdue = "Overdue";
        public const string Cancelled = "Cancelled";
        public const string Void = "Void";

        /// <summary>
        /// The DISPLAY form of <see cref="PartiallyPaid"/>.
        ///
        /// <para><b>Never stored.</b> Verified 2026-09-08: nothing in the
        /// repository assigns <c>Status = "Partially Paid"</c>. The only
        /// assignment anywhere is a test fixture, and it uses the
        /// PascalCase form.</para>
        ///
        /// <para><c>ReportingDataService.TaxInvoice.cs</c> nevertheless
        /// matches both spellings in four places, and
        /// <c>ReportingDataService.DrillDown.cs:289</c> normalises between
        /// them. That defence is harmless but it reads as though a second
        /// stored form exists, which is how the Job Order split survived for
        /// so long. Compare with <see cref="Canonical"/>.</para>
        /// </summary>
        public const string PartiallyPaidDisplay = "Partially Paid";

        /// <summary>Case- and whitespace-insensitive comparison.</summary>
        public static bool Is(string? stored, string expected) =>
            string.Equals(stored?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

        public static bool IsDraft(string? stored) => Is(stored, Draft);
        public static bool IsIssued(string? stored) => Is(stored, Issued);
        public static bool IsPaid(string? stored) => Is(stored, Paid);
        public static bool IsOverdue(string? stored) => Is(stored, Overdue);
        public static bool IsCancelled(string? stored) =>
            Is(stored, Cancelled) || Is(stored, Void);

        /// <summary>
        /// Some payment has been received. The "Paid MTD" card counts this
        /// group, not <see cref="IsPaid"/> alone — see
        /// <c>TaxInvoiceMetricsService.cs</c> CARD 5.
        /// </summary>
        public static bool IsPaidOrPartial(string? stored) =>
            IsPaid(stored) || Is(stored, PartiallyPaid);

        /// <summary>
        /// The invoice has left the building and counts toward revenue.
        /// Duplicates the rule in <c>DocumentListStatus.IsIssuedInvoice</c>.
        /// </summary>
        public static bool HasBeenIssued(string? stored) =>
            IsIssued(stored) || IsPaidOrPartial(stored) || IsOverdue(stored);

        /// <summary>
        /// Money this invoice could still bring in. Mirrors
        /// <c>TaxInvoiceMetricsService.ExcludedFromOverdue</c> — anything
        /// settled or dead is not overdue.
        /// </summary>
        public static bool CanStillBeOwed(string? stored) =>
            !IsPaidOrPartial(stored) && !IsCancelled(stored);

        /// <summary>
        /// Counts toward revenue. Mirrors
        /// <c>TaxInvoiceMetricsService.ExcludedFromRevenue</c>.
        /// </summary>
        public static bool CountsAsRevenue(string? stored) =>
            !IsDraft(stored) && !IsCancelled(stored);

        /// <summary>
        /// Canonical label for display and grouping. Renders
        /// <c>PartiallyPaid</c> as two words and leaves everything else
        /// alone, matching <c>ReportingDataService.DrillDown.cs:294</c>.
        /// </summary>
        public static string Canonical(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return "Unknown";
            return Is(stored, PartiallyPaid) ? PartiallyPaidDisplay : stored.Trim();
        }
    }
}
