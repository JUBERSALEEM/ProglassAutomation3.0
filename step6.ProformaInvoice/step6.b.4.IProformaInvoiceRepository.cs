// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.b.4.IProformaInvoiceRepository.cs
// MODULE: step6 — what the rest of the application may ask of an invoice.
//
// There is NO delete. A document is cancelled, or it is revised into a new
// draft; a quotation that reached a customer is evidence and its number must
// stay used. The register hides cancelled documents only when the caller asks
// it to.
//
// Reads return projections. The body is loaded only by GetAsync/GetByInquiry,
// so a register of 500 quotations does not deserialize 500 JSON documents to
// draw a list.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Proforma;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Proforma
{
    /// <summary>Register filter.</summary>
    public sealed class ProformaInvoiceQuery
    {
        /// <summary>Matched against number, client, project and inquiry number.</summary>
        public string Search { get; init; } = "";

        /// <summary>One status, or null for every status.</summary>
        public string? Status { get; init; }

        /// <summary>Cancelled documents are excluded unless this is true.</summary>
        public bool IncludeCancelled { get; init; }

        public int? ClientId { get; init; }
        public int? ProjectId { get; init; }
        public DateTime? IssuedFrom { get; init; }
        public DateTime? IssuedTo { get; init; }

        /// <summary>Safety cap; the register pages in the view model.</summary>
        public int MaxRows { get; init; } = 500;
    }

    /// <summary>One register row — no document body.</summary>
    public sealed class ProformaInvoiceListRow
    {
        public int Id { get; init; }
        public string ProformaNumber { get; init; } = "";
        public string Status { get; init; } = "";
        public string ClientName { get; init; } = "";
        public string ProjectName { get; init; } = "";
        public string InquiryNumber { get; init; } = "";
        public string PreparedByName { get; init; } = "";
        public string SalesRepresentativeName { get; init; } = "";
        public DateTime IssueDate { get; init; }
        public DateTime ValidUntil { get; init; }
        public decimal NetSubtotal { get; init; }
        public decimal GrandTotal { get; init; }
        public decimal Profit { get; init; }
        public DateTime UpdatedAtUtc { get; init; }

        public string NumberText => ProformaNumber.Length == 0 ? "(not numbered)" : ProformaNumber;

        public string IssueText => IssueDate == default
            ? "—" : IssueDate.ToString("yyyy-MM-dd");

        public string ValidUntilText => ValidUntil == default
            ? "—" : ValidUntil.ToString("yyyy-MM-dd");

        public string UpdatedText => UpdatedAtUtc == default
            ? "—" : UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        /// <summary>Past its validity date while still awaiting an answer.</summary>
        public bool IsOverdue => Status is ProformaInvoiceStatus.Sent or ProformaInvoiceStatus.Draft
            && ValidUntil != default && ValidUntil.Date < DateTime.Today;
    }

    /// <summary>Outcome of a save, with the document as persisted.</summary>
    public sealed class ProformaInvoiceSaveResult
    {
        public bool Succeeded { get; init; }
        public ProformaInvoiceDraft? Invoice { get; init; }
        public string Message { get; init; } = "";
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
        public bool NumberWasAllocated { get; init; }

        public static ProformaInvoiceSaveResult Ok(
            ProformaInvoiceDraft invoice, IReadOnlyList<string>? warnings, bool numberAllocated) =>
            new()
            {
                Succeeded = true,
                Invoice = invoice,
                Warnings = warnings ?? Array.Empty<string>(),
                NumberWasAllocated = numberAllocated
            };

        public static ProformaInvoiceSaveResult Fail(string message, IReadOnlyList<string>? warnings = null) =>
            new() { Succeeded = false, Message = message, Warnings = warnings ?? Array.Empty<string>() };
    }

    /// <summary>Outcome of a status change.</summary>
    public sealed class ProformaInvoiceStatusResult
    {
        public bool Succeeded { get; init; }
        public string Message { get; init; } = "";
        public string Status { get; init; } = "";

        public static ProformaInvoiceStatusResult Ok(string status) =>
            new() { Succeeded = true, Status = status };

        public static ProformaInvoiceStatusResult Fail(string message) =>
            new() { Succeeded = false, Message = message };
    }

    /// <summary>Proforma invoice storage and lifecycle.</summary>
    public interface IProformaInvoiceRepository
    {
        /// <summary>Brings the ERP file to the current schema (migration 1018 included).</summary>
        Task InitializeAsync(CancellationToken ct = default);

        Task<bool> IsSchemaReadyAsync(CancellationToken ct = default);

        /// <summary>Register rows, most recently updated first.</summary>
        Task<IReadOnlyList<ProformaInvoiceListRow>> QueryAsync(
            ProformaInvoiceQuery query, CancellationToken ct = default);

        /// <summary>One invoice with its body and attachments, or null.</summary>
        Task<ProformaInvoiceDraft?> GetAsync(int id, CancellationToken ct = default);

        /// <summary>The invoice created from one inquiry, or null.</summary>
        Task<ProformaInvoiceDraft?> GetByInquiryAsync(int inquiryId, CancellationToken ct = default);

        /// <summary>The next free number for the current year, for display before a save.</summary>
        Task<string> SuggestNumberAsync(CancellationToken ct = default);

        /// <summary>
        /// Validates and saves. A new draft gets its number here. Refused when
        /// the stored row has moved since <paramref name="expectedRowVersion"/>.
        /// </summary>
        Task<ProformaInvoiceSaveResult> SaveAsync(
            ProformaInvoiceDraft draft, int expectedRowVersion, CancellationToken ct = default);

        /// <summary>Changes status through the one transition table.</summary>
        Task<ProformaInvoiceStatusResult> ChangeStatusAsync(
            int id, string targetStatus, int expectedRowVersion, CancellationToken ct = default);

        /// <summary>Copies an existing invoice into a new DRAFT with its own number.</summary>
        Task<ProformaInvoiceSaveResult> DuplicateAsync(
            int sourceId, CancellationToken ct = default);

        // ── Attachments (their own table; files live in the app data folder) ──
        Task<IReadOnlyList<ProformaAttachment>> GetAttachmentsAsync(
            int invoiceId, CancellationToken ct = default);

        Task<ProformaAttachmentAddResult> AddAttachmentAsync(
            int invoiceId, string sourceFilePath, string attachmentType, string note,
            CancellationToken ct = default);

        /// <summary>Removes an attachment's row; the stored file is deleted after the commit.</summary>
        Task<bool> RemoveAttachmentAsync(long attachmentId, CancellationToken ct = default);

        /// <summary>Absolute path of a stored attachment, or null when it is gone.</summary>
        string? ResolveAttachmentPath(int invoiceId, string storedFileName);
    }

    /// <summary>Outcome of adding an attachment.</summary>
    public sealed class ProformaAttachmentAddResult
    {
        public bool Succeeded { get; init; }
        public ProformaAttachment? Attachment { get; init; }
        public string Message { get; init; } = "";

        public static ProformaAttachmentAddResult Ok(ProformaAttachment attachment) =>
            new() { Succeeded = true, Attachment = attachment };

        public static ProformaAttachmentAddResult Fail(string message) =>
            new() { Succeeded = false, Message = message };
    }
}
