using ProGlassAutomation.Models.TaxInvoice.SavedTaxInvoice;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.FileSystem;
using ProGlassAutomation.Services.Export.Pdf;
using ProGlassAutomation.Services.Export.Pdf.Renderers;
using ProGlassAutomation.Services.TaxInvoice.Mapper;
using ProGlassAutomation.Utilities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProGlassAutomation.Services.Reporting;

namespace ProGlassAutomation.Services.TaxInvoice.Pdf
{
    /// <summary>
    /// IPdfLayout implementation for Tax Invoice.
    ///
    /// CHAT 17 CRITICAL FIXES:
    ///   1. RenderFinancialSummary: removed .Width(320) fixed width.
    ///      Replaced with proper Row(2/1 split) layout — no measurement
    ///      conflict when this document is embedded/merged into a
    ///      Statement PDF pipeline.
    ///   2. RenderSignatureBlocks: removed .Height(1) on signature
    ///      underlines — BorderBottom(1) alone renders the line.
    ///      .Height(1) inside a Row causes measurement pass conflicts.
    ///
    /// UAE VAT RULES:
    ///   VAT = 5% applied to AfterDiscount
    ///   TRN = 15 digits, printed prominently
    ///   Currency = AED only
    /// </summary>
    public class TaxInvoicePdfDocument : IPdfLayout
    {

        /// <summary>Derived rhythm + type scale for the active print layout.</summary>
        private static ProGlassAutomation.Services.Export.Pdf.PdfMetrics M
            => ProGlassAutomation.Services.Export.Pdf.DocumentStyle.Metrics;
        // ── Brand colors (match app palette) ─────────────────────────────
        private static string ACCENT => DocumentStyle.ColorAccent;
        private static string ACCENT_DARK => DocumentStyle.ColorAccentDark;
        private static string DARK => DocumentStyle.ColorText;
        private static string MUTED => DocumentStyle.ColorMuted;
        private static string LINE => DocumentStyle.ColorBorder;
        private static string LIGHT_BG => DocumentStyle.ColorSurface;
        private static string TABLE_HDR => DocumentStyle.ColorTableHeader;
        private static string GREEN => DocumentStyle.ColorSuccess;
        private static string AMBER => DocumentStyle.ColorWarning;
        private static string RED => DocumentStyle.ColorDanger;
        private static string WHITE => DocumentStyle.ColorWhite;

        private readonly SavedTaxInvoice _entity;
        private readonly TaxInvoiceSnapshot _snapshot;
        private readonly TaxInvoiceRenderCtx _ctx;

        // ── Constructor ───────────────────────────────────────────────────

        public TaxInvoicePdfDocument(
            SavedTaxInvoice entity,
            TaxInvoiceSnapshot snapshot)
        {
            _entity = entity ?? throw new ArgumentNullException(nameof(entity));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _ctx = BuildRenderContext(entity, snapshot);
        }

        // ── IDocumentLayout ───────────────────────────────────────────────

        public string DocumentPrefix => "INV";
        public string DocumentId => _entity.InvoiceNumber;
        public string DocumentTitle => "TAX INVOICE";
        public string CompanyName => ExportFileNamer.FirstFilled(
            _entity.CustomerName, _ctx.CustomerName);
        public string Reference => ExportFileNamer.FirstFilled(
            _entity.ProjectName,
            _ctx.ProjectIdentifierName,
            _ctx.ProjectReference);
        public DateTime DocumentDate => _entity.IssueDate;

        // ── IPdfLayout ────────────────────────────────────────────────────

        public PageLayout PageLayout => PageLayout.A4Portrait;

        public LetterheadInfo? Letterhead
        {
            get
            {
                var info = new LetterheadInfo
                {
                    CompanyName = _ctx.CompanyName,
                    CompanyLocation = _ctx.CompanyLocation,
                    CompanyTrn = _ctx.CompanyTrn,
                    DocumentTitle = DocumentTitle,
                    DocumentId = DocumentId,
                    SubtitleLine = _ctx.Status,
                    LogoPath = _ctx.LogoPath,
                    LogoBase64 = _ctx.LogoBase64,
                    LogoLayout = _ctx.LogoLayout,
                    LogoWidthMm = _ctx.LogoWidthMm,
                    LogoHeightMm = _ctx.LogoHeightMm
                };
                info.Meta.Add(("Issue", _ctx.IssueDateDisplay));
                info.Meta.Add(("Due", _ctx.DueDateDisplay));
                info.Meta.Add(("Status", _ctx.Status));
                if (_ctx.IsPartial)
                    info.Meta.Add(("Type", $"Partial {_ctx.SequenceLetter}"));
                return info;
            }
        }

        public void RenderContent(ColumnDescriptor content)
        {
            content.Item().Element(RenderIndustrialControlPanel);

            if (_ctx.IsPartial)
            {
                content.Item()
                    .Background(AMBER)
                    .PaddingHorizontal(10).PaddingVertical(5)
                    .Text($"Partial invoice — sequence {_ctx.SequenceLetter}. Covers a portion of the order.")
                    .FontSize(M.MetaValueFont).Bold().FontColor(WHITE);
            }

            content.Item().Row(row =>
            {
                row.RelativeItem().Element(RenderSupplierBlock);
                row.ConstantItem(12);
                row.RelativeItem().Element(RenderCustomerBlock);
            });

            bool hasProject =
                !string.IsNullOrWhiteSpace(_ctx.ProjectIdentifierName) ||
                !string.IsNullOrWhiteSpace(_ctx.ProjectContractNo) ||
                !string.IsNullOrWhiteSpace(_ctx.ProjectReference) ||
                !string.IsNullOrWhiteSpace(_ctx.SiteDeliveryLocation);
            if (hasProject)
                content.Item().Element(RenderProjectBlock);

            content.Item().Element(RenderReferencesBar);

            foreach (var group in _ctx.SpecGroups)
                content.Item().Element(c => RenderSpecTable(c, group));

            if (_ctx.SpecGroups.Any())
                content.Item().Element(RenderGlassTotalsRow);

            content.Item().Element(RenderFinancialSummary);
            content.Item().Element(RenderAmountInWords);

            bool hasBank =
                !string.IsNullOrWhiteSpace(_ctx.BankName) ||
                !string.IsNullOrWhiteSpace(_ctx.BankAccountNumber) ||
                !string.IsNullOrWhiteSpace(_ctx.Iban) ||
                !string.IsNullOrWhiteSpace(_ctx.SwiftCode);
            if (hasBank)
                content.Item().Element(RenderBankDetails);

            content.Item().Element(RenderSignatureBlocks);

            content.Item()
                .Text("This is a computer-generated Tax Invoice. " +
                      "No signature is required for electronic copies.")
                .FontSize(M.MetaValueFont).Italic().FontColor(MUTED);
        }

        // ════════════════════════════════════════════════════════════════════
        //  SECTION RENDERERS
        // ════════════════════════════════════════════════════════════════════

        private static string GetStatusColor(string status) => status switch
        {
            "Paid" => GREEN,
            "PartiallyPaid" => GREEN,
            "Overdue" => RED,
            "Cancelled" => RED,
            "Void" => RED,
            "Issued" => AMBER,
            _ => DARK
        };


        private void RenderIndustrialControlPanel(IContainer container)
        {
            int totalQty = _ctx.SpecGroups.Sum(g => g.TotalQty);
            decimal totalSqm = _ctx.SpecGroups.Sum(g => g.TotalSqm);
            PdfChrome.IndustrialDocumentPanel(container,
                "VAT invoice fiscal control",
                "Industrial ERP tax invoice: VAT registration, customer/project references, delivery links, taxable value and balance controls are consolidated for finance approval.",
                ("Status", _ctx.Status, DocumentStyle.ColorAccent),
                ("Type", _ctx.IsPartial ? $"Partial {_ctx.SequenceLetter}" : "Final / Full", null),
                ("Pieces", totalQty.ToString(CultureInfo.InvariantCulture), null),
                ("Total SQM", totalSqm.ToString("0.000", CultureInfo.InvariantCulture), DocumentStyle.ColorAccent),
                ("Grand Total", $"AED {_ctx.GrandTotal:N2}", DocumentStyle.ColorSuccess));
        }

        private void RenderSupplierBlock(IContainer container)
        {
            PdfChrome.InfoCard(container, "From / Supplier", col =>
            {
                PdfChrome.LabelValue(col, "Company", _ctx.CompanyName);
                PdfChrome.LabelValue(col, "TRN", _ctx.CompanyTrn);
                PdfChrome.LabelValue(col, "Address", _ctx.CompanyAddress);
                PdfChrome.LabelValue(col, "Location", _ctx.CompanyLocation);
                PdfChrome.LabelValue(col, "Phone", _ctx.CompanyPhone);
                PdfChrome.LabelValue(col, "Email", _ctx.CompanyEmail);
            });
        }

        private void RenderCustomerBlock(IContainer container)
        {
            PdfChrome.InfoCard(container, "Bill To / Customer", col =>
            {
                PdfChrome.LabelValue(col, "Customer", _ctx.CustomerName);
                PdfChrome.LabelValue(col, "TRN", _ctx.CustomerTrn);
                PdfChrome.LabelValue(col, "Contact", _ctx.ContactPerson);
                if (!string.IsNullOrWhiteSpace(_ctx.CustomerAddress))
                    PdfChrome.LabelValue(col, "Address", _ctx.CustomerAddress);
            });
        }

        /// <summary>
        /// CHAT 20: Dedicated project section — separated from customer block
        /// for professional layout. Shows all 4 project fields in a bordered
        /// card with indigo accent header.
        /// </summary>
        private void RenderProjectBlock(IContainer container)
        {
            // Skip entire section if no project data at all
            bool hasAnyProjectData =
                !string.IsNullOrWhiteSpace(_ctx.ProjectIdentifierName) ||
                !string.IsNullOrWhiteSpace(_ctx.ProjectContractNo) ||
                !string.IsNullOrWhiteSpace(_ctx.ProjectReference) ||
                !string.IsNullOrWhiteSpace(_ctx.SiteDeliveryLocation);

            if (!hasAnyProjectData) return;

            PdfChrome.InfoCard(container, "Project", col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        if (!string.IsNullOrWhiteSpace(_ctx.ProjectIdentifierName))
                            PdfChrome.LabelValue(left, "Project Name",
                                _ctx.ProjectIdentifierName);
                        if (!string.IsNullOrWhiteSpace(_ctx.ProjectContractNo))
                            PdfChrome.LabelValue(left, "Contract No",
                                _ctx.ProjectContractNo);
                    });
                    row.ConstantItem(16);
                    row.RelativeItem().Column(right =>
                    {
                        if (!string.IsNullOrWhiteSpace(_ctx.ProjectReference))
                            PdfChrome.LabelValue(right, "Reference",
                                _ctx.ProjectReference);
                        if (!string.IsNullOrWhiteSpace(_ctx.SiteDeliveryLocation))
                            PdfChrome.LabelValue(right, "Site",
                                _ctx.SiteDeliveryLocation);
                    });
                });
            });
        }

        private void RenderReferencesBar(IContainer container)
        {
            var items = new List<(string, string)>
            {
                ("PI", _ctx.ProformaId),
                ("DN", _ctx.DeliveryNoteRef),
                ("Customer PO", _ctx.PurchaseOrderNumber),
                ("Terms", _ctx.PaymentTerms)
            };
            PdfChrome.MetaStrip(container, items);
        }

        private void RenderSpecTable(IContainer container, InvRenderSpecGroup group)
        {
            container.PaddingBottom(10).Column(col =>
            {
                col.Item().Element(c =>
                    PdfChrome.GroupBanner(c,
                        $"Specification {group.GroupNumber}: {group.GroupName}"));

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(24);
                        c.RelativeColumn(1.6f);
                        c.ConstantColumn(36);
                        c.ConstantColumn(36);
                        c.ConstantColumn(32);
                        c.ConstantColumn(32);
                        c.ConstantColumn(30);
                        c.RelativeColumn(0.75f);
                        c.RelativeColumn(0.85f);
                        c.RelativeColumn(0.80f);
                        c.RelativeColumn(0.95f);
                    });

                    table.Header(header =>
                    {
                        string[] heads =
                        {
                            "#", "GLASS REF", "W1", "H1", "W2", "H2",
                            "QTY", "SQM", "TOTAL SQM", "RATE", "VALUE"
                        };
                        string[] colors =
                        {
                            ExportColumnPalette.Identity, ExportColumnPalette.Identity,
                            ExportColumnPalette.Pane1, ExportColumnPalette.Pane1,
                            ExportColumnPalette.Pane2, ExportColumnPalette.Pane2,
                            ExportColumnPalette.Qty,
                            ExportColumnPalette.Area, ExportColumnPalette.Area,
                            ExportColumnPalette.Money, ExportColumnPalette.Money
                        };
                        for (int i = 0; i < heads.Length; i++)
                        {
                            bool center = i == 0 || (i >= 6 && i <= 8);
                            bool right = (i >= 2 && i <= 5) || i >= 9;
                            PdfChrome.HeaderCell(header.Cell(), heads[i],
                                center: center, right: right,
                                background: colors[i]);
                        }
                    });

                    bool altRow = false;
                    foreach (var row in group.Rows)
                    {
                        PdfChrome.DataCell(table.Cell(), row.Serial.ToString(),
                            alt: altRow, center: true);
                        PdfChrome.DataCell(table.Cell(), row.GlassRef, alt: altRow);
                        PdfChrome.DataCell(table.Cell(), row.W1.ToString(),
                            alt: altRow, right: true,
                            background: ExportColumnPalette.Pane1Fill);
                        PdfChrome.DataCell(table.Cell(), row.H1.ToString(),
                            alt: altRow, right: true,
                            background: ExportColumnPalette.Pane1Fill);
                        PdfChrome.DataCell(table.Cell(),
                            row.W2 > 0 ? row.W2.ToString() : "—",
                            alt: altRow, right: true,
                            background: ExportColumnPalette.Pane2Fill);
                        PdfChrome.DataCell(table.Cell(),
                            row.H2 > 0 ? row.H2.ToString() : "—",
                            alt: altRow, right: true,
                            background: ExportColumnPalette.Pane2Fill);
                        PdfChrome.DataCell(table.Cell(), row.Qty.ToString(),
                            alt: altRow, center: true, bold: true,
                            background: ExportColumnPalette.QtyFill);
                        PdfChrome.DataCell(table.Cell(), row.Sqm.ToString("0.000"),
                            alt: altRow, center: true,
                            background: ExportColumnPalette.AreaFill);
                        PdfChrome.DataCell(table.Cell(), row.TotalSqm.ToString("0.000"),
                            alt: altRow, center: true, bold: true,
                            background: ExportColumnPalette.AreaFill);
                        PdfChrome.DataCell(table.Cell(), $"AED {row.Rate:N2}",
                            alt: altRow, right: true);
                        PdfChrome.DataCell(table.Cell(), $"AED {row.Value:N2}",
                            alt: altRow, right: true, bold: true, color: GREEN);
                        altRow = !altRow;
                    }
                });

                col.Item().Element(c =>
                    PdfChrome.SubtotalBar(c,
                        $"Group qty {group.TotalQty}  ·  {group.TotalSqm:0.000} sqm",
                        $"AED {group.TotalValue:N2}"));
            });
        }

        private void RenderGlassTotalsRow(IContainer container)
        {
            int totalQty = _ctx.SpecGroups.Sum(g => g.TotalQty);
            decimal totalSqm = _ctx.SpecGroups.Sum(g => g.TotalSqm);
            decimal totalValue = _ctx.SpecGroups.Sum(g => g.TotalValue);

            PdfChrome.GrandTotal(container,
                $"Glass items  ·  {totalQty} pcs  ·  {totalSqm:0.000} sqm",
                $"AED {totalValue:N2}");
        }

        private void RenderFinancialSummary(IContainer container)
        {
            container
                .PaddingTop(10)
                .Row(outer =>
                {
                    outer.RelativeItem(1);

                    outer.RelativeItem(1).Column(col =>
                    {
                        AddSummaryRow(col, "Subtotal:",
                            $"AED {_ctx.Subtotal:N2}");

                        if (_ctx.DiscountAmount > 0)
                        {
                            AddSummaryRow(col,
                                $"Discount ({_ctx.DiscountPercent:0.##}%):",
                                $"- AED {_ctx.DiscountAmount:N2}",
                                color: AMBER);
                        }

                        AddSummaryRow(col, "After Discount:",
                            $"AED {_ctx.AfterDiscount:N2}");

                        col.Item().PaddingVertical(3)
                            .LineHorizontal(1).LineColor(LINE);

                        AddSummaryRow(col, "UAE VAT (5%):",
                            $"AED {_ctx.VatAmount:N2}",
                            size: 10);

                        col.Item().PaddingTop(6).Element(c =>
                            PdfChrome.GrandTotal(c,
                                "Grand total (VAT inclusive)",
                                $"AED {_ctx.GrandTotal:N2}"));

                        if (_ctx.IsPartial && _ctx.PreviouslyInvoiced > 0)
                        {
                            col.Item().PaddingTop(6).Column(partial =>
                            {
                                AddSummaryRow(partial,
                                    "Previously Invoiced:",
                                    $"AED {_ctx.PreviouslyInvoiced:N2}",
                                    color: MUTED);

                                AddSummaryRow(partial,
                                    "Remaining Balance:",
                                    $"AED {_ctx.RemainingBalance:N2}",
                                    bold: true, color: AMBER);
                            });
                        }
                    });
                });
        }

        private void RenderAmountInWords(IContainer container)
        {
            PdfChrome.InfoCard(container, "Amount in words", col =>
            {
                col.Item()
                    .Text(TaxInvoiceMapper.AmountToWords(_ctx.GrandTotal))
                    .FontSize(M.GrandLabelFont).Italic().FontColor(DARK);
            });
        }

        private void RenderBankDetails(IContainer container)
        {
            if (string.IsNullOrWhiteSpace(_ctx.BankName)
                && string.IsNullOrWhiteSpace(_ctx.BankAccountNumber)
                && string.IsNullOrWhiteSpace(_ctx.Iban)
                && string.IsNullOrWhiteSpace(_ctx.SwiftCode))
                return;

            PdfChrome.InfoCard(container, "Bank / Payment", col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        PdfChrome.LabelValue(left, "Bank", _ctx.BankName);
                        PdfChrome.LabelValue(left, "Account", _ctx.BankAccountNumber);
                    });
                    row.ConstantItem(16);
                    row.RelativeItem().Column(right =>
                    {
                        PdfChrome.LabelValue(right, "IBAN", _ctx.Iban);
                        PdfChrome.LabelValue(right, "SWIFT / BIC", _ctx.SwiftCode);
                    });
                });

                if (!string.IsNullOrWhiteSpace(_ctx.PaymentNotes))
                {
                    col.Item().PaddingTop(4)
                        .Text(_ctx.PaymentNotes)
                        .FontSize(M.MetaValueFont).Italic().FontColor(MUTED);
                }
            });
        }

        // ═════════════════════════════════════════════════════════════════
        //  SIGNATURE BLOCKS — CHAT 17 FIX
        //  Removed .Height(1) on signature underlines.
        //  BorderBottom(1) alone renders the line without measurement crash.
        //  Added Text(" ") as content since BorderBottom needs content
        //  to have measurable height.
        // ═════════════════════════════════════════════════════════════════

        private void RenderSignatureBlocks(IContainer container)
        {
            PdfChrome.Signatures(container,
                new SignatureSlot
                {
                    Title = "Prepared by",
                    Body = col =>
                    {
                        col.Item().PaddingTop(4)
                            .Text(string.IsNullOrWhiteSpace(_ctx.PreparedByName)
                                ? "—" : _ctx.PreparedByName)
                            .FontSize(M.SubtotalValueFont).SemiBold().FontColor(DARK);
                        if (!string.IsNullOrWhiteSpace(_ctx.PreparedByTitle))
                            col.Item().PaddingTop(2)
                                .Text(_ctx.PreparedByTitle)
                                .FontSize(M.MetaValueFont).FontColor(MUTED);
                    }
                },
                new SignatureSlot
                {
                    Title = "Customer acknowledgement"
                });
        }

        // ════════════════════════════════════════════════════════════════════
        //  SNAPSHOT → RENDER CONTEXT
        // ════════════════════════════════════════════════════════════════════

        private static TaxInvoiceRenderCtx BuildRenderContext(
            SavedTaxInvoice entity,
            TaxInvoiceSnapshot snap)
        {
            var ctx = new TaxInvoiceRenderCtx
            {
                InvoiceNumber = entity.InvoiceNumber ?? "",
                IssueDateDisplay = entity.IssueDate == default
                    ? "—" : entity.IssueDate.ToString("dd MMM yyyy"),
                DueDateDisplay = entity.DueDate == default
                    ? "—" : entity.DueDate.ToString("dd MMM yyyy"),
                IsOverdue = entity.DueDate != default
                    && entity.DueDate < DateTime.Today
                    && entity.Status != "Paid"
                    && entity.Status != "Cancelled"
                    && entity.Status != "Void",
                Status = entity.Status ?? "",
                IsPartial = string.Equals(entity.InvoiceType,
                    "Partial", StringComparison.OrdinalIgnoreCase),
                SequenceLetter = entity.SequenceLetter ?? "",

                CompanyName = snap.Company?.CompanyName ?? "",
                CompanyTrn = snap.Company?.CompanyTrn ?? "",
                CompanyAddress = snap.Company?.CompanyAddress ?? "",
                CompanyLocation = snap.Company?.CompanyLocation ?? "",
                CompanyPhone = snap.Company?.CompanyPhone ?? "",
                CompanyEmail = snap.Company?.CompanyEmail ?? "",
                LogoPath = snap.Company?.LogoPath ?? "",
                LogoBase64 = snap.Company?.LogoBase64 ?? "",
                LogoLayout = snap.Company?.LogoLayout
                    ?? ProGlassAutomation.Models.MasterData.CompanyLogoLayout.None,
                LogoWidthMm = snap.Company?.LogoWidthMm ?? 22,
                LogoHeightMm = snap.Company?.LogoHeightMm ?? 26,

                CustomerName = snap.Customer?.CustomerName ?? "",
                CustomerTrn = snap.Customer?.CustomerTrn ?? "",
                ContactPerson = snap.Customer?.ContactPerson ?? "",
                CustomerAddress = snap.Customer?.CustomerAddress ?? "",

                // CHAT 20: Read new fields first, fall back to legacy fields
                // for invoices saved before the field rename.
#pragma warning disable CS0612
                ProjectIdentifierName = !string.IsNullOrWhiteSpace(snap.Project?.ProjectIdentifierName)
                    ? snap.Project.ProjectIdentifierName
                    : snap.Project?.ProjectName ?? "",

                ProjectContractNo = snap.Project?.ProjectContractNo ?? "",
                ProjectReference = snap.Project?.ProjectReference ?? "",

                SiteDeliveryLocation = !string.IsNullOrWhiteSpace(snap.Project?.SiteDeliveryLocation)
                    ? snap.Project.SiteDeliveryLocation
                    : snap.Project?.ProjectLocation ?? "",
#pragma warning restore CS0612

                ProformaId = snap.Header?.ProformaId ?? "",
                DeliveryNoteRef = snap.Header?.DeliveryNoteReference ?? "",
                PurchaseOrderNumber = snap.Header?.PurchaseOrderNumber ?? "",
                PaymentTerms = snap.Bank?.PaymentTerms ?? "",

                Subtotal = snap.Financial?.Subtotal ?? 0,
                DiscountPercent = snap.Financial?.DiscountPercent ?? 0,
                DiscountAmount = snap.Financial?.DiscountAmount ?? 0,
                AfterDiscount = snap.Financial?.AfterDiscount ?? 0,
                VatAmount = snap.Financial?.VatAmount ?? 0,
                GrandTotal = snap.Financial?.GrandTotal ?? 0,
                PreviouslyInvoiced = snap.Financial?.PreviouslyInvoicedTotal ?? 0,
                RemainingBalance = snap.Financial?.RemainingBalance ?? 0,

                BankName = snap.Bank?.BankName ?? "",
                BankAccountNumber = snap.Bank?.BankAccountNumber ?? "",
                Iban = snap.Bank?.Iban ?? "",
                SwiftCode = snap.Bank?.SwiftCode ?? "",
                PaymentNotes = snap.Bank?.PaymentNotes ?? "",

                PreparedByName = snap.PreparedBy?.Name ?? "",
                PreparedByTitle = snap.PreparedBy?.Title ?? ""
            };

            if (snap.SpecGroups != null)
            {
                foreach (var g in snap.SpecGroups)
                {
                    var group = new InvRenderSpecGroup
                    {
                        GroupNumber = g.GroupNumber,
                        GroupName = !string.IsNullOrWhiteSpace(g.SpecName)
                            ? g.SpecName
                            : (!string.IsNullOrWhiteSpace(g.GroupName)
                                ? g.GroupName
                                : $"Spec Group {g.GroupNumber}")
                    };

                    if (g.Rows != null)
                    {
                        foreach (var r in g.Rows)
                        {
                            decimal sqm = SqmCalculator.CalculatePanel(r.W1, r.H1);
                            decimal totalSqm = Math.Round(sqm * r.Qty, 3);
                            decimal value = MoneyRounding.Round(r.Rate * r.Qty * sqm);

                            group.Rows.Add(new InvRenderRow
                            {
                                Serial = r.SerialNumber,
                                GlassRef = r.GlassRef ?? "",
                                W1 = r.W1,
                                H1 = r.H1,
                                W2 = r.W2,
                                H2 = r.H2,
                                Qty = r.Qty,
                                Sqm = sqm,
                                TotalSqm = totalSqm,
                                Rate = r.Rate,
                                Value = value
                            });

                            group.TotalQty += r.Qty;
                            group.TotalSqm += totalSqm;
                            group.TotalValue += value;
                        }
                    }

                    group.TotalSqm = Math.Round(group.TotalSqm, 3);
                    group.TotalValue = MoneyRounding.Round(group.TotalValue);
                    ctx.SpecGroups.Add(group);
                }
            }

            return ctx;
        }

        // ════════════════════════════════════════════════════════════════════
        //  TABLE CELL HELPERS
        // ════════════════════════════════════════════════════════════════════

        private static void AddLabelValue(
            ColumnDescriptor col, string label, string value)
        {
            col.Item().PaddingTop(1).Row(r =>
            {
                r.AutoItem()
                    .Text($"{label} ")
                    .FontSize(M.ValueFont).FontColor(MUTED);
                r.RelativeItem()
                    .Text(string.IsNullOrWhiteSpace(value) ? "—" : value)
                    .FontSize(M.ValueFont).SemiBold().FontColor(DARK);
            });
        }

        private static void AddSummaryRow(
            ColumnDescriptor col,
            string label,
            string value,
            bool bold = false,
            int size = 9,
            string? color = null)
        {
            col.Item().PaddingBottom(3).Row(r =>
            {
                var lbl = r.RelativeItem().Text(label).FontSize(size);
                if (bold) lbl.Bold();
                if (color != null) lbl.FontColor(color);

                var val = r.AutoItem().Text(value).FontSize(size);
                if (bold) val.Bold();
                if (color != null) val.FontColor(color);
            });
        }

        private static void AddTableCell(
            TableDescriptor table,
            string text,
            string bg,
            bool center = false,
            bool bold = false,
            string? color = null)
        {
            var cell = table.Cell()
                .Background(bg)
                .BorderBottom(1).BorderColor(LINE)
                .PaddingHorizontal(4).PaddingVertical(3);

            var txt = (center ? cell.AlignCenter() : cell).Text(text).FontSize(M.MetaValueFont);
            if (bold) txt.Bold();
            if (color != null) txt.FontColor(color);
        }

        private static void AddTableCellRight(
            TableDescriptor table,
            string text,
            string bg,
            bool bold = false,
            string? color = null)
        {
            var cell = table.Cell()
                .Background(bg)
                .BorderBottom(1).BorderColor(LINE)
                .PaddingHorizontal(4).PaddingVertical(3)
                .AlignRight();

            var txt = cell.Text(text).FontSize(M.MetaValueFont);
            if (bold) txt.Bold();
            if (color != null) txt.FontColor(color);
        }

        // ════════════════════════════════════════════════════════════════════
        //  PRIVATE RENDER CONTEXT DTOs
        // ════════════════════════════════════════════════════════════════════

        private sealed class TaxInvoiceRenderCtx
        {
            public string InvoiceNumber { get; set; } = "";
            public string IssueDateDisplay { get; set; } = "";
            public string DueDateDisplay { get; set; } = "";
            public bool IsOverdue { get; set; }
            public string Status { get; set; } = "";
            public bool IsPartial { get; set; }
            public string SequenceLetter { get; set; } = "";

            public string CompanyName { get; set; } = "";
            public string CompanyTrn { get; set; } = "";
            public string CompanyAddress { get; set; } = "";
            public string CompanyLocation { get; set; } = "";
            public string CompanyPhone { get; set; } = "";
            public string CompanyEmail { get; set; } = "";
            public string LogoPath { get; set; } = "";
            public string LogoBase64 { get; set; } = "";
            public ProGlassAutomation.Models.MasterData.CompanyLogoLayout LogoLayout
                { get; set; } = ProGlassAutomation.Models.MasterData.CompanyLogoLayout.None;
            public double LogoWidthMm { get; set; } = 22;
            public double LogoHeightMm { get; set; } = 26;

            public string CustomerName { get; set; } = "";
            public string CustomerTrn { get; set; } = "";
            public string ContactPerson { get; set; } = "";
            public string CustomerAddress { get; set; } = "";

            public string ProjectIdentifierName { get; set; } = "";
            public string ProjectContractNo { get; set; } = "";
            public string ProjectReference { get; set; } = "";
            public string SiteDeliveryLocation { get; set; } = "";

            public string ProformaId { get; set; } = "";
            public string DeliveryNoteRef { get; set; } = "";
            public string PurchaseOrderNumber { get; set; } = "";
            public string PaymentTerms { get; set; } = "";

            public decimal Subtotal { get; set; }
            public decimal DiscountPercent { get; set; }
            public decimal DiscountAmount { get; set; }
            public decimal AfterDiscount { get; set; }
            public decimal VatAmount { get; set; }
            public decimal GrandTotal { get; set; }
            public decimal PreviouslyInvoiced { get; set; }
            public decimal RemainingBalance { get; set; }

            public string BankName { get; set; } = "";
            public string BankAccountNumber { get; set; } = "";
            public string Iban { get; set; } = "";
            public string SwiftCode { get; set; } = "";
            public string PaymentNotes { get; set; } = "";

            public string PreparedByName { get; set; } = "";
            public string PreparedByTitle { get; set; } = "";

            public List<InvRenderSpecGroup> SpecGroups { get; } = new();
        }

        private sealed class InvRenderSpecGroup
        {
            public int GroupNumber { get; set; }
            public string GroupName { get; set; } = "";
            public int TotalQty { get; set; }
            public decimal TotalSqm { get; set; }
            public decimal TotalValue { get; set; }
            public List<InvRenderRow> Rows { get; } = new();
        }

        private sealed class InvRenderRow
        {
            public int Serial { get; set; }
            public string GlassRef { get; set; } = "";
            public int W1 { get; set; }
            public int H1 { get; set; }
            public int W2 { get; set; }
            public int H2 { get; set; }
            public int Qty { get; set; }
            public decimal Sqm { get; set; }
            public decimal TotalSqm { get; set; }
            public decimal Rate { get; set; }
            public decimal Value { get; set; }
        }
    }
}