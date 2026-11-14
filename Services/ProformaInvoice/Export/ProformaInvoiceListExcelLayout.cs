// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/ProformaInvoice/Export/ProformaInvoiceListExcelLayout.cs
// PROJECT: ProGlassAutomation
// MODULE: Proforma Invoice — register (list) export
//
// Exports the CURRENT FILTERED invoice list as one styled Excel table —
// used by "share today's prepared invoices" and any list sharing.
// Styling reuses WorkbookStyle so the register matches every other
// ProGlass workbook.
// ═══════════════════════════════════════════════════════════════════════

using ClosedXML.Excel;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.Export.Excel;
using System;
using System.Collections.Generic;
using System.Linq;

using System.Globalization;
namespace ProGlassAutomation.Services.ProformaInvoice.Export
{
    /// <summary>Filtered PI list → one Excel worksheet table.</summary>
    public class ProformaInvoiceListExcelLayout : IExcelLayout
    {
        private readonly List<SavedInvoiceListItem> _items;
        private readonly string _title;

        public ProformaInvoiceListExcelLayout(
            List<SavedInvoiceListItem> items, string title)
        {
            _items = items ?? new List<SavedInvoiceListItem>();
            _title = string.IsNullOrWhiteSpace(title)
                ? "PROFORMA INVOICE REGISTER"
                : title.ToUpperInvariant();
        }

        // ── IDocumentLayout identity ────────────────────────────────────

        public string DocumentPrefix => "PI";
        public string DocumentId => "PI-REGISTER";
        public string DocumentTitle => _title;
        public string CompanyName => "ProGlass";
        public string Reference => "";
        public DateTime DocumentDate => DateTime.Now;

        // ── Rendering ───────────────────────────────────────────────────

        public void RenderWorkbook(IXLWorkbook workbook)
        {
            var ws = workbook.Worksheets.Add("Invoices");

            // Title block
            ws.Cell(1, 1).Value = _title;
            ws.Cell(1, 1).Style
                .Font.SetFontName(WorkbookStyle.FontFamily)
                .Font.SetFontSize(WorkbookStyle.FontSizeTitle)
                .Font.SetBold()
                .Font.SetFontColor(XLColor.FromHtml(WorkbookStyle.ColorAccent));

            ws.Cell(2, 1).Value =
                $"Generated {DateTime.Now:dd MMM yyyy HH:mm}  •  " +
                $"{_items.Count} invoice(s)";
            ws.Cell(2, 1).Style
                .Font.SetFontName(WorkbookStyle.FontFamily)
                .Font.SetFontSize(WorkbookStyle.FontSizeCaption)
                .Font.SetFontColor(XLColor.FromHtml(WorkbookStyle.ColorSubtitle));

            // Header row
            string[] headers =
            {
                "PI #", "Created", "Last Updated", "Customer", "Project",
                "Company", "Status", "Grand Total (AED)"
            };

            int headerRow = 4;
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(headerRow, c + 1);
                cell.Value = headers[c];
                cell.Style
                    .Font.SetFontName(WorkbookStyle.FontFamily)
                    .Font.SetFontSize(WorkbookStyle.FontSizeHeader)
                    .Font.SetBold()
                    .Font.SetFontColor(
                        XLColor.FromHtml(WorkbookStyle.ColorHeaderText))
                    .Fill.SetBackgroundColor(
                        XLColor.FromHtml(WorkbookStyle.ColorHeaderBg))
                    .Border.SetBottomBorder(XLBorderStyleValues.Thin)
                    .Border.SetBottomBorderColor(
                        XLColor.FromHtml(WorkbookStyle.ColorBorder))
                    .Alignment.SetHorizontal(
                        c == headers.Length - 1
                            ? XLAlignmentHorizontalValues.Right
                            : XLAlignmentHorizontalValues.Left);
            }

            // Data rows with alternating fill
            int row = headerRow + 1;
            foreach (var item in _items)
            {
                bool alt = (row - headerRow) % 2 == 0;

                SetText(ws, row, 1, item.ProformaId, bold: true);
                SetText(ws, row, 2, item.CreatedAt.ToString("dd MMM yyyy", CultureInfo.InvariantCulture));
                SetText(ws, row, 3, item.UpdatedAt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture));
                SetText(ws, row, 4, item.CustomerName);
                SetText(ws, row, 5, item.ProjectName);
                SetText(ws, row, 6, item.CompanyName);
                SetText(ws, row, 7, item.Status);
                SetMoney(ws, row, 8, item.GrandTotal);

                if (alt)
                {
                    for (int c = 1; c <= headers.Length; c++)
                    {
                        ws.Cell(row, c).Style.Fill.SetBackgroundColor(
                            XLColor.FromHtml(WorkbookStyle.ColorAltRowBg));
                    }
                }

                row++;
            }

            // Totals row
            SetText(ws, row, 6, "TOTAL", bold: true);
            SetMoney(ws, row, 8, _items.Sum(i => i.GrandTotal), bold: true);

            for (int c = 1; c <= headers.Length; c++)
            {
                ws.Cell(row, c).Style.Fill.SetBackgroundColor(
                    XLColor.FromHtml(WorkbookStyle.ColorTotalRowBg));
            }

            // Column widths
            ws.Column(1).Width = 16;
            ws.Column(2).Width = 13;
            ws.Column(3).Width = 18;
            ws.Column(4).Width = WorkbookStyle.WideColumn;
            ws.Column(5).Width = WorkbookStyle.WideColumn;
            ws.Column(6).Width = WorkbookStyle.MediumColumn;
            ws.Column(7).Width = 11;
            ws.Column(8).Width = 18;

            ws.SheetView.FreezeRows(headerRow);
        }

        private static void SetText(
            IXLWorksheet ws, int row, int col, string value, bool bold = false)
        {
            ws.Cell(row, col).Value = value ?? "";
            ws.Cell(row, col).Style
                .Font.SetFontName(WorkbookStyle.FontFamily)
                .Font.SetFontSize(WorkbookStyle.FontSizeBody)
                .Font.SetBold(bold);
        }

        private static void SetMoney(
            IXLWorksheet ws, int row, int col, decimal value, bool bold = false)
        {
            ws.Cell(row, col).Value = value;
            ws.Cell(row, col).Style
                .Font.SetFontName(WorkbookStyle.FontFamily)
                .Font.SetFontSize(WorkbookStyle.FontSizeBody)
                .Font.SetBold(bold)
                .NumberFormat.SetFormat("#,##0.00")
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        }
    }
}
