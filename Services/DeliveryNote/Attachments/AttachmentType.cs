namespace ProGlassAutomation.Services.DeliveryNote.Attachments
{
    /// <summary>
    /// Type of file attached to a delivery note.
    /// Stored as string in DB (matches DeliveryNoteAttachment.AttachmentType).
    /// </summary>
    public static class AttachmentType
    {
        public const string Image = "Image";
        public const string Pdf = "Pdf";
    }

    /// <summary>
    /// Central constants for attachment upload rules.
    /// </summary>
    public static class AttachmentLimits
    {
        /// <summary>10 MB per file.</summary>
        public const long MaxFileSizeBytes = 10 * 1024 * 1024;

        public const string MaxFileSizeDisplay = "10 MB";

        /// <summary>Accepted image extensions (lowercase, with dot).</summary>
        public static readonly string[] ImageExtensions =
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"
        };

        /// <summary>Accepted PDF extensions.</summary>
        public static readonly string[] PdfExtensions = { ".pdf" };

        /// <summary>WPF file dialog filter string for images.</summary>
        public const string ImageDialogFilter =
            "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|All files|*.*";

        /// <summary>WPF file dialog filter string for PDFs.</summary>
        public const string PdfDialogFilter =
            "PDF files|*.pdf|All files|*.*";

        /// <summary>
        /// WPF file dialog filter accepting BOTH images and PDFs — the single
        /// Approve-Invoice picker (Step 8). The type of each chosen file is
        /// auto-detected from its extension afterwards, so the user never has
        /// to pre-answer "image or PDF?" (2026-09-10 unification).
        /// </summary>
        public const string CombinedDialogFilter =
            "Confirmation document (image or PDF)|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.pdf|All files|*.*";
    }
}