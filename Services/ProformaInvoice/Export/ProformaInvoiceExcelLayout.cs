using ClosedXML.Excel;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.Excel;
using ProGlassAutomation.Services.Export.FileSystem;
using ProGlassAutomation.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

using System.Globalization;
using ProGlassAutomation.Services.Reporting;
namespace ProGlassAutomation.Services.ProformaInvoice.Export
{
    /// <summary>
    /// IExcelLayout implementation for Proforma Invoice.
    ///
    /// BUG-21 [CRITICAL]: Removed duplicated ComputeSpecMetrics /
    ///         ComputePolishLm / ComputeWastageQty. Uses shared
    ///         ProformaChargeMetricsCalculator.
    /// BUG-22: SqmCalculator.CalculatePanel() for panel area.
    /// BUG-23: DELIVERY hardcoded to 1 TRIP — documented tech debt.
    /// BUG-24: IsVatApplicable from snapshot.Summary (not VatAmount > 0).
    /// BUG-30: File size reduced via shared calculator.
    /// </summary>
    public class ProformaInvoiceExcelLayout : IExcelLayout
    {
        // Brand colours
        private const string ACCENT = "#4F46E5";
        private const string MUTED = "#475569";
        private const string LIGHT_BG = "#F1F5F9";
        private const string TABLE_HEADER = "#1E293B";
        private const string GREEN = "#047857";
        private const string ALT_ROW = "#F8FAFC";

        private readonly SavedProformaInvoice _entity;
        private readonly ProformaInvoiceSnapshot _snapshot;

        public ProformaInvoiceExcelLayout(
            SavedProformaInvoice entity,
            ProformaInvoiceSnapshot snapshot)
        {
            _entity = entity ?? throw new ArgumentNullException(nameof(entity));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        // ── IDocumentLayout ──────────────────────────────────────────────────

        public string DocumentPrefix => "PI";
        public string DocumentId => _entity.ProformaId;
        public string DocumentTitle => "PROFORMA INVOICE";
        public string CompanyName => _entity.CustomerName;
        public string Reference => ExportFileNamer.FirstFilled(
            _entity.ProjectName,
            _snapshot.Project?.ProjectIdentifierName,
            _snapshot.Project?.ProjectReference);
        public DateTime DocumentDate => _entity.IssueDate;

        // ── IExcelLayout ─────────────────────────────────────────────────────

        public void RenderWorkbook(IXLWorkbook workbook)
        {
            BuildSummarySheet(workbook.Worksheets.Add("Invoice Summary"));
            BuildGlassSheet(workbook.Worksheets.Add("Glass Panels"));

            var otherCharges = BuildOtherChargeLines();
            if (otherCharges.Any())
                BuildChargesSheet(
                    workbook.Worksheets.Add("Other Charges"),
                    otherCharges);

            var terms = GetActiveTerms();
            if (terms.Any())
                BuildTermsSheet(
                    workbook.Worksheets.Add("Terms & Conditions"),
                    terms);
        }

        // ════════════════════════════════════════════════════════════════════
        //  SHEET 1 — Invoice Summary
        //  BUG-24: IsVatApplicable check from snapshot.Summary
        // ════════════════════════════════════════════════════════════════════

        private void BuildSummarySheet(IXLWorksheet ws)
        {
            ws.Cell("A1").Value = "PROFORMA INVOICE";
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Cell("A1").Style.Font.FontSize = 16;
            ws.Cell("A1").Style.Font.FontColor = XLColor.FromHtml(ACCENT);
            ws.Range("A1:D1").Merge();

            int row = 3;

            AddSection(ws, ref row, "COMPANY DETAILS");
            AddField(ws, ref row, "Company Name", _snapshot.Company?.CompanyName ?? "");
            AddField(ws, ref row, "Company TRN", _snapshot.Company?.CompanyTrn ?? "");
            AddField(ws, ref row, "Location", _snapshot.Company?.CompanyLocation ?? "");
            AddField(ws, ref row, "Proforma ID", _snapshot.Company?.ProformaId ?? _entity.ProformaId);
            AddField(ws, ref row, "Issue Date", _snapshot.Company?.IssueDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "");
            AddField(ws, ref row, "Valid Until", _snapshot.Company?.ValidUntil.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "");
            AddField(ws, ref row, "Status", _snapshot.Company?.ProformaStatus ?? _entity.Status);
            row++;

            AddSection(ws, ref row, "CLIENT DETAILS");
            AddField(ws, ref row, "Customer Name", _snapshot.Client?.CustomerName ?? _entity.CustomerName);
            AddField(ws, ref row, "Customer TRN", _snapshot.Client?.CustomerTrn ?? "");
            AddField(ws, ref row, "Attention Name", _snapshot.Client?.AttentionExecutiveName ?? "");
            AddField(ws, ref row, "Contact Phone", _snapshot.Client?.ClientContactPhone ?? "");
            AddField(ws, ref row, "Billing Address", _snapshot.Client?.BillingAddress ?? "");
            AddField(ws, ref row, "Customer Reference", _snapshot.Client?.CustomerReferenceCode ?? "");
            AddField(ws, ref row, "Sales Representative", _snapshot.Client?.SalesRepresentative ?? "");
            row++;

            AddSection(ws, ref row, "PROJECT DETAILS");
            AddField(ws, ref row, "Project Name",
                _snapshot.Project?.ProjectIdentifierName ?? _entity.ProjectName);
            AddField(ws, ref row, "Project Reference",
                _snapshot.Project?.ProjectReference ?? "");
            AddField(ws, ref row, "Contract No", _snapshot.Project?.ProjectContractNo ?? "");
            AddField(ws, ref row, "Site Delivery", _snapshot.Project?.SiteDeliveryLocation ?? "");
            AddField(ws, ref row, "LPO Number", _snapshot.Project?.LpoNumber ?? "");
            row++;

            AddSection(ws, ref row, "FINANCIAL SUMMARY");
            AddField(ws, ref row, "Glass Materials Total",
                $"AED {_entity.GlassMaterialsSubtotal:N2}");
            AddField(ws, ref row, "Other Charges Total",
                $"AED {_entity.OtherChargesSubtotal:N2}");

            decimal netSubtotal = _entity.GlassMaterialsSubtotal
                                + _entity.OtherChargesSubtotal
                                - _entity.DiscountAmount;
            AddField(ws, ref row, "Net Subtotal",
                $"AED {netSubtotal:N2}");

            if (_entity.DiscountAmount > 0)
                AddField(ws, ref row, "Discount",
                    $"- AED {_entity.DiscountAmount:N2}");

            // BUG-24: IsVatApplicable from snapshot.Summary, not VatAmount > 0
            bool isVatApplicable = _snapshot.Summary?.IsVatApplicable ?? true;
            if (isVatApplicable)
            {
                decimal vatPct = _snapshot.Summary?.VatPercent ?? Reporting.VatCalculator.VAT_RATE_PERCENT;
                AddField(ws, ref row, $"VAT ({vatPct:0.##}%)",
                    $"AED {_entity.VatAmount:N2}");
            }

            AddField(ws, ref row, "GRAND TOTAL (VAT INC)",
                $"AED {_entity.GrandTotalVatInclusive:N2}");
            row++;

            AddSection(ws, ref row, "PREPARED BY");
            AddField(ws, ref row, "Name", _snapshot.PreparedBy?.PreparedByName ?? "");
            AddField(ws, ref row, "Department", _snapshot.PreparedBy?.PreparedByDept ?? "");
            AddField(ws, ref row, "Status", _snapshot.PreparedBy?.PreparedByStatus ?? "");

            ws.Column("A").Width = 25;
            ws.Column("B").Width = 40;
        }

        // ════════════════════════════════════════════════════════════════════
        //  SHEET 2 — Glass Panels
        //  BUG-22: uses SqmCalculator.CalculatePanel()
        // ════════════════════════════════════════════════════════════════════

        private void BuildGlassSheet(IXLWorksheet ws)
        {
            var headers = new[]
            {
                "Spec #", "Spec Name", "#", "Glass Ref",
                "W1", "H1", "W2", "H2",
                "Qty", "SQM", "TOTAL SQM",
                "Rate (AED)", "Total (AED)"
            };
            int totalCols = headers.Length;

            string[] headerColors =
            {
                ExportColumnPalette.Identity, ExportColumnPalette.Identity,
                ExportColumnPalette.Identity, ExportColumnPalette.Identity,
                ExportColumnPalette.Pane1, ExportColumnPalette.Pane1,
                ExportColumnPalette.Pane2, ExportColumnPalette.Pane2,
                ExportColumnPalette.Qty,
                ExportColumnPalette.Area, ExportColumnPalette.Area,
                ExportColumnPalette.Money, ExportColumnPalette.Money
            };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor =
                    XLColor.FromHtml(headerColors[i]);
                cell.Style.Alignment.Horizontal =
                    XLAlignmentHorizontalValues.Center;
            }

            int dataRow = 2;
            int allQty = 0;
            decimal allArea = 0m;
            decimal allValue = 0m;

            if (_snapshot.SpecGroups == null)
            {
                ws.Columns().AdjustToContents();
                return;
            }

            foreach (var g in _snapshot.SpecGroups)
            {
                string groupLabel =
                    !string.IsNullOrWhiteSpace(g.SelectedSpecName)
                        ? g.SelectedSpecName
                        : (!string.IsNullOrWhiteSpace(g.GroupName)
                            ? g.GroupName
                            : $"Spec Group {g.GroupNumber}");

                int groupQty = 0;
                decimal groupArea = 0m;
                decimal groupValue = 0m;

                if (g.ItemRows != null)
                {
                    foreach (var item in g.ItemRows)
                    {
                        // BUG-22: SqmCalculator (0.5 m² floor)
                        decimal unitArea = SqmCalculator.CalculatePanel(item.W1, item.H1);
                        decimal totalArea = Math.Round(unitArea * item.Qty, 3);
                        decimal total = Math.Round(totalArea * item.Rate, 2);

                        ws.Cell(dataRow, 1).Value = g.GroupNumber;
                        ws.Cell(dataRow, 2).Value = groupLabel;
                        ws.Cell(dataRow, 3).Value = item.SerialNumber;
                        ws.Cell(dataRow, 4).Value = item.GlassRef ?? "";
                        ws.Cell(dataRow, 5).Value = item.W1;
                        ws.Cell(dataRow, 6).Value = item.H1;
                        ws.Cell(dataRow, 7).Value = item.W2;
                        ws.Cell(dataRow, 8).Value = item.H2;
                        ws.Cell(dataRow, 9).Value = item.Qty;
                        ws.Cell(dataRow, 10).Value = unitArea;
                        ws.Cell(dataRow, 11).Value = totalArea;
                        ws.Cell(dataRow, 12).Value = item.Rate;
                        ws.Cell(dataRow, 13).Value = total;

                        ws.Cell(dataRow, 10).Style.NumberFormat.Format = "#,##0.000";
                        ws.Cell(dataRow, 11).Style.NumberFormat.Format = "#,##0.000";
                        ws.Cell(dataRow, 12).Style.NumberFormat.Format = "#,##0.00";
                        ws.Cell(dataRow, 13).Style.NumberFormat.Format = "#,##0.00";

                        if (dataRow % 2 == 0)
                            for (int c = 1; c <= totalCols; c++)
                                ws.Cell(dataRow, c).Style.Fill
                                    .BackgroundColor = XLColor.FromHtml(ALT_ROW);

                        ws.Range(dataRow, 5, dataRow, 6).Style.Fill.BackgroundColor =
                            XLColor.FromHtml(ExportColumnPalette.Pane1Fill);
                        ws.Range(dataRow, 7, dataRow, 8).Style.Fill.BackgroundColor =
                            XLColor.FromHtml(ExportColumnPalette.Pane2Fill);
                        ws.Cell(dataRow, 9).Style.Fill.BackgroundColor =
                            XLColor.FromHtml(ExportColumnPalette.QtyFill);
                        ws.Range(dataRow, 10, dataRow, 11).Style.Fill.BackgroundColor =
                            XLColor.FromHtml(ExportColumnPalette.AreaFill);

                        groupQty += item.Qty;
                        groupArea += totalArea;
                        groupValue += total;
                        dataRow++;
                    }
                }

                ws.Cell(dataRow, 2).Value = $"SPEC {g.GroupNumber} SUBTOTAL";
                ws.Cell(dataRow, 2).Style.Font.Bold = true;
                ws.Cell(dataRow, 2).Style.Font.FontColor = XLColor.FromHtml(ACCENT);
                ws.Cell(dataRow, 9).Value = groupQty;
                ws.Cell(dataRow, 9).Style.Font.Bold = true;
                ws.Cell(dataRow, 11).Value = groupArea;
                ws.Cell(dataRow, 11).Style.Font.Bold = true;
                ws.Cell(dataRow, 11).Style.NumberFormat.Format = "#,##0.000";
                ws.Cell(dataRow, 13).Value = groupValue;
                ws.Cell(dataRow, 13).Style.Font.Bold = true;
                ws.Cell(dataRow, 13).Style.Font.FontColor = XLColor.FromHtml(GREEN);
                ws.Cell(dataRow, 13).Style.NumberFormat.Format = "#,##0.00";

                for (int c = 1; c <= totalCols; c++)
                    ws.Cell(dataRow, c).Style.Fill.BackgroundColor =
                        XLColor.FromHtml(LIGHT_BG);
                dataRow++;

                allQty += groupQty;
                allArea += groupArea;
                allValue += groupValue;
            }

            ws.Cell(dataRow, 2).Value = "GRAND TOTAL";
            ws.Cell(dataRow, 2).Style.Font.Bold = true;
            ws.Cell(dataRow, 2).Style.Font.FontSize = 12;
            ws.Cell(dataRow, 9).Value = allQty;
            ws.Cell(dataRow, 9).Style.Font.Bold = true;
            ws.Cell(dataRow, 11).Value = allArea;
            ws.Cell(dataRow, 11).Style.Font.Bold = true;
            ws.Cell(dataRow, 11).Style.NumberFormat.Format = "#,##0.000";
            ws.Cell(dataRow, 13).Value = allValue;
            ws.Cell(dataRow, 13).Style.Font.Bold = true;
            ws.Cell(dataRow, 13).Style.Font.FontSize = 12;
            ws.Cell(dataRow, 13).Style.NumberFormat.Format = "#,##0.00";

            for (int c = 1; c <= totalCols; c++)
            {
                ws.Cell(dataRow, c).Style.Fill.BackgroundColor = XLColor.FromHtml(ACCENT);
                ws.Cell(dataRow, c).Style.Font.FontColor = XLColor.White;
            }

            ws.Columns().AdjustToContents();
        }

        // ════════════════════════════════════════════════════════════════════
        //  SHEET 3 — Other Charges
        // ════════════════════════════════════════════════════════════════════

        private void BuildChargesSheet(
            IXLWorksheet ws,
            List<(string Name, string Qty, decimal Rate, decimal Amount)> charges)
        {
            // 2026-09-16: Rate arrives as a NUMBER and the "(AED)" lives in
            // the header only. It used to be the text "AED 12.00", which put
            // the currency twice on the same line (header + cell) and made
            // the column unsortable/unsummable in Excel.
            var headers = new[]
            {
                "Charge Name",
                "QTY/SQM",
                "Rate (AED)",
                "Amount (AED)"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor =
                    XLColor.FromHtml(TABLE_HEADER);
            }

            int row = 2;
            foreach (var (name, qty, rate, amount) in charges)
            {
                ws.Cell(row, 1).Value = name;
                ws.Cell(row, 2).Value = qty;
                ws.Cell(row, 3).Value = rate;
                ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(row, 4).Value = amount;
                ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0.00";

                if (row % 2 == 0)
                    for (int c = 1; c <= 4; c++)
                        ws.Cell(row, c).Style.Fill.BackgroundColor =
                            XLColor.FromHtml(ALT_ROW);
                row++;
            }

            ws.Cell(row, 1).Value = "TOTAL OTHER CHARGES";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 4).Value = _entity.OtherChargesSubtotal;
            ws.Cell(row, 4).Style.Font.Bold = true;
            ws.Cell(row, 4).Style.Font.FontColor = XLColor.FromHtml(GREEN);
            ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0.00";

            ws.Columns().AdjustToContents();
        }

        // ════════════════════════════════════════════════════════════════════
        //  SHEET 4 — Terms & Conditions
        // ════════════════════════════════════════════════════════════════════

        private void BuildTermsSheet(
            IXLWorksheet ws,
            List<TermClauseSnapshot> terms)
        {
            ws.Cell("A1").Value = "#";
            ws.Cell("B1").Value = "Title";
            ws.Cell("C1").Value = "Body";
            ws.Range("A1:C1").Style.Font.Bold = true;
            ws.Range("A1:C1").Style.Fill.BackgroundColor = XLColor.FromHtml(ACCENT);
            ws.Range("A1:C1").Style.Font.FontColor = XLColor.White;

            int row = 2;
            foreach (var clause in terms)
            {
                ws.Cell(row, 1).Value = clause.Order;
                ws.Cell(row, 2).Value = clause.Title ?? "";

                string body = clause.Body ?? "";
                if (clause.SubLines != null && clause.SubLines.Count > 0)
                    body += "\n" + string.Join("\n",
                        clause.SubLines.Select(l => $"• {l}"));

                ws.Cell(row, 3).Value = body;
                ws.Cell(row, 3).Style.Alignment.WrapText = true;
                row++;
            }

            ws.Column("A").Width = 5;
            ws.Column("B").Width = 25;
            ws.Column("C").Width = 60;
        }

        // ════════════════════════════════════════════════════════════════════
        //  HELPERS
        //  BUG-21: charge line builder uses ProformaChargeMetricsCalculator
        // ════════════════════════════════════════════════════════════════════

        private List<(string Name, string Qty, decimal Rate, decimal Amount)>
            BuildOtherChargeLines()
        {
            var lines = new List<(string, string, decimal, decimal)>();

            if (_snapshot.OtherCharges == null) return lines;

            var templatesByKey = PredefinedChargeTemplates.GetAll()
                .ToDictionary(t => t.Key, t => t);

            // Predefined charges
            foreach (var c in _snapshot.OtherCharges.PredefinedCharges
                .Where(c => c.IsActive))
            {
                templatesByKey.TryGetValue(c.TemplateKey ?? "", out var template);
                string friendlyName = template?.ChargeName ?? c.TemplateKey ?? "";
                string unitLabel = template?.Unit.ToString().ToUpper() ?? "QTY";

                decimal metricQty;

                // BUG-23: DELIVERY quick-hack preserved — tech debt
                if (c.TemplateKey == "DELIVERY")
                {
                    metricQty = 1m;
                    unitLabel = "TRIP";
                }
                else if (c.TemplateKey == "POLISH"
                      || c.TemplateKey == "POLISH_MATCHED_LAM")
                {
                    if (c.PolishSubType == "Custom")
                    {
                        metricQty = c.CustomPolishQty;
                        if (!string.IsNullOrWhiteSpace(c.CustomPolishDescription))
                            friendlyName = $"Polish — {c.CustomPolishDescription}";
                    }
                    else
                    {
                        var polishGroups = ProformaChargeMetricsCalculator.FilterSpecs(
                            _snapshot.SpecGroups, c.CheckedSpecGroupNumbers);
                        metricQty = ProformaChargeMetricsCalculator
                            .ComputePolishLm(
                                polishGroups,
                                c.PolishSubType,
                                matchedSameGlassOnly: c.TemplateKey == "POLISH_MATCHED_LAM");
                        if (c.TemplateKey == "POLISH_MATCHED_LAM")
                            friendlyName = "Polish — same-glass laminate (2×)";
                        else if (!string.IsNullOrWhiteSpace(c.PolishSubType))
                            friendlyName = $"Polish — {c.PolishSubType}";
                    }
                }
                else
                {
                    string sourceKey = ProformaChargeMetricsCalculator.FirstChargeSourceKey(
                        c.SelectedMetricKey,
                        c.SelectedSourceKey,
                        template?.DefaultSourceKey);
                    metricQty = ProformaChargeMetricsCalculator
                        .ComputeTargetedMetric(
                            _snapshot.SpecGroups,
                            sourceKey,
                            c.CheckedSpecGroupNumbers);
                }

                decimal amount = MoneyRounding.Round(metricQty * c.Rate);
                string qtyStr = metricQty > 0
                    ? $"{metricQty:0.###} {unitLabel}"
                    : "—";

                lines.Add((
                    friendlyName,
                    qtyStr,
                    c.Rate,
                    amount));
            }

            // Wastages — shared calculator
            foreach (var w in _snapshot.OtherCharges.Wastages
                .Where(w => w.IsActive))
            {
                // BUG-21: shared calculator
                decimal wastageQty = ProformaChargeMetricsCalculator
                    .ComputeWastageQty(_snapshot.SpecGroups, w.CheckedSourceKeys);
                decimal wastageAmount = MoneyRounding.Round(wastageQty * w.Rate);

                lines.Add((
                    $"Wastage — {w.ColorName}",
                    wastageQty > 0 ? $"{wastageQty:0.###} SQM" : "—",
                    w.Rate,
                    wastageAmount));
            }

            // Custom charges
            foreach (var c in _snapshot.OtherCharges.CustomCharges
                .Where(c => c.IsActive))
            {
                decimal amount = MoneyRounding.Round(c.Qty * c.Rate);
                string unitStr = string.IsNullOrWhiteSpace(c.Unit)
                                      ? "QTY" : c.Unit.ToUpper();

                lines.Add((
                    c.ChargeName ?? "",
                    c.Qty > 0 ? $"{c.Qty:0.###} {unitStr}" : "—",
                    c.Rate,
                    amount));
            }

            return lines;
        }

        private List<TermClauseSnapshot> GetActiveTerms()
        {
            if (_snapshot.TermClauses == null)
                return new List<TermClauseSnapshot>();

            return _snapshot.TermClauses
                .Where(c => c.IsActive)
                .OrderBy(c => c.Order)
                .ToList();
        }

        private static void AddSection(
            IXLWorksheet ws, ref int row, string title)
        {
            var cell = ws.Cell(row, 1);
            cell.Value = title;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontSize = 11;
            cell.Style.Font.FontColor = XLColor.FromHtml(ACCENT);
            ws.Range(row, 1, row, 2).Style.Fill.BackgroundColor =
                XLColor.FromHtml(LIGHT_BG);
            row++;
        }

        private static void AddField(
            IXLWorksheet ws, ref int row, string label, string value)
        {
            ws.Cell(row, 1).Value = label;
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml(MUTED);
            ws.Cell(row, 2).Value = string.IsNullOrWhiteSpace(value) ? "—" : value;
            ws.Cell(row, 2).Style.Font.Bold = true;
            row++;
        }
    }
}