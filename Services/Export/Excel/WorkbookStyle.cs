namespace ProGlassAutomation.Services.Export.Excel
{
    /// <summary>
    /// Central style tokens for all Excel documents.
    /// Change once here → applies to every module's XLSX output.
    ///
    /// Colors are hex strings applied to ClosedXML XLColor.FromHtml().
    /// Fonts and sizes stay consistent across modules for brand identity.
    /// </summary>
    public static class WorkbookStyle
    {
        // ── Fonts ─────────────────────────────────────────────────────
        public const string FontFamily = "Segoe UI";
        public const int FontSizeTitle = 16;
        public const int FontSizeH1 = 12;
        public const int FontSizeHeader = 11;
        public const int FontSizeBody = 10;
        public const int FontSizeCaption = 9;

        // ── Colors (hex without leading #) ────────────────────────────
        public const string ColorAccent = "0F4C3A";
        public const string ColorAccentLight = "E8F5EE";
        public const string ColorHeaderBg = "0F4C3A";
        public const string ColorHeaderText = "FFFFFF";
        public const string ColorAltRowBg = "F9FAFB";
        public const string ColorBorder = "E5E7EB";
        public const string ColorTotalRowBg = "F3F4F6";
        public const string ColorSubtitle = "6B7280";

        // ── Column widths (in Excel units) ────────────────────────────
        public const double DefaultColumnWidth = 15d;
        public const double NarrowColumn = 8d;
        public const double MediumColumn = 18d;
        public const double WideColumn = 30d;
    }
}