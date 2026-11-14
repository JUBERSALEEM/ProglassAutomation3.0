using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProGlassAutomation.Services.Proforma
{
    public sealed class ProformaGridImportRow
    {
        public string GlassRef { get; init; } = "";
        public string SpecName { get; init; } = "";
        public string GlassType { get; init; } = "";
        public int W1 { get; init; }
        public int H1 { get; init; }
        public int W2 { get; init; }
        public int H2 { get; init; }
        public int Qty { get; init; } = 1;
    }

    public sealed class ProformaGridImportResult
    {
        public List<ProformaGridImportRow> Rows { get; } = new();
        public int Skipped { get; set; }
        public string Layout { get; set; } = "";
        public string Warning { get; set; } = "";
    }

    /// <summary>Header-aware CSV/TSV/text and scanner-JSON cut-list parser.</summary>
    public static class ProformaGridImportParser
    {
        private static readonly Regex DimensionPair = new(
            @"^\s*(\d+(?:[.,]\d+)?)\s*[xX×*]\s*(\d+(?:[.,]\d+)?)\s*$",
            RegexOptions.Compiled);

        public static ProformaGridImportResult ParseDelimitedText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Failure("File is empty.");

            var table = new List<IReadOnlyList<string>>();
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                char delimiter = trimmed.Contains('\t') ? '\t'
                    : trimmed.Contains(',') ? ',' : trimmed.Contains(';') ? ';' : '\0';
                string[] cells = delimiter == '\0'
                    ? SplitLooseText(trimmed)
                    : SplitDelimitedLine(trimmed, delimiter);
                table.Add(cells.Select(cell => cell.Trim()).ToList());
            }
            return ParseTable(table);
        }

        public static ProformaGridImportResult ParseTable(
            IReadOnlyList<IReadOnlyList<string>> table)
        {
            var result = new ProformaGridImportResult();
            if (table == null || table.Count == 0)
                return Failure("No rows were found in the file.");

            Dictionary<string, int>? map = DetectHeader(table[0]);
            int start = map == null ? 0 : 1;
            result.Layout = map == null ? "positional" : "header";

            for (int index = start; index < table.Count; index++)
            {
                IReadOnlyList<string> cells = table[index];
                if (cells == null || cells.Count == 0 || cells.All(string.IsNullOrWhiteSpace))
                {
                    result.Skipped++;
                    continue;
                }

                ProformaGridImportRow? row = map == null
                    ? FromPositional(cells)
                    : FromMapped(cells, map);
                if (row == null || row.W1 <= 0 || row.H1 <= 0 || row.Qty <= 0)
                {
                    result.Skipped++;
                    continue;
                }
                if ((row.W2 > 0) != (row.H2 > 0))
                {
                    result.Skipped++;
                    continue;
                }
                result.Rows.Add(row);
            }

            if (result.Rows.Count == 0)
                result.Warning = "No usable cut-list rows. Expected REF, W1, H1 and QTY; W2/H2 are optional.";
            else if (result.Skipped > 0)
                result.Warning = result.Skipped + " invalid or empty row(s) were skipped.";
            return result;
        }

        public static ProformaGridImportResult ParseJsonArray(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Failure("The scanner returned no data.");
            var result = new ProformaGridImportResult { Layout = "json" };
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    return Failure("Scanner data must be a JSON array.");

                foreach (JsonElement element in document.RootElement.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        result.Skipped++;
                        continue;
                    }
                    int w1 = ReadJsonInt(element, "w1", "width", "w");
                    int h1 = ReadJsonInt(element, "h1", "height", "h");
                    int w2 = ReadJsonInt(element, "w2");
                    int h2 = ReadJsonInt(element, "h2");
                    string qtyText = ReadJsonString(element, "qty", "quantity", "pieces");
                    int qty = qtyText.Length == 0 ? 1 : ReadQuantity(qtyText);
                    if (w1 <= 0 || h1 <= 0 || qty <= 0 || ((w2 > 0) != (h2 > 0)))
                    {
                        result.Skipped++;
                        continue;
                    }
                    result.Rows.Add(new ProformaGridImportRow
                    {
                        GlassRef = ReadJsonString(element, "ref", "glassRef", "mark", "code"),
                        SpecName = ReadJsonString(element, "spec", "specName", "specification"),
                        GlassType = GlassBuilds.Normalize(ReadJsonString(element, "glassType", "type", "glass")),
                        W1 = w1, H1 = h1, W2 = w2, H2 = h2, Qty = qty
                    });
                }
            }
            catch (JsonException error)
            {
                result.Warning = "Could not read scanner JSON: " + error.Message;
                return result;
            }

            if (result.Rows.Count == 0 && result.Warning.Length == 0)
                result.Warning = "The scanner response contained no usable rows.";
            return result;
        }

        private static ProformaGridImportRow? FromMapped(
            IReadOnlyList<string> cells, IReadOnlyDictionary<string, int> map)
        {
            string Read(string key) =>
                map.TryGetValue(key, out int index) && index >= 0 && index < cells.Count
                    ? cells[index].Trim() : "";

            int w1 = ReadMillimetres(Read("w1"));
            int h1 = ReadMillimetres(Read("h1"));
            int w2 = ReadMillimetres(Read("w2"));
            int h2 = ReadMillimetres(Read("h2"));
            if ((w1 <= 0 || h1 <= 0) && TryDimensions(Read("dim"), out int dw, out int dh))
            {
                w1 = dw;
                h1 = dh;
            }
            string qtyText = Read("qty");
            int qty = qtyText.Length == 0 ? 1 : ReadQuantity(qtyText);
            if (qty <= 0) return null;
            return new ProformaGridImportRow
            {
                GlassRef = Read("ref"),
                SpecName = Read("spec"),
                GlassType = GlassBuilds.Normalize(Read("glasstype")),
                W1 = w1, H1 = h1, W2 = w2, H2 = h2,
                Qty = qty
            };
        }

        private static ProformaGridImportRow? FromPositional(IReadOnlyList<string> cells)
        {
            string At(int index) => index < cells.Count ? cells[index].Trim() : "";
            int w1;
            int h1;
            int w2 = 0;
            int h2 = 0;
            int qty;
            string specName = "";
            string glassType = "";

            if (TryDimensions(At(1), out int dimensionWidth, out int dimensionHeight))
            {
                w1 = dimensionWidth;
                h1 = dimensionHeight;
                string qtyText = At(2);
                qty = qtyText.Length == 0 ? 1 : ReadQuantity(qtyText);
                w2 = ReadMillimetres(At(3));
                h2 = ReadMillimetres(At(4));
                specName = At(5);
                glassType = At(6);
            }
            else
            {
                w1 = ReadMillimetres(At(1));
                h1 = ReadMillimetres(At(2));
                string qtyText = At(3);
                qty = qtyText.Length == 0 ? 1 : ReadQuantity(qtyText);
                w2 = ReadMillimetres(At(4));
                h2 = ReadMillimetres(At(5));
                specName = At(6);
                glassType = At(7);
            }

            if (qty <= 0) return null;
            return new ProformaGridImportRow
            {
                GlassRef = At(0), SpecName = specName,
                GlassType = GlassBuilds.Normalize(glassType),
                W1 = w1, H1 = h1, W2 = w2, H2 = h2, Qty = qty
            };
        }

        private static Dictionary<string, int>? DetectHeader(IReadOnlyList<string> row)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < row.Count; index++)
            {
                string key = NormalizeHeader(row[index]);
                if (key.Length > 0 && IsKnownHeader(key))
                    map[key] = index;
            }
            bool hasDimensions = map.ContainsKey("w1") && map.ContainsKey("h1")
                || map.ContainsKey("dim");
            return hasDimensions && (map.ContainsKey("ref") || map.ContainsKey("qty"))
                ? map : null;
        }

        private static bool IsKnownHeader(string key) =>
            key is "ref" or "w1" or "h1" or "w2" or "h2" or "qty"
                or "dim" or "spec" or "glasstype";

        private static string NormalizeHeader(string? value)
        {
            string key = Regex.Replace((value ?? "").Trim().ToLowerInvariant(), @"[\s_\-./()]", "");
            return key switch
            {
                "glassref" or "mark" or "code" or "item" or "tag" => "ref",
                "width" or "width1" or "w" => "w1",
                "height" or "height1" or "h" => "h1",
                "width2" => "w2",
                "height2" => "h2",
                "quantity" or "pieces" or "pcs" => "qty",
                "dimensions" or "size" or "panel" => "dim",
                "specname" or "specification" or "group" or "description" => "spec",
                "type" or "glass" => "glasstype",
                _ => key
            };
        }

        private static string[] SplitLooseText(string line)
        {
            string[] cells = Regex.Split(line, @"\s{2,}");
            return cells.Length > 1 ? cells : Regex.Split(line, @"\s+");
        }

        private static string[] SplitDelimitedLine(string line, char delimiter)
        {
            var cells = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            for (int index = 0; index < line.Length; index++)
            {
                char value = line[index];
                if (value == '"')
                {
                    if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                    {
                        current.Append('"');
                        index++;
                    }
                    else quoted = !quoted;
                }
                else if (value == delimiter && !quoted)
                {
                    cells.Add(current.ToString());
                    current.Clear();
                }
                else current.Append(value);
            }
            cells.Add(current.ToString());
            return cells.ToArray();
        }

        private static bool TryDimensions(string value, out int width, out int height)
        {
            Match match = DimensionPair.Match(value ?? "");
            width = match.Success ? ReadMillimetres(match.Groups[1].Value) : 0;
            height = match.Success ? ReadMillimetres(match.Groups[2].Value) : 0;
            return width > 0 && height > 0;
        }

        private static int ReadMillimetres(string value)
        {
            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number))
                return 0;
            return number <= 0m || number > int.MaxValue ? 0
                : (int)Math.Round(number, MidpointRounding.AwayFromZero);
        }

        private static int ReadQuantity(string value)
        {
            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number))
                return 0;
            return number < 1m || number > int.MaxValue ? 0
                : (int)Math.Round(number, MidpointRounding.AwayFromZero);
        }

        private static int ReadJsonInt(JsonElement element, params string[] names)
        {
            string text = ReadJsonString(element, names);
            return ReadQuantity(text);
        }

        private static string ReadJsonString(JsonElement element, params string[] names)
        {
            foreach (JsonProperty property in element.EnumerateObject())
                if (names.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)))
                    return property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? "" : property.Value.ToString();
            return "";
        }

        private static ProformaGridImportResult Failure(string warning) =>
            new() { Warning = warning };
    }
}