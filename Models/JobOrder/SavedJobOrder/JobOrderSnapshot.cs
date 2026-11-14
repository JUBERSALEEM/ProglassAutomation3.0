using System;
using ProGlassAutomation.Models.JobOrder.ProductionDetails;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;

namespace ProGlassAutomation.Models.JobOrder.SavedJobOrder
{
    /// <summary>
    /// Root JSON snapshot for a Job Order.
    ///
    /// Structure:
    ///   JobOrderSnapshot
    ///     ├── JobOrderId              (JOB-2026-001)
    ///     ├── SourceProformaId        (PI-2026-001)
    ///     ├── ConvertedAt             (when created from PI)
    ///     ├── EmbeddedInvoice         (full ProformaInvoiceSnapshot tree —
    ///     │                            IMMUTABLE historical record)
    ///     └── Production              (ProductionDetails — MUTABLE)
    ///
    /// Serialized to/from SavedJobOrder.FullSnapshotJson via JobOrderMapper.
    /// No WPF types — pure POCO tree for reliable JSON serialization.
    /// </summary>
    public class JobOrderSnapshot
    {
        public string JobOrderId { get; set; } = string.Empty;
        public string SourceProformaId { get; set; } = string.Empty;
        public DateTime ConvertedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Full copy of the Proforma Invoice at conversion time.
        /// Immutable — if the source PI is later edited, this snapshot
        /// preserves what was signed off originally.
        /// Reuses ProformaInvoiceSnapshot POCO tree — no duplicate schema.
        /// </summary>
        public ProformaInvoiceSnapshot EmbeddedInvoice { get; set; } = new();

        /// <summary>
        /// Production-specific fields (delivery date, priority, notes, etc.)
        /// Mutable — updated as the job progresses through production.
        /// </summary>
        public ProductionDetails.ProductionDetails Production { get; set; } = new();
    }
}