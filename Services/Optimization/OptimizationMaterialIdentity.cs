// Canonical optimization identity — structured material + piece provenance.
// Display composition strings are labels only; they are never the identity.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ProGlassAutomation.Services.Optimization
{
    /// <summary>
    /// Structured glass identity used to pack, match stock, and recover
    /// provenance. Two panes with the same colour + thickness + processing
    /// share a key even when their composition sentences differ; two panes
    /// that differ on any field never share a key.
    /// </summary>
    public static class OptimizationMaterialIdentity
    {
        public static string Normalize(string? value) =>
            (value ?? string.Empty).Trim().ToUpperInvariant();

        /// <summary>
        /// Current session-identity contract version. Values ≤ 0 are UNKNOWN/LEGACY.
        /// </summary>
        public const int CurrentIdentityVersion = 1;

        /// <summary>True only when a persisted identity version is current-valid.</summary>
        public static bool IsCurrentIdentityVersion(int version) => version > 0;

        /// <summary>
        /// Material key: type|color|thickness|processing|coating.
        /// Empty when colour or thickness is missing — never invented from
        /// composition text, and never filled with a dash for a blank colour.
        /// </summary>
        public static string BuildKey(
            string? glassType,
            string? color,
            int thicknessMm,
            string? processing,
            string? coating)
        {
            if (thicknessMm <= 0) return string.Empty;

            string col = Normalize(color);
            if (col.Length == 0) return string.Empty;

            string type = Normalize(glassType);
            return string.Join("|",
                type.Length == 0 ? "-" : type,
                col,
                thicknessMm.ToString(CultureInfo.InvariantCulture),
                Token(processing),
                Token(coating));
        }

        /// <summary>
        /// Thickness used to look up the DGU+LAM third (Lam Inner) rack.
        /// Independent of SETTINGS and of the other two panes. Zero stays
        /// unresolved — it is never substituted.
        /// </summary>
        public static int DguLamThirdRackThicknessMm(int lamInnerThicknessMm) =>
            lamInnerThicknessMm > 0 ? lamInnerThicknessMm : 0;

        /// <summary>
        /// Stock identity for a physical sheet: colour|thickness|min×max.
        /// Empty when any required field is missing — never a guessed SKU.
        /// </summary>
        public static string StockSku(string? color, int thicknessMm, int widthMm, int heightMm)
        {
            if (string.IsNullOrWhiteSpace(color) || thicknessMm <= 0 || widthMm <= 0 || heightMm <= 0)
                return string.Empty;

            int a = Math.Min(widthMm, heightMm);
            int b = Math.Max(widthMm, heightMm);
            return string.Format(CultureInfo.InvariantCulture,
                "{0}|{1}|{2}x{3}", Normalize(color), thicknessMm, a, b);
        }

        /// <summary>
        /// Stable fingerprint of a cutting list. Quantity is recorded, never
        /// mutated. Used so a later input change cannot keep a stale CURRENT
        /// optimization result.
        /// </summary>
        public static string Fingerprint(IEnumerable<CutPiece> pieces)
        {
            if (pieces == null) return string.Empty;

            var lines = pieces
                .Select(p => string.Join("\t",
                    p.ProformaId ?? string.Empty,
                    p.JobOrderId ?? string.Empty,
                    p.GlassRef ?? string.Empty,
                    p.SpecGroupNumber.ToString(CultureInfo.InvariantCulture),
                    p.SourceRowSerial.ToString(CultureInfo.InvariantCulture),
                    p.MaterialKey ?? string.Empty,
                    p.WidthMm.ToString(CultureInfo.InvariantCulture),
                    p.HeightMm.ToString(CultureInfo.InvariantCulture),
                    p.W1.ToString(CultureInfo.InvariantCulture),
                    p.H1.ToString(CultureInfo.InvariantCulture),
                    p.W2.ToString(CultureInfo.InvariantCulture),
                    p.H2.ToString(CultureInfo.InvariantCulture),
                    p.Position ?? string.Empty,
                    p.SheetStoreSku ?? string.Empty))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();

            return HashLines(lines);
        }

        public static string HashLines(IReadOnlyList<string> lines)
        {
            string joined = string.Join("\n", lines ?? Array.Empty<string>());
            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(joined));
            return Convert.ToHexString(bytes);
        }

        private static string Token(string? value)
        {
            string n = Normalize(value);
            return n.Length == 0 ? "-" : n;
        }
    }
}
