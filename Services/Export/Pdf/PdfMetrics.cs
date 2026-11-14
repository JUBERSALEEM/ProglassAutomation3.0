using System;

namespace ProGlassAutomation.Services.Export.Pdf
{
    /// <summary>
    /// The derived rhythm + type scale for one <see cref="PdfTheme"/>.
    ///
    /// WHY THIS EXISTS
    /// ───────────────
    /// Before this type, spacing and font sizes were ~240 hand-typed literals
    /// scattered across PdfChrome (PaddingHorizontal(12), FontSize(6.5f),
    /// PaddingTop(22), …). Every layout author picked a number by feel, so the
    /// printed output had no vertical rhythm: a 22 pt empty signature box next
    /// to a 3 pt row gap, a 13 pt grand total next to an 8 pt table.
    ///
    /// THE CONTRACT
    /// ────────────
    /// Two independent axes, and only two:
    ///
    ///   1. KIND  → SHAPE.  Rail weights, border weights, hairline weights.
    ///      Identity. "Workshop draws a 4 pt amber rail; Editorial draws a
    ///      1.2 pt copper rule." These never scale with density — a workshop
    ///      docket must look like a workshop docket at any density.
    ///
    ///   2. CONTENTSPACING → RHYTHM. Every gap, pad and font size is a ratio
    ///      of the theme's ContentSpacing (7.5 Workshop … 15 QuietForm).
    ///      One knob, coherent scale, no per-call guesswork.
    ///
    /// Every value here is DERIVED — nothing is authored per theme — so the
    /// five layouts stay internally consistent and a future sixth layout gets
    /// a correct rhythm for free.
    ///
    /// Units are QuestPDF points (pt) unless the name says Mm.
    /// </summary>
    public sealed class PdfMetrics
    {
        private PdfMetrics(PdfTheme theme)
        {
            Theme = theme;
            Kind = theme.Kind;

            // ── Rhythm anchor ──────────────────────────────────────────
            // S is the section gap the engine already applies between
            // top-level blocks. Everything else is a ratio of it.
            float S = Math.Max(4f, theme.ContentSpacing);

            SectionGap = R1(S);
            BlockGap = R1(S * 0.62f);
            RowGap = R1(S * 0.30f);
            StackGap = R1(S * 0.18f);
            GutterGap = R1(S * 1.10f);

            CardPad = R1(S * 0.82f);
            CardHeadPadH = R1(S * 0.82f);
            CardHeadPadV = R1(S * 0.42f);
            CardTitleGap = R1(S * 0.34f);

            MetaPadH = R1(S * 0.95f);
            MetaPadV = R1(S * 0.52f);

            GroupPadH = R1(S * 0.72f);
            GroupPadV = R1(S * 0.38f);

            LetterheadPadH = R1(S * 1.15f);
            LetterheadPadV = R1(S * 0.95f);
            MastheadPadH = R1(S * 1.15f);
            MastheadPadV = R1(S * 0.80f);
            ContinuationPadV = R1(S * 0.40f);
            ContinuationPadH = R1(S * 0.75f);

            TotalsPadH = R1(S * 1.10f);
            TotalsPadV = R1(S * 0.72f);
            SubtotalPadH = R1(S * 0.78f);
            SubtotalPadV = R1(S * 0.44f);

            SignatureHeight = R1(Math.Clamp(S * 3.1f, 20f, 44f));
            SignatureGap = R1(S * 0.95f);
            SignatureRuleGap = R1(S * 0.34f);

            TablePadH = theme.CellPadH;
            TablePadV = theme.CellPadV;

            // ── Type scale ─────────────────────────────────────────────
            // Anchored on the theme's own table size so a dense workshop
            // docket and an airy board report both stay in proportion.
            float tf = Math.Max(6.4f, theme.TableFont);

            TableFont = R1(tf);
            HeaderFont = R1(Math.Max(6.8f, tf - 0.4f));
            CaptionFont = R1(ClampF(tf * 0.88f, 6.4f, 7.8f));
            KickerFont = R1(ClampF(tf * 0.82f, 6.2f, 7.6f));

            LabelFont = R1(ClampF(tf + 0.4f, 7.4f, 8.8f));
            ValueFont = R1(ClampF(tf + 0.6f, 7.8f, 9.2f));

            CardTitleFont = R1(ClampF(tf * 0.95f, 7.0f, 8.4f));
            SectionFont = R1(ClampF(tf * 1.10f, 7.6f, 10.0f));
            GroupFont = R1(ClampF(tf * 1.12f, 7.8f, 10.0f));

            MetaLabelFont = R1(ClampF(tf * 0.88f, 6.6f, 7.8f));
            MetaValueFont = R1(ClampF(tf * 1.15f, 8.2f, 9.6f));

            SubtotalLabelFont = R1(ClampF(tf * 1.02f, 7.6f, 8.8f));
            SubtotalValueFont = R1(ClampF(tf * 1.16f, 8.4f, 9.8f));

            // Grand total: a controlled step up from the table, not a cliff.
            // Was 13 pt against an 8 pt table (1.63×) with no relationship to
            // the theme; now a fixed 1.55× step that scales with the docket.
            GrandLabelFont = R1(ClampF(tf * 1.22f, 8.8f, 11.2f));
            GrandValueFont = R1(ClampF(tf * 1.55f, 11.0f, 14.0f));

            KpiLabelFont = R1(ClampF(tf * 0.88f, 6.6f, 7.8f));
            KpiValueFont = R1(ClampF(tf * 1.45f, 10.0f, 12.5f));

            SignatureFont = R1(ClampF(tf * 0.98f, 7.2f, 8.6f));
            SignatureHintFont = R1(ClampF(tf * 0.86f, 6.6f, 7.8f));

            FooterFont = R1(ClampF(tf * 0.88f, 6.6f, 7.8f));

            // ── Shape (kind-driven, deliberately NOT density-scaled) ─────
            RailWeight = Kind switch
            {
                PdfLayoutKind.Workshop => 4.0f,
                PdfLayoutKind.FormalBand => 4.0f,
                PdfLayoutKind.SplitConsole => 3.0f,
                _ => 1.2f
            };

            RailWeightHeavy = Kind switch
            {
                PdfLayoutKind.Workshop => 5.0f,
                PdfLayoutKind.FormalBand => 5.0f,
                PdfLayoutKind.SplitConsole => 4.0f,
                _ => 2.0f
            };

            HairlineWeight = Kind switch
            {
                PdfLayoutKind.Workshop => 0.8f,
                PdfLayoutKind.QuietForm => 0.4f,
                PdfLayoutKind.Editorial => 0.5f,
                _ => 0.75f
            };

            CardBorderWeight = Kind switch
            {
                PdfLayoutKind.Workshop => 0.7f,
                PdfLayoutKind.QuietForm => 0.4f,
                PdfLayoutKind.Editorial => 0.5f,
                _ => 0.6f
            };

            SignatureRuleWeight = Kind switch
            {
                PdfLayoutKind.Workshop => 0.8f,
                PdfLayoutKind.Editorial => 0.6f,
                _ => 0.6f
            };

            // ── Fixed label column ─────────────────────────────────────
            // LabelValue previously used AutoItem for the label, so every
            // value started at a different x and the cards read ragged.
            // A single shared column width fixes the alignment.
            float labelBase = Kind switch
            {
                PdfLayoutKind.Workshop => 62f,
                PdfLayoutKind.SplitConsole => 70f,
                PdfLayoutKind.Editorial => 66f,
                PdfLayoutKind.QuietForm => 78f,
                _ => 74f
            };
            LabelColumn = R1(ClampF(labelBase * (0.80f + 0.20f * (S / 10f)), 54f, 92f));
        }

        public PdfTheme Theme { get; }
        public PdfLayoutKind Kind { get; }

        // ── Rhythm (pt) ────────────────────────────────────────────────
        public float SectionGap { get; }
        public float BlockGap { get; }
        public float RowGap { get; }
        public float StackGap { get; }
        public float GutterGap { get; }

        public float CardPad { get; }
        public float CardHeadPadH { get; }
        public float CardHeadPadV { get; }
        public float CardTitleGap { get; }

        public float MetaPadH { get; }
        public float MetaPadV { get; }

        public float GroupPadH { get; }
        public float GroupPadV { get; }

        public float LetterheadPadH { get; }
        public float LetterheadPadV { get; }
        public float MastheadPadH { get; }
        public float MastheadPadV { get; }
        public float ContinuationPadH { get; }
        public float ContinuationPadV { get; }

        public float TotalsPadH { get; }
        public float TotalsPadV { get; }
        public float SubtotalPadH { get; }
        public float SubtotalPadV { get; }

        public float SignatureHeight { get; }
        public float SignatureGap { get; }
        public float SignatureRuleGap { get; }

        public float TablePadH { get; }
        public float TablePadV { get; }

        // ── Type scale (pt) ────────────────────────────────────────────
        public float TableFont { get; }
        public float HeaderFont { get; }
        public float CaptionFont { get; }
        public float KickerFont { get; }
        public float LabelFont { get; }
        public float ValueFont { get; }
        public float CardTitleFont { get; }
        public float SectionFont { get; }
        public float GroupFont { get; }
        public float MetaLabelFont { get; }
        public float MetaValueFont { get; }
        public float SubtotalLabelFont { get; }
        public float SubtotalValueFont { get; }
        public float GrandLabelFont { get; }
        public float GrandValueFont { get; }
        public float KpiLabelFont { get; }
        public float KpiValueFont { get; }
        public float SignatureFont { get; }
        public float SignatureHintFont { get; }
        public float FooterFont { get; }

        // ── Shape (kind-driven) ────────────────────────────────────────
        public float RailWeight { get; }
        public float RailWeightHeavy { get; }
        public float HairlineWeight { get; }
        public float CardBorderWeight { get; }
        public float SignatureRuleWeight { get; }
        public float LabelColumn { get; }

        /// <summary>Metrics for a theme, cached one instance per theme.</summary>
        public static PdfMetrics For(PdfTheme theme)
        {
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            return Cache.Get(theme);
        }

        private static float ClampF(float v, float lo, float hi) =>
            v < lo ? lo : (v > hi ? hi : v);

        /// <summary>Round to 1 decimal — sub-0.1 pt differences are invisible
        /// in print but make diffs and audits noisy.</summary>
        private static float R1(float v) => (float)Math.Round(v, 1);

        /// <summary>
        /// One PdfMetrics per theme instance. Themes are catalog singletons,
        /// so this is effectively a fixed five-entry cache built on first use.
        /// </summary>
        private static class Cache
        {
            private static readonly System.Collections.Concurrent
                .ConcurrentDictionary<PdfTheme, PdfMetrics> _map = new();

            public static PdfMetrics Get(PdfTheme theme) =>
                _map.GetOrAdd(theme, static t => new PdfMetrics(t));
        }
    }
}
