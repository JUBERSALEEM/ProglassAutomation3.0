using ProGlassAutomation.Models.DeliveryNote.SavedDeliveryNote;
using ProGlassAutomation.Services.Common;
using ProGlassAutomation.Services.Security;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.DeliveryNote.Attachments
{
    /// <summary>
    /// Disk I/O service for DN attachments.
    ///
    /// Files stored under:
    ///   %AppData%\ProGlassAutomation\DeliveryNotes\Attachments\
    ///     Images\{DeliveryNoteId}\{filename}
    ///     PDFs\{DeliveryNoteId}\{filename}
    ///
    /// BUG-26: SanitizeFolderName + SanitizeFileName were identical —
    ///         merged into one shared SanitizeName helper.
    /// BUG-27: DeleteFile now returns bool — caller knows if file was
    ///         actually deleted or was missing/locked.
    /// BUG-28: ResolveUniquePath is synchronous disk I/O — kept as-is
    ///         since it is called from async context, but loop limit
    ///         reduced to 1000 and GUID fallback is immediate safety net.
    ///
    /// Phase 10.6a: CopyImage/CopyPdf accept optional customFileName.
    /// </summary>
    public class AttachmentService
    {
        private readonly string _rootFolder;
        private readonly string _imagesFolder;
        private readonly string _pdfsFolder;

        public AttachmentService()
        {
            _rootFolder = Path.Combine(
                ProGlassAutomation.Services.Database.AppDataRoot.CurrentFolder,
                "DeliveryNotes",
                "Attachments");

            _imagesFolder = Path.Combine(_rootFolder, "Images");
            _pdfsFolder = Path.Combine(_rootFolder, "PDFs");

            Directory.CreateDirectory(_imagesFolder);
            Directory.CreateDirectory(_pdfsFolder);
        }

        // ═════════════════════════════════════════════════════════════════
        //  VALIDATION
        // ═════════════════════════════════════════════════════════════════

        public bool IsValidImageExtension(string sourcePath)
        {
            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            return AttachmentLimits.ImageExtensions.Contains(ext);
        }

        public bool IsValidPdfExtension(string sourcePath)
        {
            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            return AttachmentLimits.PdfExtensions.Contains(ext);
        }

        public bool IsUnderSizeLimit(string sourcePath)
        {
            var info = new FileInfo(sourcePath);
            return info.Length <= AttachmentLimits.MaxFileSizeBytes;
        }

        // ═════════════════════════════════════════════════════════════════
        //  COPY (upload)
        // ═════════════════════════════════════════════════════════════════

        public async Task<DeliveryNoteAttachment> CopyImageAsync(
            string sourcePath,
            string deliveryNoteId,
            string? customFileName = null)
        {
            return await CopyFileAsync(
                sourcePath, deliveryNoteId, customFileName,
                _imagesFolder, AttachmentType.Image);
        }

        public async Task<DeliveryNoteAttachment> CopyPdfAsync(
            string sourcePath,
            string deliveryNoteId,
            string? customFileName = null)
        {
            return await CopyFileAsync(
                sourcePath, deliveryNoteId, customFileName,
                _pdfsFolder, AttachmentType.Pdf);
        }

        private async Task<DeliveryNoteAttachment> CopyFileAsync(
            string sourcePath,
            string deliveryNoteId,
            string? customFileName,
            string typeFolder,
            string attachmentType)
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException(
                    $"Source file not found: {sourcePath}");

            // BUG-26: use shared SanitizeName instead of duplicate methods
            string safeDnId = SanitizeName(deliveryNoteId);
            string destFolder = Path.Combine(typeFolder, safeDnId);
            Directory.CreateDirectory(destFolder);

            string finalName;
            if (!string.IsNullOrWhiteSpace(customFileName))
            {
                var customExt = Path.GetExtension(customFileName);
                if (string.IsNullOrWhiteSpace(customExt))
                {
                    var srcExt = Path.GetExtension(sourcePath);
                    finalName = customFileName + srcExt;
                }
                else
                {
                    finalName = customFileName;
                }
            }
            else
            {
                finalName = Path.GetFileName(sourcePath);
            }

            finalName = SanitizeName(finalName);  // BUG-26: shared helper

            string destPath = ResolveUniquePath(destFolder, finalName);

            using (var src = new FileStream(
                       sourcePath, FileMode.Open, FileAccess.Read,
                       FileShare.Read, 81920, useAsync: true))
            using (var dst = new FileStream(
                       destPath, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 81920, useAsync: true))
            {
                await src.CopyToAsync(dst);
            }

            var info = new FileInfo(destPath);

            return new DeliveryNoteAttachment
            {
                DeliveryNoteId = deliveryNoteId,
                AttachmentType = attachmentType,
                FileName = Path.GetFileName(destPath),
                FilePath = destPath,
                FileSize = info.Length,
                UploadedAt = DateTime.Now
            };
        }

        // BUG-28: reduced loop to 1_000, GUID fallback is immediate safety net
        private static string ResolveUniquePath(string folder, string fileName)
        {
            string candidate = Path.Combine(folder, fileName);
            if (!File.Exists(candidate)) return candidate;

            string nameOnly = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);

            for (int i = 1; i < 1_000; i++)
            {
                string next = Path.Combine(folder, $"{nameOnly}-{i}{ext}");
                if (!File.Exists(next)) return next;
            }

            // Immediate GUID fallback — avoids 10,000 disk I/O calls
            return Path.Combine(folder, $"{Guid.NewGuid():N}{ext}");
        }

        // BUG-26: single shared sanitize helper (was two identical methods)
        private static string SanitizeName(string name)
        {
            // S-8: hardened against path traversal.
            //
            // The previous rule replaced Path.GetInvalidFileNameChars() only.
            // '.' is NOT in that set, so ".." passed through untouched. This
            // matters because the value is used as a PATH SEGMENT
            // (Path.Combine(_rootFolder, SanitizeName(proformaId))), and
            // Path.Combine happily resolves "..", so a Proforma ID of ".."
            // or "..\..\x" walked out of the attachments folder. The only
            // validation on Proforma ID is a non-empty check, so the value
            // is effectively free text from the Step 1 TextBox.
            //
            // Rules, in order:
            //   1. null/blank -> a safe constant, never an empty segment.
            //   2. strip directory separators and invalid chars.
            //   3. reject a segment that is entirely dots ("." / ".." / ...).
            //   4. cap length so a long name cannot break MAX_PATH.
            if (string.IsNullOrWhiteSpace(name))
                return "_";

            var invalid = Path.GetInvalidFileNameChars();
            var chars = name
                .Select(c => invalid.Contains(c)
                             || c == Path.DirectorySeparatorChar
                             || c == Path.AltDirectorySeparatorChar
                    ? '_'
                    : c)
                .ToArray();

            string safe = new string(chars).Trim();

            // "." and ".." are valid filenames to the char filter but are
            // directory navigation to Path.Combine. Anything that is only
            // dots is replaced outright rather than trimmed, so "..." cannot
            // degrade into "." either.
            if (safe.Length == 0 || safe.All(c => c == '.'))
                return "_";

            // Trailing dots and spaces are silently dropped by Windows,
            // which would make two distinct IDs collide on one folder.
            safe = safe.TrimEnd('.', ' ');
            if (safe.Length == 0) return "_";

            const int MaxSegment = 120;
            return safe.Length <= MaxSegment ? safe : safe[..MaxSegment];
        }

        // ═════════════════════════════════════════════════════════════════
        //  DELETE
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Deletes the file at the given path from disk.
        /// BUG-27: Now returns bool — true if deleted, false if missing or locked.
        /// DB row deletion is authoritative; this is best-effort file cleanup.
        /// </summary>
        public bool DeleteFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                File.Delete(filePath);
                return true;
            }
            catch (Exception)
            {
                // File locked or access denied — DB row still deleted.
                // Orphan file can be cleaned up manually if needed.
                return false;
            }
        }

        // ═════════════════════════════════════════════════════════════════
        //  OPEN IN DEFAULT APP
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Opens an attachment with the OS default handler (async — await this).
        ///
        /// SECURITY (S-9 + Phase 8): UseShellExecute = true asks Windows to run the
        /// file with whatever handler is registered for its extension. For
        /// a .pdf or .png that is a viewer; for a .exe, .bat, .cmd, .ps1,
        /// .js, .lnk or .scr it is EXECUTION with the user's privileges.
        ///
        /// The upload path validates extensions, but this method is public
        /// and is called with FilePath read back out of the database. A row
        /// written by an older build, a hand-edited database, a restored
        /// backup, or any future caller that forgets to validate would
        /// therefore be enough to launch an arbitrary binary from a click
        /// in the attachments list.
        ///
        /// The allow-list is re-applied HERE, at the moment of launch,
        /// because that is the only place the guarantee cannot be bypassed
        /// by a caller. Anything not an approved image or PDF is refused.
        ///
        /// Phase 8: Now uses ProcessStartGuard.TryOpenFile with full validation:
        ///   - Path inside allowed root (AppData\ProGlassAutomation)
        ///   - No traversal, no UNC, no ADS
        ///   - Extension allow-list + MIME sniffing via AttachmentValidationService
        ///   - Size cap double-checked
        ///   - CorrelationId + ErrorId for support
        ///
        /// The legacy synchronous overloads (OpenInDefaultApp /
        /// OpenInDefaultAppWithResult) were removed 2026-09-06: the only
        /// caller already awaited this async form, and the sync forms
        /// blocked the UI thread via GetAwaiter().GetResult().
        /// </summary>
        public async Task<Result> OpenInDefaultAppAsync(
            string filePath, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewIdFor("DeliveryNote", "OpenAttachment");
            using var scope = CorrelationContext.BeginScope(corrId);

            var validationResult = await AttachmentValidationService
                .ValidateAttachmentAsync(filePath, corrId)
                .ConfigureAwait(true);   // resume on UI thread: ShellExecute wants it

            if (validationResult.IsFailure)
                return Result.Fail(validationResult.Error, corrId, null, validationResult.ErrorId);

            return ProcessStartGuard.TryOpenFile(filePath, corrId);
        }

        // ═════════════════════════════════════════════════════════════════
        //  HELPERS
        // ═════════════════════════════════════════════════════════════════

        public static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
            return $"{bytes / (1024.0 * 1024.0):0.##} MB";
        }
    }
}