using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ProGlassAutomation.Models.Proforma;

namespace ProGlassAutomation.Services.Proforma
{
    /// <summary>V3-owned Excel workbooks for invoices, internal dossiers and the register.</summary>
    public static class ProformaWorkbookExporter
    {
        private const string Navy = "#17324D";
        private const string Light = "#EAF0F6";
        private const string Alternate = "#F6F8FA";
        private const string Muted = "#5B6775";
        private const string Danger = "#B42318";

        public static XLWorkbook CreateInvoiceWorkbook(
            ProformaInvoiceDraft draft, bool includeProfitLoss = false)
        {
            ArgumentNullException.ThrowIfNull(draft);
            ProformaInvoiceDraft invoice = draft.Clone();
            var workbook = new XLWorkbook();
            WriteSummary(workbook.Worksheets.Add("Summary"), invoice);
            WriteSpecifications(workbook.Worksheets.Add("Specifications"), invoice);
            WriteCharges(workbook.Worksheets.Add("Other Charges"), invoice);
            WriteTerms(workbook.Worksheets.Add("Terms"), invoice);
            if (includeProfitLoss)
            {
                WriteProfitLoss(workbook.Worksheets.Add("Internal P&L"), invoice);
                WriteProfitLossTargets(workbook.Worksheets.Add("P&L Targets"), invoice);
            }
            return workbook;
        }

        public static XLWorkbook CreateRegisterWorkbook(IReadOnlyList<ProformaInvoiceListRow> rows)
        {
            ArgumentNullException.ThrowIfNull(rows);
            var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Invoice Register");
            string[] headers =
            {
                "PI Number", "Status", "Client", "Project", "Inquiry", "Prepared By",
                "Sales Representative", "Issue Date", "Valid Until", "Net Subtotal",
                "Grand Total", "Profit", "Updated UTC"
            };
            WriteTitle(sheet, "PROFORMA INVOICE REGISTER", headers.Length);
            WriteHeader(sheet, 3, headers);
            int row = 4;
            foreach (ProformaInvoiceListRow item in rows)
            {
                object[] values =
                {
                    item.ProformaNumber, item.Status, item.ClientName, item.ProjectName,
                    item.InquiryNumber, item.PreparedByName, item.SalesRepresentativeName,
                    item.IssueDate, item.ValidUntil, item.NetSubtotal, item.GrandTotal,
                    item.Profit, item.UpdatedAtUtc
                };
                for (int col = 0; col < values.Length; col++)
                    sheet.Cell(row, col + 1).Value = XLCellValue.FromObject(values[col]);
                row++;
            }
            FormatData(sheet, 3, row - 1, headers.Length);
            sheet.SheetView.FreezeRows(3);
            sheet.Columns().AdjustToContents();
            return workbook;
        }

        public static async Task ExportInvoiceAsync(
            ProformaInvoiceDraft draft, string outputPath, bool includeProfitLoss = false)
        {
            string fullPath = PreparePath(outputPath);
            ProformaInvoiceDraft snapshot = draft.Clone();
            await Task.Run(() =>
            {
                using XLWorkbook workbook = CreateInvoiceWorkbook(snapshot, includeProfitLoss);
                workbook.SaveAs(fullPath);
            }).ConfigureAwait(false);
        }

        public static async Task ExportRegisterAsync(
            IReadOnlyList<ProformaInvoiceListRow> rows, string outputPath)
        {
            string fullPath = PreparePath(outputPath);
            ProformaInvoiceListRow[] snapshot = rows.ToArray();
            await Task.Run(() =>
            {
                using XLWorkbook workbook = CreateRegisterWorkbook(snapshot);
                workbook.SaveAs(fullPath);
            }).ConfigureAwait(false);
        }

        public static string SuggestedInvoiceFileName(ProformaInvoiceDraft draft) =>
            "PI_" + SafeName(draft.ProformaNumber) + ".xlsx";

        public static string SuggestedRegisterFileName() =>
            "PI_Register_" + DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".xlsx";

        private static void WriteSummary(IXLWorksheet sheet, ProformaInvoiceDraft invoice)
        {
            WriteTitle(sheet, "PROFORMA INVOICE", 4);
            sheet.Cell(2, 1).Value = "PI Number"; sheet.Cell(2, 2).Value = invoice.ProformaNumber;
            sheet.Cell(3, 1).Value = "Status"; sheet.Cell(3, 2).Value = invoice.Status;
            sheet.Cell(4, 1).Value = "Issue Date"; sheet.Cell(4, 2).Value = invoice.IssueDate;
            sheet.Cell(5, 1).Value = "Valid Until"; sheet.Cell(5, 2).Value = invoice.ValidUntil;
            sheet.Cell(7, 1).Value = "Company"; sheet.Cell(7, 2).Value = invoice.Parties.Company.CompanyName;
            sheet.Cell(8, 1).Value = "Company TRN"; sheet.Cell(8, 2).Value = invoice.Parties.Company.Trn;
            sheet.Cell(9, 1).Value = "Client"; sheet.Cell(9, 2).Value = invoice.Parties.Client.ClientName;
            sheet.Cell(10, 1).Value = "Client TRN"; sheet.Cell(10, 2).Value = invoice.Parties.Client.Trn;
            sheet.Cell(11, 1).Value = "Project"; sheet.Cell(11, 2).Value = invoice.Parties.Project.DisplayText;
            sheet.Cell(12, 1).Value = "Inquiry"; sheet.Cell(12, 2).Value = invoice.Parties.Inquiry.InquiryNumber;
            sheet.Cell(13, 1).Value = "Prepared By"; sheet.Cell(13, 2).Value = invoice.Parties.PreparedBy.DisplayText;
            sheet.Cell(15, 1).Value = "FINANCIAL SUMMARY";
            sheet.Range(15, 1, 15, 2).Merge();
            string[] labels = { "Glass subtotal", "Other charges", "Net subtotal", "Discount",
                "Before VAT", "VAT", "Grand total" };
            decimal[] amounts =
            {
                invoice.Summary.GlassSubtotal, invoice.Summary.OtherChargesSubtotal,
                invoice.Summary.NetSubtotal, invoice.Summary.DiscountAmount,
                invoice.Summary.SubtotalBeforeVat, invoice.Summary.VatAmount,
                invoice.Summary.GrandTotal
            };
            for (int i = 0; i < labels.Length; i++)
            {
                sheet.Cell(16 + i, 1).Value = labels[i];
                sheet.Cell(16 + i, 2).Value = amounts[i];
                MoneyCell(sheet.Cell(16 + i, 2));
            }
            sheet.Cell(23, 1).Value = "VAT Applicable";
            sheet.Cell(23, 2).Value = invoice.Summary.IsVatApplicable ? "Yes" : "No";
            sheet.Cell(24, 1).Value = "VAT Rate";
            sheet.Cell(24, 2).Value = invoice.Summary.VatPercent;
            sheet.Cell(24, 2).Style.NumberFormat.Format = "0.00\"%\"";
            StyleLabelColumn(sheet, 2, 24);
            sheet.Columns().AdjustToContents();
        }

        private static void WriteSpecifications(IXLWorksheet sheet, ProformaInvoiceDraft invoice)
        {
            string[] headers =
            {
                "Group #", "Group", "Glass Type", "Specification", "Outer Colour",
                "Inner Colour", "Third Colour", "Row #", "Glass Ref", "W1 mm",
                "H1 mm", "W2 mm", "H2 mm", "Qty", "Unit m²", "Total m²",
                "Rate AED/m²", "Value AED"
            };
            WriteTitle(sheet, "GLASS SPECIFICATIONS AND CUT LIST", headers.Length);
            WriteHeader(sheet, 3, headers);
            int row = 4;
            foreach (SpecGroup group in invoice.Groups)
            {
                foreach (SpecRow item in group.Rows)
                {
                    object[] values =
                    {
                        group.GroupNumber, group.DisplayText, group.GlassType, group.SpecName,
                        group.OuterGlassColor, group.InnerGlassColor, group.ThirdGlassColor,
                        item.SerialNumber, item.GlassRef, item.W1, item.H1, item.W2, item.H2,
                        item.Qty, item.PanelSqm, item.TotalSqm,
                        item.RateOverridePerSqm > 0m ? item.RateOverridePerSqm : group.EffectiveRatePerSqm,
                        item.ValueAt(group.EffectiveRatePerSqm)
                    };
                    for (int col = 0; col < values.Length; col++)
                        sheet.Cell(row, col + 1).Value = XLCellValue.FromObject(values[col]);
                    row++;
                }
            }
            FormatData(sheet, 3, row - 1, headers.Length);
            sheet.SheetView.FreezeRows(3);
            sheet.Columns().AdjustToContents();
        }

        private static void WriteCharges(IXLWorksheet sheet, ProformaInvoiceDraft invoice)
        {
            string[] headers = { "Type", "Description", "Quantity", "Unit", "Rate AED", "Amount AED" };
            WriteTitle(sheet, "OTHER CHARGES", headers.Length);
            WriteHeader(sheet, 3, headers);
            int row = 4;
            foreach (PredefinedChargeRow item in invoice.Charges.Predefined.Where(x => x.IsActive))
                AddCharge(sheet, ref row, "Predefined", item.ChargeName, item.Quantity, item.UnitText, item.Rate, item.Amount);
            foreach (WastageChargeRow item in invoice.Charges.Wastages.Where(x => x.IsActive))
                AddCharge(sheet, ref row, "Wastage", item.ColorName + " (" + item.SheetQuantity + " sheets)",
                    item.AreaSqm, "m²", item.EffectiveRate, item.Amount);
            foreach (CustomChargeRow item in invoice.Charges.Custom.Where(x => x.IsActive))
                AddCharge(sheet, ref row, "Custom", item.ChargeName, item.Qty, item.UnitText, item.Rate, item.Amount);
            FormatData(sheet, 3, row - 1, headers.Length);
            sheet.SheetView.FreezeRows(3);
            sheet.Columns().AdjustToContents();
        }

        private static void WriteTerms(IXLWorksheet sheet, ProformaInvoiceDraft invoice)
        {
            WriteTitle(sheet, "NOTES AND TERMS", 2);
            sheet.Cell(3, 1).Value = "Notes"; sheet.Cell(3, 2).Value = invoice.Notes;
            sheet.Cell(5, 1).Value = "Terms & Conditions"; sheet.Cell(5, 2).Value = invoice.TermsText;
            sheet.Column(1).Width = 24;
            sheet.Column(2).Width = 110;
            sheet.Column(2).Style.Alignment.WrapText = true;
            sheet.Row(3).Height = 60;
            sheet.Row(5).Height = 120;
            StyleLabelColumn(sheet, 2, 5);
        }

        private static void WriteProfitLoss(IXLWorksheet sheet, ProformaInvoiceDraft invoice)
        {
            sheet.TabColor = XLColor.FromHtml(Danger);
            WriteTitle(sheet, "INTERNAL P&L — DO NOT SEND TO CUSTOMER", 8);
            sheet.Range(2, 1, 2, 8).Merge();
            sheet.Cell(2, 1).Value = "INTERNAL COPY — CONTAINS COST AND MARGIN. DO NOT SEND TO CUSTOMER.";
            sheet.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml(Danger);
            sheet.Cell(2, 1).Style.Font.Bold = true;
            var result = invoice.ProfitLoss;
            string[] labels = { "Net revenue ex VAT", "Glass cost", "Production cost", "Total cost", "Profit", "Margin %" };
            decimal?[] values = { result.NetExVat, result.TotalGlassCost, result.TotalProductionCost,
                result.TotalCost, result.Profit, result.MarginPercent };
            for (int i = 0; i < labels.Length; i++)
            {
                sheet.Cell(4 + i, 1).Value = labels[i];
                if (values[i].HasValue) sheet.Cell(4 + i, 2).Value = values[i]!.Value;
                if (i < 5) MoneyCell(sheet.Cell(4 + i, 2));
                else sheet.Cell(4 + i, 2).Style.NumberFormat.Format = "0.0\"%\"";
            }
            string[] headers = { "Kind", "Spec #", "Specification", "Description", "Sheet", "Quantity", "Rate AED/m²", "Amount AED" };
            if (!string.IsNullOrWhiteSpace(result.BasisNote))
            {
                sheet.Cell(10, 1).Value = "Costing basis";
                sheet.Cell(10, 2).Value = result.BasisNote;
                sheet.Range(10, 2, 10, 8).Merge();
            }
            int warningRow = 11;
            foreach (string warning in result.Warnings)
            {
                sheet.Cell(warningRow, 1).Value = "Warning";
                sheet.Cell(warningRow, 2).Value = warning;
                sheet.Range(warningRow, 2, warningRow, 8).Merge();
                sheet.Cell(warningRow, 1).Style.Font.FontColor = XLColor.FromHtml(Danger);
                warningRow++;
            }
            WriteHeader(sheet, Math.Max(12, warningRow + 1), headers);
            int tableHeaderRow = Math.Max(12, warningRow + 1);
            int dataStart = tableHeaderRow + 1;
            int currentRow = dataStart;
            foreach (ProfitLossRow item in result.Rows)
            {
                object[] valuesRow = { item.Kind.ToString(), item.SpecNumber, item.SpecName,
                    item.Description, item.SheetWidthMm > 0 && item.SheetHeightMm > 0
                        ? item.SheetWidthMm + " × " + item.SheetHeightMm : "",
                    item.Quantity, item.PricePerSqm, item.Amount };
                for (int col = 0; col < valuesRow.Length; col++)
                    sheet.Cell(currentRow, col + 1).Value = XLCellValue.FromObject(valuesRow[col]);
                currentRow++;
            }
            FormatData(sheet, tableHeaderRow, currentRow - 1, headers.Length);
            sheet.SheetView.FreezeRows(tableHeaderRow);
            sheet.Columns().AdjustToContents();
        }

        private static void WriteProfitLossTargets(IXLWorksheet sheet, ProformaInvoiceDraft invoice)
        {
            sheet.TabColor = XLColor.FromHtml(Danger);
            string[] headers =
            {
                "Spec #", "Specification", "Area m²", "Given AED/m²", "Cost Basis AED/m²",
                "Target Uplift %", "Target AED/m²", "Difference AED/m²",
                "Extra Profit AED/m²", "Margin vs Target %", "Extra Profit Margin %", "Status"
            };
            WriteTitle(sheet, "INTERNAL SPECIFICATION TARGET ANALYSIS", headers.Length);
            sheet.Range(2, 1, 2, headers.Length).Merge();
            sheet.Cell(2, 1).Value = "INTERNAL COPY — COST AND TARGET ANALYSIS. DO NOT SEND TO CUSTOMER.";
            sheet.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml(Danger);
            sheet.Cell(2, 1).Style.Font.Bold = true;
            WriteHeader(sheet, 4, headers);

            int row = 5;
            foreach (SpecTargetRow target in invoice.ProfitLoss.SpecTargets)
            {
                object[] values =
                {
                    target.SpecNumber, target.SpecName, target.AreaSqm,
                    target.GivenPricePerSqm, target.CostBasisPerSqm,
                    target.TargetUpliftPercent, target.SpecificationTargetPerSqm,
                    target.DifferencePerSqm, target.ExtraProfitPerSqm,
                    target.MarginAgainstTargetPercent ?? 0m,
                    target.ExtraProfitMarginPercent ?? 0m, target.StatusText
                };
                for (int col = 0; col < values.Length; col++)
                    sheet.Cell(row, col + 1).Value = XLCellValue.FromObject(values[col]);
                row++;
            }
            FormatData(sheet, 4, row - 1, headers.Length);
            sheet.SheetView.FreezeRows(4);
            sheet.Columns().AdjustToContents();
        }

        private static void AddCharge(IXLWorksheet sheet, ref int row, string type,
            string name, decimal qty, string unit, decimal rate, decimal amount)
        {
            sheet.Cell(row, 1).Value = type; sheet.Cell(row, 2).Value = name;
            sheet.Cell(row, 3).Value = qty; sheet.Cell(row, 4).Value = unit;
            sheet.Cell(row, 5).Value = rate; sheet.Cell(row, 6).Value = amount;
            MoneyCell(sheet.Cell(row, 5)); MoneyCell(sheet.Cell(row, 6)); row++;
        }

        private static void WriteTitle(IXLWorksheet sheet, string title, int columns)
        {
            sheet.Range(1, 1, 1, columns).Merge();
            sheet.Cell(1, 1).Value = title;
            sheet.Cell(1, 1).Style.Font.Bold = true;
            sheet.Cell(1, 1).Style.Font.FontSize = 16;
            sheet.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            sheet.Range(1, 1, 1, columns).Style.Fill.BackgroundColor = XLColor.FromHtml(Navy);
        }

        private static void WriteHeader(IXLWorksheet sheet, int row, IReadOnlyList<string> headers)
        {
            for (int col = 0; col < headers.Count; col++)
            {
                var cell = sheet.Cell(row, col + 1);
                cell.Value = headers[col];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml(Navy);
            }
        }

        private static void FormatData(IXLWorksheet sheet, int headerRow, int lastRow, int columns)
        {
            if (lastRow >= headerRow + 1)
            {
                sheet.Range(headerRow + 1, 1, lastRow, columns).Style.Alignment.Vertical =
                    XLAlignmentVerticalValues.Center;
                for (int row = headerRow + 1; row <= lastRow; row++)
                    if ((row - headerRow) % 2 == 0)
                        sheet.Range(row, 1, row, columns).Style.Fill.BackgroundColor = XLColor.FromHtml(Alternate);
                sheet.Range(headerRow, 1, lastRow, columns).SetAutoFilter();
            }
            sheet.SheetView.FreezeRows(headerRow);
        }

        private static void StyleLabelColumn(IXLWorksheet sheet, int firstRow, int lastRow)
        {
            for (int row = firstRow; row <= lastRow; row++)
            {
                sheet.Cell(row, 1).Style.Font.Bold = true;
                sheet.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml(Muted);
            }
        }

        private static void MoneyCell(IXLCell cell) =>
            cell.Style.NumberFormat.Format = "#,##0.00;[Red](#,##0.00)";

        private static string PreparePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Choose an output file path.", nameof(path));
            string fullPath = Path.GetFullPath(path);
            string? folder = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
            return fullPath;
        }

        private static string SafeName(string? value)
        {
            string name = string.IsNullOrWhiteSpace(value) ? "DRAFT" : value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '-');
            return name;
        }
    }
}