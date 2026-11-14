using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ProGlassAutomation.Models.Proforma;

namespace ProGlassAutomation.Services.Proforma
{
    /// <summary>Portable 13-column cut-list CSV export for a V3 proforma invoice.</summary>
    public static class ProformaCsvExporter
    {
        private static readonly string[] Headers =
        {
            "Spec #", "Spec Name", "Row #", "Glass Ref", "W1", "H1",
            "W2", "H2", "Qty", "SQM", "TOTAL SQM", "Rate (AED)", "Total (AED)"
        };

        public static string Render(ProformaInvoiceDraft draft)
        {
            ArgumentNullException.ThrowIfNull(draft);
            var output = new StringBuilder();
            WriteRow(output, Headers);
            foreach (SpecGroup group in draft.Groups)
            {
                foreach (SpecRow item in group.Rows)
                {
                    decimal rate = item.RateOverridePerSqm > 0m
                        ? item.RateOverridePerSqm : group.EffectiveRatePerSqm;
                    WriteRow(output, new[]
                    {
                        group.GroupNumber.ToString(CultureInfo.InvariantCulture),
                        group.SpecName,
                        item.SerialNumber.ToString(CultureInfo.InvariantCulture),
                        item.GlassRef,
                        item.W1.ToString(CultureInfo.InvariantCulture),
                        item.H1.ToString(CultureInfo.InvariantCulture),
                        item.W2.ToString(CultureInfo.InvariantCulture),
                        item.H2.ToString(CultureInfo.InvariantCulture),
                        item.Qty.ToString(CultureInfo.InvariantCulture),
                        Area(item.PanelSqm),
                        Area(item.TotalSqm),
                        Money(rate),
                        Money(item.ValueAt(group.EffectiveRatePerSqm))
                    });
                }
            }

            WriteRow(output, Array.Empty<string>());
            WriteSummary(output, "GLASS SUBTOTAL", draft.Summary.GlassSubtotal);
            WriteSummary(output, "OTHER CHARGES TOTAL", draft.Summary.OtherChargesSubtotal);
            WriteSummary(output, "GRAND TOTAL (VAT INC)", draft.Summary.GrandTotal);
            return output.ToString();
        }

        public static async Task ExportAsync(ProformaInvoiceDraft draft, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Choose a CSV output path.", nameof(outputPath));
            string fullPath = Path.GetFullPath(outputPath);
            string? folder = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
            string csv = Render(draft.Clone());
            await File.WriteAllTextAsync(fullPath, csv, new UTF8Encoding(false)).ConfigureAwait(false);
        }

        public static string SuggestedFileName(ProformaInvoiceDraft draft)
        {
            string number = string.IsNullOrWhiteSpace(draft.ProformaNumber) ? "DRAFT" : draft.ProformaNumber.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
                number = number.Replace(invalid, '-');
            return "PI_" + number + "_CutList.csv";
        }

        private static void WriteSummary(StringBuilder output, string label, decimal value)
        {
            var cells = new string[Headers.Length];
            cells[Headers.Length - 2] = label;
            cells[Headers.Length - 1] = Money(value);
            WriteRow(output, cells);
        }

        private static void WriteRow(StringBuilder output, System.Collections.Generic.IReadOnlyList<string> cells)
        {
            output.Append(string.Join(",", cells.Select(Escape)));
            output.Append("\r\n");
        }

        private static string Escape(string? value)
        {
            string text = value ?? "";
            return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                ? "\"" + text.Replace("\"", "\"\"") + "\""
                : text;
        }

        private static string Money(decimal value) =>
            value.ToString("0.00", CultureInfo.InvariantCulture);

        private static string Area(decimal value) =>
            value.ToString("0.000", CultureInfo.InvariantCulture);
    }
}