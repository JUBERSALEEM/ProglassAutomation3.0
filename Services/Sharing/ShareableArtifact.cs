// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Sharing/ShareableArtifact.cs
// PROJECT: ProGlassAutomation
// MODULE: Sharing Center — format-agnostic artifact abstraction (Stage 1)
//
// WHAT THIS IS
// ──────────────
// The handoff object between "the app generated a file" and "the user
// shares it". Every export path in the ERP already ends in a file on disk
// (ExportResult.SavedPath, TaxInvoicePdfExporter.SaveToFileAsync,
// StatementPdfExporter.Export*Async returning a path). The Sharing Center
// wraps that path — it NEVER regenerates the document, so the bytes the
// recipient gets are exactly the bytes the exporter produced.
//
// FORMAT-AGNOSTIC BY CONSTRUCTION. Providers receive ShareableArtifact,
// never a PDF/Excel/CSV-specific type — there is no WhatsAppPdfService,
// no EmailExcelService, and there must never be. Only formats the app can
// actually generate are exposed (PDF / XLSX / XLS / CSV today; images and
// text exist in the MIME map for ScreenshotService-class producers).
//
// VALIDATION. FromFile() proves the file exists, is non-empty and is
// readable (a share-lock probe, not a full read — large workbooks must not
// be buffered just to validate). Failures return a reason string, never
// throw, so the composer can show exactly what is wrong.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.IO;

namespace ProGlassAutomation.Services.Sharing
{
    /// <summary>
    /// Formats the ERP can actually generate today. Derived from the file
    /// extension — the artifact does not sniff content.
    /// </summary>
    public enum ShareFormat
    {
        Unknown,
        Pdf,
        Excel,
        Csv
    }

    /// <summary>
    /// One already-generated file, ready to share. Immutable after
    /// validation: the path, size and identity captured here are what the
    /// provider hands off and what the audit trail records.
    /// </summary>
    public sealed class ShareableArtifact
    {
        /// <summary>Full path of the generated file.</summary>
        public string FilePath { get; }

        /// <summary>File name with extension (PI-2026-001.pdf).</summary>
        public string FileName { get; }

        /// <summary>Lower-case extension with dot (.pdf).</summary>
        public string FileExtension { get; }

        /// <summary>MIME type for the extension (application/pdf).</summary>
        public string MimeType { get; }

        /// <summary>ShareFormat derived from the extension.</summary>
        public ShareFormat Format { get; }

        /// <summary>Size in bytes at validation time.</summary>
        public long FileSizeBytes { get; }

        /// <summary>When the file was generated (UTC).</summary>
        public DateTime CreatedAtUtc { get; }

        /// <summary>Document type key: PI, JO, DN, TI, Statement, Report…</summary>
        public string DocumentType { get; }

        /// <summary>Document number: PI-2026-001…</summary>
        public string DocumentNumber { get; }

        /// <summary>Module that produced it (Proforma Invoice…).</summary>
        public string SourceModule { get; }

        /// <summary>
        /// True when the file lives in the share temp folder (generated
        /// for this share) as opposed to a user-owned export the operator
        /// saved earlier. Temp artifacts may be swept; user files are never
        /// deleted by the Sharing Center.
        /// </summary>
        public bool IsTemporary { get; }

        private ShareableArtifact(
            string filePath, string documentType, string documentNumber,
            string sourceModule, bool isTemporary, long sizeBytes,
            DateTime createdAtUtc)
        {
            FilePath = filePath;
            FileName = Path.GetFileName(filePath);
            FileExtension = Path.GetExtension(filePath).ToLowerInvariant();
            MimeType = ShareMimeTypes.ForExtension(FileExtension);
            Format = ShareMimeTypes.FormatForExtension(FileExtension);
            FileSizeBytes = sizeBytes;
            CreatedAtUtc = createdAtUtc;
            DocumentType = documentType ?? "";
            DocumentNumber = documentNumber ?? "";
            SourceModule = sourceModule ?? "";
            IsTemporary = isTemporary;
        }

        /// <summary>
        /// Validates an existing file and wraps it. Returns null with a
        /// reason when the file cannot be shared (missing, empty, locked,
        /// unsupported extension). Never throws.
        /// </summary>
        public static ShareableArtifact? FromFile(
            string? filePath,
            string documentType,
            string documentNumber,
            string sourceModule,
            bool isTemporary,
            out string? failureReason)
        {
            failureReason = null;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                failureReason = "No file was generated to share.";
                return null;
            }

            string full;
            try { full = Path.GetFullPath(filePath); }
            catch (Exception ex)
            {
                failureReason = $"The file path is not valid: {ex.Message}";
                return null;
            }

            string ext = Path.GetExtension(full).ToLowerInvariant();
            if (!ShareMimeTypes.IsSupported(ext))
            {
                failureReason =
                    $"Files of type '{ext}' cannot be shared. " +
                    "Share a PDF, Excel or CSV export.";
                return null;
            }

            FileInfo info;
            try { info = new FileInfo(full); }
            catch (Exception ex)
            {
                failureReason = $"The file cannot be accessed: {ex.Message}";
                return null;
            }

            if (!info.Exists)
            {
                failureReason =
                    $"The file '{Path.GetFileName(full)}' no longer exists. " +
                    "Generate it again, then share.";
                return null;
            }

            if (info.Length <= 0)
            {
                failureReason =
                    $"The file '{info.Name}' is empty. " +
                    "Generate it again, then share.";
                return null;
            }

            // Share-lock probe: proves readability without buffering the
            // file. Large workbooks must not be read into memory here.
            try
            {
                using var probe = new FileStream(
                    full, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (IOException)
            {
                failureReason =
                    $"The file '{info.Name}' is locked by another program. " +
                    "Close it there and try again.";
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                failureReason =
                    $"The file '{info.Name}' cannot be read (access denied).";
                return null;
            }
            catch (Exception ex)
            {
                failureReason = $"The file cannot be read: {ex.Message}";
                return null;
            }

            DateTime createdUtc;
            try { createdUtc = info.CreationTimeUtc; }
            catch { createdUtc = DateTime.UtcNow; }

            return new ShareableArtifact(
                full, documentType, documentNumber, sourceModule,
                isTemporary, info.Length, createdUtc);
        }

        /// <summary>"PI-2026-001.pdf · 84 KB" for composer/audit lines.</summary>
        public string DisplaySummary =>
            $"{FileName} · {FormatSize(FileSizeBytes)}";

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0} KB";
            return $"{bytes / (1024.0 * 1024.0):0.0} MB";
        }
    }
}
