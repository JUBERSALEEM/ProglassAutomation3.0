using ProGlassAutomation.Services.Export.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ProGlassAutomation.Services.Export.FileSystem
{
    /// <summary>
    /// One filename for every industrial export (PDF, Excel, CSV):
    ///
    ///   {DocumentId}_{Customer}_{Project}_{dd-MM-yyyy}.{ext}
    ///
    /// Example:
    ///   PI-2026-001_SKYLINE-INTERIORS-LLC_SHOWROOM-PARTITION-WORKS_30-08-2026.pdf
    ///   JOB-2026-001_SKYLINE-INTERIORS-LLC_SHOWROOM-PARTITION-WORKS_30-08-2026.xlsx
    ///   DN-2026-001_SKYLINE-INTERIORS-LLC_SHOWROOM-PARTITION-WORKS_30-08-2026.csv
    ///   INV-2026-001_SKYLINE-INTERIORS-LLC_SHOWROOM-PARTITION-WORKS_30-08-2026.pdf
    ///
    /// Empty customer or project segments are skipped (no placeholders).
    /// </summary>
    public static class ExportFileNamer
    {
        private const int MaxSegmentLength = 48;

        private static readonly Regex MultipleHyphens =
            new(@"-{2,}", RegexOptions.Compiled);

        private static readonly Regex NonAlphaNumOrHyphen =
            new(@"[^A-Z0-9\-]", RegexOptions.Compiled);

        public static string Build(IDocumentLayout layout, ExportFormat format)
        {
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));

            return Build(
                layout.DocumentId,
                layout.CompanyName,
                layout.Reference,
                layout.DocumentDate,
                format);
        }

        public static string Build(
            string? documentId,
            string? customer,
            string? project,
            DateTime documentDate,
            ExportFormat format)
        {
            var segments = new List<string>();

            string id = Sanitize(documentId);
            if (string.IsNullOrWhiteSpace(id))
                id = "DOCUMENT";
            segments.Add(id);

            string company = Sanitize(customer);
            if (!string.IsNullOrWhiteSpace(company))
                segments.Add(company);

            string reference = Sanitize(project);
            if (!string.IsNullOrWhiteSpace(reference))
                segments.Add(reference);

            DateTime date = documentDate == default ? DateTime.Now : documentDate;
            segments.Add(date.ToString("dd-MM-yyyy"));

            return string.Join("_", segments) + "." + Extension(format);
        }

        /// <summary>
        /// First non-blank value. Used by layouts so the project segment
        /// is always the identifier name (SHOWROOM-PARTITION-WORKS), not
        /// a long descriptive reference.
        /// </summary>
        public static string FirstFilled(params string?[] values)
        {
            if (values == null) return string.Empty;
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return string.Empty;
        }

        public static string Extension(ExportFormat format) => format switch
        {
            ExportFormat.Pdf => "pdf",
            ExportFormat.Excel => "xlsx",
            ExportFormat.Csv => "csv",
            _ => "bin"
        };

        /// <summary>
        /// Uppercase, spaces → hyphens, strip invalid characters,
        /// collapse hyphens, cap length.
        /// </summary>
        public static string Sanitize(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var sb = new StringBuilder(input.Length);
            bool lastWasSpace = false;
            foreach (var ch in input.Trim())
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!lastWasSpace)
                    {
                        sb.Append('-');
                        lastWasSpace = true;
                    }
                }
                else
                {
                    sb.Append(char.ToUpperInvariant(ch));
                    lastWasSpace = false;
                }
            }

            string s = sb.ToString();

            foreach (var bad in Path.GetInvalidFileNameChars())
                s = s.Replace(bad.ToString(), string.Empty);

            s = NonAlphaNumOrHyphen.Replace(s, "");
            s = MultipleHyphens.Replace(s, "-");
            s = s.Trim('-');

            if (s.Length > MaxSegmentLength)
                s = s.Substring(0, MaxSegmentLength).Trim('-');

            return s;
        }
    }
}
