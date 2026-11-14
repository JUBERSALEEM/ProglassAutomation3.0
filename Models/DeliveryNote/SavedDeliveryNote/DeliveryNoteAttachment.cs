using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProGlassAutomation.Models.DeliveryNote.SavedDeliveryNote
{
    /// <summary>
    /// Tracks one uploaded image or PDF file attached to a delivery note.
    /// File is stored on disk; path reference stored in DB.
    ///
    /// Storage structure:
    ///   %AppData%\ProGlassAutomation\DeliveryNotes\Attachments\
    ///     Images\{DeliveryNoteId}\{filename}
    ///     PDFs\{DeliveryNoteId}\{filename}
    ///
    /// EF relationship: SavedDeliveryNote (1) → DeliveryNoteAttachment (many).
    /// Cascade delete configured in DeliveryNotesDbContext.
    /// BUG-03: Added ForeignKey attribute + navigation property for
    ///         EF Core relationship clarity.
    /// BUG-11: UploadedAt no longer defaults to DateTime.Now at property
    ///         level — set explicitly by AttachmentService/repository only.
    /// </summary>
    [Table("DeliveryNoteAttachments")]
    public class DeliveryNoteAttachment
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Parent DN reference, e.g. "DN-2026-001-01".
        /// FK to SavedDeliveryNote.DeliveryNoteId (string PK via HasPrincipalKey).
        /// </summary>
        [Required, MaxLength(50)]
        [ForeignKey(nameof(DeliveryNote))]
        public string DeliveryNoteId { get; set; } = "";

        /// <summary>
        /// Navigation property back to parent DN.
        /// Not loaded by default — use Include() when needed.
        /// </summary>
        public SavedDeliveryNote? DeliveryNote { get; set; }

        /// <summary>"Image" or "Pdf" — matches AttachmentType constants.</summary>
        [Required, MaxLength(20)]
        public string AttachmentType { get; set; } = "Image";

        /// <summary>Original filename, e.g. "photo1.jpg".</summary>
        [Required, MaxLength(260)]
        public string FileName { get; set; } = "";

        /// <summary>Full path on disk where the file is stored.</summary>
        [Required, MaxLength(500)]
        public string FilePath { get; set; } = "";

        /// <summary>File size in bytes.</summary>
        public long FileSize { get; set; }

        /// <summary>Optional user-entered description.</summary>
        [MaxLength(500)]
        public string Description { get; set; } = "";

        /// <summary>
        /// Timestamp when file was uploaded.
        /// Set explicitly by AttachmentService/repository.
        /// Not defaulted at property level to avoid EF
        /// materialization interference (BUG-11 fix).
        /// </summary>
        public DateTime UploadedAt { get; set; }
    }
}