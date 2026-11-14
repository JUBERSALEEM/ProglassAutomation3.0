    using ProGlassAutomation.Models.TaxInvoice.Attachment;
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    namespace ProGlassAutomation.Models.TaxInvoice.SavedTaxInvoice
    {
        /// <summary>
        /// EF Core entity for one Tax Invoice row in taxinvoices.db.
        ///
        /// Uses JSON-blob strategy (same as PI + JO + DN):
        /// FullSnapshotJson contains entire invoice state for load/export/PDF.
        /// Indexed columns surfaced for list queries without JSON parsing.
        ///
        /// NUMBERING:
        ///   Full invoice    : INV-YYYY-NNN       (e.g. INV-2026-001)
        ///   Partial invoice : INV-YYYY-NNN-A/B/C (e.g. INV-2026-001-A)
        ///   Derived from ProformaId: PI-2026-001 → INV-2026-001
        ///
        /// FAMILY:
        ///   All invoices sharing same ProformaId form a family.
        ///   Family tracks: Ordered (MAX), Invoiced (SUM), Balance (MAX(0,O-I))
        ///
        /// ATTACHMENTS:
        ///   Stored as byte[] in DB (not file path) for full portability.
        ///   Statement PDF downstream: attachments appended as pages.
        ///   Base64-encoded in FullSnapshotJson for complete round-trip.
        ///
        /// STATUS:
        ///   Draft | Issued | Paid | PartiallyPaid | Overdue | Cancelled | Void
        ///
        /// INVOICE TYPE:
        ///   Full | Partial
        ///
        /// Timestamps set explicitly by repository — never defaulted at
        /// property level (same BUG-11 fix applied to DN).
        /// </summary>
        [Table("TaxInvoices")]
        public class SavedTaxInvoice
        {
            [Key]
            public int Id { get; set; }

            // ── Invoice identification ────────────────────────────────────────

            /// <summary>
            /// User-facing invoice number, e.g. "INV-2026-001" or
            /// "INV-2026-001-A". UNIQUE.
            /// </summary>
            [Required, MaxLength(60)]
            public string InvoiceNumber { get; set; } = "";

            /// <summary>
            /// Parent Proforma Invoice reference, e.g. "PI-2026-001".
            /// Primary family key — all invoices for one PI share this.
            /// </summary>
            [Required, MaxLength(50)]
            public string ProformaId { get; set; } = "";

            /// <summary>
            /// Delivery Note reference(s) this invoice covers.
            /// Comma-separated when invoice covers multiple DNs,
            /// e.g. "DN-2026-001-01,DN-2026-001-02".
            /// Empty string = no DN linked (proforma-only invoice).
            /// </summary>
            [MaxLength(500)]
            public string DeliveryNoteReference { get; set; } = "";

            /// <summary>
            /// Job Order reference for cross-module queries.
            /// e.g. "JOB-2026-001". May be empty for PI-only invoices.
            /// </summary>
            [MaxLength(50)]
            public string JobOrderId { get; set; } = "";

            /// <summary>
            /// Customer's Purchase Order number.
            /// Optional — customer-provided reference for their records.
            /// </summary>
            [MaxLength(100)]
            public string PurchaseOrderNumber { get; set; } = "";

            // ── Invoice type + partial sequencing ────────────────────────────

            /// <summary>Full | Partial</summary>
            [Required, MaxLength(20)]
            public string InvoiceType { get; set; } = "Full";

            /// <summary>
            /// For partial invoices: A, B, C, D...
            /// Empty string for full invoices.
            /// Used to build InvoiceNumber suffix: INV-2026-001-A
            /// </summary>
            [MaxLength(5)]
            public string SequenceLetter { get; set; } = "";

            /// <summary>
            /// Ordinal position within the family (1, 2, 3...).
            /// First invoice = 1. Used for ordering within a ProformaId family.
            /// </summary>
            public int SequenceNumber { get; set; } = 1;

            // ── Status ────────────────────────────────────────────────────────

            /// <summary>
            /// Draft | Issued | Paid | PartiallyPaid | Overdue | Cancelled | Void
            /// </summary>
            [MaxLength(30)]
            public string Status { get; set; } = "Draft";

            // ── Void / soft-close (ERP lifecycle hardening — Phase 1) ─────
            // A Tax Invoice is a fiscal document: once Issued it is NEVER
            // physically deleted — it is voided (and later corrected by a
            // credit note). Additive, nullable; a live invoice is
            // IsVoid=false with null audit fields, identical to legacy rows.

            /// <summary>True when the invoice was soft-closed (voided)
            /// instead of deleted. Voided invoices remain visible to VAT /
            /// reports / audit so the number is never silently missing.</summary>
            public bool IsVoid { get; set; }

            /// <summary>UTC timestamp the void was applied. Null when live.</summary>
            public DateTime? VoidedAtUtc { get; set; }

            /// <summary>Username that voided the invoice. Null when live.</summary>
            [MaxLength(100)]
            public string? VoidedBy { get; set; }

            /// <summary>Mandatory reason captured when voiding. Null when live.</summary>
            [MaxLength(500)]
            public string? VoidReason { get; set; }

            // ── Customer + project (indexed for list search) ──────────────────

            [MaxLength(200)]
            public string CustomerName { get; set; } = "";

            [MaxLength(200)]
            public string ProjectName { get; set; } = "";

            [MaxLength(200)]
            public string CompanyName { get; set; } = "";

            /// <summary>
            /// Tax Registration Number — 15 digits (UAE TRN standard).
            /// Validated at the VM level before save.
            /// </summary>
            [MaxLength(20)]
            public string CompanyTrn { get; set; } = "";

            // ── Dates ─────────────────────────────────────────────────────────

            /// <summary>
            /// Date the invoice was issued (shown on printed invoice).
            /// Set explicitly by mapper at creation — no property-level default.
            /// </summary>
            public DateTime IssueDate { get; set; }

            /// <summary>
            /// Payment due date — derived from IssueDate + PaymentTermsDays.
            /// Set explicitly by mapper.
            /// </summary>
            public DateTime DueDate { get; set; }

            // ── Payment terms ─────────────────────────────────────────────────

            /// <summary>
            /// Display label: "Net 30", "Net 60", "Net 90",
            /// "Immediate", "Custom". Stored as-is.
            /// </summary>
            [MaxLength(50)]
            public string PaymentTerms { get; set; } = "Net 30";

            /// <summary>
            /// Days until payment due (0 = immediate).
            /// Used to compute DueDate = IssueDate + PaymentTermsDays.
            /// </summary>
            public int PaymentTermsDays { get; set; } = 30;

            // ── Bank details (for printed invoice footer) ─────────────────────

            [MaxLength(200)]
            public string BankName { get; set; } = "";

            [MaxLength(100)]
            public string BankAccountNumber { get; set; } = "";

            [MaxLength(34)]
            public string Iban { get; set; } = "";

            [MaxLength(12)]
            public string SwiftCode { get; set; } = "";

            // ── Financial totals (indexed for reporting) ──────────────────────

            /// <summary>Sum of (Rate × Qty × SQM) for all line items.</summary>
            [Column(TypeName = "TEXT")]
            public decimal Subtotal { get; set; }

            /// <summary>Discount amount in AED (applied after subtotal).</summary>
            [Column(TypeName = "TEXT")]
            public decimal DiscountAmount { get; set; }

            /// <summary>Discount percentage (0-100). For display only.</summary>
            [Column(TypeName = "TEXT")]
            public decimal DiscountPercent { get; set; }

            /// <summary>Subtotal − DiscountAmount.</summary>
            [Column(TypeName = "TEXT")]
            public decimal AfterDiscount { get; set; }

            /// <summary>UAE VAT at 5% applied to AfterDiscount.</summary>
            [Column(TypeName = "TEXT")]
            public decimal VatAmount { get; set; }

            /// <summary>AfterDiscount + VatAmount. Final billable amount.</summary>
            [Column(TypeName = "TEXT")]
            public decimal GrandTotal { get; set; }

            // ── Quantity summary (for list display without JSON parse) ─────────

            public int TotalQty { get; set; }

            [Column(TypeName = "TEXT")]
            public decimal TotalSqm { get; set; }

            // ── Full snapshot ─────────────────────────────────────────────────

            /// <summary>
            /// Complete invoice state as JSON.
            /// Includes: company, customer, project, spec groups with
            /// glass item rows (Rate + Value), bank details, payment terms,
            /// attachments as base64, prepared-by info.
            /// </summary>
            [Required, Column(TypeName = "TEXT")]
            public string FullSnapshotJson { get; set; } = "{}";

            // ── Timestamps ────────────────────────────────────────────────────

            /// <summary>Set explicitly by repository in InsertAsync only.</summary>
            public DateTime CreatedAt { get; set; }

            /// <summary>Set explicitly by repository in InsertAsync + UpdateAsync.</summary>
            public DateTime UpdatedAt { get; set; }

            /// <summary>
            /// ERP hardening (Phase 5): optimistic-concurrency counter. The
            /// repository increments this on every successful UpdateAsync. An
            /// editor captures the value it loaded and passes it back on save;
            /// if the stored value has moved on, another user saved first and
            /// the repository raises a ConcurrencyConflictException instead of
            /// silently overwriting their work. Application-level (not EF's
            /// native token) because editors rebuild a detached entity via the
            /// mapper, so the token must be carried explicitly.
            /// </summary>
            public int RowVersion { get; set; }


            // ── Navigation (EF relationships) ─────────────────────────────────

            /// <summary>
            /// Line items stored normalized for cross-invoice queries.
            /// Cascade-deleted with parent invoice.
            /// </summary>
            public List<TaxInvoiceItem> Items { get; set; } = new();

            /// <summary>
            /// Attachments stored as byte[] blobs in DB.
            /// Includes: signed PI PDF, signed DN PDF, any other docs.
            /// Cascade-deleted with parent invoice.
            /// Used downstream by Statement PDF generator.
            /// </summary>
            public List<TaxInvoiceAttachment> Attachments { get; set; } = new();
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  TAX INVOICE LINE ITEM
        //  Stored normalized (not in JSON blob) for cross-invoice queries.
        //  One row per glass panel per spec group.
        // ═══════════════════════════════════════════════════════════════════════

        [Table("TaxInvoiceItems")]
        public class TaxInvoiceItem
        {
            [Key]
            public int Id { get; set; }

            /// <summary>
            /// Parent invoice number, e.g. "INV-2026-001".
            /// FK to SavedTaxInvoice.InvoiceNumber via HasPrincipalKey.
            /// </summary>
            [Required, MaxLength(60)]
            [ForeignKey(nameof(TaxInvoice))]
            public string InvoiceNumber { get; set; } = "";

            /// <summary>Navigation property — not loaded by default.</summary>
            public SavedTaxInvoice? TaxInvoice { get; set; }

            /// <summary>Parent ProformaId for cross-invoice family queries.</summary>
            [Required, MaxLength(50)]
            public string ProformaId { get; set; } = "";

            // ── Spec identification ──────────────────────────────────────────

            public int SpecGroupNumber { get; set; }

            [MaxLength(200)]
            public string SpecGroupName { get; set; } = "";

            [MaxLength(100)]
            public string GlassType { get; set; } = "";

            // ── Glass item identification ────────────────────────────────────

            public int SerialNumber { get; set; }

            [MaxLength(200)]
            public string GlassRef { get; set; } = "";

            public int W1 { get; set; }
            public int H1 { get; set; }
            public int W2 { get; set; }
            public int H2 { get; set; }

            // ── Quantity + area ──────────────────────────────────────────────

            public int Qty { get; set; }

            /// <summary>SQM per panel (0.5 m² minimum floor).</summary>
            [Column(TypeName = "TEXT")]
            public decimal Sqm { get; set; }

            /// <summary>Total SQM = Sqm × Qty.</summary>
            [Column(TypeName = "TEXT")]
            public decimal TotalSqm { get; set; }

            // ── Pricing ──────────────────────────────────────────────────────

            /// <summary>Rate per SQM in AED.</summary>
            [Column(TypeName = "TEXT")]
            public decimal Rate { get; set; }

            /// <summary>Value = Rate × Qty × Sqm (per row).</summary>
            [Column(TypeName = "TEXT")]
            public decimal Value { get; set; }

            // ── Notes ────────────────────────────────────────────────────────

            [MaxLength(500)]
            public string Notes { get; set; } = "";
        }
    }