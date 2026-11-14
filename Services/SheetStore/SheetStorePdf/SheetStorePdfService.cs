using ProGlassAutomation.Models.SheetStore;
using ProGlassAutomation.Services.Export.Pdf;
using ProGlassAutomation.Services.Export.Pdf.Renderers;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.SheetStore.SheetStorePdf
{
    /// <summary>
    /// Generates professionally formatted PDF inventory reports.
    /// Uses QuestPDF (MIT license).
    /// </summary>
    public class SheetStorePdfService
    {

        /// <summary>Derived rhythm + type scale for the active print layout.</summary>
        private static ProGlassAutomation.Services.Export.Pdf.PdfMetrics M
            => ProGlassAutomation.Services.Export.Pdf.DocumentStyle.Metrics;
        static SheetStorePdfService()
        {
            // Required for free/community use
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public Task GenerateReportAsync(string filePath, IList<GlassSheet> sheets,
                                         string filterInfo = "All sheets (no filters)")
        {
            return Task.Run(() =>
            {
                PdfThemeCatalog.ApplySaved();
                PdfChrome.Reset();
                var totalBuy = sheets.Sum(s => s.TotalPurchaseValue);
                var totalSell = sheets.Sum(s => s.TotalSellValue);
                var totalProfit = totalSell - totalBuy;
                var totalStock = sheets.Sum(s => s.Stock);
                var totalUsed = sheets.Sum(s => s.Used);
                var totalBalance = sheets.Sum(s => s.Balance);

                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4.Landscape());
                        page.Margin(DocumentStyle.MarginMm, Unit.Millimetre);
                        page.DefaultTextStyle(x => x
                            .FontFamily(DocumentStyle.FontFamily)
                            .FontSize(M.ValueFont)
                            .FontColor(DocumentStyle.ColorText));

                        page.Header().Element(h =>
                        {
                            var info = new LetterheadInfo
                            {
                                CompanyName = "ProGlass Automation",
                                DocumentTitle = "Sheet Store — Inventory Report",
                                DocumentId = $"{sheets.Count} items",
                                SubtitleLine = filterInfo
                            };
                            info.Meta.Add(("Generated", DateTime.Now.ToString("dd MMM yyyy  HH:mm")));
                            info.Meta.Add(("User", Environment.UserName));
                            info.Meta.Add(("Items", sheets.Count.ToString()));
                            PdfChrome.Letterhead(h, info);
                        });

                        // ============ CONTENT ============
                        page.Content().PaddingVertical(10).Column(col =>
                        {
                            // Summary cards
                            col.Item().Row(row =>
                            {
                            PdfChrome.KpiStrip(row.RelativeItem(),
                                ("Stock", $"{totalStock}", DocumentStyle.ColorSuccess),
                                ("Used", $"{totalUsed}", DocumentStyle.ColorDanger),
                                ("Balance", $"{totalBalance}", DocumentStyle.ColorWarning),
                                ("Buy", $"{totalBuy:N0} AED", DocumentStyle.ColorDanger),
                                ("Sell", $"{totalSell:N0} AED", DocumentStyle.ColorSuccess),
                                ("Profit", $"{totalProfit:N0} AED", DocumentStyle.ColorAccent));
                            });

                            col.Item().Height(8).Background(DocumentStyle.ColorWhite);

                            // Data table
                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(25);   // #
                                    columns.RelativeColumn(2.5f); // Category
                                    columns.ConstantColumn(45);   // Thick
                                    columns.RelativeColumn(1.2f); // Color
                                    columns.RelativeColumn(1.5f); // Size
                                    columns.RelativeColumn(2f);   // Supplier
                                    columns.ConstantColumn(50);   // SQM
                                    columns.ConstantColumn(60);   // Buy/SQM
                                    columns.ConstantColumn(60);   // Sell/SQM
                                    columns.ConstantColumn(70);   // Total Buy
                                    columns.ConstantColumn(70);   // Total Sell
                                    columns.ConstantColumn(40);   // Stock
                                    columns.ConstantColumn(40);   // Used
                                    columns.ConstantColumn(40);   // Balance
                                });

                                // Table header
                                table.Header(header =>
                                {
                                    void HeaderCell(string text) =>
                                        PdfChrome.HeaderCell(header.Cell(), text);

                                    HeaderCell("#");
                                    HeaderCell("CATEGORY");
                                    HeaderCell("THICK");
                                    HeaderCell("COLOR");
                                    HeaderCell("SIZE");
                                    HeaderCell("SUPPLIER");
                                    HeaderCell("SQM");
                                    HeaderCell("BUY/SQM");
                                    HeaderCell("SELL/SQM");
                                    HeaderCell("TOTAL BUY");
                                    HeaderCell("TOTAL SELL");
                                    HeaderCell("STOCK");
                                    HeaderCell("USED");
                                    HeaderCell("BAL");
                                });

                                // Table rows
                                for (int i = 0; i < sheets.Count; i++)
                                {
                                    var s = sheets[i];
                                    bool alt = i % 2 == 1;

                                    void DataCell(string text, string color = null)
                                    {
                                        PdfChrome.DataCell(table.Cell(), text,
                                            alt: alt, color: color);
                                    }

                                    DataCell($"{s.Id}");
                                    DataCell(s.Category, DocumentStyle.ColorAccent);
                                    DataCell(s.ThicknessDisplay);
                                    DataCell(s.Color);
                                    DataCell(s.SizeDisplay);
                                    DataCell(s.Supplier, DocumentStyle.ColorMuted);
                                    DataCell($"{s.SqmPerSheet:N4}");
                                    DataCell($"{s.PurchasePricePerSqm:N2}");
                                    DataCell($"{s.SellPricePerSqm:N2}");
                                    DataCell($"{s.TotalPurchaseValue:N0}", DocumentStyle.ColorDanger);
                                    DataCell($"{s.TotalSellValue:N0}", DocumentStyle.ColorSuccess);
                                    DataCell($"{s.Stock}", DocumentStyle.ColorSuccess);
                                    DataCell($"{s.Used}", DocumentStyle.ColorDanger);
                                    DataCell($"{s.Balance}", DocumentStyle.ColorWarning);
                                }
                            });

                            col.Item().PaddingVertical(8);

                            // Totals row
                            col.Item().Background(DocumentStyle.ColorAccentLight)
                                .Border(1).BorderColor(DocumentStyle.ColorBorder)
                                .Padding(10).Row(row =>
                                {
                                    row.RelativeItem().Text("TOTALS").Bold().FontSize(M.SubtotalValueFont)
                                        .FontColor(DocumentStyle.ColorText);
                                    row.ConstantItem(120).Text($"Buy: {totalBuy:N0} AED")
                                        .Bold().FontSize(M.ValueFont).FontColor(DocumentStyle.ColorDanger);
                                    row.ConstantItem(120).Text($"Sell: {totalSell:N0} AED")
                                        .Bold().FontSize(M.ValueFont).FontColor(DocumentStyle.ColorSuccess);
                                    row.ConstantItem(120).Text($"Profit: {totalProfit:N0} AED")
                                        .Bold().FontSize(M.ValueFont).FontColor(DocumentStyle.ColorAccent);
                                    row.ConstantItem(80).Text($"Stock: {totalStock}")
                                        .Bold().FontSize(M.ValueFont).FontColor(DocumentStyle.ColorSuccess);
                                    row.ConstantItem(80).Text($"Balance: {totalBalance}")
                                        .Bold().FontSize(M.ValueFont).FontColor(DocumentStyle.ColorWarning);
                                });
                        });

                        // ============ FOOTER ============
                        page.Footer().AlignCenter().Text(text =>
                        {
                            text.Span("ProGlass Automation — Sheet Store Report  •  Page ");
                            text.CurrentPageNumber();
                            text.Span(" of ");
                            text.TotalPages();
                            text.Span($"  •  © {DateTime.Now.Year}");
                        });
                    });
                }).GeneratePdf(filePath);
            });
        }
    }
}