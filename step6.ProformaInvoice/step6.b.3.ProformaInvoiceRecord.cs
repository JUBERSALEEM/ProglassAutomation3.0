// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.b.3.ProformaInvoiceRecord.cs
// MODULE: step6 — the EF row, and why each column exists.
//
// The row is a REGISTER of one document: identity, parties as they read when
// the row was written, the money a list or a report needs, and the body. The
// body is the authority for every detail; the columns are a fast, indexed
// projection of it, refreshed on every save.
//
// Decimal columns are TEXT, which is how this database already stores
// InquiryValue and how EF Core maps decimal on SQLite: a REAL column would
// lose a fil in a 15,500.05 total.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProGlassAutomation.Services.Proforma
{
    /// <summary>One saved proforma invoice.</summary>
    [Table("ProformaInvoices")]
    public sealed class ProformaInvoiceRecord
    {
        [Key]
        public int Id { get; set; }

        /// <summary>PI-YYYY-NNN. Unique; never reused, not even after cancellation.</summary>
        [Required, MaxLength(50)]
        public string ProformaNumber { get; set; } = "";

        [Required, MaxLength(30)]
        public string Status { get; set; } = "Draft";

        public DateTime IssueDate { get; set; }
        public DateTime ValidUntil { get; set; }

        // ── Party projections (ids link, names print) ───────────────────
        public int? ClientId { get; set; }
        [Required, MaxLength(200)] public string ClientName { get; set; } = "";
        public int? ProjectId { get; set; }
        [MaxLength(200)] public string ProjectName { get; set; } = "";
        public int? InquiryId { get; set; }
        [MaxLength(50)] public string InquiryNumber { get; set; } = "";
        public int? PreparedByUserId { get; set; }
        [MaxLength(200)] public string PreparedByName { get; set; } = "";
        public int? SalesRepresentativeUserId { get; set; }
        [MaxLength(200)] public string SalesRepresentativeName { get; set; } = "";

        // ── Money (register and reporting projections) ──────────────────
        [Column(TypeName = "TEXT")] public decimal NetSubtotal { get; set; }
        [Column(TypeName = "TEXT")] public decimal OtherChargesSubtotal { get; set; }
        [Column(TypeName = "TEXT")] public decimal DiscountAmount { get; set; }
        [Column(TypeName = "TEXT")] public decimal VatAmount { get; set; }
        [Column(TypeName = "TEXT")] public decimal GrandTotal { get; set; }
        [Column(TypeName = "TEXT")] public decimal TotalCost { get; set; }
        [Column(TypeName = "TEXT")] public decimal Profit { get; set; }

        /// <summary>The entire document body. Required so a row can never exist without one.</summary>
        [Required]
        public string SnapshotJson { get; set; } = "{}";

        [Required] public string Notes { get; set; } = "";

        /// <summary>Optimistic-concurrency counter, advanced by every successful save.</summary>
        public int RowVersion { get; set; }

        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public int? CreatedByUserId { get; set; }
        [Required, MaxLength(200)] public string CreatedByName { get; set; } = "";
        public int? UpdatedByUserId { get; set; }
        [Required, MaxLength(200)] public string UpdatedByName { get; set; } = "";
    }
}
