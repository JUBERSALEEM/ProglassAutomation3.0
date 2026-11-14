using System;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Services.Proforma
{
    /// <summary>The five print-layout families retained from the legacy PDF selector.</summary>
    public enum ProformaPdfLayoutKind
    {
        FormalBand,
        SplitConsole,
        Editorial,
        Workshop,
        QuietForm
    }

    /// <summary>One print treatment. All five styles share the same invoice structure.</summary>
    public sealed record ProformaPdfStyle(
        string Id,
        ProformaPdfLayoutKind Kind,
        string Name,
        string Label,
        string Description,
        string Accent,
        string AccentDark,
        string AccentLight,
        string Text,
        string Muted,
        string Border,
        string AlternateRow,
        string Paper,
        string AccentText,
        string Highlight,
        float MarginMm,
        float SectionGap,
        float TitleSize,
        float TableFontSize,
        float CellPaddingX,
        float CellPaddingY,
        bool ZebraRows,
        bool TableGrid);

    /// <summary>The five owner-facing PDF styles; none changes the document's content order.</summary>
    public static class ProformaPdfStyles
    {
        public static readonly IReadOnlyList<ProformaPdfStyle> All = Array.AsReadOnly(new[]
        {
            new ProformaPdfStyle(
                "ClassicNavy", ProformaPdfLayoutKind.FormalBand, "Classic Navy", "EXECUTIVE",
                "Formal commercial navy with a restrained brass accent.",
                "#0B2E59", "#071E3B", "#E9EEF6", "#14202E", "#5B6B7E",
                "#D5DDE7", "#F4F7FB", "#FFFFFF", "#FFFFFF", "#C9A227",
                16f, 9.5f, 16f, 8f, 5.5f, 3.6f, true, false),
            new ProformaPdfStyle(
                "ModernTeal", ProformaPdfLayoutKind.SplitConsole, "Modern Teal", "MODERN",
                "Airy teal and cyan styling for everyday quotations.",
                "#0F766E", "#134E4A", "#D8F1EE", "#10333A", "#537079",
                "#BFE0DC", "#F0FAF8", "#FFFFFF", "#FFFFFF", "#06B6D4",
                15f, 11f, 19f, 8.2f, 6f, 4f, true, false),
            new ProformaPdfStyle(
                "EditorialInk", ProformaPdfLayoutKind.Editorial, "Editorial Ink", "EDITORIAL",
                "Warm paper, black ink and copper rules for premium client packs.",
                "#1B1B18", "#0C0C0A", "#F4EFE7", "#1B1B18", "#6E675E",
                "#DAD2C6", "#FAF8F4", "#FAFAF7", "#FAFAF7", "#B4552D",
                18f, 12.5f, 21f, 8f, 4.6f, 4.4f, false, false),
            new ProformaPdfStyle(
                "IndustrialSteel", ProformaPdfLayoutKind.Workshop, "Industrial Steel", "INDUSTRIAL",
                "Dense graphite and safety amber with a full table grid.",
                "#2E3A46", "#131C26", "#E8EDF2", "#17212B", "#536170",
                "#C6D2DD", "#F2F5F7", "#FFFFFF", "#FFFFFF", "#F59E0B",
                13f, 8f, 12.5f, 7.6f, 4.2f, 2.8f, true, true),
            new ProformaPdfStyle(
                "MinimalIvory", ProformaPdfLayoutKind.QuietForm, "Minimal Ivory", "MINIMAL",
                "Quiet ivory paper, charcoal hairlines and generous whitespace.",
                "#222933", "#0B0F14", "#F7F3EA", "#171D26", "#6F675B",
                "#D9D0BF", "#FFFFFF", "#FAF6EE", "#FFFFFF", "#8B5E34",
                20f, 14f, 11.5f, 8f, 3.6f, 3.8f, false, false)
        });

        public static ProformaPdfStyle Default => All[0];

        public static ProformaPdfStyle Resolve(string? id) =>
            All.FirstOrDefault(style =>
                string.Equals(style.Id, id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(style.Name, id, StringComparison.OrdinalIgnoreCase))
            ?? Default;
    }
}