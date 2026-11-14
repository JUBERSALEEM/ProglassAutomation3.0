// ═══════════════════════════════════════════════════════════════════════
// FILE: Models/ProformaInvoice/SavedInvoice/ProformaSearchRow.cs
// PROJECT: ProGlassAutomation
//
// PERF. Narrow, read-only row for the global-search box. Global search
// previously loaded every SavedProformaInvoice (with FullSnapshotJson) just
// to find top matches. This type carries only the columns search actually
// reads, so the DB query can project them directly and skip the blob.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Models.ProformaInvoice.SavedInvoice
{
    /// <summary>Read-only projection of <see cref="SavedProformaInvoice"/> for search.</summary>
    public sealed class ProformaSearchRow
    {
        public string ProformaId { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
