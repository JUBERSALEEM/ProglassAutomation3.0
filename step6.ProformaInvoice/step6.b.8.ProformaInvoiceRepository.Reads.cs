// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.b.8.ProformaInvoiceRepository.Reads.cs
// MODULE: step6 — the read path: register, one document, and attachment files.
//
// The register reads columns only. A row's body is deserialized by GetAsync
// alone, so drawing a list of five hundred quotations costs one indexed query
// rather than five hundred JSON documents.
//
// A stored body that cannot be read is REPORTED. Returning an empty invoice
// instead would look like a quotation whose contents silently vanished, and
// somebody would re-quote it at the wrong price.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Diagnostics;
using ProGlassAutomation.Models.Proforma;
using ProGlassAutomation.Services.Database;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace ProGlassAutomation.Services.Proforma
{
    public sealed partial class ProformaInvoiceRepository
    {
        public async Task<IReadOnlyList<ProformaInvoiceListRow>> QueryAsync(
            ProformaInvoiceQuery query, CancellationToken ct = default)
        {
            query ??= new ProformaInvoiceQuery();
            await InitializeAsync(ct).ConfigureAwait(false);

            try
            {
                await using var db = CreateContext();
                IQueryable<ProformaInvoiceRecord> source = db.Invoices.AsNoTracking();

                if (!query.IncludeCancelled)
                    source = source.Where(row => row.Status != ProformaInvoiceStatus.Cancelled);
                if (!string.IsNullOrWhiteSpace(query.Status))
                {
                    string status = ProformaInvoiceStatus.Normalize(query.Status);
                    source = source.Where(row => row.Status == status);
                }
                if (query.ClientId is int clientId && clientId > 0)
                    source = source.Where(row => row.ClientId == clientId);
                if (query.ProjectId is int projectId && projectId > 0)
                    source = source.Where(row => row.ProjectId == projectId);
                if (query.IssuedFrom is DateTime from)
                    source = source.Where(row => row.IssueDate >= from.Date);
                if (query.IssuedTo is DateTime to)
                    source = source.Where(row => row.IssueDate <= to.Date);

                string search = ProformaInvoiceRules.NormalizeText(query.Search).ToLowerInvariant();
                if (search.Length > 0)
                {
                    source = source.Where(row =>
                        row.ProformaNumber.ToLower().Contains(search) ||
                        row.ClientName.ToLower().Contains(search) ||
                        row.ProjectName.ToLower().Contains(search) ||
                        row.InquiryNumber.ToLower().Contains(search) ||
                        row.SalesRepresentativeName.ToLower().Contains(search) ||
                        row.PreparedByName.ToLower().Contains(search));
                }

                int cap = query.MaxRows <= 0 ? 500 : Math.Min(query.MaxRows, 5000);
                var rows = await source
                    .OrderByDescending(row => row.UpdatedAtUtc)
                    .ThenByDescending(row => row.Id)
                    .Take(cap)
                    .Select(row => new ProformaInvoiceListRow
                    {
                        Id = row.Id,
                        ProformaNumber = row.ProformaNumber,
                        Status = row.Status,
                        ClientName = row.ClientName,
                        ProjectName = row.ProjectName,
                        InquiryNumber = row.InquiryNumber,
                        PreparedByName = row.PreparedByName,
                        SalesRepresentativeName = row.SalesRepresentativeName,
                        IssueDate = row.IssueDate,
                        ValidUntil = row.ValidUntil,
                        NetSubtotal = row.NetSubtotal,
                        GrandTotal = row.GrandTotal,
                        Profit = row.Profit,
                        UpdatedAtUtc = row.UpdatedAtUtc
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                return rows;
            }
            catch (Exception error)
            {
                AppLog.Error("step6.invoice.register-read", error, "invoice register");
                return Array.Empty<ProformaInvoiceListRow>();
            }
        }

        public async Task<ProformaInvoiceDraft?> GetAsync(int id, CancellationToken ct = default)
        {
            if (id <= 0) return null;
            await InitializeAsync(ct).ConfigureAwait(false);

            try
            {
                await using var db = CreateContext();
                ProformaInvoiceRecord? record = await db.Invoices.AsNoTracking()
                    .FirstOrDefaultAsync(row => row.Id == id, ct).ConfigureAwait(false);
                if (record == null) return null;

                ProformaInvoiceDraft? draft = Restore(record);
                if (draft == null) return null;

                IReadOnlyList<ProformaAttachment> attachments =
                    await GetAttachmentsAsync(id, ct).ConfigureAwait(false);
                draft.Attachments = attachments.ToList();
                return draft;
            }
            catch (Exception error)
            {
                AppLog.Error("step6.invoice.read", error, "invoice " + id);
                return null;
            }
        }

        public async Task<ProformaInvoiceDraft?> GetByInquiryAsync(
            int inquiryId, CancellationToken ct = default)
        {
            if (inquiryId <= 0) return null;
            await InitializeAsync(ct).ConfigureAwait(false);

            try
            {
                await using var db = CreateContext();
                int id = await db.Invoices.AsNoTracking()
                    .Where(row => row.InquiryId == inquiryId &&
                                  row.Status != ProformaInvoiceStatus.Cancelled)
                    .OrderByDescending(row => row.Id)
                    .Select(row => row.Id)
                    .FirstOrDefaultAsync(ct).ConfigureAwait(false);
                return id <= 0 ? null : await GetAsync(id, ct).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                AppLog.Error("step6.invoice.read-by-inquiry", error, "inquiry " + inquiryId);
                return null;
            }
        }

        public async Task<string> SuggestNumberAsync(CancellationToken ct = default)
        {
            await InitializeAsync(ct).ConfigureAwait(false);
            await using var db = CreateContext();
            await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
            return ProformaInvoiceSchema.NextNumber(
                (Microsoft.Data.Sqlite.SqliteConnection)db.Database.GetDbConnection(),
                DateTime.Today.Year);
        }

        /// <summary>
        /// Rebuilds a draft from a row: the body is authoritative for details and
        /// the columns repair the identity/audit fields the body may not carry.
        /// </summary>
        private static ProformaInvoiceDraft? Restore(ProformaInvoiceRecord record)
        {
            ProformaInvoiceJsonResult body = ProformaInvoiceJson.Read(record.SnapshotJson);
            if (!body.Succeeded || body.Draft == null)
            {
                // Reported, not swallowed, and never replaced with an empty
                // document that looks legitimate.
                AppLog.Error("step6.invoice.snapshot-unreadable",
                    new InvalidDataException(body.Message), "invoice " + record.ProformaNumber);
                return null;
            }

            ProformaInvoiceDraft draft = body.Draft;
            draft.Id = record.Id;
            draft.ProformaNumber = record.ProformaNumber;
            draft.Status = ProformaInvoiceStatus.Normalize(record.Status);
            draft.IssueDate = record.IssueDate;
            draft.ValidUntil = record.ValidUntil;
            draft.Notes = record.Notes;
            draft.RowVersion = record.RowVersion;
            draft.CreatedAtUtc = record.CreatedAtUtc;
            draft.UpdatedAtUtc = record.UpdatedAtUtc;
            draft.CreatedByUserId = record.CreatedByUserId;
            draft.CreatedByName = record.CreatedByName;
            draft.UpdatedByUserId = record.UpdatedByUserId;
            draft.UpdatedByName = record.UpdatedByName;

            // Party snapshots: the columns are the register's copy. When the body
            // has an empty name (an imported or repaired row) the column fills it.
            if (draft.Parties.Client.ClientName.Length == 0)
                draft.Parties.Client.ClientName = record.ClientName;
            if (draft.Parties.Project.ProjectName.Length == 0)
                draft.Parties.Project.ProjectName = record.ProjectName;
            if (draft.Parties.Inquiry.InquiryNumber.Length == 0)
                draft.Parties.Inquiry.InquiryNumber = record.InquiryNumber;
            if (draft.Parties.PreparedBy.FullName.Length == 0)
                draft.Parties.PreparedBy.FullName = record.PreparedByName;
            if (draft.Parties.Client.SalesRepresentativeName.Length == 0)
                draft.Parties.Client.SalesRepresentativeName = record.SalesRepresentativeName;

            draft.Recalculate();
            return draft;
        }

        // ── Attachment files ────────────────────────────────────────────

        /// <summary>
        /// Folder that holds one invoice's evidence:
        /// %APPDATA%/ProGlassAutomation/ProformaInvoice/&lt;number&gt;/. A bare file
        /// name is resolved inside it; a separator, "..", an absolute path or a
        /// foreign name resolves to null instead of reaching the filesystem.
        /// </summary>
        public static string? AttachmentFolder(int invoiceId, string? proformaNumber)
        {
            if (invoiceId <= 0) return null;
            string folderName = SafeFolderName(proformaNumber);
            return Path.Combine(AppDataRoot.CurrentFolder, "ProformaInvoice",
                invoiceId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                folderName);
        }

        private static string SafeFolderName(string? proformaNumber)
        {
            string text = (proformaNumber ?? "").Trim();
            if (text.Length == 0) return "unallocated";
            foreach (char invalid in Path.GetInvalidFileNameChars())
                text = text.Replace(invalid, '_');
            text = text.Replace("..", "_");
            return text.Length > 60 ? text.Substring(0, 60) : text;
        }

        public string? ResolveAttachmentPath(int invoiceId, string storedFileName)
        {
            string name = (storedFileName ?? "").Trim();
            if (invoiceId <= 0 || name.Length == 0) return null;
            if (name.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                name.IndexOf(Path.AltDirectorySeparatorChar) >= 0 ||
                name.Contains("..") || Path.IsPathRooted(name))
                return null;

            try
            {
                using var db = CreateContext();
                string number = db.Invoices.AsNoTracking()
                    .Where(row => row.Id == invoiceId)
                    .Select(row => row.ProformaNumber)
                    .FirstOrDefault() ?? "";
                string? folder = AttachmentFolder(invoiceId, number);
                if (folder == null) return null;
                string path = Path.Combine(folder, name);
                return File.Exists(path) ? path : null;
            }
            catch (Exception error)
            {
                AppLog.Error("step6.invoice.attachment-path", error, "invoice " + invoiceId);
                return null;
            }
        }
    }
}
