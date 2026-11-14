// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/ProformaInvoice/Attachments/ProformaAttachmentService.cs
// PROJECT: ProGlassAutomation
// MODULE: Proforma Invoice — Step 8 Attachments
//
// Disk I/O service for proforma invoice attachments. Modelled 1:1 on
// the proven Delivery Note AttachmentService
// (Services/DeliveryNote/Attachments/AttachmentService.cs) so the two
// sections behave identically: same limits, same file-name handling,
// same copy/delete semantics.
//
// STORAGE LAYOUT
//   %AppData%\ProGlassAutomation\ProformaInvoices\Attachments\
//     {ProformaId}\
//       manifest.json      ← List<ProformaAttachmentRecord>
//       Images\{filename}
//       PDFs\{filename}
//
// The manifest makes attachments survive without any EF schema change
// (see ProformaAttachmentRecord.cs for the reasoning).
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.ProformaInvoice.Attachments;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Services.Security;
using ProGlassAutomation.Services.Common;
using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.DeliveryNote.Attachments;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.ProformaInvoice.Attachments
{
    /// <summary>
    /// Disk service for proforma attachments (copy / delete / open /
    /// rename + manifest persistence). Thread-safe per-call; one
    /// instance per ViewModel is the intended lifetime.
    /// </summary>
    public class ProformaAttachmentService
    {
        private readonly string _rootFolder;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public ProformaAttachmentService()
        {
            _rootFolder = Path.Combine(
                ProGlassAutomation.Services.Database.AppDataRoot.CurrentFolder,
                "ProformaInvoices",
                "Attachments");

            Directory.CreateDirectory(_rootFolder);
        }

        // ══════════════════════════════════════════════════════════════
        //  VALIDATION (delegates to shared DN limits — single source)
        // ══════════════════════════════════════════════════════════════

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

        // ══════════════════════════════════════════════════════════════
        //  COPY (upload)
        // ══════════════════════════════════════════════════════════════

        /// <summary>Copies an image into the invoice folder + manifest.</summary>
        public async Task<ProformaAttachmentRecord> CopyImageAsync(
            string sourcePath, string proformaId)
        {
            return await CopyFileAsync(
                sourcePath, proformaId, "Images",
                AttachmentType.Image);
        }

        /// <summary>Copies a PDF into the invoice folder + manifest.</summary>
        public async Task<ProformaAttachmentRecord> CopyPdfAsync(
            string sourcePath, string proformaId)
        {
            return await CopyFileAsync(
                sourcePath, proformaId, "PDFs",
                AttachmentType.Pdf);
        }

        private async Task<ProformaAttachmentRecord> CopyFileAsync(
            string sourcePath,
            string proformaId,
            string typeFolderName,
            string attachmentType)
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException(
                    $"Source file not found: {sourcePath}");

            string invoiceFolder = InvoiceFolder(proformaId);
            string destFolder = Path.Combine(invoiceFolder, typeFolderName);
            Directory.CreateDirectory(destFolder);

            string finalName = SanitizeName(Path.GetFileName(sourcePath));
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

            var record = new ProformaAttachmentRecord
            {
                ProformaId = proformaId,
                AttachmentType = attachmentType,
                FileName = Path.GetFileName(destPath),
                FilePath = destPath,
                FileSize = info.Length,
                UploadedAt = DateTime.Now
            };

            // Persist immediately so no upload is ever lost.
            var manifest = await LoadManifestAsync(proformaId);
            manifest.Add(record);
            await SaveManifestAsync(proformaId, manifest);

            return record;
        }

        // ══════════════════════════════════════════════════════════════
        //  DELETE / RENAME
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Removes the record from the manifest and deletes the file.
        /// File deletion is best-effort (same BUG-27 semantics as DN).
        /// </summary>
        public async Task<bool> DeleteAsync(
            string proformaId, string recordId)
        {
            // A-1: deleting an attachment destroys a document that may be
            // the only copy of a signed drawing or approval. Neither the
            // UI nor this service checked anything before now.
            OperationGuard.Demand(
                AppPermissions.ProformaEdit, "delete a proforma attachment");

            var manifest = await LoadManifestAsync(proformaId);
            var record = manifest.FirstOrDefault(r => r.Id == recordId);
            if (record == null) return false;

            manifest.Remove(record);
            await SaveManifestAsync(proformaId, manifest);
            return DeleteFile(record.FilePath);
        }

        /// <summary>
        /// Removes EVERY attachment for the invoice: deletes each file
        /// from disk, deletes the manifest and prunes the invoice folder
        /// (Images / PDFs / root) when empty.
        ///
        /// Used when an invoice is cancelled / rejected or permanently
        /// deleted, so a later re-approval (or a re-created invoice with
        /// the same Proforma ID) starts with a clean Step 8. Mirrors the
        /// downstream cleanup a Job Order delete performs on its master
        /// delivery notes.
        ///
        /// NO permission demand of its own, by design: this method is only
        /// invoked from flows that are ALREADY authorized (PI status
        /// change, PI delete, or lifecycle sync after a JO / DN / TI
        /// delete — the Production role can delete a Job Order without
        /// holding ProformaEdit, so demanding it here would silently
        /// block legitimate cleanup). File deletion is best-effort with
        /// the same BUG-27 semantics as <see cref="DeleteFile"/>: locked
        /// files are left behind, never thrown.
        /// </summary>
        /// <returns>The number of attachment files actually deleted.</returns>
        public async Task<int> ClearAllAsync(string proformaId)
        {
            var manifest = await LoadManifestAsync(proformaId);
            int cleared = 0;

            foreach (var record in manifest)
            {
                if (DeleteFile(record.FilePath))
                    cleared++;
            }

            // Remove the manifest, then prune empty folders. Any leftovers
            // (locked files) keep their folder — same best-effort rule as
            // per-file deletion.
            try
            {
                string manifestPath = ManifestPath(proformaId);
                if (File.Exists(manifestPath))
                    File.Delete(manifestPath);

                string invoiceFolder = InvoiceFolder(proformaId);

                foreach (var sub in new[] { "Images", "PDFs" })
                {
                    string dir = Path.Combine(invoiceFolder, sub);
                    if (Directory.Exists(dir) &&
                        !Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir);
                    }
                }

                if (Directory.Exists(invoiceFolder) &&
                    !Directory.EnumerateFileSystemEntries(invoiceFolder).Any())
                {
                    Directory.Delete(invoiceFolder);
                }
            }
            catch (System.Exception ex)
            {
                // Best-effort pruning only; the manifest may already be gone.
                ProGlassAutomation.Services.Logging.AppLogService.Debug("[ProformaAttachmentService] Manifest pruning failed: " + ex.Message);
            }

            return cleared;
        }

        /// <summary>Renames the file on disk + in the manifest.</summary>
        public async Task<bool> RenameAsync(
            string proformaId, string recordId, string newName)
        {
            // A-1: renaming moves the file on disk, so a failure or a
            // hostile name change is a mutation, not a display tweak.
            OperationGuard.Demand(
                AppPermissions.ProformaEdit, "rename a proforma attachment");

            var manifest = await LoadManifestAsync(proformaId);
            var record = manifest.FirstOrDefault(r => r.Id == recordId);
            if (record == null) return false;

            string ext = Path.GetExtension(record.FileName);
            string candidate = SanitizeName(newName);

            if (!Path.HasExtension(candidate))
                candidate += ext;

            string folder = Path.GetDirectoryName(record.FilePath)!;
            string newPath = ResolveUniquePath(folder, candidate);

            try
            {
                File.Move(record.FilePath, newPath);
            }
            catch
            {
                return false;   // target locked / access denied
            }

            record.FilePath = newPath;
            record.FileName = Path.GetFileName(newPath);
            await SaveManifestAsync(proformaId, manifest);
            return true;
        }

        // ══════════════════════════════════════════════════════════════
        //  MANIFEST
        // ══════════════════════════════════════════════════════════════

        /// <summary>Loads the invoice's attachment list (empty if none).</summary>
        public async Task<List<ProformaAttachmentRecord>> LoadManifestAsync(
            string proformaId)
        {
            string path = ManifestPath(proformaId);

            try
            {
                if (!File.Exists(path))
                    return new List<ProformaAttachmentRecord>();

                string json = await File.ReadAllTextAsync(path);

                if (string.IsNullOrWhiteSpace(json))
                    return new List<ProformaAttachmentRecord>();

                return JsonSerializer
                    .Deserialize<List<ProformaAttachmentRecord>>(
                        json, _jsonOptions)
                    ?? new List<ProformaAttachmentRecord>();
            }
            catch (JsonException ex)
            {
                AppLogService.Debug(
                    $"[ProformaAttachments] Corrupt manifest for " +
                    $"{proformaId}: {ex.Message}");
                return new List<ProformaAttachmentRecord>();
            }
        }

        private async Task SaveManifestAsync(
            string proformaId, List<ProformaAttachmentRecord> records)
        {
            string path = ManifestPath(proformaId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            string json = JsonSerializer.Serialize(records, _jsonOptions);
            await File.WriteAllTextAsync(path, json);
        }

        // ══════════════════════════════════════════════════════════════
        //  OPEN IN DEFAULT APP
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Opens an attachment with the OS default handler.
        ///
        /// SECURITY (S-9): identical reasoning to the delivery-note
        /// service. UseShellExecute = true EXECUTES anything Windows has a
        /// handler for, so a stored path ending in .exe / .bat / .ps1 /
        /// .lnk would run rather than open. The manifest that supplies
        /// FilePath is a plain JSON file in %AppData% and is therefore not
        /// a trust boundary. The extension allow-list is re-checked at the
        /// point of launch, where it cannot be bypassed.
        /// </summary>
        public void OpenInDefaultApp(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException(
                    "No file path supplied.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            bool allowed =
                AttachmentLimits.ImageExtensions.Contains(ext) ||
                AttachmentLimits.PdfExtensions.Contains(ext);

            if (!allowed)
            {
                throw new InvalidOperationException(
                    $"Refusing to open '{Path.GetFileName(filePath)}': " +
                    "only image and PDF attachments can be opened.");
            }

                        var corrId = CorrelationContext.NewIdFor("ProformaInvoice", "OpenAttachment");
            using var scope = CorrelationContext.BeginScope(corrId);
            var result = ProcessStartGuard.TryOpenFile(filePath, corrId);
            if (result.IsFailure) throw new InvalidOperationException(result.Error);
        }

        // ══════════════════════════════════════════════════════════════
        //  PATH HELPERS
        // ══════════════════════════════════════════════════════════════

        private string InvoiceFolder(string proformaId) =>
            Path.Combine(_rootFolder, SanitizeName(proformaId));

        private string ManifestPath(string proformaId) =>
            Path.Combine(InvoiceFolder(proformaId), "manifest.json");

        /// <summary>Shared file-name sanitizer (same rule as DN service).</summary>
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

        /// <summary>Collision-safe path with a fast GUID fallback.</summary>
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

            return Path.Combine(folder, $"{Guid.NewGuid():N}{ext}");
        }

        /// <summary>Best-effort file delete (locked files are left behind).</summary>
        public bool DeleteFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                File.Delete(filePath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Human-friendly file size (shared with DN display).</summary>
        public static string FormatFileSize(long bytes) =>
            AttachmentService.FormatFileSize(bytes);
    }
}
