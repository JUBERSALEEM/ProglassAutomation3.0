using System;
using System.Collections.Generic;

namespace ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges
{
    /// <summary>
    /// Same-glass laminate polish: outer color == inner color (Clear + Clear)
    /// uses the 2× row. Different colors (HD Grey + Clear) stay on regular Polish.
    ///
    /// Colors come from Step 4 LAM / DGU+LAM pickers when that spec is live;
    /// otherwise they are read from the spec name layers.
    /// </summary>
    public static class LaminateGlassMatcher
    {
        private static readonly string[] InterlayerTokens =
        {
            "PVB", "EVA", "SGP", "ASP", "AIR", "SPACER", "ARGON", "KRYPTON"
        };

        private static readonly string[] KnownColors =
        {
            "Reflective Grey", "Ford Blue", "Dark Grey", "HD Grey",
            "Milky White", "Clear", "Bronze", "Green", "Blue",
            "Silver", "Grey", "White", "Black", "Frosted"
        };

        public static bool IsLaminateType(string? glassType)
        {
            if (string.IsNullOrWhiteSpace(glassType)) return false;
            return glassType.Equals("LAM", StringComparison.OrdinalIgnoreCase)
                || glassType.Equals("DGU+LAM", StringComparison.OrdinalIgnoreCase);
        }

        public static bool ColorsMatch(string? outerColor, string? innerColor)
        {
            string a = NormalizeColor(outerColor);
            string b = NormalizeColor(innerColor);
            return a.Length > 0 && b.Length > 0
                && string.Equals(a, b, StringComparison.Ordinal);
        }

        public static bool IsMatchedSameGlass(
            string? specName,
            string? glassType,
            string? outerColor = null,
            string? innerColor = null)
        {
            if (!IsLaminateType(glassType)) return false;

            if (ColorsMatch(outerColor, innerColor))
                return true;

            if (!string.IsNullOrWhiteSpace(outerColor)
                || !string.IsNullOrWhiteSpace(innerColor))
                return false;

            ExtractColorsFromSpecName(specName, glassType, out string parsedOuter, out string parsedInner);
            return ColorsMatch(parsedOuter, parsedInner);
        }

        public static void ExtractColorsFromSpecName(
            string? specName,
            string? glassType,
            out string outerColor,
            out string innerColor)
        {
            outerColor = "";
            innerColor = "";

            var glasses = ExtractGlassLayers(specName);
            if (glasses.Count < 2) return;

            bool dguLam = !string.IsNullOrWhiteSpace(glassType)
                && glassType.Equals("DGU+LAM", StringComparison.OrdinalIgnoreCase);

            // DGU+LAM: last two glasses are the laminated pair.
            // LAM: first and last glass are the pair.
            if (dguLam && glasses.Count >= 2)
            {
                outerColor = ExtractColorToken(glasses[glasses.Count - 2]);
                innerColor = ExtractColorToken(glasses[glasses.Count - 1]);
            }
            else
            {
                outerColor = ExtractColorToken(glasses[0]);
                innerColor = ExtractColorToken(glasses[glasses.Count - 1]);
            }
        }

        public static bool SpecNameMatchesGenerated(string? specName, string? generated)
        {
            string a = StripPolishSuffix(specName);
            string b = StripPolishSuffix(generated);
            if (a.Length == 0 || b.Length == 0) return false;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public static string ExtractColorToken(string? layer)
        {
            if (string.IsNullOrWhiteSpace(layer)) return "";
            foreach (var color in KnownColors)
            {
                if (layer.IndexOf(color, StringComparison.OrdinalIgnoreCase) >= 0)
                    return color;
            }
            return "";
        }

        public static List<string> ExtractGlassLayers(string? specName)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(specName)) return result;

            foreach (var raw in specName.Split('+'))
            {
                var layer = raw.Trim();
                if (layer.Length == 0) continue;
                if (IsInterlayer(layer)) continue;
                result.Add(layer);
            }

            return result;
        }

        private static bool IsInterlayer(string layer)
        {
            foreach (var token in InterlayerTokens)
            {
                if (layer.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static string StripPolishSuffix(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string s = name.Trim();
            int with = s.IndexOf(" With ", StringComparison.OrdinalIgnoreCase);
            if (with > 0) s = s.Substring(0, with);
            return s.Trim();
        }

        private static string NormalizeColor(string? color)
        {
            return string.IsNullOrWhiteSpace(color)
                ? ""
                : color.Trim().ToLowerInvariant();
        }
    }
}
