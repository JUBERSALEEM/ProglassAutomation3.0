// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Models/InquiryLifecycle.cs
// MODULE: Inquiry — full commercial lifecycle tracking.
//
// An inquiry is the front door of a commercial chain. Behind it, five
// downstream documents can exist:
//
//   1. Inquiry           (this record — always exists)
//   2. Proforma Invoice  (PI)
//   3. Job Order         (JO)
//   4. Delivery Note     (DN)
//   5. Tax Invoice       (TI)
//   6. Account Statement (STMT)
//
// Each stage has its own vocabulary and its own state
// (NotStarted / Draft / Active / Completed / Skipped). The snapshot is
// display-first: status strings are snapshotted at read time so the
// register doesn't re-query the downstream databases on every property
// read.
//
// V3 scope: the snapshot model is ported unchanged. The V3 lifecycle
// service hydrates the PI / JO stages from the denormalised link fields
// on the Inquiries row (written by SetProformaLinkAsync once the Proforma
// module lands); DN / TI / STMT stages report NotStarted until those
// modules are rebuilt on top of this same snapshot contract.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace ProGlassAutomation.Models.Inquiry
{
    /// <summary>
    /// Which downstream document a lifecycle entry refers to. Ordered
    /// from earliest to latest in the commercial chain.
    /// </summary>
    public enum InquiryLifecycleKind
    {
        Inquiry = 0,
        ProformaInvoice = 1,
        JobOrder = 2,
        DeliveryNote = 3,
        TaxInvoice = 4,
        Statement = 5
    }

    /// <summary>
    /// Coarse-grained state of one downstream document relative to its
    /// place in the pipeline. Entity-specific status vocabularies are
    /// collapsed into 5 buckets so the register UI can colour-code rows
    /// consistently across all six stages.
    /// </summary>
    public enum InquiryLifecycleState
    {
        /// <summary>No document of this type has been generated yet.</summary>
        NotStarted = 0,
        /// <summary>Document exists but hasn't been released/sent/issued.</summary>
        Draft = 1,
        /// <summary>Document is in flight (released, dispatched, issued, in production…).</summary>
        Active = 2,
        /// <summary>Terminal positive state (delivered, received, paid, signed…).</summary>
        Completed = 3,
        /// <summary>Explicitly bypassed (cancelled, void, on hold, hold-then-skip…).</summary>
        Skipped = 4
    }

    /// <summary>
    /// Per-inquiry snapshot of all six pipeline stages. Built by
    /// <c>IInquiryLifecycleService</c> from a batch of inquiry IDs and
    /// hydrated onto each register row.
    /// </summary>
    public sealed class InquiryLifecycleSnapshot
    {
        public int InquiryId { get; init; }

        // ── Per-stage raw data (nullable when not started) ─────────────

        public string InquiryStatus { get; init; } = "";
        // Proforma Invoice (PI) — the row ID lets the right-click
        // "Open Proforma Invoice" item load the specific document once
        // the PI module exists. Set to 0 when no PI exists yet.
        public int ProformaRowId { get; init; }
        public string? ProformaNumber { get; init; }
        public string? ProformaStatus { get; init; }
        public DateTime? ProformaUpdatedAt { get; init; }

        // Job Order (JO) — same pattern as PI.
        public int JobOrderRowId { get; init; }
        public string? JobOrderNumber { get; init; }
        public string? JobOrderStatus { get; init; }
        public DateTime? JobOrderUpdatedAt { get; init; }

        // Delivery Note (DN) — the most recent DN in the family.
        public int DeliveryNoteRowId { get; init; }
        public int DeliveryNoteCount { get; init; }
        public string? LatestDeliveryNoteNumber { get; init; }
        public string? LatestDeliveryNoteStatus { get; init; }
        public DateTime? LatestDeliveryNoteAt { get; init; }

        // Tax Invoice (TI) — the most recent TI in the family.
        public int TaxInvoiceRowId { get; init; }
        public int TaxInvoiceCount { get; init; }
        public string? LatestTaxInvoiceNumber { get; init; }
        public string? LatestTaxInvoiceStatus { get; init; }
        public DateTime? LatestTaxInvoiceAt { get; init; }
        public decimal LatestTaxInvoiceGrandTotal { get; init; }

        public int StatementCount { get; init; }
        public string? LatestStatementNumber { get; init; }
        public DateTime? LatestStatementAt { get; init; }

        // ── Aggregated fields ──────────────────────────────────────────

        /// <summary>
        /// Total billed across all tax invoices linked to this inquiry
        /// (sum of GrandTotal). AED. Zero if no tax invoice exists yet.
        /// </summary>
        public decimal TotalBilled { get; init; }

        /// <summary>
        /// Outstanding on the latest open tax invoice in the family.
        /// Zero if everything is paid or no tax invoice exists.
        /// </summary>
        public decimal CurrentBillOutstanding { get; init; }

        /// <summary>
        /// Total outstanding across ALL tax invoices linked to this
        /// inquiry (sum of per-invoice balances still owed).
        /// </summary>
        public decimal TotalOutstanding { get; init; }

        /// <summary>
        /// Furthest stage reached in the pipeline (0-based index into
        /// <see cref="InquiryLifecycleKind"/>). 0 = only the inquiry
        /// exists; 5 = a statement has been generated.
        /// </summary>
        public int FurthestStageReached { get; init; }

        // ── Computed helpers (cheap to read in XAML) ───────────────────

        public bool HasProforma => !string.IsNullOrWhiteSpace(ProformaNumber);
        public bool HasJobOrder => !string.IsNullOrWhiteSpace(JobOrderNumber);
        public bool HasDeliveryNote => DeliveryNoteCount > 0;
        public bool HasTaxInvoice => TaxInvoiceCount > 0;
        public bool HasStatement => StatementCount > 0;
        public bool HasAnyDownstreamDocument =>
            HasProforma || HasJobOrder || HasDeliveryNote || HasTaxInvoice || HasStatement;

        /// <summary>
        /// Coarse <see cref="InquiryLifecycleState"/> for a given stage,
        /// collapsed from the raw status string.
        /// </summary>
        public InquiryLifecycleState StateFor(InquiryLifecycleKind kind) => kind switch
        {
            InquiryLifecycleKind.Inquiry => MapInquiryStatus(InquiryStatus),
            InquiryLifecycleKind.ProformaInvoice => HasProforma
                ? MapProformaStatus(ProformaStatus) : InquiryLifecycleState.NotStarted,
            InquiryLifecycleKind.JobOrder => HasJobOrder
                ? MapJobOrderStatus(JobOrderStatus) : InquiryLifecycleState.NotStarted,
            InquiryLifecycleKind.DeliveryNote => HasDeliveryNote
                ? MapDeliveryNoteStatus(LatestDeliveryNoteStatus) : InquiryLifecycleState.NotStarted,
            InquiryLifecycleKind.TaxInvoice => HasTaxInvoice
                ? MapTaxInvoiceStatus(LatestTaxInvoiceStatus) : InquiryLifecycleState.NotStarted,
            InquiryLifecycleKind.Statement => HasStatement
                ? InquiryLifecycleState.Completed : InquiryLifecycleState.NotStarted,
            _ => InquiryLifecycleState.NotStarted
        };

        // ── Status vocabulary maps (single source of truth) ────────────

        private static InquiryLifecycleState MapInquiryStatus(string? status) =>
            string.Equals(status, InquiryStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
                ? InquiryLifecycleState.Skipped
                : string.Equals(status, InquiryStatuses.Hold, StringComparison.OrdinalIgnoreCase)
                    ? InquiryLifecycleState.Skipped
                    : string.Equals(status, InquiryStatuses.Confirmed, StringComparison.OrdinalIgnoreCase)
                        ? InquiryLifecycleState.Completed
                        : InquiryLifecycleState.Active;

        private static InquiryLifecycleState MapProformaStatus(string? status) =>
            string.IsNullOrWhiteSpace(status) ? InquiryLifecycleState.NotStarted
            : status.Equals("Draft", StringComparison.OrdinalIgnoreCase) ? InquiryLifecycleState.Draft
            : status.Equals("Expired", StringComparison.OrdinalIgnoreCase) ? InquiryLifecycleState.Skipped
            : status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) ? InquiryLifecycleState.Skipped
            : status.Equals("Rejected", StringComparison.OrdinalIgnoreCase) ? InquiryLifecycleState.Skipped
            : status.Equals("Approved", StringComparison.OrdinalIgnoreCase) ? InquiryLifecycleState.Completed
            : InquiryLifecycleState.Active;

        private static InquiryLifecycleState MapJobOrderStatus(string? status) =>
            string.IsNullOrWhiteSpace(status) ? InquiryLifecycleState.NotStarted
            : status.Equals("Draft", StringComparison.OrdinalIgnoreCase) ? InquiryLifecycleState.Draft
            : status.Equals("Delivered", StringComparison.OrdinalIgnoreCase) ? InquiryLifecycleState.Completed
            : status.Equals("Completed", StringComparison.OrdinalIgnoreCase) ? InquiryLifecycleState.Completed
            : InquiryLifecycleState.Active; // Released, InProduction

        private static InquiryLifecycleState MapDeliveryNoteStatus(string? status) =>
            string.IsNullOrWhiteSpace(status) ? InquiryLifecycleState.NotStarted
            : status.Equals("Draft", StringComparison.OrdinalIgnoreCase) ? InquiryLifecycleState.Draft
            : status.Equals("Received", StringComparison.OrdinalIgnoreCase) ? InquiryLifecycleState.Completed
            : status.Equals("Signed", StringComparison.OrdinalIgnoreCase) ? InquiryLifecycleState.Completed
            : status.Equals("Delivered", StringComparison.OrdinalIgnoreCase) ? InquiryLifecycleState.Completed
            : InquiryLifecycleState.Active; // Dispatched

        private static InquiryLifecycleState MapTaxInvoiceStatus(string? status) =>
            string.IsNullOrWhiteSpace(status) ? InquiryLifecycleState.NotStarted
            : status.Equals("Draft", StringComparison.OrdinalIgnoreCase) ? InquiryLifecycleState.Draft
            : status.Equals("Paid", StringComparison.OrdinalIgnoreCase) ? InquiryLifecycleState.Completed
            : status.Equals("Void", StringComparison.OrdinalIgnoreCase) ? InquiryLifecycleState.Skipped
            : status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) ? InquiryLifecycleState.Skipped
            : InquiryLifecycleState.Active; // Issued, PartiallyPaid, Overdue

        /// <summary>Empty snapshot for an inquiry that has no downstream documents yet.</summary>
        public static InquiryLifecycleSnapshot Empty(int inquiryId) => new() { InquiryId = inquiryId };
    }

    /// <summary>
    /// Display-friendly vocabulary for the segmented pipeline stages that
    /// sit above the register. One entry per stage, with a live count of
    /// how many inquiries have reached it.
    /// </summary>
    public sealed class InquiryPipelineTab
    {
        public InquiryLifecycleKind Kind { get; init; }
        public string Label { get; init; } = "";
        public string ShortLabel { get; init; } = "";
        public int Count { get; set; }
        public bool IsSelected { get; set; }

        public static IReadOnlyList<InquiryPipelineTab> All => new[]
        {
            new InquiryPipelineTab { Kind = InquiryLifecycleKind.Inquiry, Label = "Inquiries", ShortLabel = "INQ" },
            new InquiryPipelineTab { Kind = InquiryLifecycleKind.ProformaInvoice, Label = "Proforma Invoices", ShortLabel = "PI" },
            new InquiryPipelineTab { Kind = InquiryLifecycleKind.JobOrder, Label = "Job Orders", ShortLabel = "JO" },
            new InquiryPipelineTab { Kind = InquiryLifecycleKind.DeliveryNote, Label = "Delivery Notes", ShortLabel = "DN" },
            new InquiryPipelineTab { Kind = InquiryLifecycleKind.TaxInvoice, Label = "Tax Invoices", ShortLabel = "TI" },
            new InquiryPipelineTab { Kind = InquiryLifecycleKind.Statement, Label = "Statements", ShortLabel = "STMT" }
        };
    }
}
