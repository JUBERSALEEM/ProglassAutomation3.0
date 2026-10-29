using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ProGlassAutomation.Customization;

/// <summary>
/// Shows the whole value in a themed bubble when a register cell could not.
///
/// A cell trims a long value with an ellipsis so one wide name cannot push every
/// other column off the page. Trimming is a silent loss: the operator sees
/// "Al Mansoori Contracting &amp; Fit…" and has no way to read the rest. WPF will
/// not say whether a TextBlock trimmed, so this compares the measured width of
/// the text against the width the block was given.
///
/// The bubble is the theme's own <c>ListBubble</c> style, not a bare string: a
/// string tooltip is drawn by the system and ignores the theme, which on a dark
/// package puts a pale grey box over a dark register. If a package ever stops
/// providing the style, this falls back to the string rather than showing
/// nothing.
///
/// It is evaluated twice, so the bubble is ready before the pointer arrives:
/// when the block settles its size (the usual case — a register lays out long
/// before anyone hovers it), and again on mouse-enter, which covers a value that
/// changed without changing the block's width.
/// </summary>
public static class TruncatedTextToolTip
{
    /// <summary>The style every register bubble uses; defined by each theme package.</summary>
    private const string BubbleStyleKey = "ListBubble";

    /// <summary>How long the full value stays on screen; the default is too short to read.</summary>
    private const int ShowDurationMilliseconds = 20000;

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled",
            typeof(bool),
            typeof(TruncatedTextToolTip),
            new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject element) =>
        (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) =>
        element.SetValue(EnabledProperty, value);

    /// <summary>
    /// True when the text is wider than the space it was given. Separated from the
    /// measuring so the rule can be checked without a window.
    /// </summary>
    public static bool IsClipped(string? text, double availableWidth, double measuredWidth) =>
        !string.IsNullOrWhiteSpace(text)
        && availableWidth > 0
        && measuredWidth > availableWidth + 0.5;

    private static void OnEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not TextBlock block) return;

        if (args.NewValue is true)
        {
            block.MouseEnter += OnMouseEnter;
            block.MouseLeave += OnMouseLeave;
            block.SizeChanged += OnSizeChanged;
        }
        else
        {
            block.MouseEnter -= OnMouseEnter;
            block.MouseLeave -= OnMouseLeave;
            block.SizeChanged -= OnSizeChanged;
            block.ClearValue(FrameworkElement.ToolTipProperty);
        }
    }

    private static void OnMouseEnter(object sender, MouseEventArgs args)
    {
        if (sender is TextBlock block) Evaluate(block);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (sender is TextBlock block) Evaluate(block);
    }

    private static void OnMouseLeave(object sender, MouseEventArgs args)
    {
        if (sender is TextBlock block) block.ClearValue(FrameworkElement.ToolTipProperty);
    }

    /// <summary>Keeps the bubble present only while the value is actually clipped.</summary>
    private static void Evaluate(TextBlock block)
    {
        var available = block.ActualWidth - Padding(block);
        if (!IsClipped(block.Text, available, Measure(block, block.Text)))
        {
            block.ClearValue(FrameworkElement.ToolTipProperty);
            return;
        }

        block.ToolTip = Build(block);
        ToolTipService.SetShowDuration(block, ShowDurationMilliseconds);
        ToolTipService.SetBetweenShowDelay(block, 0);
    }

    /// <summary>The themed bubble, or the plain string if a package has no style.</summary>
    private static object Build(TextBlock block)
    {
        if (block.TryFindResource(BubbleStyleKey) is not Style style) return block.Text;

        return new ToolTip
        {
            Style = style,
            Content = block.Text,
            PlacementTarget = block,
            StaysOpen = false,
        };
    }

    private static double Padding(TextBlock block) =>
        block.Padding.Left + block.Padding.Right;

    /// <summary>The width the text would need at this block's own font.</summary>
    private static double Measure(TextBlock block, string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var typeface = new Typeface(
            block.FontFamily,
            block.FontStyle,
            block.FontWeight,
            block.FontStretch);

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            block.FlowDirection,
            typeface,
            block.FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(block).PixelsPerDip);

        // The register never wraps, so the unwrapped width is the comparison that
        // matters: a value that fits on one line is not clipped.
        return formatted.WidthIncludingTrailingWhitespace;
    }
}
