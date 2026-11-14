// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Sharing/ShareMimeTypes.cs
// PROJECT: ProGlassAutomation
// MODULE: Sharing Center — extension → MIME map (Stage 1)
//
// Single home for "what MIME type is this attachment". Providers must not
// guess, and must never hardcode one content type for every file.
// Only extensions the app can actually produce are supported; anything
// else is refused at ShareableArtifact.FromFile, not mislabeled here.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace ProGlassAutomation.Services.Sharing
{
    public static class ShareMimeTypes
    {
        private static readonly Dictionary<string, string> Map =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [".pdf"] = "application/pdf",
                [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                [".xls"] = "application/vnd.ms-excel",
                [".csv"] = "text/csv",
                // ScreenshotService-class producers (wired when a surface
                // shares them; the map existing does not expose them).
                [".png"] = "image/png",
                [".jpg"] = "image/jpeg",
                [".jpeg"] = "image/jpeg",
                [".txt"] = "text/plain",
            };

        public static string ForExtension(string? extension)
        {
            if (string.IsNullOrWhiteSpace(extension)) return "application/octet-stream";
            return Map.TryGetValue(extension.Trim(), out string? mime)
                ? mime
                : "application/octet-stream";
        }

        public static ShareFormat FormatForExtension(string? extension)
        {
            if (string.IsNullOrWhiteSpace(extension)) return ShareFormat.Unknown;
            string ext = extension.Trim().ToLowerInvariant();
            if (ext == ".pdf") return ShareFormat.Pdf;
            if (ext == ".xlsx" || ext == ".xls") return ShareFormat.Excel;
            if (ext == ".csv") return ShareFormat.Csv;
            return ShareFormat.Unknown;
        }

        /// <summary>
        /// True only for formats an ERP exporter actually generates today.
        /// The map above may know more types than sharing accepts.
        /// </summary>
        public static bool IsSupported(string? extension) =>
            FormatForExtension(extension) != ShareFormat.Unknown;
    }
}
