// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.b.12.ProformaInvoiceAttachmentStore.cs
// MODULE: step6 — stage 6 (ADV step 8): the evidence chain.
//
// The FILE is copied into the application data folder under a generated name;
// the ROW stores that name, never the operator's absolute path. A path that
// travelled with a backup would point at somebody else's disk after a restore;
// a name inside the app data folder travels with the backup.
//
// Ordering is deliberate, exactly as the company logo store does it:
//   1. copy to a temporary name inside the invoice's folder;
//   2. move it into place (a crash mid-copy leaves nothing a row points at);
//   3. insert the row;
//   4. on removal, delete the ROW first and the FILE only after the commit.
// A failed save therefore never leaves a row whose file is missing, and a
// failed delete never loses evidence that a row still names.
//
// Any file may be attached as evidence, up to 20 MB. The type is the
// operator's label ("Purchase order", "Drawing"), not a guess from the bytes.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Proforma;
using ProGlassAutomation.Services.Database;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace ProGlassAutomation.Services.Proforma
{
    public sealed partial class ProformaInvoiceRepository
    {
        /// <summary>Largest file the evidence chain accepts.</summary>
        public const long MaxAttachmentBytes = 20L * 1024 * 1024;

        public async Task<IReadOnlyList<ProformaAttachment>> GetAttachmentsAsync(
            int invoiceId, CancellationToken ct = default)
        {
            if (invoiceId <= 0) return Array.Empty<ProformaAttachment>();
            try
            {
                await using var db = CreateContext();
                var rows = await db.Attachments.AsNoTracking()
                    .Where(row => row.ProformaInvoiceId == invoiceId)
                    .OrderBy(row => row.UploadedAtUtc)
                    .ThenBy(row => row.Id)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                return rows.Select(row => new ProformaAttachment
                {
                    Id = row.Id,
                    FileName = row.FileName,
                    StoredFileName = row.StoredFileName,
                    Extension = row.Extension,
                    FileSize = row.FileSize,
                    AttachmentType = row.AttachmentType,
                    Note = row.Note,
                    UploadedAtUtc = row.UploadedAtUtc,
                    UploadedByUserId = row.UploadedByUserId,
                    UploadedByName = row.UploadedByName
                }).ToList();
            }
            catch (Exception error)
            {
                ProGlassAutomation.Diagnostics.AppLog.Error(
                    "step6.invoice.attachments-read", error, "invoice " + invoiceId);
                return Array.Empty<ProformaAttachment>();
            }
        }

        public async Task<ProformaAttachmentAddResult> AddAttachmentAsync(
            int invoiceId, string sourceFilePath, string attachmentType, string note,
            CancellationToken ct = default)
        {
            if (invoiceId <= 0)
                return ProformaAttachmentAddResult.Fail("Save the invoice before attaching files.");

            InvoiceActor? actor = InvoiceAccess.Current(out string refusal);
            if (actor == null) return ProformaAttachmentAddResult.Fail(refusal);

            string source = (sourceFilePath ?? "").Trim();
            if (source.Length == 0 || !File.Exists(source))
                return ProformaAttachmentAddResult.Fail("The chosen file could not be found.");

            var info = new FileInfo(source);
            if (info.Length > MaxAttachmentBytes)
                return ProformaAttachmentAddResult.Fail(
                    "The file is larger than " +
                    (MaxAttachmentBytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture) +
                    " MB. Attach the relevant pages instead.");

            string extension = info.Extension.ToLowerInvariant();
            if (extension.Length > 20) extension = extension.Substring(0, 20);

            await InitializeAsync(ct).ConfigureAwait(false);
            string? temporaryPath = null, finalPath = null;

            try
            {
                string number;
                await using (var probe = CreateContext())
                {
                    number = await probe.Invoices.AsNoTracking()
                        .Where(row => row.Id == invoiceId)
                        .Select(row => row.ProformaNumber)
                        .FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "";
                }
                if (number.Length == 0 && !await InvoiceExistsAsync(invoiceId, ct).ConfigureAwait(false))
                    return ProformaAttachmentAddResult.Fail("This invoice no longer exists.");

                string? folder = AttachmentFolder(invoiceId, number);
                if (folder == null)
                    return ProformaAttachmentAddResult.Fail("The evidence folder could not be resolved.");
                Directory.CreateDirectory(folder);

                string storedName = "a-" + Guid.NewGuid().ToString("N") + extension;
                temporaryPath = Path.Combine(folder, storedName + ".part");
                finalPath = Path.Combine(folder, storedName);

                using (var input = File.OpenRead(source))
                using (var output = new FileStream(temporaryPath, FileMode.CreateNew,
                           FileAccess.Write, FileShare.None))
                {
                    await input.CopyToAsync(output, 81920, ct).ConfigureAwait(false);
                }
                File.Move(temporaryPath, finalPath);
                temporaryPath = null;

                var record = new ProformaInvoiceAttachmentRecord
                {
                    ProformaInvoiceId = invoiceId,
                    FileName = info.Name.Length > 260 ? info.Name.Substring(0, 260) : info.Name,
                    StoredFileName = storedName,
                    Extension = extension,
                    FileSize = info.Length,
                    AttachmentType = ProformaInvoiceRules.NormalizeText(attachmentType, 50) is { Length: > 0 } type
                        ? type : "Document",
                    Note = ProformaInvoiceRules.NormalizeText(note, 2000),
                    UploadedAtUtc = DateTime.UtcNow,
                    UploadedByUserId = actor.UserId,
                    UploadedByName = actor.Name
                };

                await using (var db = CreateContext())
                {
                    db.Attachments.Add(record);
                    await using var transaction = await db.Database
                        .BeginTransactionAsync(ct).ConfigureAwait(false);
                    await SqliteResilience.SaveWithRetryAsync(
                        token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                }

                return ProformaAttachmentAddResult.Ok(new ProformaAttachment
                {
                    Id = record.Id,
                    FileName = record.FileName,
                    StoredFileName = record.StoredFileName,
                    Extension = record.Extension,
                    FileSize = record.FileSize,
                    AttachmentType = record.AttachmentType,
                    Note = record.Note,
                    UploadedAtUtc = record.UploadedAtUtc,
                    UploadedByUserId = record.UploadedByUserId,
                    UploadedByName = record.UploadedByName
                });
            }
            catch (Exception error)
            {
                // A half-copied file must not survive beside a row that never
                // committed.
                TryDelete(temporaryPath);
                if (finalPath != null && !await StoredFileNameIsReferencedAsync(
                        Path.GetFileName(finalPath), ct).ConfigureAwait(false))
                    TryDelete(finalPath);
                return ProformaAttachmentAddResult.Fail(
                    DescribeWriteFailure("The file could not be attached", error));
            }
        }

        public async Task<bool> RemoveAttachmentAsync(long attachmentId, CancellationToken ct = default)
        {
            if (attachmentId <= 0) return false;

            InvoiceActor? actor = InvoiceAccess.Current(out string refusal);
            if (actor == null) return false;
            _ = refusal;

            await InitializeAsync(ct).ConfigureAwait(false);
            string? fileToDelete = null;

            try
            {
                await using (var db = CreateContext())
                {
                    ProformaInvoiceAttachmentRecord? record = await db.Attachments
                        .FirstOrDefaultAsync(row => row.Id == attachmentId, ct)
                        .ConfigureAwait(false);
                    if (record == null) return false;

                    string number = await db.Invoices.AsNoTracking()
                        .Where(row => row.Id == record.ProformaInvoiceId)
                        .Select(row => row.ProformaNumber)
                        .FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "";

                    db.Attachments.Remove(record);
                    await using var transaction = await db.Database
                        .BeginTransactionAsync(ct).ConfigureAwait(false);
                    await SqliteResilience.SaveWithRetryAsync(
                        token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);

                    // Only after the row is gone may the file go.
                    string? folder = AttachmentFolder(record.ProformaInvoiceId, number);
                    if (folder != null)
                        fileToDelete = Path.Combine(folder, record.StoredFileName);
                }

                TryDelete(fileToDelete);
                return true;
            }
            catch (Exception error)
            {
                ProGlassAutomation.Diagnostics.AppLog.Error(
                    "step6.invoice.attachment-remove", error, "attachment " + attachmentId);
                return false;
            }
        }

        private async Task<bool> InvoiceExistsAsync(int invoiceId, CancellationToken ct)
        {
            await using var db = CreateContext();
            return await db.Invoices.AsNoTracking()
                .AnyAsync(row => row.Id == invoiceId, ct).ConfigureAwait(false);
        }

        private async Task<bool> StoredFileNameIsReferencedAsync(string storedName, CancellationToken ct)
        {
            try
            {
                await using var db = CreateContext();
                return await db.Attachments.AsNoTracking()
                    .AnyAsync(row => row.StoredFileName == storedName, ct).ConfigureAwait(false);
            }
            catch
            {
                // Unknown: keep the file rather than delete evidence on a guess.
                return true;
            }
        }

        private static void TryDelete(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception error)
            {
                // Best-effort cleanup; the row is already consistent.
                ProGlassAutomation.Diagnostics.AppLog.Error(
                    "step6.invoice.attachment-cleanup", error, Path.GetFileName(path));
            }
        }
    }
}
