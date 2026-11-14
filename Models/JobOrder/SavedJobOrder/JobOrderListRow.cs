// ═══════════════════════════════════════════════════════════════════════
// FILE: Models/JobOrder/SavedJobOrder/JobOrderListRow.cs
// PROJECT: ProGlassAutomation
//
// PERF. A narrow, read-only projection for the saved-job-orders list. The
// list renders JobOrderId, customer, project, value, status, priority and
// the two dates — none of which live in FullSnapshotJson. Loading the full
// entity (which carries the whole serialized snapshot blob per row) just to
// build these columns is pure waste, especially as the list grows. This type
// holds only the indexed scalar columns the list actually reads. It is
// intentionally NOT an entity — no key, never tracked, never saved.
//
// It mirrors the fields SavedJobOrdersListViewModel passes into
// JobOrderRowViewModel, so the two stay in step.
// ═══════════════════════════════════════════════════════════════════════

using System;

namespace ProGlassAutomation.Models.JobOrder.SavedJobOrder
{
    /// <summary>
    /// Read-only projection of <see cref="SavedJobOrder"/> for the list view.
    /// Excludes FullSnapshotJson by design.
    /// </summary>
    public sealed class JobOrderListRow
    {
        public int Id { get; set; }
        public string JobOrderId { get; set; } = "";
        public string SourceProformaId { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public decimal GrandTotalVatInclusive { get; set; }
        public string Status { get; set; } = "";
        public string Priority { get; set; } = "";
        public DateTime? DeliveryDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// ERP lifecycle hardening (Phase 1): true when the job order has been
        /// voided (soft-closed). Hidden from the list unless "Show voided" is on.
        /// </summary>
        public bool IsVoid { get; set; }
    }
}
