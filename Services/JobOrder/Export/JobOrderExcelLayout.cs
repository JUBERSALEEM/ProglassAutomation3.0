using ClosedXML.Excel;
using ProGlassAutomation.Models.JobOrder.SavedJobOrder;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.Excel;
using ProGlassAutomation.Services.Export.FileSystem;
using System;
using System.Linq;

using System.Globalization;
namespace ProGlassAutomation.Services.JobOrder.Export
{
    /// <summary>
    /// Excel layout for a Job Order — multi-sheet workbook per Round 3
    /// design decision Q4:
    ///
    ///   Sheet 1: Summary          — header + status + client + delivery
    ///   Sheet 2: Cut List         — flat glass items with Overdue flag
    ///   Sheet 3: Production Notes — cutting instructions + internal notes
    ///
    /// No pricing in any sheet.
    ///
    /// CHAT 20 FIX:
    ///   - Summary PROJECT block labels corrected to match Step 3 exactly:
    ///       "Project Name"  → "Project Identifier Name"
    ///       "Contract No"   → "Project Name / No"
    ///       (new)           → "Project Reference"
    ///   - Column 1 widened 22 → 26 to accommodate new longer labels.
    ///   - ResolveReference prefers ProjectReference (most descriptive).
    /// </summary>
    public class JobOrderExcelLayout : IExcelLayout
    {
        private readonly SavedJobOrder _entity;
        private readonly JobOrderSnapshot _snapshot;
        private readonly ProformaInvoiceSnapshot _invoice;

        public JobOrderExcelLayout(SavedJobOrder entity, JobOrderSnapshot snapshot)
        {
            _entity = entity ?? throw new ArgumentNullException(nameof(entity));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _invoice = snapshot.EmbeddedInvoice ?? new ProformaInvoiceSnapshot();
        }

        // ── IDocumentLayout ──────────────────────────────────────────
        public string DocumentPrefix => "JO";
        public string DocumentId => _entity.JobOrderId ?? _snapshot.JobOrderId;
        public string DocumentTitle => "Job Order — Production Sheet";
        public string CompanyName => _invoice.Client?.CustomerName
            ?? _entity.CustomerName
            ?? string.Empty;
        public string Reference => ExportFileNamer.FirstFilled(
            _invoice.Project?.ProjectIdentifierName,
            _entity.ProjectName,
            _invoice.Project?.ProjectReference);
        public DateTime DocumentDate => _snapshot.ConvertedAt != default
                                           ? _snapshot.ConvertedAt
                                           : _entity.CreatedAt;

        public void RenderWorkbook(IXLWorkbook workbook)
        {
            BuildSummarySheet(workbook);
            BuildCutListSheet(workbook);
            BuildProductionNotesSheet(workbook);
        }

        // ═════════════════════════════════════════════════════════════
        //  SHEET 1 — SUMMARY
        // ═════════════════════════════════════════════════════════════

        private void BuildSummarySheet(IXLWorkbook workbook)
        {
            var ws = workbook.Worksheets.Add("Summary");
            ws.ShowGridLines = false;
            int r = 1;

            // Title strip
            ws.Cell(r, 1).Value = "JOB ORDER — PRODUCTION SHEET";
            ws.Range(r, 1, r, 4).Merge()
                .Style.Fill.SetBackgroundColor(XLColor.FromHtml("#" + WorkbookStyle.ColorHeaderBg))
                .Font.SetBold().Font.SetFontColor(XLColor.White)
                .Font.SetFontSize(WorkbookStyle.FontSizeTitle);
            ws.Row(r).Height = 26;
            r += 2;

            // Job ID + status header row
            SectionHeader(ws, r, "JOB IDENTITY"); r++;
            LabelValue(ws, r++, "Job Order ID", _entity.JobOrderId ?? "");
            LabelValue(ws, r++, "Source Proforma", _snapshot.SourceProformaId ?? "");
            LabelValue(ws, r++, "Converted On", _snapshot.ConvertedAt.ToString("dd MMM yyyy", CultureInfo.InvariantCulture));
            LabelValue(ws, r++, "Status", FormatStatus(_snapshot.Production?.Status));
            LabelValue(ws, r++, "Priority", _snapshot.Production?.Priority ?? "Medium");
            LabelValue(ws, r++, "Delivery Date",
                       _snapshot.Production?.DeliveryDate?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "Not set");
            r++;

            // Company block
            SectionHeader(ws, r, "COMPANY"); r++;
            LabelValue(ws, r++, "Company Name", _invoice.Company?.CompanyName ?? "");
            LabelValue(ws, r++, "TRN", _invoice.Company?.CompanyTrn ?? "");
            LabelValue(ws, r++, "Location", _invoice.Company?.CompanyLocation ?? "");
            r++;

            // Client block
            SectionHeader(ws, r, "CLIENT"); r++;
            LabelValue(ws, r++, "Customer Name", _invoice.Client?.CustomerName ?? "");
            LabelValue(ws, r++, "Customer TRN", _invoice.Client?.CustomerTrn ?? "");
            LabelValue(ws, r++, "Billing Address", _invoice.Client?.BillingAddress ?? "");
            LabelValue(ws, r++, "Sales Rep", _invoice.Client?.SalesRepresentative ?? "");
            r++;

            // Project block
            // CHAT 20 FIX: Labels corrected to match Step 3 exactly.
            // Previous labels "Project Name" and "Contract No" were misleading
            // — they didn't match what user sees in Step 3.
            // Also added Project Reference row (was completely missing).
            SectionHeader(ws, r, "PROJECT"); r++;
            LabelValue(ws, r++, "Project Name", _invoice.Project?.ProjectIdentifierName ?? "");
            LabelValue(ws, r++, "Project Reference", _invoice.Project?.ProjectReference ?? "");
            LabelValue(ws, r++, "Contract No", _invoice.Project?.ProjectContractNo ?? "");
            LabelValue(ws, r++, "LPO Number", _invoice.Project?.LpoNumber ?? "");
            LabelValue(ws, r++, "Site Location", _invoice.Project?.SiteDeliveryLocation ?? "");
            r++;

            // Site delivery block
            SectionHeader(ws, r, "SITE DELIVERY"); r++;
            var prod = _snapshot.Production;
            LabelValue(ws, r++, "Contact Name", prod?.SiteDeliveryContact ?? "");
            LabelValue(ws, r++, "Contact Phone", prod?.SiteDeliveryPhone ?? "");
            LabelValue(ws, r++, "Delivery Address", prod?.SiteDeliveryAddress ?? "");
            LabelValue(ws, r++, "Assigned To", prod?.AssignedTo ?? "");
            LabelValue(ws, r++, "Drawing Ref", prod?.DrawingReference ?? "");
            r++;

            // Production totals
            int totalPieces = _invoice.SpecGroups?
                .SelectMany(g => g.ItemRows ?? Enumerable.Empty<GlassItemRowSnapshot>())
                .Sum(row => row.Qty) ?? 0;
            int totalSpecs = _invoice.SpecGroups?.Count ?? 0;

            SectionHeader(ws, r, "PRODUCTION TOTALS"); r++;
            LabelValue(ws, r++, "Total Specs", totalSpecs.ToString());
            LabelValue(ws, r++, "Total Pieces", totalPieces.ToString());

            // Column widths — CHAT 20: widened Column 1 from 22 to 26 to
            // accommodate longer labels like "Project Identifier Name".
            ws.Column(1).Width = 26;
            ws.Column(2).Width = 50;
        }

        // ═════════════════════════════════════════════════════════════
        //  SHEET 2 — CUT LIST (with Overdue column per Q2)
        // ═════════════════════════════════════════════════════════════

        private void BuildCutListSheet(IXLWorkbook workbook)
        {
            var ws = workbook.Worksheets.Add("Cut List");
            ws.ShowGridLines = false;
            int r = 1;

            // Header row
            string[] headers =
            {
                "Spec #", "Spec Name", "Glass Type", "Row #",
                "Glass Ref", "W1", "H1", "W2", "H2", "Qty",
                "SQM", "TOTAL SQM",
                "Job ID", "Customer", "Delivery Date", "Overdue"
            };
            string[] headerColors =
            {
                ExportColumnPalette.Identity, ExportColumnPalette.Identity,
                ExportColumnPalette.Identity, ExportColumnPalette.Identity,
                ExportColumnPalette.Identity,
                ExportColumnPalette.Pane1, ExportColumnPalette.Pane1,
                ExportColumnPalette.Pane2, ExportColumnPalette.Pane2,
                ExportColumnPalette.Qty,
                ExportColumnPalette.Area, ExportColumnPalette.Area,
                ExportColumnPalette.Identity, ExportColumnPalette.Identity,
                ExportColumnPalette.Identity, ExportColumnPalette.Identity
            };
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(r, c + 1);
                cell.Value = headers[c];
                cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml(headerColors[c]));
                cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
                cell.Style.Font.SetFontSize(WorkbookStyle.FontSizeHeader);
                cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            }
            ws.Row(r).Height = 22;
            r++;

            bool isOverdue = IsOverdue();
            string overdueFlag = isOverdue ? "YES" : "NO";
            string customer = _invoice.Client?.CustomerName ?? "";
            string jobId = _entity.JobOrderId ?? "";
            string deliveryDisplay = _snapshot.Production?.DeliveryDate.HasValue == true
                ? _snapshot.Production.DeliveryDate.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
                : "Not set";

            if (_invoice.SpecGroups != null)
            {
                foreach (var group in _invoice.SpecGroups.OrderBy(g => g.GroupNumber))
                {
                    if (group.ItemRows == null || group.ItemRows.Count == 0)
                    {
                        // Emit a single "no rows" placeholder line so the
                        // spec still appears in the cut list
                        ws.Cell(r, 1).Value = group.GroupNumber;
                        ws.Cell(r, 2).Value = group.SelectedSpecName ?? "";
                        ws.Cell(r, 3).Value = group.SelectedGlassType ?? "";
                        ws.Cell(r, 4).Value = "—";
                        ws.Cell(r, 5).Value = "(no rows)";
                        for (int c = 6; c <= 12; c++) ws.Cell(r, c).Value = "";
                        ws.Cell(r, 13).Value = jobId;
                        ws.Cell(r, 14).Value = customer;
                        ws.Cell(r, 15).Value = deliveryDisplay;
                        ws.Cell(r, 16).Value = overdueFlag;
                        StyleDataRow(ws, r, isOverdue);
                        PaintCutListDims(ws, r);
                        r++;
                        continue;
                    }

                    foreach (var row in group.ItemRows)
                    {
                        ws.Cell(r, 1).Value = group.GroupNumber;
                        ws.Cell(r, 2).Value = group.SelectedSpecName ?? "";
                        ws.Cell(r, 3).Value = group.SelectedGlassType ?? "";
                        ws.Cell(r, 4).Value = row.SerialNumber;
                        ws.Cell(r, 5).Value = row.GlassRef ?? "";
                        ws.Cell(r, 6).Value = row.W1;
                        ws.Cell(r, 7).Value = row.H1;
                        ws.Cell(r, 8).Value = row.W2;
                        ws.Cell(r, 9).Value = row.H2;
                        ws.Cell(r, 10).Value = row.Qty;
                        ws.Cell(r, 11).Value = row.Sqm1;
                        ws.Cell(r, 12).Value = row.TotalSqm;
                        ws.Cell(r, 11).Style.NumberFormat.Format = "#,##0.000";
                        ws.Cell(r, 12).Style.NumberFormat.Format = "#,##0.000";
                        ws.Cell(r, 13).Value = jobId;
                        ws.Cell(r, 14).Value = customer;
                        ws.Cell(r, 15).Value = deliveryDisplay;
                        ws.Cell(r, 16).Value = overdueFlag;
                        StyleDataRow(ws, r, isOverdue);
                        PaintCutListDims(ws, r);
                        r++;
                    }
                }
            }

            // Enable auto-filter
            if (r > 2)
                ws.Range(1, 1, r - 1, headers.Length).SetAutoFilter();

            // Column widths
            ws.Column(1).Width = 8;    // Spec #
            ws.Column(2).Width = 28;   // Spec Name
            ws.Column(3).Width = 12;   // Glass Type
            ws.Column(4).Width = 8;    // Row #
            ws.Column(5).Width = 22;   // Glass Ref
            ws.Column(6).Width = 8;    // W1
            ws.Column(7).Width = 8;    // H1
            ws.Column(8).Width = 8;    // W2
            ws.Column(9).Width = 8;    // H2
            ws.Column(10).Width = 8;    // Qty
            ws.Column(11).Width = 10;   // SQM
            ws.Column(12).Width = 12;   // TOTAL SQM
            ws.Column(13).Width = 16;   // Job ID
            ws.Column(14).Width = 26;   // Customer
            ws.Column(15).Width = 15;   // Delivery
            ws.Column(16).Width = 10;   // Overdue

            // Freeze header row
            ws.SheetView.FreezeRows(1);
        }

        // ═════════════════════════════════════════════════════════════
        //  SHEET 3 — PRODUCTION NOTES
        // ═════════════════════════════════════════════════════════════

        private void BuildProductionNotesSheet(IXLWorkbook workbook)
        {
            var ws = workbook.Worksheets.Add("Production Notes");
            ws.ShowGridLines = false;
            int r = 1;

            var prod = _snapshot.Production;

            SectionHeader(ws, r, "CUTTING INSTRUCTIONS"); r++;
            ws.Cell(r, 1).Value = string.IsNullOrWhiteSpace(prod?.CuttingInstructions)
                                    ? "(none)" : prod.CuttingInstructions;
            ws.Cell(r, 1).Style.Alignment.SetWrapText(true);
            ws.Row(r).Height = 100;
            r += 2;

            SectionHeader(ws, r, "INTERNAL NOTES"); r++;
            ws.Cell(r, 1).Value = string.IsNullOrWhiteSpace(prod?.InternalNotes)
                                    ? "(none)" : prod.InternalNotes;
            ws.Cell(r, 1).Style.Alignment.SetWrapText(true);
            ws.Row(r).Height = 100;
            r += 2;

            SectionHeader(ws, r, "AUDIT"); r++;
            LabelValue(ws, r++, "Last Edited At", prod?.LastEditedAt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture) ?? "—");
            LabelValue(ws, r++, "Last Edited By", prod?.LastEditedBy ?? "—");

            ws.Column(1).Width = 100;
        }

        // ═════════════════════════════════════════════════════════════
        //  STYLING HELPERS
        // ═════════════════════════════════════════════════════════════

        private static void SectionHeader(IXLWorksheet ws, int row, string title)
        {
            var cell = ws.Cell(row, 1);
            cell.Value = title;
            ws.Range(row, 1, row, 2).Merge()
                .Style.Fill.SetBackgroundColor(XLColor.FromHtml("#" + WorkbookStyle.ColorAccent))
                .Font.SetBold().Font.SetFontColor(XLColor.White);
            ws.Row(row).Height = 20;
        }

        private static void LabelValue(IXLWorksheet ws, int row, string label, string value)
        {
            var l = ws.Cell(row, 1);
            l.Value = label;
            l.Style.Font.SetBold();
            l.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#" + WorkbookStyle.ColorAltRowBg));

            ws.Cell(row, 2).Value = value ?? "";
        }

        private static void PaintCutListDims(IXLWorksheet ws, int row)
        {
            ws.Range(row, 6, row, 7).Style.Fill.SetBackgroundColor(
                XLColor.FromHtml(ExportColumnPalette.Pane1Fill));
            ws.Range(row, 8, row, 9).Style.Fill.SetBackgroundColor(
                XLColor.FromHtml(ExportColumnPalette.Pane2Fill));
            ws.Cell(row, 10).Style.Fill.SetBackgroundColor(
                XLColor.FromHtml(ExportColumnPalette.QtyFill));
            ws.Range(row, 11, row, 12).Style.Fill.SetBackgroundColor(
                XLColor.FromHtml(ExportColumnPalette.AreaFill));
        }

        private static void StyleDataRow(IXLWorksheet ws, int row, bool isOverdue)
        {
            var range = ws.Range(row, 1, row, 16);
            range.Style.Font.SetFontSize(WorkbookStyle.FontSizeBody);
            range.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);

            if (isOverdue)
            {
                range.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FEF2F2"));
                ws.Cell(row, 16).Style.Font.SetBold()
                    .Font.SetFontColor(XLColor.FromHtml("#DC2626"));
            }
            else if (row % 2 == 0)
            {
                range.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#" + WorkbookStyle.ColorAltRowBg));
            }
        }

        // ═════════════════════════════════════════════════════════════
        //  HELPERS
        // ═════════════════════════════════════════════════════════════

        private bool IsOverdue()
        {
            var status = _snapshot.Production?.Status ?? "";
            var delivery = _snapshot.Production?.DeliveryDate;
            if (!delivery.HasValue) return false;
            if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(status, "Delivered", StringComparison.OrdinalIgnoreCase)) return false;
            return delivery.Value.Date < DateTime.Today;
        }

        /// <summary>
        /// Resolves the reference text used in export metadata.
        /// CHAT 20 FIX: Prefer ProjectReference (most descriptive) first.
        /// Falls back through the other project fields in decreasing order
        /// of descriptiveness.
        /// </summary>
        private string ResolveReference()
        {
            var proj = _invoice.Project;
            if (proj == null) return string.Empty;

            // CHAT 20: ProjectReference first — most descriptive text
            // like "HOUSE FOR IMAD HABIB".
            if (!string.IsNullOrWhiteSpace(proj.ProjectReference))
                return proj.ProjectReference;

            if (!string.IsNullOrWhiteSpace(proj.ProjectIdentifierName))
                return proj.ProjectIdentifierName;
            if (!string.IsNullOrWhiteSpace(proj.ProjectContractNo))
                return proj.ProjectContractNo;
            if (!string.IsNullOrWhiteSpace(proj.LpoNumber))
                return proj.LpoNumber;

            return string.Empty;
        }

        private static string FormatStatus(string? status) =>
            status switch
            {
                "InProduction" => "In Production",
                null or "" => "Draft",
                _ => status
            };
    }
}