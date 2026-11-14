using CsvHelper;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.Export.Csv;
using ProGlassAutomation.Services.Export.FileSystem;
using ProGlassAutomation.Utilities;
using System;
using System.Linq;

using System.Globalization;
using ProGlassAutomation.Services.Reporting;
namespace ProGlassAutomation.Services.ProformaInvoice.Export
{
    /// <summary>
    /// ICsvLayout implementation for Proforma Invoice.
    ///
    /// 13-column CSV:
    ///   Spec # | Spec Name | Row # | Glass Ref | W1 | H1 | W2 | H2 |
    ///   Qty | SQM | TOTAL SQM | Rate (AED) | Total (AED)
    ///
    /// Followed by blank row + 3 summary rows
    /// (11 empty fields + label + value = 13 fields per row).
    ///
    /// BUG-22: SqmCalculator.CalculatePanel() replaces inline formula.
    /// BUG-27: Inline SQM formula removed — uses shared SqmCalculator.
    /// BUG-28: Magic number 11 replaced with named constant.
    /// </summary>
    public class ProformaInvoiceCsvLayout : ICsvLayout
    {
        // BUG-28: Named constant — 11 empty fields precede label + value
        // (11 + 1 label + 1 value = 13 total columns matching data rows)
        private const int EMPTY_COLS_BEFORE_SUMMARY = 11;

        private readonly SavedProformaInvoice _entity;
        private readonly ProformaInvoiceSnapshot _snapshot;

        public ProformaInvoiceCsvLayout(
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

        // ── ICsvLayout ───────────────────────────────────────────────────────

        public void WriteRows(CsvWriter writer)
        {
            // ── 13-column header ─────────────────────────────────────────
            writer.WriteField("Spec #");
            writer.WriteField("Spec Name");
            writer.WriteField("Row #");
            writer.WriteField("Glass Ref");
            writer.WriteField("W1");
            writer.WriteField("H1");
            writer.WriteField("W2");
            writer.WriteField("H2");
            writer.WriteField("Qty");
            writer.WriteField("SQM");
            writer.WriteField("TOTAL SQM");
            writer.WriteField("Rate (AED)");
            writer.WriteField("Total (AED)");
            writer.NextRecord();

            // ── Data rows ────────────────────────────────────────────────
            if (_snapshot.SpecGroups != null)
            {
                foreach (var g in _snapshot.SpecGroups)
                {
                    string groupLabel =
                        !string.IsNullOrWhiteSpace(g.SelectedSpecName)
                            ? g.SelectedSpecName
                            : (!string.IsNullOrWhiteSpace(g.GroupName)
                                ? g.GroupName
                                : $"Spec Group {g.GroupNumber}");

                    if (g.ItemRows == null) continue;

                    foreach (var r in g.ItemRows)
                    {
                        // BUG-22 / BUG-27: shared SqmCalculator (0.5 m² floor)
                        decimal unitArea = SqmCalculator.CalculatePanel(r.W1, r.H1);
                        decimal totalArea = Math.Round(unitArea * r.Qty, 3);
                        decimal total = MoneyRounding.Round(totalArea * r.Rate);

                        writer.WriteField(g.GroupNumber);
                        writer.WriteField(groupLabel);
                        writer.WriteField(r.SerialNumber);
                        writer.WriteField(r.GlassRef ?? "");
                        writer.WriteField(r.W1);
                        writer.WriteField(r.H1);
                        writer.WriteField(r.W2);
                        writer.WriteField(r.H2);
                        writer.WriteField(r.Qty);
                        writer.WriteField(unitArea.ToString("0.000", CultureInfo.InvariantCulture));
                        writer.WriteField(totalArea.ToString("0.000", CultureInfo.InvariantCulture));
                        writer.WriteField(r.Rate.ToString("N2", CultureInfo.InvariantCulture));
                        writer.WriteField(total.ToString("N2", CultureInfo.InvariantCulture));
                        writer.NextRecord();
                    }
                }
            }

            // ── Blank separator row ──────────────────────────────────────
            writer.NextRecord();

            // ── Summary rows ─────────────────────────────────────────────
            WriteEmptyThenSummary(writer,
                "Glass Materials Total",
                _entity.GlassMaterialsSubtotal.ToString("N2", CultureInfo.InvariantCulture));

            WriteEmptyThenSummary(writer,
                "Other Charges Total",
                _entity.OtherChargesSubtotal.ToString("N2", CultureInfo.InvariantCulture));

            WriteEmptyThenSummary(writer,
                "GRAND TOTAL (VAT INC)",
                _entity.GrandTotalVatInclusive.ToString("N2", CultureInfo.InvariantCulture));
        }

        // ── Helper ───────────────────────────────────────────────────────────

        /// <summary>
        /// BUG-28: Uses EMPTY_COLS_BEFORE_SUMMARY constant instead of
        /// hardcoded 11 loop iterations.
        /// </summary>
        private static void WriteEmptyThenSummary(
            CsvWriter writer, string label, string value)
        {
            for (int i = 0; i < EMPTY_COLS_BEFORE_SUMMARY; i++)
                writer.WriteField("");
            writer.WriteField(label);
            writer.WriteField(value);
            writer.NextRecord();
        }
    }
}