using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using CsvHelper;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.Csv;
using ProGlassAutomation.Services.Export.Excel;
using ProGlassAutomation.Services.Export.Pdf;
using ProGlassAutomation.Services.Export.Pdf.Renderers;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid;
using QuestPDF.Fluent;

namespace ProGlassAutomation.Services.ProformaInvoice.Step5Grid
{
    internal class CutListDocument : IDocumentLayout
    {
        private readonly string _id;
        private readonly string _customer;
        private readonly string _project;

        protected CutListDocument(
            string documentId,
            string? customer = null,
            string? project = null)
        {
            string raw = string.IsNullOrWhiteSpace(documentId) ? "PI" : documentId.Trim();
            _id = raw.EndsWith("-CUTLIST", StringComparison.OrdinalIgnoreCase)
                ? raw
                : raw + "-CUTLIST";
            _customer = customer ?? "";
            _project = project ?? "";
        }

        public string DocumentPrefix => "PI";
        public string DocumentId => _id;
        public string DocumentTitle => "CUT LIST";
        public string CompanyName => _customer;
        public string Reference => _project;
        public DateTime DocumentDate => DateTime.Today;
    }

    internal sealed class CutListExcelLayout : CutListDocument, IExcelLayout
    {
        private readonly IReadOnlyList<SpecGroup> _groups;
        public CutListExcelLayout(
            IReadOnlyList<SpecGroup> groups,
            string documentId,
            string? customer = null,
            string? project = null)
            : base(documentId, customer, project) => _groups = groups;

        public void RenderWorkbook(IXLWorkbook workbook)
        {
            var ws = workbook.Worksheets.Add("Cut list");
            string[] headers =
            {
                "Spec #", "Spec Name", "Glass Type", "Glass Ref",
                "W1", "H1", "W2", "H2", "Qty"
            };
            string[] headerColors =
            {
                ExportColumnPalette.Identity, ExportColumnPalette.Identity,
                ExportColumnPalette.Identity, ExportColumnPalette.Identity,
                ExportColumnPalette.Pane1, ExportColumnPalette.Pane1,
                ExportColumnPalette.Pane2, ExportColumnPalette.Pane2,
                ExportColumnPalette.Qty
            };
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(1, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml(headerColors[c]);
            }

            ws.Row(1).Style.Font.Bold = true;
            int r = 2;
            foreach (var group in _groups)
            {
                string spec = string.IsNullOrWhiteSpace(group.SelectedSpecName)
                    ? group.GroupName
                    : group.SelectedSpecName;
                string glassType = group.SelectedGlassType ?? "";
                foreach (var row in group.ItemRows)
                {
                    ws.Cell(r, 1).Value = group.GroupNumber;
                    ws.Cell(r, 2).Value = spec ?? "";
                    ws.Cell(r, 3).Value = glassType;
                    ws.Cell(r, 4).Value = row.GlassRef ?? "";
                    ws.Cell(r, 5).Value = row.W1;
                    ws.Cell(r, 6).Value = row.H1;
                    ws.Cell(r, 7).Value = row.W2;
                    ws.Cell(r, 8).Value = row.H2;
                    ws.Cell(r, 9).Value = row.Qty;
                    ws.Range(r, 5, r, 6).Style.Fill.BackgroundColor =
                        XLColor.FromHtml(ExportColumnPalette.Pane1Fill);
                    ws.Range(r, 7, r, 8).Style.Fill.BackgroundColor =
                        XLColor.FromHtml(ExportColumnPalette.Pane2Fill);
                    ws.Cell(r, 9).Style.Fill.BackgroundColor =
                        XLColor.FromHtml(ExportColumnPalette.QtyFill);
                    r++;
                }
            }
            ws.Columns().AdjustToContents();
        }
    }

    internal sealed class CutListCsvLayout : CutListDocument, ICsvLayout
    {
        private readonly IReadOnlyList<SpecGroup> _groups;
        public CutListCsvLayout(
            IReadOnlyList<SpecGroup> groups,
            string documentId,
            string? customer = null,
            string? project = null)
            : base(documentId, customer, project) => _groups = groups;

        public void WriteRows(CsvWriter writer)
        {
            writer.WriteField("Spec #");
            writer.WriteField("Spec Name");
            writer.WriteField("Glass Type");
            writer.WriteField("Glass Ref");
            writer.WriteField("W1");
            writer.WriteField("H1");
            writer.WriteField("W2");
            writer.WriteField("H2");
            writer.WriteField("Qty");
            writer.NextRecord();

            foreach (var group in _groups)
            {
                string spec = string.IsNullOrWhiteSpace(group.SelectedSpecName)
                    ? group.GroupName
                    : group.SelectedSpecName;
                string glassType = group.SelectedGlassType ?? "";
                foreach (var row in group.ItemRows)
                {
                    writer.WriteField(group.GroupNumber);
                    writer.WriteField(spec ?? "");
                    writer.WriteField(glassType);
                    writer.WriteField(row.GlassRef ?? "");
                    writer.WriteField(row.W1);
                    writer.WriteField(row.H1);
                    writer.WriteField(row.W2);
                    writer.WriteField(row.H2);
                    writer.WriteField(row.Qty);
                    writer.NextRecord();
                }
            }
        }
    }

    internal sealed class CutListPdfLayout : CutListDocument, IPdfLayout
    {
        private readonly IReadOnlyList<SpecGroup> _groups;
        public CutListPdfLayout(
            IReadOnlyList<SpecGroup> groups,
            string documentId,
            string? customer = null,
            string? project = null)
            : base(documentId, customer, project) => _groups = groups;

        public PageLayout PageLayout => PageLayout.A4Landscape;
        public LetterheadInfo? Letterhead => new()
        {
            DocumentTitle = "Cut list",
            DocumentId = DocumentId,
            SubtitleLine = $"{_groups.Count} spec group(s)"
        };

        public void RenderContent(ColumnDescriptor content)
        {
            foreach (var group in _groups)
            {
                string spec = string.IsNullOrWhiteSpace(group.SelectedSpecName)
                    ? group.GroupName
                    : group.SelectedSpecName;
                string glassType = group.SelectedGlassType ?? "";

                content.Item().Element(c =>
                    PdfChrome.GroupBanner(c,
                        $"Spec {group.GroupNumber}: {spec}" +
                        (string.IsNullOrWhiteSpace(glassType) ? "" : $"  ·  {glassType}")));

                content.Item().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(22);
                        cols.RelativeColumn(1.6f);
                        cols.ConstantColumn(40);
                        cols.ConstantColumn(40);
                        cols.ConstantColumn(40);
                        cols.ConstantColumn(40);
                        cols.ConstantColumn(36);
                    });

                    table.Header(h =>
                    {
                        string[] headers =
                        {
                            "#", "GLASS REF", "W1", "H1", "W2", "H2", "QTY"
                        };
                        for (int i = 0; i < headers.Length; i++)
                            PdfChrome.HeaderCell(h.Cell(), headers[i],
                                center: i == 0 || i == 6,
                                right: i >= 2 && i != 6);
                    });

                    bool alt = false;
                    foreach (var row in group.ItemRows)
                    {
                        PdfChrome.DataCell(table.Cell(), row.SerialNumber.ToString(), alt, center: true);
                        PdfChrome.DataCell(table.Cell(), row.GlassRef ?? "", alt);
                        PdfChrome.DataCell(table.Cell(), row.W1.ToString(), alt, right: true);
                        PdfChrome.DataCell(table.Cell(), row.H1.ToString(), alt, right: true);
                        PdfChrome.DataCell(table.Cell(), row.W2.ToString(), alt, right: true);
                        PdfChrome.DataCell(table.Cell(), row.H2.ToString(), alt, right: true);
                        PdfChrome.DataCell(table.Cell(), row.Qty.ToString(), alt, center: true);
                        alt = !alt;
                    }
                });

                content.Item().Element(c =>
                    PdfChrome.SubtotalBar(c,
                        $"Qty {group.FooterTotalQty}  ·  TOTAL SQM {group.FooterTotalSqm:0.000}",
                        $"AED {group.FooterTotalValue:N2}"));
            }
        }
    }
}
