// ═══════════════════════════════════════════════════════════════════════
// FILE: Models/ProformaInvoice/Attachments/ProformaAttachmentRecord.cs
// PROJECT: ProGlassAutomation
// MODULE: Proforma Invoice — Step 8 Attachments
//
// Pure POCO describing one attached file (image or PDF). Records live
// in a per-invoice JSON manifest on disk — NOT in the EF database.
//
// WHY A MANIFEST INSTEAD OF A DB TABLE:
//   proformainvoices.db already ships to users. EF EnsureCreated does
//   not add new tables to an existing database (only migrations do),
//   and this project uses no migration pipeline. A manifest file is
//   100% backward compatible: old invoices simply have no manifest
//   (= zero attachments) and nothing breaks.
// ═══════════════════════════════════════════════════════════════════════

using System;

namespace ProGlassAutomation.Models.ProformaInvoice.Attachments
{
    /// <summary>
    /// One file attached to a proforma invoice.
    /// Serialized into %AppData%\ProGlassAutomation\ProformaInvoices\
    /// Attachments\{ProformaId}\manifest.json
    /// </summary>
    public class ProformaAttachmentRecord
    {
        /// <summary>Stable id (GUID hex) — used as delete/rename key.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>Parent invoice reference, e.g. "PI-2026-001".</summary>
        public string ProformaId { get; set; } = "";

        /// <summary>"Image" or "Pdf" (same constants as DN attachments).</summary>
        public string AttachmentType { get; set; } = "Image";

        /// <summary>File name inside the type folder.</summary>
        public string FileName { get; set; } = "";

        /// <summary>Absolute path on disk.</summary>
        public string FilePath { get; set; } = "";

        /// <summary>Size in bytes.</summary>
        public long FileSize { get; set; }

        /// <summary>Optional user description.</summary>
        public string Description { get; set; } = "";

        /// <summary>Upload timestamp (local time, matches DN behaviour).</summary>
        public DateTime UploadedAt { get; set; } = DateTime.Now;
    }
}
