// Step 5 cut-list import — header-aware table parser.
// Accepts Excel-style rows, CSV/TSV text, and loose scan lines
// (REF  W1  H1  QTY  or  REF  1000x1200  2).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProGlassAutomation.Utilities;

namespace ProGlassAutomation.Services.ProformaInvoice.Step5Grid
{
    public sealed class CutListImportRow
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

    public sealed class CutListImportResult
    {
        public List<CutListImportRow> Rows { get; } = new();
        public int Skipped { get; set; }
        public string? Warning { get; set; }
        public string Layout { get; set; } = "";
    }

    public static class CutListImportParser
    {
        private static readonly Regex DimPair = new(
            @"^\s*(\d+(?:[.,]\d+)?)\s*[xX×*]\s*(\d+(?:[.,]\d+)?)\s*$",
            RegexOptions.Compiled);

        public static CutListImportResult ParseDelimitedText(string? text)
        {
            var result = new CutListImportResult();
            if (string.IsNullOrWhiteSpace(text))
            {
                result.Warning = "File is empty.";
                return result;
            }

            var raw = SplitToTable(text);
            return ParseTable(raw);
        }

        public static CutListImportResult ParseTable(
            IReadOnlyList<IReadOnlyList<string>> table)
        {
            var result = new CutListImportResult();
            if (table == null || table.Count == 0)
            {
                result.Warning = "No rows to import.";
                return result;
            }

            int start = 0;
            var map = DetectHeader(table[0]);
            if (map != null)
            {
                start = 1;
                result.Layout = "header";
            }
            else
            {
                result.Layout = "positional";
            }

            for (int i = start; i < table.Count; i++)
            {
                var cells = table[i];
                if (cells == null || cells.Count == 0 || cells.All(string.IsNullOrWhiteSpace))
                {
                    result.Skipped++;
                    continue;
                }

                var row = map != null
                    ? FromMapped(cells, map)
                    : FromPositional(cells);

                if (row == null || (row.W1 <= 0 && row.H1 <= 0 && string.IsNullOrWhiteSpace(row.GlassRef)))
                {
                    result.Skipped++;
                    continue;
                }

                if (row.W1 <= 0 || row.H1 <= 0)
                {
                    result.Skipped++;
                    continue;
                }

                result.Rows.Add(row);
            }

            if (result.Rows.Count == 0)
                result.Warning = "No cut-list rows found. Expected REF, W1, H1, QTY (W2/H2 optional).";

            return result;
        }

        public static CutListImportResult ParseJsonArray(string? json)
        {
            var result = new CutListImportResult { Layout = "json" };
            if (string.IsNullOrWhiteSpace(json))
            {
                result.Warning = "Scanner returned no data.";
                return result;
            }

            string payload = json.Trim();
            int start = payload.IndexOf('[');
            int end = payload.LastIndexOf(']');
            if (start >= 0 && end > start)
                payload = payload.Substring(start, end - start + 1);

            try
            {
                using var doc = JsonDocument.Parse(payload);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    result.Warning = "Scanner response was not a JSON array.";
                    return result;
                }

                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object)
                    {
                        result.Skipped++;
                        continue;
                    }

                    string glassRef = ReadString(el, "ref", "glassRef", "mark", "code");
                    string specName = ReadString(el, "spec", "specName", "specification");
                    string glassType = ReadString(el, "glassType", "glasstype", "type", "glass");
                    int w1 = ReadInt(el, "w1", "width", "w");
                    int h1 = ReadInt(el, "h1", "height", "h");
                    int w2 = ReadInt(el, "w2");
                    int h2 = ReadInt(el, "h2");
                    var qtyKind = ReadPieceQty(el, out int qty);
                    if (qtyKind == PieceQtyKind.Invalid)
                    {
                        result.Skipped++;
                        continue;
                    }
                    if (qtyKind == PieceQtyKind.Missing)
                        qty = 1;

                    if (w1 <= 0 || h1 <= 0)
                    {
                        result.Skipped++;
                        continue;
                    }

                    result.Rows.Add(new CutListImportRow
                    {
                        GlassRef = glassRef,
                        SpecName = specName,
                        GlassType = glassType,
                        W1 = w1,
                        H1 = h1,
                        W2 = w2,
                        H2 = h2,
                        Qty = qty
                    });
                }
            }
            catch (JsonException ex)
            {
                result.Warning = "Could not read scanner JSON: " + ex.Message;
            }

            if (result.Rows.Count == 0 && string.IsNullOrWhiteSpace(result.Warning))
                result.Warning = "Scanner found no usable rows.";

            return result;
        }

        private static List<IReadOnlyList<string>> SplitToTable(string text)
        {
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var table = new List<IReadOnlyList<string>>(lines.Length);

            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0) continue;

                string[] parts;
                if (trimmed.Contains('\t'))
                    parts = trimmed.Split('\t');
                else if (trimmed.Contains(';'))
                    parts = trimmed.Split(';');
                else if (trimmed.Contains(','))
                    parts = SplitCsvLine(trimmed);
                else
                    parts = Regex.Split(trimmed, @"\s{2,}|\s");

                table.Add(parts.Select(p => p.Trim().Trim('"')).ToList());
            }

            return table;
        }

        private static string[] SplitCsvLine(string line)
        {
            var cells = new List<string>();
            var cur = new System.Text.StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        cur.Append('"');
                        i++;
                    }
                    else quoted = !quoted;
                    continue;
                }
                if (c == ',' && !quoted)
                {
                    cells.Add(cur.ToString());
                    cur.Clear();
                    continue;
                }
                cur.Append(c);
            }
            cells.Add(cur.ToString());
            return cells.ToArray();
        }

        private static Dictionary<string, int>? DetectHeader(IReadOnlyList<string> row)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            bool any = false;
            for (int i = 0; i < row.Count; i++)
            {
                string key = NormalizeHeader(row[i]);
                if (key.Length == 0) continue;
                if (key is "ref" or "w1" or "h1" or "w2" or "h2" or "qty" or "dim" or "spec" or "glasstype")
                {
                    map[key] = i;
                    any = true;
                }
            }
            return any ? map : null;
        }

        private static string NormalizeHeader(string? raw)
        {
            string n = (raw ?? "").Trim().ToLowerInvariant();
            n = Regex.Replace(n, @"[\s_\-./]+", "");
            return n switch
            {
                "ref" or "glassref" or "glassreference" or "mark" or "code"
                    or "item" or "panel" or "tag" => "ref",
                "w1" or "w" or "width" or "width1" or "widthmm" or "l" or "length" => "w1",
                "h1" or "h" or "height" or "height1" or "heightmm" => "h1",
                "w2" or "width2" => "w2",
                "h2" or "height2" => "h2",
                // "No" is a row-number column in Excel cut lists — not PCS.
                "qty" or "quantity" or "pcs" or "qty." or "nos" or "count" => "qty",
                "spec" or "specname" or "specification" or "glassspec" => "spec",
                "type" or "glasstype" or "glass" or "paneltype" => "glasstype",
                "size" or "dim" or "dimension" or "wxh" or "sizewh" => "dim",
                _ => ""
            };
        }

        private static CutListImportRow? FromMapped(
            IReadOnlyList<string> cells, Dictionary<string, int> map)
        {
            string glassRef = Cell(cells, map, "ref");
            string specName = Cell(cells, map, "spec");
            string glassType = Cell(cells, map, "glasstype");
            int w1 = ParseMm(Cell(cells, map, "w1"));
            int h1 = ParseMm(Cell(cells, map, "h1"));
            int w2 = ParseMm(Cell(cells, map, "w2"));
            int h2 = ParseMm(Cell(cells, map, "h2"));
            var qtyKind = ClassifyPieceQty(Cell(cells, map, "qty"), out int qty);

            if (map.TryGetValue("dim", out int dimIdx))
            {
                var (dw, dh) = ParseDimension(CellAt(cells, dimIdx));
                if (w1 <= 0) w1 = dw;
                if (h1 <= 0) h1 = dh;
            }

            if (w1 <= 0 || h1 <= 0)
            {
                var (dw, dh) = ParseDimension(glassRef);
                if (dw > 0 && dh > 0 && string.IsNullOrWhiteSpace(Cell(cells, map, "ref")) == false)
                {
                    // ref was actually a size — keep it as ref only if it has letters
                }
            }

            if (qtyKind == PieceQtyKind.Invalid)
                return null;
            if (qtyKind == PieceQtyKind.Missing)
                qty = 1;
            return new CutListImportRow
            {
                GlassRef = glassRef,
                SpecName = specName,
                GlassType = glassType,
                W1 = w1,
                H1 = h1,
                W2 = w2,
                H2 = h2,
                Qty = qty
            };
        }

        private static CutListImportRow? FromPositional(IReadOnlyList<string> cells)
        {
            var values = cells.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            if (values.Count == 0) return null;

            string glassRef = "";
            int idx = 0;
            if (!LooksNumeric(values[0]) && ParseDimension(values[0]).w == 0)
            {
                glassRef = values[0];
                idx = 1;
            }

            int w1 = 0, h1 = 0, w2 = 0, h2 = 0, qty = 1;
            if (idx < values.Count)
            {
                var dim = ParseDimension(values[idx]);
                if (dim.w > 0 && dim.h > 0)
                {
                    w1 = dim.w;
                    h1 = dim.h;
                    idx++;
                }
            }

            var nums = new List<int>();
            for (int i = idx; i < values.Count; i++)
            {
                int n = ParseMm(values[i]);
                if (n > 0) nums.Add(n);
            }

            if (w1 <= 0 && nums.Count >= 2)
            {
                w1 = nums[0];
                h1 = nums[1];
                nums.RemoveRange(0, 2);
            }

            if (nums.Count >= 3)
            {
                w2 = nums[0];
                h2 = nums[1];
                qty = nums[2];
            }
            else if (nums.Count == 2)
            {
                // Could be W2 H2 or leftover + qty. If both look like sizes (>= 100) treat as W2 H2.
                if (nums[0] >= 100 && nums[1] >= 100)
                {
                    w2 = nums[0];
                    h2 = nums[1];
                }
                else
                {
                    qty = nums[^1];
                }
            }
            else if (nums.Count == 1)
            {
                qty = nums[0];
            }

            string lastRaw = values[^1];
            var lastKind = ClassifyPieceQty(lastRaw, out int lastQty);
            bool lastIsDim = ParseDimension(lastRaw).w > 0;
            bool lastIsInnerPair = nums.Count == 2 && nums[0] >= 100 && nums[1] >= 100;
            if (lastKind == PieceQtyKind.Invalid && !lastIsDim && !lastIsInnerPair)
                return null;
            if (lastKind == PieceQtyKind.Valid && !lastIsDim && !lastIsInnerPair)
                qty = lastQty;
            if (qty <= 0) qty = 1;
            return new CutListImportRow
            {
                GlassRef = glassRef,
                W1 = w1,
                H1 = h1,
                W2 = w2,
                H2 = h2,
                Qty = qty
            };
        }


        private enum PieceQtyKind { Missing, Valid, Invalid }

        private static PieceQtyKind ClassifyPieceQty(string? raw, out int qty)
        {
            qty = 0;
            if (string.IsNullOrWhiteSpace(raw))
                return PieceQtyKind.Missing;
            return TextParsing.TryParsePieceQty(raw, out qty)
                ? PieceQtyKind.Valid
                : PieceQtyKind.Invalid;
        }

        private static PieceQtyKind ReadPieceQty(JsonElement el, out int qty)
        {
            qty = 0;
            bool present = false;
            foreach (var name in new[] { "qty", "quantity", "pcs" })
            {
                if (!el.TryGetProperty(name, out var p)) continue;
                present = true;
                if (p.ValueKind == JsonValueKind.Number)
                {
                    if (p.TryGetInt32(out int n) && n > 0)
                    {
                        qty = n;
                        return PieceQtyKind.Valid;
                    }
                    if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out decimal d))
                        return ClassifyPieceQty(d.ToString(CultureInfo.InvariantCulture), out qty);
                    return PieceQtyKind.Invalid;
                }
                if (p.ValueKind == JsonValueKind.String)
                    return ClassifyPieceQty(p.GetString(), out qty);
                return ClassifyPieceQty(p.ToString(), out qty);
            }
            return present ? PieceQtyKind.Invalid : PieceQtyKind.Missing;
        }

        private static (int w, int h) ParseDimension(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return (0, 0);
            var m = DimPair.Match(raw);
            if (!m.Success) return (0, 0);
            return (ParseMm(m.Groups[1].Value), ParseMm(m.Groups[2].Value));
        }

        private static int ParseMm(string? raw)
        {
            string cleaned = TextParsing.CleanNum(raw);
            if (int.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                return i;
            if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal d))
                return (int)Math.Round(d, MidpointRounding.AwayFromZero);
            return 0;
        }

        private static bool LooksNumeric(string s)
        {
            if (ParseDimension(s).w > 0) return true;
            return ParseMm(s) > 0 && s.Any(char.IsDigit) && !s.Any(char.IsLetter);
        }

        private static string Cell(
            IReadOnlyList<string> cells, Dictionary<string, int> map, string key)
        {
            return map.TryGetValue(key, out int i) ? CellAt(cells, i) : "";
        }

        private static string CellAt(IReadOnlyList<string> cells, int i) =>
            i >= 0 && i < cells.Count ? cells[i]?.Trim() ?? "" : "";

        private static string ReadString(JsonElement el, params string[] names)
        {
            foreach (var name in names)
            {
                if (el.TryGetProperty(name, out var p))
                    return p.ValueKind == JsonValueKind.String
                        ? p.GetString() ?? ""
                        : p.ToString();
            }
            return "";
        }

        private static int ReadInt(JsonElement el, params string[] names)
        {
            foreach (var name in names)
            {
                if (!el.TryGetProperty(name, out var p)) continue;
                if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int n))
                    return n;
                if (p.ValueKind == JsonValueKind.String)
                    return ParseMm(p.GetString());
            }
            return 0;
        }
    }
}
