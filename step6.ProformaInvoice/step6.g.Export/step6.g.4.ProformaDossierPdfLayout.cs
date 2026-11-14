using System;
using System.Linq;
using ProGlassAutomation.Models.Proforma;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ProGlassAutomation.Services.Proforma
{
    /// <summary>
    /// Full working dossier. Cost and margin are excluded unless the caller
    /// explicitly opts in; the internal version carries a warning on every page.
    /// </summary>
    public static class ProformaDossierPdfLayout
    {
        public static byte[] Render(
            ProformaInvoiceDraft draft,
            ProformaPdfStyle style,
            bool includeProfitLoss = false)
        {
            ArgumentNullException.ThrowIfNull(draft);
            ArgumentNullException.ThrowIfNull(style);
            ProformaInvoiceDraft invoice = draft.Clone();

            return Document.Create(document => document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(style.MarginMm, Unit.Millimetre);
                page.Background(style.Paper);
                page.DefaultTextStyle(text => text.FontFamily("Arial")
                    .FontSize(style.TableFontSize + 1f).FontColor(style.Text));

                if (includeProfitLoss)
                {
                    page.Header().Background("#B42318").Padding(6)
                        .Text("INTERNAL COPY — CONTAINS COST AND MARGIN. DO NOT SEND TO CUSTOMER.")
                        .Bold().FontSize(8).FontColor("#FFFFFF");
                }

                page.Content().Column(column =>
                {
                    column.Spacing(style.SectionGap);
                    column.Item().Element(c => ProformaPdfLayout.Letterhead(c, invoice, style));
                    column.Item().Element(c => KpiBand(c, invoice, style));
                    column.Item().Element(c => ProformaPdfLayout.Parties(c, invoice, style));
                    column.Item().Element(c => ProformaPdfLayout.Specifications(c, invoice, style));
                    if (ProformaPdfLayout.HasCharges(invoice))
                        column.Item().Element(c => ProformaPdfLayout.Charges(c, invoice, style));
                    if (!string.IsNullOrWhiteSpace(invoice.Notes))
                        column.Item().Element(c => ProformaPdfLayout.TextPanel(c, "NOTES", invoice.Notes, style));
                    if (!string.IsNullOrWhiteSpace(invoice.TermsText))
                        column.Item().Element(c => ProformaPdfLayout.TextPanel(c, "TERMS & CONDITIONS", invoice.TermsText, style));
                    if (includeProfitLoss)
                        column.Item().Element(c => ProfitLoss(c, invoice, style));
                    column.Item().Element(c => ProformaPdfLayout.Signatures(c, invoice, style));
                });

                page.Footer().PaddingTop(5).BorderTop(0.5f).BorderColor(style.Border)
                    .AlignCenter()
                    .DefaultTextStyle(text => text.FontSize(7).FontColor(style.Muted))
                    .Text(text =>
                    {
                        text.Span("DOSSIER · " + (string.IsNullOrWhiteSpace(invoice.ProformaNumber)
                            ? "DRAFT" : invoice.ProformaNumber));
                        text.Span("  ·  Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
            })).GeneratePdf();
        }

        private static void KpiBand(IContainer container, ProformaInvoiceDraft invoice, ProformaPdfStyle style)
        {
            container.Background(style.AccentDark).Padding(8).Row(row =>
            {
                Kpi(row, "SPECIFICATIONS", invoice.Groups.Count.ToString("N0"), style);
                Kpi(row, "PIECES", invoice.GridTotals.TotalPieces.ToString("N0"), style);
                Kpi(row, "NET", ProformaPdfLayout.Money(invoice.Summary.SubtotalBeforeVat), style);
                Kpi(row, "VAT", ProformaPdfLayout.Money(invoice.Summary.VatAmount), style);
                Kpi(row, "GRAND TOTAL", ProformaPdfLayout.Money(invoice.Summary.GrandTotal), style);
            });
        }

        private static void Kpi(RowDescriptor row, string label, string value, ProformaPdfStyle style)
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text(label).FontSize(6.5f).FontColor(style.AccentLight);
                column.Item().Text(value).FontSize(10).Bold().FontColor(style.AccentText);
            });
        }

        private static void ProfitLoss(IContainer container, ProformaInvoiceDraft invoice, ProformaPdfStyle style)
        {
            var result = invoice.ProfitLoss;
            container.Column(column =>
            {
                ProformaPdfLayout.SectionTitle(column, "PROFIT & LOSS — INTERNAL", style);
                column.Item().Background("#FDECEC").Padding(6)
                    .Text("INTERNAL FINANCIAL INFORMATION — DO NOT SEND TO CUSTOMER.")
                    .Bold().FontColor("#B42318").FontSize(8);

                column.Item().Row(row =>
                {
                    Kpi(row, "GLASS COST", ProformaPdfLayout.Money(result.TotalGlassCost), style);
                    Kpi(row, "PRODUCTION COST", ProformaPdfLayout.Money(result.TotalProductionCost), style);
                    Kpi(row, "TOTAL COST", ProformaPdfLayout.Money(result.TotalCost), style);
                    Kpi(row, "NET PROFIT", ProformaPdfLayout.Money(result.Profit), style);
                    Kpi(row, "MARGIN", result.MarginPercent is decimal margin ? margin.ToString("N1") + " %" : "—", style);
                });

                if (result.Rows.Count > 0)
                {
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(28);
                            columns.RelativeColumn(1.4f);
                            columns.RelativeColumn(2.3f);
                            columns.RelativeColumn(2.4f);
                            columns.ConstantColumn(48);
                            columns.ConstantColumn(58);
                        });
                        table.Header(header =>
                        {
                            ProformaPdfLayout.HeaderCell(header.Cell(), "SPEC", style);
                            ProformaPdfLayout.HeaderCell(header.Cell(), "KIND", style);
                            ProformaPdfLayout.HeaderCell(header.Cell(), "DESCRIPTION", style);
                            ProformaPdfLayout.HeaderCell(header.Cell(), "BASIS", style);
                            ProformaPdfLayout.HeaderCell(header.Cell(), "QTY", style);
                            ProformaPdfLayout.HeaderCell(header.Cell(), "COST AED", style);
                        });
                        int index = 0;
                        foreach (ProfitLossRow line in result.Rows)
                        {
                            bool alternate = style.ZebraRows && index % 2 == 1;
                            ProformaPdfLayout.BodyCell(table.Cell(), line.SpecNumber.ToString(), style, alternate);
                            ProformaPdfLayout.BodyCell(table.Cell(), line.Kind.ToString(), style, alternate);
                            ProformaPdfLayout.BodyCell(table.Cell(), line.Description, style, alternate);
                            ProformaPdfLayout.BodyCell(table.Cell(), line.CostBasis, style, alternate);
                            ProformaPdfLayout.BodyCell(table.Cell(), line.Quantity.ToString("0.###"), style, alternate, true);
                            ProformaPdfLayout.BodyCell(table.Cell(), ProformaPdfLayout.Money(line.Amount), style, alternate, true);
                            index++;
                        }
                    });
                }

                if (result.SpecTargets.Count > 0)
                {
                    ProformaPdfLayout.SectionTitle(column, "SPECIFICATION TARGET ANALYSIS", style);
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(28);
                            columns.RelativeColumn(2.4f);
                            columns.ConstantColumn(48);
                            columns.ConstantColumn(60);
                            columns.ConstantColumn(60);
                            columns.ConstantColumn(56);
                        });
                        table.Header(header =>
                        {
                            ProformaPdfLayout.HeaderCell(header.Cell(), "SPEC", style);
                            ProformaPdfLayout.HeaderCell(header.Cell(), "DESCRIPTION", style);
                            ProformaPdfLayout.HeaderCell(header.Cell(), "AREA m²", style);
                            ProformaPdfLayout.HeaderCell(header.Cell(), "GIVEN AED", style);
                            ProformaPdfLayout.HeaderCell(header.Cell(), "TARGET AED", style);
                            ProformaPdfLayout.HeaderCell(header.Cell(), "STATUS", style);
                        });
                        int index = 0;
                        foreach (SpecTargetRow target in result.SpecTargets)
                        {
                            bool alternate = style.ZebraRows && index % 2 == 1;
                            ProformaPdfLayout.BodyCell(table.Cell(), target.SpecNumber.ToString(), style, alternate);
                            ProformaPdfLayout.BodyCell(table.Cell(), target.SpecName, style, alternate);
                            ProformaPdfLayout.BodyCell(table.Cell(), ProformaPdfLayout.Area(target.AreaSqm), style, alternate, true);
                            ProformaPdfLayout.BodyCell(table.Cell(), ProformaPdfLayout.Money(target.GivenPricePerSqm), style, alternate, true);
                            ProformaPdfLayout.BodyCell(table.Cell(), ProformaPdfLayout.Money(target.SpecificationTargetPerSqm), style, alternate, true);
                            ProformaPdfLayout.BodyCell(table.Cell(), target.StatusText, style, alternate);
                            index++;
                        }
                    });
                }

                if (!string.IsNullOrWhiteSpace(result.BasisNote))
                    column.Item().Text("Costing basis: " + result.BasisNote).FontSize(7).FontColor(style.Muted);
                foreach (string warning in result.Warnings)
                    column.Item().Text("Warning: " + warning).FontSize(7).FontColor("#B42318");
            });
        }
    }
}