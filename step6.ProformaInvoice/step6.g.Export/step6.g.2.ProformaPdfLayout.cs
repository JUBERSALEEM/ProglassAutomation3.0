using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProGlassAutomation.Models.Proforma;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ProGlassAutomation.Services.Proforma
{
    /// <summary>Renders the one customer invoice composition in any of the five PDF styles.</summary>
    public static class ProformaPdfLayout
    {
        static ProformaPdfLayout() => QuestPDF.Settings.License = LicenseType.Community;

        public static byte[] Render(ProformaInvoiceDraft draft, ProformaPdfStyle style)
        {
            ArgumentNullException.ThrowIfNull(draft);
            ArgumentNullException.ThrowIfNull(style);

            // Render a detached snapshot: printing must not mutate the live editor.
            ProformaInvoiceDraft invoice = draft.Clone();
            return Document.Create(document => document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(style.MarginMm, Unit.Millimetre);
                page.PageColor(style.Paper);
                page.DefaultTextStyle(text => text.FontFamily("Arial")
                    .FontSize(style.TableFontSize + 1f).FontColor(style.Text));

                page.Content().Column(column =>
                {
                    column.Spacing(style.SectionGap);
                    column.Item().Element(c => ProformaPdfLetterheadRenderer.Render(c, invoice, style));
                    if (style.RepeatTitleInBody)
                        column.Item().Text("PROFORMA INVOICE · " + invoice.ProformaNumber)
                            .Bold().FontSize(9).FontColor(style.Accent);
                    column.Item().Element(c => Parties(c, invoice, style));
                    column.Item().Element(c => Specifications(c, invoice, style));
                    if (HasCharges(invoice))
                        column.Item().Element(c => Charges(c, invoice, style));
                    column.Item().Element(c => FinancialSummary(c, invoice, style));
                    if (!string.IsNullOrWhiteSpace(invoice.Notes))
                        column.Item().Element(c => TextPanel(c, "NOTES", invoice.Notes, style));
                    if (!string.IsNullOrWhiteSpace(invoice.TermsText))
                        column.Item().Element(c => TextPanel(c, "TERMS & CONDITIONS", invoice.TermsText, style));
                    column.Item().Element(c => Signatures(c, invoice, style));
                });

                page.Footer().PaddingTop(5).BorderTop(0.5f).BorderColor(style.Border)
                    .AlignCenter()
                    .DefaultTextStyle(text => text.FontSize(7).FontColor(style.Muted))
                    .Text(text =>
                    {
                        text.Span(string.IsNullOrWhiteSpace(invoice.ProformaNumber)
                            ? "PROFORMA INVOICE" : invoice.ProformaNumber);
                        text.Span("  ·  Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
            })).GeneratePdf();
        }

        internal static void Parties(IContainer container, ProformaInvoiceDraft invoice, ProformaPdfStyle style)
        {
            container.Row(row =>
            {
                row.RelativeItem().Element(c => InfoCard(c, "BILL TO", new[]
                {
                    invoice.Parties.Client.ClientName,
                    Label("Attention", invoice.Parties.Client.AttentionName),
                    Label("Phone", invoice.Parties.Client.Phone),
                    Label("Email", invoice.Parties.Client.Email),
                    Label("TRN", invoice.Parties.Client.Trn),
                    invoice.Parties.Client.BillingAddress
                }, style));
                row.ConstantItem(8);
                row.RelativeItem().Element(c => InfoCard(c, "PROJECT", new[]
                {
                    invoice.Parties.Project.DisplayText,
                    Label("Location", invoice.Parties.Project.Location),
                    Label("Contract", invoice.Parties.Project.ContractReference),
                    Label("LPO", invoice.Parties.Project.LpoNumber),
                    Label("Inquiry", invoice.Parties.Inquiry.InquiryNumber),
                    Label("Prepared by", invoice.Parties.PreparedBy.DisplayText)
                }, style));
            });
        }

        internal static void Specifications(IContainer container, ProformaInvoiceDraft invoice, ProformaPdfStyle style)
        {
            container.Column(column =>
            {
                SectionTitle(column, "GLASS SPECIFICATIONS", style);
                foreach (SpecGroup group in invoice.Groups)
                {
                    column.Item().Background(style.AccentLight).Padding(6).Row(row =>
                    {
                        row.RelativeItem().Text(group.GroupNumber + ". " + PolishSubTypeInfo.DisplayGroupTitle(group))
                            .Bold().FontColor(style.AccentDark);
                        row.AutoItem().Text(group.GlassType + "  ·  " + Money(group.EffectiveRatePerSqm) + " / m²")
                            .FontSize(8).FontColor(style.Muted);
                    });
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(22);
                            columns.RelativeColumn(1.7f);
                            columns.RelativeColumn(2.1f);
                            columns.ConstantColumn(30);
                            columns.ConstantColumn(48);
                            columns.ConstantColumn(55);
                            columns.ConstantColumn(60);
                        });
                        table.Header(header =>
                        {
                            HeaderCell(header.Cell(), "#", style);
                            HeaderCell(header.Cell(), "REF", style);
                            HeaderCell(header.Cell(), "DIMENSIONS mm", style);
                            HeaderCell(header.Cell(), "QTY", style);
                            HeaderCell(header.Cell(), "AREA m²", style);
                            HeaderCell(header.Cell(), "RATE AED", style);
                            HeaderCell(header.Cell(), "VALUE AED", style);
                        });
                        int index = 0;
                        foreach (SpecRow specRow in group.Rows.Where(item => item.IsMeasurable))
                        {
                            bool alternate = style.ZebraRows && index % 2 == 1;
                            BodyCell(table.Cell(), specRow.SerialNumber.ToString(CultureInfo.InvariantCulture), style, alternate);
                            BodyCell(table.Cell(), specRow.GlassRef, style, alternate);
                            string dimensions = specRow.W1 + " × " + specRow.H1;
                            if (specRow.W2 > 0 && specRow.H2 > 0)
                                dimensions += " + " + specRow.W2 + " × " + specRow.H2;
                            BodyCell(table.Cell(), dimensions, style, alternate);
                            BodyCell(table.Cell(), specRow.Qty.ToString(CultureInfo.InvariantCulture), style, alternate, true);
                            BodyCell(table.Cell(), Area(specRow.TotalSqm), style, alternate, true);
                            decimal rate = specRow.RateOverridePerSqm > 0m
                                ? specRow.RateOverridePerSqm : group.EffectiveRatePerSqm;
                            BodyCell(table.Cell(), Money(rate), style, alternate, true);
                            BodyCell(table.Cell(), Money(specRow.ValueAt(group.EffectiveRatePerSqm)), style, alternate, true);
                            index++;
                        }
                    });
                    column.Item().AlignRight().Text(
                        "GROUP TOTAL  ·  " + group.TotalPieces + " pcs  ·  " + Area(group.TotalSqm)
                        + " m²  ·  AED " + Money(group.TotalValue))
                        .Bold().FontSize(style.TableFontSize + 0.5f).FontColor(style.Accent);
                }

                column.Item().Background(style.Accent).Padding(8).Row(row =>
                {
                    row.RelativeItem().Text("GRAND SPECIFICATION TOTAL")
                        .Bold().FontColor(style.AccentText);
                    row.AutoItem().Text(invoice.GridTotals.TotalPieces + " pcs  ·  "
                        + Area(invoice.GridTotals.TotalSqm) + " m²  ·  AED "
                        + Money(invoice.GridTotals.TotalValue))
                        .Bold().FontColor(style.AccentText);
                });
            });
        }

        internal static void Charges(IContainer container, ProformaInvoiceDraft invoice, ProformaPdfStyle style)
        {
            var lines = new List<(string Name, decimal Qty, string Unit, decimal Rate, decimal Amount)>();
            lines.AddRange(invoice.Charges.Predefined.Where(row => row.IsActive)
                .Select(row => (row.ChargeName, row.Quantity, row.UnitText, row.Rate, row.Amount)));
            lines.AddRange(invoice.Charges.Wastages.Where(row => row.IsActive)
                .Select(row => ("Wastage · " + row.ColorName + " (" + row.SheetQuantity + " sheets)",
                    row.AreaSqm, "m²", row.EffectiveRate, row.Amount)));
            lines.AddRange(invoice.Charges.Custom.Where(row => row.IsActive)
                .Select(row => (row.ChargeName, row.Qty, row.UnitText, row.Rate, row.Amount)));

            container.Column(column =>
            {
                SectionTitle(column, "OTHER CHARGES", style);
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3);
                        columns.ConstantColumn(48);
                        columns.ConstantColumn(54);
                        columns.ConstantColumn(62);
                        columns.ConstantColumn(68);
                    });
                    table.Header(header =>
                    {
                        HeaderCell(header.Cell(), "DESCRIPTION", style);
                        HeaderCell(header.Cell(), "QTY", style);
                        HeaderCell(header.Cell(), "UNIT", style);
                        HeaderCell(header.Cell(), "RATE AED", style);
                        HeaderCell(header.Cell(), "AMOUNT AED", style);
                    });
                    for (int i = 0; i < lines.Count; i++)
                    {
                        var line = lines[i];
                        bool alternate = style.ZebraRows && i % 2 == 1;
                        BodyCell(table.Cell(), line.Name, style, alternate);
                        BodyCell(table.Cell(), Quantity(line.Qty), style, alternate, true);
                        BodyCell(table.Cell(), line.Unit, style, alternate);
                        BodyCell(table.Cell(), Money(line.Rate), style, alternate, true);
                        BodyCell(table.Cell(), Money(line.Amount), style, alternate, true);
                    }
                });
                column.Item().AlignRight().Text("OTHER CHARGES SUBTOTAL  ·  AED "
                    + Money(invoice.Summary.OtherChargesSubtotal)).Bold().FontColor(style.Accent);
            });
        }

        internal static void FinancialSummary(IContainer container, ProformaInvoiceDraft invoice, ProformaPdfStyle style)
        {
            var summary = invoice.Summary;
            container.AlignRight().Width(280).Column(column =>
            {
                SectionTitle(column, "FINANCIAL SUMMARY", style);
                SummaryLine(column, "Glass", summary.GlassSubtotal, style);
                SummaryLine(column, "Other charges", summary.OtherChargesSubtotal, style);
                SummaryLine(column, "Net subtotal", summary.NetSubtotal, style);
                SummaryLine(column, "Discount", -summary.DiscountAmount, style);
                SummaryLine(column, "Before VAT", summary.SubtotalBeforeVat, style);
                SummaryLine(column, "VAT (" + (summary.IsVatApplicable ? summary.VatPercent : 0m)
                    .ToString("0.##", CultureInfo.InvariantCulture) + "%)", summary.VatAmount, style);
                column.Item().PaddingTop(5).Background(style.Accent).Padding(9).Row(row =>
                {
                    row.RelativeItem().Text("GRAND TOTAL").Bold().FontColor(style.AccentText);
                    row.AutoItem().Text("AED " + Money(summary.GrandTotal))
                        .FontSize(style.TitleSize).Bold().FontColor(style.AccentText);
                });
            });
        }

        internal static void Signatures(IContainer container, ProformaInvoiceDraft invoice, ProformaPdfStyle style)
        {
            container.Row(row =>
            {
                row.RelativeItem().Element(c => SignatureCard(c, "PREPARED BY",
                    invoice.Parties.PreparedBy.DisplayText, style));
                row.ConstantItem(10);
                row.RelativeItem().Element(c => SignatureCard(c, "CLIENT APPROVAL",
                    invoice.Parties.Client.ClientName, style));
            });
        }

        private static void SignatureCard(IContainer container, string title, string name, ProformaPdfStyle style)
        {
            container.Border(0.6f).BorderColor(style.Border).Padding(8).Column(column =>
            {
                column.Item().Text(title).Bold().FontSize(8).FontColor(style.Accent);
                column.Item().PaddingTop(4).Text(EmptyFallback(name, " ")).FontSize(8);
                column.Item().PaddingTop(22).Height(0.6f).Background(style.Border);
                column.Item().PaddingTop(3).Text("Signature / Date").FontSize(7).FontColor(style.Muted);
            });
        }

        internal static void InfoCard(IContainer container, string title, IEnumerable<string> values, ProformaPdfStyle style)
        {
            container.Border(0.6f).BorderColor(style.Border).Padding(8).Column(column =>
            {
                column.Item().Text(title).Bold().FontSize(8).FontColor(style.Accent);
                foreach (string value in values.Where(text => !string.IsNullOrWhiteSpace(text)))
                    column.Item().PaddingTop(2).Text(value).FontSize(8).FontColor(style.Text);
            });
        }

        internal static void TextPanel(IContainer container, string title, string value, ProformaPdfStyle style)
        {
            container.Border(0.6f).BorderColor(style.Border).Padding(8).Column(column =>
            {
                column.Item().Text(title).Bold().FontSize(8).FontColor(style.Accent);
                column.Item().PaddingTop(4).Text(value).FontSize(8).FontColor(style.Text);
            });
        }

        internal static void SectionTitle(ColumnDescriptor column, string title, ProformaPdfStyle style)
        {
            column.Item().PaddingBottom(3).BorderBottom(1).BorderColor(style.Highlight)
                .Text(title).Bold().FontSize(9).FontColor(style.Accent);
        }

        internal static void SummaryLine(ColumnDescriptor column, string label, decimal value, ProformaPdfStyle style)
        {
            column.Item().PaddingVertical(3).BorderBottom(0.4f).BorderColor(style.Border).Row(row =>
            {
                row.RelativeItem().Text(label).FontSize(8).FontColor(style.Muted);
                row.AutoItem().Text("AED " + Money(value)).FontSize(8).Bold().FontColor(style.Text);
            });
        }

        internal static void HeaderCell(IContainer cell, string value, ProformaPdfStyle style)
        {
            cell.Background(style.Accent).PaddingHorizontal(style.CellPaddingX)
                .PaddingVertical(style.CellPaddingY)
                .DefaultTextStyle(text => text.FontSize(style.TableFontSize - 0.5f)
                    .Bold().FontColor(style.AccentText))
                .Text(value);
        }

        internal static void BodyCell(IContainer cell, string value, ProformaPdfStyle style,
            bool alternate, bool right = false)
        {
            IContainer content = cell;
            if (style.TableGrid)
                content = content.Border(0.35f).BorderColor(style.Border);
            else
                content = content.BorderBottom(0.35f).BorderColor(style.Border);
            content = content.PaddingHorizontal(style.CellPaddingX).PaddingVertical(style.CellPaddingY);
            if (alternate) content = content.Background(style.AlternateRow);
            if (right) content = content.AlignRight();
            content.Text(value).FontSize(style.TableFontSize).FontColor(style.Text);
        }

        private static void AddLine(ColumnDescriptor column, string value, ProformaPdfStyle style)
        {
            if (!string.IsNullOrWhiteSpace(value))
                column.Item().Text(value).FontSize(7.5f).FontColor(style.Muted);
        }

        internal static bool HasCharges(ProformaInvoiceDraft invoice) =>
            invoice.Charges.Predefined.Any(row => row.IsActive)
            || invoice.Charges.Wastages.Any(row => row.IsActive)
            || invoice.Charges.Custom.Any(row => row.IsActive);

        private static string Label(string label, string value) =>
            string.IsNullOrWhiteSpace(value) ? "" : label + ": " + value.Trim();

        private static string EmptyFallback(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        private static string Date(DateTime value) =>
            value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

        internal static string Money(decimal value) =>
            value.ToString("#,##0.00;(#,##0.00)", CultureInfo.InvariantCulture);

        internal static string Area(decimal value) =>
            value.ToString("N3", CultureInfo.InvariantCulture);

        private static string Quantity(decimal value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}