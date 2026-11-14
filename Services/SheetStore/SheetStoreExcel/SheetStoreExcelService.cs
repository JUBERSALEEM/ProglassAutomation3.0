using ClosedXML.Excel;
using ProGlassAutomation.Models.SheetStore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.SheetStore.SheetStoreExcel
{
    /// <summary>
    /// Exports glass sheet data to formatted Excel (.xlsx) files.
    /// Uses ClosedXML (MIT license) for Excel generation.
    /// </summary>
    public class SheetStoreExcelService
    {
        /// <summary>
        /// Exports sheets to a professionally formatted Excel file.
        /// </summary>
        public Task ExportAsync(string filePath, IList<GlassSheet> sheets,
                                string filterInfo = "All sheets (no filters)")
        {
            return Task.Run(() =>
            {
                using var workbook = new XLWorkbook();
                var ws = workbook.Worksheets.Add("Sheet Store");

                // ============ HEADER ============
                var headerRow = 1;
                ws.Cell(headerRow, 1).Value = "ProGlass Automation — Sheet Store Inventory";
                ws.Range(headerRow, 1, headerRow, 15).Merge();
                ws.Cell(headerRow, 1).Style
                    .Font.SetBold(true)
                    .Font.SetFontSize(16)
                    .Font.SetFontColor(XLColor.White)
                    .Fill.SetBackgroundColor(XLColor.FromHtml("#1E88E5"))
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                ws.Row(headerRow).Height = 30;

                // ============ INFO ROW ============
                var infoRow = 2;
                ws.Cell(infoRow, 1).Value = $"Generated: {DateTime.Now:dd-MMM-yyyy HH:mm:ss}  •  {filterInfo}  •  {sheets.Count} items";
                ws.Range(infoRow, 1, infoRow, 15).Merge();
                ws.Cell(infoRow, 1).Style
                    .Font.SetFontSize(10)
                    .Font.SetItalic(true)
                    .Font.SetFontColor(XLColor.FromHtml("#64748B"))
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                // ============ COLUMN HEADERS ============
                var colRow = 4;
                string[] headers = {
                    "#", "Category", "Thickness", "Color", "Width (mm)", "Height (mm)",
                    "Size", "Supplier", "SQM/Sheet", "Buy/SQM", "Sell/SQM",
                    "Total Buy", "Total Sell", "Stock", "Used", "Balance"
                };

                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = ws.Cell(colRow, i + 1);
                    cell.Value = headers[i];
                    cell.Style
                        .Font.SetBold(true)
                        .Font.SetFontSize(10)
                        .Font.SetFontColor(XLColor.White)
                        .Fill.SetBackgroundColor(XLColor.FromHtml("#334155"))
                        .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                        .Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                        .Border.SetOutsideBorderColor(XLColor.FromHtml("#475569"));
                }

                // ============ DATA ROWS ============
                var dataStartRow = 5;
                decimal totalBuyValue = 0;
                decimal totalSellValue = 0;
                int totalStock = 0;
                int totalUsed = 0;
                int totalBalance = 0;

                for (int i = 0; i < sheets.Count; i++)
                {
                    var s = sheets[i];
                    var row = dataStartRow + i;
                    var isEven = i % 2 == 0;

                    ws.Cell(row, 1).Value = s.Id;
                    ws.Cell(row, 2).Value = s.Category;
                    ws.Cell(row, 3).Value = s.ThicknessDisplay;
                    ws.Cell(row, 4).Value = s.Color;
                    ws.Cell(row, 5).Value = s.WidthMm;
                    ws.Cell(row, 6).Value = s.HeightMm;
                    ws.Cell(row, 7).Value = s.SizeDisplay;
                    ws.Cell(row, 8).Value = s.Supplier;
                    ws.Cell(row, 9).Value = s.SqmPerSheet;
                    ws.Cell(row, 10).Value = s.PurchasePricePerSqm;
                    ws.Cell(row, 11).Value = s.SellPricePerSqm;
                    ws.Cell(row, 12).Value = s.TotalPurchaseValue;
                    ws.Cell(row, 13).Value = s.TotalSellValue;
                    ws.Cell(row, 14).Value = s.Stock;
                    ws.Cell(row, 15).Value = s.Used;
                    ws.Cell(row, 16).Value = s.Balance;

                    // Number formats
                    ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0.0000";
                    ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(row, 12).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(row, 13).Style.NumberFormat.Format = "#,##0.00";

                    // Alternating row colors
                    var bgColor = isEven
                        ? XLColor.FromHtml("#F8FAFC")
                        : XLColor.White;

                    ws.Range(row, 1, row, 16).Style
                        .Fill.SetBackgroundColor(bgColor)
                        .Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                        .Border.SetOutsideBorderColor(XLColor.FromHtml("#E2E8F0"));

                    // Color-code stock columns
                    ws.Cell(row, 14).Style.Font.SetFontColor(XLColor.FromHtml("#166534")); // Stock green
                    ws.Cell(row, 15).Style.Font.SetFontColor(XLColor.FromHtml("#991B1B")); // Used red
                    ws.Cell(row, 16).Style.Font.SetFontColor(XLColor.FromHtml("#92400E")); // Balance amber
                    ws.Cell(row, 12).Style.Font.SetFontColor(XLColor.FromHtml("#991B1B")); // Total Buy red
                    ws.Cell(row, 13).Style.Font.SetFontColor(XLColor.FromHtml("#166534")); // Total Sell green

                    ws.Cell(row, 14).Style.Font.SetBold(true);
                    ws.Cell(row, 15).Style.Font.SetBold(true);
                    ws.Cell(row, 16).Style.Font.SetBold(true);
                    ws.Cell(row, 12).Style.Font.SetBold(true);
                    ws.Cell(row, 13).Style.Font.SetBold(true);

                    // Accumulate totals
                    totalBuyValue += s.TotalPurchaseValue;
                    totalSellValue += s.TotalSellValue;
                    totalStock += s.Stock;
                    totalUsed += s.Used;
                    totalBalance += s.Balance;
                }

                // ============ TOTALS ROW ============
                var totalRow = dataStartRow + sheets.Count + 1;

                ws.Cell(totalRow, 1).Value = "TOTALS";
                ws.Range(totalRow, 1, totalRow, 11).Merge();
                ws.Cell(totalRow, 12).Value = totalBuyValue;
                ws.Cell(totalRow, 13).Value = totalSellValue;
                ws.Cell(totalRow, 14).Value = totalStock;
                ws.Cell(totalRow, 15).Value = totalUsed;
                ws.Cell(totalRow, 16).Value = totalBalance;

                ws.Cell(totalRow, 12).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(totalRow, 13).Style.NumberFormat.Format = "#,##0.00";

                ws.Range(totalRow, 1, totalRow, 16).Style
                    .Font.SetBold(true)
                    .Font.SetFontSize(11)
                    .Fill.SetBackgroundColor(XLColor.FromHtml("#EFF6FF"))
                    .Border.SetOutsideBorder(XLBorderStyleValues.Medium)
                    .Border.SetOutsideBorderColor(XLColor.FromHtml("#93C5FD"));

                ws.Cell(totalRow, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                ws.Cell(totalRow, 12).Style.Font.SetFontColor(XLColor.FromHtml("#991B1B"));
                ws.Cell(totalRow, 13).Style.Font.SetFontColor(XLColor.FromHtml("#166534"));
                ws.Cell(totalRow, 14).Style.Font.SetFontColor(XLColor.FromHtml("#166534"));
                ws.Cell(totalRow, 15).Style.Font.SetFontColor(XLColor.FromHtml("#991B1B"));
                ws.Cell(totalRow, 16).Style.Font.SetFontColor(XLColor.FromHtml("#92400E"));

                // ============ PROFIT ROW ============
                var profitRow = totalRow + 1;
                ws.Cell(profitRow, 1).Value = "EXPECTED PROFIT";
                ws.Range(profitRow, 1, profitRow, 11).Merge();
                ws.Cell(profitRow, 12).Value = totalSellValue - totalBuyValue;
                ws.Cell(profitRow, 12).Style.NumberFormat.Format = "#,##0.00";

                ws.Range(profitRow, 1, profitRow, 16).Style
                    .Font.SetBold(true)
                    .Font.SetFontSize(11)
                    .Font.SetFontColor(XLColor.FromHtml("#1E40AF"))
                    .Fill.SetBackgroundColor(XLColor.FromHtml("#DBEAFE"));

                ws.Cell(profitRow, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                // ============ AUTO-FIT COLUMNS ============
                ws.Columns().AdjustToContents();

                // ============ FREEZE HEADER ============
                ws.SheetView.FreezeRows(colRow);

                // ============ SAVE ============
                workbook.SaveAs(filePath);
            });
        }
    }
}