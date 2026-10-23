// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Models/InquiryRecord.cs
// MODULE: Inquiry — domain records.
//
// Ported from the legacy build without change of shape: the Inquiries
// table, the dimension-first BOQ line items, attachments and
// communications. The BOQ is deliberately dimension-first ("1200 ×
// 1500, 40 nos, DGU") rather than sheet-first — inquiries arrive as
// shapes the customer asked for; the estimator picks stock sheets later
// in the Proforma / Job Order.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace ProGlassAutomation.Models.Inquiry
{
    public static class InquiryStatuses
    {
        public const string Received = "Received";
        public const string Prepared = "Prepared";
        public const string Sent = "Sent";
        public const string Confirmed = "Confirmed";
        public const string Hold = "Hold";
        public const string Cancelled = "Cancelled";
        public static readonly IReadOnlyList<string> All =
            [Received, Prepared, Sent, Confirmed, Hold, Cancelled];
        public static string Normalize(string? value)
        {
            string text = value?.Trim() ?? "";
            if (text.Equals("On Hold", StringComparison.OrdinalIgnoreCase)) return Hold;
            if (text.Equals("Canceled", StringComparison.OrdinalIgnoreCase)) return Cancelled;
            return All.FirstOrDefault(x => string.Equals(x, text, StringComparison.OrdinalIgnoreCase))
                ?? Received;
        }
    }

    [Table("Inquiries")]
    public sealed class InquiryRecord
    {
        [Key] public int Id { get; set; }
        [Required, MaxLength(50)] public string InquiryNumber { get; set; } = "";
        [MaxLength(100)] public string InquiryReference { get; set; } = "Customer Inquiry";
        public int? CustomerId { get; set; }
        [MaxLength(200)] public string CustomerName { get; set; } = "";
        [MaxLength(250)] public string CustomerLocation { get; set; } = "";
        public int? ProjectId { get; set; }
        [MaxLength(200)] public string ProjectName { get; set; } = "";
        [MaxLength(500)] public string ProjectDescription { get; set; } = "";
        public int? SalesmanId { get; set; }
        [MaxLength(200)] public string SalesmanName { get; set; } = "";
        public int? PreparedByUserId { get; set; }
        [MaxLength(200)] public string PreparedByName { get; set; } = "";
        [MaxLength(100)] public string PreparedByRole { get; set; } = "";
        [Required, MaxLength(30)] public string Status { get; set; } = InquiryStatuses.Received;
        [MaxLength(20)] public string Priority { get; set; } = "Normal";
        [Column(TypeName = "TEXT")] public decimal InquiryValue { get; set; }
        public string Notes { get; set; } = "";
        public DateTime ReceivedAt { get; set; }
        public DateTime? PreparedAt { get; set; }
        public DateTime? SentAt { get; set; }
        public DateTime? ConfirmedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        [MaxLength(200)] public string UpdatedBy { get; set; } = "";
        public int? ProformaInvoiceId { get; set; }
        [MaxLength(50)] public string ProformaNumber { get; set; } = "";
        [MaxLength(50)] public string JobOrderNumber { get; set; } = "";
        /// <summary>
        /// Website intake traceability: set when this inquiry was
        /// ingested from a customer-website submission (inbox).
        /// WebSubmissionId = the submission UUID; the reference is the
        /// human number (WEB-YYYY-NNNNNN). Both stay empty for
        /// locally created inquiries.
        /// </summary>
        [MaxLength(40)] public string WebSubmissionId { get; set; } = "";
        [MaxLength(30)] public string WebSubmissionReference { get; set; } = "";
        /// <summary>Optimistic-concurrency counter advanced by each persisted edit.</summary>
        public int RowVersion { get; set; }
        public List<InquiryAttachment> Attachments { get; set; } = new();
        public List<InquiryCommunication> Communications { get; set; } = new();

        /// <summary>
        /// Scope-of-supply (bill of quantities) captured at inquiry time.
        /// ConvertToPi maps each row into a spec group so quoted shapes
        /// are never re-typed by the estimator.
        /// </summary>
        public List<InquiryLineItem> LineItems { get; set; } = new();
    }

    /// <summary>
    /// ONE scope-of-supply row on an inquiry: the glass build the
    /// customer asked for, in unit dimensions + quantity, with the rate
    /// the salesman EXPECTS to sell at (0 = prices decided later in the
    /// PI). Deliberately dimension-first (like the PI grid) rather than
    /// sheet-first: inquiries arrive as "1200 × 1500, 40 nos, DGU
    /// grey/clear" — the estimator picks stock sheets later.
    /// </summary>
    [Table("InquiryLineItems")]
    public sealed class InquiryLineItem
    {
        [Key] public long Id { get; set; }
        public int InquiryId { get; set; }
        /// <summary>Display + import ordering inside the inquiry.</summary>
        public int SortOrder { get; set; }
        /// <summary>SGU / DGU / LAM / DGU+LAM (normalized at import).</summary>
        [MaxLength(30)] public string GlassType { get; set; } = "";
        /// <summary>Generated-style spec name or the customer's own text.</summary>
        [MaxLength(300)] public string SpecName { get; set; } = "";
        [MaxLength(120)] public string OuterGlassColor { get; set; } = "";
        [MaxLength(120)] public string InnerGlassColor { get; set; } = "";
        /// <summary>Unit width, millimetres (maps to grid W1).</summary>
        public int WidthMm { get; set; }
        /// <summary>Unit height, millimetres (maps to grid H1).</summary>
        public int HeightMm { get; set; }
        public int Qty { get; set; }
        /// <summary>Salesman's expected sell rate AED/sqm; 0 = not quoted yet.</summary>
        [Column(TypeName = "TEXT")] public decimal RatePerSqm { get; set; }
        public string Notes { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public InquiryRecord Inquiry { get; set; } = null!;

        /// <summary>Null when the row cannot produce a grid line.</summary>
        public bool IsImportable =>
            !string.IsNullOrWhiteSpace(SpecName) && WidthMm > 0 && HeightMm > 0 && Qty > 0;
    }

    [Table("InquiryAttachments")]
    public sealed class InquiryAttachment
    {
        [Key] public long Id { get; set; }
        public int InquiryId { get; set; }
        [Required, MaxLength(260)] public string FileName { get; set; } = "";
        [MaxLength(20)] public string Extension { get; set; } = "";
        public long FileSize { get; set; }
        [Required] public string StoragePath { get; set; } = "";
        [MaxLength(50)] public string AttachmentType { get; set; } = "Document";
        [MaxLength(200)] public string UploadedBy { get; set; } = "";
        public DateTime UploadedAt { get; set; }
        public InquiryRecord Inquiry { get; set; } = null!;
    }

    [Table("InquiryCommunications")]
    public sealed class InquiryCommunication
    {
        [Key] public long Id { get; set; }
        public int InquiryId { get; set; }
        [Required, MaxLength(100)] public string EventType { get; set; } = "";
        [Required, MaxLength(200)] public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime OccurredAt { get; set; }
        public int? ActorUserId { get; set; }
        [MaxLength(200)] public string ActorName { get; set; } = "";
        public InquiryRecord Inquiry { get; set; } = null!;
    }
}
