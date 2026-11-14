// ═══════════════════════════════════════════════════════════════════════
// FILE: Models/JobOrder/SavedJobOrder/JobOrderSearchRow.cs
// PROJECT: ProGlassAutomation
//
// PERF. Narrow, read-only row for the global-search box. Global search
// previously loaded every SavedJobOrder (with FullSnapshotJson) just to
// find top matches. This type carries only the columns search reads, so the
// DB query can project them directly and skip the blob.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Models.JobOrder.SavedJobOrder
{
    /// <summary>Read-only projection of <see cref="SavedJobOrder"/> for search.</summary>
    public sealed class JobOrderSearchRow
    {
        public string JobOrderId { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string SourceProformaId { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
