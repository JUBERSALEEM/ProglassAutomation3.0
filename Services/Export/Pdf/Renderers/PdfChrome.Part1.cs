// ═══════════════════════════════════════════════════════════════════════
// FILE: PdfChrome.Part1.cs (Phase 3.8 split — member census verified)
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.MasterData;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Services.Export.Pdf.Renderers
{
        public static partial class PdfChrome
    {
            private static PdfTheme T => DocumentStyle.Current;
        private static PdfLayoutKind K => T.Kind;

        /// <summary>
        /// Derived rhythm + type scale. Every pad, gap and font size in this
        /// file comes from here so the five layouts share one vertical rhythm
        /// instead of ~240 independently hand-typed numbers.
        /// </summary>
        private static PdfMetrics M => DocumentStyle.Metrics;

        // Section numbering is per-generation, not per-process: GeneratePdf
        // runs on a worker thread, so two concurrent exports must not
        // interleave their SEC 01 / SEC 02 counters.
        [ThreadStatic] private static int _sectionIndex;

        public static void Reset() => _sectionIndex = 0;

        // ═════════════════════════════════════════════════════════════
        //  COMPANY LOGO — loaded from the saved company record and placed
        //  per the chosen layout (None / LeftPortrait / TopPanel). Loading is
        //  fully defensive: a missing file or corrupt image must never crash
        //  a money document; the letterhead simply falls back to text.
        // ═════════════════════════════════════════════════════════════

        // internal (not private) so module-owned letterheads — e.g. the PI
        // WYSIWYG preview-parity renderer — load the company logo through
        // this ONE defensive path instead of forking their own.
        internal static byte[]? LoadLogoBytes(LetterheadInfo info)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(info.LogoPath))
                {
                    string? path = ResolveLogoPath(info.LogoPath);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        byte[] file = System.IO.File.ReadAllBytes(path);
                        if (file.Length > 0) return file;
                    }
                }

                if (!string.IsNullOrWhiteSpace(info.LogoBase64))
                {
                    byte[] b64 = Convert.FromBase64String(info.LogoBase64);
                    if (b64.Length > 0) return b64;
                }
            }
            catch
            {
                ProGlassAutomation.Services.Logging.AppLogService.Debug("Reset suppressed");
            }
            return null;
        }

        private static string? ResolveLogoPath(string? rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) return null;
            string path = rawPath.Trim().Trim('"');
            if (System.IO.File.Exists(path)) return path;

            try
            {
                if (!System.IO.Path.IsPathRooted(path))
                {
                    string baseDir = AppContext.BaseDirectory;
                    string candidate = System.IO.Path.Combine(baseDir, path);
                    if (System.IO.File.Exists(candidate)) return candidate;

                    candidate = System.IO.Path.Combine(Environment.CurrentDirectory, path);
                    if (System.IO.File.Exists(candidate)) return candidate;
                }

                string fileName = System.IO.Path.GetFileName(path);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    foreach (var dir in new[]
                    {
                        AppContext.BaseDirectory,
                        Environment.CurrentDirectory,
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
                    })
                    {
                        if (string.IsNullOrWhiteSpace(dir)) continue;
                        string candidate = System.IO.Path.Combine(dir, fileName);
                        if (System.IO.File.Exists(candidate)) return candidate;
                    }
                }
            }
            catch (Exception ex)
            {
                ProGlassAutomation.Services.Logging.AppLogService.Debug(
                    "[PdfChrome] Logo file candidate search failed: " + ex.Message);
            }

            return null;
        }

        private static CompanyLogoLayout EffectiveLogoLayout(LetterheadInfo info)
        {
            if (LoadLogoBytes(info) == null)
                return CompanyLogoLayout.None;

            // Shared with PrintTokenService so the on-screen preview routes
            // the logo to the same slot the exporter does.
            bool looksWide = PrintPreview.PrintTokenService.IsTopPanelLogo(
                info.LogoWidthMm, info.LogoHeightMm);

            if (info.LogoLayout == CompanyLogoLayout.TopPanel || looksWide)
                return CompanyLogoLayout.TopPanel;

            // Legacy-safe fallback: old records often have a path/base64 but
            // still have layout None. Treat that as a normal left letterhead.
            return CompanyLogoLayout.LeftPortrait;
        }

        private static bool HasLogo(LetterheadInfo info) =>
            LoadLogoBytes(info) != null;

        /// <summary>
        /// One separated contact strip built from whichever company contact
        /// fields are populated. Returns null when there is nothing to show,
        /// so callers can skip the row entirely instead of printing an
        /// empty spacer.
        /// </summary>
        private static string? ContactLine(LetterheadInfo info)
        {
            var parts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(info.CompanyEmail))
                parts.Add(info.CompanyEmail.Trim());
            if (!string.IsNullOrWhiteSpace(info.CompanyPhone))
                parts.Add(info.CompanyPhone.Trim());
            return parts.Count == 0 ? null : string.Join("   ·   ", parts);
        }

        // Clamp bounds are shared with PrintTokenService so the on-screen
        // Step 7 preview and the printed chip are the same physical size for
        // the same company record.
        private static float LogoW(LetterheadInfo info) =>
            Math.Clamp((float)(info.LogoWidthMm > 0 ? info.LogoWidthMm : 22),
                (float)PrintPreview.PrintTokenService.LogoMinWidthMm,
                (float)PrintPreview.PrintTokenService.LogoMaxWidthMm);

        private static float TopPanelLogoW(LetterheadInfo info) =>
            Math.Clamp((float)(info.LogoWidthMm > 0 ? info.LogoWidthMm : 160), 40f, 180f);

        private static float LogoH(LetterheadInfo info) =>
            Math.Clamp((float)(info.LogoHeightMm > 0 ? info.LogoHeightMm : 26),
                (float)PrintPreview.PrintTokenService.LogoMinHeightMm,
                (float)PrintPreview.PrintTokenService.LogoMaxHeightMm);

        private static float TopPanelLogoH(LetterheadInfo info) =>
            Math.Clamp((float)(info.LogoHeightMm > 0 ? info.LogoHeightMm : 24), 10f, 32f);

        /// <summary>
        /// Renders the sized logo box on a white chip (so a transparent PNG
        /// stays legible on the navy / steel mastheads). No-op without a logo.
        ///
        /// Two layouts:
        ///   LeftPortrait — portrait box at the configured W × H.
        ///   TopPanel     — the box is capped to masthead height (≤14 mm) so
        ///                  it reads as a top-panel lockup rather than a tall
        ///                  portrait.
        /// </summary>
        private static void LogoBox(IContainer container, LetterheadInfo info)
        {
            byte[]? bytes = LoadLogoBytes(info);
            if (bytes == null) return;
            float h = EffectiveLogoLayout(info) == CompanyLogoLayout.TopPanel
                ? TopPanelLogoH(info)
                : LogoH(info);
            container
                .Background(T.White)
                .Border(M.CardBorderWeight)
                .BorderColor(T.Border)
                .Padding(M.RowGap)
                .Width(LogoW(info), Unit.Millimetre)
                .Height(h, Unit.Millimetre)
                .Image(bytes)
                .FitArea();
        }

        // ═════════════════════════════════════════════════════════════
        //  LETTERHEAD — five page-1 compositions
        // ═════════════════════════════════════════════════════════════

        public static void Letterhead(IContainer container, LetterheadInfo info)
        {
            Reset();

            // TopPanel (2026-09-07): the logo lives in a FULL-WIDTH masthead
            // band across the top of the page, with the company identity and
            // document reference beside it. The regular composition follows
            // below with its inline logo suppressed so the logo never
            // renders twice. LeftPortrait / None render exactly as before
            // (logo inline at the left of the company identity).
            if (EffectiveLogoLayout(info) == CompanyLogoLayout.TopPanel)
            {
                TopPanelMasthead(container, info);
                return;
            }

            RenderHeader(container, info, suppressLogo: false);
        }

        private static void RenderHeader(
            IContainer container, LetterheadInfo info, bool suppressLogo)
        {
            switch (K)
            {
                case PdfLayoutKind.SplitConsole:
                    LetterheadSplit(container, info, suppressLogo);
                    break;
                case PdfLayoutKind.Editorial:
                    LetterheadEditorial(container, info, suppressLogo);
                    break;
                case PdfLayoutKind.Workshop:
                    LetterheadWorkshop(container, info, suppressLogo);
                    break;
                case PdfLayoutKind.QuietForm:
                    LetterheadQuiet(container, info, suppressLogo);
                    break;
                default:
                    LetterheadFormal(container, info, suppressLogo);
                    break;
            }
        }

        /// <summary>
        /// Full-width industrial masthead for the TopPanel logo layout: the
        /// logo sits in a framed white chip on the left, the company identity
        /// beside it, and the document title + reference on the right — all
        /// on one accent band that spans the whole page width.
        /// </summary>
        private static void TopPanelMasthead(IContainer container, LetterheadInfo info)
        {
            byte[]? bytes = LoadLogoBytes(info);

            container.Background(T.Accent)
                .PaddingHorizontal(M.MastheadPadH).PaddingVertical(M.MastheadPadV)
                .Column(col =>
                {
                    if (bytes != null)
                    {
                        // Full-width / landscape brand lockup. The operator's
                        // Company module size is honoured, but capped to page-safe
                        // dimensions so a long logo can span the masthead without
                        // crushing document identity or flowing outside the page.
                        col.Item().AlignCenter()
                            .Background(T.White)
                            .Border(M.CardBorderWeight)
                            .BorderColor(T.Border)
                            .Padding(M.RowGap)
                            .Width(TopPanelLogoW(info), Unit.Millimetre)
                            .Height(TopPanelLogoH(info), Unit.Millimetre)
                            .Image(bytes)
                            .FitArea();
                    }

                    col.Item().PaddingTop(bytes != null ? M.BlockGap : 0).Row(row =>
                    {
                        // Proportional split, not a fixed 200 pt right block:
                        // the masthead has to hold together on A4 portrait
                        // (507 pt of content) and on landscape catalogues
                        // without crushing either side.
                        row.RelativeItem(1.55f).Column(left =>
                        {
                            left.Item().Text(info.CompanyName ?? "")
                                .FontSize(T.TitleSize).Bold().FontColor(T.AccentOn);

                            var sub = info.CompanyLocation ?? "";
                            if (!string.IsNullOrWhiteSpace(info.CompanyTrn))
                                sub = string.IsNullOrWhiteSpace(sub)
                                    ? $"TRN {info.CompanyTrn}"
                                    : $"{sub}  ·  TRN {info.CompanyTrn}";
                            if (!string.IsNullOrWhiteSpace(sub))
                                left.Item().PaddingTop(M.StackGap).Text(sub)
                                    .FontSize(M.MetaValueFont).FontColor(T.AccentSoft);
                        });

                        row.ConstantItem(M.GutterGap);

                        row.RelativeItem().AlignRight().Column(right =>
                        {
                            right.Item().AlignRight()
                                .Text((info.DocumentTitle ?? "").ToUpperInvariant())
                                .FontSize(M.KickerFont).Bold().FontColor(T.AccentOn);
                            right.Item().AlignRight().PaddingTop(M.StackGap)
                                .Text(info.DocumentId ?? "")
                                .FontSize(T.IdSize).Bold().FontColor(T.Highlight);
                            if (!string.IsNullOrWhiteSpace(info.SubtitleLine))
                                right.Item().AlignRight().PaddingTop(M.StackGap)
                                    .Text(info.SubtitleLine)
                                    .FontSize(M.CaptionFont).FontColor(T.AccentSoft);
                        });
                    });

                    if (info.Meta.Count > 0)
                        col.Item().PaddingTop(M.BlockGap).Element(c => MetaStrip(c, info.Meta));
                });
        }

        public static void Continuation(IContainer container, LetterheadInfo info)
        {
            switch (K)
            {
                case PdfLayoutKind.Workshop:
                    container.Background(T.AccentDark)
                        .PaddingHorizontal(M.ContinuationPadH).PaddingVertical(M.ContinuationPadV)
                        .Row(row =>
                        {
                            row.RelativeItem().Text(info.CompanyName ?? "")
                                .FontSize(M.MetaValueFont).SemiBold().FontColor(T.AccentOn);
                            row.RelativeItem().AlignCenter()
                                .Text((info.DocumentTitle ?? "").ToUpperInvariant())
                                .FontSize(M.MetaValueFont).FontColor(T.AccentSoft);
                            row.RelativeItem().AlignRight()
                                .Text(info.DocumentId ?? "")
                                .FontSize(M.MetaValueFont).Bold().FontColor(T.Highlight);
                        });
                    break;

                case PdfLayoutKind.QuietForm:
                    container.BorderBottom(M.HairlineWeight).BorderColor(T.Border)
                        .PaddingBottom(M.ContinuationPadV)
                        .Row(row =>
                        {
                            row.RelativeItem().Text(info.CompanyName ?? "")
                                .FontSize(M.MetaValueFont).FontColor(T.Muted);
                            row.RelativeItem().AlignRight()
                                .Text($"{info.DocumentTitle}  {info.DocumentId}")
                                .FontSize(M.MetaValueFont).FontColor(T.Text);
                        });
                    break;

                case PdfLayoutKind.Editorial:
                    container.BorderBottom(M.HairlineWeight).BorderColor(T.Highlight)
                        .PaddingBottom(M.ContinuationPadV)
                        .Row(row =>
                        {
                            row.RelativeItem()
                                .Text((info.DocumentTitle ?? "").ToUpperInvariant())
                                .FontSize(M.MetaValueFont).FontColor(T.Muted);
                            row.RelativeItem().AlignRight()
                                .Text(info.DocumentId ?? "")
                                .FontSize(M.SubtotalValueFont).SemiBold().FontColor(T.Text);
                        });
                    break;

                default:
                    container.Column(col =>
                    {
                        col.Item().Height(M.RailWeight * 0.5f).Background(T.Accent);
                        col.Item().PaddingTop(M.ContinuationPadV).PaddingBottom(M.StackGap).Row(row =>
                        {
                            row.RelativeItem().Text(info.CompanyName ?? "")
                                .FontSize(M.MetaValueFont).SemiBold().FontColor(T.Accent);
                            row.RelativeItem().AlignRight()
                                .Text($"{info.DocumentTitle}  ·  {info.DocumentId}")
                                .FontSize(M.MetaValueFont).FontColor(T.Muted);
                        });
                    });
                    break;
            }
        }

        private static void LetterheadFormal(IContainer container, LetterheadInfo info, bool suppressLogo)
        {
            string? contact = ContactLine(info);

            container.Border(M.CardBorderWeight).BorderColor(T.Border).Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.ConstantItem(M.RailWeight).Background(T.Highlight);
                    row.RelativeItem().Background(T.Accent)
                        .PaddingHorizontal(M.LetterheadPadH).PaddingVertical(M.LetterheadPadV)
                        .Row(inner =>
                        {
                            if (!suppressLogo && HasLogo(info))
                                inner.AutoItem().PaddingRight(M.GutterGap)
                                    .AlignMiddle()
                                    .Element(c => LogoBox(c, info));

                            inner.RelativeItem().Column(left =>
                            {
                                left.Item().Text(info.CompanyName ?? "")
                                    .FontSize(T.TitleSize).Bold().FontColor(T.AccentOn);
                                var sub = info.CompanyLocation ?? "";
                                if (!string.IsNullOrWhiteSpace(info.CompanyTrn))
                                    sub = string.IsNullOrWhiteSpace(sub)
                                        ? $"TRN  {info.CompanyTrn}"
                                        : $"{sub}  ·  TRN  {info.CompanyTrn}";
                                if (!string.IsNullOrWhiteSpace(sub))
                                    left.Item().PaddingTop(M.StackGap).Text(sub)
                                        .FontSize(M.MetaValueFont).FontColor(T.AccentSoft);

                                // Real supplier contact, not internal jargon:
                                // this line is the client's route back to us.
                                if (contact != null)
                                    left.Item().PaddingTop(M.StackGap).Text(contact)
                                        .FontSize(M.CaptionFont).FontColor(T.AccentSoft);
                            });

                            // Proportional, not a fixed 210 pt block — on A4
                            // portrait that was 41% of the content width and
                            // crushed the company identity beside it.
                            inner.ConstantItem(M.GutterGap);

                            inner.RelativeItem().AlignRight().Column(right =>
                            {
                                right.Item().AlignRight()
                                    .Background(T.Highlight)
                                    .PaddingHorizontal(M.GroupPadH).PaddingVertical(M.GroupPadV)
                                    .Text((info.DocumentTitle ?? "").ToUpperInvariant())
                                    .FontSize(M.KickerFont).Bold().FontColor(T.AccentDark);
                                right.Item().AlignRight().PaddingTop(M.RowGap)
                                    .Text(info.DocumentId ?? "")
                                    .FontSize(T.IdSize).Bold().FontColor(T.AccentOn);
                                if (!string.IsNullOrWhiteSpace(info.SubtitleLine))
                                    right.Item().AlignRight().PaddingTop(M.StackGap)
                                        .Text(info.SubtitleLine)
                                        .FontSize(M.CaptionFont).FontColor(T.AccentSoft);
                            });
                        });
                });

                if (info.Meta.Count > 0)
                    col.Item().Element(c => MetaStrip(c, info.Meta));
            });
        }


        private static void LetterheadSplit(IContainer container, LetterheadInfo info, bool suppressLogo)
        {
            string? contact = ContactLine(info);

            container.Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem(1.5f).BorderLeft(M.RailWeight).BorderColor(T.Highlight)
                        .PaddingLeft(M.LetterheadPadH).Column(left =>
                        {
                            // Eyebrow carries real supplier contact when the
                            // company record has it, instead of a filler
                            // "ERP DOCUMENT" label the client cannot act on.
                            if (contact != null)
                                left.Item().Text(contact)
                                    .FontSize(M.CaptionFont).Bold().FontColor(T.Highlight);
                            left.Item().Text((info.DocumentTitle ?? "").ToUpperInvariant())
                                .FontSize(T.TitleSize).Bold().FontColor(T.Accent);
                            left.Item().PaddingTop(M.RowGap).Text(info.DocumentId ?? "")
                                .FontSize(T.IdSize).SemiBold().FontColor(T.Text);
                            if (!string.IsNullOrWhiteSpace(info.SubtitleLine))
                                left.Item().PaddingTop(M.StackGap).Text(info.SubtitleLine)
                                    .FontSize(M.MetaValueFont).FontColor(T.Muted);
                        });

                    row.ConstantItem(M.GutterGap);

                    row.RelativeItem()
                        .Border(M.CardBorderWeight).BorderColor(T.Border)
                        .Background(T.Surface)
                        .Padding(M.CardPad)
                        .Column(right =>
                        {
                            right.Item().Row(r =>
                            {
                                if (!suppressLogo && HasLogo(info))
                                    r.AutoItem().PaddingRight(M.RowGap)
                                        .AlignMiddle()
                                        .Element(c => LogoBox(c, info));
                                r.RelativeItem().Column(txt =>
                                {
                                    txt.Item().Text(info.CompanyName ?? "")
                                        .FontSize(M.GrandLabelFont).Bold().FontColor(T.Accent);
                                    if (!string.IsNullOrWhiteSpace(info.CompanyLocation))
                                        txt.Item().PaddingTop(M.StackGap)
                                            .Text(info.CompanyLocation)
                                            .FontSize(M.MetaValueFont).FontColor(T.Muted);
                                    if (!string.IsNullOrWhiteSpace(info.CompanyTrn))
                                        txt.Item().PaddingTop(M.RowGap)
                                            // AlignLeft first — without it the
                                            // background stretched the chip
                                            // across the whole card behind a
                                            // handful of characters.
                                            .AlignLeft()
                                            .Background(T.Accent)
                                            .PaddingHorizontal(M.GroupPadH).PaddingVertical(M.GroupPadV)
                                            .Text($"VAT / TRN  {info.CompanyTrn}")
                                            .FontSize(M.KickerFont).Bold().FontColor(T.AccentOn);
                                });
                            });
                            foreach (var (label, value) in info.Meta.Take(3))
                            {
                                if (string.IsNullOrWhiteSpace(value)) continue;
                                right.Item().PaddingTop(M.RowGap).Row(r =>
                                {
                                    r.ConstantItem(M.LabelColumn * 0.85f).Text(label.ToUpperInvariant())
                                        .FontSize(M.MetaLabelFont).FontColor(T.Muted);
                                    r.RelativeItem().Text(value)
                                        .FontSize(M.MetaValueFont).SemiBold().FontColor(T.Text);
                                });
                            }
                        });
                });

                col.Item().PaddingTop(M.BlockGap).Row(row =>
                {
                    row.RelativeItem().Height(M.HairlineWeight).Background(T.Accent);
                    row.ConstantItem(M.RowGap);
                    row.ConstantItem(M.SectionGap * 4.4f).Height(M.HairlineWeight).Background(T.Highlight);
                });
            });
        }


        private static void LetterheadEditorial(IContainer container, LetterheadInfo info, bool suppressLogo)
        {
            string? contact = ContactLine(info);

            container.Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        // Editorial masthead eyebrow: supplier contact when we
                        // have it, otherwise the masthead simply starts at the
                        // title — no filler label.
                        if (contact != null)
                            left.Item().Text(contact)
                                .FontSize(M.CaptionFont).FontColor(T.Highlight);
                        left.Item().PaddingTop(M.StackGap).Text(info.DocumentTitle ?? "")
                            .FontSize(T.TitleSize).FontColor(T.Text);
                    });

                    row.ConstantItem(M.GutterGap);

                    row.RelativeItem().AlignRight().Column(right =>
                    {
                        right.Item().AlignRight().Text(info.CompanyName ?? "")
                            .FontSize(M.MetaValueFont).SemiBold().FontColor(T.Muted);
                        if (!string.IsNullOrWhiteSpace(info.CompanyTrn))
                            right.Item().AlignRight().PaddingTop(M.StackGap)
                                .Text($"TRN {info.CompanyTrn}")
                                .FontSize(M.CaptionFont).FontColor(T.Muted);
                    });
                });

                col.Item().PaddingTop(M.RowGap).Row(row =>
                {
                    row.ConstantItem(M.SectionGap * 3.8f).Height(M.RailWeight).Background(T.Highlight);
                    row.RelativeItem().Height(M.HairlineWeight).AlignMiddle().Background(T.Border);
                });

                col.Item().PaddingTop(M.BlockGap).Row(row =>
                {
                    if (!suppressLogo && HasLogo(info))
                        row.AutoItem().PaddingRight(M.GutterGap)
                            .AlignMiddle()
                            .Element(c => LogoBox(c, info));

                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("DOCUMENT REFERENCE")
                            .FontSize(M.MetaLabelFont).FontColor(T.Muted);
                        c.Item().Text(info.DocumentId ?? "—")
                            .FontSize(M.GrandLabelFont).SemiBold().FontColor(T.Text);
                    });

                    foreach (var (label, value) in info.Meta.Take(3))
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(label.ToUpperInvariant())
                                .FontSize(M.MetaLabelFont).FontColor(T.Muted);
                            c.Item().Text(string.IsNullOrWhiteSpace(value) ? "—" : value)
                                .FontSize(M.MetaValueFont).SemiBold().FontColor(T.Text);
                        });
                    }
                });

                if (!string.IsNullOrWhiteSpace(info.CompanyLocation))
                    col.Item().PaddingTop(M.RowGap).Text(info.CompanyLocation)
                        .FontSize(M.MetaValueFont).FontColor(T.Muted);

                col.Item().PaddingTop(M.BlockGap).Height(M.HairlineWeight).Background(T.Border);
            });
        }


        private static void LetterheadWorkshop(IContainer container, LetterheadInfo info, bool suppressLogo)
        {
            container.Border(M.RailWeight * 0.2f).BorderColor(T.AccentDark).Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.ConstantItem(M.RailWeightHeavy).Background(T.Highlight);
                    row.RelativeItem().Background(T.AccentDark)
                        .PaddingHorizontal(M.LetterheadPadH).PaddingVertical(M.LetterheadPadV)
                        .Row(inner =>
                        {
                            if (!suppressLogo && HasLogo(info))
                                inner.AutoItem().PaddingRight(M.GutterGap)
                                    .AlignMiddle()
                                    .Element(c => LogoBox(c, info));

                            inner.RelativeItem().Column(left =>
                            {
                                left.Item().Text(info.CompanyName ?? "")
                                    .FontSize(M.GrandLabelFont).Bold().FontColor(T.AccentOn);
                                var sub = info.CompanyLocation ?? "";
                                if (!string.IsNullOrWhiteSpace(info.CompanyTrn))
                                    sub = string.IsNullOrWhiteSpace(sub)
                                        ? $"TRN {info.CompanyTrn}"
                                        : $"{sub}  ·  TRN {info.CompanyTrn}";
                                if (!string.IsNullOrWhiteSpace(sub))
                                    left.Item().PaddingTop(M.StackGap).Text(sub)
                                        .FontSize(M.MetaValueFont).FontColor(T.AccentSoft);
                                // Retained: on a shop-floor docket "CONTROLLED
                                // COPY" is a real ISO-9001 document-control
                                // statement, not filler.
                                left.Item().PaddingTop(M.RowGap)
                                    .Text("FACTORY RELEASE · QA TRACEABLE · CONTROLLED COPY")
                                    .FontSize(M.CaptionFont).Bold().FontColor(T.Highlight);
                            });

                            inner.ConstantItem(M.GutterGap);

                            inner.RelativeItem().AlignRight().Column(right =>
                            {
                                right.Item().AlignRight()
                                    .Text((info.DocumentTitle ?? "").ToUpperInvariant())
                                    .FontSize(T.TitleSize).Bold().FontColor(T.Highlight);
                                right.Item().AlignRight().PaddingTop(M.StackGap)
                                    .Text(info.DocumentId ?? "")
                                    .FontSize(T.IdSize).Bold().FontColor(T.AccentOn);
                            });
                        });
                });

                var kpi = new List<(string, string)>();
                if (!string.IsNullOrWhiteSpace(info.SubtitleLine))
                    kpi.Add(("REF", info.SubtitleLine));
                foreach (var m in info.Meta.Take(4))
                    if (!string.IsNullOrWhiteSpace(m.Value))
                        kpi.Add((m.Label.ToUpperInvariant(), m.Value));

                if (kpi.Count > 0)
                {
                    col.Item().Background(T.Surface)
                        .BorderTop(M.CardBorderWeight).BorderColor(T.Border)
                        .PaddingHorizontal(M.MetaPadH).PaddingVertical(M.MetaPadV)
                        .Row(row =>
                        {
                            foreach (var (label, value) in kpi)
                            {
                                row.RelativeItem().BorderLeft(M.RailWeight * 0.25f).BorderColor(T.Highlight)
                                    .PaddingLeft(M.RowGap).Column(c =>
                                    {
                                        c.Item().Text(label)
                                            .FontSize(M.MetaLabelFont).Bold().FontColor(T.Muted);
                                        c.Item().Text(value)
                                            .FontSize(M.MetaValueFont).Bold().FontColor(T.Text);
                                    });
                            }
                        });
                }
            });
        }


        private static void LetterheadQuiet(IContainer container, LetterheadInfo info, bool suppressLogo)
        {
            string? contact = ContactLine(info);

            container.Column(col =>
            {
                col.Item().Row(row =>
                {
                    if (!suppressLogo && HasLogo(info))
                        row.AutoItem().PaddingRight(M.GutterGap)
                            .AlignMiddle()
                            .Element(c => LogoBox(c, info));

                    row.RelativeItem().Column(col2 =>
                    {
                        col2.Item().Text(info.CompanyName ?? "")
                            .FontSize(M.GrandLabelFont).FontColor(T.Text);
                        if (!string.IsNullOrWhiteSpace(info.CompanyLocation)
                            || !string.IsNullOrWhiteSpace(info.CompanyTrn))
                        {
                            var line = info.CompanyLocation ?? "";
                            if (!string.IsNullOrWhiteSpace(info.CompanyTrn))
                                line = string.IsNullOrWhiteSpace(line)
                                    ? $"TRN {info.CompanyTrn}"
                                    : $"{line}  ·  TRN {info.CompanyTrn}";
                            col2.Item().PaddingTop(M.StackGap).Text(line)
                                .FontSize(M.MetaValueFont).FontColor(T.Muted);
                        }
                    });

                    row.AutoItem().AlignRight().Column(right =>
                    {
                        if (contact != null)
                            right.Item().AlignRight().Text(contact)
                                .FontSize(M.CaptionFont).FontColor(T.Muted);
                        // Labelled as a print stamp — an undated date next to
                        // the document's own issue date reads as a contradiction.
                        right.Item().AlignRight().PaddingTop(M.StackGap)
                            .Text($"PRINTED {DateTime.Now:dd MMM yyyy}")
                            .FontSize(M.CaptionFont).FontColor(T.Text);
                    });
                });

                col.Item().PaddingTop(M.BlockGap).Height(M.HairlineWeight).Background(T.Text);

                col.Item().PaddingTop(M.SectionGap)
                    .Text((info.DocumentTitle ?? "").ToUpperInvariant())
                    .FontSize(T.TitleSize).FontColor(T.Muted);

                col.Item().PaddingTop(M.StackGap).Text(info.DocumentId ?? "")
                    .FontSize(T.IdSize).FontColor(T.Text);

                if (info.Meta.Count > 0)
                {
                    col.Item().PaddingTop(M.BlockGap).Row(row =>
                    {
                        foreach (var (label, value) in info.Meta.Take(4))
                        {
                            row.RelativeItem().BorderTop(M.HairlineWeight).BorderColor(T.Border)
                                .PaddingTop(M.RowGap).Column(c =>
                                {
                                    c.Item().Text(label.ToUpperInvariant())
                                        .FontSize(M.MetaLabelFont).FontColor(T.Muted);
                                    c.Item().Text(string.IsNullOrWhiteSpace(value) ? "—" : value)
                                        .FontSize(M.MetaValueFont).FontColor(T.Text);
                                });
                        }
                    });
                }

                col.Item().PaddingTop(M.SectionGap).Height(M.HairlineWeight).Background(T.Border);
            });
        }


        public static void Footer(IContainer container, string? documentId)
        {
            switch (K)
            {
                case PdfLayoutKind.Workshop:
                    container.Background(T.AccentDark)
                        .PaddingHorizontal(M.MetaPadH).PaddingVertical(M.MetaPadV)
                        .Row(row =>
                        {
                            row.RelativeItem().Text(text =>
                            {
                                text.DefaultTextStyle(x => x.FontSize(M.FooterFont).FontColor(T.AccentSoft));
                                if (!string.IsNullOrWhiteSpace(documentId))
                                {
                                    text.Span(documentId).SemiBold().FontColor(T.AccentOn);
                                    text.Span("   ");
                                }
                                text.Span($"Printed {DateTime.Now:dd MMM yyyy  HH:mm}");
                            });
                            row.RelativeItem().AlignRight().Text(text =>
                            {
                                text.DefaultTextStyle(x => x.FontSize(M.FooterFont).FontColor(T.AccentSoft));
                                text.Span("PAGE ");
                                text.CurrentPageNumber().Bold().FontColor(T.AccentOn);
                                text.Span(" / ");
                                text.TotalPages().Bold().FontColor(T.AccentOn);
                            });
                        });
                    break;

                case PdfLayoutKind.Editorial:
                    container.Column(col =>
                    {
                        col.Item().Height(M.HairlineWeight).Background(T.Highlight);
                        col.Item().PaddingTop(M.RowGap).AlignCenter().Text(text =>
                        {
                            text.DefaultTextStyle(x => x.FontSize(M.FooterFont).FontColor(T.Muted));
                            if (!string.IsNullOrWhiteSpace(documentId))
                            {
                                text.Span(documentId.ToUpperInvariant());
                                text.Span("   ·   ");
                            }
                            text.Span("PAGE ");
                            text.CurrentPageNumber();
                            text.Span(" OF ");
                            text.TotalPages();
                        });
                    });
                    break;

                case PdfLayoutKind.QuietForm:
                    container.AlignCenter().Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(M.FooterFont).FontColor(T.Muted));
                        text.CurrentPageNumber();
                        text.Span("  /  ");
                        text.TotalPages();
                    });
                    break;

                case PdfLayoutKind.SplitConsole:
                    container.Column(col =>
                    {
                        col.Item().Height(M.HairlineWeight).Background(T.Border);
                        col.Item().PaddingTop(M.RowGap).Row(row =>
                        {
                            row.RelativeItem().Text($"Printed {DateTime.Now:dd MMM yyyy}")
                                .FontSize(M.FooterFont).FontColor(T.Muted);
                            row.RelativeItem().AlignCenter().Text(documentId ?? "")
                                .FontSize(M.FooterFont).SemiBold().FontColor(T.Text);
                            row.RelativeItem().AlignRight().Text(text =>
                            {
                                text.DefaultTextStyle(x => x.FontSize(M.FooterFont).FontColor(T.Muted));
                                text.CurrentPageNumber().SemiBold().FontColor(T.Text);
                                text.Span(" / ");
                                text.TotalPages();
                            });
                        });
                    });
                    break;

                default:
                    container.Column(col =>
                    {
                        col.Item().Height(M.RailWeight * 0.4f).Background(T.Accent);
                        col.Item().PaddingTop(M.RowGap).Row(row =>
                        {
                            row.RelativeItem().Text(text =>
                            {
                                text.DefaultTextStyle(x => x
                                    .FontSize(M.FooterFont)
                                    .FontColor(T.Muted));
                                if (!string.IsNullOrWhiteSpace(documentId))
                                {
                                    text.Span(documentId).SemiBold().FontColor(T.Text);
                                    text.Span("  ·  ");
                                }
                                text.Span($"Printed {DateTime.Now:dd MMM yyyy  HH:mm}");
                            });
                            row.RelativeItem().AlignRight().Text(text =>
                            {
                                text.DefaultTextStyle(x => x
                                    .FontSize(M.FooterFont)
                                    .FontColor(T.Muted));
                                text.Span("Page ");
                                text.CurrentPageNumber().SemiBold().FontColor(T.Text);
                                text.Span("  /  ");
                                text.TotalPages().SemiBold().FontColor(T.Text);
                            });
                        });
                    });
                    break;
            }
        }

        // ═════════════════════════════════════════════════════════════
        //  SECTION / GROUP / HAIRLINE
        // ═════════════════════════════════════════════════════════════

        public static void Section(IContainer container, string title)
        {
            _sectionIndex++;
            var label = title ?? "";

            switch (K)
            {
                case PdfLayoutKind.SplitConsole:
                    container.BorderLeft(M.RailWeight).BorderColor(T.Highlight)
                        .PaddingLeft(M.GroupPadH).Row(row =>
                        {
                            row.RelativeItem().Text(label.ToUpperInvariant())
                                .FontSize(M.SectionFont).SemiBold().FontColor(T.Accent);
                            row.AutoItem().Text($"SEC {_sectionIndex:00}")
                                .FontSize(M.KickerFont).Bold().FontColor(T.Muted);
                        });
                    break;

                case PdfLayoutKind.Editorial:
                    container.Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.AutoItem().Text($"{_sectionIndex:00}")
                                .FontSize(M.GrandLabelFont).FontColor(T.Highlight);
                            row.RelativeItem().PaddingLeft(M.RowGap)
                                .Text(label.ToUpperInvariant())
                                .FontSize(M.SectionFont).SemiBold().FontColor(T.Text);
                            row.AutoItem().AlignBottom().Text("SECTION")
                                .FontSize(M.KickerFont).FontColor(T.Muted);
                        });
                        col.Item().PaddingTop(M.RowGap).Row(r =>
                        {
                            r.ConstantItem(M.SectionGap * 3.8f).Height(M.RailWeight).Background(T.Highlight);
                            r.RelativeItem().Height(M.HairlineWeight).AlignMiddle().Background(T.Border);
                        });
                    });
                    break;

                case PdfLayoutKind.Workshop:
                    container.Row(row =>
                    {
                        row.ConstantItem(M.RailWeight).Background(T.Highlight);
                        row.RelativeItem().Background(T.Accent)
                            .PaddingHorizontal(M.GroupPadH).PaddingVertical(M.GroupPadV)
                            .Row(inner =>
                            {
                                inner.RelativeItem().Text(label.ToUpperInvariant())
                                    .FontSize(M.SectionFont).Bold().FontColor(T.AccentOn);
                                inner.AutoItem().Text($"QA-{_sectionIndex:00}")
                                    .FontSize(M.KickerFont).Bold().FontColor(T.Highlight);
                            });
                    });
                    break;

                case PdfLayoutKind.QuietForm:
                    container.Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text(label.ToUpperInvariant())
                                .FontSize(M.SectionFont).FontColor(T.Muted);
                            row.AutoItem().Text($"{_sectionIndex:00}")
                                .FontSize(M.KickerFont).FontColor(T.Muted);
                        });
                        col.Item().PaddingTop(M.StackGap).Height(M.HairlineWeight).Background(T.Text);
                    });
                    break;

                default:
                    container.Row(row =>
                    {
                        row.ConstantItem(M.RailWeight).Background(T.Highlight);
                        row.RelativeItem().Background(T.Accent)
                            .PaddingHorizontal(M.GroupPadH).PaddingVertical(M.GroupPadV)
                            .Row(inner =>
                            {
                                inner.RelativeItem().Text(label.ToUpperInvariant())
                                    .FontSize(M.SectionFont).Bold().FontColor(T.AccentOn);
                                inner.AutoItem().Text($"{_sectionIndex:00}")
                                    .FontSize(M.KickerFont).Bold().FontColor(T.AccentSoft);
                            });
                    });
                    break;
            }
        }


        public static void GroupBanner(IContainer container, string text, string? trailing = null)
        {
            switch (K)
            {
                case PdfLayoutKind.Editorial:
                case PdfLayoutKind.QuietForm:
                    container.BorderLeft(M.RailWeight).BorderColor(T.Highlight)
                        .PaddingLeft(M.GroupPadH).PaddingBottom(M.RowGap).Row(row =>
                        {
                            row.RelativeItem().Text(text ?? "")
                                .FontSize(M.GroupFont).SemiBold().FontColor(T.Text);
                            if (!string.IsNullOrWhiteSpace(trailing))
                                row.AutoItem().Text(trailing)
                                    .FontSize(M.MetaValueFont).FontColor(T.Muted);
                        });
                    break;

                case PdfLayoutKind.SplitConsole:
                    container.BorderLeft(M.RailWeight * 0.7f).BorderColor(T.Accent)
                        .Background(T.Surface)
                        .PaddingHorizontal(M.GroupPadH).PaddingVertical(M.GroupPadV)
                        .Row(row =>
                        {
                            row.RelativeItem().Text(text ?? "")
                                .FontSize(M.GroupFont).SemiBold().FontColor(T.Accent);
                            if (!string.IsNullOrWhiteSpace(trailing))
                                row.AutoItem().Text(trailing)
                                    .FontSize(M.MetaValueFont).FontColor(T.Muted);
                        });
                    break;

                case PdfLayoutKind.Workshop:
                    container.Row(row =>
                    {
                        row.ConstantItem(M.RailWeight * 0.75f).Background(T.Highlight);
                        row.RelativeItem().Background(T.AccentLight)
                            .BorderTop(M.CardBorderWeight).BorderBottom(M.CardBorderWeight).BorderColor(T.Border)
                            .PaddingHorizontal(M.GroupPadH).PaddingVertical(M.GroupPadV)
                            .Row(inner =>
                            {
                                inner.RelativeItem().Text(text ?? "")
                                    .FontSize(M.GroupFont).Bold().FontColor(T.AccentDark);
                                if (!string.IsNullOrWhiteSpace(trailing))
                                    inner.AutoItem().Text(trailing)
                                        .FontSize(M.MetaLabelFont).FontColor(T.Muted);
                            });
                    });
                    break;

                default:
                    container.Row(row =>
                    {
                        row.ConstantItem(M.RailWeight * 0.75f).Background(T.Highlight);
                        row.RelativeItem().Background(T.Accent)
                            .PaddingHorizontal(M.GroupPadH).PaddingVertical(M.GroupPadV)
                            .Row(inner =>
                            {
                                inner.RelativeItem().Text(text ?? "")
                                    .FontSize(M.GroupFont).Bold().FontColor(T.AccentOn);
                                if (!string.IsNullOrWhiteSpace(trailing))
                                    inner.AutoItem().Text(trailing)
                                        .FontSize(M.MetaValueFont).FontColor(T.AccentSoft);
                            });
                    });
                    break;
            }
        }


        public static void Hairline(IContainer container)
        {
            string color = K switch
            {
                PdfLayoutKind.Editorial => T.Highlight,
                PdfLayoutKind.Workshop => T.Accent,
                _ => T.Border
            };
            container.Height(M.HairlineWeight).Background(color);
        }

        // ═════════════════════════════════════════════════════════════
        //  INFO CARDS / META / LABEL-VALUE
        // ═════════════════════════════════════════════════════════════

        public static void InfoCard(
            IContainer container,
            string title,
            Action<ColumnDescriptor> body)
        {
            switch (K)
            {
                case PdfLayoutKind.SplitConsole:
                    container.BorderLeft(M.RailWeight * 0.7f).BorderColor(T.Accent)
                        .Background(T.Surface).Padding(M.CardPad).Column(col =>
                        {
                            col.Item().Text(title.ToUpperInvariant())
                                .FontSize(M.CardTitleFont).SemiBold().FontColor(T.Accent);
                            col.Item().PaddingTop(M.CardTitleGap);
                            body(col);
                        });
                    break;

                case PdfLayoutKind.Editorial:
                    container.BorderTop(M.HairlineWeight).BorderColor(T.Border)
                        .PaddingTop(M.RowGap).Column(col =>
                        {
                            col.Item().Text(title.ToUpperInvariant())
                                .FontSize(M.CardTitleFont).FontColor(T.Highlight);
                            col.Item().PaddingTop(M.CardTitleGap);
                            body(col);
                        });
                    break;

                case PdfLayoutKind.QuietForm:
                    container.BorderTop(M.HairlineWeight).BorderBottom(M.HairlineWeight).BorderColor(T.Border)
                        .PaddingVertical(M.RowGap).Column(col =>
                        {
                            col.Item().Text(title.ToUpperInvariant())
                                .FontSize(M.CardTitleFont).FontColor(T.Muted);
                            col.Item().PaddingTop(M.CardTitleGap);
                            body(col);
                        });
                    break;

                case PdfLayoutKind.Workshop:
                    container.Border(M.CardBorderWeight).BorderColor(T.Border).Column(col =>
                    {
                        col.Item().Background(T.AccentLight)
                            .BorderLeft(M.RailWeight).BorderColor(T.Highlight)
                            .PaddingHorizontal(M.CardHeadPadH).PaddingVertical(M.CardHeadPadV)
                            .Text(title.ToUpperInvariant())
                            .FontSize(M.CardTitleFont).Bold().FontColor(T.AccentDark);
                        col.Item().Padding(M.CardPad);
                        body(col);
                    });
                    break;

                default:
                    container.Border(M.CardBorderWeight).BorderColor(T.Border)
                        .Background(T.White)
                        .Column(col =>
                        {
                            col.Item().Background(T.Surface)
                                .BorderLeft(M.RailWeight).BorderColor(T.Highlight)
                                .PaddingHorizontal(M.CardHeadPadH).PaddingVertical(M.CardHeadPadV)
                                .Text(title.ToUpperInvariant())
                                .FontSize(M.CardTitleFont).Bold().FontColor(T.Accent);
                            col.Item().Padding(M.CardPad);
                            body(col);
                        });
                    break;
            }
        }


        public static void MetaStrip(
            IContainer container,
            IReadOnlyList<(string Label, string Value)> items)
        {
            var shown = (items ?? Array.Empty<(string, string)>())
                .Where(i => !string.IsNullOrWhiteSpace(i.Value))
                .Take(5)
                .ToList();
            if (shown.Count == 0)
            {
                container.Height(0.01f).Background(T.White);
                return;
            }

            IContainer box = K switch
            {
                PdfLayoutKind.Workshop => container.Background(T.Surface)
                    .BorderBottom(M.CardBorderWeight).BorderColor(T.Border),
                PdfLayoutKind.SplitConsole => container.Background(T.Surface),
                PdfLayoutKind.FormalBand => container.Background(T.Surface)
                    .BorderBottom(M.CardBorderWeight).BorderColor(T.Border),
                _ => container
            };

            box.PaddingHorizontal(M.MetaPadH).PaddingVertical(M.MetaPadV).Row(row =>
            {
                foreach (var (label, value) in shown)
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(label.ToUpperInvariant())
                            .FontSize(M.MetaLabelFont).FontColor(T.Muted);
                        c.Item().PaddingTop(M.StackGap).Text(value)
                            .FontSize(M.MetaValueFont).SemiBold().FontColor(T.Text);
                    });
                }
            });
        }

        /// <summary>
        /// One label/value pair on a fixed label column.
        ///
        /// The label column used to be AutoItem, so every row's value started
        /// at a different x and the cards read ragged. A shared
        /// <see cref="PdfMetrics.LabelColumn"/> width aligns the whole card —
        /// and every card in the document — on one vertical.
        /// </summary>
        public static void LabelValue(ColumnDescriptor col, string label, string? value)
        {
            col.Item().PaddingTop(M.RowGap).Row(r =>
            {
                r.ConstantItem(M.LabelColumn)
                    .Text(label)
                    .FontSize(M.LabelFont).FontColor(T.Muted);
                r.RelativeItem()
                    .Text(string.IsNullOrWhiteSpace(value) ? "—" : value)
                    .FontSize(M.ValueFont).SemiBold().FontColor(T.Text);
            });
        }

        // ═════════════════════════════════════════════════════════════
        //  TABLE CELLS
        // ═════════════════════════════════════════════════════════════

        public static void HeaderCell(
            IContainer cell, string text,
            bool center = true, bool right = false,
            string? background = null)
        {
            IContainer painted;
            if (!string.IsNullOrWhiteSpace(background))
            {
                painted = cell.Background(background)
                    .Border(T.TableGrid ? M.CardBorderWeight : 0)
                    .BorderColor(T.AccentDark);
            }
            else
            {
                painted = K switch
                {
                    PdfLayoutKind.SplitConsole =>
                        cell.Background(T.AccentLight)
                            .BorderBottom(M.RailWeight * 0.33f).BorderColor(T.Accent),
                    PdfLayoutKind.Editorial =>
                        cell.BorderBottom(M.RailWeight).BorderColor(T.Text)
                            .BorderTop(M.HairlineWeight).BorderColor(T.Text),
                    PdfLayoutKind.QuietForm =>
                        cell.BorderBottom(M.CardBorderWeight).BorderColor(T.Text),
                    PdfLayoutKind.Workshop =>
                        cell.Background(T.TableHeader)
                            .Border(M.CardBorderWeight).BorderColor(T.AccentDark),
                    _ =>
                        cell.Background(T.TableHeader)
                };
            }

            painted = painted
                .PaddingHorizontal(T.CellPadH)
                .PaddingVertical(T.CellPadV);

            if (right) painted = painted.AlignRight();
            else if (center) painted = painted.AlignCenter();

            var color = !string.IsNullOrWhiteSpace(background)
                ? T.AccentOn
                : K is PdfLayoutKind.Editorial
                    or PdfLayoutKind.QuietForm
                    or PdfLayoutKind.SplitConsole
                    ? T.Text
                    : T.AccentOn;

            painted.Text(text ?? "")
                .FontSize(M.HeaderFont)
                .Bold()
                .FontColor(color);
        }

        public static void DataCell(
            IContainer cell,
            string text,
            bool alt = false,
            bool center = false,
            bool right = false,
            bool bold = false,
            string? color = null,
            string? background = null)
        {
            var bg = !string.IsNullOrWhiteSpace(background)
                ? background
                : (T.Zebra && alt) ? T.TableAlt : T.White;
            IContainer painted = cell.Background(bg);

            if (T.TableGrid)
                painted = painted.Border(M.CardBorderWeight).BorderColor(T.Border);
            else
                painted = painted.BorderBottom(M.HairlineWeight * 0.6f).BorderColor(T.Border);

            painted = painted
                .PaddingHorizontal(T.CellPadH)
                .PaddingVertical(T.CellPadV);

            if (right) painted = painted.AlignRight();
            else if (center) painted = painted.AlignCenter();

            var txt = painted.Text(text ?? "").FontSize(M.TableFont);
            if (bold) txt.Bold();
            txt.FontColor(color ?? T.Text);
        }
    }
}
