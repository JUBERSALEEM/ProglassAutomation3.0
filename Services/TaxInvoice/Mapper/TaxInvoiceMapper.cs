using ProGlassAutomation.Models.MasterData;
using ProGlassAutomation.Models.TaxInvoice.Attachment;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Models.TaxInvoice.SavedTaxInvoice;
using ProGlassAutomation.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ProGlassAutomation.Services.Reporting;

namespace ProGlassAutomation.Services.TaxInvoice.Mapper
{
    /// <summary>
    /// Converts between Tax Invoice entities, snapshots, and view state.
    ///
    /// THREE CONVERSION PATHS:
    ///
    ///   Path A — BuildEntity(InvWizardState, invoiceNumber, seq, seqLetter):
    ///     Creates a SavedTaxInvoice EF entity from wizard VM state.
    ///     Called by TaxInvoiceDetailViewModel.SaveAsync().
    ///
    ///   Path B — DeserializeSnapshot(SavedTaxInvoice):
    ///     Reads FullSnapshotJson → TaxInvoiceSnapshot.
    ///     Called by TaxInvoiceDetailViewModel.LoadAsync().
    ///
    ///   Path C — RefreshSnapshot(SavedTaxInvoice):
    ///     Rebuilds FullSnapshotJson from entity state.
    ///     Called before every InsertAsync / UpdateAsync.
    ///
    /// FINANCIAL CALCULATION (always done here, not in VM):
    ///   VAT_RATE = 0.05 (5% UAE standard — hardcoded per master prompt)
    ///   Subtotal      = Σ (Rate × Qty × Sqm) per row
    ///   DiscountAmt   = Subtotal × (DiscountPercent / 100)
    ///   AfterDiscount = Subtotal − DiscountAmt
    ///   VatAmount     = AfterDiscount × VAT_RATE
    ///   GrandTotal    = AfterDiscount + VatAmount
    ///
    /// SQM CALCULATION:
    ///   Always uses SqmCalculator.CalculatePanel(W1, H1)
    ///   0.5 m² minimum floor — single source of truth.
    ///
    /// AMOUNT IN WORDS:
    ///   GrandTotalInWords computed by AmountToWords() utility.
    ///   Pre-computed at save time — not recalculated on every render.
    ///
    /// CHAT 20 CHANGE — PROJECT FIELDS ALIGNED WITH PI:
    ///   InvWizardState.Project section now uses 4 fields matching PI:
    ///     ProjectIdentifierName, ProjectContractNo, ProjectReference,
    ///     SiteDeliveryLocation
    ///
    ///   Old ProjectName / ProjectLocation removed from state
    ///   (state is transient — no backward compat needed for wizard state).
    ///
    ///   BuildSnapshot writes new fields into InvProjectSnapshot.
    ///   Obsolete ProjectName / ProjectLocation in snapshot stay empty
    ///   on new saves. Legacy invoices retain their values via
    ///   [Obsolete]-marked properties in the snapshot model.
    /// </summary>
    public static class TaxInvoiceMapper
    {
        // ── Constants ─────────────────────────────────────────────────────

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = false,
            PropertyNameCaseInsensitive = true
        };

        // ════════════════════════════════════════════════════════════════════
        //  PATH A — Wizard state → EF Entity
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds a complete SavedTaxInvoice entity from wizard state.
        /// All financial totals computed here — not trusted from VM.
        /// </summary>
        public static SavedTaxInvoice BuildEntity(
            InvWizardState state,
            string invoiceNumber,
            int sequenceNumber,
            string sequenceLetter)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            var now = DateTime.Now;

            // ── Compute financials ────────────────────────────────────────
            var (items, subtotal) = BuildItemsAndSubtotal(
                state.SpecGroups, invoiceNumber);

            // Phase 11/12: shared financial-total rule (UAE 5% VAT).
            var totals = InvoiceTotalsCalculator.Compute(
                subtotal, state.DiscountPercent);
            decimal discountAmt = totals.DiscountAmount;
            decimal afterDiscount = totals.AfterDiscount;
            decimal vatAmount = totals.VatAmount;
            decimal grandTotal = totals.GrandTotal;

            int totalQty = items.Sum(i => i.Qty);
            decimal totalSqm = Math.Round(items.Sum(i => i.TotalSqm), 3);

            // ── Build entity ──────────────────────────────────────────────
            //
            // CHAT 20: entity.ProjectName is the indexed list-view column.
            // Populated from the new ProjectIdentifierName field.
            // This keeps the list view (SELECT ProjectName FROM ...) working
            // without schema changes to the EF entity.
            var entity = new SavedTaxInvoice
            {
                InvoiceNumber = invoiceNumber,
                ProformaId = state.ProformaId,
                DeliveryNoteReference = state.DeliveryNoteReference,
                JobOrderId = state.JobOrderId,
                PurchaseOrderNumber = state.PurchaseOrderNumber,
                InvoiceType = sequenceLetter == "" ? "Full" : "Partial",
                SequenceLetter = sequenceLetter,
                SequenceNumber = sequenceNumber,
                Status = state.Status,
                CustomerName = state.CustomerName,
                ProjectName = !string.IsNullOrWhiteSpace(state.ProjectIdentifierName)
                    ? state.ProjectIdentifierName
                    : state.ProjectReference,
                CompanyName = state.CompanyName,
                CompanyTrn = state.CompanyTrn,
                IssueDate = state.IssueDate == default ? now : state.IssueDate,
                DueDate = state.IssueDate == default
                    ? now.AddDays(state.PaymentTermsDays)
                    : state.IssueDate.AddDays(state.PaymentTermsDays),
                PaymentTerms = state.PaymentTerms,
                PaymentTermsDays = state.PaymentTermsDays,
                BankName = state.BankName,
                BankAccountNumber = state.BankAccountNumber,
                Iban = state.Iban,
                SwiftCode = state.SwiftCode,
                Subtotal = subtotal,
                DiscountAmount = discountAmt,
                DiscountPercent = state.DiscountPercent,
                AfterDiscount = afterDiscount,
                VatAmount = vatAmount,
                GrandTotal = grandTotal,
                TotalQty = totalQty,
                TotalSqm = totalSqm,
                Items = items,
                Attachments = new List<TaxInvoiceAttachment>()
            };

            // ── Build + attach snapshot ───────────────────────────────────
            var snapshot = BuildSnapshot(entity, state, subtotal,
                discountAmt, afterDiscount, vatAmount, grandTotal);
            entity.FullSnapshotJson = JsonSerializer.Serialize(
                snapshot, _jsonOptions);

            return entity;
        }

        // ════════════════════════════════════════════════════════════════════
        //  PATH B — EF Entity → TaxInvoiceSnapshot (deserialize)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Deserializes FullSnapshotJson → TaxInvoiceSnapshot.
        /// Returns null if JSON is empty or malformed.
        /// Callers must handle null defensively.
        /// </summary>
        public static TaxInvoiceSnapshot? DeserializeSnapshot(
            SavedTaxInvoice entity)
        {
            if (entity == null
                || string.IsNullOrWhiteSpace(entity.FullSnapshotJson)
                || entity.FullSnapshotJson == "{}")
                return null;

            try
            {
                return JsonSerializer.Deserialize<TaxInvoiceSnapshot>(
                    entity.FullSnapshotJson, _jsonOptions);
            }
            catch (JsonException ex)
            {
                AppLogService.Debug(
                    $"[TaxInvoiceMapper] Snapshot deserialize failed " +
                    $"for invoice '{entity.InvoiceNumber}': {ex.Message}");
                return null;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  PATH C — Refresh snapshot on existing entity
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Writes a new status onto both the entity row and the snapshot
        /// header so a later Load does not resurrect Draft after Issue All.
        /// </summary>
        public static void SetHeaderStatus(SavedTaxInvoice entity, string status)
        {
            if (entity == null) return;
            entity.Status = status ?? entity.Status;
            var snapshot = DeserializeSnapshot(entity);
            if (snapshot?.Header == null) return;
            snapshot.Header.Status = entity.Status;
            entity.FullSnapshotJson = JsonSerializer.Serialize(snapshot, _jsonOptions);
        }

        public static void RefreshSnapshot(
            SavedTaxInvoice entity, InvWizardState state)
        {
            var snapshot = BuildSnapshot(
                entity, state,
                entity.Subtotal, entity.DiscountAmount,
                entity.AfterDiscount, entity.VatAmount, entity.GrandTotal);

            entity.FullSnapshotJson = JsonSerializer.Serialize(
                snapshot, _jsonOptions);
        }

        // ════════════════════════════════════════════════════════════════════
        //  PRIVATE — item builder
        // ════════════════════════════════════════════════════════════════════

        private static (List<TaxInvoiceItem> items, decimal subtotal)
            BuildItemsAndSubtotal(
                IEnumerable<InvSpecGroupState>? specGroups,
                string invoiceNumber)
        {
            var items = new List<TaxInvoiceItem>();
            decimal subtotal = 0m;

            if (specGroups == null) return (items, subtotal);

            foreach (var group in specGroups)
            {
                if (group.Rows == null || group.Rows.Count == 0) continue;

                foreach (var row in group.Rows)
                {
                    decimal sqm = SqmCalculator.CalculatePanel(row.W1, row.H1);
                    decimal totalSqm = Math.Round(sqm * row.Qty, 3);
                    decimal value = MoneyRounding.Round(row.Rate * row.Qty * sqm);
                    subtotal += value;

                    items.Add(new TaxInvoiceItem
                    {
                        InvoiceNumber = invoiceNumber,
                        ProformaId = group.ProformaId,
                        SpecGroupNumber = group.GroupNumber,
                        SpecGroupName = group.GroupName,
                        GlassType = group.GlassType,
                        SerialNumber = row.SerialNumber,
                        GlassRef = row.GlassRef,
                        W1 = row.W1,
                        H1 = row.H1,
                        W2 = row.W2,
                        H2 = row.H2,
                        Qty = row.Qty,
                        Sqm = sqm,
                        TotalSqm = totalSqm,
                        Rate = row.Rate,
                        Value = value,
                        Notes = row.Notes
                    });
                }
            }

            return (items, MoneyRounding.Round(subtotal));
        }

        // ════════════════════════════════════════════════════════════════════
        //  PRIVATE — snapshot builder
        //
        //  CHAT 20: Project snapshot now populated from new 4-field wizard
        //  state. Legacy ProjectName / ProjectLocation fields on the snapshot
        //  are NOT written by new saves — they stay empty (default).
        //  They exist on the snapshot type only for backward-compat
        //  deserialization of pre-Chat-20 saved invoices.
        // ════════════════════════════════════════════════════════════════════

        private static TaxInvoiceSnapshot BuildSnapshot(
            SavedTaxInvoice entity,
            InvWizardState state,
            decimal subtotal,
            decimal discountAmt,
            decimal afterDiscount,
            decimal vatAmount,
            decimal grandTotal)
        {
            // Build spec group snapshots from wizard state
            var specGroups = state.SpecGroups?
                .Where(g => g.Rows != null && g.Rows.Count > 0)
                .Select(g =>
                {
                    var rows = g.Rows!.Select(r =>
                    {
                        decimal sqm = SqmCalculator.CalculatePanel(r.W1, r.H1);
                        decimal totalSqm = Math.Round(sqm * r.Qty, 3);
                        decimal value = MoneyRounding.Round(r.Rate * r.Qty * sqm);

                        return new InvGlassItemRowSnapshot
                        {
                            SerialNumber = r.SerialNumber,
                            GlassRef = r.GlassRef,
                            W1 = r.W1,
                            H1 = r.H1,
                            W2 = r.W2,
                            H2 = r.H2,
                            Qty = r.Qty,
                            Sqm = sqm,
                            TotalSqm = totalSqm,
                            Rate = r.Rate,
                            Value = value,
                            Notes = r.Notes
                        };
                    }).ToList();

                    return new InvSpecGroupSnapshot
                    {
                        GroupNumber = g.GroupNumber,
                        GroupName = g.GroupName,
                        GlassType = g.GlassType,
                        SpecName = g.SpecName,
                        BaseRate = g.BaseRate,
                        Rows = rows,
                        GroupTotalQty = rows.Sum(r => r.Qty),
                        GroupTotalSqm = Math.Round(rows.Sum(r => r.TotalSqm), 3),
                        GroupTotalValue = MoneyRounding.Round(rows.Sum(r => r.Value))
                    };
                }).ToList() ?? new List<InvSpecGroupSnapshot>();

            return new TaxInvoiceSnapshot
            {
                SchemaVersion = 1,
                Header = new InvHeaderSnapshot
                {
                    InvoiceNumber = entity.InvoiceNumber,
                    ProformaId = entity.ProformaId,
                    DeliveryNoteReference = entity.DeliveryNoteReference,
                    JobOrderId = entity.JobOrderId,
                    PurchaseOrderNumber = entity.PurchaseOrderNumber,
                    InvoiceType = entity.InvoiceType,
                    SequenceLetter = entity.SequenceLetter,
                    SequenceNumber = entity.SequenceNumber,
                    Status = entity.Status,
                    IssueDate = entity.IssueDate,
                    DueDate = entity.DueDate
                },
                Company = new InvCompanySnapshot
                {
                    CompanyName = state.CompanyName,
                    CompanyTrn = state.CompanyTrn,
                    CompanyAddress = state.CompanyAddress,
                    CompanyLocation = state.CompanyLocation,
                    CompanyPhone = state.CompanyPhone,
                    CompanyEmail = state.CompanyEmail,
                    LogoBase64 = state.LogoBase64,
                    LogoPath = state.LogoPath,
                    LogoLayout = state.LogoLayout,
                    LogoWidthMm = state.LogoWidthMm,
                    LogoHeightMm = state.LogoHeightMm
                },
                Customer = new InvCustomerSnapshot
                {
                    CustomerName = state.CustomerName,
                    CustomerTrn = state.CustomerTrn,
                    CustomerAddress = state.CustomerAddress,
                    CustomerPhone = state.CustomerPhone,
                    CustomerEmail = state.CustomerEmail,
                    ContactPerson = state.ContactPerson
                },

                // CHAT 20: 4-field project snapshot aligned with Proforma Invoice.
                // Legacy ProjectName / ProjectLocation left at default ("")
                // — they exist on the type only for backward-compat
                // deserialization of pre-Chat-20 saved invoices.
                Project = new InvProjectSnapshot
                {
                    ProjectIdentifierName = state.ProjectIdentifierName,
                    ProjectContractNo = state.ProjectContractNo,
                    ProjectReference = state.ProjectReference,
                    SiteDeliveryLocation = state.SiteDeliveryLocation
                },

                SpecGroups = specGroups,
                Financial = new InvFinancialSnapshot
                {
                    Subtotal = subtotal,
                    DiscountPercent = state.DiscountPercent,
                    DiscountAmount = discountAmt,
                    AfterDiscount = afterDiscount,
                    VatAmount = vatAmount,
                    GrandTotal = grandTotal,
                    GrandTotalInWords = AmountToWords(grandTotal),
                    PreviouslyInvoicedTotal = state.PreviouslyInvoicedTotal,
                    RemainingBalance = Math.Max(0m,
                        state.ProformaTotal - state.PreviouslyInvoicedTotal - grandTotal)
                },
                Bank = new InvBankSnapshot
                {
                    BankName = state.BankName,
                    BankAccountNumber = state.BankAccountNumber,
                    Iban = state.Iban,
                    SwiftCode = state.SwiftCode,
                    PaymentTerms = state.PaymentTerms,
                    PaymentTermsDays = state.PaymentTermsDays,
                    PaymentNotes = state.PaymentNotes
                },
                PreparedBy = new InvPreparedBySnapshot
                {
                    Name = state.PreparedByName,
                    Title = state.PreparedByTitle,
                    Department = state.PreparedByDepartment,
                    SignatureBase64 = state.PreparedBySignatureBase64
                },
                Attachments = state.AttachmentSnapshots ?? new()
            };
        }

        // ════════════════════════════════════════════════════════════════════
        //  AMOUNT IN WORDS — AED currency
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Converts a decimal AED amount to words for printed invoice.
        /// e.g. 12500.50 → "AED Twelve Thousand Five Hundred and Fifty Fils Only"
        /// Handles amounts up to 999,999,999.99.
        /// </summary>
        public static string AmountToWords(decimal amount)
        {
            if (amount <= 0m) return "AED Zero Only";

            long dirhams = (long)Math.Truncate(amount);
            int fils = (int)Math.Round((amount - dirhams) * 100);

            string result = $"AED {NumberToWords(dirhams)} Dirhams";

            if (fils > 0)
                result += $" and {NumberToWords(fils)} Fils";

            return result + " Only";
        }

        private static string NumberToWords(long number)
        {
            if (number == 0) return "Zero";
            if (number < 0) return "Minus " + NumberToWords(-number);

            string[] ones = {
                "", "One", "Two", "Three", "Four", "Five", "Six", "Seven",
                "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen",
                "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen",
                "Nineteen"
            };
            string[] tens = {
                "", "", "Twenty", "Thirty", "Forty", "Fifty",
                "Sixty", "Seventy", "Eighty", "Ninety"
            };

            if (number < 20) return ones[number];
            if (number < 100)
                return tens[number / 10] +
                       (number % 10 > 0 ? " " + ones[number % 10] : "");
            if (number < 1_000)
                return ones[number / 100] + " Hundred" +
                       (number % 100 > 0 ? " " + NumberToWords(number % 100) : "");
            if (number < 1_000_000)
                return NumberToWords(number / 1_000) + " Thousand" +
                       (number % 1_000 > 0
                           ? " " + NumberToWords(number % 1_000) : "");
            if (number < 1_000_000_000)
                return NumberToWords(number / 1_000_000) + " Million" +
                       (number % 1_000_000 > 0
                           ? " " + NumberToWords(number % 1_000_000) : "");

            return NumberToWords(number / 1_000_000_000) + " Billion" +
                   (number % 1_000_000_000 > 0
                       ? " " + NumberToWords(number % 1_000_000_000) : "");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  WIZARD STATE — passed from ViewModel to Mapper
    //  Pure POCO — no WPF types. Populated by TaxInvoiceDetailViewModel.
    //
    //  CHAT 20: Project section now has 4 fields matching Proforma Invoice.
    //  Old ProjectName / ProjectLocation removed — state is transient,
    //  no backward compat needed. ViewModel must populate new field names.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Carries all wizard VM state into the mapper.
    /// The mapper reads this and produces the entity + snapshot.
    /// This decouples the VM from the entity shape completely.
    /// </summary>
    public class InvWizardState
    {
        // Header
        public string ProformaId { get; set; } = "";
        public string DeliveryNoteReference { get; set; } = "";
        public string JobOrderId { get; set; } = "";
        public string PurchaseOrderNumber { get; set; } = "";
        public string Status { get; set; } = "Draft";
        public DateTime IssueDate { get; set; } = default;
        public string PaymentTerms { get; set; } = "Net 30";
        public int PaymentTermsDays { get; set; } = 30;

        // Company
        public string CompanyName { get; set; } = "";
        public string CompanyTrn { get; set; } = "";
        public string CompanyAddress { get; set; } = "";
        public string CompanyLocation { get; set; } = "";
        public string CompanyPhone { get; set; } = "";
        public string CompanyEmail { get; set; } = "";
        public string LogoBase64 { get; set; } = "";
        public string LogoPath { get; set; } = "";
        public CompanyLogoLayout LogoLayout { get; set; } = CompanyLogoLayout.None;
        public double LogoWidthMm { get; set; } = 22;
        public double LogoHeightMm { get; set; } = 26;

        // Customer
        public string CustomerName { get; set; } = "";
        public string CustomerTrn { get; set; } = "";
        public string CustomerAddress { get; set; } = "";
        public string CustomerPhone { get; set; } = "";
        public string CustomerEmail { get; set; } = "";
        public string ContactPerson { get; set; } = "";

        // ── Project (CHAT 20: aligned with Proforma Invoice) ────────────
        //
        // These 4 fields exactly mirror PI's ProjectSnapshot naming.
        // Set by TaxInvoiceDetailViewModel.BuildWizardState().
        //
        // Field-to-PDF-label mapping:
        //   ProjectIdentifierName → "Project Identifier Name:"
        //   ProjectContractNo     → "Project Name / No:"
        //   ProjectReference      → "Project Reference:"
        //   SiteDeliveryLocation  → "Site / Delivery Location:"
        //
        // Old ProjectName / ProjectLocation removed — no backward compat
        // needed on state (state is transient, created fresh each save).

        public string ProjectIdentifierName { get; set; } = "";
        public string ProjectContractNo { get; set; } = "";
        public string ProjectReference { get; set; } = "";
        public string SiteDeliveryLocation { get; set; } = "";

        // Financials
        public decimal DiscountPercent { get; set; }
        public decimal ProformaTotal { get; set; }
        public decimal PreviouslyInvoicedTotal { get; set; }

        // Bank
        public string BankName { get; set; } = "";
        public string BankAccountNumber { get; set; } = "";
        public string Iban { get; set; } = "";
        public string SwiftCode { get; set; } = "";
        public string PaymentNotes { get; set; } = "";

        // Prepared by
        public string PreparedByName { get; set; } = "";
        public string PreparedByTitle { get; set; } = "";
        public string PreparedByDepartment { get; set; } = "";
        public string PreparedBySignatureBase64 { get; set; } = "";

        // Spec groups (the billable content)
        public List<InvSpecGroupState>? SpecGroups { get; set; }

        // Attachments already converted to snapshots
        public List<InvAttachmentSnapshot>? AttachmentSnapshots { get; set; }
    }

    public class InvSpecGroupState
    {
        public int GroupNumber { get; set; }
        public string GroupName { get; set; } = "";
        public string GlassType { get; set; } = "";
        public string SpecName { get; set; } = "";
        public string ProformaId { get; set; } = "";
        public decimal BaseRate { get; set; }
        public List<InvRowState>? Rows { get; set; }
    }

    public class InvRowState
    {
        public int SerialNumber { get; set; }
        public string GlassRef { get; set; } = "";
        public int W1 { get; set; }
        public int H1 { get; set; }
        public int W2 { get; set; }
        public int H2 { get; set; }
        public int Qty { get; set; }
        public decimal Rate { get; set; }
        public string Notes { get; set; } = "";
    }
}