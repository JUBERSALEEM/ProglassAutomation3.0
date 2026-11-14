// ═══════════════════════════════════════════════════════════════════════
//  FILE: Services/ProformaInvoice/Export/ProformaDossierExcelLayout.cs
//  PROJECT: ProGlassAutomation
//  CREATED: 2026-09-02 — advanced reporting, slice 3
//
//  THE FULL DOSSIER
//  ---------------------------------------------------------------------
//  One workbook containing every section of a proforma invoice: client
//  and project, specifications, the cut list, other charges, terms, and
//  optionally profit & loss. Industry name: a job dossier or order pack —
//  the single document an estimator hands to production, or a manager
//  reviews before approving a quote.
//
//  WHY MULTIPLE SHEETS RATHER THAN ONE
//  Each section answers a different question and has a different natural
//  column set. Flattening them into one grid forces a lowest-common-
//  denominator shape where every row carries mostly blank columns. Sheets
//  also let production print only the cut list without redacting anything.
//
//  ⚠ P&L IS OPT-IN, NEVER THE DEFAULT
//  IncludeProfitLoss defaults to FALSE. An "export everything" button that
//  silently contains cost and margin WILL eventually be emailed to a
//  customer, and that is not a recoverable mistake. The caller must ask
//  for it explicitly, and the UI labels that action as internal.
// ═══════════════════════════════════════════════════════════════════════

using ClosedXML.Excel;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.Excel;
using System;
using System.Linq;

namespace ProGlassAutomation.Services.ProformaInvoice.Export
{
    public class ProformaDossierExcelLayout : IExcelLayout
    {
        private readonly SavedProformaInvoice _entity;
        private readonly ProformaInvoiceSnapshot _snap;
        private readonly bool _includeProfitLoss;

        public ProformaDossierExcelLayout(
            SavedProformaInvoice entity,
            ProformaInvoiceSnapshot snapshot,
            bool includeProfitLoss = false)
        {
            _entity = entity ?? throw new ArgumentNullException(nameof(entity));
            _snap = snapshot ?? new ProformaInvoiceSnapshot();
            _includeProfitLoss = includeProfitLoss;
        }

        // ── IDocumentLayout ──────────────────────────────────────────────

        public string DocumentPrefix => "PI";

        public string DocumentId =>
            !string.IsNullOrWhiteSpace(_snap.Company?.ProformaId)
                ? _snap.Company!.ProformaId
                : _entity.ProformaId ?? "";

        public string DocumentTitle =>
            _includeProfitLoss
                ? "Proforma Invoice — Full Dossier (Internal)"
                : "Proforma Invoice — Dossier";

        public string CompanyName =>
            !string.IsNullOrWhiteSpace(_snap.Client?.CustomerName)
                ? _snap.Client!.CustomerName
                : _entity.CustomerName ?? "";

        public string Reference => _snap.Project?.ProjectIdentifierName ?? "";

        public DateTime DocumentDate =>
            _snap.Company != null && _snap.Company.IssueDate != default
                ? _snap.Company.IssueDate
                : _entity.CreatedAt;

        // ── Render ───────────────────────────────────────────────────────

        public void RenderWorkbook(IXLWorkbook workbook)
        {
            BuildOverviewSheet(workbook);
            BuildSpecificationsSheet(workbook);
            BuildCutListSheet(workbook);
            BuildOtherChargesSheet(workbook);
            BuildTermsSheet(workbook);

            // Opt-in only — see the header note.
            if (_includeProfitLoss) BuildProfitLossSheet(workbook);
        }

        // ═══════════════════════════════════════════════════════════════
        //  1. OVERVIEW — header + KPI band
        // ═══════════════════════════════════════════════════════════════

        private void BuildOverviewSheet(IXLWorkbook wb)
        {
            var ws = wb.Worksheets.Add("Overview");
            ws.ShowGridLines = false;
            int r = 1;

            ws.Cell(r, 1).Value = DocumentTitle;
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Font.FontSize = 15;
            ws.Range(r, 1, r, 4).Merge();
            r += 2;

            // A report without its criteria printed on it is unauditable.
            r = Pair(ws, r, "Proforma No", DocumentId);
            r = Pair(ws, r, "Status", _snap.Company?.ProformaStatus ?? "");
            r = Pair(ws, r, "Issue date", Date(_snap.Company?.IssueDate));
            r = Pair(ws, r, "Valid until", Date(_snap.Company?.ValidUntil));
            r = Pair(ws, r, "Generated", DateTime.Now.ToString("dd-MMM-yyyy HH:mm"));
            r++;

            r = Section(ws, r, "CLIENT");
            r = Pair(ws, r, "Customer", _snap.Client?.CustomerName ?? "");
            r = Pair(ws, r, "Customer TRN", _snap.Client?.CustomerTrn ?? "");
            r = Pair(ws, r, "Reference code", _snap.Client?.CustomerReferenceCode ?? "");
            r = Pair(ws, r, "Salesman", _snap.Client?.SalesRepresentative ?? "");
            r = Pair(ws, r, "Attention", _snap.Client?.AttentionExecutiveName ?? "");
            r = Pair(ws, r, "Contact", _snap.Client?.ClientContactPhone ?? "");
            r = Pair(ws, r, "Billing address", _snap.Client?.BillingAddress ?? "");
            r++;

            r = Section(ws, r, "PROJECT");
            r = Pair(ws, r, "Project", _snap.Project?.ProjectIdentifierName ?? "");
            r = Pair(ws, r, "Contract no", _snap.Project?.ProjectContractNo ?? "");
            r = Pair(ws, r, "Reference", _snap.Project?.ProjectReference ?? "");
            r = Pair(ws, r, "Site / delivery", _snap.Project?.SiteDeliveryLocation ?? "");
            r = Pair(ws, r, "LPO number", _snap.Project?.LpoNumber ?? "");
            r++;

            r = Section(ws, r, "SUPPLIER");
            r = Pair(ws, r, "Company", _snap.Company?.CompanyName ?? "");
            r = Pair(ws, r, "TRN", _snap.Company?.CompanyTrn ?? "");
            r = Pair(ws, r, "Location", _snap.Company?.CompanyLocation ?? "");
            r++;

            // Totals reconcile to the detail sheets — the rule that makes a
            // dossier trustworthy.
            r = Section(ws, r, "TOTALS");
            int specCount = _snap.SpecGroups?.Count ?? 0;
            int pieces = _snap.SpecGroups?.SelectMany(g => g.ItemRows ?? new()).Sum(i => i.Qty) ?? 0;

            r = Pair(ws, r, "Specifications", specCount.ToString("N0"));
            r = Pair(ws, r, "Total pieces", pieces.ToString("N0"));
            r = PairMoney(ws, r, "Glass materials", _entity.GlassMaterialsSubtotal);
            r = PairMoney(ws, r, "Other charges", _entity.OtherChargesSubtotal);
            r = PairMoney(ws, r, "Discount", _entity.DiscountAmount);
            r = PairMoney(ws, r, "VAT", _entity.VatAmount);
            r = PairMoney(ws, r, "Grand total (incl. VAT)", _entity.GrandTotalVatInclusive);

            ws.Columns(1, 2).AdjustToContents();
        }

        // ═══════════════════════════════════════════════════════════════
        //  2. SPECIFICATIONS
        // ═══════════════════════════════════════════════════════════════

        private void BuildSpecificationsSheet(IXLWorkbook wb)
        {
            var ws = wb.Worksheets.Add("Specifications");
            ws.ShowGridLines = false;
            int r = Title(ws, "SPECIFICATIONS", 8);

            r = Header(ws, r, new[]
            {
                "#", "Specification", "Glass type", "Outer colour",
                "Inner colour", "Base rate", "Surcharge %", "Pieces"
            });

            foreach (var g in _snap.SpecGroups ?? new())
            {
                ws.Cell(r, 1).Value = g.GroupNumber;
                ws.Cell(r, 2).Value = Pick(g.SelectedSpecName, g.GroupName);
                ws.Cell(r, 3).Value = g.SelectedGlassType ?? "";
                ws.Cell(r, 4).Value = g.OuterGlassColor ?? "";
                ws.Cell(r, 5).Value = g.InnerGlassColor ?? "";
                ws.Cell(r, 6).Value = g.BaseRate;
                ws.Cell(r, 6).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(r, 7).Value = g.SurchargePercent;
                ws.Cell(r, 8).Value = (g.ItemRows ?? new()).Sum(i => i.Qty);
                r++;
            }

            ws.Columns(1, 8).AdjustToContents();
            ws.SheetView.FreezeRows(3);
        }

        // ═══════════════════════════════════════════════════════════════
        //  3. CUT LIST — the sheet production actually uses
        // ═══════════════════════════════════════════════════════════════

        private void BuildCutListSheet(IXLWorkbook wb)
        {
            var ws = wb.Worksheets.Add("Cut List");
            ws.ShowGridLines = false;
            int r = Title(ws, "CUT LIST", 10);

            r = Header(ws, r, new[]
            {
                "Spec #", "Specification", "Sr", "Glass ref",
                "W1", "H1", "W2", "H2", "Qty", "Rate"
            });

            int firstDataRow = r;

            foreach (var g in _snap.SpecGroups ?? new())
            {
                string specName = Pick(g.SelectedSpecName, g.GroupName);

                foreach (var i in g.ItemRows ?? new())
                {
                    ws.Cell(r, 1).Value = g.GroupNumber;
                    ws.Cell(r, 2).Value = specName;
                    ws.Cell(r, 3).Value = i.SerialNumber;
                    ws.Cell(r, 4).Value = i.GlassRef ?? "";
                    ws.Cell(r, 5).Value = i.W1;
                    ws.Cell(r, 6).Value = i.H1;
                    ws.Cell(r, 7).Value = i.W2;
                    ws.Cell(r, 8).Value = i.H2;
                    ws.Cell(r, 9).Value = i.Qty;
                    ws.Cell(r, 10).Value = i.Rate;
                    ws.Cell(r, 10).Style.NumberFormat.Format = "#,##0.00";
                    r++;
                }
            }

            if (r > firstDataRow)
            {
                ws.Cell(r, 8).Value = "TOTAL";
                ws.Cell(r, 8).Style.Font.Bold = true;
                ws.Cell(r, 9).FormulaA1 = $"SUM(I{firstDataRow}:I{r - 1})";
                ws.Cell(r, 9).Style.Font.Bold = true;
                ws.Range(r, 1, r, 10).Style.Border.TopBorder = XLBorderStyleValues.Thin;
            }

            ws.Columns(1, 10).AdjustToContents();
            ws.SheetView.FreezeRows(3);
        }

        // ═══════════════════════════════════════════════════════════════
        //  4. OTHER CHARGES
        // ═══════════════════════════════════════════════════════════════

        private void BuildOtherChargesSheet(IXLWorkbook wb)
        {
            var ws = wb.Worksheets.Add("Other Charges");
            ws.ShowGridLines = false;
            int r = Title(ws, "OTHER CHARGES", 5);

            var oc = _snap.OtherCharges ?? new OtherChargesSnapshot();

            r = SubHeading(ws, r, "Predefined charges");
            r = Header(ws, r, new[] { "Template", "Active", "Source", "Rate", "" });
            foreach (var c in oc.PredefinedCharges ?? new())
            {
                ws.Cell(r, 1).Value = c.TemplateKey ?? "";
                ws.Cell(r, 2).Value = c.IsActive ? "Yes" : "No";
                ws.Cell(r, 3).Value = c.SelectedSourceKey ?? "";
                ws.Cell(r, 4).Value = c.Rate;
                ws.Cell(r, 4).Style.NumberFormat.Format = "#,##0.00";
                r++;
            }
            r++;

            r = SubHeading(ws, r, "Wastages");
            r = Header(ws, r, new[] { "Colour", "Active", "Rate", "Optimisation", "" });
            foreach (var w in oc.Wastages ?? new())
            {
                ws.Cell(r, 1).Value = w.ColorName ?? "";
                ws.Cell(r, 2).Value = w.IsActive ? "Yes" : "No";
                ws.Cell(r, 3).Value = w.Rate;
                ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(r, 4).Value = w.UseOptimizationFormula ? "Yes" : "No";
                r++;
            }
            r++;

            r = SubHeading(ws, r, "Custom charges");
            r = Header(ws, r, new[] { "Charge", "Active", "Qty", "Unit", "Rate" });
            foreach (var c in oc.CustomCharges ?? new())
            {
                ws.Cell(r, 1).Value = c.ChargeName ?? "";
                ws.Cell(r, 2).Value = c.IsActive ? "Yes" : "No";
                ws.Cell(r, 3).Value = c.Qty;
                ws.Cell(r, 4).Value = c.Unit ?? "";
                ws.Cell(r, 5).Value = c.Rate;
                ws.Cell(r, 5).Style.NumberFormat.Format = "#,##0.00";
                r++;
            }

            ws.Columns(1, 5).AdjustToContents();
        }

        // ═══════════════════════════════════════════════════════════════
        //  5. TERMS
        // ═══════════════════════════════════════════════════════════════

        private void BuildTermsSheet(IXLWorkbook wb)
        {
            var ws = wb.Worksheets.Add("Terms");
            ws.ShowGridLines = false;
            int r = Title(ws, "TERMS & CONDITIONS", 3);

            r = Header(ws, r, new[] { "#", "Title", "Body" });

            int n = 1;
            foreach (var c in _snap.TermClauses ?? new())
            {
                ws.Cell(r, 1).Value = n++;
                ws.Cell(r, 2).Value = c.Title ?? "";
                ws.Cell(r, 3).Value = c.Body ?? "";
                ws.Cell(r, 3).Style.Alignment.WrapText = true;
                r++;
            }

            ws.Column(1).Width = 6;
            ws.Column(2).Width = 32;
            ws.Column(3).Width = 90;
        }

        // ═══════════════════════════════════════════════════════════════
        //  6. PROFIT & LOSS — internal only
        // ═══════════════════════════════════════════════════════════════

        private void BuildProfitLossSheet(IXLWorkbook wb)
        {
            var ws = wb.Worksheets.Add("Profit & Loss");
            ws.ShowGridLines = false;
            int r = 1;

            ws.Cell(r, 1).Value = "PROFIT & LOSS — INTERNAL, NOT FOR CUSTOMER RELEASE";
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Font.FontSize = 13;
            ws.Cell(r, 1).Style.Font.FontColor = XLColor.FromHtml("#B00020");
            ws.Range(r, 1, r, 8).Merge();
            r += 2;

            var pl = _snap.ProfitLoss ?? new ProfitLossSnapshot();

            decimal cost = pl.TotalGlassCost + pl.TotalProductionCost;
            decimal margin = pl.NetExVat - cost;
            decimal marginPct = pl.NetExVat == 0m
                ? 0m
                : Math.Round(margin / pl.NetExVat * 100m, 1);

            r = PairMoney(ws, r, "Gross (VAT inclusive)", pl.GrossVatInclusive);
            r = PairMoney(ws, r, "Net (ex VAT)", pl.NetExVat);
            r = PairMoney(ws, r, "Glass cost", pl.TotalGlassCost);
            r = PairMoney(ws, r, "Production cost", pl.TotalProductionCost);
            r = PairMoney(ws, r, "Total cost", cost);
            r = PairMoney(ws, r, "Margin", margin);
            r = Pair(ws, r, "Margin %", $"{marginPct:0.#}%");
            r++;

            r = Header(ws, r, new[]
            {
                "Kind", "Spec #", "Description", "Sheet",
                "Sheet SQM", "Qty", "Rate", "Amount"
            });

            foreach (var row in pl.Rows ?? new())
            {
                ws.Cell(r, 1).Value = row.Kind ?? "";
                ws.Cell(r, 2).Value = row.SpecNumber;
                ws.Cell(r, 3).Value = row.Description ?? "";
                ws.Cell(r, 4).Value = row.SheetWidthMm > 0 && row.SheetHeightMm > 0
                    ? $"{row.SheetWidthMm} × {row.SheetHeightMm}"
                    : "—";
                ws.Cell(r, 5).Value = row.SheetSqm;
                ws.Cell(r, 5).Style.NumberFormat.Format = "#,##0.0000";
                ws.Cell(r, 6).Value = row.Quantity;
                ws.Cell(r, 7).Value = row.PricePerSqm;
                ws.Cell(r, 7).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(r, 8).Value = row.Amount;
                ws.Cell(r, 8).Style.NumberFormat.Format = "#,##0.00";
                r++;
            }

            ws.Columns(1, 8).AdjustToContents();
        }

        // ═══════════════════════════════════════════════════════════════
        //  Small sheet-building helpers
        // ═══════════════════════════════════════════════════════════════

        private static int Title(IXLWorksheet ws, string text, int span)
        {
            ws.Cell(1, 1).Value = text;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, Math.Max(1, span)).Merge();
            return 3;
        }

        private static int Section(IXLWorksheet ws, int r, string text)
        {
            ws.Cell(r, 1).Value = text;
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#EFEFEF");
            ws.Range(r, 1, r, 2).Merge();
            return r + 1;
        }

        private static int SubHeading(IXLWorksheet ws, int r, string text)
        {
            ws.Cell(r, 1).Value = text;
            ws.Cell(r, 1).Style.Font.Bold = true;
            return r + 1;
        }

        private static int Header(IXLWorksheet ws, int r, string[] headers)
        {
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(r, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#EFEFEF");
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            }
            return r + 1;
        }

        private static int Pair(IXLWorksheet ws, int r, string label, string value)
        {
            ws.Cell(r, 1).Value = label;
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 2).Value = value ?? "";
            return r + 1;
        }

        private static int PairMoney(IXLWorksheet ws, int r, string label, decimal value)
        {
            ws.Cell(r, 1).Value = label;
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 2).Value = value;
            ws.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
            return r + 1;
        }

        private static string Date(DateTime? d) =>
            d.HasValue && d.Value != default ? d.Value.ToString("dd-MMM-yyyy") : "";

        private static string Pick(string? a, string? b) =>
            !string.IsNullOrWhiteSpace(a) ? a! : (b ?? "");
    }
}
