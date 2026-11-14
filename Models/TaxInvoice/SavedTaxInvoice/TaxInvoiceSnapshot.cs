using ProGlassAutomation.Models.MasterData;
using System;
using System.Collections.Generic;

namespace ProGlassAutomation.Models.TaxInvoice.SavedTaxInvoice
{
    /// <summary>
    /// Pure POCO snapshot of the entire Tax Invoice state for JSON serialization.
    /// Never contains WPF types. Populated by TaxInvoiceMapper.ToSnapshot(),
    /// consumed by TaxInvoiceMapper.DeserializeSnapshot().
    ///
    /// DESIGN DECISIONS:
    ///   • Attachments stored as base64 strings in snapshot for full
    ///     portability. DB entity stores raw byte[]. Mapper converts.
    ///   • Pricing stored at both row level (Value) and summary level
    ///     (Subtotal, VAT, GrandTotal) — row-level for re-calculation,
    ///     summary-level for fast display without recalc.
    ///   • All DateTime defaults = default(DateTime) — never DateTime.Now
    ///     at property level (same BUG-08 / BUG-09 fix as DN snapshot).
    ///   • SchemaVersion = 1 — bump when breaking changes are made to
    ///     snapshot shape. Mapper checks version on deserialize.
    ///
    /// DOWNSTREAM USAGE (Statement PDF — future module):
    ///   AttachmentSnapshots carry base64-encoded signed PI + DN PDFs.
    ///   Statement generator iterates Attachments, decodes bytes, appends
    ///   as pages to the combined QuestPDF output document.
    ///
    /// CHAT 20 CHANGE — PROJECT FIELDS ALIGNED WITH PROFORMA INVOICE:
    ///   Previously TI stored only ProjectName, ProjectLocation, ProjectReference —
    ///   which was insufficient to preserve the full 5-field project model from PI.
    ///   PI → TI conversion silently lost ProjectContractNo and forced
    ///   ProjectReference to hold either PI.ProjectReference OR PI.ProjectContractNo
    ///   (never both).
    ///
    ///   Now InvProjectSnapshot mirrors PI ProjectSnapshot exactly:
    ///     ProjectIdentifierName, ProjectContractNo, ProjectReference,
    ///     SiteDeliveryLocation
    ///
    ///   Legacy fields ProjectName and ProjectLocation are kept marked [Obsolete]
    ///   for backward-compatible deserialization of existing saved invoices.
    ///   New saves populate only the new fields — old fields default to empty.
    ///   Load-time migration in TaxInvoiceDetailViewModel.RestoreFromSnapshot
    ///   uses legacy values as fallback when new fields are empty.
    /// </summary>
    public class TaxInvoiceSnapshot
    {
        public int SchemaVersion { get; set; } = 1;

        // ── Header ───────────────────────────────────────────────────────
        public InvHeaderSnapshot Header { get; set; } = new();

        // ── Company (supplier) ───────────────────────────────────────────
        public InvCompanySnapshot Company { get; set; } = new();

        // ── Customer (buyer) ────────────────────────────────────────────
        public InvCustomerSnapshot Customer { get; set; } = new();

        // ── Project ──────────────────────────────────────────────────────
        public InvProjectSnapshot Project { get; set; } = new();

        // ── Spec groups with glass item rows (the billable content) ──────
        public List<InvSpecGroupSnapshot> SpecGroups { get; set; } = new();

        // ── Financial summary ────────────────────────────────────────────
        public InvFinancialSnapshot Financial { get; set; } = new();

        // ── Bank + payment details ───────────────────────────────────────
        public InvBankSnapshot Bank { get; set; } = new();

        // ── Prepared by ──────────────────────────────────────────────────
        public InvPreparedBySnapshot PreparedBy { get; set; } = new();

        // ── Attachments (signed PI PDF, signed DN PDF, other docs) ───────
        public List<InvAttachmentSnapshot> Attachments { get; set; } = new();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  HEADER — invoice identification + dates + references
    // ════════════════════════════════════════════════════════════════════════

    public class InvHeaderSnapshot
    {
        /// <summary>e.g. "INV-2026-001" or "INV-2026-001-A"</summary>
        public string InvoiceNumber { get; set; } = "";

        /// <summary>e.g. "PI-2026-001" — parent proforma invoice</summary>
        public string ProformaId { get; set; } = "";

        /// <summary>
        /// Comma-separated DN references, e.g. "DN-2026-001-01".
        /// Empty = proforma-only invoice.
        /// </summary>
        public string DeliveryNoteReference { get; set; } = "";

        /// <summary>e.g. "JOB-2026-001" — cross-reference to production</summary>
        public string JobOrderId { get; set; } = "";

        /// <summary>Customer's PO number for their records.</summary>
        public string PurchaseOrderNumber { get; set; } = "";

        /// <summary>Full | Partial</summary>
        public string InvoiceType { get; set; } = "Full";

        /// <summary>Empty for Full invoices. A/B/C for Partial.</summary>
        public string SequenceLetter { get; set; } = "";

        /// <summary>Position within family (1, 2, 3…)</summary>
        public int SequenceNumber { get; set; } = 1;

        /// <summary>
        /// Draft | Issued | Paid | PartiallyPaid | Overdue | Cancelled | Void
        /// </summary>
        public string Status { get; set; } = "Draft";

        /// <summary>
        /// Date shown on printed invoice.
        /// Default = default(DateTime) — set explicitly by mapper.
        /// </summary>
        public DateTime IssueDate { get; set; } = default;

        /// <summary>
        /// Payment due date = IssueDate + PaymentTermsDays.
        /// Default = default(DateTime) — set explicitly by mapper.
        /// </summary>
        public DateTime DueDate { get; set; } = default;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  COMPANY — supplier / issuing company details
    // ════════════════════════════════════════════════════════════════════════

    public class InvCompanySnapshot
    {
        public string CompanyName { get; set; } = "";

        /// <summary>UAE Tax Registration Number — 15 digits.</summary>
        public string CompanyTrn { get; set; } = "";

        public string CompanyAddress { get; set; } = "";
        public string CompanyLocation { get; set; } = "";
        public string CompanyPhone { get; set; } = "";
        public string CompanyEmail { get; set; } = "";

        /// <summary>
        /// Company logo stored as base64 PNG/JPG for PDF embedding.
        /// Empty string = no logo. Decoded to byte[] by PDF generator.
        /// </summary>
        public string LogoBase64 { get; set; } = "";

        /// <summary>
        /// Path to the company logo image. Preferred over LogoBase64 when both
        /// are set (the file is the live source of truth). Empty = no logo.
        /// </summary>
        public string LogoPath { get; set; } = "";

        // v3 — logo placement (Company Center branding) so the Tax Invoice
        // letterhead matches the Proforma Invoice letterhead.
        public CompanyLogoLayout LogoLayout { get; set; } = CompanyLogoLayout.None;
        public double LogoWidthMm { get; set; } = 22;
        public double LogoHeightMm { get; set; } = 26;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CUSTOMER — buyer details
    // ════════════════════════════════════════════════════════════════════════

    public class InvCustomerSnapshot
    {
        public string CustomerName { get; set; } = "";
        public string CustomerTrn { get; set; } = "";
        public string CustomerAddress { get; set; } = "";
        public string CustomerPhone { get; set; } = "";
        public string CustomerEmail { get; set; } = "";

        /// <summary>
        /// Contact person at customer site.
        /// Used in "Attention:" field on printed invoice.
        /// </summary>
        public string ContactPerson { get; set; } = "";
    }

    // ════════════════════════════════════════════════════════════════════════
    //  PROJECT — project identification
    //
    //  CHAT 20 CHANGE:
    //    Extended from 3 fields to 4 fields to match Proforma Invoice model.
    //    Legacy ProjectName + ProjectLocation kept [Obsolete] for backward
    //    compatibility with existing saved invoices.
    //
    //  Field-to-field mapping (PI ProjectSnapshot → this InvProjectSnapshot):
    //    PI.ProjectIdentifierName → ProjectIdentifierName   (NEW)
    //    PI.ProjectContractNo     → ProjectContractNo       (NEW)
    //    PI.ProjectReference      → ProjectReference        (unchanged)
    //    PI.SiteDeliveryLocation  → SiteDeliveryLocation    (NEW)
    //    PI.LpoNumber             → InvHeaderSnapshot.PurchaseOrderNumber
    //                               (LPO = PO in UAE glass industry)
    // ════════════════════════════════════════════════════════════════════════

    public class InvProjectSnapshot
    {
        // ── Current fields (aligned with Proforma Invoice) ──────────────

        /// <summary>
        /// Short project designation (e.g. "IMAD HABIB").
        /// Matches PI ProjectIdentifierName.
        /// </summary>
        public string ProjectIdentifierName { get; set; } = "";

        /// <summary>
        /// Project number with name (e.g. "428 IMAD HABIB").
        /// Matches PI ProjectContractNo.
        /// </summary>
        public string ProjectContractNo { get; set; } = "";

        /// <summary>
        /// Full descriptive reference (e.g. "HOUSE FOR IMAD HABIB").
        /// Matches PI ProjectReference.
        /// </summary>
        public string ProjectReference { get; set; } = "";

        /// <summary>
        /// Site or delivery address (e.g. "AJMAN").
        /// Matches PI SiteDeliveryLocation.
        /// </summary>
        public string SiteDeliveryLocation { get; set; } = "";

        // ── Legacy fields (backward compatibility only) ─────────────────
        //
        // Old saved invoices in the DB have these fields populated.
        // Deserializer will fill them from legacy JSON automatically.
        // On load, TaxInvoiceDetailViewModel.RestoreFromSnapshot uses them
        // as fallback when new fields are empty (migration).
        // New saves leave these empty — clean cut going forward.

        /// <summary>
        /// LEGACY — do not use in new code. Use ProjectIdentifierName instead.
        /// Kept as public property so System.Text.Json deserializes old JSON
        /// without dropping data. Migrated to ProjectIdentifierName on load.
        /// </summary>
        [Obsolete("Use ProjectIdentifierName. Kept for legacy JSON deserialization.")]
        public string ProjectName { get; set; } = "";

        /// <summary>
        /// LEGACY — do not use in new code. Use SiteDeliveryLocation instead.
        /// Kept as public property so System.Text.Json deserializes old JSON
        /// without dropping data. Migrated to SiteDeliveryLocation on load.
        /// </summary>
        [Obsolete("Use SiteDeliveryLocation. Kept for legacy JSON deserialization.")]
        public string ProjectLocation { get; set; } = "";
    }

    // ════════════════════════════════════════════════════════════════════════
    //  SPEC GROUP — one specification type (e.g. "6mm Clear Tempered")
    //  Contains all glass item rows billed under this spec.
    // ════════════════════════════════════════════════════════════════════════

    public class InvSpecGroupSnapshot
    {
        public int GroupNumber { get; set; }
        public string GroupName { get; set; } = "";
        public string GlassType { get; set; } = "";
        public string SpecName { get; set; } = "";

        /// <summary>Rate per SQM applied to all rows in this group (AED).</summary>
        public decimal BaseRate { get; set; }

        public List<InvGlassItemRowSnapshot> Rows { get; set; } = new();

        // ── Group subtotals (pre-computed for fast PDF rendering) ─────────
        public int GroupTotalQty { get; set; }
        public decimal GroupTotalSqm { get; set; }
        public decimal GroupTotalValue { get; set; }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  GLASS ITEM ROW — one panel within a spec group
    //
    //  MANDATORY COLUMNS (from master prompt):
    //    # | GlassRef | W1 | H1 | W2 | H2 | Qty | SQM | T.SQM | Rate | Value
    //
    //  Rate and Value are Tax Invoice specific — DN rows have no pricing.
    //  SQM uses SqmCalculator.CalculatePanel() — 0.5 m² minimum floor.
    //  Value = Rate × Qty × Sqm
    // ════════════════════════════════════════════════════════════════════════

    public class InvGlassItemRowSnapshot
    {
        /// <summary>Serial number within spec group (the # column).</summary>
        public int SerialNumber { get; set; }

        public string GlassRef { get; set; } = "";

        /// <summary>Width 1 in mm (main dimension).</summary>
        public int W1 { get; set; }

        /// <summary>Height 1 in mm (main dimension).</summary>
        public int H1 { get; set; }

        /// <summary>Width 2 in mm (for shaped/non-rectangular panels).</summary>
        public int W2 { get; set; }

        /// <summary>Height 2 in mm (for shaped/non-rectangular panels).</summary>
        public int H2 { get; set; }

        public int Qty { get; set; }

        /// <summary>SQM per panel — SqmCalculator.CalculatePanel(W1, H1).</summary>
        public decimal Sqm { get; set; }

        /// <summary>Total SQM = Sqm × Qty.</summary>
        public decimal TotalSqm { get; set; }

        /// <summary>Rate per SQM in AED (inherited from group BaseRate).</summary>
        public decimal Rate { get; set; }

        /// <summary>Value = Rate × Qty × Sqm.</summary>
        public decimal Value { get; set; }

        public string Notes { get; set; } = "";
    }

    // ════════════════════════════════════════════════════════════════════════
    //  FINANCIAL SUMMARY
    //  UAE VAT rules: VAT applied AFTER discount.
    //  VAT rate: 5% (standard UAE rate — hardcoded per master prompt).
    // ════════════════════════════════════════════════════════════════════════

    public class InvFinancialSnapshot
    {
        /// <summary>Sum of all row Values across all spec groups.</summary>
        public decimal Subtotal { get; set; }

        /// <summary>Discount percentage shown on invoice (0-100).</summary>
        public decimal DiscountPercent { get; set; }

        /// <summary>Discount amount = Subtotal × (DiscountPercent / 100).</summary>
        public decimal DiscountAmount { get; set; }

        /// <summary>Subtotal − DiscountAmount.</summary>
        public decimal AfterDiscount { get; set; }

        /// <summary>UAE VAT = AfterDiscount × 0.05. Always 5%.</summary>
        public decimal VatAmount { get; set; }

        /// <summary>Final: AfterDiscount + VatAmount.</summary>
        public decimal GrandTotal { get; set; }

        /// <summary>
        /// Amount in words (AED) for printed invoice.
        /// e.g. "AED Twelve Thousand Five Hundred Only"
        /// Pre-computed by mapper — not recalculated on every render.
        /// </summary>
        public string GrandTotalInWords { get; set; } = "";

        // ── Running totals for family tracking ───────────────────────────

        /// <summary>
        /// Sum of GrandTotal across all prior ISSUED invoices in the
        /// same ProformaId family (excluding this invoice).
        /// Used to show: "Previously Invoiced: AED X"
        /// </summary>
        public decimal PreviouslyInvoicedTotal { get; set; }

        /// <summary>
        /// Balance remaining after this invoice:
        /// MAX(0, OriginalProformaTotal − PreviouslyInvoicedTotal − GrandTotal)
        /// </summary>
        public decimal RemainingBalance { get; set; }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  BANK DETAILS — printed on invoice footer
    // ════════════════════════════════════════════════════════════════════════

    public class InvBankSnapshot
    {
        public string BankName { get; set; } = "";
        public string BankAccountNumber { get; set; } = "";

        /// <summary>IBAN — up to 34 alphanumeric characters (UAE format).</summary>
        public string Iban { get; set; } = "";

        /// <summary>SWIFT/BIC code — up to 12 characters.</summary>
        public string SwiftCode { get; set; } = "";

        /// <summary>
        /// Payment terms label: "Net 30", "Net 60", "Net 90",
        /// "Immediate", "Custom".
        /// </summary>
        public string PaymentTerms { get; set; } = "Net 30";

        /// <summary>Days until payment due (0 = immediate).</summary>
        public int PaymentTermsDays { get; set; } = 30;

        /// <summary>
        /// Optional additional payment instructions shown on invoice.
        /// e.g. "Please include invoice number in payment reference."
        /// </summary>
        public string PaymentNotes { get; set; } = "";
    }

    // ════════════════════════════════════════════════════════════════════════
    //  PREPARED BY — signature block on printed invoice
    // ════════════════════════════════════════════════════════════════════════

    public class InvPreparedBySnapshot
    {
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public string Department { get; set; } = "";

        /// <summary>
        /// Signature image as base64 PNG.
        /// Empty = no signature image (manual signature on printout).
        /// </summary>
        public string SignatureBase64 { get; set; } = "";
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ATTACHMENT SNAPSHOT
    //
    //  DESIGN: byte[] stored as base64 string in JSON snapshot.
    //  DB entity (TaxInvoiceAttachment) stores raw byte[].
    //  Mapper converts between the two representations.
    //
    //  TYPES:
    //    SignedPI   — signed/stamped Proforma Invoice PDF
    //    SignedDN   — signed/stamped Delivery Note PDF
    //    Other      — any other supporting document
    //
    //  DOWNSTREAM (Statement PDF):
    //    Statement generator decodes FileBase64 → byte[] → appends as
    //    pages after the main invoice content in QuestPDF output.
    //    Order: Invoice → SignedPI → SignedDN → Other attachments.
    //
    //  NOTE: UploadedAt defaults to default(DateTime) — set explicitly
    //  by mapper/repository only (same BUG-09 fix as DN attachment).
    // ════════════════════════════════════════════════════════════════════════

    public class InvAttachmentSnapshot
    {
        /// <summary>SignedPI | SignedDN | Other</summary>
        public string AttachmentType { get; set; } = "Other";

        public string FileName { get; set; } = "";

        /// <summary>
        /// File content as base64-encoded string.
        /// Decoded to byte[] by PDF generator for appending.
        /// Empty string = file reference only (large file optimization).
        /// </summary>
        public string FileBase64 { get; set; } = "";

        /// <summary>File size in bytes (for display — before base64 encoding).</summary>
        public long FileSize { get; set; }

        public string Description { get; set; } = "";

        /// <summary>
        /// Set explicitly by mapper — never defaulted at property level.
        /// </summary>
        public DateTime UploadedAt { get; set; } = default;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  LIST ITEM — lightweight read model for list view
    //  No JSON deserialization needed for browsing the list.
    //  Populated by repository ToListItem() from indexed entity columns.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Read-only DTO surfacing indexed columns only.
    /// Used by TaxInvoiceListViewModel for the invoice list.
    /// Full snapshot loaded only when user opens detail view.
    /// Never inherits ViewModelBase — pure POCO (same BUG-06 fix as DN).
    /// </summary>
    public class TaxInvoiceListItem
    {
        public int Id { get; set; }
        public string InvoiceNumber { get; set; } = "";
        public string ProformaId { get; set; } = "";
        public string DeliveryNoteReference { get; set; } = "";
        public string JobOrderId { get; set; } = "";
        public string PurchaseOrderNumber { get; set; } = "";
        public string InvoiceType { get; set; } = "Full";
        public string SequenceLetter { get; set; } = "";
        public int SequenceNumber { get; set; } = 1;
        public string Status { get; set; } = "Draft";
        public string CustomerName { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string PaymentTerms { get; set; } = "Net 30";
        public DateTime IssueDate { get; set; }
        public DateTime DueDate { get; set; }
        public decimal Subtotal { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal AfterDiscount { get; set; }
        public decimal VatAmount { get; set; }
        public decimal GrandTotal { get; set; }
        public int TotalQty { get; set; }
        public decimal TotalSqm { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>ERP lifecycle hardening (Phase 1): true when this invoice
        /// has been voided (soft-closed). Kept on the list projection so the
        /// UI can badge/hide it and the dependency guard can ignore it.</summary>
        public bool IsVoid { get; set; }
    }
}