using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProGlassAutomation.Models.ProformaInvoice.SavedInvoice
{
    /// <summary>
    /// EF Core entity representing one saved proforma invoice row in the
    /// separate proformainvoices.db SQLite database.
    ///
    /// Phase 7.15 uses a JSON-blob strategy: the FullSnapshotJson column
    /// contains the entire serialized wizard state (all 9 steps + Terms).
    /// A few top-level fields are surfaced as real columns so we can index
    /// and query them for the Load List UI (Phase 7.17).
    ///
    /// BUG-04: IssueDate / ValidUntil / CreatedAt / UpdatedAt no longer
    ///         default to DateTime.Now at property level. Repository sets
    ///         them explicitly on insert/update.
    /// BUG-05: Unique index on ProformaId is configured in DbContext via
    ///         fluent API (HasIndex(...).IsUnique()). Kept there because
    ///         MaxLength attribute is already on the property.
    /// BUG-06: FullSnapshotJson keeps "{}" default — required by EF Core
    ///         non-null constraint. Callers must handle empty JSON gracefully.
    /// </summary>
    [Table("ProformaInvoices")]
    public class SavedProformaInvoice
    {
        [Key]
        public int Id { get; set; }

        /// <summary>User-facing proforma reference, e.g. "PI-2026-001". UNIQUE.</summary>
        [Required, MaxLength(50)]
        public string ProformaId { get; set; } = "";

        /// <summary>Stable optional source Inquiry relationship.</summary>
        public int? InquiryId { get; set; }

        // ── Indexed searchable fields ────────────────────────────────────

        [MaxLength(200)]
        public string CompanyName { get; set; } = "";

        [MaxLength(200)]
        public string CustomerName { get; set; } = "";

        [MaxLength(200)]
        public string ProjectName { get; set; } = "";

        [MaxLength(50)]
        public string Status { get; set; } = "Draft";

        // ── Void / soft-close (ERP lifecycle hardening — Phase 1) ─────────
        // A posted or dependent document is never physically deleted; it is
        // VOIDED so its number, snapshot and audit trail survive. These are
        // additive and nullable — a fresh row is IsVoid=false with null
        // audit fields, identical to legacy behaviour.

        /// <summary>True when this document has been soft-closed (voided)
        /// instead of deleted. Voided rows are hidden from the default list
        /// but remain in reports / VAT / audit.</summary>
        public bool IsVoid { get; set; }

        /// <summary>UTC timestamp the void was applied. Null when live.</summary>
        public DateTime? VoidedAtUtc { get; set; }

        /// <summary>Username that voided the document. Null when live.</summary>
        [MaxLength(100)]
        public string? VoidedBy { get; set; }

        /// <summary>Mandatory reason captured when voiding (auditor's first
        /// question). Null when live.</summary>
        [MaxLength(500)]
        public string? VoidReason { get; set; }

        /// <summary>
        /// BUG-04: Removed DateTime.Now default. Set by mapper on save.
        /// </summary>
        public DateTime IssueDate { get; set; }

        /// <summary>
        /// BUG-04: Removed DateTime.Now.AddDays(7) default. Set by mapper.
        /// </summary>
        public DateTime ValidUntil { get; set; }

        // ── Financial snapshot (for quick reports without deserializing JSON) ──

        [Column(TypeName = "DECIMAL(18,2)")]
        public decimal GlassMaterialsSubtotal { get; set; }

        [Column(TypeName = "DECIMAL(18,2)")]
        public decimal OtherChargesSubtotal { get; set; }

        [Column(TypeName = "DECIMAL(18,2)")]
        public decimal DiscountAmount { get; set; }

        [Column(TypeName = "DECIMAL(18,2)")]
        public decimal VatAmount { get; set; }

        [Column(TypeName = "DECIMAL(18,2)")]
        public decimal GrandTotalVatInclusive { get; set; }

        // ── Full snapshot (all wizard data) ─────────────────────────────

        /// <summary>
        /// Serialized JSON of the entire wizard state (Steps 1-7 + Terms).
        /// Load/edit operations deserialize this and rehydrate VMs.
        /// BUG-06: Default "{}" preserved for EF Required constraint.
        /// </summary>
        [Required]
        public string FullSnapshotJson { get; set; } = "{}";

        // ── Timestamps ──────────────────────────────────────────────────

        /// <summary>
        /// BUG-04: Removed DateTime.Now default. Set by repository InsertAsync.
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// BUG-04: Removed DateTime.Now default. Set by repository on every save.
        /// </summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// ERP hardening (Phase 5): optimistic-concurrency counter, incremented
        /// by the repository on every successful UpdateAsync. See
        /// SavedTaxInvoice.RowVersion for the full rationale.
        /// </summary>
        public int RowVersion { get; set; }

        // ── REVISION CHAIN (2026-09-11) ────────────────────────────────
        // A posted PI's body is frozen (DocumentLifecyclePolicy.CanEditBody)
        // and is corrected by creating a linked DRAFT revision — never by
        // rewriting the committed document. RevisionNumber 0 = the original;
        // RootProformaId is the chain key (the original's ProformaId; blank
        // on legacy originals, treated as their own root).
        public string RootProformaId { get; set; } = "";

        public int RevisionNumber { get; set; }
    }
}