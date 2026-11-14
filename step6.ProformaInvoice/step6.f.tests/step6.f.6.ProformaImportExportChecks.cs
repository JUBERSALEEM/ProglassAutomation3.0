using System;
using System.Linq;
using System.IO;
using ClosedXML.Excel;
using ProGlassAutomation.Models.Proforma;
using ProGlassAutomation.Services.Proforma;

namespace ProGlassAutomation.Step6Tests
{
    internal static partial class Program
    {
        private static void ImportExport()
        {
            var csv = ProformaGridImportParser.ParseDelimitedText(
                "REF,W1,H1,QTY\r\nA1,1000,1200,2\r\nA2,800,900,1");
            Check("header-aware CSV cut-list import",
                csv.Rows.Count == 2 && csv.Rows[0].GlassRef == "A1"
                && csv.Rows[0].W1 == 1000 && csv.Rows[0].Qty == 2);

            var semicolon = ProformaGridImportParser.ParseDelimitedText(
                "REF;W1;H1;QTY\r\nB1;1,000;1,200;2");
            Check("semicolon import preserves decimal-comma dimensions",
                semicolon.Rows.Count == 1 && semicolon.Rows[0].W1 == 1000
                && semicolon.Rows[0].H1 == 1200);

            var shorthand = ProformaGridImportParser.ParseDelimitedText(
                "REF,DIM,QTY\r\nC1,900x1100,3");
            Check("dimension-pair cut-list import",
                shorthand.Rows.Count == 1 && shorthand.Rows[0].W1 == 900
                && shorthand.Rows[0].H1 == 1100 && shorthand.Rows[0].Qty == 3);

            var invalid = ProformaGridImportParser.ParseDelimitedText(
                "REF,W1,H1,QTY\r\nD1,1000,1200,0");
            Check("invalid zero-quantity rows are rejected", invalid.Rows.Count == 0);

            var json = ProformaGridImportParser.ParseJsonArray(
                "[{\"ref\":\"E1\",\"w1\":700,\"h1\":800,\"qty\":2}]");
            Check("scanner JSON cut-list import",
                json.Rows.Count == 1 && json.Rows[0].GlassRef == "E1");

            string tempXlsx = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                using (var source = new XLWorkbook())
                {
                    var sheet = source.Worksheets.Add("Cut List");
                    sheet.Cell(1, 1).Value = "REF";
                    sheet.Cell(1, 2).Value = "W1";
                    sheet.Cell(1, 3).Value = "H1";
                    sheet.Cell(1, 4).Value = "QTY";
                    sheet.Cell(2, 1).Value = "X1";
                    sheet.Cell(2, 2).Value = 1200;
                    sheet.Cell(2, 3).Value = 2400;
                    sheet.Cell(2, 4).Value = 2;
                    source.SaveAs(tempXlsx);
                }
                var xlsx = ProformaGridImportService.ReadExcel(tempXlsx);
                Check("XLSX first-sheet cut-list import",
                    xlsx.Rows.Count == 1 && xlsx.Rows[0].GlassRef == "X1"
                    && xlsx.Rows[0].W1 == 1200 && xlsx.Rows[0].Qty == 2);
            }
            finally
            {
                if (File.Exists(tempXlsx)) File.Delete(tempXlsx);
            }

            var draft = new ProformaInvoiceDraft();
            draft.EnsureStructure();
            draft.ProformaNumber = "PI-TEST";
            string cutList = ProformaCsvExporter.Render(draft);
            string header = cutList.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
            Check("cut-list CSV keeps the 13-column legacy contract",
                header.Split(',').Length == 13);

            using (XLWorkbook customer = ProformaWorkbookExporter.CreateInvoiceWorkbook(draft))
                Check("customer Excel workbook excludes internal P&L",
                    customer.Worksheets.Any(sheet => sheet.Name == "Summary")
                    && customer.Worksheets.Any(sheet => sheet.Name == "Specifications")
                    && !customer.Worksheets.Any(sheet => sheet.Name == "Internal P&L"));

            using (XLWorkbook internalBook =
                ProformaWorkbookExporter.CreateInvoiceWorkbook(draft, includeProfitLoss: true))
                Check("internal Excel workbook opts into P&L and target sheets",
                    internalBook.Worksheets.Any(sheet => sheet.Name == "Internal P&L")
                    && internalBook.Worksheets.Any(sheet => sheet.Name == "P&L Targets"));

            using (XLWorkbook register = ProformaWorkbookExporter.CreateRegisterWorkbook(
                new[] { new ProformaInvoiceListRow { ProformaNumber = "PI-TEST", ClientName = "Test Client" } }))
                Check("invoice register Excel export",
                    register.Worksheets.Any(sheet => sheet.Name == "Invoice Register"));
        }
    }
}
