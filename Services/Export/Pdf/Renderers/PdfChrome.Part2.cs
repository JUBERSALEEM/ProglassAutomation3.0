// ═══════════════════════════════════════════════════════════════════════
// FILE: PdfChrome.Part2.cs (Phase 3.8 split — member census verified)
// ═══════════════════════════════════════════════════════════════════════

using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Services.Export.Pdf.Renderers
{
        public static partial class PdfChrome
    {

        // ═════════════════════════════════════════════════════════════
        //  INDUSTRIAL ERP DOCUMENT CONTROL PANEL
        // ═════════════════════════════════════════════════════════════

        public static void IndustrialDocumentPanel(
            IContainer container,
            string title,
            string subtitle,
            params (string Label, string Value, string? Color)[] metrics)
        {
            var shown = (metrics ?? Array.Empty<(string, string, string?)>())
                .Where(m => !string.IsNullOrWhiteSpace(m.Label)
                    && !string.IsNullOrWhiteSpace(m.Value))
                .Take(5)
                .ToList();

            IContainer box = K switch
            {
                PdfLayoutKind.Workshop => container.BorderLeft(M.RailWeight).BorderColor(T.Highlight)
                    .Background(T.AccentDark),
                PdfLayoutKind.SplitConsole => container.BorderLeft(M.RailWeight).BorderColor(T.Accent)
                    .Background(T.Surface),
                PdfLayoutKind.Editorial => container.BorderTop(M.RailWeight).BorderBottom(M.HairlineWeight)
                    .BorderColor(T.Highlight),
                PdfLayoutKind.QuietForm => container.BorderTop(M.RailWeight * 0.7f).BorderBottom(M.HairlineWeight)
                    .BorderColor(T.Text),
                _ => container.Border(M.CardBorderWeight).BorderColor(T.Border)
                    .Background(T.Surface)
            };

            bool dark = K == PdfLayoutKind.Workshop;
            string titleColor = dark ? T.AccentOn : T.Accent;
            string subtitleColor = dark ? T.AccentSoft : T.Muted;
            string valueFallback = dark ? T.AccentOn : T.Text;

            box.PaddingHorizontal(M.MetaPadH).PaddingVertical(M.MetaPadV).Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        left.Item().Text((title ?? "").ToUpperInvariant())
                            .FontSize(M.GroupFont).Bold().FontColor(titleColor);
                        if (!string.IsNullOrWhiteSpace(subtitle))
                            left.Item().PaddingTop(M.StackGap).Text(subtitle)
                                .FontSize(M.CaptionFont).FontColor(subtitleColor);
                    });

                    row.AutoItem().AlignMiddle()
                        .Background(dark ? T.Highlight : T.Accent)
                        .PaddingHorizontal(M.GroupPadH).PaddingVertical(M.GroupPadV)
                        .Text("ERP CONTROLLED")
                        .FontSize(M.KickerFont).Bold()
                        .FontColor(dark ? T.AccentDark : T.AccentOn);
                });

                if (shown.Count > 0)
                {
                    col.Item().PaddingTop(M.BlockGap).Row(row =>
                    {
                        foreach (var metric in shown)
                        {
                            row.RelativeItem().Element(c =>
                                IndustrialMetric(c, metric.Label, metric.Value,
                                    metric.Color ?? valueFallback, dark));
                        }
                    });
                }
            });
        }

        private static void IndustrialMetric(
            IContainer container,
            string label,
            string value,
            string valueColor,
            bool dark)
        {
            container.BorderLeft(M.RailWeight * 0.35f)
                .BorderColor(dark ? T.Highlight : T.Accent)
                .PaddingLeft(M.RowGap)
                .Column(col =>
                {
                    col.Item().Text(label.ToUpperInvariant())
                        .FontSize(M.MetaLabelFont)
                        .FontColor(dark ? T.AccentSoft : T.Muted);
                    col.Item().PaddingTop(M.StackGap).Text(value)
                        .FontSize(M.KpiValueFont).Bold()
                        .FontColor(valueColor);
                });
        }

        public static void IndustrialAssuranceNote(
            IContainer container,
            string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            container.BorderLeft(M.RailWeight * 0.5f).BorderColor(T.Accent)
                .Background(T.Surface)
                .PaddingHorizontal(M.GroupPadH).PaddingVertical(M.GroupPadV)
                .Text(text)
                .FontSize(M.CaptionFont)
                .Italic()
                .FontColor(T.Muted);
        }

        // ═════════════════════════════════════════════════════════════
        //  TOTALS
        // ═════════════════════════════════════════════════════════════

        public static void GrandTotal(IContainer container, string label, string amount)
        {
            switch (K)
            {
                case PdfLayoutKind.SplitConsole:
                    container.Background(T.Surface)
                        .BorderLeft(M.RailWeight).BorderColor(T.Accent)
                        .PaddingHorizontal(M.TotalsPadH).PaddingVertical(M.TotalsPadV)
                        .Row(r =>
                        {
                            r.RelativeItem().Text(label)
                                .FontSize(M.GrandLabelFont).SemiBold().FontColor(T.Accent);
                            r.AutoItem().Text(amount)
                                .FontSize(M.GrandValueFont).Bold().FontColor(T.Accent);
                        });
                    break;

                case PdfLayoutKind.Editorial:
                    container.Column(col =>
                    {
                        col.Item().Height(M.HairlineWeight).Background(T.Text);
                        col.Item().PaddingTop(M.RowGap).Row(r =>
                        {
                            r.RelativeItem().Text(label.ToUpperInvariant())
                                .FontSize(M.MetaValueFont).FontColor(T.Muted);
                            r.AutoItem().Text(amount)
                                .FontSize(M.GrandValueFont).FontColor(T.Text);
                        });
                    });
                    break;

                case PdfLayoutKind.QuietForm:
                    container.BorderTop(M.RailWeight * 0.8f).BorderColor(T.Text)
                        .PaddingTop(M.RowGap)
                        .Row(r =>
                        {
                            r.RelativeItem().Text(label)
                                .FontSize(M.GrandLabelFont).FontColor(T.Text);
                            r.AutoItem().Text(amount)
                                .FontSize(M.GrandValueFont).FontColor(T.Text);
                        });
                    break;

                case PdfLayoutKind.Workshop:
                    container.Background(T.AccentDark)
                        .PaddingHorizontal(M.TotalsPadH).PaddingVertical(M.TotalsPadV)
                        .Row(r =>
                        {
                            r.RelativeItem().Text(label.ToUpperInvariant())
                                .FontSize(M.GrandLabelFont).Bold().FontColor(T.Highlight);
                            r.AutoItem().Text(amount)
                                .FontSize(M.GrandValueFont).Bold().FontColor(T.AccentOn);
                        });
                    break;

                default:
                    container.Background(T.Accent)
                        .PaddingHorizontal(M.TotalsPadH).PaddingVertical(M.TotalsPadV)
                        .Row(r =>
                        {
                            r.RelativeItem().Text(label)
                                .FontSize(M.GrandLabelFont).Bold().FontColor(T.AccentOn);
                            r.AutoItem().Text(amount)
                                .FontSize(M.GrandValueFont).Bold().FontColor(T.AccentOn);
                        });
                    break;
            }
        }

        /// <summary>
        /// An intermediate total. Deliberately NOT painted in the success
        /// colour: green on a money document reads as "profit", and a
        /// subtotal is neither profit nor loss. Subtotals stay neutral;
        /// only GrandTotal carries emphasis.
        /// </summary>
        public static void SubtotalBar(IContainer container, string left, string right)
        {
            switch (K)
            {
                case PdfLayoutKind.Editorial:
                case PdfLayoutKind.QuietForm:
                    container.PaddingTop(M.RowGap).Row(r =>
                    {
                        r.RelativeItem().Text(left)
                            .FontSize(M.SubtotalLabelFont).FontColor(T.Muted);
                        r.AutoItem().Text(right)
                            .FontSize(M.SubtotalValueFont).SemiBold().FontColor(T.Text);
                    });
                    break;

                case PdfLayoutKind.SplitConsole:
                    container.Background(T.Surface)
                        .PaddingHorizontal(M.SubtotalPadH).PaddingVertical(M.SubtotalPadV)
                        .Row(r =>
                        {
                            r.RelativeItem().Text(left)
                                .FontSize(M.SubtotalLabelFont).FontColor(T.Accent);
                            r.AutoItem().Text(right)
                                .FontSize(M.SubtotalValueFont).Bold().FontColor(T.Accent);
                        });
                    break;

                default:
                    container.Background(T.Surface)
                        .BorderBottom(M.CardBorderWeight)
                        .BorderColor(T.Border)
                        .PaddingHorizontal(M.SubtotalPadH).PaddingVertical(M.SubtotalPadV)
                        .Row(r =>
                        {
                            r.RelativeItem().Text(left)
                                .FontSize(M.SubtotalLabelFont).Bold().FontColor(T.Accent);
                            r.AutoItem().Text(right)
                                .FontSize(M.SubtotalValueFont).Bold().FontColor(T.Text);
                        });
                    break;
            }
        }

        public static void KpiStrip(
            IContainer container,
            params (string Label, string Value, string? Color)[] items)
        {
            IContainer box = K switch
            {
                PdfLayoutKind.Workshop => container.Background(T.AccentDark),
                PdfLayoutKind.FormalBand => container.Background(T.TableHeader),
                PdfLayoutKind.SplitConsole => container.Background(T.Surface),
                _ => container.BorderTop(M.CardBorderWeight).BorderColor(T.Border)
            };

            var labelColor = K is PdfLayoutKind.Workshop or PdfLayoutKind.FormalBand
                ? T.AccentSoft : T.Muted;
            var valueFallback = K is PdfLayoutKind.Workshop or PdfLayoutKind.FormalBand
                ? T.AccentOn : T.Text;

            box.Padding(M.MetaPadV).Row(row =>
            {
                foreach (var (label, value, color) in items)
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(label.ToUpperInvariant())
                            .FontSize(M.KpiLabelFont).FontColor(labelColor);
                        c.Item().Text(value)
                            .FontSize(M.KpiValueFont).Bold()
                            .FontColor(color ?? valueFallback);
                    });
                }
            });
        }

        // ═════════════════════════════════════════════════════════════
        //  SIGNATURES
        // ═════════════════════════════════════════════════════════════

        public static void Signatures(IContainer container, params SignatureSlot[] slots)
        {
            if (slots == null || slots.Length == 0) return;

            container.Row(row =>
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    if (i > 0) row.ConstantItem(M.SignatureGap);
                    var slot = slots[i];
                    row.RelativeItem().Element(c => RenderSignatureSlot(c, slot));
                }
            });
        }

        private static void RenderSignatureSlot(IContainer container, SignatureSlot slot)
        {
            switch (K)
            {
                case PdfLayoutKind.SplitConsole:
                case PdfLayoutKind.Editorial:
                case PdfLayoutKind.QuietForm:
                    container.Column(col =>
                    {
                        col.Item().Height(M.SignatureHeight)
                            .BorderBottom(M.SignatureRuleWeight).BorderColor(T.Text);
                        col.Item().PaddingTop(M.SignatureRuleGap).Text(slot.Title)
                            .FontSize(M.SignatureFont).FontColor(T.Muted);
                        if (slot.Body != null)
                            slot.Body(col);
                        else
                        {
                            col.Item().PaddingTop(M.StackGap).Text("Name  ____________________")
                                .FontSize(M.SignatureHintFont).FontColor(T.Muted);
                            col.Item().Text("Date  ____________________")
                                .FontSize(M.SignatureHintFont).FontColor(T.Muted);
                        }
                    });
                    break;

                case PdfLayoutKind.Workshop:
                    container.Border(M.CardBorderWeight * 1.3f).BorderColor(T.Accent)
                        .Padding(M.CardPad).Column(col =>
                    {
                        col.Item().Text(slot.Title.ToUpperInvariant())
                            .FontSize(M.SignatureHintFont).Bold().FontColor(T.Highlight);
                        col.Item().PaddingTop(M.SignatureHeight * 0.55f)
                            .BorderBottom(M.SignatureRuleWeight).BorderColor(T.Text)
                            .Text(" ").FontSize(M.SignatureHintFont);
                        if (slot.Body != null)
                            slot.Body(col);
                        else
                        {
                            col.Item().PaddingTop(M.RowGap).Text("NAME / STAMP")
                                .FontSize(M.SignatureHintFont).FontColor(T.Muted);
                            col.Item().Text("DATE _______________")
                                .FontSize(M.SignatureHintFont).FontColor(T.Muted);
                        }
                    });
                    break;

                default:
                    container.Border(M.CardBorderWeight).BorderColor(T.Border)
                        .Padding(M.CardPad).Column(col =>
                    {
                        col.Item().Text(slot.Title.ToUpperInvariant())
                            .FontSize(M.SignatureFont).Bold().FontColor(T.Accent);
                        if (slot.Body != null)
                            slot.Body(col);
                        else
                        {
                            col.Item().PaddingTop(M.SignatureHeight)
                                .BorderBottom(M.SignatureRuleWeight).BorderColor(T.Border)
                                .Text(" ").FontSize(M.SignatureHintFont);
                            col.Item().PaddingTop(M.SignatureRuleGap).Text("Name / Date")
                                .FontSize(M.SignatureHintFont).FontColor(T.Muted);
                        }
                    });
                    break;
            }
        }
    }
}
