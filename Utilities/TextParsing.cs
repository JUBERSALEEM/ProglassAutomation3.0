// ═══════════════════════════════════════════════════════════════════════
//  Utilities/TextParsing.cs
//
//  Pure, state-free text/number parsing helpers shared across the codebase.
//  Centralised here (single source of truth) so large view-models do not
//  each carry their own private copies. All methods are static and do not
//  touch WPF or any domain type — safe to call from any layer.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;

namespace ProGlassAutomation.Utilities
{
    /// <summary>
    /// Shared text and numeric parsing helpers (no domain dependencies).
    /// </summary>
    public static class TextParsing
    {
        /// <summary>
        /// Normalises a user-typed numeric string for parsing: trims and strips
        /// thousands separators (commas) and embedded spaces. Empty/null/whitespace
        /// input collapses to "0".
        /// </summary>
        public static string CleanNum(string? s)
        {
            var cleaned = s?.Trim().Replace(",", "").Replace(" ", "");
            return string.IsNullOrEmpty(cleaned) ? "0" : cleaned;
        }

        /// <summary>
        /// Piece-count parse for import/paste. Invariant culture: comma is
        /// thousands, dot is decimal. Whole numbers only. Optional trailing
        /// pcs/pc/pieces/nos/qty. Blank is not valid here — callers treat
        /// blank as missing (default 1) separately from invalid.
        /// </summary>
        public static bool TryParsePieceQty(string? raw, out int qty)
        {
            qty = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string s = raw.Trim().Trim('"');
            s = s.Replace(" ", "").Replace(",", "");
            s = RegexReplaceUnitSuffix(s);

            if (s.Length == 0) return false;
            if (!decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d))
                return false;
            if (d <= 0m || d != decimal.Truncate(d) || d > int.MaxValue)
                return false;

            qty = (int)d;
            return true;
        }

        private static string RegexReplaceUnitSuffix(string s)
        {
            if (s.Length < 2) return s;
            string lower = s.ToLowerInvariant();
            foreach (var suffix in new[] { "pieces", "piece", "pcs", "pc", "nos", "qty" })
            {
                if (lower.EndsWith(suffix, StringComparison.Ordinal)
                    && lower.Length > suffix.Length)
                {
                    return s.Substring(0, s.Length - suffix.Length);
                }
            }
            return s;
        }

        /// <summary>
        /// Parses an invariant-culture integer, tolerating leading/trailing
        /// whitespace and empty/null input (which yields 0). Never throws.
        /// </summary>
        public static int ParseInt(string? text) =>
            int.TryParse(
                (text ?? "").Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value)
                ? value
                : 0;

        /// <summary>
        /// Returns null for blank/whitespace input, otherwise the trimmed value.
        /// </summary>
        public static string? NullIfEmpty(string s) =>
            string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        /// <summary>
        /// Splits tab-delimited clipboard text into rows, dropping blank lines
        /// and lines that contain no non-whitespace content. Each row is a
        /// tab-separated array of cell values.
        /// </summary>
        public static List<string[]> SplitClipboardRows(string clipboardText)
        {
            var lines = clipboardText.Split(
                new[] { "\r\n", "\r", "\n" },
                StringSplitOptions.RemoveEmptyEntries);

            var result = new List<string[]>(lines.Length);

            foreach (var line in lines)
            {
                var parts = line.Split('\t');

                bool hasContent = false;
                foreach (var p in parts)
                {
                    if (!string.IsNullOrWhiteSpace(p))
                    {
                        hasContent = true;
                        break;
                    }
                }

                if (hasContent)
                    result.Add(parts);
            }

            return result;
        }

        /// <summary>
        /// Maps one clipboard row (array of cell strings) into a dictionary of
        /// grid-column-index → trimmed value. Stops once the column index would
        /// exceed <paramref name="maxColumnIndex"/> so the paste never overruns
        /// the pasteable grid range.
        /// </summary>
        public static Dictionary<int, string> BuildColumnValues(
            string[] clipboardRow,
            int startColumnIndex,
            int maxColumnIndex)
        {
            var result = new Dictionary<int, string>();

            for (int clipCol = 0; clipCol < clipboardRow.Length; clipCol++)
            {
                int gridCol = startColumnIndex + clipCol;
                if (gridCol > maxColumnIndex) break;
                result[gridCol] = clipboardRow[clipCol]?.Trim() ?? "";
            }

            return result;
        }
    }
}
