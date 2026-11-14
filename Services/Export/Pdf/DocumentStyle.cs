using System;

namespace ProGlassAutomation.Services.Export.Pdf
{
    /// <summary>
    /// Live print tokens. Change the active <see cref="PdfTheme"/> and
    /// every module PDF (PI, JO, DN, TI, Statement, Sheet Store) follows.
    /// Printed documents stay light-mode — these are not UI theme brushes.
    /// </summary>
    public static class DocumentStyle
    {
        private static PdfTheme _current =
            PdfThemeCatalog.Resolve(PdfThemeCatalog.ClassicNavy);
        private static PdfMetrics _metrics = PdfMetrics.For(_current);

        public static PdfTheme Current => _current;

        /// <summary>
        /// The derived rhythm + type scale for <see cref="Current"/>.
        /// Every gap, pad and font size in the printed documents comes from
        /// here — chrome and module layouts must not hand-type numbers.
        /// </summary>
        public static PdfMetrics Metrics => _metrics;

        /// <summary>
        /// Raised after <see cref="Apply(PdfTheme)"/> actually swaps the
        /// active layout. The Step 7 print preview subscribes to this so the
        /// on-screen document re-skins live — the preview and the exported
        /// PDF can no longer drift, because they read the same tokens.
        /// Handlers may be invoked from any thread; UI subscribers are
        /// responsible for marshalling to the dispatcher.
        /// </summary>
        public static event Action<PdfTheme>? Changed;

        public static void Apply(PdfTheme theme)
        {
            if (theme == null) return;
            if (ReferenceEquals(_current, theme)) return;

            _current = theme;
            _metrics = PdfMetrics.For(theme);

            try { Changed?.Invoke(theme); }
            catch (Exception ex)
            {
                // A broken preview subscriber must never break printing.
                ProGlassAutomation.Services.Logging.AppLogService.Warning(
                    "[DocumentStyle] Changed handler failed: " + ex.Message);
            }
        }

        public static void Apply(string? layoutId) =>
            Apply(PdfThemeCatalog.Resolve(layoutId));

        public static PdfLayoutKind Kind => _current.Kind;

        // ── Fonts ─────────────────────────────────────────────────────
        public const string FontFamily = "Segoe UI";
        public const float FontSizeTitle = 20f;
        public const float FontSizeH1 = 14f;
        public const float FontSizeH2 = 11f;
        public const float FontSizeBody = 10f;
        public const float FontSizeSmall = 9f;
        public const float FontSizeCaption = 8f;

        // ── Colors (from the active print layout) ─────────────────────
        public static string ColorAccent => Current.Accent;
        public static string ColorAccentDark => Current.AccentDark;
        public static string ColorAccentLight => Current.AccentLight;
        public static string ColorAccentSoft => Current.AccentSoft;
        public static string ColorAccentOn => Current.AccentOn;
        public static string ColorText => Current.Text;
        public static string ColorMuted => Current.Muted;
        public static string ColorBorder => Current.Border;
        public static string ColorTableHeader => Current.TableHeader;
        public static string ColorTableAlt => Current.TableAlt;
        public static string ColorSurface => Current.Surface;
        public static string ColorDanger => Current.Danger;
        public static string ColorSuccess => Current.Success;
        public static string ColorWarning => Current.Warning;
        public static string ColorWhite => Current.White;
        public static string ColorHighlight => Current.Highlight;

        // ── Spacing / architecture (from the active print layout) ─────
        public static float SpaceSection => Current.ContentSpacing;
        public const float SpaceParagraph = 6f;
        public const float SpaceRow = 4f;

        public static float TableBorderThickness =>
            Current.TableGrid ? 0.75f : 0.5f;
        public static float TableCellPaddingH => Current.CellPadH;
        public static float TableCellPaddingV => Current.CellPadV;
        public static float MarginMm => Current.MarginMm;
    }
}
