// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Inquiry/InquiryAttachmentService.cs
// MODULE: Inquiry — disk + manifest attachment service (ported).
//
// STORAGE LAYOUT:
//   {AppDataRoot.CurrentFolder}/Inquiries/{InquiryId}/Attachments/
//     manifest.json                ← List<InquiryAttachment> (source of
//                                    truth for the file list)
//     {guid}_{fileName}            ← the files
//
// WHY A MANIFEST: attachment persistence writes the manifest first and
// mirrors to the InquiryAttachments EF table best-effort, so a drifted
// child table can never take an upload down with it. The mirror exists
// so the register's file-count column keeps working on healthy
// databases; mirror failures are ignored.
//
// The first Get/Add/Delete call for an inquiry backfills manifest.json
// from any legacy EF rows and then never reads them again.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Inquiry;
using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.Database;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Inquiry
{
    public sealed class InquiryAttachmentService
    {
        private readonly IInquiryRepository _repository;
        public InquiryAttachmentService(IInquiryRepository repository) => _repository = repository;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// All accepted extensions (lowercase, with dot). Matches the
        /// customer-website intake set (pdf/images/csv/xlsx/zip/txt) so
        /// WhatsApp-style manual attachments and web-ingested documents
        /// share one rule.
        /// </summary>
        public static readonly string[] AllowedExtensions =
        {
            ".pdf", ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp",
            ".dwg", ".dxf", ".xlsx", ".xls", ".csv", ".zip", ".txt"
        };

        public static bool IsAllowedExtension(string extension) =>
            AllowedExtensions.Contains((extension ?? "").ToLowerInvariant());

        public static string StorageFolder(int inquiryId) => Path.Combine(
            AppDataRoot.CurrentFolder, "Inquiries", inquiryId.ToString(), "Attachments");

        private static string ManifestPath(int inquiryId) =>
            Path.Combine(StorageFolder(inquiryId), "manifest.json");

        // ══════════════════════════════════════════════════════════════
        //  READ
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// The attachments for an inquiry, from the manifest. The first
        /// call for a legacy inquiry migrates any EF rows into the
        /// manifest.
        /// </summary>
        public async Task<IReadOnlyList<InquiryAttachment>> GetAsync(int inquiryId, CancellationToken ct = default)
        {
            if (!File.Exists(ManifestPath(inquiryId)))
            {
                var legacy = await TryLoadLegacyRowsAsync(inquiryId, ct).ConfigureAwait(false);
                if (legacy.Count > 0)
                {
                    await SaveManifestAsync(inquiryId, legacy).ConfigureAwait(false);
                    return legacy;
                }
            }
            return await LoadManifestAsync(inquiryId).ConfigureAwait(false);
        }

        private async Task<List<InquiryAttachment>> TryLoadLegacyRowsAsync(int inquiryId, CancellationToken ct)
        {
            try
            {
                var rows = await _repository.GetAttachmentsAsync(inquiryId, ct).ConfigureAwait(false);
                var result = new List<InquiryAttachment>();
                foreach (var row in rows)
                {
                    string? resolved = ResolvePath(row);
                    if (resolved == null) continue;   // file gone — row is unusable
                    row.StoragePath = resolved;
                    result.Add(row);
                }
                return result;
            }
            catch
            {
                // A drifted/unreadable legacy table must never block
                // the module.
                return new List<InquiryAttachment>();
            }
        }

        private static async Task<List<InquiryAttachment>> LoadManifestAsync(int inquiryId)
        {
            string path = ManifestPath(inquiryId);
            try
            {
                if (!File.Exists(path)) return new List<InquiryAttachment>();
                string json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json)) return new List<InquiryAttachment>();
                return JsonSerializer.Deserialize<List<InquiryAttachment>>(json, JsonOptions)
                    ?? new List<InquiryAttachment>();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                return new List<InquiryAttachment>();
            }
        }

        private static async Task SaveManifestAsync(int inquiryId, List<InquiryAttachment> records)
        {
            string path = ManifestPath(inquiryId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string json = JsonSerializer.Serialize(records, JsonOptions);
            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
        }

        // ══════════════════════════════════════════════════════════════
        //  ADD (upload)
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// <paramref name="displayName"/> overrides the stored file name
        /// shown to the user (the website inbox passes the customer's
        /// original file name while the on-disk copy keeps its
        /// {documentId}_ prefix).
        /// </summary>
        public async Task<InquiryAttachment> AddAsync(int inquiryId, string sourcePath,
            string? displayName = null, CancellationToken ct = default)
        {
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("Attachment not found.", sourcePath);
            string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (!IsAllowedExtension(extension))
                throw new InvalidOperationException(
                    $"Unsupported attachment type '{extension}'. Allowed: {string.Join(", ", AllowedExtensions)}.");
            var info = new FileInfo(sourcePath);
            if (info.Length > AttachmentLimits.MaxFileSizeBytes)
                throw new InvalidOperationException($"Attachment exceeds {AttachmentLimits.MaxFileSizeDisplay}.");

            string folder = StorageFolder(inquiryId);
            Directory.CreateDirectory(folder);
            string safeName = string.Concat(info.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            string destination = Path.Combine(folder, Guid.NewGuid().ToString("N") + "_" + safeName);
            await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            await using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await source.CopyToAsync(target, ct).ConfigureAwait(false);

            try
            {
                var record = new InquiryAttachment
                {
                    // Ticks: unique, sortable, no EF identity needed.
                    Id = DateTime.UtcNow.Ticks,
                    InquiryId = inquiryId, FileName = string.IsNullOrWhiteSpace(displayName) ? info.Name : displayName,
                    Extension = extension,
                    FileSize = info.Length, StoragePath = destination,
                    AttachmentType = extension.TrimStart('.').ToUpperInvariant(),
                    UploadedBy = CurrentOperator.Username, UploadedAt = DateTime.Now
                };
                var manifest = await LoadManifestAsync(inquiryId).ConfigureAwait(false);
                manifest.Add(record);
                await SaveManifestAsync(inquiryId, manifest).ConfigureAwait(false);
                MirrorToDatabase(record);
                return record;
            }
            catch
            {
                // Never leave a copied file behind without metadata.
                try { File.Delete(destination); } catch { /* best effort */ }
                throw;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  DELETE / OPEN
        // ══════════════════════════════════════════════════════════════

        public async Task<bool> DeleteAsync(InquiryAttachment attachment, CancellationToken ct = default)
        {
            var manifest = await LoadManifestAsync(attachment.InquiryId).ConfigureAwait(false);
            var hit = manifest.FirstOrDefault(x => x.Id == attachment.Id) ?? attachment;
            manifest.Remove(hit);
            await SaveManifestAsync(attachment.InquiryId, manifest).ConfigureAwait(false);
            MirrorDelete(hit);
            // Delete every candidate location; failures are non-fatal.
            try { if (File.Exists(hit.StoragePath)) File.Delete(hit.StoragePath); } catch { /* best effort */ }
            try
            {
                string fallback = Path.Combine(StorageFolder(hit.InquiryId), Path.GetFileName(hit.StoragePath));
                if (File.Exists(fallback)) File.Delete(fallback);
            }
            catch { /* best effort */ }
            return true;
        }

        /// <summary>
        /// Path resolution: absolute path first, then the inquiry's
        /// folder by file name (survives data-root switches and backup
        /// restores). Null when the file is gone from disk.
        /// </summary>
        public static string? ResolvePath(InquiryAttachment attachment)
        {
            if (attachment == null) return null;
            if (File.Exists(attachment.StoragePath)) return attachment.StoragePath;
            string name = Path.GetFileName(attachment.StoragePath);
            if (name.Length == 0) name = attachment.FileName;
            if (name.Length == 0) return null;
            string fallback = Path.Combine(StorageFolder(attachment.InquiryId), name);
            return File.Exists(fallback) ? fallback : null;
        }

        /// <summary>Opens with the registered system application. Never throws.</summary>
        public FileOpResult Open(InquiryAttachment attachment)
        {
            string? path = ResolvePath(attachment);
            if (path == null)
                return FileOpResult.Fail(
                    $"'{attachment.FileName}' is not found on disk. It may have been moved or deleted outside ProGlass.");
            return FileOpener.TryOpenFile(path);
        }

        // ══════════════════════════════════════════════════════════════
        //  OPTIONAL EF MIRROR (register file-count column only)
        //
        //  Best-effort write into the InquiryAttachments table so the
        //  register's "files" count keeps working on healthy databases.
        //  The manifest is the source of truth; a mirror failure is
        //  logged away and ignored. Once any mirror call fails,
        //  _mirrorEnabled drops so a broken schema can't slow every
        //  upload.
        // ══════════════════════════════════════════════════════════════

        private bool _mirrorEnabled = true;

        private void MirrorToDatabase(InquiryAttachment record)
        {
            if (!_mirrorEnabled) return;
            try
            {
                var mirror = new InquiryAttachment
                {
                    Id = record.Id, InquiryId = record.InquiryId, FileName = record.FileName,
                    Extension = record.Extension, FileSize = record.FileSize,
                    StoragePath = record.StoragePath, AttachmentType = record.AttachmentType,
                    UploadedBy = record.UploadedBy, UploadedAt = record.UploadedAt
                };
                AsyncUtility.SafeFireAndForget(
                    () => _repository.AddAttachmentAsync(mirror),
                    "Inquiry attachment count mirror");
            }
            catch
            {
                _mirrorEnabled = false;
            }
        }

        private void MirrorDelete(InquiryAttachment record)
        {
            try
            {
                AsyncUtility.SafeFireAndForget(
                    () => _repository.DeleteAttachmentAsync(record.Id),
                    "Inquiry attachment count mirror delete");
            }
            catch
            {
                // mirror failures are non-fatal
            }
        }
    }
}
