using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ProGlassAutomation.Services.Export.Pdf;

namespace ProGlassAutomation.Services.Export.PrintPreview
{
    /// <summary>
    /// Publishes the active PDF layout to WPF so the on-screen Step 7 preview
    /// and the exported PDF are the same document, not two designs that
    /// happen to share data.
    ///
    /// THE PROBLEM THIS SOLVES
    /// ───────────────────────
    /// The Step 7 preview used to be a hand-built XAML visual carrying ~40
    /// hard-coded slate literals (#E2E8F0, #F1F5F9, Padding="40,32",
    /// cell padding 6,5) and it never read PdfTheme. The PDF was fully
    /// theme-driven. Result: switching Settings → PDF Layout changed the
    /// export and left the preview untouched, so the two could never agree —
    /// and the preview carried no company logo at all.
    ///
    /// THE CONTRACT
    /// ────────────
    /// One source of truth (<see cref="DocumentStyle"/>), two consumers.
    /// This service mirrors the live tokens into application resources under
    /// stable "Print.*" keys. XAML binds with DynamicResource, so changing
    /// the layout re-skins the preview in place.
    ///
    /// UNIT CONVERSION — this is what makes the match exact, not approximate
    /// ────────────────────────────────────────────────────────────────────
    /// QuestPDF measures in POINTS (1 pt = 1/72 in).
    /// WPF measures in DEVICE-INDEPENDENT PIXELS (1 DIP = 1/96 in).
    /// Therefore  DIP = pt × 96/72 = pt × 4/3.
    ///
    /// The preview paper is 794 DIP wide; A4 is 595.276 pt wide, and
    /// 595.276 × 4/3 = 793.7 ≈ 794. So the preview already sits at exactly
    /// 4/3 print scale — applying PtToDip to every token puts the on-screen
    /// document at true 100% A4 size. A 15.5 mm page margin becomes 44 pt
    /// becomes 58.6 DIP on screen and 15.5 mm on paper: the same margin,
    /// three units, one number.
    /// </summary>
    public static class PrintTokenService
    {
        /// <summary>Points → WPF device-independent pixels.</summary>
        public const double PtToDip = 4.0 / 3.0;

        /// <summary>Millimetres → points.</summary>
        public const double MmToPt = 72.0 / 25.4;

        // Logo clamps, shared with PdfChrome so the on-screen chip and the
        // printed chip are the same size for the same company record.
        public const double LogoMinWidthMm = 8d;
        public const double LogoMaxWidthMm = 80d;
        public const double LogoMinHeightMm = 8d;
        public const double LogoMaxHeightMm = 70d;

        private const string DictionaryMarker = "ProGlass.PrintTokens";
        private static bool _hooked;

        /// <summary>Convert a print measurement in points to screen DIPs.</summary>
        public static double Dip(float points) => Math.Round(points * PtToDip, 2);

        /// <summary>Convert a print measurement in millimetres to screen DIPs.</summary>
        public static double DipFromMm(double mm) => Math.Round(mm * MmToPt * PtToDip, 2);

        /// <summary>
        /// Installs the token dictionary and starts tracking layout changes.
        /// Safe to call repeatedly and safe at design time (no Application).
        /// </summary>
        public static void Install()
        {
            if (Application.Current == null) return;

            Apply();

            if (_hooked) return;
            _hooked = true;
            DocumentStyle.Changed += OnLayoutChanged;
        }

        private static void OnLayoutChanged(PdfTheme theme)
        {
            // Raised from whatever thread changed the layout — a UI
            // subscriber has to get back onto the dispatcher.
            var app = Application.Current;
            if (app == null) return;

            var dispatcher = app.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                Apply();
            else
                dispatcher.BeginInvoke(new Action(Apply));
        }

        private static void Apply()
        {
            var app = Application.Current;
            if (app == null) return;

            try
            {
                var dict = Build(DocumentStyle.Current, DocumentStyle.Metrics);

                // Replace the whole merged entry rather than mutating it:
                // swapping the collection entry is guaranteed to invalidate
                // every DynamicResource that resolved through it.
                var merged = app.Resources.MergedDictionaries;
                for (int i = merged.Count - 1; i >= 0; i--)
                {
                    if (ReferenceEquals(merged[i][DictionaryMarker], Boolean.TrueString))
                        merged.RemoveAt(i);
                }
                merged.Add(dict);
            }
            catch (Exception ex)
            {
                // Preview cosmetics must never take down printing.
                Services.Logging.AppLogService.Warning(
                    "[PrintTokenService] token publish failed: " + ex.Message);
            }
        }

        private static ResourceDictionary Build(PdfTheme t, PdfMetrics m)
        {
            var d = new ResourceDictionary
            {
                [DictionaryMarker] = Boolean.TrueString
            };

            // ── Ink ────────────────────────────────────────────────────
            d["Print.Accent"] = Brush(t.Accent);
            d["Print.AccentDark"] = Brush(t.AccentDark);
            d["Print.AccentLight"] = Brush(t.AccentLight);
            d["Print.AccentSoft"] = Brush(t.AccentSoft);
            d["Print.AccentOn"] = Brush(t.AccentOn);
            d["Print.Text"] = Brush(t.Text);
            d["Print.Muted"] = Brush(t.Muted);
            d["Print.Border"] = Brush(t.Border);
            d["Print.Surface"] = Brush(t.Surface);
            d["Print.TableHeader"] = Brush(t.TableHeader);
            // Honours the theme's Zebra flag the same way PdfChrome.DataCell
            // does, so a theme that says "no striping" shows none on screen
            // either — no second switch needed in XAML.
            d["Print.TableAlt"] = Brush(t.Zebra ? t.TableAlt : t.White);
            d["Print.Danger"] = Brush(t.Danger);
            d["Print.Success"] = Brush(t.Success);
            d["Print.Warning"] = Brush(t.Warning);
            d["Print.Highlight"] = Brush(t.Highlight);
            d["Print.Paper"] = Brush(t.White);

            // Derived, so the preview has no literals of its own.
            d["Print.BorderStrong"] = Brush(Mix(t.Border, t.Text, 0.45));
            d["Print.SurfaceAlt"] = Brush(Mix(t.Surface, t.White, 0.5));
            d["Print.OnAccentMuted"] = Brush(Mix(t.AccentOn, t.Accent, 0.25));
            d["Print.Info"] = Brush(t.Accent);

            // ── Rhythm (screen DIPs) ───────────────────────────────────
            d["Print.Gap.Section"] = Dip(m.SectionGap);
            d["Print.Gap.Block"] = Dip(m.BlockGap);
            d["Print.Gap.Row"] = Dip(m.RowGap);
            d["Print.Gap.Stack"] = Dip(m.StackGap);
            d["Print.Gap.Gutter"] = Dip(m.GutterGap);
            d["Print.Gap.Signature"] = Dip(m.SignatureGap);

            d["Print.Pad.Card"] = Dip(m.CardPad);
            d["Print.Pad.CardHeadH"] = Dip(m.CardHeadPadH);
            d["Print.Pad.CardHeadV"] = Dip(m.CardHeadPadV);
            d["Print.Pad.GroupH"] = Dip(m.GroupPadH);
            d["Print.Pad.GroupV"] = Dip(m.GroupPadV);
            d["Print.Pad.MetaH"] = Dip(m.MetaPadH);
            d["Print.Pad.MetaV"] = Dip(m.MetaPadV);
            d["Print.Pad.TotalsH"] = Dip(m.TotalsPadH);
            d["Print.Pad.TotalsV"] = Dip(m.TotalsPadV);
            d["Print.Pad.TableH"] = Dip(m.TablePadH);
            d["Print.Pad.TableV"] = Dip(m.TablePadV);
            d["Print.Height.Signature"] = Dip(m.SignatureHeight);

            // Page margin: millimetres on paper, DIPs on screen, one number.
            d["Print.Page.Margin"] = DipFromMm(t.MarginMm);

            // ── Type scale ─────────────────────────────────────────────
            d["Print.Font.Table"] = Dip(m.TableFont);
            d["Print.Font.Header"] = Dip(m.HeaderFont);
            d["Print.Font.Caption"] = Dip(m.CaptionFont);
            d["Print.Font.Kicker"] = Dip(m.KickerFont);
            d["Print.Font.Label"] = Dip(m.LabelFont);
            d["Print.Font.Value"] = Dip(m.ValueFont);
            d["Print.Font.CardTitle"] = Dip(m.CardTitleFont);
            d["Print.Font.Section"] = Dip(m.SectionFont);
            d["Print.Font.Group"] = Dip(m.GroupFont);
            d["Print.Font.MetaLabel"] = Dip(m.MetaLabelFont);
            d["Print.Font.MetaValue"] = Dip(m.MetaValueFont);
            d["Print.Font.SubtotalLabel"] = Dip(m.SubtotalLabelFont);
            d["Print.Font.SubtotalValue"] = Dip(m.SubtotalValueFont);
            d["Print.Font.GrandLabel"] = Dip(m.GrandLabelFont);
            d["Print.Font.GrandValue"] = Dip(m.GrandValueFont);
            d["Print.Font.Signature"] = Dip(m.SignatureFont);
            d["Print.Font.Title"] = Dip(t.TitleSize);
            d["Print.Font.DocumentId"] = Dip(t.IdSize);

            // ── Geometry the preview needs as Thickness / width ─────────
            d["Print.Thickness.Cell"] = new Thickness(Dip(m.TablePadH), Dip(m.TablePadV),
                                                      Dip(m.TablePadH), Dip(m.TablePadV));
            d["Print.Thickness.Card"] = new Thickness(Dip(m.CardPad));
            d["Print.Thickness.CardHead"] = new Thickness(Dip(m.CardHeadPadH), Dip(m.CardHeadPadV),
                                                          Dip(m.CardHeadPadH), Dip(m.CardHeadPadV));
            d["Print.Thickness.Group"] = new Thickness(Dip(m.GroupPadH), Dip(m.GroupPadV),
                                                       Dip(m.GroupPadH), Dip(m.GroupPadV));
            d["Print.Thickness.LogoChip"] = new Thickness(Dip(m.RowGap));
            d["Print.Width.LabelColumn"] = Dip(m.LabelColumn);
            d["Print.Thickness.PageMargin"] = new Thickness(DipFromMm(t.MarginMm));

            // Vertical rhythm as ready-made Thickness values. The preview
            // used to hand-type Margin="0,0,0,16" / "0,0,0,12"; those now come
            // from the same scale the PDF paginates with, so switching layout
            // re-spaces the paper as well as recolouring it.
            d["Print.Margin.SectionBelow"] = new Thickness(0, 0, 0, Dip(m.SectionGap));
            d["Print.Margin.BlockBelow"] = new Thickness(0, 0, 0, Dip(m.BlockGap));
            d["Print.Margin.RowBelow"] = new Thickness(0, 0, 0, Dip(m.RowGap));
            d["Print.Margin.StackBelow"] = new Thickness(0, 0, 0, Dip(m.StackGap));
            d["Print.Margin.LabelRight"] = new Thickness(0, 0, Dip(m.RowGap), 0);
            d["Print.Margin.LabelRightBelow"] = new Thickness(0, 0, Dip(m.RowGap), Dip(m.RowGap));
            d["Print.Margin.LogoRight"] = new Thickness(0, 0, Dip(m.GutterGap), 0);
            d["Print.Margin.SectionAbove"] = new Thickness(0, Dip(m.SectionGap), 0, 0);
            d["Print.Margin.BlockAbove"] = new Thickness(0, Dip(m.BlockGap), 0, 0);
            d["Print.Rail.Weight"] = Dip(m.RailWeight);
            d["Print.Hairline.Weight"] = Math.Max(1d, Dip(m.HairlineWeight));

            // ── Identity ───────────────────────────────────────────────
            d["Print.Layout.Id"] = t.Id;
            d["Print.Layout.Name"] = t.DisplayName;
            // NOTE: no Print.Layout.Kind token on purpose — the raw enum
            // member is an implementation detail that must never reach a
            // screen. Curated label below is the only style identifier.
            // Curated operator-facing style family (EXECUTIVE / MODERN /
            // EDITORIAL / INDUSTRIAL / MINIMAL).
            d["Print.Layout.StyleLabel"] = t.StyleLabel;
            d["Print.Zebra"] = t.Zebra;

            return d;
        }

        /// <summary>
        /// The five selectable layouts, for the in-preview switcher.
        /// </summary>
        public static IReadOnlyList<PdfTheme> Layouts => PdfThemeCatalog.All;

        /// <summary>
        /// A wide brand mark belongs across the masthead, not squeezed into a
        /// portrait chip. Shared with PdfChrome.EffectiveLogoLayout so the
        /// preview puts the logo in the same slot the exporter will.
        /// </summary>
        public static bool IsTopPanelLogo(double widthMm, double heightMm) =>
            widthMm >= 70 || (heightMm > 0 && widthMm / heightMm >= 2.4);

        private static SolidColorBrush Brush(string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(
                string.IsNullOrWhiteSpace(hex) ? "#000000" : hex);
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        /// <summary>
        /// Linear RGB blend. Used only to derive the few tones the preview
        /// needs that no theme defines outright, so the preview never has to
        /// invent a literal of its own.
        ///
        /// Internal (not private): the PI preview-parity PDF renderer
        /// derives the SAME tones from the SAME theme so the exported page
        /// and the screen cannot disagree on a colour.
        /// </summary>
        internal static string Mix(string a, string b, double t)
        {
            var ca = (Color)ColorConverter.ConvertFromString(a);
            var cb = (Color)ColorConverter.ConvertFromString(b);
            t = t < 0 ? 0 : (t > 1 ? 1 : t);
            byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
            return $"#{L(ca.R, cb.R):X2}{L(ca.G, cb.G):X2}{L(ca.B, cb.B):X2}";
        }
    }
}
