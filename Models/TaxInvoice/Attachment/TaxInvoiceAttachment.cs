using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProGlassAutomation.Models.TaxInvoice.Attachment
{
    /// <summary>
    /// Tracks one attached document on a Tax Invoice.
    ///
    /// STORAGE STRATEGY — byte[] in DB (not file path):
    ///   DN module uses file path storage (disk-based).
    ///   Tax Invoice upgrades to byte[] for full portability:
    ///     • No broken paths when app folder moves
    ///     • Single DB file = single backup
    ///     • Statement PDF generator can read bytes directly
    ///       without any disk I/O
    ///
    /// ATTACHMENT TYPES:
    ///   SignedPI  — signed/stamped Proforma Invoice PDF
    ///   SignedDN  — signed/stamped Delivery Note PDF
    ///   Other     — any other supporting document
    ///
    /// DOWNSTREAM USAGE (Statement PDF — future module):
    ///   Statement generator loads all attachments via
    ///   TaxInvoiceRepository.GetAttachmentsAsync(invoiceNumber),
    ///   decodes FileBytes to byte[], appends as QuestPDF pages.
    ///   Append order: Invoice → SignedPI → SignedDN → Other.
    ///
    /// SIZE LIMIT:
    ///   Recommended max per attachment: 10 MB.
    ///   Validated at VM level before insert — not enforced at DB level.
    ///   SQLite handles blobs well up to ~50 MB per row.
    ///
    /// EF relationship: SavedTaxInvoice (1) → TaxInvoiceAttachment (many).
    /// Cascade delete configured in TaxInvoiceDbContext.
    ///
    /// UploadedAt set explicitly by repository — never defaulted at
    /// property level (same BUG-11 / BUG-09 fix applied to DN).
    /// </summary>
    [Table("TaxInvoiceAttachments")]
    public class TaxInvoiceAttachment
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Parent invoice number, e.g. "INV-2026-001".
        /// FK to SavedTaxInvoice.InvoiceNumber via HasPrincipalKey.
        /// </summary>
        [Required, MaxLength(60)]
        [ForeignKey(nameof(TaxInvoice))]
        public string InvoiceNumber { get; set; } = "";

        /// <summary>
        /// Navigation property back to parent invoice.
        /// Not loaded by default — use Include() when needed.
        /// </summary>
        public SavedTaxInvoice.SavedTaxInvoice? TaxInvoice { get; set; }

        // ── Type + identification ─────────────────────────────────────────

        /// <summary>
        /// "SignedPI" | "SignedDN" | "Other"
        /// Used by Statement PDF generator to determine append order:
        ///   Invoice content first → SignedPI → SignedDN → Other
        /// </summary>
        [Required, MaxLength(20)]
        public string AttachmentType { get; set; } = "Other";

        /// <summary>Original filename, e.g. "Signed_PI-2026-001.pdf".</summary>
        [Required, MaxLength(260)]
        public string FileName { get; set; } = "";

        /// <summary>
        /// MIME type for correct rendering.
        /// e.g. "application/pdf", "image/jpeg", "image/png"
        /// </summary>
        [MaxLength(100)]
        public string MimeType { get; set; } = "application/pdf";

        // ── File content ──────────────────────────────────────────────────

        /// <summary>
        /// Raw file bytes stored directly in DB.
        /// NULL = file not yet uploaded or reference-only attachment.
        /// Statement PDF generator reads this directly — no disk I/O.
        /// </summary>
        public byte[]? FileBytes { get; set; }

        /// <summary>
        /// File size in bytes — stored separately for fast display
        /// without loading FileBytes (which can be large).
        /// </summary>
        public long FileSize { get; set; }

        // ── Metadata ──────────────────────────────────────────────────────

        /// <summary>Optional user-entered description or notes.</summary>
        [MaxLength(500)]
        public string Description { get; set; } = "";

        /// <summary>
        /// Timestamp when file was attached.
        /// Set explicitly by repository in AddAttachmentAsync.
        /// Never defaulted at property level (BUG-11 fix).
        /// </summary>
        public DateTime UploadedAt { get; set; }

        // ── Computed helpers (not mapped) ─────────────────────────────────

        /// <summary>
        /// Human-readable file size string.
        /// e.g. "2.4 MB", "856 KB", "12 KB"
        /// Used in attachment list UI — never stored in DB.
        /// </summary>
        [NotMapped]
        public string FileSizeDisplay
        {
            get
            {
                if (FileSize <= 0) return "0 KB";
                if (FileSize >= 1_048_576)
                    return $"{FileSize / 1_048_576.0:0.#} MB";
                if (FileSize >= 1_024)
                    return $"{FileSize / 1_024.0:0.#} KB";
                return $"{FileSize} B";
            }
        }

        /// <summary>
        /// True when FileBytes is populated.
        /// False = attachment was registered but bytes not yet uploaded.
        /// </summary>
        [NotMapped]
        public bool HasContent => FileBytes != null && FileBytes.Length > 0;

        /// <summary>
        /// Icon string for attachment type display in UI.
        /// </summary>
        [NotMapped]
        public string TypeIcon => AttachmentType switch
        {
            "SignedPI" => "📄",
            "SignedDN" => "🚚",
            _ => "📎"
        };

        /// <summary>
        /// Display label for attachment type.
        /// </summary>
        [NotMapped]
        public string TypeLabel => AttachmentType switch
        {
            "SignedPI" => "Signed PI",
            "SignedDN" => "Signed DN",
            _ => "Document"
        };
    }
}