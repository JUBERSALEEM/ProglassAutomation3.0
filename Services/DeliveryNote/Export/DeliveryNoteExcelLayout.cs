using ClosedXML.Excel;
using ProGlassAutomation.Models.DeliveryNote.SavedDeliveryNote;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.Excel;
using ProGlassAutomation.Services.Export.FileSystem;
using ProGlassAutomation.Utilities;
using System;
using System.Linq;

using System.Globalization;
namespace ProGlassAutomation.Services.DeliveryNote.Export
{
    /// <summary>
    /// Excel layout for a Delivery Note — 2-sheet workbook.
    ///
    /// Sheet 1: Summary  — DN header, customer, logistics, totals
    /// Sheet 2: Items    — flat table of all glass items
    ///
    /// BUG-59: SetAutoFilter guarded — only when items exist.
    /// BUG-60: DocumentTitle uses shared constant.
    ///
    /// No pricing — delivery document only.
    /// </summary>
    public class DeliveryNoteExcelLayout : IExcelLayout
    {
        private readonly SavedDeliveryNote _entity;
        private readonly DeliveryNoteSnapshot _snapshot;
        private readonly DnJobOrderRefSnapshot _joRef;

        public DeliveryNoteExcelLayout(
            SavedDeliveryNote entity,
            DeliveryNoteSnapshot snapshot)
        {
            _entity = entity ?? throw new ArgumentNullException(nameof(entity));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _joRef = snapshot.JobOrderRef ?? new DnJobOrderRefSnapshot();
        }

        // ── IDocumentLayout ──────────────────────────────────────────────
        public string DocumentPrefix => "DN";
        public string DocumentId => _entity.DeliveryNoteId;
        public string DocumentTitle => DeliveryNoteExportConstants.DocumentTitle;
        public string CompanyName => ExportFileNamer.FirstFilled(
            _joRef.CustomerName, _entity.CustomerName);
        public string Reference => ExportFileNamer.FirstFilled(
            _joRef.ProjectName, _entity.ProjectName, _joRef.ProjectReference);
        public DateTime DocumentDate =>
            _entity.DeliveryDate != default
                ? _entity.DeliveryDate
                : _entity.CreatedAt;

        public void RenderWorkbook(IXLWorkbook workbook)
        {
            BuildSummarySheet(workbook);
            BuildItemsSheet(workbook);
        }

        // ═════════════════════════════════════════════════════════════════
        //  SHEET 1 — SUMMARY
        // ═════════════════════════════════════════════════════════════════

        private void BuildSummarySheet(IXLWorkbook workbook)
        {
            var ws = workbook.Worksheets.Add("Summary");
            ws.ShowGridLines = false;
            int r = 1;

            // Title strip
            ws.Cell(r, 1).Value = DeliveryNoteExportConstants.DocumentTitle;
            ws.Range(r, 1, r, 4).Merge()
                .Style.Fill.SetBackgroundColor(
                    XLColor.FromHtml("#" + WorkbookStyle.ColorHeaderBg))
                .Font.SetBold()
                .Font.SetFontColor(XLColor.White)
                .Font.SetFontSize(WorkbookStyle.FontSizeTitle);
            ws.Row(r).Height = 26;
            r += 2;

            // DN Identity
            SectionHeader(ws, r, "DN IDENTITY"); r++;
            LabelValue(ws, r++, "Delivery Note ID", _entity.DeliveryNoteId);
            LabelValue(ws, r++, "Job Order ID", _entity.JobOrderId);
            LabelValue(ws, r++, "Proforma ID", _entity.ProformaId);
            LabelValue(ws, r++, "Sequence No.",
                _entity.SequenceNumber.ToString());
            LabelValue(ws, r++, "Status", _entity.Status);
            LabelValue(ws, r++, "Type", _entity.DeliveryType);
            LabelValue(ws, r++, "FOC", _entity.IsFoc ? "Yes" : "No");
            r++;

            // Customer
            SectionHeader(ws, r, "CUSTOMER"); r++;
            LabelValue(ws, r++, "Customer Name", _joRef.CustomerName);
            LabelValue(ws, r++, "Project Name", _joRef.ProjectName);
            LabelValue(ws, r++, "Project Reference", _joRef.ProjectReference);
            LabelValue(ws, r++, "Company Name", _joRef.CompanyName);
            LabelValue(ws, r++, "Company TRN", _joRef.CompanyTrn);
            LabelValue(ws, r++, "Location", _joRef.CompanyLocation);
            r++;

            // Logistics
            SectionHeader(ws, r, "LOGISTICS"); r++;
            LabelValue(ws, r++, "Delivery Date",
                _entity.DeliveryDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture));
            LabelValue(ws, r++, "Delivery Time", _entity.DeliveryTime);
            LabelValue(ws, r++, "Driver Name", _entity.DriverName);
            LabelValue(ws, r++, "Vehicle No.", _entity.VehicleNumber);
            LabelValue(ws, r++, "Receiver Name", _entity.ReceiverName);
            LabelValue(ws, r++, "Notes", _entity.Notes);
            r++;

            // Quantity totals
            SectionHeader(ws, r, "QTY SUMMARY"); r++;
            LabelValue(ws, r++, "Total Ordered",
                _entity.TotalOrderedQty.ToString());
            LabelValue(ws, r++, "Total Delivered",
                _entity.TotalDeliveredQty.ToString());
            LabelValue(ws, r++, "Total Balance",
                _entity.TotalBalanceQty.ToString());
            LabelValue(ws, r++, "Total Items",
                (_entity.Items?.Count ?? 0).ToString());
            r++;

            // Tax invoice placeholder
            SectionHeader(ws, r, "TAX INVOICE"); r++;
            LabelValue(ws, r++, "Invoice ID",
                string.IsNullOrWhiteSpace(_entity.TaxInvoiceId)
                    ? "(not generated)" : _entity.TaxInvoiceId);
            LabelValue(ws, r++, "Has Invoice",
                _entity.HasTaxInvoice ? "Yes" : "No");
            r++;

            // Audit
            SectionHeader(ws, r, "AUDIT"); r++;
            LabelValue(ws, r++, "Created At",
                _entity.CreatedAt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture));
            LabelValue(ws, r++, "Updated At",
                _entity.UpdatedAt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture));

            ws.Column(1).Width = 22;
            ws.Column(2).Width = 50;
        }

        // ═════════════════════════════════════════════════════════════════
        //  SHEET 2 — ITEMS
        // ═════════════════════════════════════════════════════════════════

        private void BuildItemsSheet(IXLWorkbook workbook)
        {
            var ws = workbook.Worksheets.Add("Items");
            ws.ShowGridLines = false;
            int r = 1;

            string[] headers =
            {
                "DN ID", "JO ID", "Customer", "Spec #", "Spec Group",
                "Row #", "Glass Ref", "W1", "H1", "W2", "H2", "SQM",
                "Ordered", "Prev Delivered", "This DN", "Balance",
                "Weight/pc (kg)", "Total Wt (kg)"
            };
            string[] headerColors =
            {
                ExportColumnPalette.Identity, ExportColumnPalette.Identity,
                ExportColumnPalette.Identity, ExportColumnPalette.Identity,
                ExportColumnPalette.Identity, ExportColumnPalette.Identity,
                ExportColumnPalette.Identity,
                ExportColumnPalette.Pane1, ExportColumnPalette.Pane1,
                ExportColumnPalette.Pane2, ExportColumnPalette.Pane2,
                ExportColumnPalette.Area,
                ExportColumnPalette.Qty, ExportColumnPalette.Qty,
                ExportColumnPalette.Qty, ExportColumnPalette.Qty,
                ExportColumnPalette.Weight, ExportColumnPalette.Weight
            };

            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(r, c + 1);
                cell.Value = headers[c];
                cell.Style
                    .Fill.SetBackgroundColor(
                        XLColor.FromHtml(headerColors[c]))
                    .Font.SetBold()
                    .Font.SetFontColor(XLColor.White)
                    .Font.SetFontSize(WorkbookStyle.FontSizeHeader);
                cell.Style.Alignment
                    .SetHorizontal(XLAlignmentHorizontalValues.Center);
            }

            ws.Row(r).Height = 22;
            r++;

            string dnId = _entity.DeliveryNoteId;
            string joId = _entity.JobOrderId;
            string customer = _joRef.CustomerName;

            // BUG-59: guard against null items
            var orderedItems = (_entity.Items ?? new())
                .OrderBy(i => i.SpecGroupNumber)
                .ThenBy(i => i.SerialNumber)
                .ToList();

            bool alt = false;
            foreach (var item in orderedItems)
            {
                ws.Cell(r, 1).Value = dnId;
                ws.Cell(r, 2).Value = joId;
                ws.Cell(r, 3).Value = customer;
                ws.Cell(r, 4).Value = item.SpecGroupNumber;
                ws.Cell(r, 5).Value = item.SpecGroupName;
                ws.Cell(r, 6).Value = item.SerialNumber;
                ws.Cell(r, 7).Value = item.GlassRef ?? "";
                ws.Cell(r, 8).Value = item.W1;
                ws.Cell(r, 9).Value = item.H1;
                ws.Cell(r, 10).Value = item.W2;
                ws.Cell(r, 11).Value = item.H2;
                ws.Cell(r, 12).Value = SqmCalculator.CalculatePanel(item.W1, item.H1);
                ws.Cell(r, 12).Style.NumberFormat.Format = "0.000";
                ws.Cell(r, 13).Value = item.OrderedQty;
                ws.Cell(r, 14).Value = item.PreviouslyDeliveredQty;
                ws.Cell(r, 15).Value = item.DeliveredQty;
                ws.Cell(r, 16).Value = item.BalanceQty;
                ws.Cell(r, 17).Value = item.WeightPerPiece;
                ws.Cell(r, 18).Value = item.TotalWeight;

                PaintDimCells(ws, r);
                StyleDataRow(ws, r, item.BalanceQty > 0, alt);
                alt = !alt;
                r++;
            }

            // Totals row
            if (orderedItems.Count > 0)
            {
                ws.Cell(r, 1).Value = "TOTAL";
                ws.Range(r, 1, r, 12).Merge()
                    .Style.Font.SetBold()
                    .Fill.SetBackgroundColor(
                        XLColor.FromHtml("#" + WorkbookStyle.ColorTotalRowBg));

                ws.Cell(r, 13).Value = _entity.TotalOrderedQty;
                ws.Cell(r, 14).Value = _entity.TotalPreviouslyDeliveredQty;
                ws.Cell(r, 15).Value = _entity.TotalDeliveredQty;
                ws.Cell(r, 16).Value = _entity.TotalBalanceQty;
                ws.Cell(r, 17).Value = "";
                ws.Cell(r, 18).Value = orderedItems.Sum(i => i.TotalWeight);

                ws.Range(r, 13, r, 18).Style
                    .Font.SetBold()
                    .Fill.SetBackgroundColor(
                        XLColor.FromHtml("#" + WorkbookStyle.ColorTotalRowBg));

                r++;
            }

            // BUG-59: only apply auto-filter when items exist
            if (orderedItems.Count > 0)
                ws.Range(1, 1, r - 1, headers.Length).SetAutoFilter();

            // Column widths
            ws.Column(1).Width = 18;
            ws.Column(2).Width = 16;
            ws.Column(3).Width = 26;
            ws.Column(4).Width = 8;
            ws.Column(5).Width = 28;
            ws.Column(6).Width = 8;
            ws.Column(7).Width = 22;
            ws.Column(8).Width = 8;
            ws.Column(9).Width = 8;
            ws.Column(10).Width = 8;
            ws.Column(11).Width = 8;
            ws.Column(12).Width = 10;  // SQM
            ws.Column(13).Width = 10;
            ws.Column(14).Width = 14;
            ws.Column(15).Width = 10;
            ws.Column(16).Width = 10;
            ws.Column(17).Width = 14;
            ws.Column(18).Width = 14;

            ws.SheetView.FreezeRows(1);
        }

        // ═════════════════════════════════════════════════════════════════
        //  STYLING HELPERS
        // ═════════════════════════════════════════════════════════════════

        private static void SectionHeader(
            IXLWorksheet ws, int row, string title)
        {
            var cell = ws.Cell(row, 1);
            cell.Value = title;
            ws.Range(row, 1, row, 2).Merge()
                .Style.Fill.SetBackgroundColor(
                    XLColor.FromHtml("#" + WorkbookStyle.ColorAccent))
                .Font.SetBold()
                .Font.SetFontColor(XLColor.White);
            ws.Row(row).Height = 20;
        }

        private static void LabelValue(
            IXLWorksheet ws, int row, string label, string value)
        {
            var l = ws.Cell(row, 1);
            l.Value = label;
            l.Style.Font.SetBold();
            l.Style.Fill.SetBackgroundColor(
                XLColor.FromHtml("#" + WorkbookStyle.ColorAltRowBg));

            ws.Cell(row, 2).Value = value ?? "";
        }

        private static void PaintDimCells(IXLWorksheet ws, int row)
        {
            ws.Range(row, 8, row, 9).Style.Fill.SetBackgroundColor(
                XLColor.FromHtml(ExportColumnPalette.Pane1Fill));
            ws.Range(row, 10, row, 11).Style.Fill.SetBackgroundColor(
                XLColor.FromHtml(ExportColumnPalette.Pane2Fill));
            ws.Cell(row, 12).Style.Fill.SetBackgroundColor(
                XLColor.FromHtml(ExportColumnPalette.AreaFill));
            ws.Range(row, 13, row, 16).Style.Fill.SetBackgroundColor(
                XLColor.FromHtml(ExportColumnPalette.QtyFill));
            ws.Range(row, 17, row, 18).Style.Fill.SetBackgroundColor(
                XLColor.FromHtml(ExportColumnPalette.WeightFill));
        }

        private static void StyleDataRow(
            IXLWorksheet ws, int row, bool hasBalance, bool alt)
        {
            var range = ws.Range(row, 1, row, 18);
            range.Style.Font.SetFontSize(WorkbookStyle.FontSizeBody);
            range.Style.Alignment
                .SetVertical(XLAlignmentVerticalValues.Center);

            if (hasBalance)
            {
                ws.Cell(row, 16).Style
                    .Font.SetBold()
                    .Font.SetFontColor(XLColor.FromHtml("#DC2626"));
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  SHARED EXPORT CONSTANTS — BUG-60
    //  Single source of truth for DocumentTitle across all 3 layouts.
    // ═════════════════════════════════════════════════════════════════════

    public static class DeliveryNoteExportConstants
    {
        public const string DocumentTitle = "Delivery Note";
    }
}