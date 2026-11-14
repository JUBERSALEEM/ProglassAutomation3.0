using CsvHelper;
using CsvHelper.Configuration;
using ProGlassAutomation.Models.SheetStore;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.SheetStore.SheetStoreCsv
{
    /// <summary>
    /// Handles CSV import/export for glass sheets.
    /// Uses CsvHelper for robust parsing (handles quoted values, commas in fields, etc.).
    /// </summary>
    public class SheetStoreCsvService
    {
        // ============ EXPORT ============

        /// <summary>
        /// Exports the given sheets to a CSV file.
        /// </summary>
        public async Task ExportAsync(string filePath, IEnumerable<GlassSheet> sheets)
        {
            using var writer = new StreamWriter(filePath);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

            // Register class map so we control column order + names
            csv.Context.RegisterClassMap<GlassSheetCsvMap>();

            await csv.WriteRecordsAsync(sheets);
        }

        // ============ IMPORT ============

        /// <summary>
        /// Reads all sheets from a CSV file. Does NOT save to DB — caller decides.
        /// </summary>
        public async Task<List<GlassSheet>> ImportAsync(string filePath)
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null,   // ignore missing columns
                HeaderValidated = null,     // ignore case/spacing in headers
                TrimOptions = TrimOptions.Trim
            };

            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, config);

            csv.Context.RegisterClassMap<GlassSheetCsvMap>();

            var list = new List<GlassSheet>();
            await foreach (var record in csv.GetRecordsAsync<GlassSheet>())
            {
                if (record == null) continue;
                if (record.ThicknessMm <= 0 || record.WidthMm <= 0 || record.HeightMm <= 0)
                    continue;
                // Do not silently rewrite inventory numbers. A negative
                // quantity or Used > Stock is a bad row, not "zero" or
                // "fully consumed". Skip it the same way invalid sizes
                // are skipped so an import cannot hide a discrepancy.
                if (record.Stock < 0 || record.Used < 0 || record.Used > record.Stock)
                    continue;
                if (string.IsNullOrWhiteSpace(record.Color)) record.Color = "Clear";
                if (string.IsNullOrWhiteSpace(record.Category)) record.Category = "Clear";
                list.Add(record);
            }
            return list;
        }
    }

    /// <summary>
    /// Maps GlassSheet properties to CSV columns.
    /// Controls order, names, and which fields are included.
    /// </summary>
    public sealed class GlassSheetCsvMap : ClassMap<GlassSheet>
    {
        public GlassSheetCsvMap()
        {
            Map(m => m.Category).Name("Category").Index(0);
            Map(m => m.ThicknessMm).Name("Thickness").Index(1);
            Map(m => m.Color).Name("Color").Index(2);
            Map(m => m.WidthMm).Name("Width").Index(3);
            Map(m => m.HeightMm).Name("Height").Index(4);
            Map(m => m.Supplier).Name("Supplier").Index(5);
            Map(m => m.PurchasePricePerSqm).Name("PurchasePricePerSqm").Index(6);
            Map(m => m.SellPricePerSqm).Name("SellPricePerSqm").Index(7);
            Map(m => m.Stock).Name("Stock").Index(8);
            Map(m => m.Used).Name("Used").Index(9);
        }
    }
}