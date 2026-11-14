using System;
using System.Collections.Generic;

namespace ProGlassAutomation.Services.Export.Pdf
{
    /// <summary>
    /// Style family of a print layout — drives derived SHAPE only (rail
    /// weights, border weights, hairlines) inside <see cref="PdfMetrics"/>.
    ///
    /// 2026-09-11 REDESIGN: this no longer switches between five different
    /// page ARCHITECTURES. Every document renders through one composition —
    /// the exact layout the Step 7 print preview shows — and the five themes
    /// are five professional STYLE VARIANTS of it (palette, rhythm, density,
    /// rules). One design is what makes WYSIWYG possible: what the operator
    /// approves on screen is byte-for-byte the structure the exporter draws.
    /// </summary>
    public enum PdfLayoutKind
    {
        FormalBand,
        SplitConsole,
        Editorial,
        Workshop,
        QuietForm
    }

    /// <summary>
    /// One professional print style. Printed documents stay light-mode.
    /// Colours support the architecture; they do not define it.
    /// </summary>
    public sealed class PdfTheme
    {
        public string Id { get; init; } = "";
        public string DisplayName { get; init; } = "";

        /// <summary>
        /// Short operator-facing style family shown on the Step 7 layout
        /// badge (EXECUTIVE / MODERN / EDITORIAL / INDUSTRIAL / MINIMAL).
        /// A curated label — never a type name or enum member.
        /// </summary>
        public string StyleLabel { get; init; } = "";

        public string Description { get; init; } = "";
        public PdfLayoutKind Kind { get; init; } = PdfLayoutKind.FormalBand;

        public string Accent { get; init; } = "#0B1F3A";
        public string AccentDark { get; init; } = "#061326";
        public string AccentLight { get; init; } = "#E8EEF5";
        public string AccentSoft { get; init; } = "#C5D0DE";
        public string AccentOn { get; init; } = "#FFFFFF";
        public string Text { get; init; } = "#111827";
        public string Muted { get; init; } = "#6B7280";
        public string Border { get; init; } = "#E5E7EB";
        public string TableHeader { get; init; } = "#0B1F3A";
        public string TableAlt { get; init; } = "#F8FAFC";
        public string Surface { get; init; } = "#F4F6F8";
        public string Danger { get; init; } = "#B91C1C";
        public string Success { get; init; } = "#047857";
        public string Warning { get; init; } = "#B45309";
        public string White { get; init; } = "#FFFFFF";
        public string Highlight { get; init; } = "#C9A227";

        public float MarginMm { get; init; } = 16f;
        public float ContentSpacing { get; init; } = 10f;
        public float TitleSize { get; init; } = 15f;
        public float IdSize { get; init; } = 16f;
        public float TableFont { get; init; } = 8f;
        public float CellPadH { get; init; } = 5f;
        public float CellPadV { get; init; } = 3.5f;
        public bool Zebra { get; init; } = true;
        public bool TableGrid { get; init; } = false;
        public bool RepeatTitleInBody { get; init; } = false;
    }

    public static class PdfThemeCatalog
    {
        public const string ClassicNavy = "ClassicNavy";
        public const string ModernTeal = "ModernTeal";
        public const string EditorialInk = "EditorialInk";
        public const string IndustrialSteel = "IndustrialSteel";
        public const string MinimalIvory = "MinimalIvory";

        // ────────────────────────────────────────────────────────────────
        //  THE FIVE STYLES — 2026-09-11 professional redesign.
        //
        //  One shared document architecture (the Step 7 preview layout);
        //  each theme restyles it with a distinct, professionally balanced
        //  palette, page margin, vertical rhythm, cell padding, type scale
        //  and rule weight. No theme is a colour swap of another: margins,
        //  density, zebra and grid behaviour differ per style.
        // ────────────────────────────────────────────────────────────────

        public static readonly IReadOnlyList<PdfTheme> All = new[]
        {
            new PdfTheme
            {
                Id = ClassicNavy,
                DisplayName = "Classic Navy",
                StyleLabel = "EXECUTIVE",
                Description = "Executive navy with a brass accent — the formal commercial letterhead for tenders and client invoices.",
                Kind = PdfLayoutKind.FormalBand,
                Accent = "#0B2E59",
                AccentDark = "#071E3B",
                AccentLight = "#E9EEF6",
                AccentSoft = "#C3D2E4",
                AccentOn = "#FFFFFF",
                Text = "#14202E",
                Muted = "#5B6B7E",
                Border = "#D5DDE7",
                TableHeader = "#0B2E59",
                TableAlt = "#F4F7FB",
                Surface = "#EEF2F8",
                Danger = "#B42318",
                Success = "#067647",
                Warning = "#B54708",
                Highlight = "#C9A227",
                MarginMm = 16f,
                ContentSpacing = 9.5f,
                TitleSize = 16f,
                IdSize = 15.5f,
                TableFont = 8f,
                CellPadH = 5.5f,
                CellPadV = 3.6f,
                Zebra = true,
                TableGrid = false,
                RepeatTitleInBody = false
            },
            new PdfTheme
            {
                Id = ModernTeal,
                DisplayName = "Modern Teal",
                StyleLabel = "MODERN",
                Description = "Calm teal with open spacing and light cyan surfaces — a clean contemporary style for day-to-day quoting.",
                Kind = PdfLayoutKind.SplitConsole,
                Accent = "#0F766E",
                AccentDark = "#134E4A",
                AccentLight = "#D8F1EE",
                AccentSoft = "#A7E5DE",
                AccentOn = "#FFFFFF",
                Text = "#10333A",
                Muted = "#537079",
                Border = "#BFE0DC",
                TableHeader = "#0F766E",
                TableAlt = "#F0FAF8",
                Surface = "#E9F7F4",
                Danger = "#B42318",
                Success = "#067647",
                Warning = "#B54708",
                Highlight = "#06B6D4",
                MarginMm = 15f,
                ContentSpacing = 11f,
                TitleSize = 19f,
                IdSize = 14f,
                TableFont = 8.2f,
                CellPadH = 6f,
                CellPadV = 4f,
                Zebra = true,
                TableGrid = false,
                RepeatTitleInBody = false
            },
            new PdfTheme
            {
                Id = EditorialInk,
                DisplayName = "Editorial Ink",
                StyleLabel = "EDITORIAL",
                Description = "Warm paper, black ink and copper rules — a premium board-room style for director and client packs.",
                Kind = PdfLayoutKind.Editorial,
                Accent = "#1B1B18",
                AccentDark = "#0C0C0A",
                AccentLight = "#F4EFE7",
                AccentSoft = "#E3D5C3",
                AccentOn = "#FAFAF7",
                Text = "#1B1B18",
                Muted = "#6E675E",
                Border = "#DAD2C6",
                TableHeader = "#1B1B18",
                TableAlt = "#FAF8F4",
                Surface = "#F5F1E9",
                Danger = "#A61B1B",
                Success = "#3F6212",
                Warning = "#B45309",
                Highlight = "#B4552D",
                MarginMm = 18f,
                ContentSpacing = 12.5f,
                TitleSize = 21f,
                IdSize = 12.5f,
                TableFont = 8f,
                CellPadH = 4.6f,
                CellPadV = 4.4f,
                Zebra = false,
                TableGrid = false,
                RepeatTitleInBody = false
            },
            new PdfTheme
            {
                Id = IndustrialSteel,
                DisplayName = "Industrial Steel",
                StyleLabel = "INDUSTRIAL",
                Description = "Graphite with a safety-amber accent and a full table grid — a dense shop-floor style for production and dispatch.",
                Kind = PdfLayoutKind.Workshop,
                Accent = "#2E3A46",
                AccentDark = "#131C26",
                AccentLight = "#E8EDF2",
                AccentSoft = "#C6D2DD",
                AccentOn = "#FFFFFF",
                Text = "#101820",
                Muted = "#55677A",
                Border = "#93A3B3",
                TableHeader = "#1C2833",
                TableAlt = "#F2F5F8",
                Surface = "#E9EEF3",
                Danger = "#B42318",
                Success = "#027A48",
                Warning = "#D97706",
                Highlight = "#F59E0B",
                MarginMm = 13f,
                ContentSpacing = 8f,
                TitleSize = 12.5f,
                IdSize = 17f,
                TableFont = 7.6f,
                CellPadH = 4.2f,
                CellPadV = 2.8f,
                Zebra = true,
                TableGrid = true,
                RepeatTitleInBody = true
            },
            new PdfTheme
            {
                Id = MinimalIvory,
                DisplayName = "Minimal Ivory",
                StyleLabel = "MINIMAL",
                Description = "Ivory paper, charcoal hairlines and generous whitespace — a quiet archive-grade style with maximum clarity.",
                Kind = PdfLayoutKind.QuietForm,
                Accent = "#222933",
                AccentDark = "#0B0F14",
                AccentLight = "#F7F3EA",
                AccentSoft = "#E5DCCB",
                AccentOn = "#FFFFFF",
                Text = "#171D26",
                Muted = "#6F675B",
                Border = "#D9D0BF",
                TableHeader = "#222933",
                TableAlt = "#FFFFFF",
                Surface = "#FAF6EE",
                Danger = "#991B1B",
                Success = "#065F46",
                Warning = "#92400E",
                Highlight = "#8B5E34",
                MarginMm = 20f,
                ContentSpacing = 14f,
                TitleSize = 11.5f,
                IdSize = 19f,
                TableFont = 8f,
                CellPadH = 3.6f,
                CellPadV = 3.8f,
                Zebra = false,
                TableGrid = false,
                RepeatTitleInBody = false
            }
        };

        public static PdfTheme Resolve(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return All[0];
            foreach (var theme in All)
            {
                if (string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(theme.DisplayName, id, StringComparison.OrdinalIgnoreCase))
                    return theme;
            }
            return All[0];
        }

        public static void ApplySaved()
        {
            string? id = null;
            try { id = App.Config?.Appearance?.PdfLayoutId; }
            catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Debug("[PdfTheme] ApplySaved: config not ready: " + ex.Message); }
            DocumentStyle.Apply(Resolve(id));
        }
    }
}
